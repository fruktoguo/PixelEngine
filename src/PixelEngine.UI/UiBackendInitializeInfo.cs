namespace PixelEngine.UI;

/// <summary>
/// UI 后端初始化信息。
/// </summary>
public readonly record struct UiBackendInitializeInfo
{
    /// <summary>
    /// 创建 UI 后端初始化信息。
    /// </summary>
    /// <param name="viewport">初始 UI 视口。</param>
    /// <param name="preferredBackend">请求的后端种类。</param>
    public UiBackendInitializeInfo(UiViewport viewport, UiBackendKind preferredBackend)
        : this(viewport, preferredBackend, default)
    {
    }

    /// <summary>
    /// 创建 UI 后端初始化信息。
    /// </summary>
    /// <param name="viewport">初始 UI 视口。</param>
    /// <param name="preferredBackend">请求的后端种类。</param>
    /// <param name="fontSelection">FontEngine 解析出的字体选择。</param>
    public UiBackendInitializeInfo(UiViewport viewport, UiBackendKind preferredBackend, UiFontSelection fontSelection)
        : this(
            UiDisplayMetrics.FromViewport(in viewport),
            UiCanvasScalerSettings.Default,
            preferredBackend,
            fontSelection)
    {
    }

    /// <summary>
    /// 使用分层 display metrics 与 CanvasScaler 设置创建后端初始化信息。
    /// </summary>
    /// <param name="displayMetrics">当前 presentation 与显示器度量。</param>
    /// <param name="canvasScalerSettings">CanvasScaler 入盘设置。</param>
    /// <param name="preferredBackend">请求的后端种类。</param>
    /// <param name="fontSelection">FontEngine 解析出的字体选择。</param>
    public UiBackendInitializeInfo(
        UiDisplayMetrics displayMetrics,
        UiCanvasScalerSettings canvasScalerSettings,
        UiBackendKind preferredBackend,
        UiFontSelection fontSelection = default)
    {
        CanvasMetrics = UiCanvasScaleResolver.Resolve(in canvasScalerSettings, in displayMetrics);
        DisplayMetrics = displayMetrics;
        CanvasScalerSettings = canvasScalerSettings;
        Viewport = new UiViewport(
            0,
            0,
            displayMetrics.PresentationWidth,
            displayMetrics.PresentationHeight,
            displayMetrics.FramebufferScaleX);
        PreferredBackend = preferredBackend;
        FontSelection = fontSelection;
    }

    /// <summary>
    /// 初始 UI 视口。
    /// </summary>
    public UiViewport Viewport { get; init; }

    /// <summary>
    /// presentation、framebuffer scale 与 raw physical DPI 分层后的显示度量。
    /// </summary>
    public UiDisplayMetrics DisplayMetrics { get; init; }

    /// <summary>
    /// 本后端实例使用的 CanvasScaler 设置。
    /// </summary>
    public UiCanvasScalerSettings CanvasScalerSettings { get; init; }

    /// <summary>
    /// layout、raster、input 与 IME 共用的解析后 Canvas 度量。
    /// </summary>
    public UiCanvasMetrics CanvasMetrics { get; init; }

    /// <summary>
    /// 请求的后端种类。
    /// </summary>
    public UiBackendKind PreferredBackend { get; init; }

    /// <summary>
    /// FontEngine 解析出的字体选择；未指定时后端使用自身默认字体。
    /// </summary>
    public UiFontSelection FontSelection { get; init; }
}
