using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Diagnostics;

namespace FieldTrader.Market;
public sealed class GameScreenCapture(string directory) : IScreenSource, IMarketCapture {
    [StructLayout(LayoutKind.Sequential)] internal struct Rect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] internal struct Point {public int X,Y;}
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr window,ref Point point);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc,int width,int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest,int x,int y,int w,int h,IntPtr src,int sx,int sy,uint op);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    internal static bool GameForeground(IntPtr window) {
        if(window==IntPtr.Zero||GetForegroundWindow()!=window)return false;
        GetWindowThreadProcessId(window,out uint pid);
        try {using var p=Process.GetProcessById((int)pid);return p.ProcessName.Equals("UAGame",StringComparison.OrdinalIgnoreCase);}catch(ArgumentException){return false;}
    }
    public Task<CapturedScreen> CaptureAsync(CancellationToken token)=>Task.Run(()=>Capture(token),token);
    private CapturedScreen Capture(CancellationToken token) {
        token.ThrowIfCancellationRequested();var window=GetForegroundWindow();
        if(!GameForeground(window)||IsIconic(window))throw new InvalidOperationException("请将国服游戏窗口切到前台；采集仅支持 UAGame 的可见客户区");
        var dpi=SetThreadDpiAwarenessContext(new IntPtr(-4));
        IntPtr desktop=IntPtr.Zero,memory=IntPtr.Zero,bitmap=IntPtr.Zero,old=IntPtr.Zero;
        try {
            if(!GetClientRect(window,out var rect))throw new IOException("无法读取游戏窗口尺寸");
            var origin=new Point();if(!ClientToScreen(window,ref origin))throw new IOException("无法定位游戏客户区");
            int width=rect.Right,height=rect.Bottom;
            if(width<640||height<360)throw new IOException("窗口过小或已最小化");
            desktop=GetDC(IntPtr.Zero);memory=CreateCompatibleDC(desktop);bitmap=CreateCompatibleBitmap(desktop,width,height);
            if(desktop==IntPtr.Zero||memory==IntPtr.Zero||bitmap==IntPtr.Zero)throw new IOException("截图资源创建失败");
            old=SelectObject(memory,bitmap);
            if(!BitBlt(memory,0,0,width,height,desktop,origin.X,origin.Y,0x00CC0020))throw new IOException("屏幕截图失败");
            var at=DateTimeOffset.UtcNow;
            var source=Imaging.CreateBitmapSourceFromHBitmap(bitmap,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());source.Freeze();
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(source));using var stream=new MemoryStream();encoder.Save(stream);var bytes=stream.ToArray();
            if(!GameForeground(window))throw new InvalidOperationException("截图期间前台窗口已变化");
            Directory.CreateDirectory(directory);string id=Guid.NewGuid().ToString("N"),path=Path.Combine(directory,id+".png");File.WriteAllBytes(path,bytes);
            return new(new(id,at,path),Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),width,height,window.ToInt64(),origin.X,origin.Y,true);
        } finally {
            if(old!=IntPtr.Zero)SelectObject(memory,old);if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);if(memory!=IntPtr.Zero)DeleteDC(memory);if(desktop!=IntPtr.Zero)ReleaseDC(IntPtr.Zero,desktop);
            if(dpi!=IntPtr.Zero)SetThreadDpiAwarenessContext(dpi);
        }
    }
    async Task<Frame> IMarketCapture.CaptureAsync(CancellationToken token)=>(await CaptureAsync(token)).Frame;
}
