using System.Numerics;

namespace PixelEngine.Gui;

/// <summary>
/// ImGui 帧尺寸换算结果。Hexa OpenGL 后端按 DisplaySize 建默认 framebuffer viewport，因此 DisplaySize 使用 framebuffer 像素。
/// </summary>
public readonly record struct ImGuiFrameMetrics
{
    private ImGuiFrameMetrics(int logicalWidth, int logicalHeight, float framebufferScaleX, float framebufferScaleY)
    {
        LogicalWidth = logicalWidth;
        LogicalHeight = logicalHeight;
        FramebufferScaleX = framebufferScaleX;
        FramebufferScaleY = framebufferScaleY;
        FramebufferWidth = Math.Max(1, (int)MathF.Round(logicalWidth * framebufferScaleX));
        FramebufferHeight = Math.Max(1, (int)MathF.Round(logicalHeight * framebufferScaleY));
        DisplaySize = new Vector2(FramebufferWidth, FramebufferHeight);
        DisplayFramebufferScale = Vector2.One;
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
    /// 默认 framebuffer 宽度。
    /// </summary>
    public int FramebufferWidth { get; }

    /// <summary>
    /// 默认 framebuffer 高度。
    /// </summary>
    public int FramebufferHeight { get; }

    /// <summary>
    /// 逻辑坐标到默认 framebuffer 坐标的 X 轴缩放。
    /// </summary>
    public float FramebufferScaleX { get; }

    /// <summary>
    /// 逻辑坐标到默认 framebuffer 坐标的 Y 轴缩放。
    /// </summary>
    public float FramebufferScaleY { get; }

    /// <summary>
    /// 传给 ImGuiIO.DisplaySize 的显示尺寸。Hexa OpenGL 后端要求它与默认 framebuffer viewport 一致。
    /// </summary>
    public Vector2 DisplaySize { get; }

    /// <summary>
    /// 传给 ImGuiIO.DisplayFramebufferScale 的 framebuffer 缩放；Hexa OpenGL 后端路径已在 DisplaySize 中吸收缩放。
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

    /// <summary>
    /// 将平台逻辑鼠标坐标映射到当前 ImGui framebuffer 坐标。
    /// </summary>
    public Vector2 MapMousePosition(float x, float y)
    {
        return new Vector2(x * FramebufferScaleX, y * FramebufferScaleY);
    }

    private static float NormalizeScale(float scale)
    {
        return float.IsFinite(scale) && scale > 0f ? scale : 1f;
    }
}
