using System.IO;
using System.Text.Json;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.CoreAudioApi;
namespace FieldTrader;
public static class VoiceMixValidation {
 private sealed class Tone(int rate,int channels,double frequency,float amplitude,int length=int.MaxValue):ISampleProvider {
  private int cursor;public WaveFormat WaveFormat=>WaveFormat.CreateIeeeFloatWaveFormat(rate,channels);
  public int Read(float[] b,int o,int c){int n=Math.Min(c,length-cursor);for(int i=0;i<n;i++)b[o+i]=amplitude*(float)Math.Sin(2*Math.PI*frequency*((cursor+i)/channels)/rate);cursor+=n;return n;}
 }
 private static double Magnitude(float[] b,int offset,int samples,int channel,double frequency){double real=0,imag=0;for(int i=0;i<samples;i++){double phase=2*Math.PI*frequency*i/48000;float v=b[offset+i*2+channel];real+=v*Math.Cos(phase);imag+=v*Math.Sin(phase);}return 2*Math.Sqrt(real*real+imag*imag)/samples;}
 public static void Run(string directory){
  Directory.CreateDirectory(directory);var checks=new List<string>();void Check(bool ok,string message){if(!ok)throw new Exception(message);checks.Add(message);}
  using var mix=new LiveVoiceMix(new Tone(48000,2,440,.2f));mix.SetClip(new Tone(48000,2,880,.15f),null,1);var b=new float[96004];b[0]=42;b[^1]=43;Check(mix.Read(b,2,96000)==96000&&b[0]==42&&b[^1]==43,"按请求区域输出完整采样，不越界");
  Check(Math.Abs(Magnitude(b,2,48000,0,440)-.2)<.002&&Math.Abs(Magnitude(b,2,48000,0,880)-.15)<.002,"真人麦克风与音效两路同时存在，响度正确");
  mix.StopClip();mix.Read(b,2,96000);Check(Math.Abs(Magnitude(b,2,48000,0,440)-.2)<.002&&Magnitude(b,2,48000,0,880)<.002,"停止音效不停止真人麦克风");
  mix.MicrophoneGain=0;mix.Read(b,2,96000);Check(b.Skip(2).Take(96000).All(v=>v==0),"真人麦克风可独立静音");mix.SetClip(new Tone(48000,2,880,.15f),null,.5f);mix.Read(b,2,96000);Check(Math.Abs(Magnitude(b,2,48000,0,880)-.075)<.002,"音效音量独立生效");
  using var resampled=new LiveVoiceMix(new Tone(16000,1,440,.2f));resampled.Read(b,2,96000);Check(Math.Abs(Magnitude(b,2,48000,0,440)-.2)<.01&&Enumerable.Range(0,48000).All(i=>Math.Abs(b[2+i*2]-b[3+i*2])<.0001),"16kHz单声道麦克风转48kHz立体声 magnitude="+Magnitude(b,2,48000,0,440)+" stereoDiff="+Enumerable.Range(0,48000).Max(i=>Math.Abs(b[2+i*2]-b[3+i*2])));
  resampled.SetClip(new Tone(22050,1,880,.15f),null,1);resampled.Read(b,2,96000);Check(Magnitude(b,2,48000,0,440)>.18&&Magnitude(b,2,48000,0,880)>.12,"不同采样率音效可以和麦克风混合");
  using var loud=new LiveVoiceMix(new Tone(48000,2,440,1));loud.SetClip(new Tone(48000,2,440,1),null,1);loud.Read(b,2,96000);Check(b.Skip(2).Take(96000).All(v=>float.IsFinite(v)&&Math.Abs(v)<=.98f),"混音峰值受限，不产生越界采样");
  Check(!VoiceMicrophoneMixer.IsVirtualOutput("扬声器 (High Definition Audio Device)")&&VoiceMicrophoneMixer.IsVirtualOutput("CABLE Input (VB-Audio Virtual Cable)"),"不把扬声器当作虚拟麦克风线路");
  using var finite=new LiveVoiceMix(new Tone(48000,2,440,.2f));finite.SetClip(new Tone(48000,2,880,.15f,100),null,1);finite.Read(b,2,96000);Check(finite.IsPlaying,"保留输出缓冲尾音时间");finite.StopClip();Check(!finite.IsPlaying,"停止立即取消尾音等待");
  using var boosted=new LiveVoiceMix(new Tone(48000,2,440,.01f));boosted.TransmitGain=4;boosted.Read(b,2,96000);Check(Math.Abs(Magnitude(b,2,48000,0,440)-.04)<.001,"发送增强实际放大输出采样4倍");boosted.MicrophoneGain=3;boosted.Read(b,2,96000);Check(Math.Abs(Magnitude(b,2,48000,0,440)-.12)<.001,"真人麦克风音量实时放大输出采样");boosted.SetClip(new Tone(48000,2,880,.02f),null,1);boosted.EffectGain=.25f;boosted.Read(b,2,96000);Check(Math.Abs(Magnitude(b,2,48000,0,880)-.02)<.001,"播放途中调整音效音量立即改变发送采样");boosted.EffectGain=0;boosted.Read(b,2,96000);Check(Magnitude(b,2,48000,0,880)<.001&&Magnitude(b,2,48000,0,440)>.11,"音效静音不影响真人声发送");Check(!VoiceMicrophoneMixer.IsPhysicalMicrophone("CABLE Output (VB-Audio Virtual Cable)")&&VoiceMicrophoneMixer.IsPhysicalMicrophone("麦克风 (USB Headset)"),"虚拟回录设备不能作为真人麦克风");
  var settings=VoiceBoardSettings.Defaults();settings.MicrophoneId="physical-mic";settings.MixOutputId="virtual-render";settings.MicrophoneVolume=325;settings.TransmitBoost=4;settings.Save(directory);var loaded=VoiceBoardSettings.Load(directory,out _);Check(loaded.MicrophoneId==settings.MicrophoneId&&loaded.MixOutputId==settings.MixOutputId&&loaded.MicrophoneVolume==325&&loaded.TransmitBoost==4,"麦克风和混音设备、音量设置保存");
  File.WriteAllText(Path.Combine(directory,"mix-validation.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks,VirtualDriverInstalledByTest=false},new JsonSerializerOptions{WriteIndented=true}));
 }
 public static async Task Probe(string directory){
  Directory.CreateDirectory(directory);var inputs=VoiceMicrophoneMixer.Inputs();var outputs=AudioCaptureService.EnumerateDevices().Where(d=>VoiceMicrophoneMixer.IsVirtualOutput(d.Name)).ToArray();string status="无可用实体麦克风";int callbacks=0,samples=0;string? error=null;
  var device=inputs.FirstOrDefault(d=>!d.Name.Contains("CABLE",StringComparison.OrdinalIgnoreCase)&&!d.Name.Contains("VoiceMeeter",StringComparison.OrdinalIgnoreCase));
  if(device!=null)try{using var enumerator=new NAudio.CoreAudioApi.MMDeviceEnumerator();using var endpoint=enumerator.GetDevice(device.Id);using var capture=new WasapiCapture(endpoint);var decoder=new CaptureSampleDecoder(capture.WaveFormat);capture.DataAvailable+=(_,e)=>{try{var (mono,_,_)=decoder.Decode(e.Buffer,e.BytesRecorded);Interlocked.Increment(ref callbacks);Interlocked.Add(ref samples,mono.Length);}catch(Exception ex){error=ex.Message;}};capture.StartRecording();await Task.Delay(900);capture.StopRecording();status=callbacks>0&&error==null?"原生麦克风采集与格式解码通过":"未收到有效数据帧";}catch(Exception ex){status="采集失败";error=ex.Message;}
  File.WriteAllText(Path.Combine(directory,"microphone-probe.json"),JsonSerializer.Serialize(new{Inputs=inputs.Select(d=>d.Name),VirtualOutputs=outputs.Select(d=>d.Name),Status=status,Callbacks=callbacks,Samples=samples,Error=error,AudioSaved=false,AudioPlayed=false},new JsonSerializerOptions{WriteIndented=true}));
 }
}
