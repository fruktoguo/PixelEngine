using System.Buffers.Binary;
using PixelEngine.Simulation;

namespace PixelEngine.Serialization;

/// <summary>
/// chunk blob 文件头。
/// </summary>
public readonly record struct ChunkBlobHeader(
    int FormatVersion,
    ChunkCoord Coord,
    int MaterialRleBytes,
    int FlagsRleBytes,
    int LifetimeRleBytes,
    int TemperatureBytes,
    int UncompressedPayloadBytes)
{
    /// <summary>
    /// chunk blob 魔数，ASCII "PECH"。
    /// </summary>
    public const uint Magic = 0x4843_4550u;

    /// <summary>
    /// 头部字节数。
    /// </summary>
    public const int Size = sizeof(uint) + (sizeof(int) * 8);

    /// <summary>
    /// 写入 chunk blob 头部。
    /// </summary>
    public void Write(Span<byte> destination)
    {
        if (destination.Length < Size)
        {
            throw new ArgumentException("目标缓冲不足以写入 chunk blob header。", nameof(destination));
        }

        BinaryPrimitives.WriteUInt32LittleEndian(destination[..sizeof(uint)], Magic);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(4, 4), FormatVersion);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(8, 4), Coord.X);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(12, 4), Coord.Y);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(16, 4), MaterialRleBytes);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(20, 4), FlagsRleBytes);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(24, 4), LifetimeRleBytes);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(28, 4), TemperatureBytes);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(32, 4), UncompressedPayloadBytes);
    }

    /// <summary>
    /// 从字节读取 chunk blob 头部。
    /// </summary>
    public static ChunkBlobHeader Read(ReadOnlySpan<byte> source)
    {
        if (source.Length < Size)
        {
            throw new InvalidDataException("chunk blob header 不完整。");
        }

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(source[..sizeof(uint)]);
        if (magic != Magic)
        {
            throw new InvalidDataException("chunk blob magic 不匹配。");
        }

        ChunkBlobHeader header = new(
            BinaryPrimitives.ReadInt32LittleEndian(source.Slice(4, 4)),
            new ChunkCoord(
                BinaryPrimitives.ReadInt32LittleEndian(source.Slice(8, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(source.Slice(12, 4))),
            BinaryPrimitives.ReadInt32LittleEndian(source.Slice(16, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(source.Slice(20, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(source.Slice(24, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(source.Slice(28, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(source.Slice(32, 4)));
        header.Validate();
        return header;
    }

    /// <summary>
    /// 校验头部字段。
    /// </summary>
    public void Validate()
    {
        if (FormatVersion != SaveFormatVersions.ChunkBlob)
        {
            throw new InvalidDataException($"不支持的 chunk blob 版本：{FormatVersion}。");
        }

        if (MaterialRleBytes < 0 ||
            FlagsRleBytes < 0 ||
            LifetimeRleBytes < 0 ||
            TemperatureBytes != ChunkSnapshot.TemperatureCellCount * 2 ||
            UncompressedPayloadBytes < 0)
        {
            throw new InvalidDataException("chunk blob header 包含非法长度字段。");
        }

        int expected = checked(MaterialRleBytes + FlagsRleBytes + LifetimeRleBytes + TemperatureBytes);
        if (expected != UncompressedPayloadBytes)
        {
            throw new InvalidDataException("chunk blob payload 长度字段不一致。");
        }
    }
}
