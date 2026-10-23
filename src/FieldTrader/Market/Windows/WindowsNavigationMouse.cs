using System.Runtime.InteropServices;

namespace FieldTrader.Market;
// Stage E adapter, deliberately not armed by the stage A/B UI. No buy/sell/confirm action exists here.
public sealed class WindowsNavigationMouse : IMouseExecutor {
    public bool IsSimulation=>false;
    private readonly bool armed;
    public WindowsNavigationMouse(bool armed=false){this.armed=armed;}
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput {public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
    [StructLayout(LayoutKind.Sequential)] private struct Input {public uint Type;public MouseInput Mouse;}
    [DllImport("user32.dll")] private static extern uint SendInput(uint count,Input[] inputs,int size);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x,int y);
    public Task ExecuteAsync(NavigationAction action,CapturedScreen screen,CancellationToken token) {
        token.ThrowIfCancellationRequested();
        if(!armed)throw new InvalidOperationException("真实导航尚未启用");
        if(action.Kind==NavigationKind.SimulatedTrade)throw new InvalidOperationException("模拟交易禁止发送真实输入");
        if(action.Kind==NavigationKind.Search)throw new InvalidOperationException("搜索输入仅支持预演；需阶段E验证焦点后接入真实键盘输入");
        if(!screen.Live||screen.Frame.Id!=action.FrameId||screen.ContentHash!=action.ContentHash||!action.Bounds.Valid
            ||DateTimeOffset.UtcNow-screen.Frame.CapturedAt>TimeSpan.FromSeconds(5)||DateTimeOffset.UtcNow<screen.Frame.CapturedAt)
            throw new InvalidOperationException("旧截图或不合法坐标");
        var window=new IntPtr(screen.WindowHandle);
        var dpi=GameScreenCapture.SetThreadDpiAwarenessContext(new IntPtr(-4));
        try {
            var p=new GameScreenCapture.Point();
            if(!GameScreenCapture.GameForeground(window)||!GameScreenCapture.GetClientRect(window,out var rect)
                ||!GameScreenCapture.ClientToScreen(window,ref p)||rect.Right!=screen.Width||rect.Bottom!=screen.Height||p.X!=screen.ScreenX||p.Y!=screen.ScreenY)
                throw new InvalidOperationException("窗口位置、尺寸或前台状态已变化");
            if(action.Kind==NavigationKind.CloseDialog && Geometry.Normalize(action.Text)!="关闭")throw new InvalidOperationException("非关闭按钮");
            if(action.Kind!=NavigationKind.CloseDialog&&action.Before!=MarketPage.Market)throw new InvalidOperationException("仅允许市场浏览导航");
            if(new[]{"购买","上架","兑换","确认","出售"}.Any(s=>Geometry.Normalize(action.Text)==s))throw new InvalidOperationException("禁止交易提交");
            token.ThrowIfCancellationRequested();
            if(!SetCursorPos(p.X+(int)((action.Bounds.X+action.Bounds.Width/2)*screen.Width),p.Y+(int)((action.Bounds.Y+action.Bounds.Height/2)*screen.Height)))throw new InvalidOperationException("定位鼠标失败");
            if(!GameScreenCapture.GameForeground(window))throw new InvalidOperationException("点击前失去游戏焦点");
            token.ThrowIfCancellationRequested();
            var inputs=new[]{new Input{Type=0,Mouse=new MouseInput{Flags=0x0002}},new Input{Type=0,Mouse=new MouseInput{Flags=0x0004}}};
            if(SendInput(2,inputs,Marshal.SizeOf<Input>())!=2)throw new InvalidOperationException("UNKNOWN：鼠标输入未完整发送");
            return Task.CompletedTask;
        }finally{if(dpi!=IntPtr.Zero)GameScreenCapture.SetThreadDpiAwarenessContext(dpi);}
    }
}
