using Hexa.NET.ImGui;
using PixelEngine.Physics;
using PixelEngine.Rendering;
using PixelEngine.Simulation.Particles;

namespace PixelEngine.Editor;

/// <summary>
/// 物理调参快照。
/// </summary>
public sealed record PhysicsTuningState(
    float PixelsPerMeter,
    int SubStepCount,
    int WorkerCount,
    int FragmentPixelThreshold,
    int RebuildThrottleTicks,
    float GravityX,
    float GravityY,
    PhysicsSystemStats Stats);

/// <summary>
/// 粒子调参快照。
/// </summary>
public sealed record ParticleTuningState(
    int MaxCount,
    float GravityPerTick,
    int MaxLifetimeTicks,
    float DepositSpeedEpsilon,
    float EjectionImpulseScale,
    int MaxEjectionPerTick,
    ParticleSystemStats Stats);

/// <summary>
/// 光照调参快照。
/// </summary>
public sealed record LightingTuningState(
    LightingQualityLevel QualityLevel,
    bool BloomEnabled,
    float BloomThreshold,
    float BloomIntensity,
    bool FogOfWarEnabled,
    bool DitherEnabled,
    float Gamma,
    bool RadianceCascadesEnabled);

/// <summary>
/// 物理调参服务。
/// </summary>
public interface IPhysicsTuningService
{
    /// <summary>
    /// 捕获当前物理调参和统计。
    /// </summary>
    PhysicsTuningState Capture();

    /// <summary>
    /// 应用物理调参。
    /// </summary>
    void Apply(PhysicsTuningState state);
}

/// <summary>
/// 粒子调参服务。
/// </summary>
public interface IParticleTuningService
{
    /// <summary>
    /// 捕获当前粒子调参和泄漏统计。
    /// </summary>
    ParticleTuningState Capture();

    /// <summary>
    /// 应用粒子调参。
    /// </summary>
    void Apply(ParticleTuningState state);
}

/// <summary>
/// 光照调参服务。
/// </summary>
public interface ILightingTuningService
{
    /// <summary>
    /// 捕获当前光照调参。
    /// </summary>
    LightingTuningState Capture();

    /// <summary>
    /// 应用光照调参。
    /// </summary>
    void Apply(LightingTuningState state);
}

/// <summary>
/// 直接作用于 PhysicsSystem 的物理调参服务。
/// </summary>
/// <param name="physics">物理系统 facade。</param>
public sealed class PhysicsSystemTuningService(PhysicsSystem physics) : IPhysicsTuningService
{
    private readonly PhysicsSystem _physics = physics ?? throw new ArgumentNullException(nameof(physics));

    /// <inheritdoc />
    public PhysicsTuningState Capture()
    {
        System.Numerics.Vector2 gravity = _physics.Gravity;
        return new PhysicsTuningState(
            PhysicsScale.PixelsPerMeter,
            _physics.SubStepCount,
            _physics.TaskBridgeWorkerCount,
            _physics.FragmentPixelThreshold,
            RebuildThrottleTicks: 0,
            gravity.X,
            gravity.Y,
            _physics.Stats);
    }

    /// <inheritdoc />
    public void Apply(PhysicsTuningState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _physics.SetSubStepCount(state.SubStepCount);
        _physics.SetGravity(new System.Numerics.Vector2(state.GravityX, state.GravityY));
        if (state.FragmentPixelThreshold != _physics.FragmentPixelThreshold)
        {
            _physics.SetFragmentPixelThreshold(state.FragmentPixelThreshold);
        }
    }
}

/// <summary>
/// 直接作用于 ParticleSystem 的粒子调参服务。
/// </summary>
/// <param name="particles">自由粒子系统。</param>
public sealed class ParticleSystemTuningService(ParticleSystem particles) : IParticleTuningService
{
    private readonly ParticleSystem _particles = particles ?? throw new ArgumentNullException(nameof(particles));

    /// <inheritdoc />
    public ParticleTuningState Capture()
    {
        ParticleSystemSettings settings = _particles.Settings;
        return new ParticleTuningState(
            settings.MaxActiveCount,
            settings.GravityPerTick,
            settings.MaxLifetimeTicks,
            settings.DepositSpeedEpsilon,
            settings.EjectionImpulseScale,
            settings.MaxEjectionPerTick,
            _particles.Stats);
    }

