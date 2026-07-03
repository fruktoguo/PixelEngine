using System.Diagnostics;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Rendering.Compute;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using Silk.NET.OpenGL;

namespace PixelEngine.Rendering;

/// <summary>
/// 相位 10 渲染管线主入口，编排 GPU 上传、world blit、光照、bloom、post、UI hook 与 present。
/// </summary>
public sealed class RenderPipeline : IGpuComputeQualityDegrader, IRenderPresentationControl, IDisposable
{
    private readonly RenderWindow _window;
    private readonly GL _gl;
    private readonly FullscreenQuad _quad;
    private readonly WorldBlitPass _worldBlit;
    private readonly OverlayRenderer _overlay;
    private readonly GpuParticleRenderer _gpuParticles;
    private readonly CompositePass _composite;
    private readonly BloomPass _bloom;
    private readonly IComputeBackend _computeBackend;
    private readonly ComputeCapabilityGate _computeGate;
    private readonly GpuComputeProfiler _gpuComputeProfiler;
    private readonly GlGpuFrameProfiler _gpuFrameProfiler;
    private readonly ComputeBloomPass? _computeBloom;
    private readonly ComputeLightCompositePass? _computeLightComposite;
    private readonly RadianceCascadePass? _radianceCascades;
    private readonly DitherPass _dither;
    private readonly GammaPass _gamma;
    private readonly CrtPass _crt;
    private readonly PresentPass _present;
    private readonly WorldTexture _worldTexture;
    private readonly PboUploader _uploader;
    private readonly EmissiveBuffer _emissive;
    private readonly LightMaskTexture _occluder;
    private readonly LightMaskTexture _visibility;
    private readonly ColorRenderTarget _scene;
    private readonly ColorRenderTarget _lit;
    private readonly ColorRenderTarget _postA;
    private readonly ColorRenderTarget _postB;
    private byte[] _visibilityMask;
    private bool _hasUploadedWorld;
    private bool _disposed;

    /// <summary>
    /// 创建渲染管线。
    /// </summary>
    /// <param name="window">渲染窗口。</param>
    /// <param name="width">初始视口宽度。</param>
    /// <param name="height">初始视口高度。</param>
    /// <param name="computeFeatures">可选 plan/09 G4 compute 功能开关；未传入时使用默认安全配置。</param>
    public RenderPipeline(RenderWindow window, int width, int height, ComputeFeatureSwitches? computeFeatures = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ValidateSize(width, height);
        _window = window;
        _gl = window.Gl;
        GlslProfile profile = window.Capabilities.IsGles ? GlslProfile.Gles300 : GlslProfile.DesktopGl330;
        _quad = new FullscreenQuad(_gl);
        _worldBlit = new WorldBlitPass(_gl, profile);
        _overlay = new OverlayRenderer(_gl, profile);
        _gpuParticles = new GpuParticleRenderer(_gl, profile);
        _composite = new CompositePass(_gl, profile);
        _bloom = new BloomPass(_gl, profile);
        GpuCapabilities gpuCapabilities = GpuCapabilities.Query(_gl, window.Capabilities);
        _computeGate = ComputeCapabilityGate.Evaluate(gpuCapabilities, computeFeatures ?? ComputeFeatureSwitches.Default, preferComputeSharp: false);
        _computeBackend = ComputeBackendFactory.Create(_gl, _computeGate);
        _gpuComputeProfiler = new GpuComputeProfiler(_computeBackend);
        _gpuFrameProfiler = new GlGpuFrameProfiler(_gl, window.Capabilities);
        GpuComputeBloomPipeline? computeBloomPipeline = _computeGate.SelectedBackend == ComputeBackendKind.GlCompute
            ? new GpuComputeBloomPipeline(_computeBackend)
            : null;
        _computeBloom = computeBloomPipeline is not null && _computeGate.FeatureSwitches.BloomComputeEnabled
            ? new ComputeBloomPass(_gl, computeBloomPipeline)
            : null;
        _computeLightComposite = computeBloomPipeline is not null
            ? new ComputeLightCompositePass(computeBloomPipeline)
            : null;
        _radianceCascades = computeBloomPipeline is not null && _computeGate.FeatureSwitches.RadianceCascadesEnabled
            ? new RadianceCascadePass(_gl, new GpuRadianceCascadePipeline(_computeBackend), width, height)
            : null;
        _dither = new DitherPass(_gl, profile);
        _gamma = new GammaPass(_gl, profile);
        _crt = new CrtPass(_gl, profile);
        _present = new PresentPass(_gl, profile);
        _worldTexture = new WorldTexture(_gl, width, height);
        _uploader = new PboUploader(_gl, checked(width * height * sizeof(uint)));
        _emissive = new EmissiveBuffer(_gl, width, height);
        _occluder = new LightMaskTexture(_gl, width, height);
        _visibility = new LightMaskTexture(_gl, width, height);
        _scene = new ColorRenderTarget(_gl, width, height);
        _lit = new ColorRenderTarget(_gl, width, height);
        _postA = new ColorRenderTarget(_gl, width, height);
        _postB = new ColorRenderTarget(_gl, width, height);
        _visibilityMask = GC.AllocateArray<byte>(checked(width * height), pinned: true);
        _visibilityMask.AsSpan().Fill(byte.MaxValue);
        _visibility.Upload(_visibilityMask);
    }

