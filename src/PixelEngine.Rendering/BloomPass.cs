using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// Bloom 后处理 pass：bright-pass → dual-Kawase mip 链或 Gaussian 回退 → additive composite。
/// </summary>
public sealed class BloomPass : IDisposable
{
    private readonly GL _gl;
    private readonly FullscreenTexturePass _brightPass;
    private readonly FullscreenTexturePass _kawaseDownPass;
    private readonly FullscreenTexturePass _kawaseUpPass;
    private readonly FullscreenTexturePass _gaussianPass;
    private readonly ShaderProgram _compositeProgram;
    private readonly int _brightThresholdLocation;
    private readonly int _downTexelSizeLocation;
    private readonly int _downOffsetLocation;
    private readonly int _upTexelSizeLocation;
    private readonly int _upOffsetLocation;
    private readonly int _upIntensityLocation;
    private readonly int _gaussianDirectionLocation;
    private readonly int _gaussianTexelSizeLocation;
    private readonly int _sceneLocation;
    private readonly int _bloomLocation;
    private readonly int _compositeIntensityLocation;
    private ColorRenderTarget[] _mips = [];
    private ColorRenderTarget? _gaussianScratch;
    private int _chainWidth;
    private int _chainHeight;
    private int _chainIterations;
    private bool _disposed;

