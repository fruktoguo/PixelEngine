using PixelEngine.Editor;
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
    PixelEngine.Core.Threading.JobSystem? jobs = null,
    IMaterialTextureProvider? textures = null,
    SimulationKernel? kernel = null,
    PhysicsSystem? physics = null,
    ScriptOverlayApi? scriptOverlays = null,
    DebugOverlayController? debugOverlays = null) : IEnginePhaseDriver
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
    private readonly RenderBufferBuilder _builder = new(jobs, textures);
    private readonly ParticleCompositor _particleCompositor = new(textures);
    private readonly RenderBuffer _renderBuffer = new(1, 1);
    private readonly RenderAuxBuffers _aux = new(1, 1);
    private readonly List<OverlayCommand> _overlayCommands = new(256);
    private readonly BoundaryWakeSnapshot[] _boundaryWakeBuffer = new BoundaryWakeSnapshot[256];
    private readonly CaIterationSnapshot[] _caIterationBuffer = new CaIterationSnapshot[256];
    private readonly ConnectedComponentDebugSnapshot[] _connectedComponentBuffer = new ConnectedComponentDebugSnapshot[256];
    private CameraState _presentCamera = CameraState.OneToOne(0, 0, 1, 1);
    private CameraState _lastBuiltCamera = CameraState.OneToOne(0, 0, 1, 1);
    private bool _frameBuilt;
    private bool _hasBuiltCamera;
    private bool _hadCpuParticleStamps;

    /// <summary>
    /// 最近一次相位 10 实际提交给 Rendering 的 overlay 命令数量，用于 Demo/测试诊断脚本可见层是否进入渲染链路。
    /// </summary>
    public int LastOverlayCount { get; private set; }

    /// <summary>
    /// 注册相位 9 render buffer 构建与相位 10 GPU 上传/窗口 present。
    /// </summary>
    public void RegisterPhases(EnginePhasePipeline phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        phases.Register(EnginePhase.BuildRenderBuffer, BuildFrame);
        phases.Register(EnginePhase.GpuUploadAndRender, PresentFrame);
    }

    private void BuildFrame(EngineTickContext context)
    {
        CameraState camera = _camera.Current;
        ReadOnlySpan<Particle> activeParticles = _particles.ActiveReadOnly;
        bool forceRefreshForParticleErase = _sink.ParticleRenderMode == ParticleRenderMode.CpuStamp &&
            (_hadCpuParticleStamps || activeParticles.Length > 0);
        bool forceRefreshForCamera = !_hasBuiltCamera || CameraChanged(_lastBuiltCamera, camera);
        RenderFrameContext frame = new(
            _chunks,
            _materials,
            _temperature,
            camera,
            context.Timing.RunSim || forceRefreshForParticleErase || forceRefreshForCamera,
            CellDebugOverlaysEnabled() ? _debugOverlays : null);
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

        _presentCamera = camera;
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
            _presentCamera,
            dirtyRects,
            overlays,
            _lighting.PointLights,
            activeParticles,
            _materials,
            _lighting.FogOfWar,
            context.Context.Profiler);
        _frameBuilt = false;
    }

    private ReadOnlySpan<OverlayCommand> BuildOverlays(ReadOnlySpan<Particle> activeParticles)
    {
        if (_scriptOverlays is null && _debugOverlays is null)
        {
            return [];
        }

        _overlayCommands.Clear();
        AddScriptOverlays();
        if (_debugOverlays is not null)
        {
            int wakeCount = _kernel?.CopyBoundaryWakeSnapshots(_boundaryWakeBuffer) ?? 0;
            int caIterationCount = _kernel?.CopyCaIterationSnapshots(_caIterationBuffer) ?? 0;
            int componentCount = _physics?.CopyConnectedComponentDebugSnapshots(_connectedComponentBuffer) ?? 0;
            _ = _debugOverlays.BuildVectorOverlays(
                _chunks,
                _presentCamera,
                _caIterationBuffer.AsSpan(0, caIterationCount),
                _boundaryWakeBuffer.AsSpan(0, wakeCount),
                activeParticles,
                _connectedComponentBuffer.AsSpan(0, componentCount),
                _overlayCommands);
        }

        return _overlayCommands.Count == 0 ? [] : CollectionsMarshal.AsSpan(_overlayCommands);
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
