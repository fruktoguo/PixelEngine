namespace PixelEngine.UI;

/// <summary>
/// ManagedFallbackBackend 的托管控件描述。
/// </summary>
internal sealed class ManagedUiControl
{
    internal ManagedUiControlKind Kind { get; init; }

    internal required string Id { get; init; }

    internal required string Text { get; init; }

    internal UiElementId Element { get; init; }

    internal UiActionId Action { get; init; }

    internal UiPathId Path { get; init; }

    internal string ModelVariableName { get; init; } = string.Empty;

    internal UiValue Value { get; set; }

    internal ManagedUiStyle Style { get; init; }

    internal string ImagePath { get; init; } = string.Empty;

    internal int ImageWidth { get; init; }

    internal int ImageHeight { get; init; }

    internal float DisplayWidth { get; init; }

    internal float DisplayHeight { get; init; }
}
