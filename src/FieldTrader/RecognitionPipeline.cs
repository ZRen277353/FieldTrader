using System.Threading.Channels;
namespace FieldTrader;
public sealed class RecognitionPipeline : IDisposable {
 // Detection must keep reading audio while the slower matcher is working.
 private readonly Channel<AudioFrame> input=Channel.CreateBounded<AudioFrame>(new BoundedChannelOptions(16){FullMode=BoundedChannelFullMode.Wait,SingleReader=true,SingleWriter=true});
 private readonly Channel<AudioFrame> events=Channel.CreateBounded<AudioFrame>(new BoundedChannelOptions(1){FullMode=BoundedChannelFullMode.DropOldest,SingleReader=true,SingleWriter=true});
 private readonly CancellationTokenSource cancel=new();private int gap;private readonly double gain;
 public event Action<MatchDecision>? Decision;
 public event Action? AnalysisStarted;
 public event Action<string>? Status;
 public Task Completion {get;}
 public RecognitionPipeline(Catalog source,string root,double gain=4):this(CreateMatcher(source,root),gain){}
 private static Func<Func<AudioFrame,MatchDecision>> CreateMatcher(Catalog source,string root){
  var catalog=new Catalog{Items=source.Items.ToList(),SoundGroups=source.SoundGroups.ToList(),SoundSamples=source.SoundSamples.ToList()};
  return ()=>{var engine=new WaveformRecognitionService();engine.Load(catalog,root);return frame=>engine.Compare(frame,catalog);};
 }
 internal RecognitionPipeline(Func<Func<AudioFrame,MatchDecision>> initialize,double gain=1){
  this.gain=Math.Clamp(gain,1,8);
  Completion=Task.WhenAll(Task.Run(DetectAsync),Task.Run(()=>MatchAsync(initialize)));
 }
 private async Task DetectAsync(){
  var detector=new PickupEventDetector();int lastRate=48000;Task<bool>? available=null;
  try{while(!cancel.IsCancellationRequested){
   available??=input.Reader.WaitToReadAsync(cancel.Token).AsTask();
   if(await Task.WhenAny(available,Task.Delay(120,cancel.Token))!=available){foreach(var sound in detector.Push(new(new float[lastRate/8],lastRate,DateTimeOffset.Now)))events.Writer.TryWrite(sound);continue;}
   if(!await available)break;available=null;
   if(!input.Reader.TryRead(out var frame))continue;
   if(Interlocked.Exchange(ref gap,0)>0||DateTimeOffset.Now-frame.Timestamp>TimeSpan.FromMilliseconds(600)){
    detector.Reset();while(input.Reader.TryRead(out var newer))frame=newer;
    Status?.Invoke("已跳过过期音频，继续识别最新声音");
    if(DateTimeOffset.Now-frame.Timestamp>TimeSpan.FromMilliseconds(600))continue;
   }
   lastRate=frame.SampleRate;foreach(var sound in detector.Push(RecognitionGain.Apply(frame,gain)))events.Writer.TryWrite(sound);
  }}catch(OperationCanceledException){}catch(Exception ex){Fail("声音检测异常："+ex.Message);}finally{events.Writer.TryComplete();}
 }
 private async Task MatchAsync(Func<Func<AudioFrame,MatchDecision>> initialize){
  try{var compare=initialize();Status?.Invoke($"实时识别已就绪 · 识别增益{gain:0}倍 · 70%门槛 · 优先最新声音");
   await foreach(var sound in events.Reader.ReadAllAsync(cancel.Token)){
    if(DateTimeOffset.Now-sound.Timestamp>TimeSpan.FromSeconds(2))continue;
    AnalysisStarted?.Invoke();var decision=compare(sound);
    if(cancel.IsCancellationRequested)break;
    Decision?.Invoke(decision);
   }
  }catch(OperationCanceledException){}catch(Exception ex){Fail("识别服务异常："+ex.Message);}
 }
 private void Fail(string reason){if(cancel.IsCancellationRequested)return;Decision?.Invoke(new(null,Array.Empty<GroupMatch>(),reason));Status?.Invoke(reason);cancel.Cancel();}
 public void Submit(AudioFrame frame){if(!input.Writer.TryWrite(frame))Interlocked.Exchange(ref gap,1);}
 public void Dispose(){input.Writer.TryComplete();cancel.Cancel();}
}
