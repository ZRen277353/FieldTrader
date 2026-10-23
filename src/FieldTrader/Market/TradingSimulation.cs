using System.IO;

namespace FieldTrader.Market;
public enum SimulationPhase { Ready, Buying, Exchanging, Selling, Listed, Settled, Expired, Failed, Unknown, Stopped }
public enum ProofOutcome { Confirmed, PriceChanged, Failed, Unknown }
public sealed record TradeCommand(string Id,DecisionKind Kind,string ItemId,int Quantity,decimal Money,decimal Fee,PageEvidence Before,decimal? ExpectedSaleFee=null);
public sealed record TradeProof(string FrameId,string ContentHash,DateTimeOffset At,MarketPage Page,string ItemId,int Quantity,
    decimal Money,decimal Fee,ProofOutcome Outcome,string? ListingId=null);
public sealed record SimListing(string Id,string ItemId,int Quantity,decimal Cost,decimal Gross,decimal SaleFee,decimal ListingFee,string Status);
public sealed class SimulationState {
    public string Id {get;set;}="";
    public SimulationPhase Phase {get;set;}
    public bool StopRequested {get;set;}
    public decimal Budget {get;set;}
    public decimal Spent {get;set;}
    public ExchangeRecipe? Recipe {get;set;}
    public TradeDecision? Decision {get;set;}
    public TradeCommand? Pending {get;set;}
    public ListingQuota? Quota {get;set;}
    public List<InventoryLot> Inventory {get;set;}=[];
    public List<SimListing> Listings {get;set;}=[];
    public HashSet<string> SaleLotIds {get;set;}=[];
    public HashSet<string> Attempted {get;set;}=[];
    public HashSet<string> Evidence {get;set;}=[];
    public Dictionary<string,int> Purchased {get;set;}=[];
    public Dictionary<string,int> ExchangesUsed {get;set;}=[];
    public ResaleRule? DirectRule {get;set;}
    public string Reason {get;set;}="模拟账本，与游戏库存/资金隔离";
}
public static class MarketOperationGate {
    public static SemaphoreSlim Shared {get;}=new(1,1);
    public static SemaphoreSlim TradeSession {get;}=new(1,1);
}

