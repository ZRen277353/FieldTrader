using System.IO;
using System.Text.Json;
namespace FieldTrader;
public sealed record Item(string Id, string Name, string? ImagePath, string SoundGroupId,int GridWidth=0,int GridHeight=0,string Orientation="unspecified",int[]? ImageCrop=null,string? SourceName=null,string? SourceBatch=null,int? Price=null,string? Quality=null,string? QualityColor=null);
public sealed record SoundGroup(string Id, string Name,string? Evidence=null);
public sealed record SoundSample(string Id, string SoundGroupId, string FilePath, string Description,string? SourceRecording=null,double? StartSeconds=null,double? EndSeconds=null,string? SourceBatch=null);
public sealed class Catalog { public int Version { get; set; } = 1; public string? SeedRevision {get;set;} public bool ReplaceLegacySeeds {get;set;} public List<Item> Items { get; set; } = []; public List<SoundGroup> SoundGroups { get; set; } = []; public List<SoundSample> SoundSamples { get; set; } = []; }
public sealed record Candidate(Item Item, double Similarity);
public sealed record RecognitionResult(DateTimeOffset Timestamp, string SoundGroupId, IReadOnlyList<Candidate> Candidates, bool IsMock);
public sealed record AudioFrame(float[] Mono, int SampleRate, DateTimeOffset Timestamp,float[]? BackgroundMono=null);
public interface IAudioRecognitionService { RecognitionResult? Recognize(AudioFrame frame, Catalog catalog); }
public interface IAudioEventDetector { AudioFrame? Process(AudioFrame frame); void Reset(); }
public sealed class MockRecognitionService : IAudioRecognitionService {
 private int index;
 public RecognitionResult? Recognize(AudioFrame frame, Catalog catalog) {
  if(catalog.SoundGroups.Count == 0) return null;
  var group = catalog.SoundGroups[index++ % catalog.SoundGroups.Count];
  return new(frame.Timestamp, group.Id, catalog.Items.Where(i=>i.SoundGroupId==group.Id).Select(i=>new Candidate(i,0.97)).ToArray(),true);
 }
}
public sealed class CatalogStore {
 public string Root { get; }
 public Catalog Data { get; private set; } = new();
 public CatalogStore(string? root=null) { Root=root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FieldTrader"); Directory.CreateDirectory(Root); Directory.CreateDirectory(Path.Combine(Root,"images")); Directory.CreateDirectory(Path.Combine(Root,"samples")); }
 public void Load() {
  string file=Path.Combine(Root,"catalog.json");
  if(File.Exists(file)) { Data=JsonSerializer.Deserialize<Catalog>(File.ReadAllText(file)) ?? throw new InvalidDataException("物品库为空"); Validate(Data); }
  else { Data.SoundGroups.Add(new("demo-metal","金属碰撞 · 示例组")); Data.SoundGroups.Add(new("demo-glass","玻璃碰撞 · 示例组")); Data.Items.AddRange([new("demo-1","示例金属物品 A",null,"demo-metal"),new("demo-2","示例金属物品 B",null,"demo-metal"),new("demo-3","示例金属物品 C",null,"demo-metal"),new("demo-4","示例玻璃物品 A",null,"demo-glass"),new("demo-5","示例玻璃物品 B",null,"demo-glass")]); Save(); }
 }
 public static void Validate(Catalog data) { if(data.Version!=1) throw new InvalidDataException("不支持的物品库版本"); if(data.Items.Select(x=>x.Id).Distinct().Count()!=data.Items.Count || data.SoundGroups.Select(x=>x.Id).Distinct().Count()!=data.SoundGroups.Count) throw new InvalidDataException("重复 ID"); if(data.Items.Any(i=>!data.SoundGroups.Any(g=>g.Id==i.SoundGroupId)) || data.SoundSamples.Any(s=>!data.SoundGroups.Any(g=>g.Id==s.SoundGroupId))) throw new InvalidDataException("声音组关系无效"); }
 public void Save() { Validate(Data); var file=Path.Combine(Root,"catalog.json"); File.WriteAllText(file+".tmp",JsonSerializer.Serialize(Data,new JsonSerializerOptions{WriteIndented=true})); File.Move(file+".tmp",file,true); }
 public string Import(string source,string folder) { var relative=Path.Combine(folder,Guid.NewGuid()+Path.GetExtension(source)); File.Copy(source,Path.Combine(Root,relative)); return relative; }
 public string? Resolve(string? path) => string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(Path.Combine(Root,path));
}
