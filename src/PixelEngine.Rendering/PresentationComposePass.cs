using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// 把固定内部 world/post-process 纹理等比写入独立 presentation surface，并清出明确 letterbox。
/// </summary>
internal sealed class PresentationComposePass : IDisposable
{
    private readonly GL _gl;
    private readonly ShaderProgram _program;
    private readonly int _sourceLocation;
    private bool _disposed;

    public PresentationComposePass(GL gl, GlslProfile profile)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _gl = gl;
        _program = ShaderProgram.Create(gl, LightingShaderSources.FullscreenVertex(profile), Fragment(profile));
        _sourceLocation = _program.GetUniformLocation("uSourceTexture");
    }

    public void Render(
        ColorRenderTarget source,
        ColorRenderTarget destination,
        in PresentationViewport viewport,
        FullscreenQuad quad)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(quad);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (viewport.SourceWidth != source.Width ||
            viewport.SourceHeight != source.Height ||
            viewport.TargetWidth != destination.Width ||
            viewport.TargetHeight != destination.Height)
        {
            throw new ArgumentException("Presentation viewport 与源/目标纹理尺寸不一致。", nameof(viewport));
        }

        destination.BindFramebuffer();
        _gl.Viewport(0, 0, (uint)destination.Width, (uint)destination.Height);
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
