namespace PixelEngine.UI;

/// <summary>
/// 可见屏栈项。
/// </summary>
/// <param name="Handle">屏幕实例句柄。</param>
/// <param name="ScreenId">稳定屏幕 id。</param>
/// <param name="Document">后端文档句柄。</param>
/// <param name="Modal">是否模态。</param>
public readonly record struct UiScreenStackEntry(
    UiScreenHandle Handle,
    UiScreenId ScreenId,
    UiDocumentHandle Document,
    bool Modal);
