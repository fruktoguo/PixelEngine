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
    private readonly PresentationComposePass _presentationCompose;
    private readonly PresentPass _present;
    private readonly UiPrimitiveRenderer _uiPrimitives;
    private readonly WorldTexture _worldTexture;
    private readonly PboUploader _uploader;
    private readonly EmissiveBuffer _emissive;
    private readonly LightMaskTexture _occluder;
    private readonly LightMaskTexture _visibility;
    private readonly ColorRenderTarget _scene;
    private readonly ColorRenderTarget _lit;
    private readonly ColorRenderTarget _postA;
    private readonly ColorRenderTarget _postB;
    private readonly ColorRenderTarget _presentation;
    private byte[] _visibilityMask;
    private UiLayerEntry[] _uiLayers = [];
    private int _nextUiLayerSequence;
    private bool _hasUploadedWorld;
    private bool _disposed;

    /// <summary>
    /// 创建渲染管线。
    /// </summary>
    /// <param name="window">渲染窗口。</param>
    /// <param name="width">初始视口宽度。</param>
    /// <param name="height">初始视口高度。</param>
    /// <param name="computeFeatures">可选 plan/09 G4 compute 功能开关；未传入时使用默认安全配置。</param>
    /// <param name="settings">可选渲染管线设置；用于在创建 compute gate 前传入 ComputeSharp 后端偏好。</param>
    public RenderPipeline(RenderWindow window, int width, int height, ComputeFeatureSwitches? computeFeatures = null, RenderPipelineSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ValidateSize(width, height);
        Settings = settings ?? new RenderPipelineSettings();
        Settings.Validate();
        _window = window;
        _gl = window.Gl;
        GlslProfile profile = window.Capabilities.IsGles ? GlslProfile.Gles300 : GlslProfile.DesktopGl330;
        // 阶段 1：创建全屏几何与 fragment 后处理 pass。
        _quad = new FullscreenQuad(_gl);
        _worldBlit = new WorldBlitPass(_gl, profile);
        _overlay = new OverlayRenderer(_gl, profile, presentationFramebuffer: window.PresentationFramebuffer);
        _gpuParticles = new GpuParticleRenderer(_gl, profile);
        _composite = new CompositePass(_gl, profile);
        _bloom = new BloomPass(_gl, profile);
        // 阶段 2：评估 GPU compute 能力并选择性装配 bloom/RC/composite 路径。
        GpuCapabilities gpuCapabilities = GpuCapabilities.Query(_gl, window.Capabilities);
        _computeGate = ComputeCapabilityGate.Evaluate(gpuCapabilities, computeFeatures ?? ComputeFeatureSwitches.Default, Settings.PreferComputeSharpBackend);
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
        // 阶段 3：分配世界纹理、PBO 上传器、光照 mask 与 scene/lit/post FBO 链。
        _dither = new DitherPass(_gl, profile);
        _gamma = new GammaPass(_gl, profile);
        _crt = new CrtPass(_gl, profile);
        _presentationCompose = new PresentationComposePass(_gl, profile);
        _present = new PresentPass(window, profile);
        _uiPrimitives = new UiPrimitiveRenderer(_gl, profile);
        _worldTexture = new WorldTexture(_gl, width, height);
        _uploader = new PboUploader(_gl, checked(width * height * sizeof(uint)));
        _emissive = new EmissiveBuffer(_gl, width, height);
        _occluder = new LightMaskTexture(_gl, width, height);
        _visibility = new LightMaskTexture(_gl, width, height);
        _scene = new ColorRenderTarget(_gl, width, height);
        _lit = new ColorRenderTarget(_gl, width, height);
        _postA = new ColorRenderTarget(_gl, width, height);
        _postB = new ColorRenderTarget(_gl, width, height);
        _presentation = new ColorRenderTarget(_gl, width, height);
        CurrentPresentation = RenderPresentationDescriptor.CreateInitial(width, height);
        MaximumTextureSize = QueryMaximumTextureSize(_gl);
        _visibilityMask = GC.AllocateArray<byte>(checked(width * height), pinned: true);
        _visibilityMask.AsSpan().Fill(byte.MaxValue);
        _visibility.Upload(_visibilityMask);
    }

    /// <summary>
    /// Present 前兼容 UI 绘制 hook。新 UI/Editor 叠层必须使用 <see cref="RegisterUiLayer(int, IUiPresentLayer)" /> 注册确定性 order。
    /// </summary>
    [Obsolete("请使用 RegisterUiLayer(order, layer) 注册确定性 UI present 层；该事件仅保留兼容。")]
    public event Action<GL>? BeforePresentUi;

    /// <summary>
    /// UI 与 overlay 已写入默认 framebuffer、交换缓冲前的 hook。仅用于 Demo/测试截图等验收工具，不应在热路径做重工作。
    /// </summary>
    public event Action<GL>? BeforeSwapBuffers;

    /// <summary>
    /// 管线设置。
    /// </summary>
    public RenderPipelineSettings Settings { get; }

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
    /// 当前 OpenGL context 允许的二维纹理最大边长；Game View 自定义分辨率必须遵守该上限。
    /// </summary>
    public int MaximumTextureSize { get; }

    /// <summary>
    /// 当前已在 render 帧边界提交的 world→presentation 描述。
    /// </summary>
    public RenderPresentationDescriptor CurrentPresentation { get; private set; }

    /// <summary>
    /// 最近一帧 world、gameplay overlay 与全部 game Canvas 合成后的完整 presentation 纹理；供 Editor 只读采样。
    /// </summary>
    public RenderViewportTexture CurrentViewportTexture { get; private set; }

    /// <summary>
    /// 已合成进 <see cref="CurrentViewportTexture" /> 的 overlay 命令数量。
    /// </summary>
    /// <remarks>
    /// 该值用于窗口验收确认 runtime viewport 不只是裸 world 纹理；窗口 resize 后、首帧产生前为 0。
    /// </remarks>
    public int CurrentViewportOverlayCount { get; private set; }

    /// <summary>
    /// 已注册 UI present 层数量。
    /// </summary>
    public int UiLayerCount { get; private set; }

    /// <summary>
    /// 在 render 前帧边界提交下一份 presentation 描述。相同 revision 只能重复提交完全相同的描述。
    /// </summary>
    /// <param name="descriptor">已由 Hosting 统一解析的 presentation 描述。</param>
    public void CommitPresentation(in RenderPresentationDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        descriptor.Validate(Width, Height);
        if (descriptor.PresentationWidth > MaximumTextureSize || descriptor.PresentationHeight > MaximumTextureSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(descriptor),
                $"Presentation {descriptor.PresentationWidth}×{descriptor.PresentationHeight} 超过 renderer 上限 {MaximumTextureSize}。");
        }

        if (descriptor.Revision < CurrentPresentation.Revision)
        {
            throw new InvalidOperationException(
                $"Presentation revision 不能倒退：current={CurrentPresentation.Revision}, requested={descriptor.Revision}。");
        }

        if (descriptor.Revision == CurrentPresentation.Revision && descriptor != CurrentPresentation)
        {
            throw new InvalidOperationException("同一 presentation revision 不能对应不同几何或 display metrics。");
        }

        _presentation.Resize(descriptor.PresentationWidth, descriptor.PresentationHeight);
        CurrentPresentation = descriptor;
    }

    /// <summary>
    /// 注册一个窗口 framebuffer UI 层。该兼容 overload 保留既有 CLR 签名与窗口绘制语义。
    /// </summary>
    /// <param name="order">排序值；越小越早绘制。</param>
    /// <param name="layer">UI 层实例。</param>
    /// <returns>释放即可注销该层。</returns>
    public IDisposable RegisterUiLayer(int order, IUiPresentLayer layer)
    {
        return RegisterUiLayer(UiPresentSurface.WindowFramebuffer, order, layer);
    }

    /// <summary>
    /// 在显式目标 surface 注册 UI 层。每个 surface 内按 order 升序、同 order 按注册顺序确定性绘制。
    /// </summary>
    /// <param name="surface">目标 surface；runtime 游戏 UI 与窗口宿主 UI 必须显式区分。</param>
    /// <param name="order">同一 surface 内的排序值；越小越早绘制。</param>
    /// <param name="layer">UI 层实例。</param>
    /// <returns>释放即可注销该层。</returns>
    public IDisposable RegisterUiLayer(UiPresentSurface surface, int order, IUiPresentLayer layer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(layer);
        if (surface is not (UiPresentSurface.RuntimeViewport or UiPresentSurface.WindowFramebuffer))
        {
            throw new ArgumentOutOfRangeException(nameof(surface), surface, "未知 UI present surface。");
        }

        EnsureUiLayerCapacity(UiLayerCount + 1);
        UiLayerEntry entry = new(surface, order, _nextUiLayerSequence++, layer);
        int index = UiLayerCount;
        while (index > 0 && CompareUiLayers(entry, _uiLayers[index - 1]) < 0)
        {
            _uiLayers[index] = _uiLayers[index - 1];
            index--;
        }

        _uiLayers[index] = entry;
        UiLayerCount++;
        return new UiLayerRegistration(this, layer);
    }

    /// <summary>
    /// 注销已注册 UI present 层。
    /// </summary>
    /// <param name="layer">UI 层实例。</param>
    /// <returns>若找到并注销返回 true。</returns>
    public bool UnregisterUiLayer(IUiPresentLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        for (int i = 0; i < UiLayerCount; i++)
        {
            if (!ReferenceEquals(_uiLayers[i].Layer, layer))
            {
                continue;
            }

            int moveCount = UiLayerCount - i - 1;
            if (moveCount > 0)
            {
                Array.Copy(_uiLayers, i + 1, _uiLayers, i, moveCount);
            }

            _uiLayers[--UiLayerCount] = default;
            return true;
        }

        return false;
    }

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
        CurrentPresentation = CurrentPresentation with
        {
            WorldViewport = PresentationViewport.Fit(
                width,
                height,
                CurrentPresentation.PresentationWidth,
                CurrentPresentation.PresentationHeight),
        };
        _visibilityMask = GC.AllocateArray<byte>(checked(width * height), pinned: true);
        _visibilityMask.AsSpan().Fill(byte.MaxValue);
        _visibility.Upload(_visibilityMask);
        _hasUploadedWorld = false;
        CurrentViewportTexture = default;
        CurrentViewportOverlayCount = 0;
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
        // --- GPU 上传阶段：世界像素、emissive/occluder 与 fog-of-war visibility ---
        UploadWorld(renderBuffer, dirtyRects);
        _emissive.Upload(aux.Emissive);
        _occluder.Upload(aux.Occluder);
        UploadVisibility(fogOfWar, pointLights);
        RecordSub(profiler, FrameSubPhase.GpuUpload, started);

        started = Stopwatch.GetTimestamp();
        // --- 光照合成阶段：world blit → GPU 粒子 → compute/fragment 光照 ---
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

        // --- Bloom/RC 阶段：仅在 Full 质量档启用 ---
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

        // --- Runtime viewport 阶段：dither → gamma → CRT → gameplay/debug overlay ---
        started = Stopwatch.GetTimestamp();
        current = RenderPost(current);
        // Editor 的 Game View 直接采样 CurrentViewportTexture，因此 gameplay overlay 必须先写入
        // 离屏 runtime surface，不能只在默认 framebuffer 上绘制，否则玩家/准星会被 Editor 面板覆盖。
        _overlay.Render(overlays, current);
        CurrentViewportOverlayCount = overlays.Length;
        RecordSub(profiler, FrameSubPhase.PostProcess, started);

        // --- Present 阶段：固定 world → presentation letterbox → Game UI → OS framebuffer → Editor UI ---
        started = Stopwatch.GetTimestamp();
        _presentationCompose.Render(current, _presentation, CurrentPresentation.WorldViewport, _quad);
        _presentation.BindFramebuffer();
        UiPresentTarget runtimeUiTarget = new(
            0,
            0,
            CurrentPresentation.PresentationWidth,
            CurrentPresentation.PresentationHeight,
            1f);
        PresentUiLayers(
            new UiPresentContext(
                _gl,
                CurrentPresentation.PresentationWidth,
                CurrentPresentation.PresentationHeight,
                CurrentPresentation.PresentationWidth,
                CurrentPresentation.PresentationHeight,
                CurrentPresentation.WorldViewport,
                runtimeUiTarget,
                runtimeUiTarget.Scissor,
                _uiPrimitives,
                profiler),
            UiPresentSurface.RuntimeViewport);

        // CurrentViewportTexture 是 letterbox 后 world + gameplay overlay + Game UI 的完整权威 presentation。
        // Editor Game View 只采样该纹理；Editor chrome/modal 在后续默认 framebuffer 阶段绘制，绝不进入它。
        CurrentViewportTexture = new RenderViewportTexture(
            _presentation.Handle,
            _presentation.Width,
            _presentation.Height,
            CurrentPresentation.Revision);
        PresentationViewport presentation = PresentationViewport.Fit(
            CurrentPresentation.PresentationWidth,
            CurrentPresentation.PresentationHeight,
            _window.Width,
            _window.Height);
        _present.Render(_presentation, presentation, _quad);
        _gl.Viewport(0, 0, (uint)_window.Width, (uint)_window.Height);
        UiPresentTarget windowUiTarget = new(0, 0, _window.Width, _window.Height, 1f);
        PresentUiLayers(
            new UiPresentContext(
                _gl,
                _window.Width,
                _window.Height,
                _window.LogicalWidth,
                _window.LogicalHeight,
                presentation,
                windowUiTarget,
                windowUiTarget.Scissor,
                _uiPrimitives,
                profiler),
            UiPresentSurface.WindowFramebuffer);
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

        _presentation.Dispose();
        _postB.Dispose();
        _postA.Dispose();
        _lit.Dispose();
        _scene.Dispose();
        _visibility.Dispose();
        _occluder.Dispose();
        _emissive.Dispose();
        _uploader.Dispose();
        _worldTexture.Dispose();
        _uiPrimitives.Dispose();
        _present.Dispose();
        _presentationCompose.Dispose();
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

    private static int QueryMaximumTextureSize(GL gl)
    {
        try
        {
            gl.GetInteger(GLEnum.MaxTextureSize, out int maximum);
            return Math.Max(1, maximum);
        }
        catch (Exception)
        {
            // GL 3.3 / ES 3.0 最低要求远高于默认 presentation；查询异常时保持保守且可执行的上限。
            return 2048;
        }
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

    // 构建 visibility mask：无 fog 时全可见，再叠加点光源 reveal 贡献。
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

    // 在 _postA/_postB 间乒乓切换，按设置串联 dither/gamma/CRT。
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

    // 按 surface 过滤并按已排序的 order/sequence 绘制；每层前后保存/恢复 GL 状态。
    private void PresentUiLayers(in UiPresentContext context, UiPresentSurface surface)
    {
        for (int i = 0; i < UiLayerCount; i++)
        {
            if (_uiLayers[i].Surface != surface)
            {
                continue;
            }

            UiGlStateSnapshot state = UiGlStateSnapshot.Capture(_gl);
            PrepareUiState(context);
            try
            {
                _uiLayers[i].Layer.Present(in context);
            }
            finally
            {
                state.Restore(_gl);
            }
        }
    }

    private void PrepareUiState(in UiPresentContext context)
    {
        _gl.Viewport(0, 0, (uint)context.FramebufferWidth, (uint)context.FramebufferHeight);
        ApplyUiPresentClip(context);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
    }

    private void ApplyUiPresentClip(in UiPresentContext context)
    {
        if (context.Clip.Width <= 0 || context.Clip.Height <= 0)
        {
            _gl.Disable(EnableCap.ScissorTest);
            return;
        }

        _gl.Enable(EnableCap.ScissorTest);
        _gl.Scissor(
            context.Clip.X,
            context.FramebufferHeight - context.Clip.Y - context.Clip.Height,
            (uint)context.Clip.Width,
            (uint)context.Clip.Height);
    }

    private void EnsureUiLayerCapacity(int required)
    {
        if (_uiLayers.Length >= required)
        {
            return;
        }

        int capacity = _uiLayers.Length == 0 ? 4 : _uiLayers.Length * 2;
        while (capacity < required)
        {
            capacity *= 2;
        }

        Array.Resize(ref _uiLayers, capacity);
    }

    private static int CompareUiLayers(in UiLayerEntry left, in UiLayerEntry right)
    {
        int order = left.Order.CompareTo(right.Order);
        return order != 0 ? order : left.Sequence.CompareTo(right.Sequence);
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

    private readonly record struct UiLayerEntry(
        UiPresentSurface Surface,
        int Order,
        int Sequence,
        IUiPresentLayer Layer);

    private sealed class UiLayerRegistration(RenderPipeline pipeline, IUiPresentLayer layer) : IDisposable
    {
        private RenderPipeline? _pipeline = pipeline;

        public void Dispose()
        {
            RenderPipeline? pipeline = _pipeline;
            if (pipeline is null)
            {
                return;
            }

            _ = pipeline.UnregisterUiLayer(layer);
            _pipeline = null;
        }
    }
}
