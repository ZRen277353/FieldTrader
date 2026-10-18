using System.IO;
using System.Text.Json;
namespace FieldTrader;
public static class LibraryCoverageAudit {
 public static void Run(string output) {
  var root=Path.Combine(AppContext.BaseDirectory,"SeedData");
  var catalog=JsonSerializer.Deserialize<Catalog>(File.ReadAllText(Path.Combine(root,"catalog.json")))!;
  var engine=new WaveformRecognitionService();engine.Load(catalog,root);
  var rows=new List<object>();
  foreach(var sample in FirstSound.Templates(catalog)) {
   var a=AudioFiles.Read(Path.Combine(root,sample.FilePath));
   var detector=new PickupEventDetector();var events=new List<AudioFrame>();
   // One second of silence before/after a known pickup reproduces the live path.
   var data=new float[a.SampleRate*2+a.Mono.Length];a.Mono.CopyTo(data,a.SampleRate);
   for(int i=0;i<data.Length;i+=a.SampleRate/100)events.AddRange(detector.Push(new(data[i..Math.Min(data.Length,i+a.SampleRate/100)],a.SampleRate,DateTimeOffset.UnixEpoch)));
   var direct=engine.Compare(a,catalog);
   var live=events.Count>0?engine.Compare(events[0],catalog):null;
   rows.Add(new {sample.SourceRecording,sample.FilePath,sample.SoundGroupId,Duration=a.Mono.Length/(double)a.SampleRate,Detected=events.Count,Direct=direct.Result?.SoundGroupId,DirectReason=direct.Reason,Ranking=direct.Ranking.Take(2),Live=live?.Result?.SoundGroupId,LiveReason=live?.Reason});
  }
  File.WriteAllText(output,JsonSerializer.Serialize(new{engine.TemplateCount,engine.LoadErrors,Rows=rows},new JsonSerializerOptions{WriteIndented=true}));
 }
}
