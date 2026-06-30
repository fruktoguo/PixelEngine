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
    IRigidDamageSink? rigidDamageSink = null)
{
    private readonly IChunkSource _chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
    private readonly IRigidDamageSink _rigidDamageSink = rigidDamageSink ?? IRigidDamageSink.Null;

    /// <summary>
    /// 材质属性只读视图。
    /// </summary>
    public MaterialPropsTable MaterialProps { get; } = materialProps ?? throw new ArgumentNullException(nameof(materialProps));

    /// <summary>
    /// 世界随机种子。
    /// </summary>
    public ulong WorldSeed { get; } = worldSeed;

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
        CurrentParity ^= CellFlags.Parity;
        FrameIndex++;

        foreach (Chunk chunk in _chunks.ResidentChunks)
        {
            if (chunk.State != ChunkState.Awake || chunk.CurrentDirty.IsEmpty)
            {
                continue;
            }

            ChunkUpdater.UpdateChunk(
                chunk,
                _chunks,
                MaterialProps,
                CurrentParity,
                FrameIndex,
                WorldSeed,
                _rigidDamageSink);
        }
    }
}
