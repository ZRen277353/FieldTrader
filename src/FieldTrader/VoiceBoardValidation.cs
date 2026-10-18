using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace FieldTrader;
public static class VoiceBoardValidation {
 [DllImport("user32.dll",SetLastError=true)]private static extern bool RegisterHotKey(IntPtr h,int id,uint m,uint k);
 [DllImport("user32.dll")]private static extern bool UnregisterHotKey(IntPtr h,int id);
 public static async Task Run(string directory){
  Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;Directory.CreateDirectory(directory);var checks=new List<string>();
  void Check(bool value,string message){if(!value)throw new Exception(message);checks.Add("PASS "+message);}
  foreach(var s in new[]{"Ctrl+Alt+1","Shift+F11","Ctrl+Shift+Q","Alt+6"})Check(VoiceHotkey.Parse(s,out _,out _),"有效快捷键 "+s);
  foreach(var s in new[]{"A","F12","Ctrl+Ctrl+A","Win+Q","Ctrl+A+B","Ctrl+","Alt+F0"})Check(!VoiceHotkey.Parse(s,out _,out _),"拒绝无效快捷键 "+s);
  var root=Path.Combine(directory,"database");var db=new CatalogStore(root);db.Load();SeedCatalog.Merge(db);
  var settings=VoiceBoardSettings.Defaults();settings.Volume=37;settings.Rate=-2;settings.Phrases[0].Text="测试保存";settings.Phrases[0].Note="自定义中文备注";settings.Save(root);var loaded=VoiceBoardSettings.Load(root,out _);Check(loaded.Volume==37&&loaded.Rate==-2&&loaded.Phrases[0].Text=="测试保存"&&loaded.Phrases[0].Note=="自定义中文备注","短句音量语速持久化");
  File.WriteAllText(Path.Combine(root,"voiceboard.json"),"invalid json");var restored=VoiceBoardSettings.Load(root,out var notice);Check(restored.Phrases.Count>=8&&notice.Length>0&&Directory.GetFiles(root,"voiceboard.json.broken-*").Length==1,"损坏配置备份恢复");restored.Phrases.ForEach(p=>p.Hotkey="");restored.Phrases[0].Hotkey="Ctrl+Alt+Shift+F10";restored.Save(root);
  using(var audio=new VoiceBoardAudio()){
   Check(audio.Voices.Count>0,"SAPI桌面音色枚举");string file=Path.GetFullPath(Path.Combine(directory,"tts-test.wav"));audio.RenderTest("收到，谢谢。",file);var frame=AudioFiles.Read(file);Check(frame.Mono.Length>0&&frame.Mono.Any(v=>Math.Abs(v)>.0001),"实际本地TTS生成非静音WAV");
   string imported=VoiceBoardSettings.ImportWav(root,file);var before=File.ReadAllBytes(file);File.Move(file,file+".moved");Check(File.ReadAllBytes(Path.Combine(root,imported)).SequenceEqual(before),"导入WAV独立复制，原文件移动后仍可读取");string rendered=Path.GetFullPath(Path.Combine(directory,"wav-playback-test.wav"));audio.RenderWavTest(Path.GetFullPath(Path.Combine(root,imported)),rendered);Check(AudioFiles.Read(rendered).Mono.Any(v=>Math.Abs(v)>.0001),"实际WAV播放流生成非静音输出");
   var cache=audio.FileForMix(new VoicePhrase{Text="混音测试"},audio.Voices.FirstOrDefault(v=>v.Chinese)??audio.Voices[0],0,root);Check(AudioFiles.Read(cache).Mono.Any(v=>Math.Abs(v)>.0001)&&audio.FileForMix(new VoicePhrase{Text="混音测试"},audio.Voices.FirstOrDefault(v=>v.Chinese)??audio.Voices[0],0,root)==cache,"混音文字朗读缓存包含有效声音且可复用");
   bool blocked=false;try{audio.Play(new VoicePhrase(),null,null,80,0,root);}catch(InvalidOperationException){blocked=true;}Check(blocked,"未选设备禁止播放，避免误路由");checks.Add("INFO 语音输出设备 "+audio.Outputs.Count+" / 音色 "+audio.Voices.Count);
  }
  using var state=new SessionState(db,Application.Current.Dispatcher);var main=new MainWindow(state);main.Show();
  var nav=FindButtons(main).First(b=>b.Content?.ToString()?.Contains("游戏语音")==true);nav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  var board=Application.Current.Windows.OfType<VoiceBoardWindow>().Single();await Task.Delay(200);Capture(main,Path.Combine(directory,"main-window.png"));Capture(board,Path.Combine(directory,"voice-board.png"));var cards=(ListBox)board.FindName("PhraseGrid");Check(cards.Items.Count==8,"卡片录音分类显示8项");var selected=(VoicePhrase)cards.SelectedItem;selected.Note="卡片备注实时更新";Check(cards.SelectedItem==selected,"右侧编辑保持选中卡片");board.Width=1000;board.Height=740;await Task.Delay(100);Capture(board,Path.Combine(directory,"voice-board-small.png"));board.Width=1180;board.Height=860;Check(board.ActiveHotkeys==1,"实际注册全局快捷键");
  Check(!RegisterHotKey(IntPtr.Zero,0x60A1,0x4000|1|2|4,0x79),"已注册快捷键能被系统检测为冲突");board.Close();Check(!board.IsVisible&&board.ActiveHotkeys==1,"隐藏语音窗口保留快捷键");board.Show();Check(board.IsVisible,"可以重新显示语音板");main.Close();
  bool freed=RegisterHotKey(IntPtr.Zero,0x60A1,0x4000|1|2|4,0x79);if(freed)UnregisterHotKey(IntPtr.Zero,0x60A1);Check(freed,"退出后释放全局快捷键");main.Close();
  File.WriteAllText(Path.Combine(directory,"validation.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks},new JsonSerializerOptions{WriteIndented=true}));
 }
 private static IEnumerable<Button> FindButtons(DependencyObject parent){for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is Button button)yield return button;foreach(var next in FindButtons(child))yield return next;}}
 private static void Capture(Window w,string path){w.UpdateLayout();var bitmap=new RenderTargetBitmap((int)w.ActualWidth,(int)w.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(w);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);}
}
