namespace PixelEngine.Simulation;

/// <summary>
/// 提供材质热重载后对驻留权威网格的冷路径统计与 fallback 替换工具。
/// </summary>
public static class MaterialLiveGridRemapper
{
    /// <summary>捕获命中指定材质 ID 的 live cell before-image。</summary>
    /// <param name="chunks">当前 resident chunk 源。</param>
    /// <param name="materialIds">即将被 fallback 替换的材质 IDs。</param>
    /// <returns>只包含命中 cell 与原 WorkingDirty 的游离索引快照。</returns>
    public static MaterialGridRemapSnapshot CaptureReplacementState(
        IChunkSource chunks,
        ReadOnlySpan<ushort> materialIds)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        Chunk[] resident = chunks.ResidentChunks.ToArray();
        if (materialIds.IsEmpty)
        {
            return new MaterialGridRemapSnapshot(chunks, resident, [], [], 0);
        }

        List<MaterialGridRemapSnapshot.ChunkEntry> entries = [];
        int cellCount = 0;
        for (int chunkIndex = 0; chunkIndex < resident.Length; chunkIndex++)
        {
            Chunk chunk = resident[chunkIndex];
            List<int>? indices = null;
            List<ushort>? materials = null;
            List<byte>? damage = null;
            ReadOnlySpan<ushort> chunkMaterials = chunk.Material;
            ReadOnlySpan<byte> chunkDamage = chunk.Damage;
            for (int i = 0; i < chunkMaterials.Length; i++)
            {
                if (!Contains(materialIds, chunkMaterials[i]))
                {
                    continue;
                }

                (indices ??= []).Add(i);
                (materials ??= []).Add(chunkMaterials[i]);
                (damage ??= []).Add(chunkDamage[i]);
                cellCount++;
            }

            if (indices is not null)
            {
                entries.Add(new MaterialGridRemapSnapshot.ChunkEntry(
                    chunk,
                    [.. indices],
                    [.. materials!],
                    [.. damage!],
                    chunk.WorkingDirty));
            }
        }

