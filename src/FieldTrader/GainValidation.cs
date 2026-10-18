using System.IO;
using System.Text.Json;
namespace FieldTrader;
public static class GainValidation {
 public static void Run(string root){
  Directory.CreateDirectory(root);var seed=Path.Combine(AppContext.BaseDirectory,"SeedData");var catalog=JsonSerializer.Deserialize<Catalog>(File.ReadAllText(Path.Combine(seed,"catalog.json")))!;var engine=new WaveformRecognitionService();engine.Load(catalog,seed);
  var filtered=new Catalog{Items=catalog.Items.Where(i=>i.GridWidth==2&&i.GridHeight==3).ToList()};var groups=filtered.Items.Select(i=>i.SoundGroupId).ToHashSet();filtered.SoundGroups=catalog.SoundGroups.Where(g=>groups.Contains(g.Id)).ToList();filtered.SoundSamples=catalog.SoundSamples.Where(s=>groups.Contains(s.SoundGroupId)).ToList();var luteEngine=new WaveformRecognitionService();luteEngine.Load(filtered,seed);
  var rows=new List<object>();var original=AudioFiles.Read("work/items1008/pickup-test.wav");
  List<AudioFrame> Detect(AudioFrame audio,double scale,double gain){var detector=new PickupEventDetector();var found=new List<AudioFrame>();int step=audio.SampleRate/100;for(int i=0;i<audio.Mono.Length;i+=step){var frame=new AudioFrame(audio.Mono[i..Math.Min(i+step,audio.Mono.Length)].Select(v=>(float)(v*scale)).ToArray(),audio.SampleRate,DateTimeOffset.UnixEpoch.AddSeconds(i/(double)audio.SampleRate));found.AddRange(detector.Push(RecognitionGain.Apply(frame,gain)));}return found;}
  foreach(double scale in new[]{1d,.1,.01}){var frames=Detect(original,scale,4);var decisions=frames.Select(f=>luteEngine.Compare(f,filtered)).ToArray();bool ok=decisions.Any(d=>d.Result?.SoundGroupId=="sound-double-neck-lute-20261008");rows.Add(new{Case="琴拿起声回放",Scale=scale,Gain=4,Detected=frames.Count,Correct=ok});if(!ok)throw new Exception("琴的小声回放未匹配："+scale);}
  bool improved=false;for(int k=0;k<30;k++){double scale=Math.Pow(.75,k)*.01;int plain=Detect(original,scale,1).Count,boosted=Detect(original,scale,4).Count;if(plain==0&&boosted>0){rows.Add(new{Case="极弱声音触发验证（合成缩小，不代表实战准确率）",Scale=scale,WithoutGain=plain,WithGain=boosted});improved=true;break;}}if(!improved)throw new Exception("未验证增益触发改善");
  var sample=AudioFiles.Read(Path.Combine(seed,"samples/lute-pickup-20261008.wav"));var normal=engine.Compare(sample,catalog);var amplified=engine.Compare(RecognitionGain.Apply(sample,4),catalog);if(Math.Abs(normal.Ranking[0].Score-amplified.Ranking[0].Score)>.001)throw new Exception("增益改变了归一化相似度");
  var x=new AudioFrame([.8f,-.8f],16000,DateTimeOffset.Now);var amplifiedX=RecognitionGain.Apply(x,4);if(Math.Abs(amplifiedX.Mono[0]-3.2)>1e-5||x.Mono[0]!=.8f)throw new Exception("增益削顶或修改原始缓冲");
  var outdoor=Detect(AudioFiles.Read("work/outdoor0927/audio.wav"),1,4);var results=outdoor.Select(f=>engine.Compare(f,catalog)).ToArray();if(results.Length!=4||results.Any(d=>d.Result==null))throw new Exception("户外增益回归失败");
  rows.Add(new{Case="户外回放4倍增益",Detected=results.Length,Groups=results.Select(d=>d.Result?.SoundGroupId)});
  if(Detect(new(new float[48000],16000,DateTimeOffset.Now),1,8).Count!=0)throw new Exception("静音触发");
  File.WriteAllText(Path.Combine(root,"gain-tests.json"),JsonSerializer.Serialize(new{Passed=true,engine.TemplateCount,engine.LoadErrors,GainDoesNotInflateSimilarity=true,NoClipping=true,OriginalAudioUnchanged=true,SilenceRejected=true,Rows=rows},new JsonSerializerOptions{WriteIndented=true}));
 }
}