    /// <summary>
    /// Present 前 UI 绘制 hook。回调收到共享 OpenGL 上下文，调用者可绑定默认 framebuffer 绘制 UI。
    /// </summary>
    public event Action<GL>? BeforePresentUi;

    /// <summary>
    /// UI 与 overlay 已写入默认 framebuffer、交换缓冲前的 hook。仅用于 Demo/测试截图等验收工具，不应在热路径做重工作。
    /// </summary>
    public event Action<GL>? BeforeSwapBuffers;

    /// <summary>
    /// 管线设置。
    /// </summary>
    public RenderPipelineSettings Settings { get; } = new();

    /// <summary>
    /// 当前管线是否可在 GPU point-sprite 路径接管自由粒子渲染。
    /// </summary>
    public bool CanRenderParticlesOnGpu =>
        Settings.ParticleRenderMode == ParticleRenderMode.GpuPointSprite &&
        _computeGate.FeatureSwitches.GpuParticlesEnabled;

    /// <inheritdoc />
    public bool VSyncEnabled
    {
        get => _window.VSyncEnabled;
        set => _window.VSyncEnabled = value;
    }

    /// <inheritdoc />
    public bool CanToggleVSync => true;

    /// <inheritdoc />
    public bool GpuFrameTimerAvailable => _gpuFrameProfiler.IsAvailable;

    /// <summary>
    /// 当前宽度。
    /// </summary>
    public int Width => _worldTexture.Width;

    /// <summary>
    /// 当前高度。
    /// </summary>
    public int Height => _worldTexture.Height;

    /// <summary>
    /// 最近一帧 post-process 后、present 前的最终画面纹理；供 Editor 视口只读采样。
    /// </summary>
    public RenderViewportTexture CurrentViewportTexture { get; private set; }

    /// <summary>
    /// 当设置允许且上下文支持 compute shader 时，plan/09 可接管高质量光照/RC。
    /// </summary>
    public bool ShouldDelegateComputeLighting =>
        Settings.PreferComputeLighting &&
        _computeLightComposite is not null;

