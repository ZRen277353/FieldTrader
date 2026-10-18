using System.IO;
using NAudio.Dsp;
using NAudio.Wave;
namespace FieldTrader;

public static class AudioFiles {
 public static AudioFrame Read(string file) {
  using var reader=new AudioFileReader(file); int channels=reader.WaveFormat.Channels; var chunk=new float[reader.WaveFormat.SampleRate*channels]; var mono=new List<float>();int count;
  while((count=reader.Read(chunk,0,chunk.Length))>0){for(int i=0;i+channels<=count;i+=channels){double sum=0;for(int ch=0;ch<channels;ch++)sum+=chunk[i+ch];mono.Add((float)(sum/channels));}if(mono.Count>reader.WaveFormat.SampleRate*120)throw new InvalidDataException("请使用两分钟以内的声音样本");}
  return new(mono.ToArray(),reader.WaveFormat.SampleRate,DateTimeOffset.Now);
 }
}

public sealed record SoundFeatures(float[][] Frames,float[] Mean);
public static class SoundFeaturesExtractor {
 public const int Rate=16000, Size=512, Hop=160, Bands=32;
 private static readonly double[,] filters=MakeFilters();
 private static double[,] MakeFilters(){var bank=new double[Bands,Size/2+1];double maxMel=2595*Math.Log10(1+8000d/700);var hz=Enumerable.Range(0,Bands+2).Select(i=>700*(Math.Pow(10,i*maxMel/(Bands+1)/2595)-1)).ToArray();for(int b=0;b<Bands;b++)for(int k=0;k<=Size/2;k++){double f=(double)k*Rate/Size;bank[b,k]=Math.Max(0,Math.Min((f-hz[b])/(hz[b+1]-hz[b]),(hz[b+2]-f)/(hz[b+2]-hz[b+1])));}return bank;}
 public static float[] Resample(float[] input,int rate){if(rate==Rate)return input.ToArray();int length=(int)((long)input.Length*Rate/rate);var result=new float[length];double cutoff=Math.Min(1,(double)Rate/rate);const int half=24;for(int i=0;i<length;i++){double pos=(double)i*rate/Rate,sum=0,weight=0;int center=(int)pos;for(int j=center-half;j<=center+half;j++){if(j<0||j>=input.Length)continue;double d=j-pos,v=d*cutoff;double sinc=Math.Abs(v)<1e-9?1:Math.Sin(Math.PI*v)/(Math.PI*v);double w=sinc*(.5+.5*Math.Cos(Math.PI*d/(half+1)))*cutoff;sum+=input[j]*w;weight+=w;}result[i]=(float)(sum/Math.Max(weight,1e-9));}return result;}
 public static SoundFeatures? Extract(AudioFrame audio){
  if(audio.Mono.Length<audio.SampleRate*.08||audio.Mono.Length>audio.SampleRate*2)return null;
  var x=Resample(audio.Mono,audio.SampleRate);int blocks=(x.Length+Hop-1)/Hop;var energy=new double[blocks];for(int b=0;b<blocks;b++){int end=Math.Min(x.Length,(b+1)*Hop);for(int j=b*Hop;j<end;j++)energy[b]+=x[j]*x[j];energy[b]=Math.Sqrt(energy[b]/Math.Max(1,end-b*Hop));}
  double maximum=energy.Max();if(maximum<1e-6)return null;int first=Array.FindIndex(energy,v=>v>maximum*.035),last=Array.FindLastIndex(energy,v=>v>maximum*.035);int start=Math.Max(0,(first-2)*Hop),finish=Math.Min(x.Length,(last+3)*Hop);x=x[start..finish];if(x.Length<Size)return null;
  float peak=x.Max(v=>Math.Abs(v));for(int i=0;i<x.Length;i++)x[i]/=Math.Max(peak,1e-9f);
  var noisePower=audio.BackgroundMono is {Length:>0}?EstimateNoise(audio.BackgroundMono,audio.SampleRate,peak):null;
  var frames=new List<float[]>();var frameEnergy=new List<double>();var mean=new float[Bands];
  for(int offset=0;offset+Size<=x.Length;offset+=Hop){var fft=new Complex[Size];for(int i=0;i<Size;i++)fft[i].X=x[offset+i]*(float)(.5-.5*Math.Cos(2*Math.PI*i/(Size-1)));FastFourierTransform.FFT(true,9,fft);var mel=new double[Bands];double total=0;for(int b=0;b<Bands;b++){for(int k=0;k<=Size/2;k++)mel[b]+=filters[b,k]*Math.Max(0,(fft[k].X*fft[k].X+fft[k].Y*fft[k].Y)-(noisePower?[k]??0));total+=mel[b];}var row=new float[Bands];double norm=0;for(int b=0;b<Bands;b++){row[b]=(float)Math.Log(1+mel[b]/Math.Max(total,1e-20)*1000);norm+=row[b]*row[b];}norm=Math.Sqrt(norm);for(int b=0;b<Bands;b++){row[b]/=(float)Math.Max(norm,1e-9);mean[b]+=row[b];}frames.Add(row);frameEnergy.Add(total);}
  if(frameEnergy.Count>0){double gate=frameEnergy.Max()*.01;int firstFrame=frameEnergy.FindIndex(v=>v>gate),lastFrame=frameEnergy.FindLastIndex(v=>v>gate);if(firstFrame<0)return null;frames=frames.Skip(firstFrame).Take(lastFrame-firstFrame+1).ToList();Array.Clear(mean);foreach(var row in frames)for(int b=0;b<Bands;b++)mean[b]+=row[b];}
  if(frames.Count<4)return null;for(int b=0;b<Bands;b++)mean[b]/=frames.Count;return new(frames.ToArray(),mean);
 }
 private static double[]? EstimateNoise(float[] input,int rate,float peak){
  if(input.Length<rate*.12)return null;var x=Resample(input,rate);var powers=new List<double[]>();
  for(int offset=0;offset+Size<=x.Length;offset+=Hop){var fft=new Complex[Size];for(int i=0;i<Size;i++)fft[i].X=x[offset+i]/Math.Max(peak,1e-9f)*(float)(.5-.5*Math.Cos(2*Math.PI*i/(Size-1)));FastFourierTransform.FFT(true,9,fft);powers.Add(Enumerable.Range(0,Size/2+1).Select(k=>(double)fft[k].X*fft[k].X+(double)fft[k].Y*fft[k].Y).ToArray());}
  if(powers.Count==0)return null;var result=new double[Size/2+1];for(int k=0;k<result.Length;k++)result[k]=powers.Average(p=>p[k]);return result;
 }
 private static double Dot(float[] a,float[] b){double sum=0;for(int i=0;i<a.Length;i++)sum+=a[i]*b[i];return sum;}
 public static double PrefixDistance(SoundFeatures query,SoundFeatures reference){
  double best=Distance(query,reference);int n=query.Frames.Length;
  // Only compare the beginning of the pickup template, never arbitrary
  // subsequences or a release/drop template. Require enough evidence.
  if(n>=8&&n+3<reference.Frames.Length)for(int length=Math.Max(8,n-1);length<=Math.Min(reference.Frames.Length-1,n+2);length++){
   var frames=reference.Frames.Take(length).ToArray();var mean=new float[Bands];foreach(var row in frames)for(int b=0;b<Bands;b++)mean[b]+=row[b]/length;
   best=Math.Min(best,Distance(query,new(frames,mean))+.005);
  }
  return best;
 }
 public static double Distance(SoundFeatures a,SoundFeatures b){int n=a.Frames.Length,m=b.Frames.Length;var prev=Enumerable.Repeat(1e6,m+1).ToArray();prev[0]=0;for(int i=1;i<=n;i++){var next=Enumerable.Repeat(1e6,m+1).ToArray();int center=(int)((double)i*m/n);for(int j=Math.Max(1,center-10);j<=Math.Min(m,center+10);j++){double cost=Math.Max(0,1-Dot(a.Frames[i-1],b.Frames[j-1]));next[j]=cost+Math.Min(prev[j-1],Math.Min(prev[j],next[j-1]));}prev=next;}double cosine=1-Dot(a.Mean,b.Mean)/Math.Max(1e-12,Math.Sqrt(Dot(a.Mean,a.Mean)*Dot(b.Mean,b.Mean)));return Math.Max(0,.7*prev[m]/Math.Max(n,m)+.3*cosine+.04*Math.Abs(Math.Log((double)n/m)));}
}

