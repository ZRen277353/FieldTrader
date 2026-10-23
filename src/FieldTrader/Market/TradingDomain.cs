namespace FieldTrader.Market;

public enum DecisionKind { BUY, EXCHANGE, SELL, SKIP }
public enum VerificationStatus { Unverified, Verified, Disabled }
public enum Acquisition { MarketPurchase, ContactExchange, ManualVerified }
public enum SaleEligibility { Unknown, Prohibited, Verified }
public sealed record RecipeMaterial(string ItemId,int Quantity);
public sealed record ExchangeRecipe(string Id,string Contact,string ProductId,int OutputQuantity,RecipeMaterial[] Materials,
    decimal ExtraFee,VerificationStatus Status,string EvidenceId,int? RemainingExchanges,string? RefreshInformation=null);
public sealed record ResaleRule(string ItemId,string Category,SaleEligibility Eligibility,string EvidenceId);
public sealed record InventoryLot(string Id,string ItemId,int Quantity,decimal TotalCost,Acquisition Source,SaleEligibility Eligibility,string EvidenceId);
public sealed record CurrentPrice(string ItemId,decimal UnitPrice,int? AvailableQuantity,DateTimeOffset At,string FrameId,
    PriceSource Source=PriceSource.OcrObservation,bool Replay=false);
public sealed record FeePolicy(decimal SaleRate,decimal ListingRate,decimal ListingFixed,bool Verified,string EvidenceId) {
    public bool Valid=>Verified&&!string.IsNullOrWhiteSpace(EvidenceId)&&SaleRate>=0&&ListingRate>=0&&SaleRate+ListingRate<1&&ListingFixed>=0;
    public decimal SaleFee(decimal gross)=>decimal.Ceiling(gross*SaleRate);
    public decimal ListingFee(decimal gross,int batches)=>decimal.Ceiling(gross*ListingRate)+ListingFixed*batches;
}
public sealed record TradingPolicy(decimal Budget,decimal MinimumProfit=1000,decimal MinimumRoi=.05m,decimal PriceDiscount=.05m,
    decimal RiskReserve=0,int MinimumSamples=3,int PriceMaxAgeSeconds=30);
public sealed record Procurement(string ItemId,int Quantity,decimal UnitPrice);
public sealed record TradeDecision(DecisionKind Kind,string RecipeId,string ProductId,int Quantity,Procurement[] Purchases,
    decimal InventoryCost,decimal PurchaseCost,decimal ConservativeUnitSale,decimal EstimatedFees,decimal ExpectedProfit,
    decimal Roi,string Reason,DateTimeOffset At);

