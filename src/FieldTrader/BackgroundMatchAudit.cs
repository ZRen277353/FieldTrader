using System.IO;
using System.Text.Json;
namespace FieldTrader;
public static class BackgroundMatchAudit {
 public static void Run(string bulk,string backgroundFile,string output){
  Directory.CreateDirectory(output);var db=new CatalogStore(Path.Combine(output,"database"));db.Load();SeedCatalog.Merge(db);var engine=new TemplateRecognitionService();engine.Load(db.Data,db.Root);
  var bg=AudioFiles.Read(backgroundFile);var records=JsonSerializer.Deserialize<List<ExportRecording>>(File.ReadAllText(Path.Combine(bulk,"export.json")))!;var rows=new List<object>();
  foreach(var r in records.Where(r=>r.Clips.Count==8))foreach(var index in new[]{4,6}){
   var original=FirstSound.Window(AudioFiles.Read(Path.Combine(bulk,"clips",r.Clips[index].File)));var clean=SoundFeaturesExtractor.Resample(original.Mono,original.SampleRate);var noise=SoundFeaturesExtractor.Resample(bg.Mono[..Math.Min(bg.Mono.Length,bg.SampleRate)],bg.SampleRate);
   var expected=db.Data.Items.First(i=>i.SourceName==r.Name).SoundGroupId;
   foreach(var snr in new[]{30d,20d,10d}){double signalRms=Math.Sqrt(clean.Average(v=>(double)v*v));var n=Enumerable.Range(0,clean.Length).Select(i=>noise[(i+index*127)%noise.Length]).ToArray();double noiseRms=Math.Sqrt(n.Average(v=>(double)v*v));double scale=signalRms/(Math.Max(1e-12,noiseRms)*Math.Pow(10,snr/20));var mixed=clean.Select((v,i)=>(float)(v+n[i]*scale)).ToArray();var result=engine.Compare(new(mixed,16000,DateTimeOffset.Now,noise.Take(6400).Select(v=>(float)(v*scale)).ToArray()),db.Data);
    var stream=new float[8000+mixed.Length+4800];for(int j=0;j<stream.Length;j++)stream[j]=(float)(noise[j%noise.Length]*scale);Array.Copy(mixed,0,stream,8000,mixed.Length);var detector=new EnergyEventDetector(true);var decisions=new List<MatchDecision>();
    for(int j=0;j<stream.Length;j+=320)foreach(var frame in detector.Push(new(stream[j..Math.Min(stream.Length,j+320)],16000,DateTimeOffset.UnixEpoch.AddSeconds((j+320)/16000d))))decisions.Add(engine.Compare(frame,db.Data));
    string streamStatus=decisions.Any(d=>d.Result!=null&&d.Result.SoundGroupId!=expected)?"wrong":decisions.Any(d=>d.Result?.SoundGroupId==expected)?"correct":"rejected";
    rows.Add(new{Name=r.Name,SNR=snr,Status=result.Result==null?"rejected":result.Result.SoundGroupId==expected?"correct":"wrong",StreamStatus=streamStatus,Predicted=result.Result?.Candidates.Select(c=>c.Item.Name),Distance=result.Ranking.FirstOrDefault()?.Distance});
   }
  }
  File.WriteAllText(Path.Combine(output,"mix-audit.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
  var shorter=new List<object>();foreach(var r in records.Where(r=>r.Clips.Count==8))foreach(int index in new[]{4,6}){var clip=FirstSound.Window(AudioFiles.Read(Path.Combine(bulk,"clips",r.Clips[index].File)));var expected=db.Data.Items.First(i=>i.SourceName==r.Name).SoundGroupId;foreach(double duration in new[]{.12,.16,.20}){var result=engine.Compare(clip with{Mono=clip.Mono.Take((int)(clip.SampleRate*duration)).ToArray()},db.Data);shorter.Add(new{Name=r.Name,Duration=duration,Status=result.Result==null?"rejected":result.Result.SoundGroupId==expected?"correct":"wrong"});}}
  File.WriteAllText(Path.Combine(output,"short-prefix.json"),JsonSerializer.Serialize(shorter,new JsonSerializerOptions{WriteIndented=true}));
  int falseAccepts=0;for(int k=0;k<100;k++){var random=new Random(1000+k);var noise=Enumerable.Range(0,6400).Select(_=>(float)((random.NextDouble()*2-1)*.008)).ToArray();var probe=Enumerable.Range(0,4480).Select(i=>(float)((random.NextDouble()*2-1)*.008+(k%2==0?.01*Math.Sin(2*Math.PI*(100+k*50)*i/16000):0))).ToArray();if(engine.Compare(new(probe,16000,DateTimeOffset.Now,noise),db.Data).Result!=null)falseAccepts++;}
  File.WriteAllText(Path.Combine(output,"negatives.json"),JsonSerializer.Serialize(new{Cases=100,FalseAccepts=falseAccepts}));
 }
}