public sealed record GroupMatch(string GroupId,double Distance,double Score);
public sealed record MatchDecision(RecognitionResult? Result,IReadOnlyList<GroupMatch> Ranking,string Reason);
public sealed class TemplateRecognitionService : IAudioRecognitionService {
 private sealed record Template(string GroupId,SoundFeatures Features);
 private Template[] templates=[];
 public const double MaxDistance=.10,MinMargin=.045;
 public int TemplateCount=>templates.Length;
 public List<string> LoadErrors {get;}=[];
 public void Load(Catalog catalog,string root){var list=new List<Template>();LoadErrors.Clear();foreach(var sample in FirstSound.Templates(catalog)){try{var feature=SoundFeaturesExtractor.Extract(FirstSound.Window(AudioFiles.Read(Path.Combine(root,sample.FilePath))));if(feature!=null)list.Add(new(sample.SoundGroupId,feature));else LoadErrors.Add(sample.FilePath+"：请导入单次操作的短样本（0.08–2秒）");}catch(Exception ex){LoadErrors.Add(sample.FilePath+"："+ex.Message);}}templates=list.ToArray();}
 public RecognitionResult? Recognize(AudioFrame frame,Catalog catalog)=>Compare(frame,catalog).Result;
 public MatchDecision Compare(AudioFrame frame,Catalog catalog)=>CompareCore(FirstSound.Window(frame),catalog);
 private MatchDecision CompareCore(AudioFrame frame,Catalog catalog){var feature=SoundFeaturesExtractor.Extract(frame);if(feature==null)return new(null,[],"片段太短、太长或没有有效声音");var ranking=templates.GroupBy(t=>t.GroupId).Select(g=>{double d=g.Min(t=>SoundFeaturesExtractor.PrefixDistance(feature,t.Features));return new GroupMatch(g.Key,d,Math.Exp(-4*d));}).OrderBy(g=>g.Distance).ToArray();if(ranking.Length==0)return new(null,ranking,"未加载有效声音模板");var best=ranking[0];if(best.Distance>MaxDistance)return new(null,ranking,"与声音库差异过大");if(ranking.Length>1&&ranking[1].Distance-best.Distance<MinMargin&&!(best.Distance<=.01&&ranking[1].Distance-best.Distance>=.01))return new(null,ranking,"候选声音过于接近，暂不确定");var candidates=catalog.Items.Where(i=>i.SoundGroupId==best.GroupId).Select(i=>new Candidate(i,best.Score)).ToArray();return new(new(frame.Timestamp,best.GroupId,candidates,false),ranking,"首段声音匹配通过（无需放下声；相似度不是准确率）");}
}

