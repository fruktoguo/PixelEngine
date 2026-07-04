using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// UI 层 present 上下文。共享同一个 OpenGL context 与默认 framebuffer。
/// </summary>
/// <param name="Gl">共享 OpenGL 上下文。</param>
/// <param name="FramebufferWidth">默认 framebuffer 宽度。</param>
/// <param name="FramebufferHeight">默认 framebuffer 高度。</param>
/// <param name="WorldViewport">世界画面在默认 framebuffer 中的呈现区域。</param>
public readonly record struct UiPresentContext(
    GL Gl,
    int FramebufferWidth,
    int FramebufferHeight,
    PresentationViewport WorldViewport);
