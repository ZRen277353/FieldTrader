namespace FieldTrader;

// The same bounded onset window is used for library samples and live events.
// Later release/drop audio cannot affect normalization, alignment or matching.
public static class FirstSound {
 public const double Seconds=.24;
 public static AudioFrame Window(AudioFrame audio) {
  int block=Math.Max(1,audio.SampleRate/100),first=-1;
  double noise=.00001;
  for(int i=0;i+block<=audio.Mono.Length;i+=block){
   double rms=0;for(int j=i;j<i+block;j++)rms+=(double)audio.Mono[j]*audio.Mono[j];rms=Math.Sqrt(rms/block);
   double threshold=Math.Max(.00018,noise*6);
   if(rms>threshold){first=i;break;}
   noise=.98*noise+.02*Math.Min(rms,threshold);
  }
  if(first<0)return new([],audio.SampleRate,audio.Timestamp);
  int start=Math.Max(0,first-block*2),end=Math.Min(audio.Mono.Length,first+(int)(audio.SampleRate*Seconds));
  return new(audio.Mono[start..end],audio.SampleRate,audio.Timestamp,audio.BackgroundMono);
 }
 public static IEnumerable<SoundSample> Templates(Catalog catalog) {
  // Each historical recording starts with pickup. Do not assume odd/even
  // numbering: some old detector clips contain both actions in one event.
  return catalog.SoundSamples.Where(s=>s.SourceBatch!="music-202609-v04")
   .Concat(catalog.SoundSamples.Where(s=>s.SourceBatch=="music-202609-v04")
    .GroupBy(s=>s.SourceRecording).Select(g=>g.OrderBy(s=>s.StartSeconds).First()));
 }
}