// Energy gate with pre-roll, hysteresis, short-event limits and silence release.
public sealed class EnergyEventDetector(bool firstSoundOnly=false) : IAudioEventDetector {
 private readonly List<float> pending=[];private readonly Queue<float> pre=[];private readonly List<float> active=[];private readonly Queue<AudioFrame> ready=[];
 private readonly Queue<double> background=[];
 private readonly Queue<float> backgroundAudio=[];private float[]? eventBackground;
 private int rate,quiet,voiced;private double noise=.00001;private bool recording,overlong;
 public void Reset(){pending.Clear();pre.Clear();active.Clear();ready.Clear();background.Clear();backgroundAudio.Clear();eventBackground=null;rate=quiet=voiced=0;noise=.00001;recording=overlong=false;}
 public AudioFrame? Process(AudioFrame frame){foreach(var e in Push(frame))ready.Enqueue(e);return ready.Count==0?null:ready.Dequeue();}
 public IReadOnlyList<AudioFrame> Push(AudioFrame frame){if(rate!=frame.SampleRate){Reset();rate=frame.SampleRate;}pending.AddRange(frame.Mono);int block=Math.Max(1,rate/100),offset=0;var result=new List<AudioFrame>();
  while(pending.Count-offset>=block){var chunk=pending.GetRange(offset,block);offset+=block;double rms=Math.Sqrt(chunk.Sum(v=>(double)v*v)/block);// Update the background estimate even while an event is active or cooling
   // down. Otherwise a continuous ambience permanently latches overlong.
   double floor=noise;
   if(firstSoundOnly&&background.Count>=10){var levels=background.Order().ToArray();floor=levels[levels.Length/5];}
   double threshold=Math.Max(.00018,floor*(firstSoundOnly?2.5:6));bool loud=rms>threshold;
   if(firstSoundOnly){background.Enqueue(rms);while(background.Count>100)background.Dequeue();}
   if(!recording&&!overlong){if(loud){recording=true;eventBackground=firstSoundOnly&&backgroundAudio.Count>=rate*.15?backgroundAudio.ToArray():null;active.AddRange(pre);pre.Clear();quiet=voiced=0;}else noise=.98*noise+.02*Math.Min(rms,threshold);}
   if(recording){active.AddRange(chunk);if(loud){quiet=0;voiced++;}else quiet++;if(firstSoundOnly&&active.Count>=rate*(FirstSound.Seconds+.04)){if(voiced>=4)result.Add(new(active.ToArray(),rate,frame.Timestamp,eventBackground));recording=false;active.Clear();overlong=true;}else if(active.Count>rate*1.5){recording=false;active.Clear();overlong=true;}else if(quiet>=9){if(voiced>=6)result.Add(new(active.ToArray(),rate,frame.Timestamp,eventBackground));active.Clear();recording=false;quiet=voiced=0;}}
   else if(overlong){quiet=loud?0:quiet+1;if(quiet>=9){overlong=false;quiet=0;}}
   else{foreach(var value in chunk)pre.Enqueue(value);while(pre.Count>block*4)pre.Dequeue();}
   if(firstSoundOnly){foreach(var value in chunk)backgroundAudio.Enqueue(value);while(backgroundAudio.Count>rate*.4)backgroundAudio.Dequeue();}
  } if(offset>0)pending.RemoveRange(0,offset);return result;
 }
}


