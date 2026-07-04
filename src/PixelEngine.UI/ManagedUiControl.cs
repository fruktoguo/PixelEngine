namespace PixelEngine.UI;

/// <summary>
/// ManagedFallbackBackend 的托管控件描述。
/// </summary>
internal sealed class ManagedUiControl
{
    public ManagedUiControlKind Kind { get; init; }

    public required string Id { get; init; }

    public required string Text { get; init; }

    public UiElementId Element { get; init; }

    public UiActionId Action { get; init; }

    public UiPathId Path { get; init; }

    public UiValue Value { get; set; }
}
