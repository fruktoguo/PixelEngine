using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// 将内部颜色目标 blit 到默认 framebuffer。
/// </summary>
public sealed class PresentPass : IDisposable
{
    private readonly GL _gl;
    private readonly ShaderProgram _program;
    private readonly int _sourceLocation;
    private bool _disposed;

    /// <summary>
    /// 创建 present pass。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="profile">GLSL profile。</param>
    public PresentPass(GL gl, GlslProfile profile)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _gl = gl;
        _program = ShaderProgram.Create(gl, LightingShaderSources.FullscreenVertex(profile), Fragment(profile));
        _sourceLocation = _program.GetUniformLocation("uSourceTexture");
    }

    /// <summary>
    /// 绘制到默认 framebuffer。调用后由外层交换缓冲。
    /// </summary>
    /// <param name="source">输入颜色目标。</param>
    /// <param name="viewport">内部画布在默认 framebuffer 中的呈现区域。</param>
    /// <param name="quad">全屏三角形。</param>
    public void Render(ColorRenderTarget source, PresentationViewport viewport, FullscreenQuad quad)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(quad);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, (uint)viewport.TargetWidth, (uint)viewport.TargetHeight);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.ClearColor(0f, 0f, 0f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.Viewport(viewport.X, viewport.Y, (uint)viewport.Width, (uint)viewport.Height);
        _program.Use();
        source.BindTexture(0);
        _gl.Uniform1(_sourceLocation, 0);
        quad.Draw();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _program.Dispose();
        _disposed = true;
    }

    private static string Fragment(GlslProfile profile)
    {
        return GlslProfileSource.VersionHeader(profile) + """
in vec2 vUv;
layout(location = 0) out vec4 fragColor;

uniform sampler2D uSourceTexture;

void main()
{
    fragColor = texture(uSourceTexture, vUv);
}
""";
    }
}
