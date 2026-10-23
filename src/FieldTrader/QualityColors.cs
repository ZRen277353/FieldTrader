using System.Globalization;
using System.Windows.Media;
namespace FieldTrader;

public sealed record QualityPalette(string Red,string Gold,string Purple) {
 public static QualityPalette Default {get;}=new(QualityColors.RedDefault,QualityColors.GoldDefault,QualityColors.PurpleDefault);
 public string Resolve(string? sourceColor)=>QualityColors.Normalize(sourceColor) switch {
  "#ff4d4f"=>Red,
  "#faad14"=>Gold,
  "#b37feb"=>Purple,
  _=>"#243746"
 };
}

public static class QualityColors {
 public const string RedDefault="#361c1a";
 public const string GoldDefault="#2a1d0e";
 public const string PurpleDefault="#1d1a27";
 public static bool TryNormalize(string? value,out string normalized) {
  normalized="";
  if(string.IsNullOrWhiteSpace(value))return false;
  var text=value.Trim();
  if(text.StartsWith('#'))text=text[1..];
  if(text.Length!=6||!int.TryParse(text,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out _))return false;
  normalized="#"+text.ToLowerInvariant();
  return true;
 }
 public static string Normalize(string? value)=>TryNormalize(value,out var normalized)?normalized:"#243746";
 public static Color Background(string? sourceColor,QualityPalette? palette=null) {
  var selected=(palette??QualityPalette.Default).Resolve(sourceColor);
  return (Color)ColorConverter.ConvertFromString(selected);
 }
 public static Color FromHex(string? color) {
  if(!TryNormalize(color,out var normalized))return Color.FromRgb(36,55,70);
  return (Color)ColorConverter.ConvertFromString(normalized);
 }
 public static Brush Brush(string? color) {
  var brush=new SolidColorBrush(FromHex(color));
  brush.Freeze();
  return brush;
 }
 public static int Rank(string? quality)=>quality switch {"红色"=>5,"金色"=>4,"紫色"=>3,"蓝色"=>2,"白色"=>1,_=>0};
}
