using System.IO;
using System.Text.Json;
using NAudio.Wave;
namespace FieldTrader;
public static class ObsDiagnostic {
 public static void Scan(string file,string directory){Directory.CreateDirectory(directory);var audio=AudioFiles.Read(file);var db=new CatalogStore(Path.Combine(directory,"database"));db.Load();SeedCatalog.Merge(db);var engine=new TemplateRecognitionService();engine.Load(db.Data,db.Root);var rows=new List<object>();foreach(var (start,end) in new[]{(2.3,2.9),(3.7,4.3),(5.2,5.8),(6.5,7.1),(8.2,8.9),(9.5,10.1),(10.8,11.4)}){var ranked=new List<(double Time,MatchDecision Decision)>();for(double t=start;t<end;t+=.01){int a=(int)(t*audio.SampleRate),b=Math.Min(audio.Mono.Length,a+(int)(audio.SampleRate*.28));var d=engine.Compare(new(audio.Mono[a..b],audio.SampleRate,audio.Timestamp),db.Data);ranked.Add((t,d));}rows.Add(new{Start=start,Best=ranked.Where(r=>r.Decision.Ranking.Count>0).OrderBy(r=>r.Decision.Ranking[0].Distance).Take(3).Select(r=>new{r.Time,Group=db.Data.SoundGroups.First(g=>g.Id==r.Decision.Ranking[0].GroupId).Name,r.Decision.Ranking[0].Distance,Accepted=r.Decision.Result!=null})});}File.WriteAllText(Path.Combine(directory,"scan.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));}
 public static void Run(string file,string directory){
  Directory.CreateDirectory(directory);using var reader=new AudioFileReader(file);int channels=reader.WaveFormat.Channels,rate=reader.WaveFormat.SampleRate;
  var samples=new List<float>();var buffer=new float[rate*channels];int count;while((count=reader.Read(buffer,0,buffer.Length))>0){samples.AddRange(buffer.Take(count));if(samples.Count>rate*channels*600)throw new InvalidDataException("录音超过10分钟");}
  var raw=samples.ToArray();using(var output=new WaveFileWriter(Path.Combine(directory,"decoded.wav"),reader.WaveFormat))output.WriteSamples(raw,0,raw.Length);
  var db=new CatalogStore(Path.Combine(directory,"database"));db.Load();SeedCatalog.Merge(db);var engine=new TemplateRecognitionService();engine.Load(db.Data,db.Root);
  var reports=new List<object>();for(int ch=-1;ch<channels;ch++){
   var mono=new float[raw.Length/channels];for(int i=0;i<mono.Length;i++)mono[i]=ch<0?Enumerable.Range(0,channels).Sum(c=>raw[i*channels+c])/channels:raw[i*channels+ch];
   var detector=new EnergyEventDetector(true);var events=new List<object>();int step=rate/100;
   for(int i=0;i<mono.Length+rate/2;i+=step){var chunk=i<mono.Length?mono[i..Math.Min(mono.Length,i+step)]:new float[step];foreach(var frame in detector.Push(new(chunk,rate,DateTimeOffset.UnixEpoch.AddSeconds((i+step)/(double)rate)))){var result=engine.Compare(frame,db.Data);events.Add(new{Time=(frame.Timestamp-DateTimeOffset.UnixEpoch).TotalSeconds,Duration=frame.Mono.Length/(double)rate,Accepted=result.Result!=null,Candidates=result.Result?.Candidates.Select(c=>c.Item.Name),result.Reason,Ranking=result.Ranking.Take(3).Select(g=>new{Group=db.Data.SoundGroups.First(x=>x.Id==g.GroupId).Name,g.Distance})});}}
   var levels=new List<object>();for(int i=0;i<mono.Length;i+=rate){var part=mono[i..Math.Min(i+rate,mono.Length)];levels.Add(new{Second=i/rate,Rms=Math.Sqrt(part.Average(v=>(double)v*v)),Peak=part.Max(v=>Math.Abs(v))});}
   reports.Add(new{Channel=ch,Events=events,Levels=levels});
  }
  File.WriteAllText(Path.Combine(directory,"analysis.json"),JsonSerializer.Serialize(new{Rate=rate,Channels=channels,Seconds=raw.Length/(double)(rate*channels),Reports=reports},new JsonSerializerOptions{WriteIndented=true}));
 }
}
