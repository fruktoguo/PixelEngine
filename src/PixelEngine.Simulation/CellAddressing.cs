using System.Runtime.CompilerServices;
using PixelEngine.Core;

namespace PixelEngine.Simulation;

/// <summary>
/// 世界坐标到 chunk 与本地 cell 索引的位运算寻址辅助。
/// </summary>
public static class CellAddressing
{
    private const int LocalMask = EngineConstants.ChunkSize - 1;

    /// <summary>
    /// 将世界坐标转换为 chunk 坐标，负坐标依赖算术右移自然向下取整。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ChunkCoord WorldToChunk(int wx, int wy)
    {
        return new(ChunkOf(wx), ChunkOf(wy));
    }

    /// <summary>
    /// 返回单轴世界坐标所在的 chunk 坐标。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ChunkOf(int world)
    {
        return world >> EngineConstants.ChunkSizeLog2;
    }

    /// <summary>
    /// 返回单轴世界坐标在 chunk 内的本地坐标，范围为 [0,63]。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LocalCoord(int world)
    {
        return world & LocalMask;
    }

    /// <summary>
    /// 返回世界坐标在其所属 chunk 内的一维本地索引，范围为 [0,4095]。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LocalIndex(int wx, int wy)
    {
        return (LocalCoord(wy) << EngineConstants.ChunkSizeLog2) | LocalCoord(wx);
    }

    /// <summary>
    /// 返回本地坐标对应的一维索引。调用方需保证坐标已在 [0,63]。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LocalIndexFromLocal(int lx, int ly)
    {
        return (ly << EngineConstants.ChunkSizeLog2) | lx;
    }
}
