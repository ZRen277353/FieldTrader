using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
namespace FieldTrader;

// The game records the virtual cable's capture end. Never route live microphone to speakers.
public sealed class LiveVoiceMix : ISampleProvider, IDisposable {
 private readonly ISampleProvider microphone;private readonly object gate=new();private ISampleProvider? clip;private IDisposable? owner;private float[] scratch=[],microphoneScratch=[];private long tailUntil;private volatile float micGain=1,clipGain=.8f,sendGain=1;private float outputPeak;
 public WaveFormat WaveFormat=>WaveFormat.CreateIeeeFloatWaveFormat(48000,2);
 public float MicrophoneGain {get=>micGain;set=>micGain=Math.Clamp(value,0,4f);}
 public float EffectGain {get=>clipGain;set=>clipGain=Math.Clamp(value,0,1);}
 public float TransmitGain {get=>sendGain;set=>sendGain=Math.Clamp(value,1,8);}
 public float OutputPeak=>Volatile.Read(ref outputPeak);
 public bool IsPlaying {get{lock(gate)return clip!=null||Environment.TickCount64<tailUntil;}}
 public LiveVoiceMix(ISampleProvider microphone){this.microphone=Normalize(microphone);}
 internal static ISampleProvider Normalize(ISampleProvider source){if(source.WaveFormat.Channels==1)source=new MonoToStereoSampleProvider(source){LeftVolume=1,RightVolume=1};if(source.WaveFormat.Channels!=2)throw new NotSupportedException("音效需使用单声道或双声道音频。");return source.WaveFormat.SampleRate==48000?source:new WdlResamplingSampleProvider(source,48000);}
 public void SetClip(ISampleProvider source,IDisposable? resource,float volume){try{source=Normalize(source);}catch{resource?.Dispose();throw;}lock(gate){StopClip();clip=source;owner=resource;clipGain=Math.Clamp(volume,0,1);tailUntil=0;}}
 public void PlayFile(string path,int volume){var reader=new AudioFileReader(path);SetClip(reader,reader,volume/100f);}
 public void StopClip(){lock(gate){clip=null;tailUntil=0;owner?.Dispose();owner=null;}}
 public int Read(float[] buffer,int offset,int count){if(microphoneScratch.Length<count)microphoneScratch=new float[count];int got=microphone.Read(microphoneScratch,0,count);float gain=micGain;for(int i=0;i<count;i++)buffer[offset+i]=i<got?microphoneScratch[i]*gain:0;
  lock(gate){if(clip!=null){if(scratch.Length<count)scratch=new float[count];int n=clip.Read(scratch,0,count);for(int i=0;i<n;i++)buffer[offset+i]+=scratch[i]*clipGain;if(n<count){clip=null;owner?.Dispose();owner=null;tailUntil=Environment.TickCount64+150;}}}
  float master=sendGain,peak=0;for(int i=0;i<count;i++){float x=buffer[offset+i]*master;buffer[offset+i]=float.IsFinite(x)?x:0;peak=Math.Max(peak,Math.Abs(buffer[offset+i]));}float limiter=peak>.98f?.98f/peak:1;for(int i=0;i<count;i++)buffer[offset+i]*=limiter;Volatile.Write(ref outputPeak,peak*limiter);return count;
 }
 public void Dispose()=>StopClip();
}

