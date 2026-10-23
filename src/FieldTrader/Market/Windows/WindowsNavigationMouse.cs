namespace FieldTrader.Market;

// Legacy interface retained for source compatibility. The old armed=true bypass is retired:
// native navigation now requires NavigationExecutor + WindowsInputController (local test window only).
public sealed class WindowsNavigationMouse : IMouseExecutor {
    public bool IsSimulation => false;
    public WindowsNavigationMouse(bool armed = false) { }
    public Task ExecuteAsync(NavigationAction action, CapturedScreen screen, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        throw new InvalidOperationException("旧真实鼠标入口已停用；请通过统一导航执行器连接本地测试窗口。游戏实测尚未开放。");
    }
}
