using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
namespace FieldTrader;

public sealed record HotKeyGesture(ModifierKeys Modifiers,Key Key) {
 public string DisplayText => string.Join(" + ",ModifierLabels(Modifiers).Append(KeyLabel(Key)));

 public static bool TryCreate(ModifierKeys modifiers,Key key,out HotKeyGesture gesture) {
  gesture=default!;
  if(key is Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed)return false;
  if(IsModifierKey(key))return false;
  if((modifiers&(ModifierKeys.Alt|ModifierKeys.Control|ModifierKeys.Shift|ModifierKeys.Windows))==0)return false;
  gesture=new(modifiers,key);
  return true;
 }

 internal uint Win32Modifiers {
  get {
   uint value=0;
   if((Modifiers&ModifierKeys.Alt)!=0)value|=0x0001;
   if((Modifiers&ModifierKeys.Control)!=0)value|=0x0002;
   if((Modifiers&ModifierKeys.Shift)!=0)value|=0x0004;
   if((Modifiers&ModifierKeys.Windows)!=0)value|=0x0008;
   return value;
  }
 }

 internal uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

 private static IEnumerable<string> ModifierLabels(ModifierKeys modifiers) {
  if((modifiers&ModifierKeys.Control)!=0)yield return "Ctrl";
  if((modifiers&ModifierKeys.Alt)!=0)yield return "Alt";
  if((modifiers&ModifierKeys.Shift)!=0)yield return "Shift";
  if((modifiers&ModifierKeys.Windows)!=0)yield return "Win";
 }

 private static string KeyLabel(Key key)=>key switch {
  >= Key.D0 and <= Key.D9 => ((char)('0'+(key-Key.D0))).ToString(),
  >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0'+(key-Key.NumPad0))).ToString(),
  Key.OemPlus=>"+",Key.OemMinus=>"-",Key.OemComma=>",",Key.OemPeriod=>".",Key.OemQuestion=>"/",
  Key.OemSemicolon=>";",Key.OemQuotes=>"'",Key.OemOpenBrackets=>"[",Key.OemCloseBrackets=>"]",
  Key.OemPipe=>"\\",Key.OemBackslash=>"\\",Key.OemTilde=>"`",Key.Space=>"Space",Key.Return=>"Enter",
  Key.Back=>"Backspace",Key.Escape=>"Esc",Key.Delete=>"Delete",Key.Insert=>"Insert",Key.Home=>"Home",
  Key.End=>"End",Key.PageUp=>"PageUp",Key.PageDown=>"PageDown",_=>key.ToString()
 };

 private static bool IsModifierKey(Key key)=>key is
  Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
  Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
}

public sealed class GlobalHotKeyService : IDisposable {
 private const int HotKeyId=0x4654;
 private const int WmHotKey=0x0312;
 private const uint ModNoRepeat=0x4000;
 private readonly Window window;
 private HwndSource? source;
 private bool registered;
 private bool disposed;

 public event EventHandler? Pressed;
 public HotKeyGesture Gesture {get;private set;}=UserSettings.Default.ToggleListeningHotKey;
 public bool IsRegistered=>registered;
 public string? RegistrationError {get;private set;}

 public GlobalHotKeyService(Window window,HotKeyGesture gesture) {
  this.window=window;
  Gesture=gesture;
  if(new WindowInteropHelper(window).Handle!=IntPtr.Zero)Attach();
  else window.SourceInitialized+=(_,_)=>Attach();
 }

 public bool TrySet(HotKeyGesture gesture,out string error) {
  ThrowIfDisposed();
  var previous=Gesture;
  Unregister();
  Gesture=gesture;
  if(AttachSource()&&Register()) {RegistrationError=null;error="";return true;}
  var registrationError=RegistrationError??"窗口尚未准备好，无法注册全局快捷键";
  Gesture=previous;
  Register();
  error=registrationError;
  return false;
 }

 public void Suspend()=>Unregister();

 public void Resume() {
  if(!disposed&&AttachSource()&&!registered)Register();
 }

 public void Dispose() {
  if(disposed)return;
  disposed=true;
  Unregister();
  if(source!=null)source.RemoveHook(WndProc);
  source=null;
 }

 private void Attach() {
  if(!AttachSource()||registered)return;
  Register();
 }

 private bool AttachSource() {
  if(source!=null)return true;
  var handle=new WindowInteropHelper(window).Handle;
  if(handle==IntPtr.Zero)return false;
  source=HwndSource.FromHwnd(handle);
  source?.AddHook(WndProc);
  return source!=null;
 }

 private bool Register() {
  if(registered)return true;
  var handle=new WindowInteropHelper(window).Handle;
  if(handle==IntPtr.Zero)return false;
  if(RegisterHotKey(handle,HotKeyId,Gesture.Win32Modifiers|ModNoRepeat,Gesture.VirtualKey)) {
   registered=true;
   RegistrationError=null;
   return true;
  }
  RegistrationError=$"无法注册 {Gesture.DisplayText}：{new Win32Exception(Marshal.GetLastWin32Error()).Message}";
  return false;
 }

 private void Unregister() {
  if(!registered)return;
  var handle=new WindowInteropHelper(window).Handle;
  if(handle!=IntPtr.Zero)UnregisterHotKey(handle,HotKeyId);
  registered=false;
 }

 private IntPtr WndProc(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled) {
  if(message==WmHotKey&&wParam.ToInt32()==HotKeyId) {
   Pressed?.Invoke(this,EventArgs.Empty);
   handled=true;
  }
  return IntPtr.Zero;
 }

 private void ThrowIfDisposed() {
  if(disposed)throw new ObjectDisposedException(nameof(GlobalHotKeyService));
 }

 [DllImport("user32.dll",SetLastError=true)]
 private static extern bool RegisterHotKey(IntPtr hWnd,int id,uint modifiers,uint virtualKey);

 [DllImport("user32.dll",SetLastError=true)]
 private static extern bool UnregisterHotKey(IntPtr hWnd,int id);
}
