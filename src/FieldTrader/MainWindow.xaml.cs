using System.Diagnostics;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace FieldTrader;
public partial class MainWindow : Window {
 private readonly SessionState session; private OverlayWindow? overlay;private VoiceBoardWindow? voiceBoard;private Market.MarketWindow? market;
 public MainWindow(SessionState session) { InitializeComponent(); this.session=session; DataContext=session;Closed+=(_,_)=>{voiceBoard?.Dispose();market?.Close();}; }
 private void OpenMarket(object sender,RoutedEventArgs e){market??=new Market.MarketWindow(session.Store.Root){Owner=this};market.Closed+=(_,_)=>market=null;market.Show();market.Activate();}
 private void StartListening(object sender,RoutedEventArgs e) { session.Start(); if(session.Listening) ShowOverlay(); }
 private async void StopListening(object sender,RoutedEventArgs e)=>await session.StopAsync();
 private void RefreshDevices(object sender,RoutedEventArgs e)=>session.RefreshDevices();
 private void AudioDiagnostics(object sender,RoutedEventArgs e){
  var panel=new StackPanel{Margin=new Thickness(20)};
  var hint=new TextBlock{Text="先在游戏里拖动物品，再查看信号。队友说话有波形并不代表采到了游戏音效。\n导出保留原始声道，包含最近收到的最多 10 秒音频（可能含队友语音），只保存在你选的位置。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};panel.Children.Add(hint);
  var detail=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Height=340,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  var buttons=new WrapPanel();panel.Children.Add(buttons);panel.Children.Add(detail);
  void Refresh(){try{var samples=session.Audio.Buffer.Snapshot(session.Audio.SampleRate);double peak=samples.Length==0?0:samples.Max(v=>Math.Abs(v));detail.Text=$"当前监听设备：{session.SelectedDevice?.Name}\n监听状态：{session.Listening}\n最近收到音频的峰值：{(peak>0?20*Math.Log10(peak):double.NegativeInfinity):F1} dBFS\n最近音频回调各声道峰值：{string.Join(" / ",session.Audio.ChannelPeaks.Select(v=>v.ToString("F6")))}\n匹配状态：{session.RecognitionStatus}\n\n"+session.Audio.LastDiagnostic+"\n\n"+AudioRouting.Report();}catch(Exception ex){detail.Text=session.Audio.LastDiagnostic+"\n诊断操作失败："+ex.Message;}}
  var detect=new Button{Content="检测游戏输出"};detect.Click+=async(_,_)=>{detect.IsEnabled=false;try{await session.DetectGameOutputAsync();Refresh();}catch(Exception ex){detail.Text=ex.Message;}finally{detect.IsEnabled=true;}};buttons.Children.Add(detect);
  var refresh=new Button{Content="刷新诊断"};refresh.Click+=(_,_)=>Refresh();buttons.Children.Add(refresh);
  var saveDiagnostic=new Button{Content="导出诊断信息"};saveDiagnostic.Click+=(_,_)=>{try{Refresh();var dialog=new SaveFileDialog{FileName="监听诊断.txt",Filter="文本文件|*.txt"};if(dialog.ShowDialog()==true)System.IO.File.WriteAllText(dialog.FileName,detail.Text);}catch(Exception ex){detail.Text=session.Audio.LastDiagnostic+"\n诊断操作失败："+ex.Message;}};buttons.Children.Add(saveDiagnostic);
  var export=new Button{Content="导出最近音频"};export.Click+=(_,_)=>{try{var samples=session.Audio.RawBuffer.Snapshot();if(samples.Length==0){detail.Text="还没有采集到音频，请开始监听并在游戏中拖动物品。";return;}var dialog=new SaveFileDialog{FileName="盲盒诊断-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".wav",Filter="WAV 音频|*.wav"};if(dialog.ShowDialog()==true){using(var writer=new NAudio.Wave.WaveFileWriter(dialog.FileName,NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(session.Audio.SampleRate,session.Audio.Channels)))writer.WriteSamples(samples,0,samples.Length);Refresh();System.IO.File.WriteAllText(dialog.FileName+".txt",detail.Text);detail.Text="已保存音频与诊断说明：\n"+dialog.FileName+"\n\n"+detail.Text;}}catch(Exception ex){detail.Text=session.Audio.LastDiagnostic+"\n诊断操作失败："+ex.Message;}};buttons.Children.Add(export);
  Refresh();new Window{Title="音频采集诊断",Owner=this,Width=820,Height=570,WindowStartupLocation=WindowStartupLocation.CenterOwner,Style=(Style)FindResource(typeof(Window)),Content=panel}.ShowDialog();
 }
 private async void Demo(object sender,RoutedEventArgs e){var dialog=new OpenFileDialog{Filter="音频文件|*.wav;*.mp3;*.flac"};if(dialog.ShowDialog()==true)await session.TestFileAsync(dialog.FileName);}
 private void OpenOverlay(object sender,RoutedEventArgs e)=>ShowOverlay();
 private void ShowOverlay() { if(overlay==null) { overlay=new(session); overlay.Closed+=(_,_)=>overlay=null; } overlay.Show(); }
 private void OpenVoiceBoard(object sender,RoutedEventArgs e){try{voiceBoard??=new VoiceBoardWindow(session.Store.Root){Owner=this};voiceBoard.Show();voiceBoard.WindowState=WindowState.Normal;voiceBoard.Activate();}catch(Exception ex){ShowInfo("游戏语音",ex.Message);}}
 private void OpenCatalog(object sender,RoutedEventArgs e)=>new CatalogWindow(session){Owner=this}.ShowDialog();
 private void ScrollCandidates(object sender,System.Windows.Input.MouseWheelEventArgs e){if(sender is System.Windows.Controls.ScrollViewer scroll&&scroll.ScrollableWidth>0){scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset-e.Delta);e.Handled=true;}}
 private void DragWindow(object sender,MouseButtonEventArgs e){if(e.ChangedButton!=MouseButton.Left)return;if(e.ClickCount==2){Maximize(sender,e);return;}try{DragMove();}catch(InvalidOperationException){}}
 private void Minimize(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
 private void Maximize(object sender,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
 private void ExitApp(object sender,RoutedEventArgs e)=>Close();
 private void GoHome(object sender,RoutedEventArgs e)=>Activate();
 private void ClearHistory(object sender,RoutedEventArgs e)=>session.ClearHistory();
 private void OpenDataFolder(object sender,RoutedEventArgs e){try{Process.Start(new ProcessStartInfo(session.Store.Root){UseShellExecute=true});}catch(Exception ex){ShowInfo("无法打开数据目录",ex.Message);}}
 private void Help(object sender,RoutedEventArgs e)=>ShowInfo("使用说明","作者：取名字真麻烦\n抖音号：21360166462\n作者 QQ：3445838743\n免费使用，禁止倒卖；转载分发请保留作者信息。\n\n1. 选择游戏使用的输出设备，点击开始监听，小悬浮窗将自动打开。\n\n2. 两个窗口的波形、电平与频谱来自实际采集。开始监听后只匹配拿起声音的起始部分，无需放下声音；也可以用「音频文件测试」分析录音。相似度不是准确率。\n\n3. 在「物品数据库」中添加图片、声音样本和声音组关系。同一声音可能对应多个物品。\n\n4. 拖动小窗标题栏调整位置；锁定后不能拖动；极简缩小显示；右下角齿轮可按占格、横竖筛选，并调整透明度。\n\n5. 隐藏 HUD、最小化主窗口不会停止监听；退出主程序会释放设备。建议游戏使用窗口化或无边框全屏，独占全屏可能无法覆盖。");
 private void Settings(object sender,RoutedEventArgs e){
  var panel=new StackPanel{Margin=new Thickness(24)};
  panel.Children.Add(new TextBlock{Text="内部识别增益",FontSize=23,Margin=new Thickness(0,0,0,16)});
  panel.Children.Add(new TextBlock{Text="只放大送入识别的声音，不改变耳机音量、原始录音或电平表。较小的拿起声更容易触发检测，环境声也会同时放大；70%匹配门槛不变。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,20)});
  var slider=new Slider{Minimum=1,Maximum=8,Value=session.InputGain,TickFrequency=1,IsSnapToTickEnabled=true};var label=new TextBlock{Margin=new Thickness(0,10,0,18)};
  void Update()=>label.Text=slider.Value==1?"1倍 · 关闭放大":$"{slider.Value:0}倍 · 建议先用4倍";slider.ValueChanged+=(_,_)=>Update();Update();panel.Children.Add(slider);panel.Children.Add(label);
  panel.Children.Add(new TextBlock{Text=$"物品库：{session.ItemCount}件 / {session.GroupCount}组\n同声物品请结合占格筛选。调节后会重新准备识别服务。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,20)});
  var window=new Window{Title="软件设置",Owner=this,Width=540,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,Style=(Style)FindResource(typeof(Window)),Content=panel};
  var save=new Button{Content="保存并应用"};save.Click+=(_,_)=>{try{session.SetInputGain(slider.Value);window.Close();}catch(Exception ex){label.Text="保存失败："+ex.Message;}};panel.Children.Add(save);window.ShowDialog();
 }
 private void ShowHistory(object sender,RoutedEventArgs e) {var list=new ListBox{ItemsSource=session.History,Margin=new Thickness(22)};var w=new Window{Title="识别记录",Width=760,Height=500,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,Style=(Style)FindResource(typeof(Window)),Content=list};w.ShowDialog();}
 private void ShowInfo(string title,string text) {var panel=new StackPanel{Margin=new Thickness(26)};panel.Children.Add(new TextBlock{Text=title,FontSize=23,Margin=new Thickness(0,0,0,20)});panel.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,LineHeight=23});var w=new Window{Title=title,Width=600,Height=580,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,Style=(Style)FindResource(typeof(Window)),Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};w.ShowDialog();}
}



