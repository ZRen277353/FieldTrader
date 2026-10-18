using System.Windows;
using System.Windows.Media.Imaging;
namespace FieldTrader;
public static class ItemImage {
 public static BitmapSource? Load(Item item,CatalogStore store){try{var path=store.Resolve(item.ImagePath);if(path==null)return null;var raw=new BitmapImage();raw.BeginInit();raw.CacheOption=BitmapCacheOption.OnLoad;raw.UriSource=new Uri(path);if(item.ImageCrop==null)raw.DecodePixelWidth=300;raw.EndInit();raw.Freeze();if(item.ImageCrop is {Length:4} crop){var cropped=new CroppedBitmap(raw,new Int32Rect(crop[0],crop[1],crop[2],crop[3]));cropped.Freeze();return cropped;}return raw;}catch{return null;}}
 public static string Layout(Item item)=>Layout(item.GridWidth,item.GridHeight);
 public static string Layout(int width,int height)=>width==0||height==0?"规格未标注":$"{width}×{height} · {(width==height?"方形":width>height?"横放":"竖放")}";
}
