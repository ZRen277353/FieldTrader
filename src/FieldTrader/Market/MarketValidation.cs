using System.IO;
namespace FieldTrader.Market;
public static class MarketValidation {
    public static async Task Run() {
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); }
        var now = DateTimeOffset.UtcNow;
        PageEvidence Evidence(string id, MarketPage page, int age) => new(new(id, now.AddSeconds(-age), "fixture.png"), page, "item", [new("buy", 1, new(.1, .1, .1, .1))]);
        var before = Evidence("before", MarketPage.Purchase, 2);
        var after = Evidence("after", MarketPage.Receipt, 1);
        var buy = new TradeStep("buy-1", "item", MarketPage.Purchase, MarketPage.Receipt, "buy", 2, 50, false);
        var executor = new MarketExecutor();
        Check((await executor.SimulateAsync(buy, before, after, 100, null, now, default)).State == TradeState.Simulated, "buy simulation");
        Check((await executor.SimulateAsync(buy, before, after, 100, null, now, default)).State == TradeState.Rejected && executor.ReservedBudget == 50, "duplicate cannot spend twice");
        Check((await executor.SimulateAsync(buy with { Id = "over", Cost = 51 }, before, after, 100, null, now, default)).State == TradeState.Rejected, "aggregate budget");
        Check((await executor.SimulateAsync(buy with { Id = "changed", PriceChangedRejected = true }, before, after, 100, null, now, default)).State == TradeState.Rejected && executor.ReservedBudget == 50, "price rejection is normal and uncharged");
        var unknown = new MarketExecutor();
        Check((await unknown.SimulateAsync(buy, before with { Page = MarketPage.Unknown }, after, 100, null, now, default)).State == TradeState.Unknown, "unknown page");
        Check((await unknown.SimulateAsync(buy with { Id = "retry" }, before, after, 100, null, now, default)).State == TradeState.Unknown, "unknown freezes retries");
        var stale = new MarketExecutor();
        Check((await stale.SimulateAsync(buy, Evidence("stale", MarketPage.Purchase, 10), after, 100, null, now, default)).State == TradeState.Unknown, "stale evidence");
        var stopped = new MarketExecutor(); stopped.Stop();
        Check((await stopped.SimulateAsync(buy, before, after, 100, null, now, default)).State == TradeState.Stopped, "stop");
        var listing = buy with { Listing = true, Quantity = 6 };
        Check((await new MarketExecutor().SimulateAsync(listing, before, after, 100, 600, now, default)).State == TradeState.Rejected, "max five");
        Check((await new MarketExecutor().SimulateAsync(listing with { Quantity = 1 }, before, after, 100, null, now, default)).State == TradeState.Rejected, "unknown quota");
        Check(new Economics(100, 120, .1m, 2, 5).ConservativeProfit() < 0, "fees and reserve reject headline profit");
        var detection = new Detection("different-frame", MarketPage.Purchase, "item", before.Targets);
        Check(EvidenceFusion.Fuse(before.Frame, detection, detection).Page == MarketPage.Unknown, "cross frame fusion");
        var root = Path.Combine(Path.GetTempPath(), "FieldTraderMarket-" + Guid.NewGuid());
        try {
            var path = Path.Combine(root, "journal.jsonl"); var journal = new MarketJournal(path);
            var row = new MarketRecord("frame:row:item", now, "price", "item", "frame", 1, 100);
            Check(journal.Append(row) && !journal.Append(row), "dedup");
            var reopened = new MarketJournal(path);
            Check(!reopened.Append(row) && reopened.Statistics("item", now.AddMinutes(-1)).Samples == 1, "restart dedup");
            reopened.Append(row with { Id = "listing", Kind = "listed", ListingId = "L1" });
            Check(!reopened.Snapshot().Any(r => r.Kind == "sold"), "listing is not sale");
        } finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
