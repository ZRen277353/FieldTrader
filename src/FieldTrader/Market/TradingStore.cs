using System.IO;
using System.Text.Json;

namespace FieldTrader.Market;
public sealed record DecisionRecord(string Id,TradeDecision Decision);
public sealed record LedgerEntry(string Id,string Kind,string ItemId,int Quantity,decimal Cash,decimal RealizedProfit,string EvidenceId,DateTimeOffset At);
public sealed partial class MarketDatabase {
    private void InitializeTrading() {
        using var db=Open();using var c=Command(db,"""
            CREATE TABLE IF NOT EXISTS ExchangeRecipes(Id TEXT PRIMARY KEY,Contact TEXT NOT NULL,ProductId TEXT NOT NULL REFERENCES ItemCatalog(Id),OutputQuantity INTEGER NOT NULL,ExtraFee TEXT NOT NULL,Status TEXT NOT NULL,EvidenceId TEXT NOT NULL,RemainingExchanges INTEGER,RefreshInformation TEXT);
            CREATE TABLE IF NOT EXISTS ExchangeRecipeMaterials(RecipeId TEXT NOT NULL REFERENCES ExchangeRecipes(Id),ItemId TEXT NOT NULL REFERENCES ItemCatalog(Id),Quantity INTEGER NOT NULL CHECK(Quantity>0),PRIMARY KEY(RecipeId,ItemId));
            CREATE INDEX IF NOT EXISTS IX_RecipeMaterialItem ON ExchangeRecipeMaterials(ItemId);
            CREATE TABLE IF NOT EXISTS TradingDecisions(Id TEXT PRIMARY KEY,At TEXT NOT NULL,RecipeId TEXT NOT NULL,Kind TEXT NOT NULL,Payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS SimulationState(Id TEXT PRIMARY KEY,Payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS SimulationLedger(SessionId TEXT NOT NULL,Id TEXT NOT NULL,Kind TEXT NOT NULL,ItemId TEXT NOT NULL,Quantity INTEGER NOT NULL,Cash TEXT NOT NULL,RealizedProfit TEXT NOT NULL,EvidenceId TEXT NOT NULL,At TEXT NOT NULL,PRIMARY KEY(SessionId,Id));
            PRAGMA user_version=4;
            """);c.ExecuteNonQuery();
    }
    public void SaveRecipe(ExchangeRecipe recipe) {
        if(string.IsNullOrWhiteSpace(recipe.Id)||string.IsNullOrWhiteSpace(recipe.Contact)||recipe.OutputQuantity<=0||recipe.ExtraFee<0||recipe.Materials.Length==0
            ||recipe.Materials.Any(m=>m.Quantity<=0||m.ItemId==recipe.ProductId)||recipe.Materials.Select(m=>m.ItemId).Distinct().Count()!=recipe.Materials.Length
            ||recipe.RemainingExchanges<0||!Enum.IsDefined(recipe.Status)||(recipe.Status==VerificationStatus.Verified&&string.IsNullOrWhiteSpace(recipe.EvidenceId)))throw new InvalidDataException("配方必须有有效商品、数量和核验凭证");
        using var db=Open();using var tx=db.BeginTransaction();
        using var c=Command(db,"""
            INSERT INTO ExchangeRecipes VALUES($id,$contact,$product,$qty,$fee,$status,$evidence,$remaining,$refresh)
            ON CONFLICT(Id) DO UPDATE SET Contact=excluded.Contact,ProductId=excluded.ProductId,OutputQuantity=excluded.OutputQuantity,ExtraFee=excluded.ExtraFee,Status=excluded.Status,EvidenceId=excluded.EvidenceId,RemainingExchanges=excluded.RemainingExchanges,RefreshInformation=excluded.RefreshInformation
            """,("$id",recipe.Id),("$contact",recipe.Contact),("$product",recipe.ProductId),("$qty",recipe.OutputQuantity),("$fee",recipe.ExtraFee.ToString(System.Globalization.CultureInfo.InvariantCulture)),("$status",recipe.Status.ToString()),("$evidence",recipe.EvidenceId),("$remaining",recipe.RemainingExchanges),("$refresh",recipe.RefreshInformation));c.Transaction=tx;c.ExecuteNonQuery();
        using var clear=Command(db,"DELETE FROM ExchangeRecipeMaterials WHERE RecipeId=$id",("$id",recipe.Id));clear.Transaction=tx;clear.ExecuteNonQuery();
        foreach(var m in recipe.Materials){using var add=Command(db,"INSERT INTO ExchangeRecipeMaterials VALUES($id,$item,$qty)",("$id",recipe.Id),("$item",m.ItemId),("$qty",m.Quantity));add.Transaction=tx;add.ExecuteNonQuery();}tx.Commit();
    }
    public ExchangeRecipe[] Recipes(string? affectedItem=null) {
        using var db=Open();using var c=Command(db,"SELECT * FROM ExchangeRecipes WHERE $item IS NULL OR ProductId=$item OR Id IN(SELECT RecipeId FROM ExchangeRecipeMaterials WHERE ItemId=$item)",("$item",affectedItem));
        var list=new List<ExchangeRecipe>();using(var r=c.ExecuteReader()){while(r.Read())list.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetInt32(3),[],decimal.Parse(r.GetString(4),System.Globalization.CultureInfo.InvariantCulture),Enum.Parse<VerificationStatus>(r.GetString(5)),r.GetString(6),r.IsDBNull(7)?null:r.GetInt32(7),r.IsDBNull(8)?null:r.GetString(8)));}
        return list.Select(recipe=>{using var m=Command(db,"SELECT ItemId,Quantity FROM ExchangeRecipeMaterials WHERE RecipeId=$id",("$id",recipe.Id));using var r=m.ExecuteReader();var materials=new List<RecipeMaterial>();while(r.Read())materials.Add(new(r.GetString(0),r.GetInt32(1)));return recipe with{Materials=materials.ToArray()};}).ToArray();
    }
    public PriceSummary ConservativeSaleHistory(string item) {
        var fills=Statistics(item,"sale-fill",PriceSource.ActualFill);return fills.Samples>=3?fills:Statistics(item,"market-card");
    }
    public void SaveDecision(TradeDecision decision) {
        using var db=Open();using var c=Command(db,"INSERT INTO TradingDecisions VALUES($id,$at,$recipe,$kind,$json)",("$id",Guid.NewGuid().ToString("N")),("$at",decision.At.ToString("O")),("$recipe",decision.RecipeId),("$kind",decision.Kind.ToString()),("$json",JsonSerializer.Serialize(decision)));c.ExecuteNonQuery();
    }
    public SimulationState Simulation(string session) {
        using var db=Open();using var c=Command(db,"SELECT Payload FROM SimulationState WHERE Id=$id",("$id",session));return c.ExecuteScalar() is string s?JsonSerializer.Deserialize<SimulationState>(s)!:new(){Id=session};
    }
    public SimulationState[] SimulationSessions() {
        using var db=Open();using var c=Command(db,"SELECT Payload FROM SimulationState ORDER BY rowid DESC");using var r=c.ExecuteReader();var states=new List<SimulationState>();
        while(r.Read())states.Add(JsonSerializer.Deserialize<SimulationState>(r.GetString(0))!);return states.ToArray();
    }
    public void MutateSimulation(string session,Action<SimulationState,List<LedgerEntry>> mutate) {
        using var db=Open();using var tx=db.BeginTransaction();using var get=Command(db,"SELECT Payload FROM SimulationState WHERE Id=$id",("$id",session));get.Transaction=tx;
        var state=get.ExecuteScalar() is string s?JsonSerializer.Deserialize<SimulationState>(s)!:new SimulationState{Id=session};var entries=new List<LedgerEntry>();mutate(state,entries);
        using var put=Command(db,"INSERT INTO SimulationState VALUES($id,$json) ON CONFLICT(Id) DO UPDATE SET Payload=excluded.Payload",("$id",session),("$json",JsonSerializer.Serialize(state)));put.Transaction=tx;put.ExecuteNonQuery();
        foreach(var e in entries){using var insert=Command(db,"INSERT INTO SimulationLedger VALUES($session,$id,$kind,$item,$qty,$cash,$profit,$evidence,$at)",("$session",session),("$id",e.Id),("$kind",e.Kind),("$item",e.ItemId),("$qty",e.Quantity),("$cash",e.Cash.ToString(System.Globalization.CultureInfo.InvariantCulture)),("$profit",e.RealizedProfit.ToString(System.Globalization.CultureInfo.InvariantCulture)),("$evidence",e.EvidenceId),("$at",e.At.ToString("O")));insert.Transaction=tx;insert.ExecuteNonQuery();}tx.Commit();
    }
    public LedgerEntry[] SimulationLedger(string session) {
        using var db=Open();using var c=Command(db,"SELECT Id,Kind,ItemId,Quantity,Cash,RealizedProfit,EvidenceId,At FROM SimulationLedger WHERE SessionId=$id ORDER BY At,Id",("$id",session));using var r=c.ExecuteReader();var list=new List<LedgerEntry>();
        while(r.Read())list.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetInt32(3),decimal.Parse(r.GetString(4),System.Globalization.CultureInfo.InvariantCulture),decimal.Parse(r.GetString(5),System.Globalization.CultureInfo.InvariantCulture),r.GetString(6),DateTimeOffset.Parse(r.GetString(7))));return list.ToArray();
    }
}
