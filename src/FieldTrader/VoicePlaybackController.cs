using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
namespace FieldTrader;
public interface IVoiceKeyInput {bool IsDown(uint key);void Send(uint key,bool down);}
public sealed class WindowsVoiceKeyInput : IVoiceKeyInput {
 [StructLayout(LayoutKind.Sequential)]private struct Mouse {public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
 [StructLayout(LayoutKind.Sequential)]private struct Keyboard {public ushort Key,Scan;public uint Flags,Time;public UIntPtr Extra;}
 [StructLayout(LayoutKind.Explicit)]private struct Union {[FieldOffset(0)]public Mouse Mouse;[FieldOffset(0)]public Keyboard Keyboard;}
 [StructLayout(LayoutKind.Sequential)]private struct Input {public uint Type;public Union Value;}
 [DllImport("user32.dll",SetLastError=true)]private static extern uint SendInput(uint count,Input[] inputs,int size);
 [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
 [DllImport("user32.dll")]private static extern uint MapVirtualKey(uint code,uint mode);
 [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint id);
 public bool IsDown(uint key)=>(GetAsyncKeyState((int)key)&0x8000)!=0;
 public void Send(uint key,bool down){uint scan=MapVirtualKey(key,0);var input=new Input{Type=1,Value=new Union{Keyboard=new Keyboard{Key=scan==0?(ushort)key:(ushort)0,Scan=(ushort)scan,Flags=(scan==0?0u:8u)|(down?0u:2u)}}};if(SendInput(1,[input],Marshal.SizeOf<Input>())!=1)throw new Win32Exception(Marshal.GetLastWin32Error(),"Windows未接受说话键，请检查游戏权限或改用手动按键。");}
 public static bool GameIsForeground(){try{GetWindowThreadProcessId(GetForegroundWindow(),out uint id);using var p=Process.GetProcessById((int)id);return p.ProcessName.Equals("UAGame",StringComparison.OrdinalIgnoreCase);}catch{return false;}}
}
public sealed class VoicePlaybackController : IDisposable {
 private readonly IVoiceKeyInput input;private readonly Func<bool> foreground;private readonly Action stopAudio;private CancellationTokenSource? current;private Lease? active;private bool disposed;
 private sealed class Lease(IVoiceKeyInput input,uint key){private bool owns;public bool Owns=>owns;public void Press(){if(!input.IsDown(key)){input.Send(key,true);owns=true;}}public void Release(){if(!owns)return;input.Send(key,false);owns=false;}}
 public VoicePlaybackController(IVoiceKeyInput input,Func<bool> foreground,Action stopAudio){this.input=input;this.foreground=foreground;this.stopAudio=stopAudio;}
 public static bool ParseTalkKey(string? text,out uint key){key=0;return VoiceHotkey.Parse("Ctrl+"+(text??""),out uint mods,out key)&&mods==2;}
 public async Task PlayAsync(Action begin,Func<bool> isPlaying,bool autoTalk,uint key,bool fromHotkey,Action<string> status){
  if(disposed)return;Stop();var request=new CancellationTokenSource();var token=request.Token;current=request;Lease? lease=null;bool started=false;
  void Report(string s){if(current==request)status(s);}
  try{
   bool hold=autoTalk&&foreground();
   if(hold&&fromHotkey){var wait=Stopwatch.StartNew();while(input.IsDown(0x10)||input.IsDown(0x11)||input.IsDown(0x12)){Report("请松开组合快捷键，准备按住说话键播放…");if(wait.ElapsedMilliseconds>2000)throw new InvalidOperationException("快捷键仍被按住，请松开后重试。");await Task.Delay(20,token);}}
   if(hold){if(!foreground())throw new OperationCanceledException();lease=new(input,key);active=lease;lease.Press();Report("已按住说话键，准备播放…");await Task.Delay(150,token);if(!foreground())throw new OperationCanceledException();}
   token.ThrowIfCancellationRequested();begin();started=true;Report(hold?"正在播放 · 自动按住说话键":autoTalk?"本地试听 · 游戏前台时才会自动按说话键":"正在播放 · 自由说话模式");var watch=Stopwatch.StartNew();
   while(isPlaying()){token.ThrowIfCancellationRequested();if(hold&&!foreground())throw new OperationCanceledException();if(watch.Elapsed.TotalMinutes>2)throw new InvalidOperationException("播放超过两分钟，已停止。");await Task.Delay(50,token);}
   if(hold)await Task.Delay(120,token);lease?.Release();Report("播放完成");
  }catch(OperationCanceledException){if(started&&current==request)stopAudio();Report("已停止播放");}
  catch(Exception ex){if(started&&current==request)stopAudio();Report("播放失败："+ex.Message);}
  finally{try{lease?.Release();}finally{if(current==request){current=null;active=null;}request.Dispose();}}
 }
 public void Stop(){var old=current;current=null;old?.Cancel();try{stopAudio();}finally{var lease=active;active=null;lease?.Release();}}
 public void Dispose(){if(disposed)return;disposed=true;Stop();}
}
