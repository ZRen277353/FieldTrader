using System.Windows;
using System.Windows.Media;
using NAudio.Dsp;
namespace FieldTrader;
// Original schematic placeholders, not game item artwork.
public sealed class ItemPlaceholder : FrameworkElement {
 public static readonly DependencyProperty KindProperty=DependencyProperty.Register(nameof(Kind),typeof(int),typeof(ItemPlaceholder),new FrameworkPropertyMetadata(0,FrameworkPropertyMetadataOptions.AffectsRender));
 public int Kind { get=>(int)GetValue(KindProperty);set=>SetValue(KindProperty,value); }
 protected override void OnRender(DrawingContext dc) {
  double s=Math.Min(ActualWidth/120,ActualHeight/120); dc.PushTransform(new TranslateTransform((ActualWidth-100*s)/2,(ActualHeight-112*s)/2));dc.PushTransform(new ScaleTransform(s,s));
  var line=new Pen(new SolidColorBrush(Color.FromRgb(137,119,84)),1.5);var fill=new SolidColorBrush(Color.FromRgb(29,39,40));var detail=new Pen(new SolidColorBrush(Color.FromRgb(73,90,99)),1);
  switch(Kind%4) {
   case 0: dc.DrawRoundedRectangle(fill,line,new Rect(23,18,54,73),5,5);dc.DrawEllipse(null,line,new Point(50,42),17,17);dc.DrawLine(line,new Point(50,42),new Point(57,32));dc.DrawLine(line,new Point(50,42),new Point(41,37));for(int i=0;i<4;i++)dc.DrawLine(detail,new Point(34,69+i*4),new Point(66,69+i*4));break;
   case 1: for(int r=36;r>0;r-=8)dc.DrawEllipse(r==36?fill:null,line,new Point(50,56),r,r);for(int i=0;i<8;i++){double a=i*Math.PI/4;dc.DrawLine(detail,new Point(50+20*Math.Cos(a),56+20*Math.Sin(a)),new Point(50+36*Math.Cos(a),56+36*Math.Sin(a)));}break;
   case 2: dc.DrawRoundedRectangle(fill,line,new Rect(19,20,62,47),3,3);dc.DrawRectangle(null,detail,new Rect(25,26,50,34));dc.DrawGeometry(fill,line,Geometry.Parse("M19,71 L81,71 91,88 9,88 Z"));for(int i=0;i<3;i++)dc.DrawLine(detail,new Point(22-i*3,75+i*4),new Point(78+i*3,75+i*4));break;
   default: dc.DrawGeometry(fill,line,Geometry.Parse("M28,32 L50,20 74,34 74,78 51,92 28,79 Z"));dc.DrawLine(line,new Point(28,32),new Point(51,45));dc.DrawLine(line,new Point(51,45),new Point(74,34));dc.DrawLine(line,new Point(51,45),new Point(51,92));break;
  } dc.Pop();dc.Pop();
 }
}
public sealed class LevelMeter : FrameworkElement {
 public static readonly DependencyProperty ValueProperty=DependencyProperty.Register(nameof(Value),typeof(double),typeof(LevelMeter),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
 public double Value {get=>(double)GetValue(ValueProperty);set=>SetValue(ValueProperty,value);}
 protected override void OnRender(DrawingContext dc){int n=30;double w=ActualWidth/n;for(int i=0;i<n;i++)dc.DrawRoundedRectangle(new SolidColorBrush(i<n*Value/100?Color.FromRgb(0,224,137):Color.FromRgb(29,49,61)),null,new Rect(i*w,1,Math.Max(1,w-2.5),Math.Max(0,ActualHeight-2)),1.5,1.5);}
}
public sealed class TerrainBackdrop : FrameworkElement {
 protected override void OnRender(DrawingContext dc) {var p=new Pen(new SolidColorBrush(Color.FromArgb(19,120,163,181)),1);for(int i=0;i<16;i++){var g=new StreamGeometry();using(var ctx=g.Open()){ctx.BeginFigure(new Point(ActualWidth*.35,15+i*9),false,false);for(int j=0;j<26;j++)ctx.LineTo(new Point(ActualWidth*(.35+j*.026),15+i*9+Math.Sin(j*.8+i*.2)*12+Math.Cos(j*.35)*20),true,false);}dc.DrawGeometry(null,p,g);}}
}
public static class SpectrumAnalysis {
 public static float[] Mel(float[] samples,int rate) {
  const int n=2048,bands=64;var fft=new Complex[n];for(int i=0;i<n;i++){int source=samples.Length-n+i;fft[i].X=source>=0?samples[source]*(float)(.5-.5*Math.Cos(2*Math.PI*i/(n-1))):0;}FastFourierTransform.FFT(true,11,fft);
  double maxMel=2595*Math.Log10(1+Math.Min(16000,rate/2d)/700);var edges=Enumerable.Range(0,bands+2).Select(i=>(700*(Math.Pow(10,i*maxMel/(bands+1)/2595)-1))*n/rate).ToArray();var result=new float[bands];for(int b=0;b<bands;b++){double sum=0,weights=0;for(int k=Math.Max(0,(int)edges[b]);k<=Math.Min(n/2-1,(int)Math.Ceiling(edges[b+2]));k++){double weight=k<edges[b+1]?(k-edges[b])/(edges[b+1]-edges[b]):(edges[b+2]-k)/(edges[b+2]-edges[b+1]);weight=Math.Max(0,weight);sum+=weight*(fft[k].X*fft[k].X+fft[k].Y*fft[k].Y);weights+=weight;}double db=10*Math.Log10(sum/Math.Max(weights,1)+1e-12);result[b]=(float)Math.Clamp((db+95)/75,0,1);}return result;
 }
}
public sealed class SpectrogramView : FrameworkElement {
 private readonly Queue<float[]> columns=new(); private readonly Brush[] palette=Enumerable.Range(0,100).Select(i=>{double t=i/99d;var c=t<.5?Color.FromRgb((byte)(8+t*12),(byte)(12+t*190),(byte)(28+t*280)):Color.FromRgb((byte)((t-.5)*500),(byte)(105+(t-.5)*260),(byte)(170-(t-.5)*300));var brush=new SolidColorBrush(c);brush.Freeze();return (Brush)brush;}).ToArray();
 public static readonly DependencyProperty BandsProperty=DependencyProperty.Register(nameof(Bands),typeof(float[]),typeof(SpectrogramView),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender,OnBands));
 public float[]? Bands {get=>(float[]?)GetValue(BandsProperty);set=>SetValue(BandsProperty,value);}
 private static void OnBands(DependencyObject d,DependencyPropertyChangedEventArgs e){var view=(SpectrogramView)d;if(e.NewValue is float[] a){view.columns.Enqueue(a);while(view.columns.Count>150)view.columns.Dequeue();}}
 protected override void OnRender(DrawingContext dc){dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(5,12,24)),null,new Rect(0,0,ActualWidth,ActualHeight));int x=150-columns.Count;double w=ActualWidth/150;foreach(var col in columns){for(int b=0;b<col.Length;b++)dc.DrawRectangle(palette[(int)Math.Clamp(col[b]*99,0,99)],null,new Rect(x*w,ActualHeight*(1-(b+1d)/col.Length),w+.2,ActualHeight/col.Length+.2));x++;}var grid=new Pen(new SolidColorBrush(Color.FromArgb(45,91,137,167)),.5);for(int i=1;i<8;i++)dc.DrawLine(grid,new Point(ActualWidth*i/8,0),new Point(ActualWidth*i/8,ActualHeight));for(int i=1;i<4;i++)dc.DrawLine(grid,new Point(0,ActualHeight*i/4),new Point(ActualWidth,ActualHeight*i/4));}
}
