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
    /// <param name="viewportWidth">默认 framebuffer viewport 宽度。</param>
    /// <param name="viewportHeight">默认 framebuffer viewport 高度。</param>
    /// <param name="quad">全屏三角形。</param>
    public void Render(ColorRenderTarget source, int viewportWidth, int viewportHeight, FullscreenQuad quad)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(quad);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportWidth), "Present viewport 尺寸必须为正数。");
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, (uint)viewportWidth, (uint)viewportHeight);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.ScissorTest);
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
