using System.IO;
using System.Text.Json;
using NAudio.Wave;
namespace FieldTrader;

// Offline, unlabeled recording review. Never imports predicted labels into the library.
public static class PairedRecordingAudit {
 public sealed record Window(double Peak,double Start,double End);
 public static void Run(string audioPath,string windowsPath,string directory) {
  Directory.CreateDirectory(directory);
  var audio=AudioFiles.Read(audioPath);
  var windows=JsonSerializer.Deserialize<Window[]>(File.ReadAllText(windowsPath))!;
  var store=new CatalogStore();store.Load();
  var engine=new TemplateRecognitionService();engine.Load(store.Data,store.Root);
  var references=FirstSound.Templates(store.Data).Select(s=>new {Sample=s,Features=SoundFeaturesExtractor.Extract(FirstSound.Window(AudioFiles.Read(Path.Combine(store.Root,s.FilePath))))}).Where(r=>r.Features!=null).ToArray();
  SoundFeatures? previous=null;
  var rows=new List<object>();
  for(int i=0;i<windows.Length;i++) {
   var w=windows[i];int start=(int)(w.Start*audio.SampleRate),end=Math.Min(audio.Mono.Length,(int)(w.End*audio.SampleRate));
   if(start<0||end<=start)throw new InvalidDataException("Invalid audio window");
   var bg=audio.Mono[Math.Max(0,start-(int)(audio.SampleRate*.45))..Math.Max(0,start-(int)(audio.SampleRate*.05))];
   var frame=new AudioFrame(audio.Mono[start..end],audio.SampleRate,DateTimeOffset.UnixEpoch.AddSeconds(w.Start),bg);
   string file=$"group-{i/2+1:00}-{i%2+1}.wav";
   using(var writer=new WaveFileWriter(Path.Combine(directory,file),new WaveFormat(audio.SampleRate,16,1)))writer.WriteSamples(frame.Mono,0,frame.Mono.Length);
   var d=engine.Compare(frame,store.Data);var raw=engine.Compare(frame with {BackgroundMono=null},store.Data);
   var feature=SoundFeaturesExtractor.Extract(FirstSound.Window(frame));
   var referenceRanking=feature==null?null:references.Select(r=>new{r.Sample.SoundGroupId,r.Sample.SourceRecording,r.Sample.FilePath,Score=Math.Exp(-4*SoundFeaturesExtractor.PrefixDistance(feature,r.Features!))}).OrderByDescending(r=>r.Score).ToArray();
   double? repeatScore=i%2==1&&previous!=null&&feature!=null?Math.Exp(-4*SoundFeaturesExtractor.Distance(previous,feature)):null;
   previous=feature;
   rows.Add(new{Index=i+1,Pair=i/2+1,w.Start,w.End,w.Peak,File=file,Accepted=d.Result!=null,d.Reason,Ranking=d.Ranking,RawRanking=raw.Ranking,ReferenceRanking=referenceRanking,RepeatScore=repeatScore});
  }
  File.WriteAllText(Path.Combine(directory,"matches.json"),JsonSerializer.Serialize(new{Source=audioPath,Templates=engine.TemplateCount,engine.LoadErrors,Threshold=.60,ConfirmedLabels=false,Groups=store.Data.SoundGroups,Items=store.Data.Items,Rows=rows},new JsonSerializerOptions{WriteIndented=true}));
 }
}
