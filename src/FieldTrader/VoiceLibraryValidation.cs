using System.IO;
using System.Text.Json;
namespace FieldTrader;
public static class VoiceLibraryValidation {
 public static void Run(string directory){
  Directory.CreateDirectory(directory);var root=Path.Combine(directory,"library-db");Directory.CreateDirectory(root);var settings=VoiceBoardSettings.Load(root,out _);
  var supplied=settings.Phrases.Where(p=>p.Id.StartsWith("qq20261015-")).ToArray();if(supplied.Length!=8||settings.Phrases.Count!=16||supplied.Any(p=>string.IsNullOrWhiteSpace(p.Note)))throw new Exception("8段录音或中文备注缺失");
  var rows=new List<object>();using var audio=new VoiceBoardAudio();
  foreach(var p in supplied){var file=Path.Combine(root,p.Wav);var frame=AudioFiles.Read(file);double rms=Math.Sqrt(frame.Mono.Average(v=>(double)v*v)),peak=frame.Mono.Max(v=>Math.Abs(v));if(peak<.2||peak>.99||rms<.025)throw new Exception("录音音量或峰值异常："+p.Title);
   string target=Path.GetFullPath(Path.Combine(directory,p.Id+"-played.wav"));audio.RenderWavTest(Path.GetFullPath(file),target);var played=AudioFiles.Read(target);if(!played.Mono.Any(v=>Math.Abs(v)>.0001))throw new Exception("录音播放流静音");
   rows.Add(new{p.Id,p.Title,p.Note,Seconds=frame.Mono.Length/(double)frame.SampleRate,Peak=peak,RmsDb=20*Math.Log10(rms),PlaybackStreamPassed=true});
  }
  var first=supplied[0];first.Note="用户修改的中文备注";settings.Volume=45;settings.Save(root);var again=VoiceBoardSettings.Load(root,out _);if(again.Phrases.Count!=16||again.Volume!=45||again.Phrases.Single(p=>p.Id==first.Id).Note!="用户修改的中文备注")throw new Exception("升级重复导入或覆盖用户配置");
  again.Phrases.RemoveAll(p=>p.Id==first.Id);again.Save(root);if(VoiceBoardSettings.Load(root,out _).Phrases.Any(p=>p.Id==first.Id))throw new Exception("删除的预置录音被强行恢复");
  File.WriteAllText(Path.Combine(directory,"recording-validation.json"),JsonSerializer.Serialize(new{Passed=true,NoDuplicates=true,UserEditsPreserved=true,UserDeletionsPreserved=true,Rows=rows},new JsonSerializerOptions{WriteIndented=true}));
 }
}
