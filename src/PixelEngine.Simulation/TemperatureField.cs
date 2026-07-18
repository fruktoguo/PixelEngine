using PixelEngine.Core;
using PixelEngine.Core.Threading;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace PixelEngine.Simulation;

/// <summary>
/// 温度场子块的存储格式。
/// </summary>
public enum TemperatureStorageKind
{
    /// <summary>
    /// 以 <see cref="Half" /> 存储，优先节省内存。
    /// </summary>
    Float16,

    /// <summary>
    /// 以 <see cref="float" /> 存储，优先精度与 SIMD 传导性能。
    /// </summary>
    Float32,
}

/// <summary>
/// 1/4 分辨率温度场。每个 64x64 chunk 对应 16x16 温度子块，温度场使用独立 ping-pong 缓冲。
/// </summary>
public sealed class TemperatureField
{
    private static readonly RangeJob ConductRowsJob = static (start, end, workerIndex, context) =>
    {
        TemperatureField field = (TemperatureField)context!;
        field.MarkConductWorker(workerIndex);
        field.ConductRows(start, end);
    };

    /// <summary>
    /// 单 chunk 温度子块边长。
    /// </summary>
    public const int BlockSize = EngineConstants.ChunkSize / EngineConstants.TempFieldDownscale;

    /// <summary>
    /// 单 chunk 温度子块 cell 数。
    /// </summary>
    public const int BlockArea = BlockSize * BlockSize;

    private const int MinConductRowsPerJob = BlockSize;
    private const int MinParallelConductRows = EngineConstants.SingleThreadChunkThreshold * BlockSize;
    private const float AmbientTemperatureCelsius = 0f;
    private const float AmbientCoolingPerStep = 3f;
    private const float AmbientTemperatureEpsilon = 0.01f;
    // 仅缓存近期冷却完毕的温度块：避免长距离探索把临时 glow 峰值永久转化为常驻内存。
    private const int MaxRecycledBlockCount = 128;

    private readonly Dictionary<ChunkCoord, TemperatureBlock> _blocks = [];
    private readonly ChunkCoord[] _inactiveBlockBuffer = GC.AllocateArray<ChunkCoord>(128, pinned: true);
    private readonly HashSet<ChunkCoord> _conductActiveCoords = [];
    private readonly Stack<TemperatureBlock> _recycledBlocks = new(MaxRecycledBlockCount);
    private Chunk[] _conductChunks = [];
    private TemperatureBlock[] _conductBlocks = [];
    private int[] _conductWorkerHits = [];
    private int _lastConductStepVectorizedCellCount;
    private int _activeConductChunkCount;
    private MaterialHotTable? _activeConductMaterials;
    private uint _activeConductFrameIndex;
    private uint _activeConductWorldSeed;

