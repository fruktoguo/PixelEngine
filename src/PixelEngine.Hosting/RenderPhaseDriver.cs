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

    /// <summary>
    /// 注册相位 9 render buffer 构建与 present。
    /// </summary>
    public void RegisterPhases(EnginePhasePipeline phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        phases.Register(EnginePhase.BuildRenderBuffer, RenderFrame);
    }

    private void RenderFrame(EngineTickContext context)
    {
        CameraState camera = _camera.Current;
        RenderFrameContext frame = new(_chunks, _materials, _temperature, camera, context.Timing.RunSim);
        _builder.Build(frame, _renderBuffer, _aux, context.Context.Profiler);
        ReadOnlySpan<Particle> activeParticles = _particles.ActiveReadOnly;
        _particleCompositor.Stamp(activeParticles, _materials, camera, _renderBuffer, _aux, context.Context.Profiler);
        Span<PixelUploadRect> dirtyRects = [new PixelUploadRect(0, 0, _renderBuffer.Width, _renderBuffer.Height)];
        _sink.Render(
            _renderBuffer,
            _aux,
            camera,
            dirtyRects,
            [],
            activeParticles,
            _materials,
            _lighting.FogOfWar,
            context.Context.Profiler);
    }
}
