using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// 线性颜色到 sRGB 的 gamma 校正 pass。
/// </summary>
public sealed class GammaPass : IDisposable
{
    private readonly FullscreenTexturePass _pass;
    private readonly int _gammaLocation;
    private bool _disposed;

    /// <summary>
    /// 创建 gamma pass。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="profile">GLSL profile。</param>
    public GammaPass(GL gl, GlslProfile profile)
    {
        _pass = new FullscreenTexturePass(gl, PostProcessShaderSources.GammaFragment(profile), profile);
        _gammaLocation = _pass.Uniform("uGamma");
    }

    /// <summary>
    /// 执行 gamma 校正。
    /// </summary>
    /// <param name="source">输入颜色目标。</param>
    /// <param name="destination">输出颜色目标。</param>
    /// <param name="quad">全屏三角形。</param>
    /// <param name="gamma">gamma 值，默认 2.2。</param>
    public void Render(ColorRenderTarget source, ColorRenderTarget destination, FullscreenQuad quad, float gamma = 2.2f)
    {
        if (!float.IsFinite(gamma) || gamma <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(gamma), "gamma 必须为正有限数值。");
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        _pass.Begin(source, destination, quad);
        _pass.Uniform1(_gammaLocation, gamma);
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