public static class ExchangeProfitEngine {
    public static TradeDecision Evaluate(ExchangeRecipe recipe,IReadOnlyDictionary<string,CurrentPrice> prices,
        IReadOnlyList<InventoryLot> inventory,PriceSummary saleHistory,FeePolicy fees,TradingPolicy policy,DateTimeOffset now,bool simulation=false) {
        TradeDecision Skip(string reason)=>new(DecisionKind.SKIP,recipe.Id,recipe.ProductId,recipe.OutputQuantity,[],0,0,0,0,0,0,reason,now);
        if(policy.Budget<0||policy.MinimumProfit<0||policy.MinimumRoi<0||policy.RiskReserve<0||policy.MinimumSamples<1||policy.PriceDiscount<0||policy.PriceDiscount>=1||policy.PriceMaxAgeSeconds<=0)return Skip("风控配置无效");
        if(recipe.Status!=VerificationStatus.Verified||string.IsNullOrWhiteSpace(recipe.EvidenceId)||recipe.RemainingExchanges is null or <=0)return Skip("配方或剩余兑换次数未验证");
        if(recipe.OutputQuantity<=0||recipe.ExtraFee<0||recipe.Materials.Length==0||recipe.Materials.Any(m=>m.Quantity<=0)||recipe.Materials.Select(m=>m.ItemId).Distinct().Count()!=recipe.Materials.Length)return Skip("配方数量或费用无效");
        if(!fees.Valid)return Skip("手续费和管理费尚未验证");
        bool Fresh(CurrentPrice p)=>p.UnitPrice>0&&p.Source==PriceSource.OcrObservation&&(!p.Replay||simulation)&&!string.IsNullOrWhiteSpace(p.FrameId)&&now>=p.At&&now-p.At<=TimeSpan.FromSeconds(policy.PriceMaxAgeSeconds);
        if(!prices.TryGetValue(recipe.ProductId,out var product)||!Fresh(product))return Skip("缺少新鲜的成品游戏售价");
        if(saleHistory.Samples<policy.MinimumSamples||saleHistory.P25<=0)return Skip("真实观察/成交历史样本不足；网站参考价不能替代");
        decimal ownedCost=0,purchaseCost=0;var purchases=new List<Procurement>();
        foreach(var material in recipe.Materials) {
            int needed=material.Quantity;
            foreach(var lot in inventory.Where(l=>l.ItemId==material.ItemId&&l.Quantity>0).OrderBy(l=>l.Id,StringComparer.Ordinal)) {
                if(lot.TotalCost<0||string.IsNullOrWhiteSpace(lot.EvidenceId))return Skip("库存成本或获取凭证未知");
                int take=Math.Min(needed,lot.Quantity);ownedCost+=lot.TotalCost*take/lot.Quantity;needed-=take;if(needed==0)break;
            }
            if(needed==0)continue;
            if(!prices.TryGetValue(material.ItemId,out var price)||!Fresh(price)||price.AvailableQuantity is null||price.AvailableQuantity<needed)return Skip("材料价格、数量或时效证据不足："+material.ItemId);
            purchases.Add(new(material.ItemId,needed,price.UnitPrice));purchaseCost+=price.UnitPrice*needed;
        }
        decimal sale=decimal.Floor(Math.Min(product.UnitPrice,saleHistory.P25)*(1-policy.PriceDiscount));
        decimal gross=sale*recipe.OutputQuantity;
        var batches=ListingQuotaManager.Batches(recipe.OutputQuantity);
        decimal listingCharges=batches.Sum(q=>fees.ListingFee(sale*q,1));
        decimal charges=batches.Sum(q=>fees.SaleFee(sale*q))+listingCharges;
        decimal totalCost=ownedCost+purchaseCost+recipe.ExtraFee;
        decimal profit=gross-charges-totalCost-policy.RiskReserve;
        decimal roi=totalCost>0?profit/totalCost:0;
        if(purchaseCost+recipe.ExtraFee+listingCharges>policy.Budget)return Skip("预算不足（含管理费与兑换费）");
        var kind=profit>=policy.MinimumProfit&&roi>=policy.MinimumRoi?(purchases.Count>0?DecisionKind.BUY:DecisionKind.EXCHANGE):DecisionKind.SKIP;
        return new(kind,recipe.Id,recipe.ProductId,recipe.OutputQuantity,purchases.ToArray(),ownedCost,purchaseCost,sale,charges,profit,roi,
            kind==DecisionKind.SKIP?"整套配方净利润或收益率不足":"整套配方已计入库存成本、采购成本、两类费用及保守分位售价",now);
    }
    public static TradeDecision EvaluateSpecial(ResaleRule rule,CurrentPrice buy,CurrentPrice sell,PriceSummary history,
        FeePolicy fees,TradingPolicy policy,DateTimeOffset now,bool simulation=false) {
        // Reuse whole-cost evaluation only after per-item eligibility has been explicitly verified.
        var recipe=new ExchangeRecipe("special:"+rule.ItemId,"特殊商品独立路径",rule.ItemId,1,[new("purchase:"+rule.ItemId,1)],0,
            rule.Category is "ammunition" or "key"&&rule.Eligibility==SaleEligibility.Verified&&buy.ItemId==rule.ItemId&&sell.ItemId==rule.ItemId?VerificationStatus.Verified:VerificationStatus.Unverified,rule.EvidenceId,1);
        var decision=Evaluate(recipe,new Dictionary<string,CurrentPrice>{{recipe.ProductId,sell},{"purchase:"+rule.ItemId,buy with{ItemId="purchase:"+rule.ItemId}}},[],history,fees,policy,now,simulation);
        return decision with{Purchases=decision.Purchases.Select(p=>p with{ItemId=rule.ItemId}).ToArray(),Reason="单品特殊路径："+decision.Reason};
    }
}

public sealed record ListingQuota(int Used,int Limit,string WeekKey,DateTimeOffset At,string EvidenceId) {
    public int Remaining=>Math.Max(0,Limit-Used);
    public bool Valid=>Used>=0&&Used<=Limit&&Limit is 600 or 1200&&!string.IsNullOrWhiteSpace(WeekKey)&&!string.IsNullOrWhiteSpace(EvidenceId);
}
public static class ListingQuotaManager {
    public static string Week(DateTimeOffset at) {var local=at.ToOffset(TimeSpan.FromHours(8));return $"{System.Globalization.ISOWeek.GetYear(local.DateTime)}-{System.Globalization.ISOWeek.GetWeekOfYear(local.DateTime):00}";}
    public static int[] Batches(int quantity) {if(quantity<=0)throw new ArgumentOutOfRangeException(nameof(quantity));var list=new List<int>();while(quantity>0){int batch=Math.Min(5,quantity);list.Add(batch);quantity-=batch;}return list.ToArray();}
    public static bool CanList(ListingQuota? quota,int quantity,DateTimeOffset now)=>quantity is >=1 and <=5&&quota is {Valid:true,Remaining:>0}
        &&quota.WeekKey==Week(now)&&now>=quota.At&&now-quota.At<=TimeSpan.FromMinutes(5);
}