    /// <summary>
    /// 创建温度场。
    /// </summary>
    /// <param name="stepInterval">温度 pass 运行间隔。</param>
    /// <param name="storageKind">温度子块存储格式。</param>
    /// <param name="enableSimd">是否允许使用运行时 SIMD light-up 路径。</param>
    public TemperatureField(
        int stepInterval = 1,
        TemperatureStorageKind storageKind = TemperatureStorageKind.Float16,
        bool enableSimd = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stepInterval);
        StepInterval = stepInterval;
        StorageKind = storageKind;
        EnableSimd = enableSimd;
    }

    /// <summary>
    /// 降采样倍率。
    /// </summary>
    public int Downscale => EngineConstants.TempFieldDownscale;

    /// <summary>
    /// 温度场运行间隔；1 表示每 tick 运行，N 表示每 N tick 运行一次。
    /// </summary>
    public int StepInterval { get; private set; }

    /// <summary>
    /// 是否降级为仅接触火传播。降级后温度传导与阈值相变 no-op。
    /// </summary>
    public bool ContactFireOnly { get; private set; }

    /// <summary>
    /// 是否存在活动温度 block；rendering 可据此决定是否允许跳过逐 cell 温度 glow 采样。
    /// </summary>
    public bool HasActiveBlocks => _blocks.Count != 0;

    /// <summary>
    /// 判断指定 64x64 chunk 是否包含活动温度子块，供渲染相位按 chunk 保留 palette 快路径。
    /// </summary>
    /// <param name="coord">chunk 坐标。</param>
    /// <returns>存在活动温度子块时返回 <see langword="true"/>。</returns>
    public bool HasActiveBlock(ChunkCoord coord)
    {
        return _blocks.ContainsKey(coord);
    }

    /// <summary>
    /// 温度子块存储格式。
    /// </summary>
    public TemperatureStorageKind StorageKind { get; }

    private bool EnableSimd { get; }

    /// <summary>
    /// 最近一次温度传导是否通过 <see cref="JobSystem" /> 派发行分块工作。
    /// </summary>
    public bool LastConductStepUsedJobSystem { get; private set; }

    /// <summary>
    /// 最近一次温度传导触达的 JobSystem worker 数。单线程路径为 1；无行可处理为 0。
    /// </summary>
    public int LastConductStepWorkerCount { get; private set; }

    /// <summary>
    /// 最近一次温度传导通过 Intrinsics SIMD 处理的温度 cell 数。
    /// </summary>
    public int LastConductStepVectorizedCellCount => Volatile.Read(ref _lastConductStepVectorizedCellCount);

    /// <summary>
    /// 当前运行时是否具备 Vector SIMD 加速能力。
    /// </summary>
    public bool SimdAvailable => EnableSimd &&
        StorageKind == TemperatureStorageKind.Float32 &&
        (Vector256.IsHardwareAccelerated || Vector128.IsHardwareAccelerated);

    /// <summary>
    /// 调整温度场降频间隔。
    /// </summary>
    public void SetStepInterval(int stepInterval)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stepInterval);
        StepInterval = stepInterval;
    }

    /// <summary>
    /// 降级为仅接触式火传播；温度场不再执行传导与相变。
    /// </summary>
    public void DegradeToContactFireOnly()
    {
        ContactFireOnly = true;
    }

    /// <summary>
    /// 当前 frame 是否应执行温度场 pass。
    /// </summary>
    public bool ShouldRun(uint frameIndex)
    {
        return !ContactFireOnly && frameIndex % (uint)StepInterval == 0;
    }

    /// <summary>
    /// 向指定世界 cell 所属粗温度 cell 注入热量。
    /// </summary>
    public void AddHeat(int worldX, int worldY, float deltaC)
    {
        (ChunkCoord coord, int local) = TemperatureAddress(worldX, worldY);
        TemperatureBlock block = GetOrCreateBlock(coord);
        block.AddCurrent(local, deltaC);
    }

    /// <summary>
    /// 按材质热容向指定世界 cell 所属粗温度 cell 注入热量。
    /// </summary>
    public void AddHeat(int worldX, int worldY, ushort material, MaterialHotTable materials, float deltaC)
    {
        ArgumentNullException.ThrowIfNull(materials);
        float capacity = MathF.Max(materials.HeatCapacity[material], 0.0001f);
        AddHeat(worldX, worldY, deltaC / capacity);
    }

    /// <summary>
    /// 读取指定世界 cell 对应粗温度 cell 的温度。
    /// </summary>
    public float GetTemperature(int worldX, int worldY)
    {
        (ChunkCoord coord, int local) = TemperatureAddress(worldX, worldY);
        return _blocks.TryGetValue(coord, out TemperatureBlock? block) ? block.ReadCurrent(local) : 0;
    }

    /// <summary>
    /// 导出指定 chunk 的 16x16 温度子块，格式固定为 Half，供存档写入。
    /// </summary>
    public void ExportBlock(ChunkCoord coord, Span<Half> destination)
    {
        if (destination.Length != BlockArea)
        {
            throw new ArgumentException("温度子块导出缓冲长度必须等于 BlockArea。", nameof(destination));
        }

        if (!_blocks.TryGetValue(coord, out TemperatureBlock? block))
        {
            destination.Clear();
            return;
        }

        for (int i = 0; i < destination.Length; i++)
        {
            destination[i] = (Half)block.ReadCurrent(i);
        }
    }

    /// <summary>
    /// 导入指定 chunk 的 16x16 温度子块，读档后作为当前温度场状态。
    /// </summary>
    public void ImportBlock(ChunkCoord coord, ReadOnlySpan<Half> source)
    {
        if (source.Length != BlockArea)
        {
            throw new ArgumentException("温度子块导入缓冲长度必须等于 BlockArea。", nameof(source));
        }

        TemperatureBlock block = GetOrCreateBlock(coord);
        for (int i = 0; i < source.Length; i++)
        {
            block.WriteCurrent(i, (float)source[i]);
        }

        block.CopyCurrentToScratch();
    }

    /// <summary>
    /// 清空全部温度 block 与相位暂存；仅可在 world 结构性变更安全点调用。
    /// </summary>
    public void Clear()
    {
        foreach (TemperatureBlock block in _blocks.Values)
        {
            RecycleBlock(block);
        }

        _blocks.Clear();
        _conductActiveCoords.Clear();
        _activeConductChunkCount = 0;
        _conductChunks = [];
        _conductBlocks = [];
        _activeConductMaterials = null;
        LastConductStepUsedJobSystem = false;
        LastConductStepWorkerCount = 0;
        Volatile.Write(ref _lastConductStepVectorizedCellCount, 0);
    }

    /// <summary>
    /// 执行一次 5-point von Neumann 热传导。
    /// </summary>
    public void ConductStep(IChunkSource chunks, MaterialHotTable materials, uint frameIndex = 0, uint worldSeed = 0)
    {
        ConductStepCore(chunks, materials, jobs: null, frameIndex, worldSeed);
    }

    /// <summary>
    /// 使用 JobSystem 按温度子块行分块执行一次 5-point von Neumann 热传导；活跃行较少时回退单线程。
    /// </summary>
    public void ConductStep(
        IChunkSource chunks,
        MaterialHotTable materials,
        JobSystem jobs,
        uint frameIndex = 0,
        uint worldSeed = 0)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ConductStepCore(chunks, materials, jobs, frameIndex, worldSeed);
    }

    private void ConductStepCore(
        IChunkSource chunks,
        MaterialHotTable materials,
        JobSystem? jobs,
        uint frameIndex,
        uint worldSeed)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(materials);
        _lastConductStepVectorizedCellCount = 0;
        // 降级模式跳过传导与相变，仅保留接触式火传播路径。
        if (ContactFireOnly)
        {
            LastConductStepUsedJobSystem = false;
            LastConductStepWorkerCount = 0;
            return;
        }

        int chunkCount = CaptureConductChunks(chunks.ResidentChunks);
        int rowCount = chunkCount * BlockSize;
        if (rowCount != 0)
        {
            _activeConductMaterials = materials;
            _activeConductFrameIndex = frameIndex;
            _activeConductWorldSeed = worldSeed;
            try
            {
                // 活跃行较少时回退单线程，避免 JobSystem 调度开销盖过传导收益。
                if (ShouldConductSingleThread(jobs, rowCount))
                {
                    LastConductStepUsedJobSystem = false;
                    LastConductStepWorkerCount = 1;
                    ConductRows(0, rowCount);
                }
                else
                {
                    ClearWorkerHits(jobs!.WorkerCount);
                    LastConductStepUsedJobSystem = true;
                    jobs.ParallelRange(rowCount, MinConductRowsPerJob, ConductRowsJob, this);
                    LastConductStepWorkerCount = CountWorkerHits(jobs.WorkerCount);
                }
            }
            finally
            {
                ClearConductContext();
            }
        }
        else
        {
            LastConductStepUsedJobSystem = false;
            LastConductStepWorkerCount = 0;
        }

        // 传导写入 scratch，帧末 ping-pong 交换后再向环境冷却并剔除近零 block。
        foreach (TemperatureBlock block in _blocks.Values)
        {
            block.Swap();
        }

        CoolTowardAmbientAndPrune();
    }

    private void CoolTowardAmbientAndPrune()
    {
        foreach (TemperatureBlock block in _blocks.Values)
        {
            block.CoolTowardAmbient(AmbientTemperatureCelsius, AmbientCoolingPerStep, AmbientTemperatureEpsilon);
        }

        PruneAmbientBlocks();
    }

    // 分批剔除近环境温度 block，避免单次遍历中修改 Dictionary 枚举器。
    private void PruneAmbientBlocks()
    {
        while (true)
        {
            int count = 0;
            foreach (KeyValuePair<ChunkCoord, TemperatureBlock> item in _blocks)
            {
                if (item.Value.IsAmbient(AmbientTemperatureCelsius, AmbientTemperatureEpsilon))
                {
                    _inactiveBlockBuffer[count++] = item.Key;
                    if (count == _inactiveBlockBuffer.Length)
                    {
                        break;
                    }
                }
            }

            if (count == 0)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                if (_blocks.Remove(_inactiveBlockBuffer[i], out TemperatureBlock? block))
                {
                    RecycleBlock(block);
                }
            }

            if (count < _inactiveBlockBuffer.Length)
            {
                return;
            }
        }
    }

    /// <summary>
    /// 对活跃 chunk 应用 melt/freeze/boil 阈值相变。
    /// </summary>
    /// <param name="chunks">驻留 chunk 源。</param>
    /// <param name="materials">材质注册表。</param>
    /// <param name="parityBit">当前 CA parity 位。</param>
    /// <param name="rigidDamageSink">刚体占用 cell 相变时接收 damage 事件的可选 sink。</param>
    public void ApplyPhaseTransitions(
        IChunkSource chunks,
        MaterialTable materials,
        byte parityBit,
        IRigidDamageSink? rigidDamageSink = null)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(materials);
        if (ContactFireOnly)
        {
            return;
        }

        MaterialHotTable hot = materials.Hot;
        foreach (Chunk chunk in chunks.ResidentChunks)
        {
            int baseX = chunk.Coord.X << EngineConstants.ChunkSizeLog2;
            int baseY = chunk.Coord.Y << EngineConstants.ChunkSizeLog2;
            // 有温度 block 或 chunk 无 current dirty 时扫全 chunk，否则只扫 CA dirty 区。
            DirtyRect rect = chunk.CurrentDirty.IsEmpty || _blocks.ContainsKey(chunk.Coord)
                ? DirtyRect.Full
                : chunk.CurrentDirty;
            for (int ly = rect.MinY; ly <= rect.MaxY; ly++)
            {
                int wy = baseY + ly;
                for (int lx = rect.MinX; lx <= rect.MaxX; lx++)
                {
                    int local = CellAddressing.LocalIndexFromLocal(lx, ly);
                    ushort material = chunk.GetMaterialAt(local);
                    byte flags = chunk.FlagsBuffer[local];
                    if (material == 0 ||
                        (!CellFlags.Has(flags, CellFlags.RigidOwned) && CellFlags.MatchesFrame(flags, parityBit)))
                    {
                        continue;
                    }

                    float temp = GetTemperature(baseX + lx, wy);
                    if (!TryPhaseTarget(hot, material, temp, out ushort target))
                    {
                        continue;
                    }

                    if (CellFlags.Has(flags, CellFlags.RigidOwned))
                    {
                        rigidDamageSink?.OnOwnedCellDamaged(baseX + lx, wy, material);
                        flags = CellFlags.Clear(flags, CellFlags.RigidOwned);
                    }

                    chunk.SetMaterialAt(local, target);
                    chunk.LifetimeBuffer[local] = DefaultLifetimeByte(hot, target);
                    chunk.FlagsBuffer[local] = CellFlags.SetParity(flags, parityBit);
                    chunk.DamageBuffer[local] = 0;
                    DirtyRegionMarker.MarkCell(chunks, baseX + lx, wy, DirtyPhaseTarget.Working, includeBoundaryNeighbors: true);
                }
            }
        }
    }

    private void ConductRows(int start, int end)
    {
        MaterialHotTable materials = _activeConductMaterials ??
            throw new InvalidOperationException("温度传导材质上下文未设置。");

        for (int rowIndex = start; rowIndex < end; rowIndex++)
        {
            int chunkIndex = rowIndex / BlockSize;
            int ty = rowIndex - (chunkIndex * BlockSize);
            ConductChunkRow(
                _conductChunks[chunkIndex],
                _conductBlocks[chunkIndex],
                materials,
                ty,
                _activeConductFrameIndex,
                _activeConductWorldSeed);
        }
    }

    private void ConductChunkRow(
        Chunk chunk,
        TemperatureBlock block,
        MaterialHotTable materials,
        int ty,
        uint frameIndex,
        uint worldSeed)
    {
        int worldBaseX = chunk.Coord.X << EngineConstants.ChunkSizeLog2;
        int worldBaseY = chunk.Coord.Y << EngineConstants.ChunkSizeLog2;
        int row = ty * BlockSize;
        Span<byte> conductChanceRow = stackalloc byte[BlockSize];
        Span<float> capacityRow = stackalloc float[BlockSize];
        for (int tx = 0; tx < BlockSize; tx++)
        {
            conductChanceRow[tx] = AverageHeatConduct(chunk, materials, tx, ty);
            capacityRow[tx] = MathF.Max(AverageHeatCapacity(chunk, materials, tx, ty), 0.0001f);
        }

        // 行内传导概率全满且非边界行时走 SIMD 内区，边界列仍标量处理跨界采样。
        if (SimdAvailable && ty > 0 && ty < BlockSize - 1 && CanVectorizeConductRow(conductChanceRow))
        {
            ConductCellScalar(block, conductChanceRow, capacityRow, worldBaseX, worldBaseY, 0, ty, frameIndex, worldSeed);
            int nextScalar = ConductInteriorRowIntrinsics(block, capacityRow, row, out int vectorizedCells);
            if (vectorizedCells != 0)
            {
                _ = Interlocked.Add(ref _lastConductStepVectorizedCellCount, vectorizedCells);
            }

            for (int tx = nextScalar; tx < BlockSize; tx++)
            {
                ConductCellScalar(block, conductChanceRow, capacityRow, worldBaseX, worldBaseY, tx, ty, frameIndex, worldSeed);
            }
        }
        else
        {
            for (int tx = 0; tx < BlockSize; tx++)
            {
                ConductCellScalar(block, conductChanceRow, capacityRow, worldBaseX, worldBaseY, tx, ty, frameIndex, worldSeed);
            }
        }
    }

    private int CaptureConductChunks(ReadOnlySpan<Chunk> residentChunks)
    {
        if (_blocks.Count == 0)
        {
            _activeConductChunkCount = 0;
            return 0;
        }

        EnsureConductCapacity(residentChunks.Length);
        _conductActiveCoords.Clear();
        foreach (ChunkCoord coord in _blocks.Keys)
        {
            _ = _conductActiveCoords.Add(coord);
        }

        int count = 0;
        for (int i = 0; i < residentChunks.Length; i++)
        {
            Chunk chunk = residentChunks[i];
            if (!RequiresConductBlock(chunk.Coord))
            {
                continue;
            }

            _conductChunks[count] = chunk;
            _conductBlocks[count] = GetOrCreateBlock(chunk.Coord);
            count++;
        }

        _conductActiveCoords.Clear();
        _activeConductChunkCount = count;
        return count;
    }

    private bool RequiresConductBlock(ChunkCoord coord)
    {
        return _conductActiveCoords.Contains(coord) ||
            _conductActiveCoords.Contains(new ChunkCoord(coord.X - 1, coord.Y)) ||
            _conductActiveCoords.Contains(new ChunkCoord(coord.X + 1, coord.Y)) ||
            _conductActiveCoords.Contains(new ChunkCoord(coord.X, coord.Y - 1)) ||
            _conductActiveCoords.Contains(new ChunkCoord(coord.X, coord.Y + 1));
    }

    private void EnsureConductCapacity(int chunkCount)
    {
        if (_conductChunks.Length < chunkCount)
        {
            _conductChunks = new Chunk[chunkCount];
            _conductBlocks = new TemperatureBlock[chunkCount];
        }
    }

    private static bool ShouldConductSingleThread(JobSystem? jobs, int rowCount)
    {
        return jobs is null
            || jobs.WorkerCount <= 1
            || rowCount < MinParallelConductRows
            || rowCount <= MinConductRowsPerJob
            || rowCount <= Math.Max(1, jobs.SingleThreadThreshold);
    }

    private void ClearWorkerHits(int workerCount)
    {
        if (_conductWorkerHits.Length < workerCount)
        {
            _conductWorkerHits = GC.AllocateArray<int>(workerCount, pinned: true);
            return;
        }

        Array.Clear(_conductWorkerHits, 0, workerCount);
    }

    private void MarkConductWorker(int workerIndex)
    {
        if ((uint)workerIndex < (uint)_conductWorkerHits.Length)
        {
            Volatile.Write(ref _conductWorkerHits[workerIndex], 1);
        }
    }

    private int CountWorkerHits(int workerCount)
    {
        int count = 0;
        int length = Math.Min(workerCount, _conductWorkerHits.Length);
        for (int i = 0; i < length; i++)
        {
            count += Volatile.Read(ref _conductWorkerHits[i]) != 0 ? 1 : 0;
        }

        return count;
    }

    private void ClearConductContext()
    {
        Array.Clear(_conductChunks, 0, _activeConductChunkCount);
        Array.Clear(_conductBlocks, 0, _activeConductChunkCount);
        _activeConductChunkCount = 0;
        _activeConductMaterials = null;
        _activeConductFrameIndex = 0;
        _activeConductWorldSeed = 0;
    }

    private static bool CanVectorizeConductRow(ReadOnlySpan<byte> conductChanceRow)
    {
        for (int tx = 1; tx < BlockSize - 1; tx++)
        {
            if (conductChanceRow[tx] != byte.MaxValue)
            {
                return false;
            }
        }

        return true;
    }

    private static int ConductInteriorRowIntrinsics(
        TemperatureBlock block,
        ReadOnlySpan<float> capacityRow,
        int row,
        out int vectorizedCells)
    {
        float[] current = block.CurrentFloat;
        float[] scratch = block.ScratchFloat;
        ref float currentBase = ref MemoryMarshal.GetArrayDataReference(current);
        ref float scratchBase = ref MemoryMarshal.GetArrayDataReference(scratch);
        ref float capacityBase = ref MemoryMarshal.GetReference(capacityRow);
        int tx = 1;
        vectorizedCells = 0;
        if (Vector256.IsHardwareAccelerated)
        {
            for (; tx <= BlockSize - 1 - Vector256<float>.Count; tx += Vector256<float>.Count)
            {
                Vector256<float> center = Vector256.LoadUnsafe(ref Unsafe.Add(ref currentBase, row + tx));
                Vector256<float> left = Vector256.LoadUnsafe(ref Unsafe.Add(ref currentBase, row + tx - 1));
                Vector256<float> right = Vector256.LoadUnsafe(ref Unsafe.Add(ref currentBase, row + tx + 1));
                Vector256<float> up = Vector256.LoadUnsafe(ref Unsafe.Add(ref currentBase, row - BlockSize + tx));
                Vector256<float> down = Vector256.LoadUnsafe(ref Unsafe.Add(ref currentBase, row + BlockSize + tx));
                Vector256<float> capacity = Vector256.LoadUnsafe(ref Unsafe.Add(ref capacityBase, tx));
                Vector256<float> neighborAverage = (left + right + up + down) * Vector256.Create(0.25f);
                Vector256<float> result = center + ((neighborAverage - center) / capacity);
                result.StoreUnsafe(ref Unsafe.Add(ref scratchBase, row + tx));
                vectorizedCells += Vector256<float>.Count;
            }
        }

        if (Vector128.IsHardwareAccelerated)
        {
            for (; tx <= BlockSize - 1 - Vector128<float>.Count; tx += Vector128<float>.Count)
            {
                Vector128<float> center = Vector128.LoadUnsafe(ref Unsafe.Add(ref currentBase, row + tx));
                Vector128<float> left = Vector128.LoadUnsafe(ref Unsafe.Add(ref currentBase, row + tx - 1));
                Vector128<float> right = Vector128.LoadUnsafe(ref Unsafe.Add(ref currentBase, row + tx + 1));
                Vector128<float> up = Vector128.LoadUnsafe(ref Unsafe.Add(ref currentBase, row - BlockSize + tx));
                Vector128<float> down = Vector128.LoadUnsafe(ref Unsafe.Add(ref currentBase, row + BlockSize + tx));
                Vector128<float> capacity = Vector128.LoadUnsafe(ref Unsafe.Add(ref capacityBase, tx));
                Vector128<float> neighborAverage = (left + right + up + down) * Vector128.Create(0.25f);
                Vector128<float> result = center + ((neighborAverage - center) / capacity);
                result.StoreUnsafe(ref Unsafe.Add(ref scratchBase, row + tx));
                vectorizedCells += Vector128<float>.Count;
            }
        }

        return tx;
    }

    private void ConductCellScalar(
        TemperatureBlock block,
        ReadOnlySpan<byte> conductChanceRow,
        ReadOnlySpan<float> capacityRow,
        int worldBaseX,
        int worldBaseY,
        int tx,
        int ty,
        uint frameIndex,
        uint worldSeed)
    {
        int index = (ty * BlockSize) + tx;
        float center = block.ReadCurrent(index);
        byte chance = conductChanceRow[tx];
        if (chance == 0)
        {
            block.WriteScratch(index, center);
            return;
        }

        int worldX = worldBaseX + (tx * Downscale);
        int worldY = worldBaseY + (ty * Downscale);
        // 非满传导材质按确定性 hash 概率门控，避免每 tick 全量 5-point 更新。
        if (chance != byte.MaxValue && ConductRandomByte(worldX, worldY, frameIndex, worldSeed) >= chance)
        {
            block.WriteScratch(index, center);
            return;
        }

        float neighborAverage =
            ReadTemperatureAtWorld(worldX - Downscale, worldY) +
            ReadTemperatureAtWorld(worldX + Downscale, worldY) +
            ReadTemperatureAtWorld(worldX, worldY - Downscale) +
            ReadTemperatureAtWorld(worldX, worldY + Downscale);
        neighborAverage *= 0.25f;
        block.WriteScratch(index, center + ((neighborAverage - center) / capacityRow[tx]));
    }

    private static byte AverageHeatConduct(Chunk chunk, MaterialHotTable materials, int tempX, int tempY)
    {
        int startX = tempX * EngineConstants.TempFieldDownscale;
        int startY = tempY * EngineConstants.TempFieldDownscale;
        int sum = 0;
        for (int y = 0; y < EngineConstants.TempFieldDownscale; y++)
        {
            for (int x = 0; x < EngineConstants.TempFieldDownscale; x++)
            {
                ushort material = chunk.GetMaterialAt(CellAddressing.LocalIndexFromLocal(startX + x, startY + y));
                sum += materials.HeatConduct[material];
            }
        }

        const int coveredCells = EngineConstants.TempFieldDownscale * EngineConstants.TempFieldDownscale;
        return (byte)((sum + (coveredCells / 2)) / coveredCells);
    }

    private static byte ConductRandomByte(int worldX, int worldY, uint frameIndex, uint worldSeed)
    {
        uint hash = worldSeed;
        hash ^= (uint)worldX * 0x9E3779B9u;
        hash = BitOperations.RotateLeft(hash, 7);
        hash ^= (uint)worldY * 0x85EBCA6Bu;
        hash = BitOperations.RotateLeft(hash, 11);
        hash ^= frameIndex * 0xC2B2AE35u;
        hash ^= hash >> 16;
        hash *= 0x7FEB352Du;
        hash ^= hash >> 15;
        return (byte)hash;
    }

    private static float AverageHeatCapacity(Chunk chunk, MaterialHotTable materials, int tempX, int tempY)
    {
        int startX = tempX * EngineConstants.TempFieldDownscale;
        int startY = tempY * EngineConstants.TempFieldDownscale;
        float sum = 0;
        for (int y = 0; y < EngineConstants.TempFieldDownscale; y++)
        {
            for (int x = 0; x < EngineConstants.TempFieldDownscale; x++)
            {
                ushort material = chunk.GetMaterialAt(CellAddressing.LocalIndexFromLocal(startX + x, startY + y));
                sum += materials.HeatCapacity[material];
            }
        }

        return sum * (1f / (EngineConstants.TempFieldDownscale * EngineConstants.TempFieldDownscale));
    }

    private float ReadTemperatureAtWorld(int worldX, int worldY)
    {
        (ChunkCoord coord, int local) = TemperatureAddress(worldX, worldY);
        return _blocks.TryGetValue(coord, out TemperatureBlock? block) ? block.ReadCurrent(local) : 0;
    }

    private TemperatureBlock GetOrCreateBlock(ChunkCoord coord)
    {
        if (_blocks.TryGetValue(coord, out TemperatureBlock? block))
        {
            return block;
        }

        block = _recycledBlocks.Count == 0
            ? new TemperatureBlock(StorageKind)
            : _recycledBlocks.Pop();
        _blocks.Add(coord, block);
        return block;
    }

    private void RecycleBlock(TemperatureBlock block)
    {
        if (_recycledBlocks.Count == MaxRecycledBlockCount)
        {
            return;
        }

        block.Reset();
        _recycledBlocks.Push(block);
    }

    private static bool TryPhaseTarget(MaterialHotTable hot, ushort material, float temperature, out ushort target)
    {
        if (!float.IsNaN(hot.BoilPoint[material]) && temperature >= hot.BoilPoint[material])
        {
            target = hot.BoilTarget[material];
            return true;
        }

        if (!float.IsNaN(hot.MeltPoint[material]) && temperature >= hot.MeltPoint[material])
        {
            target = hot.MeltTarget[material];
            return true;
        }

        if (!float.IsNaN(hot.FreezePoint[material]) && temperature <= hot.FreezePoint[material])
        {
            target = hot.FreezeTarget[material];
            return true;
        }

        target = 0;
        return false;
    }

    private static byte DefaultLifetimeByte(MaterialHotTable hot, ushort material)
    {
        ushort lifetime = hot.DefaultLifetime[material];
        return lifetime > byte.MaxValue
            ? throw new InvalidOperationException($"材质 {material} 的默认 lifetime 超过 byte 存储上限。")
            : (byte)lifetime;
    }

    private static (ChunkCoord Coord, int Local) TemperatureAddress(int worldX, int worldY)
    {
        ChunkCoord coord = CellAddressing.WorldToChunk(worldX, worldY);
        int localX = CellAddressing.LocalCoord(worldX) / EngineConstants.TempFieldDownscale;
        int localY = CellAddressing.LocalCoord(worldY) / EngineConstants.TempFieldDownscale;
        return (coord, (localY * BlockSize) + localX);
    }

    private sealed class TemperatureBlock
    {
        private float[]? _currentFloat;
        private float[]? _scratchFloat;
        private Half[]? _currentHalf;
        private Half[]? _scratchHalf;

        public TemperatureBlock(TemperatureStorageKind storageKind)
        {
            StorageKind = storageKind;
            if (storageKind == TemperatureStorageKind.Float32)
            {
                _currentFloat = GC.AllocateArray<float>(BlockArea, pinned: true);
                _scratchFloat = GC.AllocateArray<float>(BlockArea, pinned: true);
                return;
            }

            _currentHalf = GC.AllocateArray<Half>(BlockArea, pinned: true);
            _scratchHalf = GC.AllocateArray<Half>(BlockArea, pinned: true);
        }

        public TemperatureStorageKind StorageKind { get; }

        public float[] CurrentFloat => _currentFloat ??
            throw new InvalidOperationException("当前温度块不是 float 存储。");

        public float[] ScratchFloat => _scratchFloat ??
            throw new InvalidOperationException("当前温度块不是 float 存储。");

        public float ReadCurrent(int index)
        {
            return StorageKind == TemperatureStorageKind.Float32
                ? _currentFloat![index]
                : (float)_currentHalf![index];
        }

        public void AddCurrent(int index, float delta)
        {
            if (StorageKind == TemperatureStorageKind.Float32)
            {
                _currentFloat![index] += delta;
                return;
            }

            _currentHalf![index] = (Half)((float)_currentHalf[index] + delta);
        }

        public void WriteCurrent(int index, float value)
        {
            if (StorageKind == TemperatureStorageKind.Float32)
            {
                _currentFloat![index] = value;
                return;
            }

            _currentHalf![index] = (Half)value;
        }

        public void WriteScratch(int index, float value)
        {
            if (StorageKind == TemperatureStorageKind.Float32)
            {
                _scratchFloat![index] = value;
                return;
            }

            _scratchHalf![index] = (Half)value;
        }

        public void CopyCurrentToScratch()
        {
            if (StorageKind == TemperatureStorageKind.Float32)
            {
                _currentFloat!.CopyTo(_scratchFloat!, 0);
                return;
            }

            _currentHalf!.CopyTo(_scratchHalf!, 0);
        }

        /// <summary>
        /// 清除 ping-pong 两侧缓冲，供冷却后的 block 在后续热源注入中安全复用。
        /// </summary>
        public void Reset()
        {
            if (StorageKind == TemperatureStorageKind.Float32)
            {
                _currentFloat.AsSpan().Clear();
                _scratchFloat.AsSpan().Clear();
                return;
            }

            _currentHalf.AsSpan().Clear();
            _scratchHalf.AsSpan().Clear();
        }

        // ping-pong：传导结果在 scratch，帧末交换 current/scratch 指针。
        public void Swap()
        {
            if (StorageKind == TemperatureStorageKind.Float32)
            {
                (_currentFloat, _scratchFloat) = (_scratchFloat, _currentFloat);
                return;
            }

            (_currentHalf, _scratchHalf) = (_scratchHalf, _currentHalf);
        }

        public void CoolTowardAmbient(float ambient, float coolingPerStep, float epsilon)
        {
            if (StorageKind == TemperatureStorageKind.Float32)
            {
                Span<float> current = _currentFloat;
                for (int i = 0; i < current.Length; i++)
                {
                    current[i] = CoolValue(current[i], ambient, coolingPerStep, epsilon);
                }

                return;
            }

            Span<Half> currentHalf = _currentHalf;
            for (int i = 0; i < currentHalf.Length; i++)
            {
                currentHalf[i] = (Half)CoolValue((float)currentHalf[i], ambient, coolingPerStep, epsilon);
            }
        }

        public bool IsAmbient(float ambient, float epsilon)
        {
            if (StorageKind == TemperatureStorageKind.Float32)
            {
                ReadOnlySpan<float> current = _currentFloat;
                for (int i = 0; i < current.Length; i++)
                {
                    if (MathF.Abs(current[i] - ambient) > epsilon)
                    {
                        return false;
                    }
                }

                return true;
            }

            ReadOnlySpan<Half> currentHalf = _currentHalf;
            for (int i = 0; i < currentHalf.Length; i++)
            {
                if (MathF.Abs((float)currentHalf[i] - ambient) > epsilon)
                {
                    return false;
                }
            }

            return true;
        }

        private static float CoolValue(float value, float ambient, float coolingPerStep, float epsilon)
        {
            float delta = value - ambient;
            return MathF.Abs(delta) <= MathF.Max(epsilon, coolingPerStep)
                ? ambient
                : value - (MathF.Sign(delta) * coolingPerStep);
        }
    }
}