    /// <inheritdoc />
    public void Apply(ParticleTuningState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _particles.ApplySettings(new ParticleSystemSettings(
            state.MaxCount,
            state.GravityPerTick,
            state.MaxLifetimeTicks,
            state.DepositSpeedEpsilon,
            state.EjectionImpulseScale,
            state.MaxEjectionPerTick));
    }
}

/// <summary>
/// 直接作用于 RenderPipelineSettings 的光照调参服务。
/// </summary>
/// <param name="settings">渲染管线设置对象。</param>
public sealed class RenderPipelineLightingTuningService(RenderPipelineSettings settings) : ILightingTuningService
{
    private readonly RenderPipelineSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    /// <inheritdoc />
    public LightingTuningState Capture()
    {
        BloomSettings bloom = _settings.Bloom.Normalize();
        return new LightingTuningState(
            _settings.QualityLevel,
            bloom.Intensity > 0f,
            bloom.Threshold,
            bloom.Intensity,
            _settings.QualityLevel != LightingQualityLevel.FogOfWarEmissiveOnly,
            _settings.EnableDither,
            _settings.Gamma,
            _settings.RadianceCascades.Enabled);
    }

    /// <inheritdoc />
    public void Apply(LightingTuningState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _settings.QualityLevel = state.QualityLevel;
        BloomSettings bloom = _settings.Bloom with
        {
            Threshold = state.BloomThreshold,
            Intensity = state.BloomEnabled ? state.BloomIntensity : 0f,
        };
        _settings.Bloom = bloom.Normalize();
        _settings.EnableDither = state.DitherEnabled;
        _settings.Gamma = state.Gamma;
        _settings.RadianceCascades = _settings.RadianceCascades with { Enabled = state.RadianceCascadesEnabled };
        _settings.Validate();
    }
}

/// <summary>
/// 物理调参面板。
/// </summary>
/// <param name="service">物理调参服务。</param>
public sealed class PhysicsTuningPanel(IPhysicsTuningService service) : TuningPanelBase<PhysicsTuningState>(EditorDockSpace.PhysicsTuningWindowTitle)
{
    private readonly IPhysicsTuningService _service = service ?? throw new ArgumentNullException(nameof(service));

    /// <inheritdoc />
    protected override PhysicsTuningState Capture()
    {
        return _service.Capture();
    }

    /// <inheritdoc />
    protected override void Apply(PhysicsTuningState state)
    {
        _service.Apply(state);
    }

    /// <inheritdoc />
    protected override PhysicsTuningState DrawState(PhysicsTuningState state)
    {
        float ppm = state.PixelsPerMeter;
        int subSteps = state.SubStepCount;
        int fragment = state.FragmentPixelThreshold;
        float gx = state.GravityX;
        float gy = state.GravityY;
        ImGui.TextUnformatted($"pixels/meter={ppm:F0} workers={state.WorkerCount} rebuild throttle={state.RebuildThrottleTicks}");
        _ = ImGui.InputInt("subStep", ref subSteps);
        _ = ImGui.InputInt("fragment px", ref fragment);
        _ = ImGui.InputFloat("gravity x", ref gx);
        _ = ImGui.InputFloat("gravity y", ref gy);
        ImGui.TextUnformatted($"bodies={state.Stats.ActiveBodyCount} damage={state.Stats.PendingDamageCount} erased={state.Stats.LastErasedCellCount} stamped={state.Stats.LastStampedCellCount}");
        return state with
        {
            PixelsPerMeter = ppm,
            SubStepCount = Math.Max(1, subSteps),
            FragmentPixelThreshold = Math.Max(0, fragment),
            GravityX = gx,
            GravityY = gy,
        };
    }
}

/// <summary>
/// 粒子调参面板。
/// </summary>
/// <param name="service">粒子调参服务。</param>
public sealed class ParticleTuningPanel(IParticleTuningService service) : TuningPanelBase<ParticleTuningState>(EditorDockSpace.ParticleTuningWindowTitle)
{
    private readonly IParticleTuningService _service = service ?? throw new ArgumentNullException(nameof(service));

    /// <inheritdoc />
    protected override ParticleTuningState Capture()
    {
        return _service.Capture();
    }

    /// <inheritdoc />
    protected override void Apply(ParticleTuningState state)
    {
        _service.Apply(state);
    }

