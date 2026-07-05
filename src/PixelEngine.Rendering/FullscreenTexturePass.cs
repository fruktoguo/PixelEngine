using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

internal sealed class FullscreenTexturePass : IDisposable
{
    private readonly GL _gl;
    private readonly ShaderProgram _program;
    private readonly int _sourceLocation;
    private bool _disposed;

    public FullscreenTexturePass(GL gl, string fragmentSource, GlslProfile profile)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentNullException.ThrowIfNull(fragmentSource);
        _gl = gl;
        _program = ShaderProgram.Create(gl, LightingShaderSources.FullscreenVertex(profile), fragmentSource);
        _sourceLocation = _program.GetUniformLocation("uSourceTexture");
    }

    public int Uniform(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _program.GetUniformLocation(name);
    }

    public void Begin(ColorRenderTarget source, ColorRenderTarget destination, FullscreenQuad quad)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(quad);
        ObjectDisposedException.ThrowIf(_disposed, this);
        destination.BindFramebuffer();
        _gl.Viewport(0, 0, (uint)destination.Width, (uint)destination.Height);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.ScissorTest);
        _program.Use();
        source.BindTexture(0);
        _gl.Uniform1(_sourceLocation, 0);
    }

    public void Draw(FullscreenQuad quad)
    {
        ArgumentNullException.ThrowIfNull(quad);
        ObjectDisposedException.ThrowIf(_disposed, this);
        quad.Draw();
    }

    public void Uniform1(int location, float value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gl.Uniform1(location, value);
    }

    public void Uniform2(int location, float x, float y)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gl.Uniform2(location, x, y);
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
}
