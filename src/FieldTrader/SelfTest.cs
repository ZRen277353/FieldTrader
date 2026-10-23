using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.CoreAudioApi;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace FieldTrader;
public static class SelfTest {
 public static async Task Run(bool audioTest) {
  var report=new List<string>(); void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);report.Add("PASS "+text);}
  report.AddRange(CaptureDecoderValidation.Run());
  var meter=new DisplayPeakMeter();Check(meter.Read(.08).Value==0,"电平表静音零格");
  meter.Add([.01f,-.01f]);meter.Add(new float[480]);var weak=meter.Read(.08);Check(weak.Value>30&&weak.Value<35&&Math.Abs(weak.Db+40)<.01,"小声音显示约10格；随后静音回调不覆盖短峰值");
  meter.Add([.1f]);var loud=meter.Read(.08);Check(loud.Value>weak.Value&&Math.Abs(loud.Db+20)<.01,"电平随声音增强上升");
  Check(meter.Read(.08).Value<loud.Value,"电平无新声音时回落");Check(meter.Read(1).Value==0,"静音后电平归零，不保持假亮");
  meter.Add([1]);Check(meter.Read(.08).Value==100,"满幅信号电平上限正确");meter.Reset();Check(meter.Read(.08).Value==0,"停止重置立即归零");
  var detector=new EnergyEventDetector(true);var events=new List<AudioFrame>();const int rate=16000;
  for(int block=0;block<550;block++){double seconds=block*.01;bool burst=new[]{2d,3d,4d}.Any(t=>seconds>=t&&seconds<t+.18);var frame=Enumerable.Range(0,160).Select(i=>(float)((burst?.04:.002)*Math.Sin(2*Math.PI*1000*(block*160+i)/rate))).ToArray();events.AddRange(detector.Push(new(frame,rate,DateTimeOffset.UnixEpoch.AddSeconds(seconds+.01))));}
  Check(events.Count(e=>(e.Timestamp-DateTimeOffset.UnixEpoch).TotalSeconds>1)==3,"持续背景声下仍能连续检测三个后续声音，不等待绝对静音");
  detector.Reset();Check(detector.Push(new(new float[16000],16000,DateTimeOffset.UnixEpoch)).Count==0,"停止重置后静音不产生事件");
  var ring=new AudioRingBuffer(4);ring.Write([1,2,3]);ring.Write([4,5,6]);Check(ring.Snapshot().SequenceEqual(new float[]{3,4,5,6}),"循环缓冲保留最新样本");
  Check(ReferencePriceCatalog.FormatWan(1550000)=="155W"&&ReferencePriceCatalog.FormatWan(75000)=="7.5W","参考价格式化为整数或一位小数 W");
  var silence=SpectrumAnalysis.Mel(new float[2048],48000);Check(silence.All(v=>v==0),"静音频谱不产生伪信号"); var sine=Enumerable.Range(0,2048).Select(i=>(float)(.3*Math.Sin(2*Math.PI*1000*i/48000))).ToArray();var mel=SpectrumAnalysis.Mel(sine,48000);int peak=Array.IndexOf(mel,mel.Max());Check(peak>=15&&peak<=19,"1 kHz 测试音落入正确 Mel 频带");
  var root=Path.Combine(AppContext.BaseDirectory,"test-data");var store=new CatalogStore(root);store.Load();store.Save();var reread=new CatalogStore(root);reread.Load();Check(reread.Data.Items.Count==store.Data.Items.Count,"JSON 往返与声音组关系");
  var mock=new MockRecognitionService();var result=mock.Recognize(new([],48000,DateTimeOffset.Now),store.Data)!;Check(result.Candidates.Count==3&&result.IsMock,"一声音组多候选与 MOCK 标记");
  using var state=new SessionState(store,Application.Current.Dispatcher);var main=new MainWindow(state);var overlay=new OverlayWindow(state);main.Show();overlay.Show();await Task.Delay(200);Check(main.IsListeningHotKeyRegistered,"默认 Alt + W 全局监听快捷键注册成功");Check(ReferenceEquals(main.DataContext,overlay.DataContext)&&overlay.Topmost,"主窗口与置顶 HUD 共享状态");Check(overlay.OpacityPanel.Visibility==Visibility.Visible,"HUD 默认显示规格与透明度选项");
  var settingsButton=Descendants(main).OfType<Button>().First(b=>b.Content?.ToString()?.Contains("软件设置")==true);
  bool defaultHotKeyShown=false;
  _ = Application.Current.Dispatcher.BeginInvoke(new Action(async()=>{await Task.Delay(150);var settings=Application.Current.Windows.OfType<Window>().First(w=>w.Title=="软件设置");defaultHotKeyShown=Descendants(settings).OfType<TextBox>().Any(t=>t.Text=="Alt + W");var slider=Descendants(settings).OfType<Slider>().First();slider.Value=3;SaveImage(settings,"recognition-gain-settings.png");Click(settings,"保存并应用");}));
  settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(defaultHotKeyShown,"设置窗口显示默认全局监听快捷键");Check(state.InputGain==3&&RecognitionGain.Load(state.Store.Root)==3,"内部增益设置保存并记忆");state.SetInputGain(4);
  var combo=Descendants(main).OfType<ComboBox>().First();combo.IsDropDownOpen=true;await Task.Delay(80);Check(combo.IsDropDownOpen,"深色设备下拉菜单可展开");combo.IsDropDownOpen=false; SaveImage(main,"main-window-idle.png"); Check(state.DisplayCandidates.Count()==4,"待机界面保留四个候选占位卡片"); state.Demo();state.Demo();state.Demo();
  var pricedItems=new[]{
   new Candidate(new Item("preview-array-lens","阵列镜片",null,"preview",2,3,Price:1550000,Quality:"红色",QualityColor:"#ff4d4f"),.97),
   new Candidate(new Item("preview-antenna","天线",null,"preview",2,2,Price:180873,Quality:"金色",QualityColor:"#faad14"),.95),
   new Candidate(new Item("preview-dawn","黎明",null,"preview",3,2,Price:1600000,Quality:"红色",QualityColor:"#ff4d4f"),.93)
  };
  state.Publish(new(DateTimeOffset.Now,"price-preview",pricedItems,false));await Task.Delay(100);
  Check(state.HudCandidates.Select(c=>c.PriceLabel).SequenceEqual(new[]{"160W","155W","18.09W"}),"HUD 按价格降序并在名称下方显示参考价");
  state.SortField="名字";state.SortDirection="升序";Check(state.HudCandidates.Select(c=>c.Name).SequenceEqual(new[]{"天线","黎明","阵列镜片"}),"HUD 名字升序排序");
  state.HudOpacity=.62;Check(Math.Abs(UserSettingsStore.Load(store.Root).OverlayOpacity-.62)<.001,"HUD 透明度自动记忆");
  state.SortField="价格";state.SortDirection="降序";state.HudOpacity=1;
  SaveImage(main,"main-window.png");SaveImage(overlay,"overlay-window.png"); main.Width=900;main.Height=760;await Task.Delay(100);SaveImage(main,"main-window-small.png");main.Width=1150;main.Height=970;overlay.Close();Check(state.Candidates.Count==3,"关闭 HUD 保留结果");
  var lastHud=state.HudCandidates.ToArray();var priorHud=state.PreviousHudCandidates.ToArray();
  state.MarkAnalyzing();Check(state.ResultBadge=="正在分析","检测后立即显示正在分析");state.MarkUnmatched("最高相似度 64%，未达到70%");Check(state.Candidates.Count==0&&state.HistoryEntries[0].Name=="本次不确定"&&state.ResultNotice.Contains("已收到声音"),"不确定清除旧候选、显示收到声音并写入历史");
  Check(state.HudCandidates.SequenceEqual(lastHud)&&state.PreviousHudCandidates.SequenceEqual(priorHud)&&state.HudNotice.Contains("保留上次结果"),"不确定不清除两轮HUD快照");
  state.MarkUnmatched("再次不确定");Check(state.HudCandidates.SequenceEqual(lastHud)&&state.PreviousHudCandidates.SequenceEqual(priorHud),"连续不确定不推进历史轮次");
  state.Demo();Check(state.PreviousHudCandidates.SequenceEqual(lastHud),"新有效匹配将最近有效结果推进上一轮");state.MarkUnmatched("本次不确定");
  var uncertainHud=new OverlayWindow(state);uncertainHud.Show();await Task.Delay(100);SaveImage(uncertainHud,"overlay-uncertain.png");SaveImage(main,"main-uncertain.png");Click(uncertainHud,"极简");await Task.Delay(100);SaveImage(uncertainHud,"overlay-uncertain-compact.png");uncertainHud.Close();
  report.Add($"INFO 输出设备数量：{state.Devices.Count}");
  if(audioTest&&state.Devices.Count>0) { Click(main,"●  开始监听"); Check(state.Listening,"WASAPI 启动"); var hud=Application.Current.Windows.OfType<OverlayWindow>().Single(); Check(hud.IsVisible,"点击开始监听后自动打开 HUD"); Click(hud,"极简"); Check(hud.Width==370,"HUD 极简模式切换"); await Task.Delay(150); SaveImage(hud,"overlay-compact.png"); hud.Close();Check(state.Audio.IsRunning,"关闭 HUD 后捕获继续");main.WindowState=WindowState.Minimized;Check(state.Audio.IsRunning,"主窗口最小化后捕获继续");using var enumerator=new MMDeviceEnumerator(); using var endpoint=enumerator.GetDevice(state.SelectedDevice!.Id); using var player=new WasapiOut(endpoint,AudioClientShareMode.Shared,false,100); var tone=new SignalGenerator(48000,2){Gain=0.015,Frequency=440,Type=SignalGeneratorType.Sin}; player.Init(tone.Take(TimeSpan.FromSeconds(0.6))); player.Play(); await Task.Delay(300); main.WindowState=WindowState.Normal; SaveImage(main,"main-window-listening.png"); Check(state.Spectrum.Any(v=>v>0),"实时 Mel 频谱响应测试音频"); await Task.Delay(1200); Check(state.Audio.Buffer.Snapshot().Any(v=>Math.Abs(v)>0.00001),"真实 Loopback 收到非零测试音频"); Check(state.Audio.RawBuffer.Snapshot().Any(v=>Math.Abs(v)>0.00001)&&state.Audio.RawBuffer.Snapshot().Length%state.Audio.Channels==0,"原始声道诊断缓冲保留有效完整帧");report.Add($"INFO 缓冲样本数：{state.Audio.Buffer.Snapshot().Length}（静音环境允许为 0）");await state.StopAsync();Check(!state.Audio.IsRunning&&!state.Listening,"WASAPI 明确停止");state.Start();Check(state.Listening,"停止后可以重新开始");await state.StopAsync(); }
  var catalog=new CatalogWindow(state);catalog.Show();await Task.Delay(100);SaveImage(catalog,"catalog-window.png");catalog.Close();main.Close();File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"verification.txt"),report);
 }
 private static void Click(DependencyObject root,string label) { var button=Descendants(root).OfType<Button>().First(b=>b.Content?.ToString()==label); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
 private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var next in Descendants(child))yield return next;} }
 private static void SaveImage(Window window,string filename) {window.UpdateLayout();var image=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(AppContext.BaseDirectory,filename));encoder.Save(file);}
}





