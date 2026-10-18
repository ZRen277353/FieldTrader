using System.IO;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;
namespace FieldTrader;
public static class LiveAudioDiagnostic {
 public static async Task Capture(string directory) {
  Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"routing-before.txt"),AudioRouting.Report());
  using var enumerator=new MMDeviceEnumerator();using var device=enumerator.GetDevice(AudioRouting.RecommendedId()??throw new InvalidOperationException("没有输出设备"));
  using var capture=new WasapiLoopbackCapture(device);capture.WaveFormat=WaveFormat.CreateIeeeFloatWaveFormat(capture.WaveFormat.SampleRate,capture.WaveFormat.Channels);
  var rows=new List<object>();var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);Exception? error=null;
  using(var writer=new WaveFileWriter(Path.Combine(directory,"原始声道.wav"),capture.WaveFormat)){
   int channels=capture.WaveFormat.Channels;long frames=0;
   capture.DataAvailable+=(_,e)=>{writer.Write(e.Buffer,0,e.BytesRecorded);int count=e.BytesRecorded/4/channels;var sums=new double[channels];var peaks=new float[channels];double mono=0;
    for(int i=0;i<count;i++){double sum=0;for(int ch=0;ch<channels;ch++){float value=BitConverter.ToSingle(e.Buffer,(i*channels+ch)*4);sum+=value;sums[ch]+=(double)value*value;peaks[ch]=Math.Max(peaks[ch],Math.Abs(value));}mono+=sum*sum/(channels*channels);}
    rows.Add(new{Seconds=frames/(double)capture.WaveFormat.SampleRate,Count=count,ChannelRms=sums.Select(s=>Math.Sqrt(s/Math.Max(1,count))).ToArray(),ChannelPeaks=peaks,MonoRms=Math.Sqrt(mono/Math.Max(1,count))});frames+=count;
   };
   capture.RecordingStopped+=(_,e)=>{error=e.Exception;done.TrySetResult();};capture.StartRecording();await Task.Delay(TimeSpan.FromSeconds(12));capture.StopRecording();await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
  }
  File.WriteAllText(Path.Combine(directory,"capture.json"),JsonSerializer.Serialize(new{Device=device.FriendlyName,DeviceId=device.ID,Rate=capture.WaveFormat.SampleRate,Channels=capture.WaveFormat.Channels,Error=error?.Message,Blocks=rows},new JsonSerializerOptions{WriteIndented=true}));
  File.WriteAllText(Path.Combine(directory,"routing-after.txt"),AudioRouting.Report());if(error!=null)throw error;
 }
}
