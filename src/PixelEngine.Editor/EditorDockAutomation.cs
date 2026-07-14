namespace PixelEngine.Editor;

/// <summary>
/// 相对另一 panel 的语义停靠位置。
/// </summary>
public enum EditorDockPlacement
{
    /// <summary>与目标 panel 放入同一 tab group。</summary>
    Tab,

    /// <summary>在目标 panel 左侧拆分。</summary>
    Left,

    /// <summary>在目标 panel 右侧拆分。</summary>
    Right,

    /// <summary>在目标 panel 上方拆分。</summary>
    Top,

    /// <summary>在目标 panel 下方拆分。</summary>
    Bottom,

    /// <summary>从 dock tree 脱离为单窗口。</summary>
    Floating,
}

/// <summary>
/// 后端执行的一次 panel 停靠变更。
/// </summary>
public sealed record EditorDockWindowRequest
{
    /// <summary>源 ImGui window 的完整稳定标题。</summary>
    public required string WindowTitle { get; init; }

    /// <summary>Tab/四向拆分的目标 ImGui window 标题。</summary>
    public string? TargetWindowTitle { get; init; }

    /// <summary>语义停靠位置。</summary>
    public required EditorDockPlacement Placement { get; init; }

    /// <summary>四向拆分时新节点占目标节点的比例。</summary>
    public float SplitRatio { get; init; } = 0.25f;

    /// <summary>Floating 时可选窗口 X。</summary>
    public float? X { get; init; }

    /// <summary>Floating 时可选窗口 Y。</summary>
    public float? Y { get; init; }

    /// <summary>Floating 时可选窗口宽度。</summary>
    public float? Width { get; init; }

    /// <summary>Floating 时可选窗口高度。</summary>
    public float? Height { get; init; }
}

/// <summary>
/// ImGui window 的实时停靠与矩形状态。
/// </summary>
/// <param name="Known">窗口已至少完成一次 Begin，可读取运行态。</param>
/// <param name="DockId">当前 session 内部 dock node ID；只供同次捕获分组，不得对外持久化。</param>
/// <param name="X">窗口或 dock node X。</param>
/// <param name="Y">窗口或 dock node Y。</param>
/// <param name="Width">窗口或 dock node宽度。</param>
/// <param name="Height">窗口或 dock node 高度。</param>
public readonly record struct EditorDockWindowState(
    bool Known,
    uint DockId,
    float X,
    float Y,
    float Width,
    float Height);
