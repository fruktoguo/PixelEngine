using System.Numerics;

namespace PixelEngine.Gui;

/// <summary>
/// ImGui 帧尺寸换算结果。DisplaySize 使用平台逻辑坐标，DisplayFramebufferScale 描述逻辑坐标到默认 framebuffer 的缩放。
/// </summary>
public readonly record struct ImGuiFrameMetrics
{
    private ImGuiFrameMetrics(int logicalWidth, int logicalHeight, float framebufferScaleX, float framebufferScaleY)
    {
        LogicalWidth = logicalWidth;
        LogicalHeight = logicalHeight;
        FramebufferScaleX = framebufferScaleX;
        FramebufferScaleY = framebufferScaleY;
        DisplaySize = new Vector2(logicalWidth, logicalHeight);
        DisplayFramebufferScale = new Vector2(framebufferScaleX, framebufferScaleY);
    }

    /// <summary>
    /// 平台窗口逻辑宽度。
    /// </summary>
    public int LogicalWidth { get; }

    /// <summary>
    /// 平台窗口逻辑高度。
    /// </summary>
    public int LogicalHeight { get; }

    /// <summary>
    /// 逻辑坐标到默认 framebuffer 坐标的 X 轴缩放。
    /// </summary>
    public float FramebufferScaleX { get; }

    /// <summary>
    /// 逻辑坐标到默认 framebuffer 坐标的 Y 轴缩放。
    /// </summary>
    public float FramebufferScaleY { get; }

    /// <summary>
    /// 传给 ImGuiIO.DisplaySize 的逻辑显示尺寸。
    /// </summary>
    public Vector2 DisplaySize { get; }

    /// <summary>
    /// 传给 ImGuiIO.DisplayFramebufferScale 的 framebuffer 缩放。
    /// </summary>
    public Vector2 DisplayFramebufferScale { get; }

    /// <summary>
    /// 创建 ImGui 帧尺寸换算结果。
    /// </summary>
    /// <param name="width">平台窗口逻辑宽度。</param>
    /// <param name="height">平台窗口逻辑高度。</param>
    /// <param name="framebufferScaleX">逻辑坐标到默认 framebuffer 坐标的 X 轴缩放。</param>
    /// <param name="framebufferScaleY">逻辑坐标到默认 framebuffer 坐标的 Y 轴缩放。</param>
    /// <returns>规范化后的 ImGui 帧尺寸。</returns>
    public static ImGuiFrameMetrics Create(int width, int height, float framebufferScaleX, float framebufferScaleY)
    {
        int logicalWidth = Math.Max(1, width);
        int logicalHeight = Math.Max(1, height);
        return new ImGuiFrameMetrics(
            logicalWidth,
            logicalHeight,
            NormalizeScale(framebufferScaleX),
            NormalizeScale(framebufferScaleY));
    }

    private static float NormalizeScale(float scale)
    {
        return float.IsFinite(scale) && scale > 0f ? scale : 1f;
    }
}
