using PixelEngine.Core;

namespace PixelEngine.Simulation;

/// <summary>
/// 提供材质热重载后对驻留权威网格的冷路径统计与 fallback 替换工具。
/// </summary>
public static class MaterialLiveGridRemapper
{
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

        int replaced = 0;
        ReadOnlySpan<Chunk> resident = chunks.ResidentChunks;
        for (int chunkIndex = 0; chunkIndex < resident.Length; chunkIndex++)
        {
            Chunk chunk = resident[chunkIndex];
            bool chunkDirty = false;
            Span<ushort> material = chunk.Material;
            for (int i = 0; i < material.Length; i++)
            {
                if (!Contains(materialIds, material[i]))
                {
                    continue;
                }

                material[i] = fallbackId;
                chunk.Damage[i] = 0;
                replaced++;
                chunkDirty = true;
            }

            if (chunkDirty)
            {
                chunk.SetWorkingDirty(new DirtyRect(0, 0, EngineConstants.ChunkSize - 1, EngineConstants.ChunkSize - 1));
            }
        }

        return replaced;
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
}
