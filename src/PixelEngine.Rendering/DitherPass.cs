using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// 有序 Bayer dither 后处理 pass，应在 gamma 校正前执行。
/// </summary>
public sealed class DitherPass : IDisposable
{
    private readonly FullscreenTexturePass _pass;
    private readonly int _strengthLocation;
    private bool _disposed;

    /// <summary>
    /// 创建 dither pass。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="profile">GLSL profile。</param>
    public DitherPass(GL gl, GlslProfile profile)
    {
        _pass = new FullscreenTexturePass(gl, PostProcessShaderSources.DitherFragment(profile), profile);
        _strengthLocation = _pass.Uniform("uStrength");
    }

    /// <summary>
    /// 执行 dither。
    /// </summary>
    /// <param name="source">输入颜色目标。</param>
    /// <param name="destination">输出颜色目标。</param>
    /// <param name="quad">全屏三角形。</param>
    /// <param name="strength">dither 强度。</param>
    public void Render(ColorRenderTarget source, ColorRenderTarget destination, FullscreenQuad quad, float strength = 1f / 255f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pass.Begin(source, destination, quad);
        _pass.Uniform1(_strengthLocation, Math.Clamp(strength, 0f, 1f));
        _pass.Draw(quad);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _pass.Dispose();
        _disposed = true;
    }
}
