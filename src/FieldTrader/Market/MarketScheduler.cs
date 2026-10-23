namespace FieldTrader.Market;
public sealed record ScheduledTarget(CatalogItem Item, double Priority, bool Overdue, string Reason);
public sealed class MarketScheduler(MarketDatabase database) {
    public TimeSpan MaximumAge { get; init; } = TimeSpan.FromHours(6);
    public TimeSpan MinimumInterval { get; init; } = TimeSpan.FromMinutes(5);
    public ScheduledTarget? Next(DateTimeOffset now, IReadOnlyDictionary<string,double>? recipeBoost = null) {
        var history=database.History().ToDictionary(h=>h.ItemId);
        var statistics=database.PatrolStatistics();
        var candidates=new List<ScheduledTarget>();
        foreach(var item in database.Catalog().Where(i=>i.Confirmed)) {
            var h=history[item.Id];
            if(h.LastAttempt is {} attempt && now-attempt<TimeSpan.FromSeconds(Math.Min(900,30*Math.Pow(2,Math.Min(h.Failures,5)))))continue;
            if(h.LastSuccess is {} recent && now-recent<MinimumInterval)continue;
            var age=h.LastSuccess is {} last ? now-last:TimeSpan.MaxValue;
            bool overdue=age>=MaximumAge;
            var data=statistics.GetValueOrDefault(item.Id);
            var stats=data.All??MarketDatabase.Summarize([]);
            double volatility=stats.Samples>=5 && stats.Median>0 ? (double)((stats.P75-stats.P25)/stats.Median):0;
            var local=now.ToOffset(TimeSpan.FromHours(8));
            var hour=data.Hourly?.FirstOrDefault(x=>x.Weekday==(int)local.DayOfWeek&&x.Hour==local.Hour);
            double timeBoost=0;
            if(hour is { Prices.Samples: >=5 } && stats.Samples>=10 && stats.Median>0) {
                decimal change=(hour.Prices.Median-stats.Median)/stats.Median;
                timeBoost=(double)(item.Material ? -change : item.Product ? change : 0);
            }
            double boost=recipeBoost is not null&&recipeBoost.TryGetValue(item.Id,out var b)&&double.IsFinite(b)?Math.Clamp(b,0,10):0;
            candidates.Add(new(item,volatility+Math.Clamp(timeBoost,-1,1)+boost,overdue,overdue?"基础巡查到期":"波动/时段/配方优先级（仅巡查）"));
        }
        // Hard starvation bound first, oldest successful visit wins; others use learned priority.
        return candidates.OrderByDescending(x=>x.Overdue)
            .ThenBy(x=>x.Overdue?x.Item.LastVisited??DateTimeOffset.MinValue:DateTimeOffset.MinValue)
            .ThenByDescending(x=>x.Priority).ThenBy(x=>x.Item.Id,StringComparer.Ordinal).FirstOrDefault();
    }
}
