using System.Numerics;

namespace PixelEngine.Gui;

/// <summary>
/// shared/runtime Gui 按钮的只读输入诊断；仅累计已有 ImGui 状态，不注入或消费输入。
/// </summary>
/// <param name="ButtonCalls">累计绘制按钮次数。</param>
/// <param name="HoveredCalls">累计 hover 按钮次数。</param>
/// <param name="PressedCalls">累计在 hover 按钮上观察到 press edge 的次数。</param>
/// <param name="DownCalls">累计在 hover 按钮上观察到左键按下帧的次数。</param>
/// <param name="ReleasedCalls">累计在 hover 按钮上观察到 release edge 的次数。</param>
/// <param name="ClickedCalls">累计由 ImGui 返回 clicked 的次数。</param>
/// <param name="LastHoveredLabel">最近 hover 的按钮标签。</param>
/// <param name="LastHoveredRectMin">最近 hover 按钮矩形左上角。</param>
/// <param name="LastHoveredRectMax">最近 hover 按钮矩形右下角。</param>
/// <param name="LastMousePosition">最近绘制按钮时的 ImGui 指针位置。</param>
public readonly record struct GuiButtonInputDiagnostics(
    long ButtonCalls,
    long HoveredCalls,
    long PressedCalls,
    long DownCalls,
    long ReleasedCalls,
    long ClickedCalls,
    string? LastHoveredLabel,
    Vector2 LastHoveredRectMin,
    Vector2 LastHoveredRectMax,
    Vector2 LastMousePosition);
