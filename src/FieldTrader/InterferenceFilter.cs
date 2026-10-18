namespace FieldTrader;

// Conservative voiced/tonal interference rejection, not speech separation.
// Reject only sustained, strongly periodic signals; mixed or unvoiced speech
// still needs the normal template score and ambiguity checks.
public static class InterferenceFilter {
 public static string? Reason(float[] audio) {
  const int size=640;
  int stable=0;double min=double.MaxValue,max=0;
  for(int start=0;start+size<=Math.Min(audio.Length,6720);start+=size) {
   double energy=0;for(int i=0;i<size;i++)energy+=audio[start+i]*audio[start+i];
   if(energy<1e-10){stable=0;min=double.MaxValue;max=0;continue;}
   double best=0;
   for(int lag=40;lag<=200;lag+=2) {
    double dot=0,a=0,b=0;
    for(int i=lag;i<size;i++){double x=audio[start+i],y=audio[start+i-lag];dot+=x*y;a+=x*x;b+=y*y;}
    if(a*b>0)best=Math.Max(best,dot/Math.Sqrt(a*b));
   }
   if(best<.85){stable=0;min=double.MaxValue;max=0;continue;}
   stable++;min=Math.Min(min,energy);max=Math.Max(max,energy);
   if(stable>=5&&max/min<2.25)return "疑似持续人声或音调干扰，本次不确定";
  }
  return null;
 }
}
