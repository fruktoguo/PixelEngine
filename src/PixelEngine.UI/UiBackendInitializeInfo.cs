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
    {
        Viewport = viewport;
        PreferredBackend = preferredBackend;
        FontSelection = fontSelection;
    }

    /// <summary>
    /// 初始 UI 视口。
    /// </summary>
    public UiViewport Viewport { get; init; }

    /// <summary>
    /// 请求的后端种类。
    /// </summary>
    public UiBackendKind PreferredBackend { get; init; }

    /// <summary>
    /// FontEngine 解析出的字体选择；未指定时后端使用自身默认字体。
    /// </summary>
    public UiFontSelection FontSelection { get; init; }
}
