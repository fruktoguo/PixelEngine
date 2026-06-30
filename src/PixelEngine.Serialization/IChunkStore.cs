using System.Buffers;
using PixelEngine.Simulation;

namespace PixelEngine.Serialization;

/// <summary>
/// 按 chunk 坐标读写序列化 blob 的持久化存储接口。
/// </summary>
public interface IChunkStore
{
    /// <summary>
    /// 尝试读取指定 chunk 的二进制 blob。
    /// </summary>
    /// <param name="coord">chunk 坐标。</param>
    /// <param name="destination">接收 blob 字节的缓冲写入器。</param>
    /// <returns>存在并成功写入 <paramref name="destination"/> 时为 true；不存在时为 false。</returns>
    bool TryRead(ChunkCoord coord, IBufferWriter<byte> destination);

    /// <summary>
    /// 写入或覆盖指定 chunk 的二进制 blob。
    /// </summary>
    /// <param name="coord">chunk 坐标。</param>
    /// <param name="blob">待持久化的 chunk blob。</param>
    void Write(ChunkCoord coord, ReadOnlySpan<byte> blob);

    /// <summary>
    /// 判断指定 chunk 是否已有持久化 blob。
    /// </summary>
    /// <param name="coord">chunk 坐标。</param>
    /// <returns>存在时为 true；不存在时为 false。</returns>
    bool Exists(ChunkCoord coord);

    /// <summary>
    /// 删除指定 chunk 的持久化 blob；目标不存在时不产生副作用。
    /// </summary>
    /// <param name="coord">chunk 坐标。</param>
    void Delete(ChunkCoord coord);
}
