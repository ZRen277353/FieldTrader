using System.Globalization;
namespace FieldTrader;

public static class ReferencePriceCatalog {
 public static string FormatWan(int? price) {
  if(price is null or <=0)return "";
  return (price.Value/10000d).ToString("0.##",CultureInfo.InvariantCulture)+"W";
 }
}
