using System.IO;
using System.Text.Json;
namespace FieldTrader;
public static class OutdoorValidation {
 public static void Run(string root){
  var seed=Path.Combine(AppContext.BaseDirectory,"SeedData");var catalog=JsonSerializer.Deserialize<Catalog>(File.ReadAllText(Path.Combine(seed,"catalog.json")))!;
  var engine=new WaveformRecognitionService();engine.Load(catalog,seed);var rows=new List<object>();
  using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"cases.json")));
  foreach(var entry in manifest.RootElement.EnumerateArray()){
   var audio=AudioFiles.Read(Path.Combine(root,entry.GetProperty("File").GetString()!));
   var bg=AudioFiles.Read(Path.Combine(root,entry.GetProperty("Background").GetString()!));
   if(audio.SampleRate!=bg.SampleRate)throw new InvalidDataException("测试采样率不一致");
   var decision=engine.Compare(audio with{BackgroundMono=bg.Mono},catalog);
   var expected=entry.GetProperty("Expected").GetString();
   rows.Add(new{Name=entry.GetProperty("Name").GetString(),Expected=expected,Predicted=decision.Result?.SoundGroupId,Passed=decision.Result?.SoundGroupId==expected,decision.Reason,Ranking=decision.Ranking.Take(2)});
  }
  File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new{engine.TemplateCount,engine.LoadErrors,Rows=rows},new JsonSerializerOptions{WriteIndented=true}));
 }
}
