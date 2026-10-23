using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace FieldTrader;
public sealed record CandidateView(string Name,string ScoreLabel,BitmapSource? Image,int ArtKind=0,bool Highlight=false,double Score=0,string Tag="MOCK · 同一声音组",int GridWidth=0,int GridHeight=0,string PriceLabel="",int? PriceValue=null,string Quality="",string QualityColor="",int QualityRank=0) {
 public bool HasImage=>Image!=null;
 public string LayoutLabel=>ItemImage.Layout(GridWidth,GridHeight);
 public Visibility PriceVisibility=>string.IsNullOrWhiteSpace(PriceLabel)?Visibility.Collapsed:Visibility.Visible;
 public Brush QualityBackground {
  get {
   try {
    var color=(Color)ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(QualityColor)?"#243746":QualityColor);
    byte Blend(byte channel,byte baseChannel)=> (byte)Math.Clamp(Math.Round(channel*.45+baseChannel*.55),0,255);
    var tinted=Color.FromRgb(Blend(color.R,12),Blend(color.G,18),Blend(color.B,24));
    var brush=new SolidColorBrush(tinted);brush.Freeze();return brush;
   } catch { return new SolidColorBrush(Color.FromRgb(36,55,70)); }
  }
 }
}
public sealed record HistoryView(string Time,string Name,string Detail,CandidateView? Candidate);
public sealed class SessionState : INotifyPropertyChanged, IDisposable {
 public event PropertyChangedEventHandler? PropertyChanged;
 private void Changed([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
 public CatalogStore Store { get; }
 public double InputGain {get;private set;}
 public void SetInputGain(double value){value=double.IsFinite(value)?Math.Clamp(Math.Round(value),1,8):4;RecognitionGain.Save(Store.Root,value);InputGain=value;Changed(nameof(InputGain));if(Listening)StartPipeline();}
 public AudioCaptureService Audio { get; }=new();
 private readonly IAudioRecognitionService recognition=new MockRecognitionService();
 private RecognitionPipeline? pipeline;
 private readonly Dispatcher dispatcher; private readonly DispatcherTimer timer;
 private AudioFrame? latest; private DateTimeOffset lastFrame,startedAt;
 public ObservableCollection<AudioDevice> Devices { get; }=[];
 public ObservableCollection<CandidateView> Candidates { get; }=[];
 public ObservableCollection<string> History { get; }=[];
 public ObservableCollection<HistoryView> HistoryEntries { get; }=[];
 private readonly CandidateView[] placeholders=Enumerable.Range(0,4).Select(i=>new CandidateView("等待识别","—",null,i,false,0,"候选物品")).ToArray();
  public string[] LayoutOptions {get;}=["全部规格","1格","2格·横放","2格·竖放","3格·横放","3格·竖放","4格","6格·横放","6格·竖放","8格·横放","8格·竖放"];
 public string[] SortFields {get;}=["价格","品质","名字"];
 public string[] SortDirections {get;}=["降序","升序"];
 private string layoutFilter="全部规格";
 public string LayoutFilter {get=>layoutFilter;set{layoutFilter=value;Changed();Changed(nameof(DisplayCandidates));Changed(nameof(HudCandidates));Changed(nameof(PreviousHudCandidates));Changed(nameof(FilterSummary));Changed(nameof(NoFilterMatchesVisibility));Changed(nameof(HudFilterNote));if(Listening)StartPipeline();}}
 public Visibility NoFilterMatchesVisibility=>Candidates.Count>0&&!Candidates.Any(MatchesLayout)?Visibility.Visible:Visibility.Collapsed;
 public string FilterSummary=>$"同声候选 {Candidates.Count} 件 · 当前显示 {Candidates.Count(MatchesLayout)} 件";
 private bool MatchesLayout(CandidateView c)=>LayoutFilter switch {"1格"=>c.GridWidth*c.GridHeight==1,"2格·横放"=>c.GridWidth==2&&c.GridHeight==1,"2格·竖放"=>c.GridWidth==1&&c.GridHeight==2,"3格·横放"=>c.GridWidth==3&&c.GridHeight==1,"3格·竖放"=>c.GridWidth==1&&c.GridHeight==3,"4格"=>c.GridWidth*c.GridHeight==4,"6格·横放"=>c.GridWidth==3&&c.GridHeight==2,"6格·竖放"=>c.GridWidth==2&&c.GridHeight==3,"8格·横放"=>c.GridWidth==4&&c.GridHeight==2,"8格·竖放"=>c.GridWidth==2&&c.GridHeight==4,_=>true};
 private string sortField="价格",sortDirection="降序";
 private double hudOpacity=1;
 public double HudOpacity {get=>hudOpacity;set{var next=double.IsFinite(value)?Math.Clamp(value,.35,1):1;if(Math.Abs(next-hudOpacity)<.0001)return;hudOpacity=next;Changed();UserSettingsStore.Update(Store.Root,s=>s with{OverlayOpacity=next});}}
 public string SortField {get=>sortField;set{if(value is not("价格" or "品质" or "名字")||value==sortField)return;sortField=value;Changed();NotifySorted();UserSettingsStore.Update(Store.Root,s=>s with{SortField=value});}}
 public string SortDirection {get=>sortDirection;set{if(value is not("降序" or "升序")||value==sortDirection)return;sortDirection=value;Changed();NotifySorted();UserSettingsStore.Update(Store.Root,s=>s with{SortDirection=value});}}
 public IEnumerable<CandidateView> DisplayCandidates=>Candidates.Count==0?placeholders:SortCandidates(Candidates.Where(MatchesLayout));
 private CandidateView[] latestHud=[],previousHud=[];
 private string latestHudTime="",previousHudTime="";
 public IEnumerable<CandidateView> HudCandidates=>latestHud.Length==0?placeholders.Take(3):SortCandidates(latestHud.Where(MatchesLayout));
 public IEnumerable<CandidateView> PreviousHudCandidates=>SortCandidates(previousHud.Where(MatchesLayout));
 public string HudCurrentLabel=>latestHud.Length==0?"当前结果 · 等待首次有效匹配":$"{(ResultBadge is "本次不确定" or "正在分析"?"保留最近有效结果":"当前结果")} · {latestHudTime}";
 public string HudPreviousLabel=>previousHud.Length==0?"上一轮有效结果 · 暂无":$"上一轮有效结果 · {previousHudTime}";
 public string HudNotice=>ResultBadge=="本次不确定"?(latestHud.Length>0?"本次不确定 · 保留上次结果，请再拖动一次":"本次不确定 · 暂无有效结果，请再拖动一次"):ResultBadge=="正在分析"?"检测到声音 · 正在分析，已有结果继续保留":ResultNotice;
 public string HudFilterNote=>latestHud.Length>0&&!latestHud.Any(MatchesLayout)?"当前结果不符合规格筛选，请选择全部规格":"";
 private void RefreshHud(){foreach(var n in new[]{nameof(HudCandidates),nameof(PreviousHudCandidates),nameof(HudCurrentLabel),nameof(HudPreviousLabel),nameof(HudNotice),nameof(HudFilterNote)})Changed(n);}

 public Visibility EmptyHistoryVisibility=>History.Count==0?Visibility.Visible:Visibility.Collapsed;
 public string ResultBadge {get;private set;}="等待识别";
 public string MatchLabel {get;private set;}="相似度  —";
 public string LastResultLabel {get;private set;}="最近识别：暂无";
 public string ResultNotice {get;private set;}="等待声音";
 public double ResultOpacity {get;private set;}=1;
 public void MarkAnalyzing(){ResultBadge="正在分析";ResultNotice="检测到声音 · 正在分析…";ResultOpacity=.35;Changed(nameof(ResultBadge));Changed(nameof(ResultNotice));Changed(nameof(ResultOpacity));RefreshHud();}
 public void MarkUnmatched(string reason){
  Candidates.Clear();RecognitionStatus="本次不确定："+reason;ResultBadge="本次不确定";ResultNotice="已收到声音 · 不确定 · "+reason;ResultOpacity=1;MatchLabel="未达到匹配条件";LastResultLabel=$"最近分析：{DateTimeOffset.Now:HH:mm:ss}";
  History.Insert(0,$"{DateTimeOffset.Now:HH:mm:ss} · 不确定 · {reason}");HistoryEntries.Insert(0,new(DateTimeOffset.Now.ToString("HH:mm:ss"),"本次不确定",reason,null));while(History.Count>100)History.RemoveAt(History.Count-1);while(HistoryEntries.Count>100)HistoryEntries.RemoveAt(HistoryEntries.Count-1);
  foreach(var name in new[]{nameof(DisplayCandidates),nameof(HudCandidates),nameof(ResultBadge),nameof(ResultNotice),nameof(ResultOpacity),nameof(MatchLabel),nameof(LastResultLabel),nameof(EmptyHistoryVisibility),nameof(FilterSummary),nameof(NoFilterMatchesVisibility)})Changed(name);RefreshHud();
 }
 public int ItemCount=>Store.Data.Items.Count; public int GroupCount=>Store.Data.SoundGroups.Count;public int SampleCount=>Store.Data.SoundSamples.Count;
 public string ElapsedLabel=>"已运行：  "+(Listening?(DateTimeOffset.Now-startedAt).ToString(@"hh\:mm\:ss"):"00:00:00");
 public string ShortStatus=>Listening?"正在监听":"监听已停止";
 public Brush StatusBrush=>Listening?Brushes.SpringGreen:new SolidColorBrush(Color.FromRgb(111,140,157));
 private float[] spectrum=new float[64];public float[] Spectrum {get=>spectrum;private set{spectrum=value;Changed();}}
 private AudioDevice? selectedDevice;
 public AudioDevice? SelectedDevice { get=>selectedDevice; set { selectedDevice=value; Changed(); Changed(nameof(CanStart)); } }
 private bool listening,busy; public bool Listening { get=>listening; private set { listening=value; Changed(); Changed(nameof(CanStart)); Changed(nameof(CanStop)); Changed(nameof(CanChooseDevice));Changed(nameof(ShortStatus));Changed(nameof(StatusBrush)); } }
 public bool CanStart=>!Listening&&!busy&&SelectedDevice!=null; public bool CanStop=>Listening&&!busy; public bool CanChooseDevice=>!Listening&&!busy;
 private string status="待机 · 选择输出设备后开始监听"; public string Status { get=>status; private set { status=value; Changed(); } }
 private string recognitionStatus="首段识别待机 · 只需拿起声音，无需放下声音"; public string RecognitionStatus { get=>recognitionStatus; private set { recognitionStatus=value; Changed(); } }
 private readonly DisplayPeakMeter meter=new();
 private long lastMeterTick=System.Diagnostics.Stopwatch.GetTimestamp();private double levelDb=-60;
 private void ResetMeter(){meter.Reset();lastMeterTick=System.Diagnostics.Stopwatch.GetTimestamp();levelDb=-60;Level=0;Changed(nameof(LevelLabel));}
 private double level; public double Level { get=>level; private set { level=value; Changed(); } }
 public string LevelLabel=>Level<=0?"≤−60 dBFS":$"{levelDb:0} dBFS";
 private int detectedEvents,matchedEvents;
 public string DetectionSummary=>$"声音检测 {detectedEvents} 次 / 匹配 {matchedEvents} 次";
 private float[] waveform=new float[240]; public float[] Waveform { get=>waveform; private set { waveform=value; Changed(); } }
 public string CatalogSummary=>$"{ItemCount} 件物品  /  {GroupCount} 个声音组  /  {SampleCount} 段样本";
 public SessionState(CatalogStore store,Dispatcher dispatcher) { Store=store;InputGain=RecognitionGain.Load(store.Root);var settings=UserSettingsStore.Load(store.Root);hudOpacity=settings.OverlayOpacity;sortField=settings.SortField;sortDirection=settings.SortDirection; this.dispatcher=dispatcher; Audio.FrameAvailable+=frame=> { meter.Add(frame.Mono);Interlocked.Exchange(ref latest,frame);pipeline?.Submit(frame); }; Audio.CaptureStopped+=error=>dispatcher.BeginInvoke(()=>{ pipeline?.Dispose();pipeline=null; Listening=false; Status=error==null?"已停止监听":$"捕获中断：{error.Message} · 请重新选择设备"; ResetMeter(); }); timer=new DispatcherTimer(TimeSpan.FromMilliseconds(80),DispatcherPriority.Background,Tick,dispatcher); RefreshDevices(); }
 public void RefreshDevices() { if(Listening) return; try { var id=SelectedDevice?.Id??AudioRouting.RecommendedId(); Devices.Clear(); foreach(var d in AudioCaptureService.EnumerateDevices()) Devices.Add(d); SelectedDevice=Devices.FirstOrDefault(d=>d.Id==id)??Devices.FirstOrDefault(); Status=Devices.Count==0?"未找到可用输出设备，请连接扬声器或耳机":"待机 · 系统输出音频 / WASAPI Loopback"; Changed(nameof(CanStart)); } catch(Exception ex) { Status="设备枚举失败："+ex.Message; } }
 public void Start() { if(!CanStart) return; try { ResetMeter();Interlocked.Exchange(ref latest,null);detectedEvents=matchedEvents=0;Changed(nameof(DetectionSummary));StartPipeline();Audio.Start(SelectedDevice!.Id); startedAt=DateTimeOffset.Now; Listening=true; Status="监听："+SelectedDevice.Name; } catch(Exception ex) { pipeline?.Dispose();pipeline=null;Status=$"无法开始监听：{ex.Message}（0x{ex.HResult:X8}）· 请在音频诊断中导出诊断信息"; } }
 public async Task DetectGameOutputAsync(){var id=AudioRouting.RecommendedId();if(id==null)throw new InvalidOperationException("没有可用的输出设备");bool resume=Listening;if(resume)await StopAsync();RefreshDevices();SelectedDevice=Devices.FirstOrDefault(d=>d.Id==id)??SelectedDevice;if(resume)Start();else Status="已选择游戏活动输出或系统默认输出："+SelectedDevice?.Name;}
 public async Task StopAsync() { if(!CanStop) return; busy=true; Changed(nameof(CanStop)); try { await Audio.StopAsync(); Listening=false;ResetMeter(); Status="已停止监听"; } catch(Exception ex) { Status="停止失败："+ex.Message; } finally { busy=false; Changed(nameof(CanStart)); Changed(nameof(CanStop)); Changed(nameof(CanChooseDevice)); } }
 private void Tick(object? sender,EventArgs e) {
  long now=System.Diagnostics.Stopwatch.GetTimestamp();double elapsed=(now-lastMeterTick)/(double)System.Diagnostics.Stopwatch.Frequency;lastMeterTick=now;
  if(Listening){var reading=meter.Read(elapsed);levelDb=reading.Db;Level=reading.Value;}
  var frame=Interlocked.Exchange(ref latest,null);
  if(frame!=null&&Listening) { lastFrame=DateTimeOffset.Now; var peaks=new float[240]; for(int i=0;i<peaks.Length;i++) { int start=i*frame.Mono.Length/peaks.Length,end=(i+1)*frame.Mono.Length/peaks.Length; for(int j=start;j<end;j++) peaks[i]=Math.Max(peaks[i],Math.Abs(frame.Mono[j])); } Waveform=peaks; Spectrum=SpectrumAnalysis.Mel(Audio.Buffer.Snapshot(2048),frame.SampleRate); }
  else if(DateTimeOffset.Now-lastFrame>TimeSpan.FromMilliseconds(250)) { Waveform=new float[240]; if(Listening)Spectrum=new float[64]; }
  Changed(nameof(LevelLabel));Changed(nameof(ElapsedLabel));
 }
 private Catalog RecognitionCatalog(){
  var items=Store.Data.Items.Where(i=>MatchesLayout(new CandidateView(i.Name,"",null,GridWidth:i.GridWidth,GridHeight:i.GridHeight))).ToList();
  var groups=items.Select(i=>i.SoundGroupId).ToHashSet();
  return new Catalog{Items=items,SoundGroups=Store.Data.SoundGroups.Where(g=>groups.Contains(g.Id)).ToList(),SoundSamples=Store.Data.SoundSamples.Where(s=>groups.Contains(s.SoundGroupId)).ToList()};
 }
 private DateTimeOffset lastDecisionAt;
 private void StartPipeline(){pipeline?.Dispose();var active=new RecognitionPipeline(RecognitionCatalog(),Store.Root,InputGain);pipeline=active;active.AnalysisStarted+=()=>dispatcher.BeginInvoke(()=>{if(pipeline==active&&Listening&&DateTimeOffset.Now-lastDecisionAt>TimeSpan.FromMilliseconds(700))MarkAnalyzing();});active.Status+=text=>dispatcher.BeginInvoke(()=>{if(pipeline==active)RecognitionStatus=text;});active.Decision+=decision=>dispatcher.BeginInvoke(()=>{if(pipeline!=active||!Listening)return;lastDecisionAt=DateTimeOffset.Now;detectedEvents++;if(decision.Result!=null)matchedEvents++;Changed(nameof(DetectionSummary));if(decision.Result!=null)Publish(decision.Result);else MarkUnmatched(decision.Reason);});}
 public async Task TestFileAsync(string path){RecognitionStatus="正在分析录音文件…";var fileGain=InputGain;try{var snapshot=RecognitionCatalog();var decisions=await Task.Run(()=>{var engine=new WaveformRecognitionService();engine.Load(snapshot,Store.Root);var audio=AudioFiles.Read(path);var detector=new PickupEventDetector();var results=new List<MatchDecision>();int step=audio.SampleRate/50;for(int i=0;i<audio.Mono.Length;i+=step){foreach(var sound in detector.Push(RecognitionGain.Apply(new(audio.Mono[i..Math.Min(audio.Mono.Length,i+step)],audio.SampleRate,DateTimeOffset.Now),fileGain)))results.Add(engine.Compare(sound,snapshot));}foreach(var sound in detector.Push(new(new float[audio.SampleRate/5],audio.SampleRate,DateTimeOffset.Now)))results.Add(engine.Compare(sound,snapshot));return results;});foreach(var decision in decisions)if(decision.Result!=null)Publish(decision.Result);else MarkUnmatched(decision.Reason);RecognitionStatus=$"文件测试：检测 {decisions.Count} 段，匹配 {decisions.Count(d=>d.Result!=null)} 段，拒绝 {decisions.Count(d=>d.Result==null)} 段 · 相似度不是准确率";}catch(Exception ex){RecognitionStatus="文件分析失败："+ex.Message;}}
 public void Demo() { var result=recognition.Recognize(new(Audio.Buffer.Snapshot(),Audio.SampleRate,DateTimeOffset.Now),Store.Data); if(result!=null) Publish(result); }
 public void Publish(RecognitionResult result) {
  if(!dispatcher.CheckAccess()) { dispatcher.BeginInvoke(()=>Publish(result)); return; }
  Candidates.Clear();int index=0;
  ResultNotice=result.IsMock?"模拟演示":"匹配 ≥70% · 同声物品请结合规格判断";ResultOpacity=1;Changed(nameof(ResultNotice));Changed(nameof(ResultOpacity));
  foreach(var c in result.Candidates) { var image=ItemImage.Load(c.Item,Store);
   Candidates.Add(new(c.Item.Name,$"{c.Similarity:P1}",image,index,index==0,c.Similarity*100,result.IsMock?"MOCK · 同一声音组":ItemImage.Layout(c.Item)+" · 同声",c.Item.GridWidth,c.Item.GridHeight,ReferencePriceCatalog.FormatWan(c.Item.Price),c.Item.Price,c.Item.Quality??"",c.Item.QualityColor??"",QualityRank(c.Item.Quality)));index++;
  }
  previousHud=latestHud;previousHudTime=latestHudTime;latestHud=Candidates.ToArray();latestHudTime=result.Timestamp.ToString("HH:mm:ss");
  var group=Store.Data.SoundGroups.FirstOrDefault(g=>g.Id==result.SoundGroupId)?.Name??result.SoundGroupId;
  RecognitionStatus=$"{(result.IsMock?"MOCK 演示":"首段识别")} · {group} · {result.Candidates.Count} 个候选 · 实验相似度，非准确率";
  ResultBadge=result.IsMock?"MOCK 演示":"匹配到声音组";MatchLabel=result.Candidates.Count==0?"无候选":$"相似度  {result.Candidates.Max(c=>c.Similarity):P1}";LastResultLabel=$"最近识别：{result.Timestamp:HH:mm:ss}";
  History.Insert(0,$"{result.Timestamp:HH:mm:ss}  ·  {RecognitionStatus}");while(History.Count>100) History.RemoveAt(History.Count-1);
  HistoryEntries.Insert(0,new(result.Timestamp.ToString("HH:mm:ss"),Candidates.FirstOrDefault()?.Name??group,$"{(result.IsMock?"MOCK · ":"")}共 {Candidates.Count} 个候选",Candidates.FirstOrDefault()));while(HistoryEntries.Count>100)HistoryEntries.RemoveAt(HistoryEntries.Count-1);
  foreach(var name in new[]{nameof(DisplayCandidates),nameof(HudCandidates),nameof(ResultBadge),nameof(MatchLabel),nameof(LastResultLabel),nameof(EmptyHistoryVisibility),nameof(FilterSummary),nameof(NoFilterMatchesVisibility)})Changed(name);RefreshHud();
 }
 public void ClearHistory(){History.Clear();HistoryEntries.Clear();Changed(nameof(EmptyHistoryVisibility));}
 private IEnumerable<CandidateView> SortCandidates(IEnumerable<CandidateView> source) {
  var ordered=sortField switch {
   "品质"=>source.OrderBy(c=>c.QualityRank).ThenBy(c=>c.Name,StringComparer.CurrentCulture),
   "名字"=>source.OrderBy(c=>c.Name,StringComparer.CurrentCulture),
   _=>source.OrderBy(c=>c.PriceValue??int.MinValue).ThenBy(c=>c.Name,StringComparer.CurrentCulture)
  };
  return sortDirection=="升序"?ordered:ordered.Reverse();
 }
 private static int QualityRank(string? quality)=>quality switch {"红色"=>5,"金色"=>4,"紫色"=>3,"蓝色"=>2,"白色"=>1,_=>0};
 private void NotifySorted(){foreach(var name in new[]{nameof(DisplayCandidates),nameof(HudCandidates),nameof(PreviousHudCandidates)})Changed(name);}
 public void CatalogChanged() { if(Listening)StartPipeline(); Changed(nameof(CatalogSummary));Changed(nameof(ItemCount));Changed(nameof(GroupCount));Changed(nameof(SampleCount)); }
 public void Dispose() { timer.Stop();pipeline?.Dispose();pipeline=null; Audio.Dispose(); }
}





