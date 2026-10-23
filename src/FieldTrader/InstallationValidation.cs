using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace FieldTrader;
public static class InstallationValidation {
 public static async Task Run(string dataDirectory,string reportPath) {
  Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
  var db=new CatalogStore(dataDirectory);db.Load();SeedCatalog.Merge(db);
  var engine=new WaveformRecognitionService();engine.Load(db.Data,db.Root);
  var images=db.Data.Items.Count(i=>ItemImage.Load(i,db)!=null);
  if(images!=db.Data.Items.Count(i=>i.ImagePath!=null)||db.Data.Items.Count!=138||db.Data.SoundGroups.Count!=17||db.Data.SoundSamples.Count!=17||engine.TemplateCount!=17||engine.LoadErrors.Count!=0)throw new InvalidDataException("安装后的图片或声音数据库不完整");
  var qualityItem=db.Data.Items.First(i=>i.Name=="鎏金羽宫");var qualityImage=ItemImage.Load(qualityItem,db)!;var pixel=new byte[4];qualityImage.CopyPixels(new Int32Rect(0,0,1,1),pixel,4,0);var expected=QualityColors.Background(qualityItem.QualityColor);
  if(pixel[3]!=255||Math.Abs(pixel[0]-expected.B)>2||Math.Abs(pixel[1]-expected.G)>2||Math.Abs(pixel[2]-expected.R)>2)throw new InvalidDataException("透明图片品质底色合成失败");
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
  ShowGroup("aqtwwx-listen-sound-01");session.LayoutFilter="全部规格";session.SortField="价格";session.SortDirection="降序";
  var prices=session.HudCandidates.Select(c=>c.PriceValue??0).ToArray();
  if(!prices.SequenceEqual(prices.OrderByDescending(v=>v)))throw new InvalidDataException("价格降序失败");
  if(!session.HudCandidates.Any(c=>c.Quality=="红色"&&c.QualityColor=="#ff4d4f"&&c.QualityBackground is System.Windows.Media.SolidColorBrush brush&&brush.Color.A==255&&brush.Color.R>brush.Color.G&&brush.Color.R>brush.Color.B))throw new InvalidDataException("品质颜色背景缺失");
  if(session.HudCandidates.First(c=>c.Quality=="红色").QualityBackgroundColor!="#361c1a"||session.HudCandidates.First(c=>c.Quality=="金色").QualityBackgroundColor!="#2a1d0e"||session.HudCandidates.First(c=>c.Quality=="紫色").QualityBackgroundColor!="#1d1a27")throw new InvalidDataException("品质背景默认颜色错误");
  session.SortField="品质";session.SortDirection="降序";
  var qualities=session.HudCandidates.Select(c=>c.QualityRank).ToArray();
  if(!qualities.SequenceEqual(qualities.OrderByDescending(v=>v)))throw new InvalidDataException("品质降序失败");
  session.SortField="名字";session.SortDirection="升序";
  var names=session.HudCandidates.Select(c=>c.Name).ToArray();
  if(!names.SequenceEqual(names.OrderBy(v=>v,StringComparer.CurrentCulture)))throw new InvalidDataException("名字升序失败");
  session.HudOpacity=.61;var saved=UserSettingsStore.Load(db.Root);
  if(Math.Abs(saved.OverlayOpacity-.61)>.001||saved.SortField!="名字"||saved.SortDirection!="升序")throw new InvalidDataException("透明度或排序未保存");
  var customPalette=new QualityPalette("#112233","#445566","#778899");session.ApplyQualityPalette(customPalette);
  if(session.HudCandidates.First(c=>c.Quality=="红色").QualityBackgroundColor!="#112233")throw new InvalidDataException("自定义红色背景未即时应用");
  var customImage=ItemImage.Load(qualityItem,db,customPalette)!;customImage.CopyPixels(new Int32Rect(0,0,1,1),pixel,4,0);var customExpected=QualityColors.FromHex("#112233");
  if(pixel[3]!=255||pixel[0]!=customExpected.B||pixel[1]!=customExpected.G||pixel[2]!=customExpected.R)throw new InvalidDataException("自定义背景未合成到透明图片");
  var customSettings=UserSettingsStore.Load(db.Root) with{RedQualityBackground="#112233",GoldQualityBackground="#445566",PurpleQualityBackground="#778899"};UserSettingsStore.Save(db.Root,customSettings);var reloaded=UserSettingsStore.Load(db.Root);
  if(reloaded.RedQualityBackground!="#112233"||reloaded.GoldQualityBackground!="#445566"||reloaded.PurpleQualityBackground!="#778899")throw new InvalidDataException("自定义品质颜色未保存");
  var sizeHud=new OverlayWindow(session){SizeToContent=SizeToContent.Manual,Width=480,Height=700};sizeHud.Show();sizeHud.ResizeBy("BottomRight",80,60);sizeHud.SaveSize();var sized=UserSettingsStore.Load(db.Root);
  if(Math.Abs(sized.OverlayWidth-560)>.1||Math.Abs(sized.OverlayHeight-760)>.1)throw new InvalidDataException("悬浮窗尺寸未保存");
  sizeHud.Close();var restoredHud=new OverlayWindow(session);restoredHud.Show();if(Math.Abs(restoredHud.Width-560)>.1||Math.Abs(restoredHud.Height-760)>.1||restoredHud.SizeToContent!=SizeToContent.Manual)throw new InvalidDataException("悬浮窗尺寸未恢复");restoredHud.Close();
  UserSettingsStore.Update(db.Root,s=>s with{OverlayWidth=480,OverlayHeight=0});
  session.ApplyQualityPalette(QualityPalette.Default);
  SaveImage(hud,Path.Combine(Path.GetDirectoryName(reportPath)!,"overlay-quality-sort.png"));SaveImage(main,Path.Combine(Path.GetDirectoryName(reportPath)!,"main-quality-sort.png"));
  File.WriteAllText(reportPath,JsonSerializer.Serialize(new{Passed=true,Items=db.Data.Items.Count,Groups=db.Data.SoundGroups.Count,Samples=db.Data.SoundSamples.Count,Images=images,Templates=engine.TemplateCount,Recognition=true,MainWindow=true,OverlayTopmost=hud.Topmost,SeedMergeIdempotent=true,LayoutsAndPrices=true,QualityBackgrounds=true,CustomQualityColors=true,Sorting=true,PreferencePersistence=true,OverlaySizePersistence=true,SelectedOutput=session.SelectedDevice?.Name,RecommendedOutput=AudioRouting.RecommendedId(),Runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription},new JsonSerializerOptions{WriteIndented=true}));
  hud.Close();main.Close();
 }
 private static void SaveImage(Window window,string filename){window.UpdateLayout();var image=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(filename);encoder.Save(file);}
}
