using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace FieldTrader;

public sealed partial class ReferencePriceCatalog {
 private static readonly IReadOnlyDictionary<string,string> Aliases=new Dictionary<string,string>{
  ["体外去颤器"]="体外除颤器",
  ["摄影机"]="摄像机",
  ["金手机"]="金色手机",
  ["红外线理疗仪"]="红外线理疗灯",
  ["量子2000玩具机器人"]="量子2000",
  ["金条"]="金块",
  ["维塞尔日记"]="塞维尔日记",
  ["手持接收机"]="手持接收器",
  ["盘蛇金象"]="盘蛇金像",
  ["盛宴雕塑"]="盛宴雕像",
  ["石膏雕塑"]="石膏人头",
  ["监听音箱"]="监听音响",
  ["小金豹"]="金豹雕像",
  ["千花书签"]="干花书签"
 };
 private readonly Dictionary<string,string> prices;
 public static ReferencePriceCatalog Bundled {get;}=LoadBundled();

 private ReferencePriceCatalog(Dictionary<string,string> prices)=>this.prices=prices;

 public string FormatFor(string? itemName) {
  if(string.IsNullOrWhiteSpace(itemName))return "";
  var key=Normalize(itemName);
  if(Aliases.TryGetValue(key,out var alias))key=Normalize(alias);
  return prices.TryGetValue(key,out var price)?price:"";
 }

 public static string FormatWan(string raw) {
  var match=NumberPattern().Match(raw.Replace(",","").Replace("，",""));
  if(!match.Success||!double.TryParse(match.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)||value<=0)return "";
  return (value/10000d).ToString("0.##",CultureInfo.InvariantCulture)+"W";
 }

 private static ReferencePriceCatalog LoadBundled() {
  var result=new Dictionary<string,string>(StringComparer.Ordinal);
  try {
   var path=Path.Combine(AppContext.BaseDirectory,"PriceSeed","MerchantHelper","database.json");
   using var document=JsonDocument.Parse(File.ReadAllText(path));
   if(!document.RootElement.TryGetProperty("items",out var items)||items.ValueKind!=JsonValueKind.Array)return new(result);
   foreach(var item in items.EnumerateArray()) {
    if(!item.TryGetProperty("name",out var name)||!item.TryGetProperty("reference_value",out var reference))continue;
    var itemName=name.GetString();
    var raw=reference.ValueKind==JsonValueKind.String?reference.GetString():reference.ToString();
    var price=FormatWan(raw??"");
    if(!string.IsNullOrEmpty(itemName)&&!string.IsNullOrEmpty(price))result[Normalize(itemName)]=price;
   }
  } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException) { }
  return new(result);
 }

 private static string Normalize(string value) {
  var builder=new StringBuilder(value.Length);
  foreach(var ch in value)if(char.IsLetterOrDigit(ch))builder.Append(char.ToUpperInvariant(ch));
  return builder.ToString();
 }

 [GeneratedRegex(@"\d+(?:\.\d+)?",RegexOptions.CultureInvariant)]
 private static partial Regex NumberPattern();
}