    /// <summary>
    /// 创建 bloom pass。
    /// </summary>
    /// <param name="gl">OpenGL 入口。</param>
    /// <param name="profile">GLSL profile。</param>
    public BloomPass(GL gl, GlslProfile profile)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _gl = gl;
        _brightPass = new FullscreenTexturePass(gl, PostProcessShaderSources.BrightPassFragment(profile), profile);
        _kawaseDownPass = new FullscreenTexturePass(gl, PostProcessShaderSources.KawaseDownFragment(profile), profile);
        _kawaseUpPass = new FullscreenTexturePass(gl, PostProcessShaderSources.KawaseUpFragment(profile), profile);
        _gaussianPass = new FullscreenTexturePass(gl, PostProcessShaderSources.GaussianBlurFragment(profile), profile);
        _compositeProgram = ShaderProgram.Create(
            gl,
            LightingShaderSources.FullscreenVertex(profile),
            PostProcessShaderSources.BloomCompositeFragment(profile));
        _brightThresholdLocation = _brightPass.Uniform("uThreshold");
        _downTexelSizeLocation = _kawaseDownPass.Uniform("uTexelSize");
        _downOffsetLocation = _kawaseDownPass.Uniform("uOffset");
        _upTexelSizeLocation = _kawaseUpPass.Uniform("uTexelSize");
        _upOffsetLocation = _kawaseUpPass.Uniform("uOffset");
        _upIntensityLocation = _kawaseUpPass.Uniform("uIntensity");
        _gaussianDirectionLocation = _gaussianPass.Uniform("uDirection");
        _gaussianTexelSizeLocation = _gaussianPass.Uniform("uTexelSize");
        _sceneLocation = _compositeProgram.GetUniformLocation("uSceneTexture");
        _bloomLocation = _compositeProgram.GetUniformLocation("uBloomTexture");
        _compositeIntensityLocation = _compositeProgram.GetUniformLocation("uIntensity");
    }

    /// <summary>
    /// 执行 bloom 并输出到目标颜色 buffer。
    /// </summary>
    /// <param name="source">输入 scene 颜色。</param>
    /// <param name="destination">输出颜色目标。</param>
    /// <param name="quad">全屏三角形。</param>
    /// <param name="settings">Bloom 设置。</param>
    public void Render(
        ColorRenderTarget source,
        ColorRenderTarget destination,
        FullscreenQuad quad,
        BloomSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(quad);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (source.Width != destination.Width || source.Height != destination.Height)
        {
            throw new ArgumentException("Bloom source 与 destination 尺寸必须一致。", nameof(destination));
        }

        BloomSettings normalized = (settings ?? BloomSettings.Default).Normalize();
        EnsureChain(source.Width, source.Height, normalized.Iterations);
        RenderBrightPass(source, _mips[0], quad, normalized.Threshold);
        ColorRenderTarget bloomTexture = normalized.Mode == BloomMode.Gaussian
            ? RenderGaussian(quad, normalized)
            : RenderDualKawase(quad, normalized);
        RenderComposite(source, bloomTexture, destination, quad, normalized.Intensity);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        DisposeChain();
        _gaussianScratch?.Dispose();
        _brightPass.Dispose();
        _kawaseDownPass.Dispose();
        _kawaseUpPass.Dispose();
        _gaussianPass.Dispose();
        _compositeProgram.Dispose();
        _disposed = true;
    }

    private void RenderBrightPass(ColorRenderTarget source, ColorRenderTarget destination, FullscreenQuad quad, float threshold)
    {
        _brightPass.Begin(source, destination, quad);
        _brightPass.Uniform1(_brightThresholdLocation, threshold);
        _brightPass.Draw(quad);
    }

    private ColorRenderTarget RenderDualKawase(FullscreenQuad quad, BloomSettings settings)
    {
        for (int i = 1; i < _mips.Length; i++)
        {
            ColorRenderTarget source = _mips[i - 1];
            ColorRenderTarget destination = _mips[i];
            _kawaseDownPass.Begin(source, destination, quad);
            _kawaseDownPass.Uniform2(_downTexelSizeLocation, 1f / source.Width, 1f / source.Height);
            _kawaseDownPass.Uniform1(_downOffsetLocation, settings.KawaseOffset);
            _kawaseDownPass.Draw(quad);
        }

        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);
        for (int i = _mips.Length - 2; i >= 0; i--)
        {
            ColorRenderTarget source = _mips[i + 1];
            ColorRenderTarget destination = _mips[i];
            _kawaseUpPass.Begin(source, destination, quad);
            _kawaseUpPass.Uniform2(_upTexelSizeLocation, 1f / source.Width, 1f / source.Height);
            _kawaseUpPass.Uniform1(_upOffsetLocation, settings.KawaseOffset);
            _kawaseUpPass.Uniform1(_upIntensityLocation, 1f);
            _kawaseUpPass.Draw(quad);
        }

        _gl.Disable(EnableCap.Blend);
        return _mips[0];
    }

    private ColorRenderTarget RenderGaussian(FullscreenQuad quad, BloomSettings settings)
    {
        ColorRenderTarget scratch = EnsureGaussianScratch(_mips[0].Width, _mips[0].Height);
        for (int i = 0; i < settings.Iterations; i++)
        {
            _gaussianPass.Begin(_mips[0], scratch, quad);
            _gaussianPass.Uniform2(_gaussianDirectionLocation, 1f, 0f);
            _gaussianPass.Uniform2(_gaussianTexelSizeLocation, 1f / _mips[0].Width, 1f / _mips[0].Height);
            _gaussianPass.Draw(quad);

            _gaussianPass.Begin(scratch, _mips[0], quad);
            _gaussianPass.Uniform2(_gaussianDirectionLocation, 0f, 1f);
            _gaussianPass.Uniform2(_gaussianTexelSizeLocation, 1f / scratch.Width, 1f / scratch.Height);
            _gaussianPass.Draw(quad);
        }

        return _mips[0];
    }

    private void RenderComposite(
        ColorRenderTarget scene,
        ColorRenderTarget bloom,
        ColorRenderTarget destination,
        FullscreenQuad quad,
        float intensity)
    {
        destination.BindFramebuffer();
        _gl.Viewport(0, 0, (uint)destination.Width, (uint)destination.Height);
        _compositeProgram.Use();
        scene.BindTexture(0);
        bloom.BindTexture(1);
        _gl.Uniform1(_sceneLocation, 0);
        _gl.Uniform1(_bloomLocation, 1);
        _gl.Uniform1(_compositeIntensityLocation, intensity);
        quad.Draw();
    }

    private void EnsureChain(int width, int height, int iterations)
    {
        if (_chainWidth == width && _chainHeight == height && _chainIterations == iterations)
        {
            return;
        }

        DisposeChain();
        _chainWidth = width;
        _chainHeight = height;
        _chainIterations = iterations;
        _mips = new ColorRenderTarget[iterations];
        int mipWidth = width;
        int mipHeight = height;
        for (int i = 0; i < iterations; i++)
        {
            _mips[i] = new ColorRenderTarget(_gl, mipWidth, mipHeight);
            mipWidth = Math.Max(1, mipWidth / 2);
            mipHeight = Math.Max(1, mipHeight / 2);
        }

        _gaussianScratch?.Dispose();
        _gaussianScratch = null;
    }

    private ColorRenderTarget EnsureGaussianScratch(int width, int height)
    {
        if (_gaussianScratch is not null && _gaussianScratch.Width == width && _gaussianScratch.Height == height)
        {
            return _gaussianScratch;
        }

        _gaussianScratch?.Dispose();
        _gaussianScratch = new ColorRenderTarget(_gl, width, height);
        return _gaussianScratch;
    }

    private void DisposeChain()
    {
        for (int i = 0; i < _mips.Length; i++)
        {
            _mips[i].Dispose();
        }

        _mips = [];
        _chainWidth = 0;
        _chainHeight = 0;
        _chainIterations = 0;
    }
}
