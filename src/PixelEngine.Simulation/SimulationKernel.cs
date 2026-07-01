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
    IMaterialCustomUpdateExecutor? customUpdateExecutor = null,
    FrameProfiler? profiler = null)
{
    private readonly IChunkSource _chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
    private readonly IRigidDamageSink _rigidDamageSink = rigidDamageSink ?? IRigidDamageSink.Null;
    private readonly IReactionExecutor _reactionExecutor = reactionExecutor ?? IReactionExecutor.Null;
    private readonly ILifetimeSink _lifetimeSink = lifetimeSink ?? ILifetimeSink.Null;
    private readonly IMaterialCustomUpdateExecutor _customUpdateExecutor = customUpdateExecutor ?? IMaterialCustomUpdateExecutor.Null;
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
    /// 强制 `StepCa(JobSystem)` 使用单线程路径，供确定性 oracle 和调试使用。
    /// </summary>
    public bool ForceSingleThread { get; set; }

    /// <summary>
    /// 相位 3：把粒子落定结果写回权威网格，并标记 current dirty 使本帧 CA 立即可见。
    /// </summary>
    public void DepositCell(int wx, int wy, ushort material, byte persistentFlags)
    {
        WriteCellAndMarkCurrent(wx, wy, material, persistentFlags);
    }

    /// <summary>
    /// 相位 1：编辑器/输入写入权威网格，并标记 current dirty 与边界唤醒，使本帧 CA 立即可见。
    /// </summary>
    public void EditCellAtInputPhase(int wx, int wy, ushort material, byte persistentFlags)
    {
        WriteCellAndMarkCurrent(wx, wy, material, persistentFlags);
    }

    /// <summary>
    /// 相位 1：编辑器/输入清空权威网格 cell，并标记 current dirty 与边界唤醒。
    /// </summary>
    public void ClearCellAtInputPhase(int wx, int wy)
    {
        Chunk chunk = RequireChunk(wx, wy);
        int local = CellAddressing.LocalIndex(wx, wy);
        if (chunk.Material[local] == 0 && chunk.Flags[local] == 0 && chunk.Lifetime[local] == 0)
        {
            return;
        }

        NotifyRigidDamageIfNeeded(wx, wy, chunk.Flags[local]);
        chunk.Material[local] = 0;
        chunk.Flags[local] = 0;
        chunk.Lifetime[local] = 0;
        MarkDirty(wx, wy);
    }

    /// <summary>
    /// 相位 3：标记世界坐标所在 cell 为 current dirty，使本帧 CA 会重检该区域。
    /// </summary>
    public void MarkDirty(int wx, int wy)
    {
        DirtyRegionMarker.MarkCell(_chunks, wx, wy, DirtyPhaseTarget.Current, includeBoundaryNeighbors: true, Diagnostics);
    }

    /// <summary>
    /// 执行一次单线程 CA step：翻转 parity，并顺序更新 awake chunk 的 current dirty。
    /// </summary>
    public void StepCa()
    {
        AdvanceParity();
        _scheduler.StepSingleThread(_chunks, MaterialProps, CurrentParity, FrameIndex, WorldSeed, _rigidDamageSink, _reactionExecutor, _lifetimeSink, _customUpdateExecutor, Diagnostics, Profiler);
    }

    /// <summary>
    /// 使用 JobSystem 执行一次 4-pass checkerboard CA step，低活跃 chunk 数时回退单线程。
    /// </summary>
    public void StepCa(JobSystem jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        AdvanceParity();
        if (ForceSingleThread)
        {
            _scheduler.StepSingleThread(_chunks, MaterialProps, CurrentParity, FrameIndex, WorldSeed, _rigidDamageSink, _reactionExecutor, _lifetimeSink, _customUpdateExecutor, Diagnostics, Profiler);
            return;
        }

        _scheduler.Step(_chunks, jobs, MaterialProps, CurrentParity, FrameIndex, WorldSeed, _rigidDamageSink, _reactionExecutor, _lifetimeSink, _customUpdateExecutor, Diagnostics, Profiler);
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

    /// <summary>
    /// 相位 7：读取一个 cell 并清空为 Empty，标记 dirty 给下一帧使用。
    /// </summary>
    public ushort ReadAndClearCell(int wx, int wy, out byte flags, out byte lifetime)
    {
        Chunk chunk = RequireChunk(wx, wy);
        int local = CellAddressing.LocalIndex(wx, wy);
        ushort material = chunk.Material[local];
        flags = chunk.Flags[local];
        lifetime = chunk.Lifetime[local];
        if (material != 0 || flags != 0 || lifetime != 0)
        {
            chunk.Material[local] = 0;
            chunk.Flags[local] = 0;
            chunk.Lifetime[local] = 0;
            DirtyRegionMarker.MarkCell(_chunks, wx, wy, DirtyPhaseTarget.Current, includeBoundaryNeighbors: true, Diagnostics);
        }

        return material;
    }

    /// <summary>
    /// 读取一个 cell 与所属 chunk 的编辑器检视快照。
    /// </summary>
    public bool TryInspectCell(
        int wx,
        int wy,
        MaterialTable materials,
        TemperatureField? temperature,
        IRigidCellOwnershipLookup? rigidOwnership,
        out SimulationCellInspection inspection)
    {
        ArgumentNullException.ThrowIfNull(materials);
        ChunkCoord coord = CellAddressing.WorldToChunk(wx, wy);
        if (!_chunks.TryGetChunk(coord, out Chunk chunk))
        {
            inspection = default;
            return false;
        }

        int localX = CellAddressing.LocalCoord(wx);
        int localY = CellAddressing.LocalCoord(wy);
        int local = CellAddressing.LocalIndexFromLocal(localX, localY);
        ushort material = chunk.Material[local];
        byte flags = chunk.Flags[local];
        string materialName = material < materials.Count && !materials.IsTombstone(material)
            ? materials.GetName(material)
            : string.Empty;
        bool hasTemperature = temperature is not null;
        int? bodyId = CellFlags.Has(flags, CellFlags.RigidOwned) &&
            rigidOwnership is not null &&
            rigidOwnership.TryGetBodyAtCell(wx, wy, out int resolvedBodyId)
            ? resolvedBodyId
            : null;
        inspection = new SimulationCellInspection(
            wx,
            wy,
            coord,
            localX,
            localY,
            material,
            materialName,
            hasTemperature ? temperature!.GetTemperature(wx, wy) : 0f,
            hasTemperature,
            SimulationCellFlags.FromRaw(flags),
            bodyId,
            chunk.CurrentDirty,
            chunk.WorkingDirty,
            chunk.State,
            chunk.Parity);
        return true;
    }

    /// <summary>
    /// 复制本帧记录到的 KeepAlive/边界唤醒诊断，供 Editor 调试叠层读取。
    /// </summary>
    public int CopyBoundaryWakeSnapshots(Span<BoundaryWakeSnapshot> destination)
    {
        ReadOnlySpan<BoundaryWakeRecord> records = Diagnostics.BoundaryWakeRecords;
        int count = Math.Min(records.Length, destination.Length);
        for (int i = 0; i < count; i++)
        {
            BoundaryWakeRecord record = records[i];
            destination[i] = new BoundaryWakeSnapshot(record.TargetCoord, record.IncomingSlot, record.Rect);
        }

        return count;
    }

    internal long CountNonEmptyCells()
    {
        long count = 0;
        foreach (Chunk chunk in _chunks.ResidentChunks)
        {
            ushort[] material = chunk.Material;
            for (int i = 0; i < material.Length; i++)
            {
                if (material[i] != 0)
                {
                    count++;
                }
            }
        }

        return count;
    }

    internal SimulationDiagnostics Diagnostics { get; } = new();

    internal ChunkSnapshot SnapshotChunk(ChunkCoord coord)
    {
        return _chunks.TryGetChunk(coord, out Chunk chunk)
            ? ChunkSnapshot.Create(chunk)
            : throw new InvalidOperationException($"目标 chunk 未驻留：{coord}。");
    }

    private void AdvanceParity()
    {
        CurrentParity ^= CellFlags.Parity;
        FrameIndex++;
    }

    private Chunk RequireChunk(int wx, int wy)
    {
        ChunkCoord coord = CellAddressing.WorldToChunk(wx, wy);
        return !_chunks.TryGetChunk(coord, out Chunk chunk) ? throw new InvalidOperationException($"目标 chunk 未驻留：{coord}。") : chunk;
    }

    private void WriteCellAndMarkCurrent(int wx, int wy, ushort material, byte persistentFlags)
    {
        Chunk chunk = RequireChunk(wx, wy);
        int local = CellAddressing.LocalIndex(wx, wy);
        NotifyRigidDamageIfNeeded(wx, wy, chunk.Flags[local]);
        chunk.Material[local] = material;
        chunk.Flags[local] = CellFlags.SetParity(persistentFlags, CurrentParity);
        chunk.Lifetime[local] = DefaultLifetimeByte(material);
        MarkDirty(wx, wy);
    }

    private byte DefaultLifetimeByte(ushort material)
    {
        ushort lifetime = MaterialProps.DefaultLifetimeOf(material);
        return lifetime > byte.MaxValue
            ? throw new InvalidOperationException($"材质 {material} 的默认 lifetime 超过 byte 存储上限。")
            : (byte)lifetime;
    }

    private void NotifyRigidDamageIfNeeded(int wx, int wy, byte flags)
    {
        if (CellFlags.Has(flags, CellFlags.RigidOwned))
        {
            _rigidDamageSink.OnOwnedCellDamaged(wx, wy);
        }
    }
}
