using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// 可选 CRT/scanline 最终后处理 pass。
/// </summary>
public sealed class CrtPass : IDisposable
{
    private readonly FullscreenTexturePass _pass;
    private readonly int _scanlineStrengthLocation;
    private readonly int _curvatureLocation;
    private bool _disposed;

    /// <summary>
    /// 创建 CRT pass。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="profile">GLSL profile。</param>
    public CrtPass(GL gl, GlslProfile profile)
    {
        _pass = new FullscreenTexturePass(gl, PostProcessShaderSources.CrtFragment(profile), profile);
        _scanlineStrengthLocation = _pass.Uniform("uScanlineStrength");
        _curvatureLocation = _pass.Uniform("uCurvature");
    }

    /// <summary>
    /// 执行 CRT/scanline pass。
    /// </summary>
    /// <param name="source">输入颜色目标。</param>
    /// <param name="destination">输出颜色目标。</param>
    /// <param name="quad">全屏三角形。</param>
    /// <param name="scanlineStrength">scanline 强度。</param>
    /// <param name="curvature">屏幕曲率强度。</param>
    public void Render(
        ColorRenderTarget source,
        ColorRenderTarget destination,
        FullscreenQuad quad,
        float scanlineStrength = 0.12f,
        float curvature = 0.04f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pass.Begin(source, destination, quad);
        _pass.Uniform1(_scanlineStrengthLocation, Math.Clamp(scanlineStrength, 0f, 1f));
        _pass.Uniform1(_curvatureLocation, Math.Clamp(curvature, 0f, 0.5f));
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
