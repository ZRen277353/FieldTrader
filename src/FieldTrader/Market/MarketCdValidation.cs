using System.IO;
using System.Text.Json;

namespace FieldTrader.Market;
public static class MarketCdValidation {
    public static async Task Run(string directory) {
        Directory.CreateDirectory(directory);var checks=new List<string>();void Check(bool ok,string title){if(!ok)throw new InvalidOperationException(title);checks.Add(title);}
        var db=new MarketDatabase(Path.Combine(directory,"simulation-"+Guid.NewGuid().ToString("N")+".sqlite"));
        foreach(var id in new[]{"m1","m2","product"})db.UpsertItem(new(id,"模拟测试-"+id,Confirmed:true));
        var now=DateTimeOffset.UtcNow;var recipe=new ExchangeRecipe("fixture-only","虚构测试联络人","product",2,[new("m1",2),new("m2",3)],10,VerificationStatus.Verified,"synthetic-recipe",2);
        db.SaveRecipe(recipe);Check(db.Recipes("m1").Single().Id==recipe.Id&&db.Recipes("product").Length==1,"配方持久化及材料/成品变价关联");
        var fees=new FeePolicy(.07m,.03m,0,true,"synthetic-fees");var policy=new TradingPolicy(10000,100,.05m);
        var inventory=new[]{new InventoryLot("owned","m1",1,80,Acquisition.MarketPurchase,SaleEligibility.Prohibited,"synthetic-owned")};
        var prices=new Dictionary<string,CurrentPrice>{{"m1",new("m1",100,10,now,"m1")},{"m2",new("m2",20,10,now,"m2")},{"product",new("product",1000,2,now,"sell")}};
        var history=MarketDatabase.Summarize([1000,1000,1000]);
        TradeDecision Eval(ExchangeRecipe? r=null,FeePolicy? f=null,TradingPolicy? p=null,PriceSummary? h=null)=>ExchangeProfitEngine.Evaluate(r??recipe,prices,inventory,h??history,f??fees,p??policy,now);
        var decision=Eval();Check(decision.Kind==DecisionKind.BUY&&decision.PurchaseCost==160&&decision.InventoryCost==80&&decision.ExpectedProfit==1460,"整套配方包含已有库存成本、缺口采购、兑换费和两类费用");
        Check(Eval(recipe with{Status=VerificationStatus.Unverified}).Kind==DecisionKind.SKIP&&Eval(recipe with{RemainingExchanges=null}).Kind==DecisionKind.SKIP,"未核实配方或兑换次数不生成采购");
        Check(Eval(f:fees with{Verified=false}).Kind==DecisionKind.SKIP&&Eval(p:policy with{Budget=10}).Kind==DecisionKind.SKIP,"未知费率和预算不足拒绝");
        Check(Eval(h:MarketDatabase.Summarize([1000])).Kind==DecisionKind.SKIP,"历史样本不足不可用网站价代替");
        prices["m1"]=prices["m1"] with{Source=PriceSource.Reference};Check(Eval().Kind==DecisionKind.SKIP,"网站参考价不能触发材料采购");prices["m1"]=prices["m1"] with{Source=PriceSource.OcrObservation,At=now.AddMinutes(-1)};
        Check(Eval().Kind==DecisionKind.SKIP,"过期报价禁止交易");prices["m1"]=prices["m1"] with{At=now};
        prices["product"]=prices["product"] with{UnitPrice=100};Check(Eval().Kind==DecisionKind.SKIP,"材料再便宜也必须成品净利润达标");prices["product"]=prices["product"] with{UnitPrice=1000};
        var special=new ResaleRule("m1","ammunition",SaleEligibility.Unknown,"");
        Check(ExchangeProfitEngine.EvaluateSpecial(special,prices["m1"],prices["m1"],history,fees,policy,now).Kind==DecisionKind.SKIP,"子弹或钥匙不默认可转售");
        var quota=new ListingQuota(511,1200,ListingQuotaManager.Week(now),now,"ocr-quota-fixture");
        Check(quota.Remaining==689&&ListingQuotaManager.Batches(11).SequenceEqual(new[]{5,5,1}),"已用511/1200得到剩余689，批量拆为1到5件");
        Check(!ListingQuotaManager.CanList(quota,6,now)&&!ListingQuotaManager.CanList(quota with{Used=1200},1,now)&&!ListingQuotaManager.CanList(quota with{WeekKey="old"},1,now),"数量超限、额度耗尽、跨周旧额度暂停上架");
        var settings=new TradingSettings(fees,policy,inventory,[]);
        var complete=await SimulationRunner.Run(db,"complete",recipe,decision,settings,quota,1000);
        Check(complete.Phase==SimulationPhase.Settled&&complete.Quota!.Used==512&&complete.Purchased["m1"]==1&&complete.Purchased["m2"]==3,"采购→兑换→上架→成交模拟闭环及购买数量账本");
        Check(db.SimulationLedger("complete").Sum(e=>e.RealizedProfit)==1550&&db.Statistics("product","sale-fill",PriceSource.ActualFill).Samples==0,"模拟净利润1550且不污染真实成交数据");
        var batchRecipe=recipe with{OutputQuantity=6};var batchDecision=ExchangeProfitEngine.Evaluate(batchRecipe,prices,inventory,history,fees,policy,now);
        var batch=await SimulationRunner.Run(db,"batches",batchRecipe,batchDecision,settings,quota,1000);
        Check(batch.Listings.Select(l=>l.Quantity).SequenceEqual(new[]{5,1})&&batch.Quota!.Used==513,"6件成品拆两次上架并消耗两次额度");
        TradeCommand Buy(string id)=>new(id,DecisionKind.BUY,"m1",1,100,0,new(new(id+"-before",now,"synthetic"),MarketPage.Purchase,"m1",[new("购买",1,new(.5,.5,.1,.1))]));
        TradeProof After(TradeCommand c,ProofOutcome outcome=ProofOutcome.Confirmed)=>new(c.Id+"-after",c.Id+"-hash",now.AddMilliseconds(1),outcome==ProofOutcome.PriceChanged?MarketPage.Purchase:MarketPage.Receipt,c.ItemId,c.Quantity,c.Money,c.Fee,outcome);
        var changed=new TradingSimulation(db,"changed");changed.Begin(recipe,decision,10000,inventory,quota);var c=Buy("price-change");await changed.ExecuteAsync(c,_=>Task.FromResult(After(c,ProofOutcome.PriceChanged)),now);
        Check(changed.Snapshot.Phase==SimulationPhase.Failed&&changed.Snapshot.Spent==0&&changed.Snapshot.Inventory.Sum(l=>l.Quantity)==1,"价格变化为正常拒绝，无扣款、无虚假购买库存");
        var unknown=new TradingSimulation(db,"unknown");unknown.Begin(recipe,decision,10000,inventory,quota);c=Buy("unknown-op");bool failed=false;
        try{await unknown.ExecuteAsync(c,_=>Task.FromException<TradeProof>(new TimeoutException("synthetic timeout")),now);}catch(TimeoutException){failed=true;}
        var restarted=new TradingSimulation(db,"unknown");Check(failed&&restarted.Snapshot.Phase==SimulationPhase.Unknown&&restarted.Snapshot.Pending?.Id==c.Id&&db.SimulationSessions().Any(s=>s.Id=="unknown"&&s.Pending?.Id==c.Id),"超时保留待核对操作，重启仍UNKNOWN");
        failed=false;try{await restarted.ExecuteAsync(Buy("retry"),_=>Task.FromResult(After(c)),now);}catch(InvalidOperationException){failed=true;}Check(failed&&restarted.Snapshot.Spent==0,"UNKNOWN禁止盲目重试");
        restarted.Reconcile(After(c),now.AddSeconds(1));Check(restarted.Snapshot.Spent==100&&restarted.Snapshot.Pending is null,"凭原操作独立结果对账，无重复提交");
        failed=false;try{await restarted.ExecuteAsync(c,_=>Task.FromResult(After(c)),now);}catch(InvalidDataException){failed=true;}Check(failed,"已执行步骤ID不能重复提交");
        var cancelled=new TradingSimulation(db,"cancelled");cancelled.Begin(recipe,decision,10000,inventory,quota);cancelled.Stop();failed=false;
        try{await cancelled.ExecuteAsync(Buy("stopped"),_=>Task.FromResult(After(c)),now);}catch(InvalidOperationException){failed=true;}Check(failed&&cancelled.Snapshot.Spent==0,"停止开关阻止后续模拟操作");
        var expiring=new TradingSimulation(db,"expiry");expiring.Begin(recipe,decision with{Kind=DecisionKind.EXCHANGE,Purchases=[]},10000,
            [new("a","m1",2,180,Acquisition.ManualVerified,SaleEligibility.Prohibited,"manual"),new("b","m2",3,60,Acquisition.ManualVerified,SaleEligibility.Prohibited,"manual")],quota);
        var exchange=new TradeCommand("exchange",DecisionKind.EXCHANGE,"product",2,10,0,new(new("exchange-before",now,"synthetic"),MarketPage.Exchange,"product",[new("兑换",1,new(.5,.5,.1,.1))]));
        await expiring.ExecuteAsync(exchange,_=>Task.FromResult(After(exchange)),now);
        expiring.VerifySaleEligibility("exchange","synthetic-sale-dialog",2);
        var sell=new TradeCommand("list",DecisionKind.SELL,"product",2,2000,60,new(new("list-before",now,"synthetic"),MarketPage.Sell,"product",[new("上架",1,new(.5,.5,.1,.1))]),140);
        await expiring.ExecuteAsync(sell,_=>Task.FromResult(After(sell) with{Page=MarketPage.Listings,ListingId="listing"}),now);
        Check(db.SimulationLedger("expiry").Sum(e=>e.RealizedProfit)==-60,"上架不确认销售收入，只记管理费支出");
        expiring.ResolveListing("listing",false,0,0,"expiry-proof",now.AddHours(12));
        Check(expiring.Snapshot.Quota!.Used==512&&expiring.Snapshot.Inventory.Sum(l=>l.Quantity)==2&&db.SimulationLedger("expiry").Sum(e=>e.RealizedProfit)==-60,"到期返还库存但不返还额度或管理费");
        var partial=new TradingSimulation(db,"missing-listing");partial.Begin(recipe,decision,10000,inventory,quota);c=Buy("wrong-proof");failed=false;
        try{await partial.ExecuteAsync(c,_=>Task.FromResult(After(c) with{ItemId="wrong"}),now);}catch(InvalidDataException){failed=true;}Check(failed&&partial.Snapshot.Phase==SimulationPhase.Unknown&&partial.Snapshot.Spent==0,"商品不符不确认交易，冻结待核对");
        var text=new[]{new VisualToken("本周上架511/1200",.99,new(.4,.1,.1,.02)),new VisualToken("物品价格发生变化，交易失败",.99,new(.4,.16,.2,.02))};
        var v=new VisionResult("f",now,"hash",2560,1440,[],text,1,1,1);var facts=TradeScreenReader.Read(new(v,MarketPage.Purchase,false,"",[],[]),[]);
        Check(facts.PriceChanged&&facts.Quota?.Remaining==689,"价格变化提示和已用额度OCR解析");
        var specialRule=new ResaleRule("m1","key",SaleEligibility.Verified,"synthetic-item-specific-permission");
        var specialDecision=ExchangeProfitEngine.EvaluateSpecial(specialRule,prices["m1"],prices["m1"] with{UnitPrice=1000},history,fees,policy,now);
        var specialRecipe=new ExchangeRecipe("special:m1","特殊路径测试","m1",1,[],0,VerificationStatus.Unverified,"",1);
        var specialResult=await SimulationRunner.Run(db,"special",specialRecipe,specialDecision,settings,quota,1000,directRule:specialRule);
        Check(specialResult.Phase==SimulationPhase.Settled&&!db.SimulationLedger("special").Any(e=>e.Kind=="EXCHANGE")&&db.SimulationLedger("special").Sum(e=>e.RealizedProfit)==800,"核验单件钥匙特殊路径独立采购出售，不伪造联络人兑换");
        var unavailable=new TradingSimulation(db,"unavailable");unavailable.Begin(recipe,decision,10000,inventory,quota);failed=false;
        try{unavailable.VerifySaleEligibility("owned","pretend-sale-dialog",1);}catch(InvalidDataException){failed=true;}Check(failed,"普通市场购买物不能凭出售弹窗标记直接转售");
        c=Buy("stale-before") with{Before=Buy("stale-template").Before with{Frame=new("old",now.AddMinutes(-1),"synthetic")}};failed=false;
        try{await unavailable.ExecuteAsync(c,_=>Task.FromResult(After(c)),now);}catch(InvalidDataException){failed=true;}Check(failed&&unavailable.Snapshot.Attempted.Count==0,"旧截图坐标在提交前拒绝，不进入账本");
        c=Buy("over-plan") with{Money=101};failed=false;
        try{await unavailable.ExecuteAsync(c,_=>Task.FromResult(After(c)),now);}catch(InvalidDataException){failed=true;}Check(failed,"采购价格超过整套计划时重新评估");
        var limited=new TradingSimulation(db,"single-limit");var oneRecipe=recipe with{RemainingExchanges=1};
        await SimulationRunner.Run(db,"single-limit",oneRecipe,decision,settings,quota,1000);failed=false;
        try{limited.Begin(oneRecipe,decision,10000,[],quota);}catch(InvalidOperationException){failed=true;}Check(failed,"同一核验凭证的兑换次数用尽后不能重复兑换");
        var interrupted=new TradingSimulation(db,"interrupted");interrupted.Begin(recipe,decision,10000,inventory,quota);c=Buy("during-stop");failed=false;
        try{await interrupted.ExecuteAsync(c,_=>{interrupted.Stop();return Task.FromResult(After(c));},now);}catch(InvalidOperationException){failed=true;}
        Check(failed&&interrupted.Snapshot.Pending?.Id==c.Id&&interrupted.Snapshot.Phase==SimulationPhase.Unknown,"提交后紧急停止保留待核对凭证");
        var noFee=new TradingSimulation(db,"no-fee");noFee.Begin(recipe,decision with{Kind=DecisionKind.EXCHANGE,Purchases=[]},10000,
            [new("a","m1",2,180,Acquisition.ManualVerified,SaleEligibility.Prohibited,"manual"),new("b","m2",3,60,Acquisition.ManualVerified,SaleEligibility.Prohibited,"manual")],quota);
        var ex2=exchange with{Id="ex2",Before=exchange.Before with{Frame=new("ex2-before",now,"synthetic")}};
        await noFee.ExecuteAsync(ex2,_=>Task.FromResult(After(ex2)),now);noFee.VerifySaleEligibility("ex2","synthetic-available",2);
        var sell2=sell with{Id="fee-missing",ExpectedSaleFee=null};failed=false;
        try{await noFee.ExecuteAsync(sell2,_=>Task.FromResult(After(sell2)),now);}catch(InvalidDataException){failed=true;}Check(failed&&noFee.Snapshot.Quota!.Used==511,"缺少独立成交手续费时不能上架或扣额度");
        sell2=sell with{Id="listing-missing"};failed=false;
        try{await noFee.ExecuteAsync(sell2,_=>Task.FromResult(After(sell2) with{Page=MarketPage.Listings}),now);}catch(InvalidDataException){failed=true;}
        Check(failed&&noFee.Snapshot.Phase==SimulationPhase.Unknown&&noFee.Snapshot.Quota!.Used==511&&noFee.Snapshot.Inventory.Single(l=>l.ItemId=="product").Quantity==2,"仅出现挂单列表而无明确挂单身份，事务回滚并进入UNKNOWN");
        var rounded=Eval(recipe with{OutputQuantity=6},fees with{SaleRate=.0001m,ListingRate=.0001m});
        Check(rounded.EstimatedFees==4,"多批上架费用分别取整，避免总额取整低估成本");
        var unrelated=new InventoryLot("old-product","product",1,10000,Acquisition.ContactExchange,SaleEligibility.Verified,"unrelated-stock");
        var reserved=await SimulationRunner.Run(db,"reserved",recipe,decision,settings with{Inventory=[..inventory,unrelated]},quota,1000);
        Check(reserved.Phase==SimulationPhase.Settled&&reserved.Inventory.Single().Id=="old-product"&&db.SimulationLedger("reserved").Sum(e=>e.RealizedProfit)==1550,"只出售本次交易生成批次，不挪用或混算已有同类成品");
        await File.WriteAllTextAsync(Path.Combine(directory,"stage-cd-tests.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks,NativeInput=false,Recipes="synthetic fixtures only",RealTransactions=false,Ledger=db.SimulationLedger("complete")},TradingAnalysis.JsonOptions));
    }
    public static async Task Replay(string config,string images,string output) {
        Directory.CreateDirectory(output);var db=new MarketDatabase(Path.Combine(output,"market.sqlite"));db.ImportSeed(MarketDatabase.BundledSeedPath);
        await using var vision=new PythonVisionPipeline(config);var workflow=new MarketWorkflow(vision,db,Path.Combine(output,"evidence"),new TradingAnalysis(db,null));var results=new List<object>();
        foreach(var file in Directory.GetFiles(images,"*.png").Order()) {
            var u=await workflow.CollectAsync(new ImageFileCapture(file),default);var v=u.Perception.Vision;
            results.Add(new{Image=Path.GetFileName(file),Page=u.Perception.Page.ToString(),v.YoloMs,v.OcrMs,v.TotalMs,u.Perception.Quotes,u.Perception.Issues,u.TradeFacts,u.Decisions});
        }
        await File.WriteAllTextAsync(Path.Combine(output,"screenshots.json"),JsonSerializer.Serialize(new{Mode="HistoricalReplay",NativeInput=false,vision.StartupMs,Results=results},TradingAnalysis.JsonOptions));
    }
}
