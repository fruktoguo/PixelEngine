using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using PixelEngine.Scripting;
using PixelEngine.World;

namespace PixelEngine.Hosting;

/// <summary>
/// 将 Simulation 与 Particles 的真实低层入口绑定到 Hosting 相位管线。
/// </summary>
/// <param name="chunks">当前权威驻留 chunk 集合。</param>
/// <param name="grid">世界 cell 访问门面。</param>
/// <param name="kernel">CA 模拟内核。</param>
/// <param name="particles">自由粒子系统。</param>
/// <param name="temperature">温度场。</param>
/// <param name="materials">材质注册表。</param>
/// <param name="scriptContext">可选脚本 Simulation 上下文；提供时在相位安全窗口 flush 脚本命令。</param>
public sealed class SimulationPhaseDriver(
    IChunkSource chunks,
    CellGrid grid,
    SimulationKernel kernel,
    ParticleSystem particles,
    TemperatureField temperature,
    MaterialTable materials,
    ScriptSimulationContext? scriptContext = null) : IEnginePhaseDriver
{
    private const int DistantThrottleFullRatePaddingChunks = 1;

    private readonly IChunkSource _chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
    private ScriptSimulationContext? _scriptContext = scriptContext;

    /// <summary>
    /// 世界 cell 访问门面。
    /// </summary>
    public CellGrid Grid { get; } = grid ?? throw new ArgumentNullException(nameof(grid));

    /// <summary>
    /// CA 模拟内核。
    /// </summary>
    public SimulationKernel Kernel { get; } = kernel ?? throw new ArgumentNullException(nameof(kernel));

    /// <summary>
    /// 自由粒子系统。
    /// </summary>
    public ParticleSystem Particles { get; } = particles ?? throw new ArgumentNullException(nameof(particles));

    /// <summary>
    /// 温度场。
    /// </summary>
    public TemperatureField Temperature { get; } = temperature ?? throw new ArgumentNullException(nameof(temperature));

    /// <summary>
    /// 材质注册表。
    /// </summary>
    public MaterialTable Materials { get; } = materials ?? throw new ArgumentNullException(nameof(materials));

    /// <summary>
    /// 绑定脚本 Simulation 上下文，使脚本命令在对应 Simulation 相位安全落地。
    /// </summary>
    /// <param name="scriptContext">脚本 Simulation 上下文。</param>
    public void AttachScriptContext(ScriptSimulationContext scriptContext)
    {
        _scriptContext = scriptContext ?? throw new ArgumentNullException(nameof(scriptContext));
    }

    /// <summary>
    /// 注册相位 3、4、5、6、7 的真实 Simulation 入口。
    /// </summary>
    /// <param name="phases">目标 12 相位管线。</param>
    public void RegisterPhases(EnginePhasePipeline phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        phases.Register(EnginePhase.ParticleToCell, RunParticleToCell);
        phases.Register(EnginePhase.CaSimulation, RunCaSimulation);
        phases.Register(EnginePhase.Temperature, RunTemperature);
        phases.Register(EnginePhase.DirtyRectSwap, RunDirtyRectSwap);
        phases.Register(EnginePhase.CellToParticle, RunCellToParticle);
    }

    private void RunParticleToCell(EngineTickContext context)
    {
        Particles.ResetTickStats();
        _ = _scriptContext?.FlushCellCommands();
        Particles.IntegrateAndAdvance(context.Context.Jobs, Grid);
        Particles.ResolveDeposits(Kernel, Grid);
        Particles.PublishDiagnostics(context.Context.Counters);
    }

    private void RunCaSimulation(EngineTickContext context)
    {
        Kernel.StepCa(context.Context.Jobs, BuildCaThrottlePolicy(context.Context));
    }

    private void RunTemperature(EngineTickContext context)
    {
        if (!Temperature.ShouldRun(Kernel.FrameIndex))
        {
            return;
        }

        Temperature.ConductStep(_chunks, Materials.Hot, context.Context.Jobs, Kernel.FrameIndex, unchecked((uint)Kernel.WorldSeed));
        Temperature.ApplyPhaseTransitions(_chunks, Materials, Kernel.CurrentParity);
    }

    private void RunDirtyRectSwap(EngineTickContext context)
    {
        Kernel.SwapDirtyRects();
    }

    private void RunCellToParticle(EngineTickContext context)
    {
        _ = _scriptContext?.FlushParticleCommands();
        Particles.RunEjectionPass(Kernel, Grid);
        Particles.PublishDiagnostics(context.Context.Counters);
    }

    private static CaChunkThrottlePolicy BuildCaThrottlePolicy(EngineContext context)
    {
        if (context.QualityTier < EngineQualityTier.DistantChunkThrottle ||
            !context.TryGetService(out WorldManager world))
        {
            return CaChunkThrottlePolicy.Disabled;
        }

        ChunkRect fullRate = world.ComputeVisibleChunks().Expand(DistantThrottleFullRatePaddingChunks);
        return fullRate.IsEmpty
            ? CaChunkThrottlePolicy.Disabled
            : new CaChunkThrottlePolicy(
            Enabled: true,
            FullRateMinCx: fullRate.MinCx,
            FullRateMinCy: fullRate.MinCy,
            FullRateMaxCx: fullRate.MaxCx,
            FullRateMaxCy: fullRate.MaxCy,
            FrameIndex: 0);
    }
}
