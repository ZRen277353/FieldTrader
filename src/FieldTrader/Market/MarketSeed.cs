using System.IO;
using System.Text.Json;

namespace FieldTrader.Market;

public sealed record MarketSeedItem(string Key, string Name, string Category, string Special, decimal? Price,
    string RawPrice, string PageUrl, DateTimeOffset FetchedAt, DateTimeOffset? SourceUpdatedAt = null);
public sealed record MarketSeedSnapshot(int Format, string Source, string SourceName, string SourceUrl,
    string Revision, DateTimeOffset FetchedAt, int WebsiteTotal, string CoverageNote, MarketSeedItem[] Items);
public sealed record SeedImportResult(bool AlreadyImported, int NewItems, int MappedItems, int ReferencePrices, string Revision);
public sealed record SeedReference(string Source, string Revision, string Name, string Category, decimal? Price,
    string Url, DateTimeOffset FetchedAt, DateTimeOffset? SourceUpdatedAt, string ResaleEligibility, string RecipeStatus);

public sealed partial class MarketDatabase {
    public const string SeedPriceContext = "website-reference";
    public static string BundledSeedPath => Path.Combine(AppContext.BaseDirectory,"MarketSeed","aqtwwx.json");

    // Offline first-run bootstrap. Each source is applied once; later snapshots require a future explicit update flow.
    // Names are eligible for patrol, never evidence of resale eligibility, inventory or verified exchange recipes.
    public SeedImportResult ImportSeed(string path) {
        string json=File.ReadAllText(path); string fileHash=Hash(json);
        var seed=JsonSerializer.Deserialize<MarketSeedSnapshot>(json) ?? throw new InvalidDataException("初始目录为空");
        bool Url(string? value)=>Uri.TryCreate(value,UriKind.Absolute,out var u)&&u.Scheme=="https";
        if(seed.Format!=1||string.IsNullOrWhiteSpace(seed.Source)||string.IsNullOrWhiteSpace(seed.Revision)||!Url(seed.SourceUrl)
            ||seed.Items is null||seed.Items.Length==0||seed.Items.Length>20000||seed.FetchedAt==default)
            throw new InvalidDataException("初始目录格式或来源无效");
        var names=new HashSet<string>(StringComparer.Ordinal);var keys=new HashSet<string>(StringComparer.Ordinal);
        foreach(var i in seed.Items) {
            if(string.IsNullOrWhiteSpace(i.Key)||!keys.Add(i.Key)||string.IsNullOrWhiteSpace(i.Name)||i.Name.Length>150
                ||!names.Add(Geometry.Normalize(i.Name))||string.IsNullOrWhiteSpace(i.Category)||!Url(i.PageUrl)
                ||i.FetchedAt==default||i.FetchedAt>seed.FetchedAt||i.SourceUpdatedAt>i.FetchedAt
                ||i.Special is not ("none" or "ammunition" or "key")||i.RawPrice is null
                ||i.Price is {} p&&(p<=0||p>1_000_000_000_000m||p!=decimal.Truncate(p)))
                throw new InvalidDataException("初始目录存在重复、无效价格或不完整来源："+i.Name);
        }
        using var db=Open();using var tx=db.BeginTransaction();
        using var existing=Command(db,"SELECT Revision,FileHash,ItemCount,PriceCount FROM SeedImports WHERE Source=$source",("$source",seed.Source));existing.Transaction=tx;
        using(var r=existing.ExecuteReader()) {
            if(r.Read()) {
                if(r.GetString(0)==seed.Revision&&r.GetString(1)!=fileHash)throw new InvalidDataException("相同初始版本的内容已改变，拒绝覆盖");
                return new(true,0,r.GetInt32(2),r.GetInt32(3),r.GetString(0));
            }
        }
        using var batch=Command(db,"INSERT INTO SeedImports VALUES($source,$revision,$hash,$fetched,$at,$count,0,$url)",
            ("$source",seed.Source),("$revision",seed.Revision),("$hash",fileHash),("$fetched",seed.FetchedAt.ToString("O")),
            ("$at",DateTimeOffset.UtcNow.ToString("O")),("$count",seed.Items.Length),("$url",seed.SourceUrl));batch.Transaction=tx;batch.ExecuteNonQuery();
        int added=0,prices=0;
        foreach(var i in seed.Items) {
            using var lookup=Command(db,"SELECT Id FROM ItemCatalog WHERE NormalizedName=$name",("$name",Geometry.Normalize(i.Name)));lookup.Transaction=tx;
            string? id=lookup.ExecuteScalar() as string;
            if(id is null) {
                id="seed-"+Hash(seed.Source+":"+i.Key)[..24];
                using var add=Command(db,"INSERT INTO ItemCatalog(Id,Name,NormalizedName,Category,Material,Product,Special,Confirmed) VALUES($id,$name,$norm,$cat,0,0,$special,1)",
                    ("$id",id),("$name",i.Name),("$norm",Geometry.Normalize(i.Name)),("$cat",i.Category.Split(" / ")[0]),("$special",i.Special));add.Transaction=tx;added+=add.ExecuteNonQuery();
            }
            // Existing records and manual flags are untouched, including candidate confirmation state and last patrol time.
            using var provenance=Command(db,"""
                INSERT INTO SeedItemSources(Source,ExternalKey,ItemId,SourceName,SourceCategory,SourceUrl,FetchedAt,SourceUpdatedAt,ReferencePrice,RawPrice)
                VALUES($source,$key,$id,$name,$cat,$url,$at,$updated,$price,$raw)
                """,("$source",seed.Source),("$key",i.Key),("$id",id),("$name",i.Name),("$cat",i.Category),("$url",i.PageUrl),
                ("$at",i.FetchedAt.ToString("O")),("$updated",i.SourceUpdatedAt?.ToString("O")),("$price",i.Price is {} price?(long)price:null),("$raw",i.RawPrice));
            provenance.Transaction=tx;provenance.ExecuteNonQuery();
            if(i.Price is {} value) {
                var at=i.SourceUpdatedAt??i.FetchedAt;var local=at.ToOffset(TimeSpan.FromHours(8));var evidence=Hash("seed:"+seed.Source+":"+i.Key);
                using var insert=Command(db,"""
                    INSERT INTO PriceObservations(ItemId,Price,Quantity,ObservedAt,Confidence,FrameId,ContentHash,RegionKey,Context,Source,Replay,Weekday,Hour,DedupKey,DetectionConfidence)
                    VALUES($id,$price,NULL,$at,0,$ev,$ev,'seed','website-reference','Reference',0,$day,$hour,$ev,0)
                    """,("$id",id),("$price",(long)value),("$at",at.ToUniversalTime().ToString("O")),("$ev",evidence),("$day",(int)local.DayOfWeek),("$hour",local.Hour));
                insert.Transaction=tx;prices+=insert.ExecuteNonQuery();
            }
        }
        using var update=Command(db,"UPDATE SeedImports SET PriceCount=$prices WHERE Source=$source",("$prices",prices),("$source",seed.Source));update.Transaction=tx;update.ExecuteNonQuery();
        tx.Commit();return new(false,added,seed.Items.Length,prices,seed.Revision);
    }
    public SeedReference[] References(string itemId) {
        using var db=Open();using var c=Command(db,"""
            SELECT s.Source,b.Revision,s.SourceName,s.SourceCategory,s.ReferencePrice,s.SourceUrl,s.FetchedAt,s.SourceUpdatedAt,s.ResaleEligibility,s.RecipeStatus
            FROM SeedItemSources s JOIN SeedImports b ON s.Source=b.Source WHERE s.ItemId=$id
            """,("$id",itemId));using var r=c.ExecuteReader();var list=new List<SeedReference>();
        while(r.Read())list.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.IsDBNull(4)?null:r.GetInt64(4),r.GetString(5),DateTimeOffset.Parse(r.GetString(6)),r.IsDBNull(7)?null:DateTimeOffset.Parse(r.GetString(7)),r.GetString(8),r.GetString(9)));
        return list.ToArray();
    }
    // A single read replaces two connections per item once the catalog contains thousands of items.
    public Dictionary<string,(PriceSummary All,HourlyPriceSummary[] Hourly)> PatrolStatistics() {
        using var db=Open();using var c=Command(db,"SELECT ItemId,Price,Weekday,Hour FROM PriceObservations WHERE Context='market-card' AND Source='OcrObservation' AND Replay=0");
        using var r=c.ExecuteReader();var rows=new List<(string Id,decimal Price,int Day,int Hour)>();
        while(r.Read())rows.Add((r.GetString(0),r.GetInt64(1),r.GetInt32(2),r.GetInt32(3)));
        return rows.GroupBy(x=>x.Id).ToDictionary(g=>g.Key,g=>(Summarize(g.Select(x=>x.Price)),g.GroupBy(x=>(x.Day,x.Hour)).Select(h=>new HourlyPriceSummary(h.Key.Day,h.Key.Hour,Summarize(h.Select(x=>x.Price)))).ToArray()));
    }
}
