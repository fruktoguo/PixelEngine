namespace PixelEngine.Rendering;

/// <summary>
/// 暴露窗口 present 相关运行时控制，供性能 HUD 显示和切换。
/// </summary>
public interface IRenderPresentationControl
{
    /// <summary>
    /// 当前 VSync 是否开启。
    /// </summary>
    bool VSyncEnabled { get; set; }

    /// <summary>
    /// 当前后端是否支持运行时切换 VSync。
    /// </summary>
    bool CanToggleVSync { get; }

    /// <summary>
    /// 当前 GL 后端是否支持整帧 GPU timer query。
    /// </summary>
    bool GpuFrameTimerAvailable { get; }
}
