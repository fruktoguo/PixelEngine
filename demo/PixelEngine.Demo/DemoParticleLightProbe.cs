using PixelEngine.Hosting;
using PixelEngine.Rendering;
using PixelEngine.Scripting;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 窗口粒子 / 光照长跑探针，验证短寿命粒子退场与光照请求同步。
/// </summary>
internal sealed class DemoParticleLightProbe(
    ParticleSystem particles,
    MaterialTable materials,
    ScriptLightingApi lighting,
    ScriptLightingSynchronizer lightingSync,
    ScriptCameraSynchronizer cameraSync) : IEnginePhaseDriver
{
    private const int BurstCount = 96;
    private const int TailStartFrame = 80;
    private const float CenterX = 320f;
    private const float CenterY = 180f;

    private readonly ParticleSystem _particles = particles ?? throw new ArgumentNullException(nameof(particles));
    private readonly MaterialTable _materials = materials ?? throw new ArgumentNullException(nameof(materials));
    private readonly ScriptLightingApi _lighting = lighting ?? throw new ArgumentNullException(nameof(lighting));
    private readonly ScriptLightingSynchronizer _lightingSync = lightingSync ?? throw new ArgumentNullException(nameof(lightingSync));
    private readonly ScriptCameraSynchronizer _cameraSync = cameraSync ?? throw new ArgumentNullException(nameof(cameraSync));
    private ushort _fire;
    private int _frames;

    /// <summary>
    /// 探针是否已解析 fire 材质。
    /// </summary>
    public bool Initialized { get; private set; }

    /// <summary>
    /// 首帧成功生成的粒子数量。
    /// </summary>
    public int Spawned { get; private set; }

    /// <summary>
    /// 采样到的最大活跃粒子数。
    /// </summary>
    public int MaxActive { get; private set; }

    /// <summary>
    /// 长跑尾段采样到的最大活跃粒子数。
    /// </summary>
    public int TailMaxActive { get; private set; }

    /// <summary>
    /// 最后一帧采样到的活跃粒子数。
    /// </summary>
    public int LastActive { get; private set; }

    /// <summary>
    /// 是否观测到粒子因 lifetime 到期被释放。
    /// </summary>
    public bool LifetimeKillObserved { get; private set; }

    /// <summary>
    /// 是否观测到点光同步进 Rendering 光源快照。
    /// </summary>
    public bool LightObserved { get; private set; }

    /// <summary>
    /// fog-of-war 在探针中心的最大 reveal alpha。
    /// </summary>
    public byte MaxFogAlpha { get; private set; }

    /// <summary>
    /// 短寿命粒子是否在尾段完全退场。
    /// </summary>
    public bool Depleted => Spawned == BurstCount &&
        MaxActive > 0 &&
        TailMaxActive == 0 &&
        LastActive == 0 &&
        LifetimeKillObserved;

    /// <summary>
    /// 光照请求是否同步到点光与 fog。
    /// </summary>
    public bool LightingSynced => LightObserved && MaxFogAlpha > 0;

    /// <inheritdoc />
    public void RegisterPhases(EnginePhasePipeline phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        phases.Register(EnginePhase.GameLogicAndScripts, EmitOnce);
        phases.Register(EnginePhase.BuildRenderBuffer, Capture);
    }

    private void EmitOnce(EngineTickContext context)
    {
        _ = context;
        if (!Initialized)
        {
            Initialized = _materials.TryGetId("fire", out _fire);
        }

        if (!Initialized || _frames++ > 0)
        {
            return;
        }

        for (int i = 0; i < BurstCount; i++)
        {
            float angle = MathF.Tau * i / BurstCount;
            float speed = 0.35f + (i % 7 * 0.03f);
            ParticleSpawn spawn = new(
                CenterX,
                CenterY,
                MathF.Cos(angle) * speed,
                MathF.Sin(angle) * speed,
                _fire,
                ColorVariant: 0,
                Life: 24);
            if (_particles.TrySpawn(in spawn))
            {
                Spawned++;
            }
        }

        _lighting.AddPointLight(CenterX, CenterY, 72f, 0xFF_30_90_FF, 1.2f);
        _lighting.RevealAround(CenterX, CenterY, 64f, 220);
    }

    private void Capture(EngineTickContext context)
    {
        int active = _particles.ActiveCount;
        MaxActive = Math.Max(MaxActive, active);
        LastActive = active;
        if (_frames >= TailStartFrame)
        {
            TailMaxActive = Math.Max(TailMaxActive, active);
        }

        LifetimeKillObserved |= context.Context.Counters.FreeParticlesKilledThisTick > 0;
        LightObserved |= _lightingSync.PointLights.Length > 0;
        CameraState camera = _cameraSync.Current;
        int fogX = (int)MathF.Round((CenterX - camera.OriginWorldX) / camera.CellsPerPixel);
        int fogY = (int)MathF.Round((CenterY - camera.OriginWorldY) / camera.CellsPerPixel);
        byte alpha = _lightingSync.FogOfWar.RevealAlpha(fogX, fogY);
        if (alpha > MaxFogAlpha)
        {
            MaxFogAlpha = alpha;
        }
    }
}
