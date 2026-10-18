using System.IO;
using System.Text.Json;
namespace FieldTrader;
public static class InterferenceValidation {
 public static void Run(string root) {
  var seed=Path.Combine(AppContext.BaseDirectory,"SeedData");
  var catalog=JsonSerializer.Deserialize<Catalog>(File.ReadAllText(Path.Combine(seed,"catalog.json")))!;
  var engine=new WaveformRecognitionService();engine.Load(catalog,seed);
  var rows=new List<object>();
  void Check(string name,float[] x,string? expected=null){var d=engine.Compare(new(x,16000,DateTimeOffset.Now),catalog);rows.Add(new{Name=name,Accepted=d.Result!=null,d.Reason,Score=d.Ranking.FirstOrDefault()?.Score,Expected=expected,Predicted=d.Result?.SoundGroupId});}
  Check("静音",new float[6720]);
  foreach(int hz in new[]{100,180,240})Check($"合成持续谐波 {hz}Hz（非真人语音）",Enumerable.Range(0,6720).Select(i=>(float)(.02*Math.Sin(2*Math.PI*hz*i/16000)+.01*Math.Sin(2*Math.PI*hz*3*i/16000)+.005*Math.Sin(2*Math.PI*hz*7*i/16000))).ToArray());
  var rng=new Random(711);for(int n=0;n<8;n++)Check($"合成宽带噪声 {n+1}",Enumerable.Range(0,6720).Select(_=>(float)((rng.NextDouble()*2-1)*.02)).ToArray());
  foreach(var p in Directory.GetFiles(Path.Combine(root,"background"),"*.wav"))Check(Path.GetFileName(p),SoundFeaturesExtractor.Resample(AudioFiles.Read(p).Mono,AudioFiles.Read(p).SampleRate));
  var voiceFile=Path.Combine(root,"background","video-ending-04.wav");
  var voice=AudioFiles.Read(voiceFile).Mono;
  foreach(var sample in catalog.SoundSamples.Where(s=>s.SourceBatch=="video-20260924").GroupBy(s=>s.SoundGroupId).Select(g=>g.First())) {
   var a=AudioFiles.Read(Path.Combine(seed,sample.FilePath));var x=SoundFeaturesExtractor.Resample(a.Mono,a.SampleRate);
   double signal=x.Sum(v=>(double)v*v),noise=voice.Sum(v=>(double)v*v);double gain=Math.Sqrt(signal/Math.Max(noise,1e-20))*.316;
   var mixed=x.Select((v,i)=>(float)(v+(i<voice.Length?voice[i]*gain:0))).ToArray();
   Check("合成叠加说话声 SNR10dB "+sample.SoundGroupId,mixed,sample.SoundGroupId);
  }
  File.WriteAllText(Path.Combine(root,"interference.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
 }
}
