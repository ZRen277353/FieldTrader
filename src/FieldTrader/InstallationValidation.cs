using System.IO;
using System.Text.Json;
using System.Windows;
namespace FieldTrader;
public static class InstallationValidation {
 public static async Task Run(string dataDirectory,string reportPath) {
  Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
  var db=new CatalogStore(dataDirectory);db.Load();SeedCatalog.Merge(db);
  var engine=new WaveformRecognitionService();engine.Load(db.Data,db.Root);
  var images=db.Data.Items.Count(i=>ItemImage.Load(i,db)!=null);
  if(images!=db.Data.Items.Count(i=>i.ImagePath!=null)||db.Data.Items.Count!=138||db.Data.SoundGroups.Count!=17||db.Data.SoundSamples.Count!=17||engine.TemplateCount!=17||engine.LoadErrors.Count!=0)throw new InvalidDataException("安装后的图片或声音数据库不完整");
  var before=File.ReadAllText(Path.Combine(db.Root,"catalog.json"));SeedCatalog.Merge(db);
  if(before!=File.ReadAllText(Path.Combine(db.Root,"catalog.json")))throw new InvalidDataException("重复初始化改变了数据库");
  using var session=new SessionState(db,Application.Current.Dispatcher);
  var main=new MainWindow(session);var hud=new OverlayWindow(session);main.Show();hud.Show();
  var sample=db.Data.SoundSamples[0];var decision=engine.Compare(AudioFiles.Read(Path.Combine(db.Root,sample.FilePath)),db.Data);
  if(decision.Result==null||decision.Result.SoundGroupId!=sample.SoundGroupId)throw new InvalidDataException("安装后的声音匹配失败");
  session.Publish(decision.Result);await Task.Delay(200);
  if(!hud.Topmost||!main.IsVisible||!hud.IsVisible)throw new InvalidOperationException("窗口初始化失败");
  void ShowGroup(string group){session.Publish(new(DateTimeOffset.Now,group,db.Data.Items.Where(i=>i.SoundGroupId==group).Select(i=>new Candidate(i,.9)).ToArray(),false));}
  ShowGroup("aqtwwx-listen-sound-01");session.LayoutFilter="6格·竖放";
  if(!session.HudCandidates.Any(c=>c.Name=="鎏金羽宫")||!session.HudCandidates.Any(c=>c.PriceLabel=="165W"))throw new InvalidDataException("网站六格竖放与价格缺失");
  session.LayoutFilter="6格·横放";
  if(!session.HudCandidates.Any(c=>c.Name=="黎明")||session.HudCandidates.Any(c=>c.Name=="鎏金羽宫"))throw new InvalidDataException("网站六格横竖筛选错误");
  session.LayoutFilter="8格·横放";
  if(!session.HudCandidates.Any(c=>c.Name=="未来滑板"))throw new InvalidDataException("网站八格横放筛选错误");
  session.LayoutFilter="4格";
  if(!session.HudCandidates.Any(c=>c.GridWidth==2&&c.GridHeight==2))throw new InvalidDataException("网站四格筛选错误");
  ShowGroup("aqtwwx-listen-sound-09");session.LayoutFilter="6格·横放";
  if(!session.HudCandidates.Any(c=>c.Name=="金狮雕像"))throw new InvalidDataException("网站独立音效组分类错误");
  File.WriteAllText(reportPath,JsonSerializer.Serialize(new{Passed=true,Items=db.Data.Items.Count,Groups=db.Data.SoundGroups.Count,Samples=db.Data.SoundSamples.Count,Images=images,Templates=engine.TemplateCount,Recognition=true,MainWindow=true,OverlayTopmost=hud.Topmost,SeedMergeIdempotent=true,LayoutsAndPrices=true,SelectedOutput=session.SelectedDevice?.Name,RecommendedOutput=AudioRouting.RecommendedId(),Runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription},new JsonSerializerOptions{WriteIndented=true}));
  hud.Close();main.Close();
 }
}
