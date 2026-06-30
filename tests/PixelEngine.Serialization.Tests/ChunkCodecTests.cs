using System.Buffers;
using PixelEngine.Core;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Serialization.Tests;

/// <summary>
/// ChunkCodec 二进制往返测试。
/// </summary>
public sealed class ChunkCodecTests
{
    /// <summary>
    /// 验证 chunk blob 往返保持 Material/Lifetime/Temperature，flags 仅持久化 burning 并重置 parity。
    /// </summary>
    [Fact]
    public void ChunkCodecRoundTripsSegmentsAndResetsTransientFlags()
    {
        ushort[] material = new ushort[EngineConstants.ChunkArea];
        byte[] flags = new byte[EngineConstants.ChunkArea];
        byte[] lifetime = new byte[EngineConstants.ChunkArea];
        Half[] temperature = new Half[ChunkSnapshot.TemperatureCellCount];
        FillSource(material, flags, lifetime, temperature);
        ChunkSnapshot source = new(new ChunkCoord(3, -4), material, flags, lifetime, temperature);
        ArrayBufferWriter<byte> writer = new();
        ChunkCodec codec = new();

        codec.Encode(source, writer);

        ushort[] decodedMaterial = new ushort[EngineConstants.ChunkArea];
        byte[] decodedFlags = new byte[EngineConstants.ChunkArea];
        byte[] decodedLifetime = new byte[EngineConstants.ChunkArea];
        Half[] decodedTemperature = new Half[ChunkSnapshot.TemperatureCellCount];
        ChunkSnapshot destination = new(new ChunkCoord(3, -4), decodedMaterial, decodedFlags, decodedLifetime, decodedTemperature);
        codec.Decode(writer.WrittenSpan, destination, currentParityBit: CellFlags.Parity);

        Assert.Equal(material, decodedMaterial);
        Assert.Equal(lifetime, decodedLifetime);
        Assert.Equal(temperature, decodedTemperature);
        for (int i = 0; i < decodedFlags.Length; i++)
        {
            byte expected = (byte)((flags[i] & CellFlags.Burning) | 0);
            Assert.Equal(expected, decodedFlags[i]);
        }
    }

    /// <summary>
    /// 验证 blob header 坐标与目标快照不一致时拒绝解码。
    /// </summary>
    [Fact]
    public void DecodeRejectsMismatchedChunkCoord()
    {
        ChunkCodec codec = new();
        ArrayBufferWriter<byte> writer = new();
        ushort[] material = new ushort[EngineConstants.ChunkArea];
        byte[] flags = new byte[EngineConstants.ChunkArea];
        byte[] lifetime = new byte[EngineConstants.ChunkArea];
        Half[] temperature = new Half[ChunkSnapshot.TemperatureCellCount];
        codec.Encode(new ChunkSnapshot(new ChunkCoord(0, 0), material, flags, lifetime, temperature), writer);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            codec.Decode(
                writer.WrittenSpan,
                new ChunkSnapshot(new ChunkCoord(1, 0), material, flags, lifetime, temperature),
                currentParityBit: 0));

        Assert.Contains("坐标", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 header 长度字段不一致会被拒绝。
    /// </summary>
    [Fact]
    public void ChunkBlobHeaderRejectsInconsistentLengths()
    {
        ChunkBlobHeader header = new(
            SaveFormatVersions.ChunkBlob,
            new ChunkCoord(0, 0),
            MaterialRleBytes: 4,
            FlagsRleBytes: 3,
            LifetimeRleBytes: 3,
            TemperatureBytes: ChunkSnapshot.TemperatureCellCount * 2,
            UncompressedPayloadBytes: 1);

        byte[] bytes = new byte[ChunkBlobHeader.Size];
        header.Write(bytes);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ChunkBlobHeader.Read(bytes));

        Assert.Contains("长度", exception.Message, StringComparison.Ordinal);
    }

    private static void FillSource(ushort[] material, byte[] flags, byte[] lifetime, Half[] temperature)
    {
        for (int i = 0; i < material.Length; i++)
        {
            material[i] = (ushort)(i % 17);
            lifetime[i] = (byte)(i % 251);
            flags[i] = CellFlags.Parity | CellFlags.Settled | CellFlags.FreeFalling | CellFlags.RigidOwned;
            if ((i & 3) == 0)
            {
                flags[i] |= CellFlags.Burning;
            }
        }

        for (int i = 0; i < temperature.Length; i++)
        {
            temperature[i] = (Half)(i * 0.5f);
        }
    }
}
