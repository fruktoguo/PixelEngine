using System.Buffers;
using Xunit;

namespace PixelEngine.Serialization.Tests;

/// <summary>
/// 验证独立 RLE 与 LZ4 block 基础编解码契约。
/// </summary>
public sealed class RleAndLz4CodecTests
{
    /// <summary>
    /// 均匀 U16 数据应能 RLE 往返，并压缩为单条行程。
    /// </summary>
    [Fact]
    public void RleU16RoundTripsUniformData()
    {
        ushort[] source = CreateRepeatedU16(4096, 42);
        ArrayBufferWriter<byte> writer = new();

        RleCodec.EncodeU16(source, writer);

        ushort[] destination = new ushort[source.Length];
        int written = RleCodec.DecodeU16(writer.WrittenSpan, destination);

        Assert.Equal(source.Length, written);
        Assert.Equal(4, writer.WrittenCount);
        Assert.Equal(source, destination);
    }

    /// <summary>
    /// 交替 U16 数据应能在最坏行程分布下完整往返。
    /// </summary>
    [Fact]
    public void RleU16RoundTripsAlternatingData()
    {
        ushort[] source = CreateAlternatingU16(4096, 7, 13);
        ArrayBufferWriter<byte> writer = new();

        RleCodec.EncodeU16(source, writer);

        ushort[] destination = new ushort[source.Length];
        int written = RleCodec.DecodeU16(writer.WrittenSpan, destination);

        Assert.Equal(source.Length, written);
        Assert.Equal(source, destination);
    }

    /// <summary>
    /// 均匀 U8 数据应能 RLE 往返，并压缩为单条行程。
    /// </summary>
    [Fact]
    public void RleU8RoundTripsUniformData()
    {
        byte[] source = CreateRepeatedU8(4096, 0xA5);
        ArrayBufferWriter<byte> writer = new();

        RleCodec.EncodeU8(source, writer);

        byte[] destination = new byte[source.Length];
        int written = RleCodec.DecodeU8(writer.WrittenSpan, destination);

        Assert.Equal(source.Length, written);
        Assert.Equal(3, writer.WrittenCount);
        Assert.Equal(source, destination);
    }

    /// <summary>
    /// 交替 U8 数据应能在最坏行程分布下完整往返。
    /// </summary>
    [Fact]
    public void RleU8RoundTripsAlternatingData()
    {
        byte[] source = CreateAlternatingU8(4096, 0x11, 0xEE);
        ArrayBufferWriter<byte> writer = new();

        RleCodec.EncodeU8(source, writer);

        byte[] destination = new byte[source.Length];
        int written = RleCodec.DecodeU8(writer.WrittenSpan, destination);

        Assert.Equal(source.Length, written);
        Assert.Equal(source, destination);
    }

    /// <summary>
    /// RLE 解码目标容量不足时应抛出明确异常，而不是静默截断。
    /// </summary>
    [Fact]
    public void RleDecodeThrowsWhenDestinationIsTooSmall()
    {
        byte[] source = [1, 2, 3, 4, 5, 6, 7, 8];
        ArrayBufferWriter<byte> writer = new();
        RleCodec.EncodeU8(source, writer);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => RleCodec.DecodeU8(writer.WrittenSpan, new byte[source.Length - 1]));

        Assert.Contains("容量不足", exception.Message);
    }

    /// <summary>
    /// LZ4 block 应能往返数据，并明确返回写入字节数与消费字节数。
    /// </summary>
    [Fact]
    public void Lz4BlockRoundTripsAndReportsWrittenAndConsumedBytes()
    {
        byte[] source = CreatePattern(8192);
        ArrayBufferWriter<byte> writer = new();

        Lz4BlockCodec.Compress(source, writer);

        byte[] destination = new byte[source.Length];
        int written = Lz4BlockCodec.Decompress(writer.WrittenSpan, destination, out int consumed);

        Assert.Equal(source.Length, written);
        Assert.Equal(writer.WrittenCount, consumed);
        Assert.Equal(source, destination);
    }

    /// <summary>
    /// LZ4 解压目标容量不足时应抛出明确异常，而不是触发底层库的不透明错误。
    /// </summary>
    [Fact]
    public void Lz4BlockDecompressThrowsWhenDestinationIsTooSmall()
    {
        byte[] source = CreatePattern(1024);
        ArrayBufferWriter<byte> writer = new();
        Lz4BlockCodec.Compress(source, writer);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => Lz4BlockCodec.Decompress(writer.WrittenSpan, new byte[source.Length - 1]));

        Assert.Contains("容量不足", exception.Message);
    }

    private static ushort[] CreateRepeatedU16(int length, ushort value)
    {
        ushort[] data = new ushort[length];
        Array.Fill(data, value);
        return data;
    }

    private static ushort[] CreateAlternatingU16(int length, ushort even, ushort odd)
    {
        ushort[] data = new ushort[length];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (i & 1) == 0 ? even : odd;
        }

        return data;
    }

    private static byte[] CreateRepeatedU8(int length, byte value)
    {
        byte[] data = new byte[length];
        Array.Fill(data, value);
        return data;
    }

    private static byte[] CreateAlternatingU8(int length, byte even, byte odd)
    {
        byte[] data = new byte[length];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (i & 1) == 0 ? even : odd;
        }

        return data;
    }

    private static byte[] CreatePattern(int length)
    {
        byte[] data = new byte[length];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)((i * 31) ^ (i >> 3));
        }

        return data;
    }
}
