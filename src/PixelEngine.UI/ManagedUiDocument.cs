namespace PixelEngine.UI;

/// <summary>
/// ManagedFallbackBackend 的已解析文档。
/// </summary>
internal sealed class ManagedUiDocument(
    UiDocumentHandle handle,
    UiDocumentSource source,
    string title,
    ManagedUiBox rootBox,
    ManagedUiControl[] controls)
{
    internal UiDocumentHandle Handle { get; } = handle;

    internal UiDocumentSource Source { get; } = source;

    internal string Title { get; } = title;

    internal ManagedUiBox RootBox { get; } = rootBox;

    internal ManagedUiControl[] Controls { get; } = controls;
}
