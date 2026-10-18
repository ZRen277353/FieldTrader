using System.Windows;
using System.Windows.Media;
namespace FieldTrader;
// Static, lightweight WPF artwork: no timers, bitmap assets or GPU animations.
public sealed class HachimiBackdrop : FrameworkElement {
 protected override void OnRender(DrawingContext dc){double w=ActualWidth,h=ActualHeight;if(w<=0||h<=0)return;dc.PushClip(new RectangleGeometry(new Rect(0,0,w,h)));
  var mint=new RadialGradientBrush(Color.FromArgb(42,69,184,169),Colors.Transparent){Center=new(.15,.05),GradientOrigin=new(.15,.05),RadiusX=.6,RadiusY=.7};dc.DrawRectangle(mint,null,new Rect(0,0,w,h));
  var honey=new RadialGradientBrush(Color.FromArgb(35,244,174,75),Colors.Transparent){Center=new(.9,.2),GradientOrigin=new(.9,.2),RadiusX=.5,RadiusY=.6};dc.DrawRectangle(honey,null,new Rect(0,0,w,h));
  var pen=new Pen(new SolidColorBrush(Color.FromArgb(16,231,185,109)),1);for(int row=0;row<5;row++)for(int col=0;col<9;col++){double x=w-340+col*38+(row%2)*19,y=30+row*33;var g=new StreamGeometry();using(var c=g.Open()){c.BeginFigure(new(x+18,y),false,true);for(int i=1;i<6;i++)c.LineTo(new(x+18*Math.Cos(i*Math.PI/3),y+18*Math.Sin(i*Math.PI/3)),true,false);}dc.DrawGeometry(null,pen,g);}
  var paw=new SolidColorBrush(Color.FromArgb(14,141,220,203));foreach(var pt in new[]{new Point(w*.08,h*.78),new Point(w*.91,h*.82)}){dc.PushTransform(new RotateTransform(-24,pt.X,pt.Y));dc.DrawEllipse(paw,null,pt,24,17);for(int i=0;i<4;i++)dc.DrawEllipse(paw,null,new Point(pt.X-25+i*17,pt.Y-28-Math.Sin(i*Math.PI/3)*7),7,10);dc.Pop();}
  dc.Pop();
 }
}
public sealed class CatMark : FrameworkElement {
 protected override void OnRender(DrawingContext dc){double scale=Math.Min(ActualWidth/80,ActualHeight/70);dc.PushTransform(new ScaleTransform(scale,scale));var fill=new SolidColorBrush(Color.FromRgb(255,218,153));var outline=new Pen(new SolidColorBrush(Color.FromRgb(243,184,96)),1.5);
  var face=Geometry.Parse("M12,28 L10,7 30,19 Q40,15 50,19 L70,7 68,30 Q78,61 40,64 Q2,61 12,28 Z");dc.DrawGeometry(fill,outline,face);
  var ear=new SolidColorBrush(Color.FromRgb(222,151,144));dc.DrawGeometry(ear,null,Geometry.Parse("M16,15 L17,27 27,22 Z M64,15 L63,27 53,22 Z"));
  var ink=new Pen(new SolidColorBrush(Color.FromRgb(55,62,77)),2){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};dc.DrawGeometry(null,ink,Geometry.Parse("M25,38 Q29,33 33,38 M47,38 Q51,33 55,38 M35,46 Q40,52 45,46"));dc.DrawEllipse(ear,null,new Point(20,46),5,2);dc.DrawEllipse(ear,null,new Point(60,46),5,2);dc.Pop();
 }
}
