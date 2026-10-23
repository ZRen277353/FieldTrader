using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace FieldTrader.Market;
public static class MarketSeedValidation {
    public static void Run(string directory,Action<bool,string> check) {
        string stem=Path.Combine(directory,"seed-"+Guid.NewGuid().ToString("N"));
        var db=new MarketDatabase(stem+".sqlite");var at=DateTimeOffset.UtcNow;
        db.UpsertItem(new("manual","人工材料","用户分类",Material:true,Product:true,Special:"custom",Confirmed:true));
        db.UpsertItem(new("candidate","未确认候选",Confirmed:false));
        db.ImportHistorical(new("receipt-before-seed","manual",180,1,at,PriceSource.ActualFill,"verified-receipt"));
        db.Store(new(new("old-frame",at,"old-hash",100,100,[],[],1,1,1),MarketPage.Purchase,false,"",
            [new("人工材料",90,1,.99,"quote","purchase-unit",true)],[]),true);
        var seed=new MarketSeedSnapshot(1,"test-source","测试来源","https://example.org/items","v1",at,3,"测试数据",
            [new("one","人工材料","网站分类","none",100,"100","https://example.org/items?page=1",at),
             new("two","钥匙样本","钥匙","key",200,"200","https://example.org/items?page=1",at),
             new("three","未确认候选","网站分类","none",null,"--","https://example.org/items?page=1",at)]);
        string path=stem+".json";void Write(MarketSeedSnapshot s)=>File.WriteAllText(path,JsonSerializer.Serialize(s));Write(seed);
        var imported=db.ImportSeed(path);
        check(!imported.AlreadyImported&&imported.NewItems==1&&imported.MappedItems==3&&imported.ReferencePrices==2,"初始目录导入：新增、同名映射与无价格条目");
        var manual=db.Catalog().Single(x=>x.Id=="manual");
        check(manual.Category=="用户分类"&&manual.Material&&manual.Product&&manual.Special=="custom"&&db.Catalog().Length==3,"初始数据保留人工分类、标记与商品ID");
        check(!db.Catalog().Single(x=>x.Id=="candidate").Confirmed,"初始导入不擅自确认已有候选商品");
        check(db.Statistics("manual","verified-receipt",PriceSource.ActualFill).Mean==180&&db.Statistics("manual","purchase-unit",replay:true).Mean==90,"初始导入保留已有成交凭证与截图报价");
        check(db.Coverage(at.AddDays(-1)).Visited==0&&db.Coverage(at.AddDays(-1)).Known==2,"网站目录不增加实时巡查覆盖");
        check(db.Statistics("manual",SeedPriceContext(),PriceSource.Reference).Samples==1&&db.Statistics("manual","market-card").Samples==0
            &&db.Statistics("manual",SeedPriceContext(),PriceSource.ActualFill).Samples==0&&db.PatrolStatistics().Count==0,"初始参考价隔离OCR、成交及巡查时段统计");
        var reference=db.References("manual").Single();
        check(reference.SourceUpdatedAt is null&&reference.FetchedAt==at&&reference.ResaleEligibility=="Unknown"&&reference.RecipeStatus=="Unverified","保留读取时间，源行情时间、转售和配方状态不猜测");
        var second=new MarketDatabase(stem+".sqlite").ImportSeed(path);
        check(second.AlreadyImported&&db.Statistics("manual",SeedPriceContext(),PriceSource.Reference).Samples==1,"重启和重复导入不增加参考价格权重");
        Write(seed with{Revision="v2",Items=[..seed.Items,new("four","新增测试","测试","none",300,"300","https://example.org/items",at)]});
        check(db.ImportSeed(path).AlreadyImported&&db.Catalog().Length==3,"首次种子不会在每次启动偷偷刷新价格或扩大目录");
        Write(seed with{Items=[seed.Items[0] with{Price=999},..seed.Items.Skip(1)]});bool rejected=false;
        try{db.ImportSeed(path);}catch(InvalidDataException){rejected=true;}check(rejected,"同版本内容变更拒绝覆盖");
        Write(seed with{Source="duplicate",Items=[seed.Items[0],seed.Items[0] with{Key="different"}]});rejected=false;
        try{db.ImportSeed(path);}catch(InvalidDataException){rejected=true;}check(rejected&&db.Catalog().Length==3,"重复标准化名称整批拒绝");
        var rollbackPath=stem+"-rollback.sqlite";var rollback=new MarketDatabase(rollbackPath);
        using(var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=rollbackPath}.ToString())) {
            c.Open();using var command=c.CreateCommand();command.CommandText="CREATE TRIGGER test_failure BEFORE INSERT ON ItemCatalog WHEN NEW.Name='钥匙样本' BEGIN SELECT RAISE(ABORT,'test rollback'); END;";command.ExecuteNonQuery();
        }
        Write(seed);rejected=false;try{rollback.ImportSeed(path);}catch(SqliteException){rejected=true;}
        check(rejected&&rollback.Catalog().Length==0,"写入中途失败时商品、来源与价格整批回滚");
        using(var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=rollbackPath}.ToString())){c.Open();using var command=c.CreateCommand();command.CommandText="DROP TRIGGER test_failure; DROP TABLE SeedItemSources; DROP TABLE SeedImports; PRAGMA user_version=2;";command.ExecuteNonQuery();}
        check(new MarketDatabase(rollbackPath).ImportSeed(path).NewItems==3,"旧版本数据库升级且失败后可安全重试导入");
        var real=new MarketDatabase(stem+"-bundled.sqlite");var timer=Stopwatch.StartNew();var result=real.ImportSeed(MarketDatabase.BundledSeedPath);timer.Stop();var importMs=timer.Elapsed.TotalMilliseconds;
        var bundled=JsonSerializer.Deserialize<MarketSeedSnapshot>(File.ReadAllText(MarketDatabase.BundledSeedPath))!;
        check(result.MappedItems==bundled.Items.Length&&result.ReferencePrices==bundled.Items.Count(i=>i.Price is not null),"真实打包目录完整导入并保留无价格项");
        check(real.Catalog().All(i=>!i.Material&&!i.Product&&i.LastVisited is null)&&real.Coverage(at.AddDays(-1)).Visited==0,"真实种子不编造兑换用途或巡查结果");
        timer.Restart();var next=new MarketScheduler(real).Next(at);timer.Stop();
        check(next is not null&&real.PatrolStatistics().Count==0,"完整参考目录可进入巡查队列，网站价不产生动态买卖信号");
        File.WriteAllText(Path.Combine(directory,"seed-import-report.json"),JsonSerializer.Serialize(new{result.MappedItems,result.ReferencePrices,ImportMs=importMs,ScheduleMs=timer.Elapsed.TotalMilliseconds,Known=real.Coverage(at.AddDays(-1)).Known,Visited=0,bundled.WebsiteTotal,bundled.CoverageNote},new JsonSerializerOptions{WriteIndented=true}));
    }
    private static string SeedPriceContext()=>MarketDatabase.SeedPriceContext;
}
