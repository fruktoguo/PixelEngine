using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// 全屏纹理采样 pass，将源 <see cref="ColorRenderTarget" /> 绘制到目标 FBO。
/// 用于 bloom、gamma、dither 等后处理链中的单纹理全屏着色。
/// </summary>
internal sealed class FullscreenTexturePass : IDisposable
{
    private readonly GL _gl;
    private readonly ShaderProgram _program;
    private readonly int _sourceLocation;
    private bool _disposed;

    /// <summary>
    /// 创建全屏纹理 pass，编译全屏顶点着色器与调用方提供的片元着色器。
    /// </summary>
    /// <param name="gl">OpenGL 上下文。</param>
    /// <param name="fragmentSource">片元着色器 GLSL 源码。</param>
    /// <param name="profile">目标 GLSL 配置档。</param>
    public FullscreenTexturePass(GL gl, string fragmentSource, GlslProfile profile)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentNullException.ThrowIfNull(fragmentSource);
        _gl = gl;
        _program = ShaderProgram.Create(gl, LightingShaderSources.FullscreenVertex(profile), fragmentSource);
        _sourceLocation = _program.GetUniformLocation("uSourceTexture");
    }

    /// <summary>
    /// 查询片元着色器 uniform 位置，供调用方在 Draw 前设置额外参数。
    /// </summary>
    /// <param name="name">uniform 名称。</param>
    /// <returns>uniform 位置；不存在时由底层返回 -1。</returns>
    public int Uniform(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _program.GetUniformLocation(name);
    }

    /// <summary>
    /// 绑定目标 FBO、设置视口并准备绘制；调用方随后设置额外 uniform 并调用 <see cref="Draw" />。
    /// </summary>
    /// <param name="source">源颜色纹理。</param>
    /// <param name="destination">目标渲染目标。</param>
    /// <param name="quad">全屏四边形几何体。</param>
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

    /// <summary>
    /// 绘制全屏四边形，完成当前 pass 的片元着色输出。
    /// </summary>
    /// <param name="quad">全屏四边形几何体。</param>
    public void Draw(FullscreenQuad quad)
    {
        ArgumentNullException.ThrowIfNull(quad);
        ObjectDisposedException.ThrowIf(_disposed, this);
        quad.Draw();
    }

    /// <summary>
    /// 向当前已绑定的着色器程序写入 float uniform。
    /// </summary>
    /// <param name="location">uniform 位置。</param>
    /// <param name="value">写入值。</param>
    public void Uniform1(int location, float value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gl.Uniform1(location, value);
    }

    /// <summary>
    /// 向当前已绑定的着色器程序写入 vec2 uniform。
    /// </summary>
    /// <param name="location">uniform 位置。</param>
    /// <param name="x">x 分量。</param>
    /// <param name="y">y 分量。</param>
    public void Uniform2(int location, float x, float y)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gl.Uniform2(location, x, y);
    }

    /// <summary>
    /// 释放着色器程序资源。
    /// </summary>
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
