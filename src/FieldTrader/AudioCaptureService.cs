using NAudio.CoreAudioApi;
using NAudio.Wave;
namespace FieldTrader;
public sealed record AudioDevice(string Id,string Name);
public sealed class AudioRingBuffer {
 private readonly float[] data; private int cursor,count; private readonly object gate=new();
 public AudioRingBuffer(int capacity) { if(capacity<=0) throw new ArgumentOutOfRangeException(nameof(capacity)); data=new float[capacity]; }
 public void Write(float[] input) { lock(gate) foreach(var v in input) { data[cursor]=v; cursor=(cursor+1)%data.Length; count=Math.Min(count+1,data.Length); } }
 public float[] Snapshot(int maxSamples=int.MaxValue) { lock(gate) { int length=Math.Min(count,maxSamples); var result=new float[length]; for(int i=0;i<length;i++) result[i]=data[(cursor-length+i+data.Length)%data.Length]; return result; } }
}
public sealed class AudioCaptureService : IDisposable {
 private CaptureSampleDecoder? decoder;
 public string LastDiagnostic {get;private set;}="尚未尝试启动监听";
 private WasapiLoopbackCapture? capture; private MMDevice? device; private TaskCompletionSource? stopped;
 public AudioRingBuffer Buffer { get; private set; } = new(48000*10);
 public AudioRingBuffer RawBuffer { get; private set; } = new(48000*2*10);
 public int Channels { get; private set; }=2;
 public event Action<AudioFrame>? FrameAvailable;
 public event Action<Exception?>? CaptureStopped;
 public bool IsRunning => capture!=null;
 public int SampleRate { get; private set; }=48000;
 private float[] channelPeaks=[];
 public float[] ChannelPeaks=>Volatile.Read(ref channelPeaks);
 public static List<AudioDevice> EnumerateDevices() { using var e=new MMDeviceEnumerator(); var result=new List<AudioDevice>(); foreach(var d in e.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active)) { result.Add(new(d.ID,d.FriendlyName)); d.Dispose(); } return result; }
 public void Start(string id) {
  if(capture!=null) return;
  LastDiagnostic=$"启动时间：{DateTimeOffset.Now:O}\n设备 ID：{id}";
  try {
   using var enumerator=new MMDeviceEnumerator(); device=enumerator.GetDevice(id);
   LastDiagnostic+="\n设备名称："+device.FriendlyName;
   capture=new WasapiLoopbackCapture(device); // Keep native MixFormat, including extensible channel information.
   var format=capture.WaveFormat;LastDiagnostic+=$"\n原生格式：{format}\n采样率：{format.SampleRate}，声道：{format.Channels}，位数：{format.BitsPerSample}，帧字节：{format.BlockAlign}\n模式：共享 WASAPI Loopback，不强制改格式";decoder=new(format);
   SampleRate=capture.WaveFormat.SampleRate; Channels=capture.WaveFormat.Channels; Buffer=new(SampleRate*10); RawBuffer=new(SampleRate*Channels*10); channelPeaks=new float[Channels]; stopped=new(TaskCreationOptions.RunContinuationsAsynchronously);
   capture.DataAvailable+=OnData; capture.RecordingStopped+=OnStopped; capture.StartRecording();LastDiagnostic+="\n启动请求成功";
  } catch(Exception ex) { LastDiagnostic+=$"\n启动失败 HRESULT=0x{ex.HResult:X8}\n{ex}";var old=capture;capture=null;if(old!=null){old.DataAvailable-=OnData;old.RecordingStopped-=OnStopped;old.Dispose();}device?.Dispose();device=null;decoder=null;throw; }
 }
 private void OnData(object? sender,WaveInEventArgs e) {
  if(!ReferenceEquals(sender,capture)||decoder==null)return;
  var (mono,raw,peaks)=decoder.Decode(e.Buffer,e.BytesRecorded);
  RawBuffer.Write(raw);Volatile.Write(ref channelPeaks,peaks);
  Buffer.Write(mono);FrameAvailable?.Invoke(new(mono,SampleRate,DateTimeOffset.Now));
 }
 private void OnStopped(object? sender,StoppedEventArgs e) { if(!ReferenceEquals(sender,capture))return;LastDiagnostic+=e.Exception==null?"\n监听已停止":$"\n采集中断 HRESULT=0x{e.Exception.HResult:X8}\n{e.Exception}";var old=capture; capture=null;decoder=null; if(old!=null) { old.DataAvailable-=OnData; old.RecordingStopped-=OnStopped; old.Dispose(); } device?.Dispose(); device=null; stopped?.TrySetResult(); CaptureStopped?.Invoke(e.Exception); }
 public async Task StopAsync() { var c=capture; if(c==null) return; var completion=stopped!.Task; c.StopRecording(); await completion; }
 public void Dispose() { var old=capture;capture=null;if(old!=null){old.DataAvailable-=OnData;old.RecordingStopped-=OnStopped;old.Dispose();}decoder=null;device?.Dispose();device=null;stopped?.TrySetResult(); }
}

