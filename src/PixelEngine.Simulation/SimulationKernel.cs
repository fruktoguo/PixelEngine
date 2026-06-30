using PixelEngine.Core.Threading;
using PixelEngine.Core.Diagnostics;

namespace PixelEngine.Simulation;

/// <summary>
/// Falling-sand CA 内核入口。当前节点提供单线程 StepCa 路径，后续节点接入 checkerboard 并行调度。
/// </summary>
/// <remarks>
/// 创建 SimulationKernel。
/// </remarks>
public sealed class SimulationKernel(
    IChunkSource chunks,
    MaterialPropsTable materialProps,
    ulong worldSeed = 0,
    IRigidDamageSink? rigidDamageSink = null,
    IReactionExecutor? reactionExecutor = null,
    ILifetimeSink? lifetimeSink = null,
    FrameProfiler? profiler = null)
{
    private readonly IChunkSource _chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
    private readonly IRigidDamageSink _rigidDamageSink = rigidDamageSink ?? IRigidDamageSink.Null;
    private readonly IReactionExecutor _reactionExecutor = reactionExecutor ?? IReactionExecutor.Null;
    private readonly ILifetimeSink _lifetimeSink = lifetimeSink ?? ILifetimeSink.Null;
    private readonly CheckerboardScheduler _scheduler = new();

    /// <summary>
    /// 材质属性只读视图。
    /// </summary>
    public MaterialPropsTable MaterialProps { get; } = materialProps ?? throw new ArgumentNullException(nameof(materialProps));

    /// <summary>
    /// 世界随机种子。
    /// </summary>
    public ulong WorldSeed { get; } = worldSeed;

    /// <summary>
    /// 可选帧诊断计时器。
    /// </summary>
    public FrameProfiler? Profiler { get; } = profiler;

    /// <summary>
    /// 当前 CA 帧 parity 位。
    /// </summary>
    public byte CurrentParity { get; private set; }

    /// <summary>
    /// 已执行 CA tick 数。
    /// </summary>
    public uint FrameIndex { get; private set; }

    /// <summary>
    /// 执行一次单线程 CA step：翻转 parity，并顺序更新 awake chunk 的 current dirty。
    /// </summary>
    public void StepCa()
    {
        AdvanceParity();
        _scheduler.StepSingleThread(_chunks, MaterialProps, CurrentParity, FrameIndex, WorldSeed, _rigidDamageSink, _reactionExecutor, _lifetimeSink, Profiler);
    }

    /// <summary>
    /// 使用 JobSystem 执行一次 4-pass checkerboard CA step，低活跃 chunk 数时回退单线程。
    /// </summary>
    public void StepCa(JobSystem jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        AdvanceParity();
        _scheduler.Step(_chunks, jobs, MaterialProps, CurrentParity, FrameIndex, WorldSeed, _rigidDamageSink, _reactionExecutor, _lifetimeSink, Profiler);
    }

    /// <summary>
    /// 执行帧边界 dirty rectangle swap，并根据下一帧 current dirty 更新 chunk sleep 状态。
    /// </summary>
    public void SwapDirtyRects()
    {
        foreach (Chunk chunk in _chunks.ResidentChunks)
        {
            chunk.SwapDirtyRects();
        }
    }

    private void AdvanceParity()
    {
        CurrentParity ^= CellFlags.Parity;
        FrameIndex++;
    }
}
