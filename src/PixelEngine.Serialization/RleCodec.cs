using System.Buffers;
using System.Buffers.Binary;

namespace PixelEngine.Serialization;

/// <summary>
/// 提供面向 chunk 热数据的定长小端 RLE 编解码。
/// </summary>
/// <remarks>
/// 编码格式为连续行程记录：U16 序列写入 <c>ushort runLength</c> + <c>ushort value</c>；
/// U8 序列写入 <c>ushort runLength</c> + <c>byte value</c>。超出 <see cref="ushort.MaxValue"/>
/// 的长行程会被拆成多条记录，避免引入可变长整数解析成本。
/// </remarks>
public static class RleCodec
{
    private const int U16RecordSize = sizeof(ushort) + sizeof(ushort);
    private const int U8RecordSize = sizeof(ushort) + sizeof(byte);

    /// <summary>
    /// 将 <see cref="ushort"/> 序列编码为小端 RLE 字节流。
    /// </summary>
    /// <param name="source">待编码的源值序列。</param>
    /// <param name="writer">接收编码字节的目标写入器。</param>
    public static void EncodeU16(ReadOnlySpan<ushort> source, IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (source.IsEmpty)
        {
            return;
        }

        ushort value = source[0];
        int runLength = 1;

        for (int i = 1; i < source.Length; i++)
        {
            ushort current = source[i];
            if (current == value && runLength < ushort.MaxValue)
            {
                runLength++;
                continue;
            }

            WriteU16Run(writer, checked((ushort)runLength), value);
            value = current;
            runLength = 1;
        }

        WriteU16Run(writer, checked((ushort)runLength), value);
    }

    /// <summary>
    /// 将小端 RLE 字节流解码为 <see cref="ushort"/> 序列。
    /// </summary>
    /// <param name="source">由 <see cref="EncodeU16"/> 生成的编码字节。</param>
    /// <param name="destination">接收解码值的目标缓冲。</param>
    /// <returns>实际写入 <paramref name="destination"/> 的元素数量。</returns>
    /// <exception cref="InvalidDataException">编码字节结构不完整或包含 0 长度行程。</exception>
    /// <exception cref="ArgumentException">目标缓冲容量不足，无法容纳编码内容。</exception>
    public static int DecodeU16(ReadOnlySpan<byte> source, Span<ushort> destination)
    {
        if (source.Length % U16RecordSize != 0)
        {
            throw new InvalidDataException("U16 RLE 数据长度不是 4 字节记录的整数倍。");
        }

        int written = 0;
        for (int offset = 0; offset < source.Length; offset += U16RecordSize)
        {
            int runLength = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset, sizeof(ushort)));
            if (runLength == 0)
            {
                throw new InvalidDataException("U16 RLE 数据包含 0 长度行程。");
            }

            if (runLength > destination.Length - written)
            {
                throw new ArgumentException("目标 ushort 缓冲容量不足，无法容纳 RLE 解码结果。", nameof(destination));
            }

            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset + sizeof(ushort), sizeof(ushort)));
            destination.Slice(written, runLength).Fill(value);
            written += runLength;
        }

        return written;
    }

    /// <summary>
    /// 将 <see cref="byte"/> 序列编码为小端 RLE 字节流。
    /// </summary>
    /// <param name="source">待编码的源值序列。</param>
    /// <param name="writer">接收编码字节的目标写入器。</param>
    public static void EncodeU8(ReadOnlySpan<byte> source, IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (source.IsEmpty)
        {
            return;
        }

        byte value = source[0];
        int runLength = 1;

        for (int i = 1; i < source.Length; i++)
        {
            byte current = source[i];
            if (current == value && runLength < ushort.MaxValue)
            {
                runLength++;
                continue;
            }

            WriteU8Run(writer, checked((ushort)runLength), value);
            value = current;
            runLength = 1;
        }

        WriteU8Run(writer, checked((ushort)runLength), value);
    }

    /// <summary>
    /// 将小端 RLE 字节流解码为 <see cref="byte"/> 序列。
    /// </summary>
    /// <param name="source">由 <see cref="EncodeU8"/> 生成的编码字节。</param>
    /// <param name="destination">接收解码值的目标缓冲。</param>
    /// <returns>实际写入 <paramref name="destination"/> 的元素数量。</returns>
    /// <exception cref="InvalidDataException">编码字节结构不完整或包含 0 长度行程。</exception>
    /// <exception cref="ArgumentException">目标缓冲容量不足，无法容纳编码内容。</exception>
    public static int DecodeU8(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (source.Length % U8RecordSize != 0)
        {
            throw new InvalidDataException("U8 RLE 数据长度不是 3 字节记录的整数倍。");
        }

        int written = 0;
        for (int offset = 0; offset < source.Length; offset += U8RecordSize)
        {
            int runLength = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset, sizeof(ushort)));
            if (runLength == 0)
            {
                throw new InvalidDataException("U8 RLE 数据包含 0 长度行程。");
            }

            if (runLength > destination.Length - written)
            {
                throw new ArgumentException("目标 byte 缓冲容量不足，无法容纳 RLE 解码结果。", nameof(destination));
            }

            byte value = source[offset + sizeof(ushort)];
            destination.Slice(written, runLength).Fill(value);
            written += runLength;
        }

        return written;
    }

    private static void WriteU16Run(IBufferWriter<byte> writer, ushort runLength, ushort value)
    {
        Span<byte> span = writer.GetSpan(U16RecordSize);
        BinaryPrimitives.WriteUInt16LittleEndian(span[..sizeof(ushort)], runLength);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(sizeof(ushort), sizeof(ushort)), value);
        writer.Advance(U16RecordSize);
    }

    private static void WriteU8Run(IBufferWriter<byte> writer, ushort runLength, byte value)
    {
        Span<byte> span = writer.GetSpan(U8RecordSize);
        BinaryPrimitives.WriteUInt16LittleEndian(span[..sizeof(ushort)], runLength);
        span[sizeof(ushort)] = value;
        writer.Advance(U8RecordSize);
    }
}
