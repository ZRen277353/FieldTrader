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
  if(images!=db.Data.Items.Count(i=>i.ImagePath!=null)||db.Data.Items.Count<109||db.Data.SoundGroups.Count<24||engine.TemplateCount<113||engine.LoadErrors.Count!=0)throw new InvalidDataException("安装后的图片或声音数据库不完整");
  var before=File.ReadAllText(Path.Combine(db.Root,"catalog.json"));SeedCatalog.Merge(db);
  if(before!=File.ReadAllText(Path.Combine(db.Root,"catalog.json")))throw new InvalidDataException("重复初始化改变了数据库");
  using var session=new SessionState(db,Application.Current.Dispatcher);
  var main=new MainWindow(session);var hud=new OverlayWindow(session);main.Show();hud.Show();
  var sample=db.Data.SoundSamples[0];var decision=engine.Compare(AudioFiles.Read(Path.Combine(db.Root,sample.FilePath)),db.Data);
  if(decision.Result==null||decision.Result.SoundGroupId!=sample.SoundGroupId)throw new InvalidDataException("安装后的声音匹配失败");
  session.Publish(decision.Result);await Task.Delay(200);
  if(!hud.Topmost||!main.IsVisible||!hud.IsVisible)throw new InvalidOperationException("窗口初始化失败");
  void ShowGroup(string group){session.Publish(new(DateTimeOffset.Now,group,db.Data.Items.Where(i=>i.SoundGroupId==group).Select(i=>new Candidate(i,.9)).ToArray(),false));}
  ShowGroup("family-202609-07");session.LayoutFilter="4格";
  if(!session.DisplayCandidates.Any(c=>c.Name=="稀有神秘货物")||!session.HudCandidates.Any(c=>c.Name=="稀有神秘货物"))throw new InvalidDataException("四格同声分类缺失");
  session.LayoutFilter="6格·横放";if(!session.HudCandidates.Any(c=>c.Name=="发射药包")||session.HudCandidates.Any(c=>c.Name=="稀有神秘货物"))throw new InvalidDataException("同声不同规格筛选错误");
  ShowGroup("family-202609-05");if(!new[]{"黎明","航天实验室","航天导航仪"}.All(n=>session.HudCandidates.Any(c=>c.Name==n)))throw new InvalidDataException("六格候选分类缺失");
  ShowGroup("sound-double-neck-lute-20261008");session.LayoutFilter="6格·竖放";
  if(session.HudCandidates.Single().Name!="双颈鲁特琴")throw new InvalidDataException("鲁特琴竖六格分类错误");
  ShowGroup("family-202609-12");session.LayoutFilter="4格";
  if(!session.HudCandidates.Any(c=>c.Name=="北境星辰")||!session.HudCandidates.Any(c=>c.Name=="花瓶"))throw new InvalidDataException("北境星辰花瓶组分类错误");
  File.WriteAllText(reportPath,JsonSerializer.Serialize(new{Passed=true,Items=db.Data.Items.Count,Groups=db.Data.SoundGroups.Count,Images=images,Templates=engine.TemplateCount,Recognition=true,MainWindow=true,OverlayTopmost=hud.Topmost,SeedMergeIdempotent=true,FourCellCargoAndSixCellCandidates=true,SelectedOutput=session.SelectedDevice?.Name,RecommendedOutput=AudioRouting.RecommendedId(),Runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription},new JsonSerializerOptions{WriteIndented=true}));
  hud.Close();main.Close();
 }
}