public sealed class VoiceMicrophoneMixer : IDisposable {
 private WasapiCapture? capture;private WasapiOut? output;private MMDevice? inputDevice,outputDevice;private BufferedWaveProvider? buffer;private CaptureSampleDecoder? decoder;private LiveVoiceMix? mix;private readonly object captureGate=new();private float peak;private bool closing;
 public bool IsRunning=>mix!=null;public bool IsPlaying=>mix?.IsPlaying==true;public float Peak=>Volatile.Read(ref peak);public event Action<int,string>? Faulted;private int generation;public int Generation=>generation;
 public float MicrophoneGain {set{if(mix!=null)mix.MicrophoneGain=value;}}
 public float EffectGain {set{if(mix!=null)mix.EffectGain=value;}}
 public float TransmitGain {set{if(mix!=null)mix.TransmitGain=value;}}
 public float OutputPeak=>mix?.OutputPeak??0;
 public static List<AudioDevice> Inputs(){using var e=new MMDeviceEnumerator();var list=new List<AudioDevice>();foreach(var d in e.EnumerateAudioEndPoints(DataFlow.Capture,DeviceState.Active)){list.Add(new(d.ID,d.FriendlyName));d.Dispose();}return list;}
 public static bool IsPhysicalMicrophone(string name)=>!name.Contains("VB-Audio",StringComparison.OrdinalIgnoreCase)&&!name.Contains("CABLE",StringComparison.OrdinalIgnoreCase)&&!name.Contains("VoiceMeeter",StringComparison.OrdinalIgnoreCase);
 public static bool IsVirtualOutput(string name)=>name.Contains("CABLE Input",StringComparison.OrdinalIgnoreCase)||name.Contains("VoiceMeeter Input",StringComparison.OrdinalIgnoreCase)||name.Contains("VoiceMeeter Aux Input",StringComparison.OrdinalIgnoreCase)||name.Contains("VB-Audio",StringComparison.OrdinalIgnoreCase);
 public void Start(string inputId,string outputId,float gain){Dispose();closing=false;try{using var e=new MMDeviceEnumerator();inputDevice=e.GetDevice(inputId);outputDevice=e.GetDevice(outputId);
   if(inputDevice.DataFlow!=DataFlow.Capture||outputDevice.DataFlow!=DataFlow.Render)throw new InvalidOperationException("麦克风或混音输出设备类型错误，请刷新后重选。");
   if(!IsVirtualOutput(outputDevice.FriendlyName))throw new InvalidOperationException("混音输出请选择 CABLE Input 等虚拟线路，不能选扬声器，以免麦克风回授。");
   if(!IsPhysicalMicrophone(inputDevice.FriendlyName))throw new InvalidOperationException("真人麦克风请选择实体麦克风，不能选择虚拟线路回录。");
   capture=new WasapiCapture(inputDevice);decoder=new(capture.WaveFormat);buffer=new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(capture.WaveFormat.SampleRate,1)){BufferDuration=TimeSpan.FromMilliseconds(250),DiscardOnBufferOverflow=true,ReadFully=true};
   mix=new LiveVoiceMix(buffer.ToSampleProvider()){MicrophoneGain=gain};capture.DataAvailable+=Data;capture.RecordingStopped+=CaptureStopped;
   output=new WasapiOut(outputDevice,AudioClientShareMode.Shared,false,80);output.PlaybackStopped+=OutputStopped;output.Init(new SampleToWaveProvider(mix));capture.StartRecording();output.Play();
  }catch{Dispose();throw;}}
 private void Data(object? sender,WaveInEventArgs args){try{lock(captureGate){if(closing||sender!=capture||decoder==null||buffer==null)return;var (mono,_,_)=decoder.Decode(args.Buffer,args.BytesRecorded);Volatile.Write(ref peak,mono.Length==0?0:mono.Max(v=>Math.Abs(v)));var bytes=new byte[mono.Length*4];Buffer.BlockCopy(mono,0,bytes,0,bytes.Length);if(buffer.BufferedDuration.TotalMilliseconds>180)buffer.ClearBuffer();buffer.AddSamples(bytes,0,bytes.Length);}}catch(Exception ex){if(!closing&&sender==capture)Faulted?.Invoke(generation,ex.Message);}}
 private void CaptureStopped(object? sender,StoppedEventArgs e){if(!closing&&sender==capture)Faulted?.Invoke(generation,"麦克风已中断："+(e.Exception?.Message??"设备停止"));}
 private void OutputStopped(object? sender,StoppedEventArgs e){if(!closing&&sender==output)Faulted?.Invoke(generation,"混音输出已中断："+(e.Exception?.Message??"设备停止"));}
 public void Play(string path,int volume){if(mix==null)throw new InvalidOperationException("请先开启真人麦克风混音。");mix.PlayFile(path,volume);}
 public void StopClip()=>mix?.StopClip();
 public void Dispose(){closing=true;generation++;var c=capture;capture=null;if(c!=null){c.DataAvailable-=Data;c.RecordingStopped-=CaptureStopped;try{c.StopRecording();}catch{}try{c.Dispose();}catch{}}var o=output;output=null;if(o!=null){o.PlaybackStopped-=OutputStopped;try{o.Stop();}catch{}try{o.Dispose();}catch{}}lock(captureGate){mix?.Dispose();mix=null;buffer=null;decoder=null;Volatile.Write(ref peak,0);}inputDevice?.Dispose();inputDevice=null;outputDevice?.Dispose();outputDevice=null;}
}
