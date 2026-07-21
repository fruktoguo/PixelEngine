using PixelEngine.Physics;
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
/// <param name="topologyChanges">与 Kernel/Temperature 共用的固体拓扑变化累加器。</param>
public sealed class SimulationPhaseDriver(
    IChunkSource chunks,
    CellGrid grid,
    SimulationKernel kernel,
    ParticleSystem particles,
    TemperatureField temperature,
    MaterialTable materials,
    ScriptSimulationContext? scriptContext = null,
    CellTopologyChangeAccumulator? topologyChanges = null) : IEnginePhaseDriver
{
    private const int DistantThrottleFullRatePaddingChunks = 1;

    private readonly IChunkSource _chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
    private ScriptSimulationContext? _scriptContext = scriptContext;
    private WorldMutationEvent _pendingTopologyMutation;
    private bool _hasPendingTopologyMutation;

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
    /// 并行 Simulation 写入与脚本事件之间的固体拓扑区域累加器。
    /// </summary>
    public CellTopologyChangeAccumulator TopologyChanges { get; } = topologyChanges ?? new CellTopologyChangeAccumulator();

    /// <summary>
    /// 绑定脚本 Simulation 上下文，使脚本命令在对应 Simulation 相位安全落地。
    /// </summary>
    /// <param name="scriptContext">脚本 Simulation 上下文。</param>
    public void AttachScriptContext(ScriptSimulationContext scriptContext)
    {
        ArgumentNullException.ThrowIfNull(scriptContext);
        // 程序化世界 Populate 可能早于脚本装配；订阅建立前的初始填充不是 gameplay 拓扑事件。
        _ = TopologyChanges.TryDrain(out _);
        _pendingTopologyMutation = default;
        _hasPendingTopologyMutation = false;
        _scriptContext = scriptContext;
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

    // 相位 3：自由粒子积分、沉积写回 cell，并在脚本安全窗口 flush cell 命令。
    private void RunParticleToCell(EngineTickContext context)
    {
        Particles.ResetTickStats();
        _ = _scriptContext?.FlushCellCommands();
        Particles.IntegrateAndAdvance(context.Context.Jobs, Grid);
        Particles.ResolveDeposits(Kernel, Grid);
        Particles.PublishDiagnostics(context.Context.Counters);
    }

    // 相位 4：CA checkerboard 主步进；过载时可按可见区对远距 chunk 降频。
    private void RunCaSimulation(EngineTickContext context)
    {
        Kernel.StepCa(context.Context.Jobs, BuildCaThrottlePolicy(context.Context));
    }

    // 相位 5：热传导与相变；步进间隔由过载策略通过 TemperatureField.SetStepInterval 控制。
    private void RunTemperature(EngineTickContext context)
    {
        if (Temperature.ShouldRun(Kernel.FrameIndex))
        {
            Temperature.ConductStep(_chunks, Materials.Hot, context.Context.Jobs, Kernel.FrameIndex, unchecked((uint)Kernel.WorldSeed));
            IRigidDamageSink? rigidDamageSink = context.Context.TryGetService(out RigidDamageQueue queue) ? queue : null;
            Temperature.ApplyPhaseTransitions(_chunks, Materials, Kernel.CurrentParity, rigidDamageSink, TopologyChanges);
        }

        PublishTopologyMutation();
    }

    private void PublishTopologyMutation()
    {
        if (TopologyChanges.TryDrain(out CellTopologyChangeRegion region))
        {
            WorldMutationEvent mutation = new(
                region.MinX,
                region.MinY,
                ToExclusive(region.MaxX),
                ToExclusive(region.MaxY),
                WorldMutationKind.SolidTopology);
            _pendingTopologyMutation = _hasPendingTopologyMutation
                ? Merge(_pendingTopologyMutation, mutation)
                : mutation;
            _hasPendingTopologyMutation = true;
        }

        if (!_hasPendingTopologyMutation)
        {
            return;
        }

        ScriptSimulationContext? scripts = _scriptContext;
        if (scripts is null)
        {
            _pendingTopologyMutation = default;
            _hasPendingTopologyMutation = false;
            return;
        }

        if (scripts.Events.TryPublish(in _pendingTopologyMutation))
        {
            _pendingTopologyMutation = default;
            _hasPendingTopologyMutation = false;
        }
    }

    private static int ToExclusive(int inclusive)
    {
        return inclusive == int.MaxValue ? int.MaxValue : inclusive + 1;
    }

    private static WorldMutationEvent Merge(in WorldMutationEvent left, in WorldMutationEvent right)
    {
        return new WorldMutationEvent(
            Math.Min(left.MinX, right.MinX),
            Math.Min(left.MinY, right.MinY),
            Math.Max(left.MaxXExclusive, right.MaxXExclusive),
            Math.Max(left.MaxYExclusive, right.MaxYExclusive),
            left.Kinds | right.Kinds);
    }

    // 相位 6：交换 dirty rectangle 双缓冲，为下一 tick 的 CA 读写分离做准备。
    private void RunDirtyRectSwap(EngineTickContext context)
    {
        Kernel.SwapDirtyRects();
    }

    // 相位 7：cell 抛射为自由粒子，并 flush 脚本粒子命令。
    private void RunCellToParticle(EngineTickContext context)
    {
        _ = _scriptContext?.FlushParticleCommands();
        Particles.RunEjectionPass(Kernel, Grid);
        Particles.PublishDiagnostics(context.Context.Counters);
    }

    // DistantChunkThrottle 档位起：仅相机可见区（加 1 chunk 边距）全速 CA，远距 chunk 隔帧更新。
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
