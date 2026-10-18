namespace FieldTrader;

// Display-only meter. No gain is applied to capture or recognition samples.
public sealed class DisplayPeakMeter {
 private readonly object gate=new();
 private double pendingPeak,displayDb=-60;
 public void Add(float[] samples){double peak=0;foreach(float value in samples)if(float.IsFinite(value))peak=Math.Max(peak,Math.Abs(value));lock(gate)pendingPeak=Math.Max(pendingPeak,peak);}
 public (double Value,double Db) Read(double elapsedSeconds){lock(gate){
  double target=pendingPeak>0?Math.Clamp(20*Math.Log10(pendingPeak),-60,0):-60;pendingPeak=0;
  displayDb=Math.Max(target,displayDb-90*Math.Max(0,elapsedSeconds));
  return ((displayDb+60)*100/60,displayDb);
 }}
 public void Reset(){lock(gate){pendingPeak=0;displayDb=-60;}}
}
