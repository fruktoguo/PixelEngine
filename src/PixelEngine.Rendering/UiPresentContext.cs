using System.Diagnostics;
using PixelEngine.Core.Diagnostics;
using Silk.NET.OpenGL;

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
        : this(
            gl,
            framebufferWidth,
            framebufferHeight,
            worldViewport,
            UiPresentTarget.FromPresentationViewport(in worldViewport),
            UiPresentTarget.FromPresentationViewport(in worldViewport).Scissor,
            primitives,
            profiler)
    {
    }

    internal UiPresentContext(
        GL gl,
        int framebufferWidth,
        int framebufferHeight,
        PresentationViewport worldViewport,
        UiPresentTarget target,
        UiScissorRect clip,
        UiPrimitiveRenderer primitives,
        FrameProfiler? profiler)
    {
        target.Validate();
        clip.Validate();
        Gl = gl;
        FramebufferWidth = framebufferWidth;
        FramebufferHeight = framebufferHeight;
        WorldViewport = worldViewport;
        Target = target;
        Clip = clip;
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
    /// 当前 UI 层的目标区域。坐标以默认 framebuffer 左上角为原点。
    /// </summary>
    public UiPresentTarget Target { get; }

    /// <summary>
    /// 当前 UI 层必须遵守的 framebuffer 裁剪区域。
    /// </summary>
    public UiScissorRect Clip { get; }

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
        UiDrawState clipped = draw with { Scissor = Intersect(draw.Scissor, Clip) };
        _primitives.SubmitTriangles(vertices, indices, in clipped, FramebufferWidth, FramebufferHeight);
    }

    private static UiScissorRect? Intersect(UiScissorRect? requested, UiScissorRect clip)
    {
        if (clip.Width <= 0 || clip.Height <= 0)
        {
            return requested;
        }

        if (requested is not { } rect)
        {
            return clip;
        }

        int left = Math.Max(rect.X, clip.X);
        int top = Math.Max(rect.Y, clip.Y);
        int right = Math.Min(rect.X + rect.Width, clip.X + clip.Width);
        int bottom = Math.Min(rect.Y + rect.Height, clip.Y + clip.Height);
        return new UiScissorRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>
    /// 上传 UI overlay 纹理脏矩形，并将实际上传耗时记录到 <see cref="FrameSubPhase.UiUpload" />。
    /// </summary>
    /// <param name="texture">目标 UI overlay 纹理。</param>
    /// <param name="pixelsBgra">按行连续的 BGRA8 源像素。</param>
    /// <param name="sourceWidth">源位图宽度。</param>
    /// <param name="sourceHeight">源位图高度。</param>
    /// <param name="dirtyRects">待上传脏矩形。</param>
    public void UploadOverlayTexture(
        UiOverlayTexture texture,
        ReadOnlySpan<uint> pixelsBgra,
        int sourceWidth,
        int sourceHeight,
        ReadOnlySpan<PixelUploadRect> dirtyRects)
    {
        ArgumentNullException.ThrowIfNull(texture);
        long start = Stopwatch.GetTimestamp();
        texture.UploadDirtyRects(pixelsBgra, sourceWidth, sourceHeight, dirtyRects);
        Profiler?.RecordSub(FrameSubPhase.UiUpload, (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
    }

    /// <summary>
    /// 上传整张 UI overlay 纹理，并将实际上传耗时记录到 <see cref="FrameSubPhase.UiUpload" />。
    /// </summary>
    /// <param name="texture">目标 UI overlay 纹理。</param>
    /// <param name="pixelsBgra">按行连续的 BGRA8 源像素，长度必须等于纹理宽高乘积。</param>
    public void UploadOverlayTexture(UiOverlayTexture texture, ReadOnlySpan<uint> pixelsBgra)
    {
        ArgumentNullException.ThrowIfNull(texture);
        long start = Stopwatch.GetTimestamp();
        texture.Upload(pixelsBgra);
        Profiler?.RecordSub(FrameSubPhase.UiUpload, (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
    }
}
