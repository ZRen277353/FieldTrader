using System.IO;
using System.Text.Json;
using NAudio.Wave;
namespace FieldTrader;
public static class BulkExport {
 public static void Run(string root){Directory.CreateDirectory(root);Directory.CreateDirectory(Path.Combine(root,"clips"));var files=new List<object>();foreach(var path in Directory.GetFiles(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),"*.wav").OrderBy(p=>p)){if(Path.GetFileNameWithoutExtension(path)=="Untitled")continue;var audio=AudioFiles.Read(path);var detector=new EnergyEventDetector();var clips=new List<object>();int index=0;var anchor=DateTimeOffset.UnixEpoch;int step=audio.SampleRate/100;
  for(int offset=0;offset<audio.Mono.Length+audio.SampleRate/5;offset+=step){var buffer=offset<audio.Mono.Length?audio.Mono[offset..Math.Min(offset+step,audio.Mono.Length)]:new float[step];foreach(var segment in detector.Push(new(buffer,audio.SampleRate,anchor.AddSeconds((offset+buffer.Length)/(double)audio.SampleRate)))){var feature=SoundFeaturesExtractor.Extract(segment);if(feature==null)continue;var name=$"{files.Count:D3}-{index:D2}.wav";double end=(segment.Timestamp-anchor).TotalSeconds,start=end-segment.Mono.Length/(double)segment.SampleRate;float peak=segment.Mono.Max(v=>Math.Abs(v));using(var writer=new WaveFileWriter(Path.Combine(root,"clips",name),new WaveFormat(segment.SampleRate,16,1))){var values=segment.Mono.Select(v=>v*.8f/Math.Max(peak,1e-9f)).ToArray();writer.WriteSamples(values,0,values.Length);}clips.Add(new{Index=index++,Start=Math.Round(start,3),End=Math.Round(end,3),File=name,Peak=peak,Features=feature});}}
  files.Add(new{Name=Path.GetFileNameWithoutExtension(path),Source=path,Seconds=audio.Mono.Length/(double)audio.SampleRate,Clips=clips});}
  File.WriteAllText(Path.Combine(root,"export.json"),JsonSerializer.Serialize(files,new JsonSerializerOptions{WriteIndented=false}));
 }
}
