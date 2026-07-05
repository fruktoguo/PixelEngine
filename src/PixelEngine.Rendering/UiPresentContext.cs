using Silk.NET.OpenGL;
using PixelEngine.Core.Diagnostics;

namespace PixelEngine.Rendering;

/// <summary>
/// UI 层 present 上下文。共享同一个 OpenGL context、默认 framebuffer 与 UI 三角形批提交器。
/// </summary>
public readonly struct UiPresentContext
{
    private readonly UiPrimitiveRenderer _primitives;

    internal UiPresentContext(
        GL gl,
        int framebufferWidth,
        int framebufferHeight,
        PresentationViewport worldViewport,
        UiPrimitiveRenderer primitives,
        FrameProfiler? profiler)
    {
        Gl = gl;
        FramebufferWidth = framebufferWidth;
        FramebufferHeight = framebufferHeight;
        WorldViewport = worldViewport;
        _primitives = primitives;
        Profiler = profiler;
    }

    /// <summary>
    /// 共享 OpenGL 上下文。优先使用 <see cref="SubmitTriangles" />，仅后端确需直接调用 GL 时使用。
    /// </summary>
    public GL Gl { get; }

    /// <summary>
    /// 默认 framebuffer 宽度。
    /// </summary>
    public int FramebufferWidth { get; }

    /// <summary>
    /// 默认 framebuffer 高度。
    /// </summary>
    public int FramebufferHeight { get; }

    /// <summary>
    /// 世界画面在默认 framebuffer 中的呈现区域。
    /// </summary>
    public PresentationViewport WorldViewport { get; }

    /// <summary>
    /// 当前帧 profiler；UI 层可用它记录自身细分相位耗时。
    /// </summary>
    public FrameProfiler? Profiler { get; }

    /// <summary>
    /// 提交一批 2D UI 三角形。坐标以默认 framebuffer 左上角为原点，单位为像素。
    /// </summary>
    /// <param name="vertices">顶点 span。</param>
    /// <param name="indices">索引 span。</param>
    /// <param name="draw">绘制状态。</param>
    public void SubmitTriangles(ReadOnlySpan<UiVertex> vertices, ReadOnlySpan<ushort> indices, in UiDrawState draw)
    {
        _primitives.SubmitTriangles(vertices, indices, in draw, FramebufferWidth, FramebufferHeight);
    }
}
