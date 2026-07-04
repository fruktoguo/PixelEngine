namespace PixelEngine.UI;

/// <summary>
/// 已载入 UI 文档记录。
/// </summary>
/// <param name="ScreenId">稳定屏幕 id。</param>
/// <param name="Document">后端文档句柄。</param>
/// <param name="SourceKind">文档来源类型。</param>
public readonly record struct UiDocumentSlot(
    UiScreenId ScreenId,
    UiDocumentHandle Document,
    UiDocumentSourceKind SourceKind);
