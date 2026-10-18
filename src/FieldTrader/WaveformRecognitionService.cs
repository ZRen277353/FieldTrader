using System.IO;
using NAudio.Dsp;
namespace FieldTrader;

// Correlates pickup waveforms after removing gain and low-frequency ambience.
// The legacy spectral matcher remains available for audit comparisons only.
public sealed class WaveformRecognitionService : IAudioRecognitionService {
 public const double Threshold=.70, Margin=.04;
 private const int Size=16384, Bits=14, Rate=16000;
 private sealed record Template(string Group,Complex[] Fft,double[] Energy,int Length);
 private readonly List<Template> templates=[],outdoorTemplates=[];
 public int TemplateCount=>templates.Count;
 public List<string> LoadErrors {get;}=[];
 public void Load(Catalog catalog,string root){templates.Clear();outdoorTemplates.Clear();LoadErrors.Clear();foreach(var s in FirstSound.Templates(catalog))try{
  var a=AudioFiles.Read(Path.Combine(root,s.FilePath));var x=Prepare(a);if(x.Length>Rate*.55)x=x[..(int)(Rate*.55)];if(x.Length<1600){LoadErrors.Add(s.FilePath+": 有效声音不足100毫秒");continue;}
  AddTemplate(s.SoundGroupId,x,templates);var outdoor=Prepare(a,1500);AddTemplate(s.SoundGroupId,outdoor[..Math.Min(outdoor.Length,(int)(Rate*.55))],outdoorTemplates);
 }catch(Exception e){LoadErrors.Add(s.FilePath+": "+e.Message);}}
 private static void AddTemplate(string group,float[] x,List<Template> bank){
  var fft=new Complex[Size];var energy=new double[x.Length+1];for(int i=0;i<x.Length;i++){fft[i].X=x[i];energy[i+1]=energy[i]+x[i]*x[i];}FastFourierTransform.FFT(true,Bits,fft);bank.Add(new(group,fft,energy,x.Length));
 }
 private static float[] Prepare(AudioFrame a,int cutoff=300){var source=SoundFeaturesExtractor.Resample(a.Mono,a.SampleRate);var x=new float[source.Length];double lastIn=0,lastOut=0;double alpha=Math.Exp(-2*Math.PI*cutoff/Rate);for(int i=0;i<x.Length;i++){double v=source[i];double y=alpha*(lastOut+v-lastIn);lastIn=v;lastOut=y;x[i]=(float)y;}return x;}
 public RecognitionResult? Recognize(AudioFrame audio,Catalog catalog)=>Compare(audio,catalog).Result;
 public MatchDecision Compare(AudioFrame audio,Catalog catalog){
  var x=Prepare(audio);var interference=InterferenceFilter.Reason(x);if(interference!=null)return new(null,Array.Empty<GroupMatch>(),interference);
  var normal=CompareCore(x,audio.Timestamp,catalog,templates);
  // Retry only in measured low-frequency background, and only confirm the same
  // independently leading group. A filtered-only winner cannot create a label.
  if(normal.Result!=null||normal.Ranking.Count==0||normal.Ranking[0].Score<.50||!HasOutdoorBackground(audio))return normal;
  if(normal.Ranking.Count>1&&normal.Ranking[0].Score-normal.Ranking[1].Score<Margin)return normal;
  var reduced=CompareCore(Prepare(audio,1500),audio.Timestamp,catalog,outdoorTemplates);
  if(reduced.Result!=null&&reduced.Result.SoundGroupId==normal.Ranking[0].GroupId)
   return new(reduced.Result,reduced.Ranking,"户外低频降噪复核通过70% · 原始与降噪声音指向同组");
  return normal;
 }
 private static bool HasOutdoorBackground(AudioFrame audio){
  if(audio.BackgroundMono is not {Length:>0} bg||bg.Length<audio.SampleRate/5)return false;
  var frame=new AudioFrame(bg,audio.SampleRate,audio.Timestamp);var normal=Prepare(frame);var reduced=Prepare(frame,1500);
  double energy=normal.Sum(v=>(double)v*v);return energy/normal.Length>1e-12&&reduced.Sum(v=>(double)v*v)<energy*.45;
 }
 private static MatchDecision CompareCore(float[] x,DateTimeOffset timestamp,Catalog catalog,IReadOnlyList<Template> bank){double queryEnergy=x.Take(6720).Sum(v=>(double)v*v);var scores=new Dictionary<string,double>();
  // Ignore near-silent query/reference tails: float FFT roundoff and shared background
  // must not win over the actual pickup transient.
  foreach(int n in new[]{1600,2880})for(int start=0;start+n<=Math.Min(x.Length,6720);start+=480){
   double qe=0;var q=new Complex[Size];for(int j=0;j<n;j++){float v=x[start+j];qe+=v*v;q[n-1-j].X=v;}if(qe<1e-12||qe<queryEnergy*.05)continue;FastFourierTransform.FFT(true,Bits,q);
   foreach(var t in bank){if(t.Length<n)continue;var c=new Complex[Size];for(int k=0;k<Size;k++){c[k].X=t.Fft[k].X*q[k].X-t.Fft[k].Y*q[k].Y;c[k].Y=t.Fft[k].X*q[k].Y+t.Fft[k].Y*q[k].X;}FastFourierTransform.FFT(false,Bits,c);double best=0;for(int j=0;j<=t.Length-n;j++){double re=t.Energy[j+n]-t.Energy[j];if(re<t.Energy[^1]*.05||re<=0)continue;double value=Math.Abs(c[j+n-1].X)*Size/Math.Sqrt(re*qe);best=Math.Max(best,Math.Min(1,value));}scores[t.Group]=Math.Max(scores.GetValueOrDefault(t.Group),best);}
  }
  var ranking=scores.Select(s=>new GroupMatch(s.Key,1-s.Value,s.Value)).OrderByDescending(s=>s.Score).ToArray();if(ranking.Length==0)return new(null,ranking,"未取得可比较的短声音");var first=ranking[0];
  if(first.Score<Threshold)return new(null,ranking,$"最高相似度 {first.Score:P0}，未达到70%");
  if(ranking.Length>1&&first.Score-ranking[1].Score<Margin)return new(null,ranking,"多个声音组太接近，请选择占格规格后再拖动");
  return new(new(timestamp,first.GroupId,catalog.Items.Where(i=>i.SoundGroupId==first.GroupId).Select(i=>new Candidate(i,first.Score)).ToArray(),false),ranking,"波形匹配通过70%门槛");
 }
}

