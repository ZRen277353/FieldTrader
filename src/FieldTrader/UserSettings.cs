using System.IO;
using System.Text.Json;
using System.Windows.Input;
namespace FieldTrader;

public sealed record UserSettings(ModifierKeys ToggleListeningModifiers, Key ToggleListeningKey) {
 public static UserSettings Default => new(ModifierKeys.Alt,Key.W);
 public HotKeyGesture ToggleListeningHotKey => new(ToggleListeningModifiers,ToggleListeningKey);
}

public static class UserSettingsStore {
 private static readonly JsonSerializerOptions JsonOptions=new(){WriteIndented=true};
 public static UserSettings Load(string root) {
  try {
   var settings=JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(Path.Combine(root,"settings.json")));
   if(settings!=null&&HotKeyGesture.TryCreate(settings.ToggleListeningModifiers,settings.ToggleListeningKey,out _))return settings;
  } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException) { }
  return UserSettings.Default;
 }
 public static void Save(string root,UserSettings settings) {
  var path=Path.Combine(root,"settings.json");
  File.WriteAllText(path+".tmp",JsonSerializer.Serialize(settings,JsonOptions));
  File.Move(path+".tmp",path,true);
 }
}
