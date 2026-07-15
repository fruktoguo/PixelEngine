using PixelEngine.Core;
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
    FrameProfiler? profiler = null,
    ICellDestructionSink? cellDestructionSink = null)
{
    private readonly IChunkSource _chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
    private readonly IRigidDamageSink _rigidDamageSink = rigidDamageSink ?? IRigidDamageSink.Null;
    private readonly ICellDestructionSink _cellDestructionSink = cellDestructionSink ?? ICellDestructionSink.Null;
    private readonly IReactionExecutor _reactionExecutor = reactionExecutor ?? IReactionExecutor.Null;
    private readonly ILifetimeSink _lifetimeSink = lifetimeSink ?? ILifetimeSink.Null;
    private readonly IMaterialCustomUpdateExecutor _customUpdateExecutor = customUpdateExecutor ?? IMaterialCustomUpdateExecutor.Null;
    private readonly CheckerboardScheduler _scheduler = new();

    /// <summary>
    /// 材质属性只读视图。
    /// </summary>
    public MaterialPropsTable MaterialProps { get; } = materialProps ?? throw new ArgumentNullException(nameof(materialProps));

    /// <summary>
    /// 刷新 CA movement / lifetime 消费的材质热表；由内容热重载在帧边界调用。
    /// </summary>
    public void ReloadMaterialHotTable(MaterialHotTable hot)
    {
        MaterialProps.Reload(hot);
    }

    /// <summary>
    /// 世界随机种子。
    /// </summary>
    public ulong WorldSeed { get; private set; } = worldSeed;

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
    /// 从世界存档恢复 CA 帧序号与 parity；不执行任何 CA step。
    /// </summary>
    /// <param name="frameIndex">已执行 CA tick 数。</param>
    /// <param name="currentParity">当前 CA parity 位。</param>
    public void RestoreFrameState(uint frameIndex, byte currentParity)
    {
        FrameIndex = frameIndex;
        CurrentParity = (byte)(currentParity & CellFlags.Parity);
    }

    /// <summary>
    /// 从整世界存档恢复随机种子、CA 帧序号与 parity；不执行任何 CA step。
    /// </summary>
    /// <param name="worldSeed">存档中的权威世界随机种子。</param>
    /// <param name="frameIndex">已执行 CA tick 数。</param>
    /// <param name="currentParity">当前 CA parity 位。</param>
    public void RestoreWorldState(ulong worldSeed, uint frameIndex, byte currentParity)
    {
        WorldSeed = worldSeed;
        RestoreFrameState(frameIndex, currentParity);
    }

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
    /// 相位 1：批量写入世界坐标闭区间矩形，并按 row-run 批量更新 SoA 与 dirty rect。
    /// </summary>
    public int EditRectAtInputPhase(int minX, int minY, int maxX, int maxY, ushort material, byte persistentFlags)
    {
        if (minX > maxX || minY > maxY)
        {
            return 0;
        }

        byte flags = CellFlags.SetParity(persistentFlags, CurrentParity);
        byte lifetime = DefaultLifetimeByte(material);
        int writes = 0;
        // 相位 1 批量写入：按 chunk 行连续段填充 SoA，最后统一扩 current dirty rect。
        ChunkCoord minCoord = CellAddressing.WorldToChunk(minX, minY);
        ChunkCoord maxCoord = CellAddressing.WorldToChunk(maxX, maxY);
        // 显式展开 row-run 遍历，避免帧/交互路径创建捕获闭包与委托。
        for (int cy = minCoord.Y; cy <= maxCoord.Y; cy++)
        {
            for (int cx = minCoord.X; cx <= maxCoord.X; cx++)
            {
                ChunkCoord coord = new(cx, cy);
                if (!_chunks.TryGetChunk(coord, out Chunk chunk))
                {
                    throw new InvalidOperationException($"目标 chunk 未驻留：{coord}。");
                }

                int chunkMinX = cx * EngineConstants.ChunkSize;
                int chunkMinY = cy * EngineConstants.ChunkSize;
                int chunkMaxX = chunkMinX + EngineConstants.ChunkSize - 1;
                int chunkMaxY = chunkMinY + EngineConstants.ChunkSize - 1;
                int runMinX = Math.Max(minX, chunkMinX);
                int runMaxX = Math.Min(maxX, chunkMaxX);
                int runMinY = Math.Max(minY, chunkMinY);
                int runMaxY = Math.Min(maxY, chunkMaxY);
                int run = runMaxX - runMinX + 1;
                int localX = CellAddressing.LocalCoord(runMinX);
                for (int worldY = runMinY; worldY <= runMaxY; worldY++)
                {
                    int localY = CellAddressing.LocalCoord(worldY);
                    int localStart = CellAddressing.LocalIndexFromLocal(localX, localY);
                    for (int i = 0; i < run; i++)
                    {
                        NotifyRigidDamageIfNeeded(runMinX + i, worldY, chunk.FlagsBuffer[localStart + i], chunk.MaterialBuffer[localStart + i]);
                    }

                    chunk.MaterialBuffer.AsSpan(localStart, run).Fill(material);
                    chunk.FlagsBuffer.AsSpan(localStart, run).Fill(flags);
                    chunk.LifetimeBuffer.AsSpan(localStart, run).Fill(lifetime);
                    chunk.DamageBuffer.AsSpan(localStart, run).Clear();
                    writes += run;
                }
            }
        }

        DirtyRegionMarker.MarkRectCurrent(_chunks, minX, minY, maxX, maxY, EngineConstants.DirtyRectPadding);
        return writes;
    }

    /// <summary>
    /// 相位 1：编辑器/输入清空权威网格 cell，并标记 current dirty 与边界唤醒。
    /// </summary>
    public void ClearCellAtInputPhase(int wx, int wy)
    {
        Chunk chunk = RequireChunk(wx, wy);
        int local = CellAddressing.LocalIndex(wx, wy);
        if (chunk.MaterialBuffer[local] == 0 && chunk.FlagsBuffer[local] == 0 && chunk.LifetimeBuffer[local] == 0)
        {
            return;
        }

        NotifyRigidDamageIfNeeded(wx, wy, chunk.FlagsBuffer[local], chunk.MaterialBuffer[local]);
        chunk.MaterialBuffer[local] = 0;
        chunk.FlagsBuffer[local] = 0;
        chunk.LifetimeBuffer[local] = 0;
        chunk.DamageBuffer[local] = 0;
        MarkDirty(wx, wy);
    }

    /// <summary>
    /// 相位 1：批量清空世界坐标闭区间矩形，并按 row-run 批量清理 SoA 与 dirty rect。
    /// </summary>
    public int ClearRectAtInputPhase(int minX, int minY, int maxX, int maxY)
    {
        if (minX > maxX || minY > maxY)
        {
            return 0;
        }

        int writes = 0;
        ChunkCoord minCoord = CellAddressing.WorldToChunk(minX, minY);
        ChunkCoord maxCoord = CellAddressing.WorldToChunk(maxX, maxY);
        // 显式展开 row-run 遍历，避免空/非空清理路径为 lambda 分配闭包与委托。
        for (int cy = minCoord.Y; cy <= maxCoord.Y; cy++)
        {
            for (int cx = minCoord.X; cx <= maxCoord.X; cx++)
            {
                ChunkCoord coord = new(cx, cy);
                if (!_chunks.TryGetChunk(coord, out Chunk chunk))
                {
                    throw new InvalidOperationException($"目标 chunk 未驻留：{coord}。");
                }

                int chunkMinX = cx * EngineConstants.ChunkSize;
                int chunkMinY = cy * EngineConstants.ChunkSize;
                int chunkMaxX = chunkMinX + EngineConstants.ChunkSize - 1;
                int chunkMaxY = chunkMinY + EngineConstants.ChunkSize - 1;
                int runMinX = Math.Max(minX, chunkMinX);
                int runMaxX = Math.Min(maxX, chunkMaxX);
                int runMinY = Math.Max(minY, chunkMinY);
                int runMaxY = Math.Min(maxY, chunkMaxY);
                int run = runMaxX - runMinX + 1;
                int localX = CellAddressing.LocalCoord(runMinX);
                for (int worldY = runMinY; worldY <= runMaxY; worldY++)
                {
                    int localY = CellAddressing.LocalCoord(worldY);
                    int localStart = CellAddressing.LocalIndexFromLocal(localX, localY);
                    bool rowChanged = false;
                    for (int i = 0; i < run; i++)
                    {
                        int local = localStart + i;
                        rowChanged |= chunk.MaterialBuffer[local] != 0 || chunk.FlagsBuffer[local] != 0 || chunk.LifetimeBuffer[local] != 0 || chunk.DamageBuffer[local] != 0;
                        NotifyRigidDamageIfNeeded(runMinX + i, worldY, chunk.FlagsBuffer[local], chunk.MaterialBuffer[local]);
                    }

                    if (!rowChanged)
                    {
                        continue;
                    }

                    chunk.MaterialBuffer.AsSpan(localStart, run).Clear();
                    chunk.FlagsBuffer.AsSpan(localStart, run).Clear();
                    chunk.LifetimeBuffer.AsSpan(localStart, run).Clear();
                    chunk.DamageBuffer.AsSpan(localStart, run).Clear();
                    writes += run;
                }
            }
        }

        if (writes != 0)
        {
            DirtyRegionMarker.MarkRectCurrent(_chunks, minX, minY, maxX, maxY, EngineConstants.DirtyRectPadding);
        }

        return writes;
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
    public void StepCa(CaChunkThrottlePolicy throttlePolicy = default)
    {
        // 单线程 CA 入口：先清空本帧迭代诊断，再翻转 parity 驱动 checkerboard 相位。
        Diagnostics.ResetCaIterationRecords();
        AdvanceParity();
        throttlePolicy = throttlePolicy.ForFrame(FrameIndex);
        _scheduler.StepSingleThread(_chunks, MaterialProps, CurrentParity, FrameIndex, WorldSeed, _rigidDamageSink, _reactionExecutor, _lifetimeSink, _customUpdateExecutor, Diagnostics, Profiler, throttlePolicy);
    }

    /// <summary>
    /// 使用 JobSystem 执行一次 4-pass checkerboard CA step，低活跃 chunk 数时回退单线程。
    /// </summary>
    public void StepCa(JobSystem jobs, CaChunkThrottlePolicy throttlePolicy = default)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        Diagnostics.ResetCaIterationRecords();
        AdvanceParity();
        throttlePolicy = throttlePolicy.ForFrame(FrameIndex);
        // 确定性 oracle / 调试可强制走单线程，避免 JobSystem 引入调度差异。
        if (ForceSingleThread)
        {
            _scheduler.StepSingleThread(_chunks, MaterialProps, CurrentParity, FrameIndex, WorldSeed, _rigidDamageSink, _reactionExecutor, _lifetimeSink, _customUpdateExecutor, Diagnostics, Profiler, throttlePolicy);
            return;
        }

        _scheduler.Step(_chunks, jobs, MaterialProps, CurrentParity, FrameIndex, WorldSeed, _rigidDamageSink, _reactionExecutor, _lifetimeSink, _customUpdateExecutor, Diagnostics, Profiler, throttlePolicy);
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
        ushort material = chunk.MaterialBuffer[local];
        flags = chunk.FlagsBuffer[local];
        lifetime = chunk.LifetimeBuffer[local];
        if (material != 0 || flags != 0 || lifetime != 0)
        {
            NotifyRigidDamageIfNeeded(wx, wy, flags, material);
            chunk.MaterialBuffer[local] = 0;
            chunk.FlagsBuffer[local] = 0;
            chunk.LifetimeBuffer[local] = 0;
            chunk.DamageBuffer[local] = 0;
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
        ushort material = chunk.MaterialBuffer[local];
        byte flags = chunk.FlagsBuffer[local];
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

    /// <summary>
    /// 复制本帧实际进入 CA 更新器的 dirty rectangle，供 Editor 调试叠层确认 sleeping 区零迭代。
    /// </summary>
    public int CopyCaIterationSnapshots(Span<CaIterationSnapshot> destination)
    {
        ReadOnlySpan<CaIterationSnapshot> records = Diagnostics.CaIterationRecords;
        int count = Math.Min(records.Length, destination.Length);
        records[..count].CopyTo(destination);
        return count;
    }

    internal long CountNonEmptyCells()
    {
        long count = 0;
        foreach (Chunk chunk in _chunks.ResidentChunks)
        {
            count += CellSpanOps.CountNonZeroUShort(chunk.MaterialBuffer);
        }

        return count;
    }

    /// <summary>
    /// 对单个世界坐标 cell 施加结构破坏。命中 RigidOwned cell 时只通知物理层，不在 Damage 平面累加。
    /// </summary>
    /// <param name="wx">世界 X 坐标。</param>
    /// <param name="wy">世界 Y 坐标。</param>
    /// <param name="damage">原始破坏当量。</param>
    /// <returns>若 cell 被实际破坏并转为碎块或 Empty，则返回 true。</returns>
    public bool ApplyStructuralDamage(int wx, int wy, ushort damage)
    {
        if (damage == 0 || !_chunks.TryGetChunk(CellAddressing.WorldToChunk(wx, wy), out Chunk chunk))
        {
            return false;
        }

        int local = CellAddressing.LocalIndex(wx, wy);
        ushort material = chunk.MaterialBuffer[local];
        if (material == 0)
        {
            chunk.DamageBuffer[local] = 0;
            return false;
        }

        byte flags = chunk.FlagsBuffer[local];
        // RigidOwned cell 不参与 Damage 平面累加，只通知物理层做刚体重建。
        if (CellFlags.Has(flags, CellFlags.RigidOwned))
        {
            _rigidDamageSink.OnOwnedCellDamaged(wx, wy, material);
            chunk.DamageBuffer[local] = 0;
            return false;
        }

        if (MaterialProps.TypeOf(material) is not (CellType.Solid or CellType.Powder))
        {
            chunk.DamageBuffer[local] = 0;
            return false;
        }

        if ((MaterialProps.PropertyFlagsOf(material) & MaterialProperty.Indestructible) != 0)
        {
            chunk.DamageBuffer[local] = 0;
            return false;
        }

        int effectiveDamage = damage - (MaterialProps.HardnessOf(material) * EngineConstants.DamageHardnessAbsorb);
        if (effectiveDamage <= 0)
        {
            return false;
        }

        ushort maxIntegrity = MaterialProps.MaxIntegrityOf(material);
        if (maxIntegrity != 0)
        {
            // 未达完整性阈值时只累加 Damage 并标 working dirty，cell 材质保持不变。
            int accumulated = Math.Min(byte.MaxValue, chunk.DamageBuffer[local] + effectiveDamage);
            if (accumulated * EngineConstants.DamageIntegrityScale < maxIntegrity)
            {
                chunk.DamageBuffer[local] = (byte)accumulated;
                MarkDamageDirty(wx, wy);
                return false;
            }
        }

        DestroyCell(chunk, local, wx, wy, material);
        return true;
    }

    /// <summary>
    /// 对圆形范围内已驻留 cell 施加结构破坏，按距离做线性衰减并跳过未驻留 chunk。
    /// </summary>
    /// <param name="centerX">圆心世界 X 坐标。</param>
    /// <param name="centerY">圆心世界 Y 坐标。</param>
    /// <param name="radius">半径，单位 cell。</param>
    /// <param name="damage">中心破坏当量。</param>
    /// <param name="falloff">是否按距离线性衰减。</param>
    /// <returns>实际破坏的 cell 数量。</returns>
    public int DamageCircle(int centerX, int centerY, int radius, ushort damage, bool falloff)
    {
        if (radius < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "破坏半径不能为负。");
        }

        if (damage == 0)
        {
            return 0;
        }

        // 圆形破坏：按距离平方剪枝，可选线性 falloff 衰减当量。
        int destroyed = 0;
        int radiusSquared = radius * radius;
        int minX = centerX - radius;
        int maxX = centerX + radius;
        int minY = centerY - radius;
        int maxY = centerY + radius;
        for (int wy = minY; wy <= maxY; wy++)
        {
            int dy = wy - centerY;
            for (int wx = minX; wx <= maxX; wx++)
            {
                int dx = wx - centerX;
                int distanceSquared = (dx * dx) + (dy * dy);
                if (distanceSquared > radiusSquared)
                {
                    continue;
                }

                ushort cellDamage = damage;
                if (falloff && radius > 0)
                {
                    double distance = Math.Sqrt(distanceSquared);
                    double scale = Math.Max(0d, 1d - (distance / radius));
                    cellDamage = (ushort)Math.Clamp((int)Math.Ceiling(damage * scale), 0, ushort.MaxValue);
                }

                if (cellDamage != 0 && ApplyStructuralDamage(wx, wy, cellDamage))
                {
                    destroyed++;
                }
            }
        }

        return destroyed;
    }

    /// <summary>
    /// 沿归一化方向近似光束逐 cell 施加结构破坏，未驻留段安全跳过。
    /// </summary>
    /// <param name="startX">起点世界 X 坐标。</param>
    /// <param name="startY">起点世界 Y 坐标。</param>
    /// <param name="dirX">方向 X 分量。</param>
    /// <param name="dirY">方向 Y 分量。</param>
    /// <param name="length">长度，单位 cell。</param>
    /// <param name="damagePerCell">每个命中 cell 的破坏当量。</param>
    /// <returns>实际破坏的 cell 数量。</returns>
    public int DamageBeam(int startX, int startY, float dirX, float dirY, int length, ushort damagePerCell)
    {
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "光束长度不能为负。");
        }

        float magnitude = MathF.Sqrt((dirX * dirX) + (dirY * dirY));
        if (magnitude <= float.Epsilon || damagePerCell == 0)
        {
            return 0;
        }

        float stepX = dirX / magnitude;
        float stepY = dirY / magnitude;
        // 光束破坏：沿归一化方向逐 cell 采样，跳过重复格点。
        int destroyed = 0;
        int lastX = int.MinValue;
        int lastY = int.MinValue;
        for (int i = 0; i <= length; i++)
        {
            int wx = (int)MathF.Round(startX + (stepX * i));
            int wy = (int)MathF.Round(startY + (stepY * i));
            if (wx == lastX && wy == lastY)
            {
                continue;
            }

            lastX = wx;
            lastY = wy;
            if (ApplyStructuralDamage(wx, wy, damagePerCell))
            {
                destroyed++;
            }
        }

        return destroyed;
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
        NotifyRigidDamageIfNeeded(wx, wy, chunk.FlagsBuffer[local], chunk.MaterialBuffer[local]);
        chunk.MaterialBuffer[local] = material;
        chunk.FlagsBuffer[local] = CellFlags.SetParity(persistentFlags, CurrentParity);
        chunk.LifetimeBuffer[local] = DefaultLifetimeByte(material);
        chunk.DamageBuffer[local] = 0;
        MarkDirty(wx, wy);
    }

    private void DestroyCell(Chunk chunk, int local, int wx, int wy, ushort sourceMaterial)
    {
        // 结构破坏落地：转为碎块材质或 Empty，并上报采集/碎屑副作用。
        ushort rubbleTarget = MaterialProps.RubbleTargetOf(sourceMaterial);
        chunk.MaterialBuffer[local] = rubbleTarget;
        chunk.FlagsBuffer[local] = rubbleTarget == 0 ? (byte)0 : CellFlags.SetParity(0, CurrentParity);
        chunk.LifetimeBuffer[local] = DefaultLifetimeByte(rubbleTarget);
        chunk.DamageBuffer[local] = 0;
        MarkDamageDirty(wx, wy);
        NotifyCellDestroyed(wx, wy, sourceMaterial, rubbleTarget);
    }

    private void NotifyCellDestroyed(int wx, int wy, ushort sourceMaterial, ushort targetMaterial)
    {
        MaterialProperty flags = MaterialProps.PropertyFlagsOf(sourceMaterial);
        byte mineYield = (flags & MaterialProperty.Diggable) != 0
            ? MaterialProps.MineYieldOf(sourceMaterial)
            : (byte)0;
        CellDestructionEvent item = new(
            wx,
            wy,
            sourceMaterial,
            targetMaterial,
            targetMaterial == 0 ? sourceMaterial : targetMaterial,
            MaterialProps.DebrisCountOf(sourceMaterial),
            mineYield);
        _cellDestructionSink.OnCellDestroyed(in item);
    }

    private void MarkDamageDirty(int wx, int wy)
    {
        // 破坏只写 working dirty；先本 chunk 再邻接唤醒，保证下一帧 CA 重检受损区。
        _ = DirtyRegionMarker.TryMarkCell(_chunks, wx, wy, DirtyPhaseTarget.Working, includeBoundaryNeighbors: false, Diagnostics);
        _ = DirtyRegionMarker.TryMarkCell(_chunks, wx, wy, DirtyPhaseTarget.Working, includeBoundaryNeighbors: true, Diagnostics);
    }

    private byte DefaultLifetimeByte(ushort material)
    {
        ushort lifetime = MaterialProps.DefaultLifetimeOf(material);
        return lifetime > byte.MaxValue
            ? throw new InvalidOperationException($"材质 {material} 的默认 lifetime 超过 byte 存储上限。")
            : (byte)lifetime;
    }

    private void NotifyRigidDamageIfNeeded(int wx, int wy, byte flags, ushort material)
    {
        if (CellFlags.Has(flags, CellFlags.RigidOwned))
        {
            _rigidDamageSink.OnOwnedCellDamaged(wx, wy, material);
        }
    }
}