        return new MaterialGridRemapSnapshot(
            chunks,
            resident,
            materialIds.ToArray(),
            [.. entries],
            cellCount);
    }

    /// <summary>把捕获快照中的 cell 替换为 fallback，并标记受影响 chunk dirty。</summary>
    /// <param name="snapshot">由 <see cref="CaptureReplacementState" /> 创建的 before-image。</param>
    /// <param name="fallbackId">目标 fallback 材质 ID。</param>
    /// <returns>替换 cell 数量。</returns>
    public static int ApplyFallback(MaterialGridRemapSnapshot snapshot, ushort fallbackId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ReadOnlySpan<MaterialGridRemapSnapshot.ChunkEntry> entries = snapshot.Entries;
        for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
        {
            MaterialGridRemapSnapshot.ChunkEntry entry = entries[entryIndex];
            for (int i = 0; i < entry.Indices.Length; i++)
            {
                int index = entry.Indices[i];
                entry.Chunk.SetMaterialAt(index, fallbackId);
                entry.Chunk.DamageBuffer[index] = 0;
            }

            entry.Chunk.SetWorkingDirty(DirtyRect.Full);
        }

        return snapshot.CellCount;
    }

    /// <summary>恢复 fallback 替换前的 cell 材质、Damage 与 WorkingDirty。</summary>
    /// <param name="snapshot">由 <see cref="CaptureReplacementState" /> 创建的 before-image。</param>
    public static void RestoreReplacementState(MaterialGridRemapSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ReadOnlySpan<MaterialGridRemapSnapshot.ChunkEntry> entries = snapshot.Entries;
        for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
        {
            MaterialGridRemapSnapshot.ChunkEntry entry = entries[entryIndex];
            for (int i = 0; i < entry.Indices.Length; i++)
            {
                int index = entry.Indices[i];
                entry.Chunk.SetMaterialAt(index, entry.Materials[i]);
                entry.Chunk.DamageBuffer[index] = entry.Damage[i];
            }

            entry.Chunk.SetWorkingDirty(entry.WorkingDirty);
        }
    }

    /// <summary>判断待 remap cell 与 WorkingDirty 是否仍等于捕获时 before-image。</summary>
    /// <param name="snapshot">稀疏 live-grid before-image。</param>
    /// <returns>全部权威值仍一致时返回 <see langword="true" />。</returns>
    public static bool ReplacementStateEquals(MaterialGridRemapSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!ResidentSetEquals(snapshot, out ReadOnlySpan<Chunk> currentResident))
        {
            return false;
        }

        ReadOnlySpan<MaterialGridRemapSnapshot.ChunkEntry> entries = snapshot.Entries;
        int observedCellCount = 0;
        for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
        {
            MaterialGridRemapSnapshot.ChunkEntry entry = entries[entryIndex];
            if (entry.Chunk.WorkingDirty != entry.WorkingDirty)
            {
                return false;
            }

            for (int i = 0; i < entry.Indices.Length; i++)
            {
                int index = entry.Indices[i];
                if (entry.Chunk.GetMaterialAt(index) != entry.Materials[i] ||
                    entry.Chunk.DamageBuffer[index] != entry.Damage[i])
                {
                    return false;
                }
            }
        }

        ReadOnlySpan<ushort> materialIds = snapshot.MaterialIds;
        for (int chunkIndex = 0; chunkIndex < currentResident.Length; chunkIndex++)
        {
            ReadOnlySpan<ushort> materials = currentResident[chunkIndex].Material;
            for (int cellIndex = 0; cellIndex < materials.Length; cellIndex++)
            {
                if (Contains(materialIds, materials[cellIndex]))
                {
                    observedCellCount++;
                }
            }
        }

        return observedCellCount == snapshot.CellCount;
    }

    /// <summary>判断 live grid 是否仍等于把快照命中 cell 全部替换成 fallback 后的状态。</summary>
    /// <param name="snapshot">稀疏 live-grid before-image。</param>
    /// <param name="fallbackId">提交时使用的 fallback 材质 ID。</param>
    /// <returns>resident 集合、命中 cell、Damage、WorkingDirty 与无残留 tombstone 均一致时为 true。</returns>
    public static bool FallbackStateEquals(MaterialGridRemapSnapshot snapshot, ushort fallbackId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!ResidentSetEquals(snapshot, out ReadOnlySpan<Chunk> currentResident))
        {
            return false;
        }

        ReadOnlySpan<MaterialGridRemapSnapshot.ChunkEntry> entries = snapshot.Entries;
        for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
        {
            MaterialGridRemapSnapshot.ChunkEntry entry = entries[entryIndex];
            if (entry.Chunk.WorkingDirty != DirtyRect.Full)
            {
                return false;
            }

            for (int i = 0; i < entry.Indices.Length; i++)
            {
                int index = entry.Indices[i];
                if (entry.Chunk.GetMaterialAt(index) != fallbackId ||
                    entry.Chunk.DamageBuffer[index] != 0)
                {
                    return false;
                }
            }
        }

        ReadOnlySpan<ushort> materialIds = snapshot.MaterialIds;
        for (int chunkIndex = 0; chunkIndex < currentResident.Length; chunkIndex++)
        {
            ReadOnlySpan<ushort> materials = currentResident[chunkIndex].Material;
            for (int cellIndex = 0; cellIndex < materials.Length; cellIndex++)
            {
                if (Contains(materialIds, materials[cellIndex]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// 统计当前驻留 chunk 中每个材质 id 的 live cell 数量。
    /// </summary>
    public static int[] CountResidentCellsByMaterial(IChunkSource chunks, int materialCount)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentOutOfRangeException.ThrowIfNegative(materialCount);
        int[] counts = new int[materialCount];
        ReadOnlySpan<Chunk> resident = chunks.ResidentChunks;
        for (int chunkIndex = 0; chunkIndex < resident.Length; chunkIndex++)
        {
            ReadOnlySpan<ushort> material = resident[chunkIndex].Material;
            for (int i = 0; i < material.Length; i++)
            {
                ushort id = material[i];
                if (id < counts.Length)
                {
                    counts[id]++;
                }
            }
        }

        return counts;
    }

    /// <summary>
    /// 将驻留 chunk 中命中 tombstone id 的 cell 原地替换为 fallback，并把受影响 chunk 标记 dirty。
    /// </summary>
    public static int ReplaceResidentMaterials(
        IChunkSource chunks,
        ReadOnlySpan<ushort> materialIds,
        ushort fallbackId)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        if (materialIds.IsEmpty)
        {
            return 0;
        }

        MaterialGridRemapSnapshot snapshot = CaptureReplacementState(chunks, materialIds);
        return ApplyFallback(snapshot, fallbackId);
    }

    private static bool Contains(ReadOnlySpan<ushort> values, ushort value)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == value)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ResidentSetEquals(
        MaterialGridRemapSnapshot snapshot,
        out ReadOnlySpan<Chunk> currentResident)
    {
        currentResident = snapshot.Chunks.ResidentChunks;
        ReadOnlySpan<Chunk> capturedResident = snapshot.ResidentChunks;
        if (currentResident.Length != capturedResident.Length)
        {
            return false;
        }

        for (int i = 0; i < currentResident.Length; i++)
        {
            if (!ReferenceEquals(currentResident[i], capturedResident[i]))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>材质 fallback 替换前的稀疏 live-grid before-image。</summary>
public sealed class MaterialGridRemapSnapshot
{
    private readonly Chunk[] _residentChunks;
    private readonly ushort[] _materialIds;
    private readonly ChunkEntry[] _entries;

    internal MaterialGridRemapSnapshot(
        IChunkSource chunks,
        Chunk[] residentChunks,
        ushort[] materialIds,
        ChunkEntry[] entries,
        int cellCount)
    {
        Chunks = chunks;
        _residentChunks = residentChunks;
        _materialIds = materialIds;
        _entries = entries;
        CellCount = cellCount;
    }

    /// <summary>快照覆盖的 cell 数量。</summary>
    public int CellCount { get; }

    internal IChunkSource Chunks { get; }

    internal ReadOnlySpan<Chunk> ResidentChunks => _residentChunks;

    internal ReadOnlySpan<ushort> MaterialIds => _materialIds;

    internal ReadOnlySpan<ChunkEntry> Entries => _entries;

    internal sealed record ChunkEntry(
        Chunk Chunk,
        int[] Indices,
        ushort[] Materials,
        byte[] Damage,
        DirtyRect WorkingDirty);
}
