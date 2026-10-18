using System.IO;
using System.Text.Json;
namespace FieldTrader;
public sealed record ExportClip(int Index,double Start,double End,string File,float Peak,SoundFeatures Features);
public sealed record ExportRecording(string Name,string Source,double Seconds,List<ExportClip> Clips);
public sealed class AcousticCluster {
 public string Id {get;set;}="";
 public List<string> Members {get;set;}=[];
 public List<string> SampleFiles {get;set;}=[];
 public double Diameter {get;set;}
 [System.Text.Json.Serialization.JsonIgnore] public List<SoundFeatures> Features {get;}=[];
}
public static class BulkCluster {
 public static void Run(string directory){var recordings=JsonSerializer.Deserialize<List<ExportRecording>>(File.ReadAllText(Path.Combine(directory,"export.json")))!;var clusters=new List<AcousticCluster>();var assignments=new List<object>();foreach(var recording in recordings){int trainCount=recording.Clips.Count>=6?4:Math.Max(1,recording.Clips.Count/2);foreach(var clip in recording.Clips.Take(trainCount)){AcousticCluster? winner=null;double best=double.MaxValue;foreach(var c in clusters){double distance=SoundFeaturesExtractor.Distance(clip.Features,c.Features[0]);if(distance>.012||distance>=best)continue;double max=distance;bool fits=true;foreach(var f in c.Features.Skip(1)){max=Math.Max(max,SoundFeaturesExtractor.Distance(clip.Features,f));if(max>.018){fits=false;break;}}if(fits){winner=c;best=distance;}}
   if(winner==null){winner=new(){Id=$"acoustic-{clusters.Count+1:D3}"};clusters.Add(winner);}foreach(var f in winner.Features)winner.Diameter=Math.Max(winner.Diameter,SoundFeaturesExtractor.Distance(clip.Features,f));winner.Features.Add(clip.Features);winner.SampleFiles.Add(clip.File);if(!winner.Members.Contains(recording.Name))winner.Members.Add(recording.Name);assignments.Add(new{recording.Name,clip.File,GroupId=winner.Id});}}
  var tests=new List<object>();int found=0,rejected=0,wrong=0;foreach(var recording in recordings){int trainCount=recording.Clips.Count>=6?4:Math.Max(1,recording.Clips.Count/2);foreach(var clip in recording.Clips.Skip(trainCount)){var ranking=clusters.Select(c=>new {Cluster=c,Distance=c.Features.Min(f=>SoundFeaturesExtractor.Distance(clip.Features,f))}).OrderBy(x=>x.Distance).ToArray();var first=ranking[0];bool accepted=first.Distance<=.10;bool contains=first.Cluster.Members.Contains(recording.Name);if(!accepted)rejected++;else if(contains)found++;else wrong++;tests.Add(new{recording.Name,clip.File,Accepted=accepted,CorrectCandidate=contains,Distance=first.Distance,Margin=ranking.Length>1?ranking[1].Distance-first.Distance:1,GroupId=first.Cluster.Id,Candidates=first.Cluster.Members});}}
  File.WriteAllText(Path.Combine(directory,"cluster-plan.json"),JsonSerializer.Serialize(new{Clusters=clusters,Assignments=assignments,Tests=tests,Summary=new{RecordingCount=recordings.Count,ClipCount=recordings.Sum(r=>r.Clips.Count),TrainingCount=assignments.Count,HeldOut=tests.Count,Found=found,Rejected=rejected,Wrong=wrong}},new JsonSerializerOptions{WriteIndented=true}));
 }
}
