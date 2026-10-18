using System.IO;
using NAudio.Wave;
using System.Text.Json;
namespace FieldTrader;
public static class FirstSoundValidation {
 public static async Task Loopback(string bulkDirectory,string reportDirectory) {
  Directory.CreateDirectory(reportDirectory);
  var db=new CatalogStore(Path.Combine(reportDirectory,"loopback-database"));db.Load();SeedCatalog.Merge(db);
  using var state=new SessionState(db,System.Windows.Application.Current.Dispatcher);
  if(state.SelectedDevice==null)throw new InvalidOperationException("未找到输出设备");
  var results=new List<object>();state.Start();await Task.Delay(2500);
  try {
   using var enumerator=new NAudio.CoreAudioApi.MMDeviceEnumerator();using var endpoint=enumerator.GetDevice(state.SelectedDevice.Id);
   var records=JsonSerializer.Deserialize<List<ExportRecording>>(File.ReadAllText(Path.Combine(bulkDirectory,"export.json")))!;
   foreach(string name in new[]{"金狮雕像","古董茶壶","显卡"}) {
    var clip=records.First(r=>r.Name==name).Clips[4];var prefix=FirstSound.Window(AudioFiles.Read(Path.Combine(bulkDirectory,"clips",clip.File)));
    string file=Path.Combine(reportDirectory,name+"-首段.wav");
    using(var writer=new NAudio.Wave.WaveFileWriter(file,new NAudio.Wave.WaveFormat(prefix.SampleRate,16,1)))writer.WriteSamples(prefix.Mono,0,prefix.Mono.Length);
    int before=state.History.Count;
    using var reader=new NAudio.Wave.AudioFileReader(file){Volume=.12f};using var player=new NAudio.Wave.WasapiOut(endpoint,NAudio.CoreAudioApi.AudioClientShareMode.Shared,false,50);
    player.Init(new NAudio.Wave.SampleProviders.MonoToStereoSampleProvider(reader));player.Play();await Task.Delay(1300);
    bool passed=state.History.Count>before&&state.Candidates.Any(c=>c.Name==name);
    results.Add(new{Name=name,Passed=passed,Milliseconds=prefix.Mono.Length*1000/prefix.SampleRate,Candidates=state.Candidates.Select(c=>c.Name).ToArray(),state.RecognitionStatus});
   }
  }finally{await state.StopAsync();}
  File.WriteAllText(Path.Combine(reportDirectory,"loopback.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
 }
 public static void Run(string bulkDirectory,string reportDirectory) {
  Directory.CreateDirectory(reportDirectory);
  var db=new CatalogStore(Path.Combine(reportDirectory,"database"));db.Load();SeedCatalog.Merge(db);
  var engine=new TemplateRecognitionService();engine.Load(db.Data,db.Root);
  var recordings=JsonSerializer.Deserialize<List<ExportRecording>>(File.ReadAllText(Path.Combine(bulkDirectory,"export.json")))!;
  var rows=new List<object>();int correct=0,wrong=0,rejected=0,streamCorrect=0,total=0,invariant=0;
  foreach(var recording in recordings){
   // Standard recordings have four pickup/drop pairs. Merged four-event
   // recordings each begin with pickup. Irregular recordings are excluded
   // from phase-labelled accuracy rather than guessing their action labels.
   var indices=recording.Clips.Count==8?new[]{2,4,6}:recording.Clips.Count==4?new[]{1,2,3}:Array.Empty<int>();
   var expected=db.Data.Items.First(i=>i.SourceName==recording.Name).SoundGroupId;
   foreach(int index in indices){var clip=recording.Clips[index];var raw=AudioFiles.Read(Path.Combine(bulkDirectory,"clips",clip.File));
    var prefix=FirstSound.Window(raw);var decision=engine.Compare(prefix,db.Data);total++;
    string status=decision.Result==null?"rejected":decision.Result.SoundGroupId==expected?"correct":"wrong";
    if(status=="correct")correct++;else if(status=="wrong")wrong++;else rejected++;
    // Remove everything following the onset, then replay in live-sized chunks.
    var input=new float[prefix.Mono.Length+raw.SampleRate/2];Array.Copy(prefix.Mono,0,input,raw.SampleRate/10,prefix.Mono.Length);
    var detector=new EnergyEventDetector(firstSoundOnly:true);var results=new List<MatchDecision>();
    int step=Math.Max(1,raw.SampleRate/50);for(int offset=0;offset<input.Length;offset+=step)foreach(var segment in detector.Push(new(input[offset..Math.Min(input.Length,offset+step)],raw.SampleRate,DateTimeOffset.Now)))results.Add(engine.Compare(segment,db.Data));
    if(results.Any(d=>d.Result?.SoundGroupId==expected)&&results.All(d=>d.Result==null||d.Result.SoundGroupId==expected))streamCorrect++;
    var tail=new float[raw.SampleRate];Array.Copy(prefix.Mono,tail,prefix.Mono.Length);for(int i=raw.SampleRate/2;i<tail.Length;i++)tail[i]=(float)(.8*Math.Sin(i*.4));
    var changed=engine.Compare(new(tail,raw.SampleRate,raw.Timestamp),db.Data);
    if(changed.Result?.SoundGroupId==decision.Result?.SoundGroupId&&changed.Ranking.Count==decision.Ranking.Count&&changed.Ranking.Zip(decision.Ranking).All(p=>Math.Abs(p.First.Distance-p.Second.Distance)<1e-8))invariant++;
    rows.Add(new{recording.Name,index,Status=status,Stream=results.Select(d=>new{Group=d.Result?.SoundGroupId,d.Reason}),Best=decision.Ranking.Take(3),Expected=expected,decision.Reason});
   }
  }
  int falseAccept=0;for(int k=0;k<40;k++){var random=new Random(k);var noise=Enumerable.Range(0,8000).Select(_=>(float)((random.NextDouble()*2-1)*.03)).ToArray();if(engine.Compare(new(noise,16000,DateTimeOffset.Now),db.Data).Result!=null)falseAccept++;}
  var summary=new{Templates=engine.TemplateCount,engine.LoadErrors,Total=total,Correct=correct,Wrong=wrong,Rejected=rejected,StreamCorrect=streamCorrect,TailIndependent=invariant,NoiseFalseAccepts=falseAccept,ExcludedRecordings=recordings.Where(r=>r.Clips.Count!=8&&r.Clips.Count!=4).Select(r=>r.Name),Note="仅旧录音首段重放，不是游戏更新后的实测；标准8段按拿起/放下交替推定。"};
  File.WriteAllText(Path.Combine(reportDirectory,"summary.json"),JsonSerializer.Serialize(summary,new JsonSerializerOptions{WriteIndented=true}));
  File.WriteAllText(Path.Combine(reportDirectory,"results.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
  if(invariant!=total||wrong>0||engine.LoadErrors.Count>0)throw new InvalidDataException("首段回归检查失败，请查看报告");
 }
}
