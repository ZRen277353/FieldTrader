using System.IO;
using System.Text.Json;
namespace FieldTrader;
public static class VoicePlaybackValidation {
 private sealed class Keys : IVoiceKeyInput {public HashSet<uint> Down=[];public List<string> Events=[];public bool IsDown(uint k)=>Down.Contains(k);public void Send(uint k,bool down){Events.Add($"{k}:{down}");if(down)Down.Add(k);else Down.Remove(k);}}
 public static async Task Run(string directory){
  Directory.CreateDirectory(directory);var checks=new List<string>();void Check(bool v,string s){if(!v)throw new Exception(s);checks.Add(s);}
  Check(VoicePlaybackController.ParseTalkKey("T",out var t)&&t==0x54&&!VoicePlaybackController.ParseTalkKey("Ctrl+T",out _),"单说话键校验");
  var keys=new Keys();bool foreground=true,playing=false;int stops=0,starts=0;
  using var c=new VoicePlaybackController(keys,()=>foreground,()=>{playing=false;stops++;});
  Task Play(bool auto=true,bool hotkey=false)=>c.PlayAsync(()=>{starts++;playing=true;},()=>playing,auto,t,hotkey,_=>{});
  var a=Play();Check(keys.IsDown(t)&&starts==0,"先按T，预留麦克风开启时间");await Task.Delay(180);Check(playing,"按住后开始播放");playing=false;await a;Check(!keys.IsDown(t),"播放完成自动松键");
  a=Play();await Task.Delay(180);c.Stop();await a;Check(!playing&&!keys.IsDown(t),"手动停止释放按键");
  a=Play();await Task.Delay(180);var b=Play();await Task.Delay(200);Check(playing,"新音效替换时，旧任务不停止新播放");playing=false;await Task.WhenAll(a,b);Check(!keys.IsDown(t),"切换音效结束释放按键");
  a=Play();await Task.Delay(180);foreground=false;await a;Check(!playing&&!keys.IsDown(t),"切出游戏立即停止并松键");
  int sent=keys.Events.Count;a=Play();await Task.Delay(20);playing=false;await a;Check(keys.Events.Count==sent,"非游戏前台仅本地试听，不发送按键");
  foreground=true;a=Play(false);await Task.Delay(20);playing=false;await a;Check(keys.Events.Count==sent,"自由说话不发送按键");
  keys.Down.Add(0x11);int before=starts;a=Play(true,true);await Task.Delay(80);Check(starts==before&&!keys.IsDown(t),"等待用户松开快捷键修饰键");keys.Down.Remove(0x11);await Task.Delay(200);Check(playing&&keys.IsDown(t),"修饰键释放后自动按T并播放");playing=false;await a;
  keys.Down.Add(t);sent=keys.Events.Count;a=Play();await Task.Delay(180);playing=false;await a;Check(keys.IsDown(t)&&keys.Events.Count==sent,"用户原本按住T时不替用户松键");keys.Down.Remove(t);
  string error="";await c.PlayAsync(()=>throw new IOException("模拟播放失败"),()=>false,true,t,false,s=>error=s);Check(!keys.IsDown(t)&&error.Contains("失败"),"播放失败仍释放按键并反馈");
  before=starts;a=Play();c.Dispose();await a;Check(!keys.IsDown(t)&&starts==before,"退出时取消等待并释放按键");
  var root=Path.Combine(directory,"settings");Directory.CreateDirectory(root);var settings=VoiceBoardSettings.Defaults();settings.PushToTalk=false;settings.TalkKey="Y";settings.Save(root);var loaded=VoiceBoardSettings.Load(root,out _);Check(!loaded.PushToTalk&&loaded.TalkKey=="Y","发言模式与说话键持久化");
  File.WriteAllText(Path.Combine(directory,"ptt-validation.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks,NativeKeyInjectionTested=false},new JsonSerializerOptions{WriteIndented=true}));
 }
}
