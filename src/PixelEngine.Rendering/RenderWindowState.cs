namespace PixelEngine.Rendering;

/// <summary>
/// 平台顶层窗口的实时状态。
/// </summary>
public enum RenderWindowState
{
    /// <summary>普通可调整尺寸窗口。</summary>
    Normal,

    /// <summary>已最小化到系统任务切换界面。</summary>
    Minimized,

    /// <summary>已最大化到工作区。</summary>
    Maximized,

    /// <summary>占据目标显示器的全屏窗口。</summary>
    Fullscreen,
}
