namespace PixelEngine.Rendering;

/// <summary>
/// 从 <see cref="RenderWindow" /> 与当前 monitor 采样显示度量，并在显式帧边界发布 revision。
/// </summary>
public sealed class RenderWindowDisplayMetricsSource : IDisplayMetricsSource
{
    private readonly RenderWindow _window;
    private readonly DisplayMetricsRevisionTracker _tracker = new();

    /// <summary>
    /// 创建窗口显示度量源并发布初始 revision。
    /// </summary>
    /// <param name="window">Rendering-owned 窗口。</param>
    public RenderWindowDisplayMetricsSource(RenderWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _ = CommitFrameBoundary();
    }

    /// <inheritdoc />
    public DisplayMetricsSnapshot Current => _tracker.Current;

    /// <inheritdoc />
    public DisplayMetricsSnapshot CommitFrameBoundary()
    {
        nint monitorId = 0;
        float? actualPhysicalDpi = null;
        if (_window.TryGetWin32WindowHandle(out IntPtr hwnd))
        {
            _ = WindowsMonitorDpi.TryCapture(hwnd, out monitorId, out actualPhysicalDpi);
        }

        return _tracker.Commit(
            monitorId,
            NormalizeScale(_window.FramebufferScaleX),
            NormalizeScale(_window.FramebufferScaleY),
            actualPhysicalDpi);
    }

    private static float NormalizeScale(float scale)
    {
        return float.IsFinite(scale) && scale > 0f ? scale : 1f;
    }
}
