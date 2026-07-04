namespace PixelEngine.UI;

/// <summary>
/// ManagedFallbackBackend 的已解析文档。
/// </summary>
internal sealed class ManagedUiDocument(
    UiDocumentHandle handle,
    UiDocumentSource source,
    string title,
    ManagedUiControl[] controls)
{
    public UiDocumentHandle Handle { get; } = handle;

    public UiDocumentSource Source { get; } = source;

    public string Title { get; } = title;

    public ManagedUiControl[] Controls { get; } = controls;
}