// A high-band transient can be short and much quieter than background ambience.
public sealed class PickupEventDetector : IAudioEventDetector {
 private readonly List<float> pending=[];private readonly Queue<float> recent=[];private readonly Queue<double> noise=[];private readonly List<float> active=[];
 private readonly Queue<float> background=[];private float[] eventBackground=[];
 private int blocks,cooldown;private DateTimeOffset onset;
 public event Action? Detected;
 public void Reset(){pending.Clear();recent.Clear();background.Clear();eventBackground=[];noise.Clear();active.Clear();blocks=cooldown=0;}
 public AudioFrame? Process(AudioFrame f)=>Push(f).FirstOrDefault();
 public IReadOnlyList<AudioFrame> Push(AudioFrame frame){var result=new List<AudioFrame>();pending.AddRange(SoundFeaturesExtractor.Resample(frame.Mono,frame.SampleRate));int offset=0;
  while(pending.Count-offset>=160){var part=pending.GetRange(offset,160);offset+=160;blocks++;foreach(var v in part){recent.Enqueue(v);background.Enqueue(v);}while(background.Count>8640)background.Dequeue();while(recent.Count>640)recent.Dequeue();var values=recent.ToArray();double high=0;
   if(values.Length>=512){var fft=new Complex[512];for(int i=0;i<512;i++)fft[i].X=values[values.Length-512+i]*(float)(.5-.5*Math.Cos(2*Math.PI*i/511));FastFourierTransform.FFT(true,9,fft);for(int k=160;k<=255;k++)high+=fft[k].X*fft[k].X+fft[k].Y*fft[k].Y;}
   var sorted=noise.Order().ToArray();double floor=sorted.Length>0?sorted[sorted.Length/2]:high;bool burst=blocks>30&&high>Math.Max(1e-11,floor*20);
   noise.Enqueue(high);while(noise.Count>80)noise.Dequeue();
   if(active.Count>0){active.AddRange(part);if(active.Count>=6720){result.Add(new(active.ToArray(),16000,onset,eventBackground));active.Clear();cooldown=20;}}
   else if(cooldown>0)cooldown--;
   else if(burst){onset=frame.Timestamp;eventBackground=background.Take(Math.Max(0,background.Count-640)).ToArray();active.AddRange(values);Detected?.Invoke();}
  }
  if(offset>0)pending.RemoveRange(0,offset);return result;
 }
}
