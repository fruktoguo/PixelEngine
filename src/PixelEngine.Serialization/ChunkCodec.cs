using System.Buffers;
using System.Runtime.InteropServices;

namespace PixelEngine.Serialization;

/// <summary>
/// chunk 二进制编解码器：RLE 分段后整体 LZ4 block 压缩。
/// </summary>
public sealed class ChunkCodec
{
    /// <summary>
    /// 编码 chunk 快照。
    /// </summary>
    public void Encode(ChunkSnapshot snapshot, IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        PooledByteBufferWriter payloadWriter = new();
        try
        {
            Encode(snapshot, writer, payloadWriter);
        }
        finally
        {
            payloadWriter.Dispose();
        }
    }

    /// <summary>
    /// 使用调用方提供的 payload staging buffer 编码 chunk 快照。
    /// </summary>
    public void Encode(ChunkSnapshot snapshot, IBufferWriter<byte> writer, PooledByteBufferWriter payloadWriter)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(payloadWriter);

        payloadWriter.Clear();
        RleCodec.EncodeU16(snapshot.Material, payloadWriter);
        int materialBytes = payloadWriter.WrittenCount;

        byte[] persistentFlags = ArrayPool<byte>.Shared.Rent(snapshot.Flags.Length);
        try
        {
            Span<byte> flags = persistentFlags.AsSpan(0, snapshot.Flags.Length);
            snapshot.Flags.CopyTo(flags);
            PersistentCellFlags.StripTransientInPlace(flags);
            RleCodec.EncodeU8(flags, payloadWriter);
            int flagsBytes = payloadWriter.WrittenCount - materialBytes;

            RleCodec.EncodeU8(snapshot.Lifetime, payloadWriter);
            int lifetimeBytes = payloadWriter.WrittenCount - materialBytes - flagsBytes;

            WriteTemperature(snapshot.Temperature, payloadWriter);
            int temperatureBytes = payloadWriter.WrittenCount - materialBytes - flagsBytes - lifetimeBytes;
            ChunkBlobHeader header = new(
                SaveFormatVersions.ChunkBlob,
                snapshot.Coord,
                materialBytes,
                flagsBytes,
                lifetimeBytes,
                temperatureBytes,
                payloadWriter.WrittenCount);

            Span<byte> headerSpan = writer.GetSpan(ChunkBlobHeader.Size);
            header.Write(headerSpan);
            writer.Advance(ChunkBlobHeader.Size);
            Lz4BlockCodec.Compress(payloadWriter.WrittenSpan, writer);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(persistentFlags);
        }
    }

    /// <summary>
    /// 解码 chunk blob 到目标快照。
    /// </summary>
    public void Decode(ReadOnlySpan<byte> source, ChunkSnapshot destination, byte currentParityBit)
    {
        ChunkBlobHeader header = ChunkBlobHeader.Read(source);
        if (header.Coord != destination.Coord)
        {
            throw new InvalidDataException("chunk blob 坐标与目标快照坐标不一致。");
        }

        byte[] payload = ArrayPool<byte>.Shared.Rent(header.UncompressedPayloadBytes);
        try
        {
            int written = Lz4BlockCodec.Decompress(
                source[ChunkBlobHeader.Size..],
                payload.AsSpan(0, header.UncompressedPayloadBytes),
                out _);
            if (written != header.UncompressedPayloadBytes)
            {
                throw new InvalidDataException("chunk blob payload 解压长度不匹配。");
            }

            ReadOnlySpan<byte> payloadSpan = payload.AsSpan(0, written);
            int offset = 0;
            int materialWritten = RleCodec.DecodeU16(payloadSpan.Slice(offset, header.MaterialRleBytes), destination.Material);
            if (materialWritten != destination.Material.Length)
            {
                throw new InvalidDataException("Material RLE 解码长度不等于 ChunkArea。");
            }

            offset += header.MaterialRleBytes;
            int flagsWritten = RleCodec.DecodeU8(payloadSpan.Slice(offset, header.FlagsRleBytes), destination.Flags);
            if (flagsWritten != destination.Flags.Length)
            {
                throw new InvalidDataException("Flags RLE 解码长度不等于 ChunkArea。");
            }

            PersistentCellFlags.ResetTransientInPlace(destination.Flags, currentParityBit);
            offset += header.FlagsRleBytes;
            int lifetimeWritten = RleCodec.DecodeU8(payloadSpan.Slice(offset, header.LifetimeRleBytes), destination.Lifetime);
            if (lifetimeWritten != destination.Lifetime.Length)
            {
                throw new InvalidDataException("Lifetime RLE 解码长度不等于 ChunkArea。");
            }

            offset += header.LifetimeRleBytes;
            ReadTemperature(payloadSpan.Slice(offset, header.TemperatureBytes), destination.Temperature);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(payload);
        }
    }

    private static void WriteTemperature(ReadOnlySpan<Half> temperature, IBufferWriter<byte> writer)
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(temperature);
        Span<byte> destination = writer.GetSpan(bytes.Length);
        bytes.CopyTo(destination);
        writer.Advance(bytes.Length);
    }

    private static void ReadTemperature(ReadOnlySpan<byte> source, Span<Half> destination)
    {
        if (source.Length != destination.Length * 2)
        {
            throw new InvalidDataException("Temperature 段长度与目标温度子块不匹配。");
        }

        ReadOnlySpan<Half> values = MemoryMarshal.Cast<byte, Half>(source);
        values.CopyTo(destination);
    }
}
