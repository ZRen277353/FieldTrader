using System.IO;
using System.Text.Json;
using System.Windows.Input;
namespace FieldTrader;

public sealed record UserSettings(ModifierKeys ToggleListeningModifiers, Key ToggleListeningKey,double OverlayOpacity=1,string SortField="价格",string SortDirection="降序",string RedQualityBackground=QualityColors.RedDefault,string GoldQualityBackground=QualityColors.GoldDefault,string PurpleQualityBackground=QualityColors.PurpleDefault) {
 public static UserSettings Default => new(ModifierKeys.Alt,Key.W);
 public HotKeyGesture ToggleListeningHotKey => new(ToggleListeningModifiers,ToggleListeningKey);
 public QualityPalette QualityPalette=>new(RedQualityBackground,GoldQualityBackground,PurpleQualityBackground);
}

public static class UserSettingsStore {
 private static readonly JsonSerializerOptions JsonOptions=new(){WriteIndented=true};
 public static UserSettings Load(string root) {
  try {
   var settings=JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(Path.Combine(root,"settings.json")));
   if(settings!=null&&HotKeyGesture.TryCreate(settings.ToggleListeningModifiers,settings.ToggleListeningKey,out _)) {
    var opacity=double.IsFinite(settings.OverlayOpacity)?Math.Clamp(settings.OverlayOpacity,.35,1):1;
    var field=settings.SortField is "价格" or "品质" or "名字"?settings.SortField:"价格";
    var direction=settings.SortDirection is "升序" or "降序"?settings.SortDirection:"降序";
    var red=QualityColors.TryNormalize(settings.RedQualityBackground,out var redValue)?redValue:QualityColors.RedDefault;
    var gold=QualityColors.TryNormalize(settings.GoldQualityBackground,out var goldValue)?goldValue:QualityColors.GoldDefault;
    var purple=QualityColors.TryNormalize(settings.PurpleQualityBackground,out var purpleValue)?purpleValue:QualityColors.PurpleDefault;
    return settings with{OverlayOpacity=opacity,SortField=field,SortDirection=direction,RedQualityBackground=red,GoldQualityBackground=gold,PurpleQualityBackground=purple};
   }
  } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException) { }
  return UserSettings.Default;
 }
 public static void Save(string root,UserSettings settings) {
  var path=Path.Combine(root,"settings.json");
  File.WriteAllText(path+".tmp",JsonSerializer.Serialize(settings,JsonOptions));
  File.Move(path+".tmp",path,true);
 }
 public static void Update(string root,Func<UserSettings,UserSettings> update) {
  Save(root,update(Load(root)));
 }
}
