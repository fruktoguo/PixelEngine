using PixelEngine.Gui;
using PixelEngine.Physics;
using PixelEngine.Rendering;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using PixelEngine.Scripting;
using System.Runtime.InteropServices;

namespace PixelEngine.Hosting;

/// <summary>
/// 将 Simulation、脚本相机/光照和 Rendering 管线绑定到 Hosting 渲染相位。
/// </summary>
public sealed class RenderPhaseDriver(
    IChunkSource chunks,
    MaterialTable materials,
    TemperatureField temperature,
    ParticleSystem particles,
    ScriptCameraSynchronizer camera,
    ScriptLightingSynchronizer lighting,
    IRenderFrameSink sink,
    Core.Threading.JobSystem? jobs = null,
    IMaterialTextureProvider? textures = null,
    SimulationKernel? kernel = null,
    PhysicsSystem? physics = null,
    ScriptOverlayApi? scriptOverlays = null,
    DebugOverlayController? debugOverlays = null,
    IWorldVisualLayerProvider? worldVisualLayers = null) : IEnginePhaseDriver
{
    private readonly IChunkSource _chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
    private readonly MaterialTable _materials = materials ?? throw new ArgumentNullException(nameof(materials));
    private readonly TemperatureField _temperature = temperature ?? throw new ArgumentNullException(nameof(temperature));
    private readonly ParticleSystem _particles = particles ?? throw new ArgumentNullException(nameof(particles));
    private readonly ScriptCameraSynchronizer _camera = camera ?? throw new ArgumentNullException(nameof(camera));
    private readonly ScriptLightingSynchronizer _lighting = lighting ?? throw new ArgumentNullException(nameof(lighting));
    private readonly IRenderFrameSink _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    private readonly SimulationKernel? _kernel = kernel;
    private readonly PhysicsSystem? _physics = physics;
    private readonly ScriptOverlayApi? _scriptOverlays = scriptOverlays;
    private readonly DebugOverlayController? _debugOverlays = debugOverlays;
    private readonly IWorldVisualLayerProvider? _worldVisualLayers = worldVisualLayers;
    private readonly WorldVisualLayerDescriptor[] _viewportWorldVisualLayers = new WorldVisualLayerDescriptor[96];
    private readonly RenderBufferBuilder _builder = new(jobs, textures);
    private readonly ParticleCompositor _particleCompositor = new(textures);
    private readonly RenderBuffer _renderBuffer = new(1, 1);
    private readonly RenderAuxBuffers _aux = new(1, 1);
    private readonly List<OverlayCommand> _overlayCommands = new(256);
    private readonly BoundaryWakeSnapshot[] _boundaryWakeBuffer = new BoundaryWakeSnapshot[256];
    private readonly CaIterationSnapshot[] _caIterationBuffer = new CaIterationSnapshot[256];
    private readonly ConnectedComponentDebugSnapshot[] _connectedComponentBuffer = new ConnectedComponentDebugSnapshot[256];
    private CameraState _lastBuiltCamera = CameraState.OneToOne(0, 0, 1, 1);
    private bool _frameBuilt;
    private bool _hasBuiltCamera;
    private bool _hadCpuParticleStamps;
    private bool _worldInvalidated;
    private bool _authoringVisibilityOverride;

    /// <summary>
    /// 最近一次相位 10 实际提交给 Rendering 的 overlay 命令数量，用于 Demo/测试诊断脚本可见层是否进入渲染链路。
    /// </summary>
    public int LastOverlayCount { get; private set; }

    /// <summary>
    /// RenderStyle 着色质量控制器，供 Hosting 过载策略独立降级 CPU 样式着色。
    /// </summary>
    public IRenderStyleQualityController RenderStyleQuality => _builder;

    /// <summary>
    /// 最近一次实际提交给运行时渲染管线的相机；Editor Scene View 在 Play/Paused 下以此对齐同一世界视口。
    /// </summary>
    public CameraState CurrentCamera { get; private set; } = CameraState.OneToOne(0, 0, 1, 1);

    /// <summary>
    /// 请求下一张 render-only 或 simulation frame 强制重建完整世界缓冲。
    /// </summary>
    /// <remarks>
    /// Edit 模式不执行 dirty-rect swap；Scene authoring provider、画刷和 Play 快照恢复
    /// 因此必须通过该显式边界通知 Game View，不能继续显示旧纹理或初始黑帧。
    /// </remarks>
    public void InvalidateWorld()
    {
        _builder.InvalidateWorldContent();
        _worldInvalidated = true;
    }

    /// <summary>
    /// 设置 Edit 模式的 authoring 可见性覆盖；启用时忽略尚未由 runtime 脚本 reveal 的 fog mask。
    /// </summary>
    /// <param name="enabled">是否显示完整 authoring world。</param>
    /// <remarks>
    /// 该覆盖只改变提交给渲染器的可见性输入，不修改权威 lighting/fog 状态；Play/Paused 必须关闭。
    /// </remarks>
    public void SetAuthoringVisibilityOverride(bool enabled)
    {
        _authoringVisibilityOverride = enabled;
    }

    /// <summary>
    /// 注册相位 9 render buffer 构建与相位 10 GPU 上传/窗口 present。
    /// </summary>
    public void RegisterPhases(EnginePhasePipeline phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        phases.Register(EnginePhase.BuildRenderBuffer, BuildFrame);
        phases.Register(EnginePhase.GpuUploadAndRender, PresentFrame);
    }

    // 相位 9：从 chunk/温度/相机构建 CPU render buffer，并按需 stamp 自由粒子。
    private void BuildFrame(EngineTickContext context)
    {
        CameraState camera = _camera.Current;
        ReadOnlySpan<Particle> activeParticles = _particles.ActiveReadOnly;
        bool forceRefreshForParticleErase = _sink.ParticleRenderMode == ParticleRenderMode.CpuStamp &&
            (_hadCpuParticleStamps || activeParticles.Length > 0);
        bool forceRefreshForCamera = !_hasBuiltCamera || CameraChanged(_lastBuiltCamera, camera);
        bool forceRefreshForWorld = _worldInvalidated;
        _worldInvalidated = false;
        bool forceRenderRefresh = forceRefreshForParticleErase || forceRefreshForCamera || forceRefreshForWorld;
        RenderFrameContext frame = new(
            _chunks,
            _materials,
            _temperature,
            camera,
            context.Timing.RunSim || forceRenderRefresh,
            CellDebugOverlaysEnabled() ? _debugOverlays : null,
            forceRebuild: forceRenderRefresh);
        _builder.Build(frame, _renderBuffer, _aux, context.Context.Profiler);
        if (_sink.ParticleRenderMode == ParticleRenderMode.CpuStamp)
        {
            _particleCompositor.Stamp(activeParticles, _materials, camera, _renderBuffer, _aux, context.Context.Profiler);
            _hadCpuParticleStamps = activeParticles.Length > 0;
        }
        else
        {
            _hadCpuParticleStamps = false;
        }

        CurrentCamera = camera;
        _lastBuiltCamera = camera;
        _hasBuiltCamera = true;
        _frameBuilt = true;
    }

    private static bool CameraChanged(in CameraState previous, in CameraState current)
    {
        return previous.ViewportWidth != current.ViewportWidth ||
            previous.ViewportHeight != current.ViewportHeight ||
            MathF.Abs(previous.OriginWorldX - current.OriginWorldX) > 0.0001f ||
            MathF.Abs(previous.OriginWorldY - current.OriginWorldY) > 0.0001f ||
            MathF.Abs(previous.CellsPerPixel - current.CellsPerPixel) > 0.0001f;
    }

    private bool CellDebugOverlaysEnabled()
    {
        return _debugOverlays?.HasCellColorOverlays == true;
    }

    // 相位 10：合成 overlay/光照后提交 GPU 上传与窗口 present。
    private void PresentFrame(EngineTickContext context)
    {
        if (!_frameBuilt)
        {
            return;
        }

        Span<PixelUploadRect> dirtyRects = [new PixelUploadRect(0, 0, _renderBuffer.Width, _renderBuffer.Height)];
        ReadOnlySpan<Particle> activeParticles = _particles.ActiveReadOnly;
        ReadOnlySpan<OverlayCommand> overlays = BuildOverlays(activeParticles);
        LastOverlayCount = overlays.Length;
        _sink.Render(
            _renderBuffer,
            _aux,
            CurrentCamera,
            dirtyRects,
            overlays,
            _lighting.PointLights,
            activeParticles,
            _materials,
            _authoringVisibilityOverride ? null : _lighting.FogOfWar,
            context.Context.Profiler);
        _frameBuilt = false;
    }

    private ReadOnlySpan<OverlayCommand> BuildOverlays(ReadOnlySpan<Particle> activeParticles)
    {
        if (_scriptOverlays is null && _debugOverlays is null && _worldVisualLayers is null)
        {
            return [];
        }

        _overlayCommands.Clear();
        AddWorldVisualLayers();
        AddScriptOverlays();
        if (_debugOverlays is not null)
        {
            int wakeCount = _kernel?.CopyBoundaryWakeSnapshots(_boundaryWakeBuffer) ?? 0;
            int caIterationCount = _kernel?.CopyCaIterationSnapshots(_caIterationBuffer) ?? 0;
            int componentCount = _physics?.CopyConnectedComponentDebugSnapshots(_connectedComponentBuffer) ?? 0;
            _ = _debugOverlays.BuildVectorOverlays(
                _chunks,
                CurrentCamera,
                _caIterationBuffer.AsSpan(0, caIterationCount),
                _boundaryWakeBuffer.AsSpan(0, wakeCount),
                activeParticles,
                _connectedComponentBuffer.AsSpan(0, componentCount),
                _overlayCommands);
        }

        return _overlayCommands.Count == 0 ? [] : CollectionsMarshal.AsSpan(_overlayCommands);
    }

    private void AddWorldVisualLayers()
    {
        if (_worldVisualLayers is null)
        {
            return;
        }

        int count = _worldVisualLayers.WorldVisualLayerCount;
        if (count is < 0 or > 128)
        {
            throw new InvalidOperationException("世界视觉层数量必须位于 0..128。");
        }

        for (int i = 0; i < count; i++)
        {
            AddWorldVisualLayer(_worldVisualLayers.GetWorldVisualLayer(i).Validate());
        }

        if (_worldVisualLayers is not IViewportWorldVisualLayerProvider viewportProvider)
        {
            return;
        }

        float cellsPerPixel = CurrentCamera.CellsPerPixel;
        int viewportCount = viewportProvider.CollectWorldVisualLayers(
            CurrentCamera.OriginWorldX,
            CurrentCamera.OriginWorldY,
            CurrentCamera.OriginWorldX + (CurrentCamera.ViewportWidth * cellsPerPixel),
            CurrentCamera.OriginWorldY + (CurrentCamera.ViewportHeight * cellsPerPixel),
            _viewportWorldVisualLayers);
        if ((uint)viewportCount > (uint)_viewportWorldVisualLayers.Length)
        {
            throw new InvalidOperationException("动态世界视觉层数量超过调用方容量。");
        }

        for (int i = 0; i < viewportCount; i++)
        {
            AddWorldVisualLayer(_viewportWorldVisualLayers[i].Validate());
        }
    }

    private void AddWorldVisualLayer(in WorldVisualLayerDescriptor layer)
    {
        if (!_sink.TryResolveWorldVisualSprite(layer.Asset, out OverlaySprite sprite))
        {
            return;
        }

        float inverseCellsPerPixel = 1f / CurrentCamera.CellsPerPixel;
        float viewportX = (layer.WorldX - CurrentCamera.OriginWorldX) * inverseCellsPerPixel;
        float viewportY = (layer.WorldY - CurrentCamera.OriginWorldY) * inverseCellsPerPixel;
        float viewportWidth = layer.WidthCells * inverseCellsPerPixel;
        float viewportHeight = layer.HeightCells * inverseCellsPerPixel;
        if (viewportX >= CurrentCamera.ViewportWidth || viewportY >= CurrentCamera.ViewportHeight ||
            viewportX + viewportWidth <= 0f || viewportY + viewportHeight <= 0f)
        {
            return;
        }

        OverlayCompositionLayer compositionLayer = layer.Layer switch
        {
            WorldVisualLayerKind.Background => OverlayCompositionLayer.Background,
            WorldVisualLayerKind.Decoration => OverlayCompositionLayer.WorldDecoration,
            _ => throw new ArgumentOutOfRangeException(nameof(layer), layer.Layer, "未知世界视觉组合层。"),
        };
        _overlayCommands.Add(OverlayCommand.SpriteRectangle(
            viewportX,
            viewportY,
            viewportWidth,
            viewportHeight,
            sprite,
            layer.TintBgra,
            compositionLayer));
    }

    private void AddScriptOverlays()
    {
        if (_scriptOverlays is null)
        {
            return;
        }

        for (int i = 0; i < _scriptOverlays.CommandCount; i++)
        {
            ScriptOverlayCommand command = _scriptOverlays.GetCommand(i);
            _overlayCommands.Add(command.Primitive switch
            {
                ScriptOverlayPrimitive.SolidRectangle => OverlayCommand.SolidRectangle(
                    command.X,
                    command.Y,
                    command.Width,
                    command.Height,
                    command.ColorBgra),
                ScriptOverlayPrimitive.OutlineRectangle => OverlayCommand.OutlineRectangle(
                    command.X,
                    command.Y,
                    command.Width,
                    command.Height,
                    command.Thickness,
                    command.ColorBgra),
                ScriptOverlayPrimitive.Line => OverlayCommand.Line(
                    command.X,
                    command.Y,
                    command.EndX,
                    command.EndY,
                    command.Thickness,
                    command.ColorBgra),
                _ => throw new ArgumentOutOfRangeException(nameof(command), command.Primitive, "未知脚本 overlay 原语。"),
            });
        }

        _scriptOverlays.Clear();
    }
}
