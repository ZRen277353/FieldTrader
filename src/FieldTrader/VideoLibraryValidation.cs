using System.IO;
using System.Text.Json;
namespace FieldTrader;
public static class VideoLibraryValidation {
 public static void Run(string root){var store=new CatalogStore(Path.Combine(root,"validation-db"));store.Load();SeedCatalog.Merge(store);var engine=new WaveformRecognitionService();engine.Load(store.Data,store.Root);using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"manifest.json")));var rows=new List<object>();
 foreach(var clip in manifest.RootElement.GetProperty("Clips").EnumerateArray()){if(clip.GetProperty("Training").GetBoolean())continue;string file=clip.GetProperty("File").GetString()!,expected=clip.GetProperty("Group").GetString()!;if(clip.GetProperty("End").GetDouble()>49)throw new Exception("越过49秒边界");var d=engine.Compare(AudioFiles.Read(Path.Combine(root,file)),store.Data);rows.Add(new{File=file,Expected=expected,Accepted=d.Result!=null,Correct=d.Result?.SoundGroupId==expected,Predicted=d.Result?.SoundGroupId,d.Reason,Ranking=d.Ranking.Take(3)});}
 File.WriteAllText(Path.Combine(root,"样本验证.json"),JsonSerializer.Serialize(new{Threshold=WaveformRecognitionService.Threshold,engine.TemplateCount,engine.LoadErrors,Rows=rows},new JsonSerializerOptions{WriteIndented=true}));}
}