// Only SimulatedMouseExecutor is accepted. There is deliberately no native trade submit implementation.
public sealed class TradingSimulation(MarketDatabase database,string session) {
    public SimulationState Snapshot=>database.Simulation(session);
    public void Begin(ExchangeRecipe recipe,TradeDecision decision,decimal budget,InventoryLot[] owned,ListingQuota? quota)=>BeginCore(recipe,decision,budget,owned,quota,null);
    private void BeginCore(ExchangeRecipe recipe,TradeDecision decision,decimal budget,InventoryLot[] owned,ListingQuota? quota,ResaleRule? direct) {
        database.MutateSimulation(session,(s,_)=>{
            if(s.Pending is not null||s.Phase is SimulationPhase.Buying or SimulationPhase.Exchanging or SimulationPhase.Selling or SimulationPhase.Listed or SimulationPhase.Unknown)throw new InvalidOperationException("已有交易或 UNKNOWN 未核对");
            if(s.StopRequested)throw new InvalidOperationException("停止后请创建新的隔离模拟会话");
            if(decision.Kind is not(DecisionKind.BUY or DecisionKind.EXCHANGE)||decision.RecipeId!=recipe.Id||decision.ProductId!=recipe.ProductId||decision.Quantity!=recipe.OutputQuantity
                ||recipe.Status!=VerificationStatus.Verified||recipe.RemainingExchanges is null or <=0||budget<=0||decision.ExpectedProfit<0
                ||direct is null&&recipe.Materials.Length==0)throw new InvalidDataException("缺少已验证且获准的整套配方决策");
            if(owned.Any(l=>l.Quantity<=0||l.TotalCost<0||string.IsNullOrWhiteSpace(l.EvidenceId))||owned.Select(l=>l.Id).Distinct().Count()!=owned.Length)throw new InvalidDataException("库存凭证无效或批次ID重复");
            if(s.Inventory.Count==0&&s.Attempted.Count==0)s.Inventory=owned.ToList();
            else if(owned.Length>0)throw new InvalidOperationException("已有账本禁止重新注入初始库存");
            if(s.ExchangesUsed.GetValueOrDefault(recipe.Id+":"+recipe.EvidenceId)>=recipe.RemainingExchanges)throw new InvalidOperationException("此凭证的兑换次数已用尽，需要重新核验刷新");
            s.DirectRule=direct;s.Recipe=recipe;s.Decision=decision;s.Budget=budget;s.Quota=quota;s.Purchased.Clear();s.SaleLotIds.Clear();s.Phase=decision.Kind==DecisionKind.BUY?SimulationPhase.Buying:SimulationPhase.Exchanging;
        });
    }
    public void BeginSpecial(ResaleRule rule,TradeDecision decision,decimal budget,ListingQuota quota) {
        if(rule.Category is not("ammunition" or "key")||rule.Eligibility!=SaleEligibility.Verified||string.IsNullOrWhiteSpace(rule.EvidenceId)
            ||decision.Kind!=DecisionKind.BUY||decision.RecipeId!="special:"+rule.ItemId||decision.ProductId!=rule.ItemId||decision.Purchases.Length!=1
            ||decision.Purchases[0].ItemId!=rule.ItemId||decision.Purchases[0].Quantity!=decision.Quantity)throw new InvalidDataException("特殊商品逐项资格或采购计划未核验");
        BeginCore(new(decision.RecipeId,"特殊商品直接转售",rule.ItemId,decision.Quantity,[],0,VerificationStatus.Verified,rule.EvidenceId,1),decision,budget,[],quota,rule);
    }
    public void Stop()=>database.MutateSimulation(session,(s,_)=>{s.StopRequested=true;s.Phase=s.Pending is null?SimulationPhase.Stopped:SimulationPhase.Unknown;s.Reason="已停止；待核对操作不释放或重试";});
    public void UpdateQuota(ListingQuota quota) {
        if(!quota.Valid)throw new InvalidDataException("额度无效");
        database.MutateSimulation(session,(s,_)=>{
            if(s.Pending is not null)throw new InvalidOperationException("提交中禁止重置额度");
            if(s.Quota is {} old&&(quota.At<old.At||quota.WeekKey==old.WeekKey&&quota.Used<old.Used))throw new InvalidDataException("旧截图或额度倒退，等待核对");
            s.Quota=quota;
        });
    }
    public async Task ExecuteAsync(TradeCommand command,Func<CancellationToken,Task<TradeProof>> observeAfter,DateTimeOffset now,CancellationToken token=default) {
        await MarketOperationGate.Shared.WaitAsync(token);
        bool prepared=false;
        try {
            database.MutateSimulation(session,(s,_)=>{
                ValidateCommand(s,command,now);s.Attempted.Add(command.Id);s.Pending=command;s.Reason="模拟提交已记录，等待独立结果证据";
            });prepared=true;
            var before=command.Before;var target=before.Targets.Single();
            var mouse=new SimulatedMouseExecutor();
            await mouse.ExecuteAsync(new(NavigationKind.SimulatedTrade,target.Label,target.Bounds,before.Frame.Id,"simulation",before.Page,MarketPage.Receipt),
                new(before.Frame,"simulation"),token);
            var proof=await observeAfter(token).WaitAsync(TimeSpan.FromSeconds(10),token);
            ApplyProof(proof,now,false);
        }catch(Exception ex) {
            if(prepared)database.MutateSimulation(session,(s,_)=>{s.Phase=SimulationPhase.Unknown;s.Reason="UNKNOWN："+ex.Message;});
            throw;
        }finally{MarketOperationGate.Shared.Release();}
    }
    private static void ValidateCommand(SimulationState s,TradeCommand c,DateTimeOffset now) {
        if(s.StopRequested||s.Pending is not null||s.Phase is SimulationPhase.Unknown or SimulationPhase.Stopped)throw new InvalidOperationException("暂停、UNKNOWN 或待确认操作禁止重试");
        if(string.IsNullOrWhiteSpace(c.Id)||s.Attempted.Contains(c.Id)||c.Quantity<=0||c.Money<0||c.Fee<0)throw new InvalidDataException("重复操作或金额数量无效");
        var expected=c.Kind switch{DecisionKind.BUY=>MarketPage.Purchase,DecisionKind.EXCHANGE=>MarketPage.Exchange,DecisionKind.SELL=>MarketPage.Sell,_=>MarketPage.Unknown};
        string label=c.Kind switch{DecisionKind.BUY=>"购买",DecisionKind.EXCHANGE=>"兑换",_=>"上架"};
        if(c.Before.Page!=expected||expected==MarketPage.Unknown||c.Before.ItemId!=c.ItemId||now<c.Before.Frame.CapturedAt||now-c.Before.Frame.CapturedAt>TimeSpan.FromSeconds(5)
            ||c.Before.Targets.Length!=1||c.Before.Targets[0].Label!=label||!c.Before.Targets[0].Bounds.Valid||c.Before.Targets[0].Confidence<.95)throw new InvalidDataException("操作前页面、商品、按钮或时效未验证");
        if(s.Recipe is null||s.Decision is null)throw new InvalidOperationException("缺少完整决策");
        if(c.Kind==DecisionKind.BUY) {
            var p=s.Decision.Purchases.SingleOrDefault(x=>x.ItemId==c.ItemId);
            if(s.Phase!=SimulationPhase.Buying||p is null||c.Quantity+s.Purchased.GetValueOrDefault(c.ItemId)>p.Quantity||c.Money<=0||c.Money>p.UnitPrice*c.Quantity||c.Fee!=0)throw new InvalidDataException("采购超出整套配方计划");
        } else if(c.Kind==DecisionKind.EXCHANGE) {
            if(s.DirectRule is not null||s.Phase is not(SimulationPhase.Buying or SimulationPhase.Exchanging)||c.ItemId!=s.Recipe.ProductId||c.Quantity!=s.Recipe.OutputQuantity||c.Money!=s.Recipe.ExtraFee||c.Fee!=0
                ||s.Recipe.Materials.Any(m=>s.Inventory.Where(l=>l.ItemId==m.ItemId).Sum(l=>l.Quantity)<m.Quantity))throw new InvalidDataException("材料不足或兑换页面数量费用不符");
        } else {
            if(s.Phase is not(SimulationPhase.Selling or SimulationPhase.Expired)||c.ItemId!=s.Recipe.ProductId||!ListingQuotaManager.CanList(s.Quota,c.Quantity,now)
                ||s.Inventory.Where(l=>l.ItemId==c.ItemId&&l.Eligibility==SaleEligibility.Verified&&s.SaleLotIds.Contains(l.Id)).Sum(l=>l.Quantity)<c.Quantity)throw new InvalidDataException("可出售库存、实际弹窗数量或额度不足");
            if(c.Money<=0||c.Fee>=c.Money||c.Money<c.Quantity*s.Decision.ConservativeUnitSale||c.Fee>s.Decision.EstimatedFees)throw new InvalidDataException("游戏售价或费用偏离整套利润计划，需要重新评估");
            if(c.ExpectedSaleFee is null or <0||c.Money-c.Fee-c.ExpectedSaleFee<=0)throw new InvalidDataException("上架前必须核对独立成交手续费");
            int remaining=c.Quantity;decimal cost=0;
            foreach(var lot in s.Inventory.Where(l=>l.ItemId==c.ItemId&&l.Eligibility==SaleEligibility.Verified&&s.SaleLotIds.Contains(l.Id)).OrderBy(l=>l.Id,StringComparer.Ordinal)){int take=Math.Min(remaining,lot.Quantity);cost+=lot.TotalCost*take/lot.Quantity;remaining-=take;if(remaining==0)break;}
            if(c.Money-c.Fee-c.ExpectedSaleFee-cost<=0)throw new InvalidDataException("当前出售净收入无法覆盖库存成本及两类费用");
        }
        decimal outlay=c.Kind==DecisionKind.SELL?c.Fee:c.Money;
        if(s.Spent+outlay>s.Budget)throw new InvalidOperationException("模拟总支出超过预算");
    }
    public void Reconcile(TradeProof proof,DateTimeOffset now) {
        if(Snapshot.Phase!=SimulationPhase.Unknown)throw new InvalidOperationException("只有 UNKNOWN 可以人工凭证核对");
        ApplyProof(proof,now,true);
    }
    private void ApplyProof(TradeProof proof,DateTimeOffset now,bool reconciliation) {
        database.MutateSimulation(session,(s,events)=>{
            var c=s.Pending??throw new InvalidOperationException("没有待核对操作");
            if(s.StopRequested&&!reconciliation)throw new InvalidOperationException("停止发生在提交后，需要人工核对");
            if(string.IsNullOrWhiteSpace(proof.FrameId)||string.IsNullOrWhiteSpace(proof.ContentHash)||proof.FrameId==c.Before.Frame.Id||s.Evidence.Contains(proof.ContentHash)||proof.At<=c.Before.Frame.CapturedAt
                ||proof.At>now.AddSeconds(10)||(!reconciliation&&proof.At-c.Before.Frame.CapturedAt>TimeSpan.FromSeconds(15)))throw new InvalidDataException("结果证据重复、过期或时间不合法");
            if(proof.Outcome==ProofOutcome.Unknown)throw new InvalidOperationException("结果未确认");
            if(proof.ItemId!=c.ItemId)throw new InvalidDataException("结果商品不匹配");
            if(proof.Outcome is ProofOutcome.PriceChanged or ProofOutcome.Failed) {
                if(proof.Page!=c.Before.Page)throw new InvalidDataException("失败提示不属于提交页面");
                if(proof.Outcome==ProofOutcome.PriceChanged&&(c.Kind!=DecisionKind.BUY||proof.Page!=MarketPage.Purchase))throw new InvalidDataException("价格变化拒绝上下文错误");
                s.Evidence.Add(proof.ContentHash);s.Pending=null;s.Phase=SimulationPhase.Failed;s.Reason=proof.Outcome==ProofOutcome.PriceChanged?"价格变化，正常拒绝；后续巡查重新评估":"明确失败";
                events.Add(new(c.Id,proof.Outcome.ToString(),c.ItemId,c.Quantity,0,0,proof.ContentHash,proof.At));return;
            }
            var expected=c.Kind==DecisionKind.SELL?MarketPage.Listings:MarketPage.Receipt;
            if(proof.Page!=expected||proof.Quantity!=c.Quantity||proof.Money!=c.Money||proof.Fee!=c.Fee)throw new InvalidDataException("结果页面、数量、金额或费用不符");
            decimal cash=0,profit=0;
            if(c.Kind==DecisionKind.BUY) {
                cash=-c.Money;s.Spent+=c.Money;s.Purchased[c.ItemId]=s.Purchased.GetValueOrDefault(c.ItemId)+c.Quantity;
                s.Inventory.Add(new(c.Id,c.ItemId,c.Quantity,c.Money,Acquisition.MarketPurchase,SaleEligibility.Prohibited,proof.ContentHash));
                if(s.DirectRule is not null)s.SaleLotIds.Add(c.Id);
                s.Phase=s.Decision!.Purchases.All(p=>s.Purchased.GetValueOrDefault(p.ItemId)>=p.Quantity)?(s.DirectRule is null?SimulationPhase.Exchanging:SimulationPhase.Selling):SimulationPhase.Buying;
            }else if(c.Kind==DecisionKind.EXCHANGE) {
                decimal cost=c.Money;foreach(var m in s.Recipe!.Materials)cost+=Consume(s,m.ItemId,m.Quantity,false);
                s.Inventory.Add(new(c.Id,c.ItemId,c.Quantity,cost,Acquisition.ContactExchange,SaleEligibility.Unknown,proof.ContentHash));
                s.SaleLotIds.Add(c.Id);
                s.Spent+=c.Money;cash=-c.Money;s.Phase=SimulationPhase.Selling;
                string limitKey=s.Recipe.Id+":"+s.Recipe.EvidenceId;s.ExchangesUsed[limitKey]=s.ExchangesUsed.GetValueOrDefault(limitKey)+1;
                events.Add(new(c.Id+":decision","SELL",c.ItemId,c.Quantity,0,0,proof.ContentHash,proof.At));
            }else {
                if(string.IsNullOrWhiteSpace(proof.ListingId)||s.Listings.Any(l=>l.Id==proof.ListingId))throw new InvalidDataException("上架必须有独立挂单证据，不能只看额度加一");
                decimal cost=Consume(s,c.ItemId,c.Quantity,true);
                s.Listings.Add(new(proof.ListingId,c.ItemId,c.Quantity,cost,c.Money,c.ExpectedSaleFee!.Value,c.Fee,"Listed"));
                s.Quota=s.Quota! with{Used=s.Quota.Used+1};s.Spent+=c.Fee;cash=-c.Fee;profit=-c.Fee;s.Phase=SimulationPhase.Listed;
            }
            s.Evidence.Add(proof.ContentHash);s.Pending=null;s.Reason="已验证模拟结果；挂单与成交分账";
            events.Add(new(c.Id,c.Kind.ToString(),c.ItemId,c.Quantity,cash,profit,proof.ContentHash,proof.At));
        });
    }
    private static decimal Consume(SimulationState s,string item,int quantity,bool sale) {
        decimal cost=0;int remaining=quantity;
        foreach(var lot in s.Inventory.Where(l=>l.ItemId==item&&(!sale||l.Eligibility==SaleEligibility.Verified&&s.SaleLotIds.Contains(l.Id))).OrderBy(l=>l.Id,StringComparer.Ordinal).ToArray()) {
            int n=Math.Min(remaining,lot.Quantity);decimal slice=lot.TotalCost*n/lot.Quantity;cost+=slice;s.Inventory.Remove(lot);
            if(n<lot.Quantity)s.Inventory.Add(lot with{Quantity=lot.Quantity-n,TotalCost=lot.TotalCost-slice});remaining-=n;if(remaining==0)break;
        }
        if(remaining!=0)throw new InvalidOperationException("账本库存不足");return cost;
    }
    public void VerifySaleEligibility(string lotId,string evidence,int actualAvailable) {
        if(string.IsNullOrWhiteSpace(evidence)||actualAvailable<=0)throw new InvalidDataException("需要当前出售弹窗的实际可用数量和凭证");
        database.MutateSimulation(session,(s,_)=>{if(s.Pending is not null)throw new InvalidOperationException("提交中禁止改库存");var lot=s.Inventory.Single(l=>l.Id==lotId);
            bool special=s.DirectRule is {Eligibility:SaleEligibility.Verified} rule&&rule.ItemId==lot.ItemId&&!string.IsNullOrWhiteSpace(rule.EvidenceId);
            if(lot.Source==Acquisition.MarketPurchase&&!special||actualAvailable<lot.Quantity)throw new InvalidDataException("普通市场购买物禁止直接转售，或游戏数量不足");
            s.Inventory.Remove(lot);s.Inventory.Add(lot with{Eligibility=SaleEligibility.Verified,EvidenceId=evidence});});
    }
    public void ResolveListing(string listingId,bool sold,decimal gross,decimal saleFee,string evidence,DateTimeOffset at) {
        database.MutateSimulation(session,(s,events)=>{
            if(s.Pending is not null||s.Phase==SimulationPhase.Unknown||string.IsNullOrWhiteSpace(evidence)||s.Evidence.Contains(evidence))throw new InvalidOperationException("有待核对操作或重复凭证");
            var listing=s.Listings.Single(l=>l.Id==listingId&&l.Status=="Listed");
            if(sold&&(gross!=listing.Gross||saleFee<0||saleFee>=gross))throw new InvalidDataException("成交凭证金额不符");
            s.Listings.Remove(listing);s.Listings.Add(listing with{Status=sold?"Sold":"Expired",SaleFee=sold?saleFee:0});s.Evidence.Add(evidence);
            if(!sold){string returned=listing.Id+":returned";s.Inventory.Add(new(returned,listing.ItemId,listing.Quantity,listing.Cost,s.DirectRule is null?Acquisition.ContactExchange:Acquisition.MarketPurchase,SaleEligibility.Unknown,evidence));s.SaleLotIds.Add(returned);}
            s.Phase=sold?(s.Inventory.Any(l=>s.SaleLotIds.Contains(l.Id)&&l.Quantity>0)?SimulationPhase.Selling:SimulationPhase.Settled):SimulationPhase.Expired;
            events.Add(new(evidence,sold?"Sold":"Expired",listing.ItemId,listing.Quantity,sold?gross-saleFee:0,sold?gross-saleFee-listing.Cost:0,evidence,at));
            // Expiry never refunds the consumed listing count or listing management fee.
        });
    }
}
