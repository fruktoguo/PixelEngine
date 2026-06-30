using System.Buffers;
using System.Buffers.Binary;
using K4os.Compression.LZ4;

namespace PixelEngine.Serialization;

/// <summary>
/// 封装 K4os LZ4 block，提供带长度头的独立压缩块编解码。
/// </summary>
/// <remarks>
/// 块格式固定为小端：<c>int uncompressedLength</c>、<c>int compressedLength</c>、
/// <c>compressedLength</c> 字节 LZ4 payload。显式长度便于上层在读 chunk blob 时
/// 预分配目标缓冲，并能在连续字节流中确定单个 block 的消费长度。
/// </remarks>
public static class Lz4BlockCodec
{
    /// <summary>
    /// 获取 LZ4 block 头部字节数。
    /// </summary>
    public const int HeaderSize = sizeof(int) + sizeof(int);

    /// <summary>
    /// 将源字节压缩为带长度头的 LZ4 block。
    /// </summary>
    /// <param name="source">待压缩的未压缩字节。</param>
    /// <param name="writer">接收 block 字节的目标写入器。</param>
    public static void Compress(ReadOnlySpan<byte> source, IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (source.IsEmpty)
        {
            Span<byte> emptyHeader = writer.GetSpan(HeaderSize);
            BinaryPrimitives.WriteInt32LittleEndian(emptyHeader[..sizeof(int)], 0);
            BinaryPrimitives.WriteInt32LittleEndian(emptyHeader.Slice(sizeof(int), sizeof(int)), 0);
            writer.Advance(HeaderSize);
            return;
        }

        int maxCompressedLength = LZ4Codec.MaximumOutputSize(source.Length);
        Span<byte> span = writer.GetSpan(HeaderSize + maxCompressedLength);
        Span<byte> payload = span.Slice(HeaderSize, maxCompressedLength);
        int compressedLength = LZ4Codec.Encode(source, payload, LZ4Level.L00_FAST);
        if (compressedLength < 0)
        {
            throw new InvalidOperationException("LZ4 压缩失败：目标缓冲容量不足。");
        }

        BinaryPrimitives.WriteInt32LittleEndian(span[..sizeof(int)], source.Length);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(sizeof(int), sizeof(int)), compressedLength);
        writer.Advance(HeaderSize + compressedLength);
    }

    /// <summary>
    /// 解压一个带长度头的 LZ4 block。
    /// </summary>
    /// <param name="source">包含完整 block 的源字节。</param>
    /// <param name="destination">接收未压缩字节的目标缓冲。</param>
    /// <returns>实际写入 <paramref name="destination"/> 的字节数。</returns>
    /// <exception cref="InvalidDataException">block 头部或 payload 损坏。</exception>
    /// <exception cref="ArgumentException">目标缓冲容量不足，无法容纳未压缩内容。</exception>
    public static int Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        return Decompress(source, destination, out _);
    }

    /// <summary>
    /// 解压一个带长度头的 LZ4 block，并返回从源字节中消费的 block 长度。
    /// </summary>
    /// <param name="source">包含完整 block 的源字节。</param>
    /// <param name="destination">接收未压缩字节的目标缓冲。</param>
    /// <param name="bytesConsumed">成功解压后从 <paramref name="source"/> 消费的字节数。</param>
    /// <returns>实际写入 <paramref name="destination"/> 的字节数。</returns>
    /// <exception cref="InvalidDataException">block 头部或 payload 损坏。</exception>
    /// <exception cref="ArgumentException">目标缓冲容量不足，无法容纳未压缩内容。</exception>
    public static int Decompress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesConsumed)
    {
        bytesConsumed = 0;
        if (source.Length < HeaderSize)
        {
            throw new InvalidDataException("LZ4 block 头部不完整。");
        }

        int uncompressedLength = BinaryPrimitives.ReadInt32LittleEndian(source[..sizeof(int)]);
        int compressedLength = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(sizeof(int), sizeof(int)));
        if (uncompressedLength < 0 || compressedLength < 0)
        {
            throw new InvalidDataException("LZ4 block 长度字段不能为负数。");
        }

        if (compressedLength > source.Length - HeaderSize)
        {
            throw new InvalidDataException("LZ4 block payload 长度超过可用源字节。");
        }

        if (uncompressedLength > destination.Length)
        {
            throw new ArgumentException("目标 byte 缓冲容量不足，无法容纳 LZ4 解压结果。", nameof(destination));
        }

        if (uncompressedLength == 0)
        {
            if (compressedLength != 0)
            {
                throw new InvalidDataException("空 LZ4 block 不应包含 payload。");
            }

            bytesConsumed = HeaderSize;
            return 0;
        }

        ReadOnlySpan<byte> payload = source.Slice(HeaderSize, compressedLength);
        int decodedLength = LZ4Codec.Decode(payload, destination[..uncompressedLength]);
        if (decodedLength != uncompressedLength)
        {
            throw new InvalidDataException("LZ4 block 解压长度与头部声明不一致。");
        }

        bytesConsumed = HeaderSize + compressedLength;
        return decodedLength;
    }
}
