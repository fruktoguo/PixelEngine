using Hexa.NET.ImGui;

namespace PixelEngine.Gui;

/// <summary>
/// 保存当前 ImGui style 的 100% 基线，并从该基线执行绝对缩放。
/// </summary>
/// <remarks>
/// Dear ImGui 的 <see cref="ImGuiStylePtr.ScaleAllSizes(float)" /> 会截断像素尺寸；
/// 若连续按相邻倍率缩放当前值，小尺寸最终可能退化到 0。每次恢复完整基线可避免累计误差。
/// </remarks>
internal sealed class ImGuiStyleScaleState
{
    private ImGuiStyle _baseline;
    private bool _captured;

    /// <summary>最近应用的绝对倍率。</summary>
    public float AppliedScale { get; private set; } = 1f;

    /// <summary>捕获当前 context 的完整 100% style。</summary>
    public unsafe void CaptureCurrent()
    {
        ImGuiStylePtr style = ImGui.GetStyle();
        if (style.IsNull)
        {
            throw new InvalidOperationException("当前 ImGui context 没有可捕获的 style。");
        }

        _baseline = style[0];
        _captured = true;
        AppliedScale = 1f;
    }

    /// <summary>从已捕获基线应用绝对倍率。</summary>
    /// <param name="scale">有限且大于 0 的绝对倍率。</param>
    public unsafe void Apply(float scale)
    {
        if (!_captured)
        {
            throw new InvalidOperationException("应用缩放前必须先捕获 ImGui style 基线。");
        }

        if (!float.IsFinite(scale) || scale <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "ImGui style 缩放必须是有限正数。");
        }

        ImGuiStylePtr style = ImGui.GetStyle();
        if (style.IsNull)
        {
            throw new InvalidOperationException("当前 ImGui context 没有可缩放的 style。");
        }

        style[0] = _baseline;
        style.ScaleAllSizes(scale);
        // Dear ImGui 1.92 在 NewFrame 强制要求该命中宽度为正；极小倍率也必须 fail-safe。
        style.WindowBorderHoverPadding = MathF.Max(1f, style.WindowBorderHoverPadding);
        AppliedScale = scale;
    }

    /// <summary>清除旧 context 的捕获状态。</summary>
    public void Reset()
    {
        _baseline = default;
        _captured = false;
        AppliedScale = 1f;
    }
}
