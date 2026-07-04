namespace PixelEngine.UI;

/// <summary>
/// UI 后端初始化信息。
/// </summary>
/// <param name="Viewport">初始 UI 视口。</param>
/// <param name="PreferredBackend">请求的后端种类。</param>
public readonly record struct UiBackendInitializeInfo(UiViewport Viewport, UiBackendKind PreferredBackend);
