using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
namespace FieldTrader;
public sealed class VoicePhrase : System.ComponentModel.INotifyPropertyChanged {
 public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
 private void Set(ref string field,string value,[System.Runtime.CompilerServices.CallerMemberName]string? name=null){if(field==value)return;field=value;PropertyChanged?.Invoke(this,new(name));if(name==nameof(Wav)){foreach(var n in new[]{nameof(WavLabel),nameof(Glyph),nameof(KindLabel)})PropertyChanged?.Invoke(this,new(n));}if(name==nameof(Hotkey))PropertyChanged?.Invoke(this,new(nameof(HotkeyLabel)));}
 public string Id {get;set;}=Guid.NewGuid().ToString("N");private string title="新短句",note="",text="收到。",hotkey="",wav="";
 public string Title {get=>title;set=>Set(ref title,value??"");}public string Note {get=>note;set=>Set(ref note,value??"");}public string Text {get=>text;set=>Set(ref text,value??"");}public string Hotkey {get=>hotkey;set=>Set(ref hotkey,value??"");}public string Wav {get=>wav;set=>Set(ref wav,value??"");}
 [System.Text.Json.Serialization.JsonIgnore]public string WavLabel=>string.IsNullOrWhiteSpace(Wav)?"文字朗读":"已导入录音";
 [System.Text.Json.Serialization.JsonIgnore]public string Glyph=>string.IsNullOrWhiteSpace(Wav)?"♪":"♫";
 [System.Text.Json.Serialization.JsonIgnore]public string KindLabel=>string.IsNullOrWhiteSpace(Wav)?"文字语音":"录制音效";
 [System.Text.Json.Serialization.JsonIgnore]public string HotkeyLabel=>string.IsNullOrWhiteSpace(Hotkey)?"未设置热键":Hotkey;
}
public sealed class VoiceBoardSettings {
 public string MicrophoneId {get;set;}="";public string MixOutputId {get;set;}="";public int MicrophoneVolume {get;set;}=100;public double TransmitBoost {get;set;}=2;
 public bool PushToTalk {get;set;}=true;public string TalkKey {get;set;}="T";
 public string OutputId {get;set;}="";public string VoiceId {get;set;}="";public int Volume {get;set;}=80;public int Rate {get;set;}
 public List<VoicePhrase> Phrases {get;set;}=[];public List<string> ImportedVoiceIds {get;set;}=[];
 public static VoiceBoardSettings Defaults(){var s=new VoiceBoardSettings();var lines=new[]{"收到","谢谢","等我一下","一起走","小心，前面有人","我需要支援","先撤退","打得漂亮"};for(int i=0;i<lines.Length;i++)s.Phrases.Add(new(){Id="default-tts-"+i,Title=lines[i],Text=lines[i]+"。",Note=new[]{"确认收到","表达感谢","请对方稍等","邀请一起行动","提醒前方有敌人","请求队友支援","提醒先撤离","称赞队友"}[i],Hotkey="Ctrl+Alt+"+(i+1)});return s;}
 public static VoiceBoardSettings Load(string root,out string notice){
  notice="";var path=Path.Combine(root,"voiceboard.json");VoiceBoardSettings result;
  if(!File.Exists(path))result=Defaults();else try{result=JsonSerializer.Deserialize<VoiceBoardSettings>(File.ReadAllText(path))??throw new InvalidDataException("配置为空");if(result.Phrases==null||result.Phrases.Count>500||result.Phrases.Any(p=>p==null))throw new InvalidDataException("短句配置无效");result.Volume=Math.Clamp(result.Volume,0,100);result.Rate=Math.Clamp(result.Rate,-10,10);}catch(Exception ex){var backup=path+".broken-"+DateTime.Now.ToString("yyyyMMddHHmmssfff");File.Copy(path,backup);notice="语音配置无法读取，已备份并恢复默认短句："+ex.Message;result=Defaults();}
  if(result.MergeSeed(root))result.Save(root);return result;
 }
 private bool MergeSeed(string root){
  var seed=Path.Combine(AppContext.BaseDirectory,"VoiceSeed");var manifest=Path.Combine(seed,"library.json");if(!File.Exists(manifest))return false;
  var library=JsonSerializer.Deserialize<VoiceBoardSettings>(File.ReadAllText(manifest))??throw new InvalidDataException("内置音效库无效");ImportedVoiceIds??=[];bool changed=false;
  foreach(var entry in library.Phrases){if(ImportedVoiceIds.Contains(entry.Id))continue;string file=Path.GetFileName(entry.Wav);string destination=Path.Combine(root,"voiceboard-audio",file);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);if(!File.Exists(destination))File.Copy(Path.Combine(seed,"audio",file),destination);entry.Wav=Path.Combine("voiceboard-audio",file);if(!Phrases.Any(p=>p.Id==entry.Id))Phrases.Add(entry);ImportedVoiceIds.Add(entry.Id);changed=true;}
  return changed;
 }
 public static string ImportWav(string root,string file){
  using(var reader=new NAudio.Wave.WaveFileReader(file)){if(reader.WaveFormat.Encoding!=NAudio.Wave.WaveFormatEncoding.Pcm&&reader.WaveFormat.Encoding!=NAudio.Wave.WaveFormatEncoding.Extensible)throw new InvalidDataException("请使用未压缩PCM WAV录音。");}
  Directory.CreateDirectory(Path.Combine(root,"voiceboard-audio"));string relative=Path.Combine("voiceboard-audio",Guid.NewGuid()+".wav");File.Copy(file,Path.Combine(root,relative));return relative;
 }
 public void Save(string root){Directory.CreateDirectory(root);string path=Path.Combine(root,"voiceboard.json");File.WriteAllText(path+".tmp",JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));if(File.Exists(path))File.Copy(path,path+".bak",true);File.Move(path+".tmp",path,true);}
}
public static class VoiceHotkey {
 public static bool Parse(string? value,out uint mods,out uint key){mods=key=0;if(string.IsNullOrWhiteSpace(value))return false;foreach(string part in value.ToUpperInvariant().Replace(" ","").Split('+')){uint m=part=="CTRL"?2u:part=="ALT"?1u:part=="SHIFT"?4u:0u;if(m!=0){if((mods&m)!=0)return false;mods|=m;continue;}if(key!=0)return false;if(part.Length==1&&((part[0]>='A'&&part[0]<='Z')||(part[0]>='0'&&part[0]<='9')))key=part[0];else if(part.StartsWith('F')&&int.TryParse(part[1..],out int f)&&f>=1&&f<=11)key=(uint)(111+f);else return false;}return key!=0&&mods!=0;}
}
public sealed record SpeechChoice(string Id,string Name,object Handle,bool Chinese=false){public override string ToString()=>Name;}
public sealed class VoiceBoardAudio : IDisposable {
 private dynamic? speech,stream;private bool playing;private readonly List<SpeechChoice> outputs=[],voices=[];
 public string DeviceNotice {get;private set;}="";
 public IReadOnlyList<SpeechChoice> Outputs=>outputs;public IReadOnlyList<SpeechChoice> Voices=>voices;
 public VoiceBoardAudio(){var type=Type.GetTypeFromProgID("SAPI.SpVoice")??throw new NotSupportedException("Windows桌面语音组件不可用");speech=Activator.CreateInstance(type);try{Refresh();}catch{Dispose();throw;}}
 private static void Release(object? value){if(value!=null&&Marshal.IsComObject(value))Marshal.FinalReleaseComObject(value);}
 private static void Fill(List<SpeechChoice> list,object collection,bool voice){dynamic tokens=collection;try{for(int i=0;i<(int)tokens.Count;i++){dynamic t=tokens.Item(i);bool chinese=false;if(voice)try{foreach(string lang in ((string)t.GetAttribute("Language")).Split(';'))if(int.TryParse(lang,System.Globalization.NumberStyles.HexNumber,null,out int code)&&(code&1023)==4)chinese=true;}catch{}list.Add(new((string)t.Id,(string)t.GetDescription(0),(object)t,chinese));}}finally{Release(collection);}}
 public void Refresh(){Stop();foreach(var t in outputs.Concat(voices))Release(t.Handle);outputs.Clear();voices.Clear();DeviceNotice="";try{Fill(outputs,(object)speech!.GetAudioOutputs("",""),false);}catch(COMException ex){DeviceNotice=$"当前无可用语音播放设备（0x{ex.HResult:X8}），连接设备后刷新。";}try{Fill(voices,(object)speech!.GetVoices("",""),true);}catch(COMException){
  // Read existing desktop tokens only; do not copy OneCore keys or change Windows settings.
  foreach(var hive in new[]{Microsoft.Win32.Registry.LocalMachine,Microsoft.Win32.Registry.CurrentUser}){
   using var key=hive.OpenSubKey(@"SOFTWARE\Microsoft\Speech\Voices\Tokens");if(key==null)continue;
   foreach(string name in key.GetSubKeyNames()){dynamic? token=null;try{string id=key.Name+"\\"+name;if(voices.Any(v=>v.Id==id))continue;token=Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpObjectToken",true)!);token!.SetId(id,"",false);string language=(string)token.GetAttribute("Language");bool chinese=language.Split(';').Any(l=>int.TryParse(l,System.Globalization.NumberStyles.HexNumber,null,out int code)&&(code&1023)==4);voices.Add(new(id,(string)token.GetDescription(0),(object)token,chinese));token=null;}catch(COMException){}finally{Release((object?)token);}}
  }
 }}
 public void SetLiveVolume(int volume){if(speech!=null)speech.Volume=Math.Clamp(volume,0,100);}
 public bool IsPlaying {get{if(!playing)return false;if((bool)speech!.WaitUntilDone(0)){playing=false;return false;}return true;}}
 public void Stop(){try{if(playing)speech?.Speak("",3);}finally{playing=false;if(stream!=null){try{stream.Close();}catch(COMException){}finally{Release((object)stream);stream=null;}}}}
 public void Play(VoicePhrase phrase,SpeechChoice? output,SpeechChoice? voice,int volume,int rate,string root){
  if(output==null)throw new InvalidOperationException("请先选择播放设备；试听选耳机，送入游戏选 CABLE Input。");Stop();speech!.AudioOutput=output.Handle;speech.Volume=Math.Clamp(volume,0,100);speech.Rate=Math.Clamp(rate,-10,10);
  if(!string.IsNullOrWhiteSpace(phrase.Wav)){var file=Path.IsPathRooted(phrase.Wav)?phrase.Wav:Path.Combine(root,phrase.Wav);if(!File.Exists(file))throw new FileNotFoundException("录音文件不存在，请重新导入");stream=Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpFileStream",true)!);stream!.Open(Path.GetFullPath(file),0,false);speech.SpeakStream(stream,1);playing=true;}
  else{if(voice==null)throw new InvalidOperationException("没有桌面TTS音色，可安装中文语音或导入WAV。");if(string.IsNullOrWhiteSpace(phrase.Text))throw new InvalidOperationException("短句内容为空");speech.Voice=voice.Handle;speech.Speak(phrase.Text,1|2|16);playing=true;}
 }
 public void RenderTest(string text,string file){Stop();dynamic fileStream=Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpFileStream",true)!)!;try{fileStream.Open(file,3,false);var voice=voices.FirstOrDefault(v=>v.Chinese)??voices.FirstOrDefault();if(voice!=null)speech!.Voice=voice.Handle;speech!.AudioOutputStream=fileStream;speech.Speak(text,2|16);fileStream.Close();}finally{Release((object)fileStream);}}
 public string FileForMix(VoicePhrase phrase,SpeechChoice? voice,int rate,string root){
  if(!string.IsNullOrWhiteSpace(phrase.Wav)){string path=Path.GetFullPath(Path.IsPathRooted(phrase.Wav)?phrase.Wav:Path.Combine(root,phrase.Wav));if(!File.Exists(path))throw new FileNotFoundException("录音文件不存在");return path;}
  if(voice==null||string.IsNullOrWhiteSpace(phrase.Text))throw new InvalidOperationException("请选择朗读音色并填写文字。");
  string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(voice.Id+"|"+rate+"|"+phrase.Text)));string folder=Path.Combine(root,"voiceboard-tts-cache");Directory.CreateDirectory(folder);string file=Path.Combine(folder,hash+".wav");if(File.Exists(file))return file;
  Stop();dynamic target=Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpFileStream",true)!)!;try{target.Open(file+".tmp",3,false);speech!.Voice=voice.Handle;speech.Rate=Math.Clamp(rate,-10,10);speech.Volume=100;speech.AudioOutputStream=target;speech.Speak(phrase.Text,2|16);target.Close();File.Move(file+".tmp",file,true);}finally{Release((object)target);}return file;
 }
 internal void RenderWavTest(string input,string file){Stop();dynamic target=Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpFileStream",true)!)!;dynamic wave=Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpFileStream",true)!)!;try{target.Open(file,3,false);wave.Open(input,0,false);speech!.AudioOutputStream=target;speech.SpeakStream(wave,0);wave.Close();target.Close();}finally{Release((object)wave);Release((object)target);}}
 public void Dispose(){try{Stop();}catch{}Release((object?)speech);speech=null;foreach(var t in outputs.Concat(voices))Release(t.Handle);outputs.Clear();voices.Clear();}
}
