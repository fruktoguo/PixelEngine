using PixelEngine.Rendering;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;

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
    IMaterialTextureProvider? textures = null) : IEnginePhaseDriver
{
    private readonly IChunkSource _chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
    private readonly MaterialTable _materials = materials ?? throw new ArgumentNullException(nameof(materials));
    private readonly TemperatureField _temperature = temperature ?? throw new ArgumentNullException(nameof(temperature));
    private readonly ParticleSystem _particles = particles ?? throw new ArgumentNullException(nameof(particles));
    private readonly ScriptCameraSynchronizer _camera = camera ?? throw new ArgumentNullException(nameof(camera));
    private readonly ScriptLightingSynchronizer _lighting = lighting ?? throw new ArgumentNullException(nameof(lighting));
    private readonly IRenderFrameSink _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    private readonly RenderBufferBuilder _builder = new(jobs, textures);
    private readonly ParticleCompositor _particleCompositor = new(textures);
    private readonly RenderBuffer _renderBuffer = new(1, 1);
    private readonly RenderAuxBuffers _aux = new(1, 1);
    private CameraState _presentCamera = CameraState.OneToOne(0, 0, 1, 1);
    private bool _frameBuilt;

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
        RenderFrameContext frame = new(_chunks, _materials, _temperature, camera, context.Timing.RunSim);
        _builder.Build(frame, _renderBuffer, _aux, context.Context.Profiler);
        ReadOnlySpan<Particle> activeParticles = _particles.ActiveReadOnly;
        _particleCompositor.Stamp(activeParticles, _materials, camera, _renderBuffer, _aux, context.Context.Profiler);
        _presentCamera = camera;
        _frameBuilt = true;
    }

    private void PresentFrame(EngineTickContext context)
    {
        if (!_frameBuilt)
        {
            return;
        }

        Span<PixelUploadRect> dirtyRects = [new PixelUploadRect(0, 0, _renderBuffer.Width, _renderBuffer.Height)];
        ReadOnlySpan<Particle> activeParticles = _particles.ActiveReadOnly;
        _sink.Render(
            _renderBuffer,
            _aux,
            _presentCamera,
            dirtyRects,
            [],
            activeParticles,
            _materials,
            _lighting.FogOfWar,
            context.Context.Profiler);
        _frameBuilt = false;
    }
}
