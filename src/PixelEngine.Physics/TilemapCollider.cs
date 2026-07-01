using PixelEngine.Core;
using PixelEngine.Core.Mathematics;
using PixelEngine.Simulation;

namespace PixelEngine.Physics;

/// <summary>
/// 静态地形 collider 的粗粒度 tilemap 回退构造器。
/// </summary>
public static class TilemapCollider
{
    /// <summary>
    /// 将 chunk 内非空且非 <see cref="CellFlags.RigidOwned"/> 的连续横向 cell run 输出为世界坐标矩形。
    /// </summary>
    /// <param name="chunk">源 chunk。</param>
    /// <param name="destination">输出矩形缓冲。</param>
    /// <returns>写入矩形数量。</returns>
    public static int BuildRowRunRects(Chunk chunk, Span<RectI> destination)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        int written = 0;
        int baseX = chunk.Coord.X << EngineConstants.ChunkSizeLog2;
        int baseY = chunk.Coord.Y << EngineConstants.ChunkSizeLog2;
        for (int y = 0; y < EngineConstants.ChunkSize; y++)
        {
            int runStart = -1;
            for (int x = 0; x <= EngineConstants.ChunkSize; x++)
            {
                bool solid = x < EngineConstants.ChunkSize && IsTerrainSolid(chunk, x, y);
                if (solid && runStart < 0)
                {
                    runStart = x;
                    continue;
                }

                if (solid || runStart < 0)
                {
                    continue;
                }

                if (written >= destination.Length)
                {
                    throw new ArgumentException("destination 缓冲不足。", nameof(destination));
                }

                destination[written++] = RectI.FromBounds(baseX + runStart, baseY + y, baseX + x, baseY + y + 1);
                runStart = -1;
            }
        }

        return written;
    }

    private static bool IsTerrainSolid(Chunk chunk, int x, int y)
    {
        int index = CellAddressing.LocalIndexFromLocal(x, y);
        return chunk.Material[index] != 0 && !CellFlags.Has(chunk.Flags[index], CellFlags.RigidOwned);
    }
}
