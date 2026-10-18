using System.Windows;
using System.Windows.Media;
namespace FieldTrader;
public sealed class WaveformView : FrameworkElement {
 public static readonly DependencyProperty SamplesProperty=DependencyProperty.Register(nameof(Samples),typeof(float[]),typeof(WaveformView),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
 public float[]? Samples { get=>(float[]?)GetValue(SamplesProperty); set=>SetValue(SamplesProperty,value); }
 protected override void OnRender(DrawingContext dc) { base.OnRender(dc);dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(5,18,25)),null,new Rect(0,0,ActualWidth,ActualHeight)); var grid=new Pen(new SolidColorBrush(Color.FromRgb(24,46,57)),.6); for(int i=1;i<4;i++) dc.DrawLine(grid,new(0,ActualHeight*i/4),new(ActualWidth,ActualHeight*i/4));for(int i=1;i<12;i++)dc.DrawLine(grid,new(ActualWidth*i/12,0),new(ActualWidth*i/12,ActualHeight)); var samples=Samples; if(samples==null||samples.Length==0)return; double peak=samples.Max(v=>Math.Abs(v));double gain=peak<.00001?1:Math.Clamp(.65/peak,1,24);var pen=new Pen(new SolidColorBrush(Color.FromRgb(22,224,163)),Math.Max(.7,ActualWidth/samples.Length*.48)); for(int i=0;i<samples.Length;i++) { double x=(i+.5)*ActualWidth/samples.Length,h=Math.Max(.45,samples[i]*gain*ActualHeight*.46); dc.DrawLine(pen,new(x,ActualHeight/2-h),new(x,ActualHeight/2+h)); } }
}
