using PixelEngine.Core;
using System.Numerics;
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
    /// <summary>
    /// 单 chunk 温度子块边长。
    /// </summary>
    public const int BlockSize = EngineConstants.ChunkSize / EngineConstants.TempFieldDownscale;

    /// <summary>
    /// 单 chunk 温度子块 cell 数。
    /// </summary>
    public const int BlockArea = BlockSize * BlockSize;

    private readonly Dictionary<ChunkCoord, TemperatureBlock> _blocks = [];

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
    /// 温度子块存储格式。
    /// </summary>
    public TemperatureStorageKind StorageKind { get; }

    private bool EnableSimd { get; }

    /// <summary>
    /// 当前运行时是否具备 Vector SIMD 加速能力。
    /// </summary>
    public bool SimdAvailable => EnableSimd &&
        StorageKind == TemperatureStorageKind.Float32 &&
        Vector.IsHardwareAccelerated &&
        (Vector512.IsHardwareAccelerated || Vector<float>.Count > 1);

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
    /// 执行一次 5-point von Neumann 热传导。
    /// </summary>
    public void ConductStep(IChunkSource chunks, MaterialHotTable materials, uint frameIndex = 0, uint worldSeed = 0)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(materials);
        if (ContactFireOnly)
        {
            return;
        }

        foreach (Chunk chunk in chunks.ResidentChunks)
        {
            _ = GetOrCreateBlock(chunk.Coord);
        }

        foreach (Chunk chunk in chunks.ResidentChunks)
        {
            TemperatureBlock block = GetOrCreateBlock(chunk.Coord);
            ConductChunk(chunk, block, materials, frameIndex, worldSeed);
        }

        foreach (TemperatureBlock block in _blocks.Values)
        {
            block.Swap();
        }
    }

    /// <summary>
    /// 对活跃 chunk 应用 melt/freeze/boil 阈值相变。
    /// </summary>
    public void ApplyPhaseTransitions(IChunkSource chunks, MaterialTable materials, byte parityBit)
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
            DirtyRect rect = chunk.CurrentDirty.IsEmpty ? DirtyRect.Full : chunk.CurrentDirty;
            for (int ly = rect.MinY; ly <= rect.MaxY; ly++)
            {
                int wy = baseY + ly;
                for (int lx = rect.MinX; lx <= rect.MaxX; lx++)
                {
                    int local = CellAddressing.LocalIndexFromLocal(lx, ly);
                    ushort material = chunk.Material[local];
                    if (material == 0 || CellFlags.MatchesFrame(chunk.Flags[local], parityBit))
                    {
                        continue;
                    }

                    float temp = GetTemperature(baseX + lx, wy);
                    if (!TryPhaseTarget(hot, material, temp, out ushort target))
                    {
                        continue;
                    }

                    chunk.Material[local] = target;
                    chunk.Lifetime[local] = DefaultLifetimeByte(hot, target);
                    chunk.Flags[local] = CellFlags.SetParity(chunk.Flags[local], parityBit);
                    DirtyRegionMarker.MarkCell(chunks, baseX + lx, wy, DirtyPhaseTarget.Working, includeBoundaryNeighbors: true);
                }
            }
        }
    }

    private void ConductChunk(
        Chunk chunk,
        TemperatureBlock block,
        MaterialHotTable materials,
        uint frameIndex,
        uint worldSeed)
    {
        int worldBaseX = chunk.Coord.X << EngineConstants.ChunkSizeLog2;
        int worldBaseY = chunk.Coord.Y << EngineConstants.ChunkSizeLog2;
        Span<byte> conductChanceRow = stackalloc byte[BlockSize];
        Span<float> conductRow = stackalloc float[BlockSize];
        Span<float> capacityRow = stackalloc float[BlockSize];
        for (int ty = 0; ty < BlockSize; ty++)
        {
            int row = ty * BlockSize;
            for (int tx = 0; tx < BlockSize; tx++)
            {
                conductChanceRow[tx] = AverageHeatConduct(chunk, materials, tx, ty);
                conductRow[tx] = conductChanceRow[tx] == byte.MaxValue ? 1f : 0f;
                capacityRow[tx] = MathF.Max(AverageHeatCapacity(chunk, materials, tx, ty), 0.0001f);
            }

            if (SimdAvailable && ty > 0 && ty < BlockSize - 1 && CanVectorizeConductRow(conductChanceRow))
            {
                ConductCellScalar(block, conductChanceRow, capacityRow, worldBaseX, worldBaseY, 0, ty, frameIndex, worldSeed);
                int nextScalar = ConductInteriorRowVectorized(block, conductRow, capacityRow, row);
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

    private static int ConductInteriorRowVectorized(
        TemperatureBlock block,
        ReadOnlySpan<float> conductRow,
        ReadOnlySpan<float> capacityRow,
        int row)
    {
        int width = Vector<float>.Count;
        int tx = 1;
        for (; tx <= BlockSize - 1 - width; tx += width)
        {
            float[] current = block.CurrentFloat;
            float[] scratch = block.ScratchFloat;
            Vector<float> center = new(current, row + tx);
            Vector<float> left = new(current, row + tx - 1);
            Vector<float> right = new(current, row + tx + 1);
            Vector<float> up = new(current, row - BlockSize + tx);
            Vector<float> down = new(current, row + BlockSize + tx);
            Vector<float> conduct = new(conductRow.Slice(tx, width));
            Vector<float> capacity = new(capacityRow.Slice(tx, width));
            Vector<float> neighborAverage = (left + right + up + down) * new Vector<float>(0.25f);
            Vector<float> result = center + ((neighborAverage - center) * conduct / capacity);
            result.CopyTo(scratch.AsSpan(row + tx, width));
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
                ushort material = chunk.Material[CellAddressing.LocalIndexFromLocal(startX + x, startY + y)];
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
                ushort material = chunk.Material[CellAddressing.LocalIndexFromLocal(startX + x, startY + y)];
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

        block = new TemperatureBlock(StorageKind);
        _blocks.Add(coord, block);
        return block;
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

        public void Swap()
        {
            if (StorageKind == TemperatureStorageKind.Float32)
            {
                (_currentFloat, _scratchFloat) = (_scratchFloat, _currentFloat);
                return;
            }

            (_currentHalf, _scratchHalf) = (_scratchHalf, _currentHalf);
        }
    }
}