    /// <summary>
    /// 创建 plan/09 compute pass 可复用的渲染资源句柄快照。本方法不创建新 GL 上下文，也不转移资源所有权。
    /// </summary>
    public GpuComputeResources CreateComputeResourcesSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new GpuComputeResources(
            Width,
            Height,
            _worldTexture.Handle,
            _emissive.Handle,
            _occluder.Handle,
            _visibility.Handle,
            _scene.Handle,
            _lit.Handle,
            _postA.Handle,
            _postB.Handle);
    }

    /// <summary>
    /// 将当前 GPU compute 后端与 G1-G4 门控状态发布到 Core 诊断计数器。
    /// </summary>
    /// <param name="counters">Core 诊断计数器。</param>
    public void PublishComputeDiagnostics(EngineCounters counters)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _computeGate.PublishDiagnostics(counters);
    }

    /// <summary>
    /// 视口 resize 时重建世界纹理、PBO 容量、FBO 链与 visibility mask。
    /// </summary>
    /// <param name="width">新宽度。</param>
    /// <param name="height">新高度。</param>
    public void Resize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSize(width, height);
        if (width == Width && height == Height)
        {
            return;
        }

        _worldTexture.Resize(width, height);
        _uploader.EnsureCapacity(checked(width * height * sizeof(uint)));
        _emissive.Resize(width, height);
        _occluder.Resize(width, height);
        _visibility.Resize(width, height);
        _scene.Resize(width, height);
        _lit.Resize(width, height);
        _postA.Resize(width, height);
        _postB.Resize(width, height);
        _visibilityMask = GC.AllocateArray<byte>(checked(width * height), pinned: true);
        _visibilityMask.AsSpan().Fill(byte.MaxValue);
        _visibility.Upload(_visibilityMask);
        _hasUploadedWorld = false;
        CurrentViewportTexture = default;
    }

    /// <summary>
    /// 按架构 §4.3 第二级降级策略降低一档光照质量。
    /// </summary>
    /// <returns>若发生降级返回 true；已在最低档时返回 false。</returns>
    public bool DegradeQualityOneStep()
    {
        LightingQualityLevel before = Settings.QualityLevel;
        Settings.QualityLevel = Settings.QualityLevel switch
        {
            LightingQualityLevel.Full => LightingQualityLevel.BloomDisabled,
            LightingQualityLevel.BloomDisabled => LightingQualityLevel.FogOfWarEmissiveOnly,
            LightingQualityLevel.FogOfWarEmissiveOnly => LightingQualityLevel.FogOfWarEmissiveOnly,
            _ => throw new ArgumentOutOfRangeException(nameof(Settings.QualityLevel), Settings.QualityLevel, "未知光照质量档位。"),
        };
        return Settings.QualityLevel != before;
    }

    /// <summary>
    /// 执行 plan/09 §4.7 的 GPU compute 第二级降级：Radiance Cascades → compute bloom → fragment bloom/fog 档。
    /// </summary>
    /// <returns>若发生降级返回 true；已在最低档时返回 false。</returns>
    public bool DegradeGpuComputeOneStep()
    {
        if (Settings.RadianceCascades.Enabled)
        {
            Settings.RadianceCascades = Settings.RadianceCascades with { Enabled = false };
            return true;
        }

        if (Settings.PreferComputeLighting)
        {
            Settings.PreferComputeLighting = false;
            return true;
        }

        return DegradeQualityOneStep();
    }

    /// <summary>
    /// 渲染一帧并 present。默认路径要求粒子已在相位 9 stamp 到 render buffer。
    /// </summary>
    /// <param name="renderBuffer">相位 9 输出的世界 BGRA8 buffer。</param>
    /// <param name="aux">相位 9 副输出。</param>
    /// <param name="camera">只读相机快照。</param>
    /// <param name="dirtyRects">需要上传的 dirty rect；首次渲染会强制全帧上传。</param>
    /// <param name="overlays">在 world blit 后、光照前绘制的屏幕空间 overlay 命令。</param>
    /// <param name="fogOfWar">可选 fog-of-war reveal map。</param>
    /// <param name="profiler">可选 Core 诊断 profiler。</param>
    public void RenderFrame(
        RenderBuffer renderBuffer,
        RenderAuxBuffers aux,
        CameraState camera,
        ReadOnlySpan<PixelUploadRect> dirtyRects,
        ReadOnlySpan<OverlayCommand> overlays,
        FogOfWarBuffer? fogOfWar = null,
        FrameProfiler? profiler = null)
    {
        RenderFrame(renderBuffer, aux, camera, dirtyRects, overlays, [], [], null, fogOfWar, profiler);
    }

    /// <summary>
    /// 渲染一帧并 present，并在 <see cref="RenderPipelineSettings.ParticleRenderMode"/> 为 GPU 模式时批绘自由粒子。
    /// </summary>
    /// <param name="renderBuffer">相位 9 输出的世界 BGRA8 buffer；GPU 粒子模式下调用者不应再把同一批粒子 stamp 进此 buffer。</param>
    /// <param name="aux">相位 9 副输出。</param>
    /// <param name="camera">只读相机快照。</param>
    /// <param name="dirtyRects">需要上传的 dirty rect；首次渲染会强制全帧上传。</param>
    /// <param name="overlays">在 world blit 后、光照前绘制的屏幕空间 overlay 命令。</param>
    /// <param name="particles">plan/05 自由粒子活跃前缀；GPU 模式只读，不修改粒子状态。</param>
    /// <param name="materials">粒子材质表；GPU 模式用于生成上传颜色与 emissive 标志。</param>
    /// <param name="fogOfWar">可选 fog-of-war reveal map。</param>
    /// <param name="profiler">可选 Core 诊断 profiler。</param>
    public void RenderFrame(
        RenderBuffer renderBuffer,
        RenderAuxBuffers aux,
        CameraState camera,
        ReadOnlySpan<PixelUploadRect> dirtyRects,
        ReadOnlySpan<OverlayCommand> overlays,
        ReadOnlySpan<Particle> particles,
        MaterialTable? materials,
        FogOfWarBuffer? fogOfWar = null,
        FrameProfiler? profiler = null)
    {
        RenderFrame(renderBuffer, aux, camera, dirtyRects, overlays, [], particles, materials, fogOfWar, profiler);
    }

    /// <summary>
    /// 渲染一帧并 present，并在 <see cref="RenderPipelineSettings.ParticleRenderMode"/> 为 GPU 模式时批绘自由粒子，同时消费点光源。
    /// </summary>
    /// <param name="renderBuffer">相位 9 输出的世界 BGRA8 buffer；GPU 粒子模式下调用者不应再把同一批粒子 stamp 进此 buffer。</param>
    /// <param name="aux">相位 9 副输出。</param>
    /// <param name="camera">只读相机快照。</param>
    /// <param name="dirtyRects">需要上传的 dirty rect；首次渲染会强制全帧上传。</param>
    /// <param name="overlays">在 world blit 后、光照前绘制的屏幕空间 overlay 命令。</param>
    /// <param name="pointLights">脚本或玩法提交的视口空间点光源。</param>
    /// <param name="particles">plan/05 自由粒子活跃前缀；GPU 模式只读，不修改粒子状态。</param>
    /// <param name="materials">粒子材质表；GPU 模式用于生成上传颜色与 emissive 标志。</param>
    /// <param name="fogOfWar">可选 fog-of-war reveal map。</param>
    /// <param name="profiler">可选 Core 诊断 profiler。</param>
    public void RenderFrame(
        RenderBuffer renderBuffer,
        RenderAuxBuffers aux,
        CameraState camera,
        ReadOnlySpan<PixelUploadRect> dirtyRects,
        ReadOnlySpan<OverlayCommand> overlays,
        ReadOnlySpan<LightSource> pointLights,
        ReadOnlySpan<Particle> particles,
        MaterialTable? materials,
        FogOfWarBuffer? fogOfWar = null,
        FrameProfiler? profiler = null)
    {
        ArgumentNullException.ThrowIfNull(renderBuffer);
        ArgumentNullException.ThrowIfNull(aux);
        ObjectDisposedException.ThrowIf(_disposed, this);
        Settings.Validate();
        _gpuComputeProfiler.ResolveCompleted(profiler);
        _gpuFrameProfiler.ResolveCompleted(profiler);
        ValidateInputs(renderBuffer, aux, camera);
        Resize(renderBuffer.Width, renderBuffer.Height);
        GlGpuFrameProfiler.GpuFrameScope gpuFrame = _gpuFrameProfiler.Measure();

        long started = Stopwatch.GetTimestamp();
        UploadWorld(renderBuffer, dirtyRects);
        _emissive.Upload(aux.Emissive);
        _occluder.Upload(aux.Occluder);
        UploadVisibility(fogOfWar, pointLights);
        RecordSub(profiler, FrameSubPhase.GpuUpload, started);

        started = Stopwatch.GetTimestamp();
        _scene.Clear();
        _worldBlit.Render(_worldTexture, _scene, camera, _quad);
        RenderGpuParticlesIfEnabled(particles, materials, camera);
        if (ShouldUseComputeLightComposite())
        {
            using GpuComputeProfiler.GpuTimerScope _ = _gpuComputeProfiler.Measure("light_composite", FrameSubPhase.GpuLightComposite);
            _lit.Clear();
            _computeLightComposite!.Render(_scene, _visibility, _emissive, _lit);
        }
        else
        {
            _lit.BindFramebuffer();
            _gl.Disable(EnableCap.ScissorTest);
            _gl.ClearColor(0f, 0f, 0f, 0f);
            _gl.Clear(ClearBufferMask.ColorBufferBit);
            _gl.Viewport(0, 0, (uint)Width, (uint)Height);
            _composite.Render(_scene, _emissive, _visibility, _quad);
        }
        RecordSub(profiler, FrameSubPhase.Lighting, started);

        ColorRenderTarget current = _lit;
        if (Settings.QualityLevel == LightingQualityLevel.Full)
        {
            if (ShouldUseRadianceCascades())
            {
                using GpuComputeProfiler.GpuTimerScope _ = _gpuComputeProfiler.Measure("radiance_cascades", FrameSubPhase.GpuRadianceCascades);
                _radianceCascades!.Render(_occluder, _emissive, current, _postB, Settings.RadianceCascades);
                current = _postB;
            }

            started = Stopwatch.GetTimestamp();
            if (ShouldUseComputeBloom(Settings.Bloom))
            {
                using GpuComputeProfiler.GpuTimerScope _ = _gpuComputeProfiler.Measure("compute_bloom", FrameSubPhase.GpuComputeBloom);
                _computeBloom!.Render(current, _postA, Settings.Bloom);
            }
            else
            {
                _bloom.Render(current, _postA, _quad, Settings.Bloom);
            }

            current = _postA;
            RecordSub(profiler, FrameSubPhase.Bloom, started);
        }

        started = Stopwatch.GetTimestamp();
        current = RenderPost(current);
        CurrentViewportTexture = new RenderViewportTexture(current.Handle, current.Width, current.Height);
        RecordSub(profiler, FrameSubPhase.PostProcess, started);

        started = Stopwatch.GetTimestamp();
        PresentationViewport presentation = PresentationViewport.Fit(Width, Height, _window.Width, _window.Height);
        _present.Render(current, presentation, _quad);
        _overlay.Render(overlays, presentation);
        _gl.Viewport(0, 0, (uint)_window.Width, (uint)_window.Height);
        BeforePresentUi?.Invoke(_gl);
        BeforeSwapBuffers?.Invoke(_gl);
        RecordSub(profiler, FrameSubPhase.Present, started);
        gpuFrame.Dispose();

        started = Stopwatch.GetTimestamp();
        _window.SwapBuffers();
        RecordSub(profiler, FrameSubPhase.PresentWait, started);
    }

    /// <summary>
    /// 使用空 dirty rect 渲染一帧。
    /// </summary>
    /// <param name="renderBuffer">相位 9 输出的世界 BGRA8 buffer。</param>
    /// <param name="aux">相位 9 副输出。</param>
    /// <param name="camera">只读相机快照。</param>
    /// <param name="profiler">可选 Core 诊断 profiler。</param>
    public void RenderFrame(RenderBuffer renderBuffer, RenderAuxBuffers aux, CameraState camera, FrameProfiler? profiler = null)
    {
        RenderFrame(renderBuffer, aux, camera, [], [], default, profiler);
    }

    /// <summary>
    /// 使用 dirty rect 且不提交 overlay 渲染一帧。
    /// </summary>
    /// <param name="renderBuffer">相位 9 输出的世界 BGRA8 buffer。</param>
    /// <param name="aux">相位 9 副输出。</param>
    /// <param name="camera">只读相机快照。</param>
    /// <param name="dirtyRects">需要上传的 dirty rect。</param>
    /// <param name="fogOfWar">可选 fog-of-war reveal map。</param>
    /// <param name="profiler">可选 Core 诊断 profiler。</param>
    public void RenderFrame(
        RenderBuffer renderBuffer,
        RenderAuxBuffers aux,
        CameraState camera,
        ReadOnlySpan<PixelUploadRect> dirtyRects,
        FogOfWarBuffer? fogOfWar = null,
        FrameProfiler? profiler = null)
    {
        RenderFrame(renderBuffer, aux, camera, dirtyRects, [], fogOfWar, profiler);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _postB.Dispose();
        _postA.Dispose();
        _lit.Dispose();
        _scene.Dispose();
        _visibility.Dispose();
        _occluder.Dispose();
        _emissive.Dispose();
        _uploader.Dispose();
        _worldTexture.Dispose();
        _present.Dispose();
        _crt.Dispose();
        _gamma.Dispose();
        _dither.Dispose();
        _radianceCascades?.Dispose();
        _computeBloom?.Dispose();
        _gpuFrameProfiler.Dispose();
        _gpuComputeProfiler.Dispose();
        _computeBackend.Dispose();
        _bloom.Dispose();
        _composite.Dispose();
        _gpuParticles.Dispose();
        _overlay.Dispose();
        _worldBlit.Dispose();
        _quad.Dispose();
        _disposed = true;
    }

    private void UploadWorld(RenderBuffer renderBuffer, ReadOnlySpan<PixelUploadRect> dirtyRects)
    {
        if (!_hasUploadedWorld)
        {
            _uploader.UploadFull(_worldTexture, renderBuffer);
            _hasUploadedWorld = true;
            return;
        }

        if (dirtyRects.IsEmpty)
        {
            return;
        }

        _uploader.UploadDirtyRects(_worldTexture, renderBuffer, dirtyRects);
    }

    private void UploadVisibility(FogOfWarBuffer? fogOfWar, ReadOnlySpan<LightSource> pointLights)
    {
        if (fogOfWar is null)
        {
            _visibilityMask.AsSpan().Fill(byte.MaxValue);
            ApplyPointLights(_visibilityMask, pointLights);
            _visibility.Upload(_visibilityMask);
            return;
        }

        if (fogOfWar.ViewportCellWidth != Width || fogOfWar.ViewportCellHeight != Height)
        {
            throw new ArgumentException("FogOfWarBuffer 视口尺寸必须与 RenderPipeline 一致。", nameof(fogOfWar));
        }

        Span<byte> mask = _visibilityMask;
        for (int y = 0; y < Height; y++)
        {
            int row = y * Width;
            for (int x = 0; x < Width; x++)
            {
                mask[row + x] = fogOfWar.RevealAlpha(x, y);
            }
        }

        ApplyPointLights(mask, pointLights);
        _visibility.Upload(mask);
    }

    private void ApplyPointLights(Span<byte> mask, ReadOnlySpan<LightSource> pointLights)
    {
        for (int i = 0; i < pointLights.Length; i++)
        {
            LightSource light = pointLights[i];
            light.Validate();
            int minX = Math.Max(0, (int)MathF.Floor(light.X - light.Radius));
            int minY = Math.Max(0, (int)MathF.Floor(light.Y - light.Radius));
            int maxX = Math.Min(Width, (int)MathF.Ceiling(light.X + light.Radius));
            int maxY = Math.Min(Height, (int)MathF.Ceiling(light.Y + light.Radius));
            float inverseRadius = 1f / light.Radius;
            for (int y = minY; y < maxY; y++)
            {
                int row = y * Width;
                float dy = y + 0.5f - light.Y;
                for (int x = minX; x < maxX; x++)
                {
                    float dx = x + 0.5f - light.X;
                    float distance = MathF.Sqrt((dx * dx) + (dy * dy));
                    if (distance > light.Radius)
                    {
                        continue;
                    }

                    int contribution = (int)MathF.Round((1f - (distance * inverseRadius)) * light.Intensity * byte.MaxValue);
                    int index = row + x;
                    if (contribution > mask[index])
                    {
                        mask[index] = (byte)Math.Min(byte.MaxValue, contribution);
                    }
                }
            }
        }
    }

    private ColorRenderTarget RenderPost(ColorRenderTarget source)
    {
        ColorRenderTarget current = source;
        ColorRenderTarget next = ReferenceEquals(current, _postA) ? _postB : _postA;
        if (Settings.EnableDither && Settings.QualityLevel != LightingQualityLevel.FogOfWarEmissiveOnly)
        {
            _dither.Render(current, next, _quad, Settings.DitherStrength);
            current = next;
            next = ReferenceEquals(current, _postA) ? _postB : _postA;
        }

        _gamma.Render(current, next, _quad, Settings.Gamma);
        current = next;
        next = ReferenceEquals(current, _postA) ? _postB : _postA;
        if (Settings.EnableCrt && Settings.QualityLevel == LightingQualityLevel.Full)
        {
            _crt.Render(current, next, _quad, Settings.CrtScanlineStrength, Settings.CrtCurvature);
            current = next;
        }

        return current;
    }

    private void RenderGpuParticlesIfEnabled(ReadOnlySpan<Particle> particles, MaterialTable? materials, CameraState camera)
    {
        if (Settings.ParticleRenderMode != ParticleRenderMode.GpuPointSprite || particles.IsEmpty)
        {
            return;
        }

        if (!_computeGate.FeatureSwitches.GpuParticlesEnabled)
        {
            return;
        }

        if (materials is null)
        {
            throw new ArgumentNullException(nameof(materials), "GPU 粒子模式需要材质表。");
        }

        using GpuComputeProfiler.GpuTimerScope _ = _gpuComputeProfiler.Measure("gpu_particles", FrameSubPhase.GpuParticleDraw);
        _gpuParticles.Render(particles, materials, camera, _scene, _emissive);
    }

    private bool ShouldUseComputeBloom(BloomSettings settings)
    {
        return ShouldDelegateComputeLighting &&
            _computeBloom is not null &&
            _computeGate.FeatureSwitches.BloomComputeEnabled &&
            settings.Normalize().Mode == BloomMode.DualKawase;
    }

    private bool ShouldUseComputeLightComposite()
    {
        return ShouldDelegateComputeLighting;
    }

    private bool ShouldUseRadianceCascades()
    {
        return _radianceCascades is not null &&
            _computeGate.FeatureSwitches.RadianceCascadesEnabled &&
            Settings.RadianceCascades.Validate().Enabled;
    }

    private static void ValidateInputs(RenderBuffer renderBuffer, RenderAuxBuffers aux, CameraState camera)
    {
        if (renderBuffer.Width != aux.Width || renderBuffer.Height != aux.Height)
        {
            throw new ArgumentException("RenderBuffer 与 RenderAuxBuffers 尺寸必须一致。", nameof(aux));
        }

        if (camera.ViewportWidth != renderBuffer.Width || camera.ViewportHeight != renderBuffer.Height)
        {
            throw new ArgumentException("Camera viewport 必须与 RenderBuffer 尺寸一致。", nameof(camera));
        }
    }

    private static void ValidateSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "RenderPipeline 尺寸必须为正数。");
        }
    }

    private static void RecordSub(FrameProfiler? profiler, FrameSubPhase subPhase, long started)
    {
        if (profiler is null)
        {
            return;
        }

        long elapsed = Stopwatch.GetTimestamp() - started;
        profiler.RecordSub(subPhase, elapsed * 1000.0 / Stopwatch.Frequency);
    }
}
