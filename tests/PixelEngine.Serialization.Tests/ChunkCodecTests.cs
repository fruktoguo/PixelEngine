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
        byte[] damage = new byte[EngineConstants.ChunkArea];
        Half[] temperature = new Half[ChunkSnapshot.TemperatureCellCount];
        FillSource(material, flags, lifetime, damage, temperature);
        ChunkSnapshot source = new(new ChunkCoord(3, -4), material, flags, lifetime, damage, temperature);
        ArrayBufferWriter<byte> writer = new();
        ChunkCodec codec = new();

        codec.Encode(source, writer);

        ushort[] decodedMaterial = new ushort[EngineConstants.ChunkArea];
        byte[] decodedFlags = new byte[EngineConstants.ChunkArea];
        byte[] decodedLifetime = new byte[EngineConstants.ChunkArea];
        byte[] decodedDamage = new byte[EngineConstants.ChunkArea];
        Half[] decodedTemperature = new Half[ChunkSnapshot.TemperatureCellCount];
        ChunkSnapshot destination = new(new ChunkCoord(3, -4), decodedMaterial, decodedFlags, decodedLifetime, decodedDamage, decodedTemperature);
        codec.Decode(writer.WrittenSpan, destination, currentParityBit: CellFlags.Parity);

        Assert.Equal(material, decodedMaterial);
        Assert.Equal(lifetime, decodedLifetime);
        Assert.Equal(damage, decodedDamage);
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
        byte[] damage = new byte[EngineConstants.ChunkArea];
        Half[] temperature = new Half[ChunkSnapshot.TemperatureCellCount];
        codec.Encode(new ChunkSnapshot(new ChunkCoord(0, 0), material, flags, lifetime, damage, temperature), writer);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            codec.Decode(
                writer.WrittenSpan,
                new ChunkSnapshot(new ChunkCoord(1, 0), material, flags, lifetime, damage, temperature),
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
            DamageRleBytes: 3,
            TemperatureBytes: ChunkSnapshot.TemperatureCellCount * 2,
            UncompressedPayloadBytes: 1);

        byte[] bytes = new byte[ChunkBlobHeader.Size];
        header.Write(bytes);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ChunkBlobHeader.Read(bytes));

        Assert.Contains("长度", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证旧 v1 chunk blob 没有 Damage 段时，解码会填充 Damage=0。
    /// </summary>
    [Fact]
    public void DecodeLegacyV1ChunkBlobFillsDamageWithZero()
    {
        ushort[] material = new ushort[EngineConstants.ChunkArea];
        byte[] flags = new byte[EngineConstants.ChunkArea];
        byte[] lifetime = new byte[EngineConstants.ChunkArea];
        byte[] noDamage = new byte[EngineConstants.ChunkArea];
        Half[] temperature = new Half[ChunkSnapshot.TemperatureCellCount];
        FillSource(material, flags, lifetime, noDamage, temperature);
        noDamage.AsSpan().Clear();
        ArrayBufferWriter<byte> writer = new();
        WriteLegacyV1Blob(new ChunkSnapshot(new ChunkCoord(2, 3), material, flags, lifetime, noDamage, temperature), writer);

        ushort[] decodedMaterial = new ushort[EngineConstants.ChunkArea];
        byte[] decodedFlags = new byte[EngineConstants.ChunkArea];
        byte[] decodedLifetime = new byte[EngineConstants.ChunkArea];
        byte[] decodedDamage = new byte[EngineConstants.ChunkArea];
        Half[] decodedTemperature = new Half[ChunkSnapshot.TemperatureCellCount];

        new ChunkCodec().Decode(
            writer.WrittenSpan,
            new ChunkSnapshot(new ChunkCoord(2, 3), decodedMaterial, decodedFlags, decodedLifetime, decodedDamage, decodedTemperature),
            currentParityBit: 0);

        Assert.Equal(material, decodedMaterial);
        Assert.Equal(lifetime, decodedLifetime);
        Assert.All(decodedDamage, value => Assert.Equal(0, value));
        Assert.Equal(temperature, decodedTemperature);
    }

    private static void FillSource(ushort[] material, byte[] flags, byte[] lifetime, byte[] damage, Half[] temperature)
    {
        for (int i = 0; i < material.Length; i++)
        {
            material[i] = (ushort)(i % 17);
            lifetime[i] = (byte)(i % 251);
            damage[i] = (byte)(i % 113);
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

    private static void WriteLegacyV1Blob(ChunkSnapshot snapshot, IBufferWriter<byte> writer)
    {
        PooledByteBufferWriter payload = new();
        try
        {
            RleCodec.EncodeU16(snapshot.Material, payload);
            int materialBytes = payload.WrittenCount;

            byte[] persistentFlags = new byte[snapshot.Flags.Length];
            snapshot.Flags.CopyTo(persistentFlags);
            PersistentCellFlags.StripTransientInPlace(persistentFlags);
            RleCodec.EncodeU8(persistentFlags, payload);
            int flagsBytes = payload.WrittenCount - materialBytes;

            RleCodec.EncodeU8(snapshot.Lifetime, payload);
            int lifetimeBytes = payload.WrittenCount - materialBytes - flagsBytes;

            ReadOnlySpan<byte> tempBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(snapshot.Temperature);
            Span<byte> tempDestination = payload.GetSpan(tempBytes.Length);
            tempBytes.CopyTo(tempDestination);
            payload.Advance(tempBytes.Length);
            int temperatureBytes = payload.WrittenCount - materialBytes - flagsBytes - lifetimeBytes;

            ChunkBlobHeader header = new(
                1,
                snapshot.Coord,
                materialBytes,
                flagsBytes,
                lifetimeBytes,
                0,
                temperatureBytes,
                payload.WrittenCount);
            Span<byte> headerBytes = writer.GetSpan(ChunkBlobHeader.LegacySize);
            header.Write(headerBytes);
            writer.Advance(ChunkBlobHeader.LegacySize);
            Lz4BlockCodec.Compress(payload.WrittenSpan, writer);
        }
        finally
        {
            payload.Dispose();
        }
    }
}
