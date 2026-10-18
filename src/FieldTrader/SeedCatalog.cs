using System.IO;
using System.Text.Json;
namespace FieldTrader;
public static class SeedCatalog {
 public static void Merge(CatalogStore store){var root=Path.Combine(AppContext.BaseDirectory,"SeedData");var file=Path.Combine(root,"catalog.json");if(!File.Exists(file))return;var seed=JsonSerializer.Deserialize<Catalog>(File.ReadAllText(file))!;CatalogStore.Validate(seed);if(seed.SeedRevision!=null&&store.Data.SeedRevision==seed.SeedRevision)return;
  var current=Path.Combine(store.Root,"catalog.json");if(File.Exists(current))File.Copy(current,Path.Combine(store.Root,$"catalog-backup-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json"),false);
  // Retire only templates shipped by earlier prototypes, never user-created GUID samples.
  store.Data.SoundSamples.RemoveAll(s=>s.Id.StartsWith("recording-lion-")||s.Id.StartsWith("recording-teapot-")||s.SourceBatch=="music-202609-v04");
  store.Data.Items.RemoveAll(i=>i.Id.StartsWith("demo-")&&i.Name.StartsWith("示例")&&i.ImagePath==null&&!store.Data.SoundSamples.Any(s=>s.SoundGroupId==i.SoundGroupId));
  foreach(var group in seed.SoundGroups){int index=store.Data.SoundGroups.FindIndex(g=>g.Id==group.Id);if(index>=0)store.Data.SoundGroups[index]=group;else store.Data.SoundGroups.Add(group);}
  foreach(var item in seed.Items){if(item.ImagePath!=null)Copy(item.ImagePath);int index=store.Data.Items.FindIndex(i=>i.Id==item.Id);if(index>=0)store.Data.Items[index]=item;else store.Data.Items.Add(item);}
  foreach(var sample in seed.SoundSamples){Copy(sample.FilePath);if(!store.Data.SoundSamples.Any(s=>s.Id==sample.Id))store.Data.SoundSamples.Add(sample);}
  store.Data.SoundGroups.RemoveAll(g=>(g.Id.StartsWith("demo-")||g.Id is "sound-golden-lion" or "sound-antique-teapot")&&!store.Data.Items.Any(i=>i.SoundGroupId==g.Id)&&!store.Data.SoundSamples.Any(s=>s.SoundGroupId==g.Id));
  store.Data.SeedRevision=seed.SeedRevision;store.Save();
  void Copy(string relative){var destination=Path.Combine(store.Root,relative);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);if(!File.Exists(destination))File.Copy(Path.Combine(root,relative),destination);}
 }
}