    /// <inheritdoc />
    protected override ParticleTuningState DrawState(ParticleTuningState state)
    {
        int maxCount = state.MaxCount;
        float gravity = state.GravityPerTick;
        int lifetime = state.MaxLifetimeTicks;
        float deposit = state.DepositSpeedEpsilon;
        float impulseScale = state.EjectionImpulseScale;
        int ejectLimit = state.MaxEjectionPerTick;
        _ = ImGui.InputInt("max count", ref maxCount);
        _ = ImGui.InputFloat("gravity/tick", ref gravity);
        _ = ImGui.InputInt("max lifetime", ref lifetime);
        _ = ImGui.InputFloat("deposit epsilon", ref deposit);
        _ = ImGui.InputFloat("ejection impulse scale", ref impulseScale);
        _ = ImGui.InputInt("eject/tick", ref ejectLimit);
        ImGui.TextUnformatted($"active={state.Stats.ActiveCount}/{state.Stats.Capacity} spawned={state.Stats.SpawnedThisTick} deposited={state.Stats.DepositedThisTick} dropped={state.Stats.DroppedThisTick}");
        return state with
        {
            MaxCount = Math.Max(1, maxCount),
            GravityPerTick = gravity,
            MaxLifetimeTicks = Math.Max(1, lifetime),
            DepositSpeedEpsilon = Math.Max(0f, deposit),
            EjectionImpulseScale = Math.Max(0f, impulseScale),
            MaxEjectionPerTick = Math.Max(0, ejectLimit),
        };
    }
}

/// <summary>
/// 光照调参面板。
/// </summary>
/// <param name="service">光照调参服务。</param>
public sealed class LightingTuningPanel(ILightingTuningService service) : TuningPanelBase<LightingTuningState>(EditorDockSpace.LightingTuningWindowTitle)
{
    private readonly ILightingTuningService _service = service ?? throw new ArgumentNullException(nameof(service));

    /// <inheritdoc />
    protected override LightingTuningState Capture()
    {
        return _service.Capture();
    }

    /// <inheritdoc />
    protected override void Apply(LightingTuningState state)
    {
        _service.Apply(state);
    }

    /// <inheritdoc />
    protected override LightingTuningState DrawState(LightingTuningState state)
    {
        bool bloom = state.BloomEnabled;
        bool fog = state.FogOfWarEnabled;
        bool dither = state.DitherEnabled;
        bool rc = state.RadianceCascadesEnabled;
        float threshold = state.BloomThreshold;
        float intensity = state.BloomIntensity;
        float gamma = state.Gamma;
        _ = ImGui.Checkbox("bloom", ref bloom);
        _ = ImGui.InputFloat("bloom threshold", ref threshold);
        _ = ImGui.InputFloat("bloom intensity", ref intensity);
        _ = ImGui.Checkbox("fog of war", ref fog);
        _ = ImGui.Checkbox("dither", ref dither);
        _ = ImGui.InputFloat("gamma", ref gamma);
        _ = ImGui.Checkbox("Radiance Cascades", ref rc);
        return state with
        {
            BloomEnabled = bloom,
            BloomThreshold = threshold,
            BloomIntensity = Math.Max(0f, intensity),
            FogOfWarEnabled = fog,
            DitherEnabled = dither,
            Gamma = Math.Max(0.01f, gamma),
            RadianceCascadesEnabled = rc,
        };
    }
}

/// <summary>
/// 调参面板公共基类。
/// </summary>
public abstract class TuningPanelBase<TState>(string title) : IEditorPanel
    where TState : class
{
    /// <inheritdoc />
    public string Title { get; } = title;

    /// <inheritdoc />
    public bool Visible { get; set; } = true;

    /// <summary>
    /// 最近一次调参快照。
    /// </summary>
    public TState? LastState { get; private set; }

    /// <inheritdoc />
    public void Draw(in EditorContext context)
    {
        bool visible = Visible;
        if (!ImGui.Begin(Title, ref visible))
        {
            Visible = visible;
            ImGui.End();
            return;
        }

        Visible = visible;
        TState edited = DrawState(Capture());
        if (ImGui.Button("应用"))
        {
            ApplyNow(edited);
        }

        ImGui.End();
    }

    /// <summary>
    /// 立即应用调参状态。
    /// </summary>
    public void ApplyNow(TState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Apply(state);
        LastState = Capture();
    }

    /// <summary>
    /// 捕获当前调参状态。
    /// </summary>
    protected abstract TState Capture();

    /// <summary>
    /// 应用调参状态。
    /// </summary>
    protected abstract void Apply(TState state);

    /// <summary>
    /// 绘制并返回编辑后的状态。
    /// </summary>
    protected abstract TState DrawState(TState state);
}
