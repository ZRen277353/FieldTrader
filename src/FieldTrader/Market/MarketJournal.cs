using System.IO;
using System.Text.Json;

namespace FieldTrader.Market;
public sealed record MarketRecord(string Id, DateTimeOffset At, string Kind, string ItemId, string FrameId,
    int Quantity, decimal UnitPrice, string? ListingId = null);

// Single writer per journal. Corruption fails closed; never silently reset accounting.
public sealed class MarketJournal {
    private readonly string path;
    private readonly object gate = new();
    private readonly List<MarketRecord> records = [];
    private readonly HashSet<string> ids = [];
    public MarketJournal(string path) {
        this.path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(this.path)!);
        if (File.Exists(this.path)) foreach (var line in File.ReadLines(this.path)) {
            var row = JsonSerializer.Deserialize<MarketRecord>(line) ?? throw new InvalidDataException("Empty journal entry");
            if (!ids.Add(row.Id)) throw new InvalidDataException("Duplicate journal ID");
            records.Add(row);
        }
    }
    public bool Append(MarketRecord record) {
        lock (gate) {
            if (string.IsNullOrWhiteSpace(record.Id) || string.IsNullOrWhiteSpace(record.FrameId) || record.Quantity < 1 || record.UnitPrice < 0)
                throw new InvalidDataException("Invalid market record");
            if (ids.Contains(record.Id)) return false;
            // Observation ID must be stable for (frame, page, item, row), not a random retry GUID.
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough)) {
                var data = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record) + "\n");
                stream.Write(data); stream.Flush(true);
            }
            ids.Add(record.Id); records.Add(record); return true;
        }
    }
    public MarketRecord[] Snapshot() { lock (gate) return records.ToArray(); }
    public (int Samples, decimal Median, decimal Low) Statistics(string item, DateTimeOffset since) {
        lock (gate) {
            var prices = records.Where(r => r.Kind == "price" && r.ItemId == item && r.At >= since).Select(r => r.UnitPrice).Order().ToArray();
            if (prices.Length == 0) return (0, 0, 0);
            int mid = prices.Length / 2;
            return (prices.Length, prices.Length % 2 == 0 ? (prices[mid - 1] + prices[mid]) / 2 : prices[mid], prices[0]);
        }
    }
}
