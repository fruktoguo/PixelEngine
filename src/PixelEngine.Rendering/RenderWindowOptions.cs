namespace PixelEngine.Rendering;

/// <summary>
/// 渲染窗口创建参数。
/// </summary>
public sealed record RenderWindowOptions
{
    /// <summary>
    /// 窗口标题。
    /// </summary>
    public string Title { get; init; } = "PixelEngine";

    /// <summary>
    /// 初始窗口宽度，单位为像素。
    /// </summary>
    public int Width { get; init; } = 1280;

    /// <summary>
    /// 初始窗口高度，单位为像素。
    /// </summary>
    public int Height { get; init; } = 720;

    /// <summary>
    /// 后端选择偏好。
    /// </summary>
    public RenderBackendPreference BackendPreference { get; init; } = RenderBackendPreference.Auto;

    /// <summary>
    /// 是否启用垂直同步；开启时 <see cref="RenderWindow.SwapBuffers" /> 可能受显示器刷新率阻塞。
    /// </summary>
    public bool VSync { get; init; } = true;

    /// <summary>
    /// Windows 下是否让原生标题栏使用与应用一致的深色 chrome；其他平台忽略。
    /// </summary>
    public bool UseDarkWindowChrome { get; init; }

    /// <summary>标题栏背景色，格式为 0xRRGGBB。</summary>
    public uint TitleBarColorRgb { get; init; } = 0x202226;

    /// <summary>标题栏文字色，格式为 0xRRGGBB。</summary>
    public uint TitleBarTextColorRgb { get; init; } = 0xE7E9ED;

    /// <summary>窗口边框色，格式为 0xRRGGBB。</summary>
    public uint WindowBorderColorRgb { get; init; } = 0x111216;

    /// <summary>
    /// Silk 窗口帧率节流；0 表示不由 Silk run loop 限制帧率。
    /// </summary>
    public double FramesPerSecond { get; init; }

    /// <summary>
    /// Silk 窗口更新频率节流；0 表示不由 Silk run loop 限制更新频率。
    /// </summary>
    public double UpdatesPerSecond { get; init; }

    /// <summary>
    /// 是否请求 OpenGL debug context。Debug 构建默认启用。
    /// </summary>
    public bool EnableDebugContext { get; init; } =
#if DEBUG
        true;
#else
        false;
#endif
}
