using System.Buffers;
using System.Buffers.Binary;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Serialization.Tests;

/// <summary>
/// world manifest 紧凑二进制编解码测试。
/// </summary>
public sealed class ManifestCodecTests
{
    /// <summary>
    /// 验证 manifest 完整往返全部全局态字段。
    /// </summary>
    [Fact]
    public void ManifestCodecRoundTripsWorldState()
    {
        WorldManifest manifest = CreateManifest();
        ManifestCodec codec = new();
        ArrayBufferWriter<byte> writer = new();

        codec.Encode(manifest, writer);
        WorldManifest decoded = codec.Decode(writer.WrittenSpan);

        Assert.Equal(SaveFormatVersions.WorldManifest, decoded.FormatVersion);
        Assert.Equal(0xFEED_BEEF_CAFE_BABEUL, decoded.WorldSeed);
        Assert.Equal(123456789L, decoded.GameTimeTicks);
        Assert.Equal([1, 2, 3, 4], decoded.PlayerStateBlob.ToArray());
        Assert.Equal([(0, "empty"), (1, "sand"), (2, "water")], decoded.MaterialNames.Entries.ToArray());
        Assert.Equal(manifest.FreeParticles.ToArray(), decoded.FreeParticles.ToArray());
        Assert.Equal(manifest.ChunkIndex.ToArray(), decoded.ChunkIndex.ToArray());

        RigidBodySnapshot decodedBody = decoded.RigidBodies.Span[0];
        Assert.Equal(42, decodedBody.Id);
        Assert.Equal(2, decodedBody.Width);
        Assert.Equal(2, decodedBody.Height);
        Assert.Equal([1, 0, 1, 1], decodedBody.BodyLocalMask.ToArray());
        Assert.Equal([1, 0, 2, 1], decodedBody.Material.ToArray());
        Assert.Equal(11.5f, decodedBody.PosX);
        Assert.Equal(-3.25f, decodedBody.PosY);
        Assert.Equal(0.5f, decodedBody.RotCos);
        Assert.Equal(0.75f, decodedBody.RotSin);
        Assert.Equal(2.5f, decodedBody.LinVelX);
        Assert.Equal(-1.5f, decodedBody.LinVelY);
        Assert.Equal(0.125f, decodedBody.AngVel);
    }

    /// <summary>
    /// 验证 manifest 拒绝未知版本。
    /// </summary>
    [Fact]
    public void ManifestCodecRejectsUnsupportedVersion()
    {
        ManifestCodec codec = new();
        ArrayBufferWriter<byte> writer = new();
        codec.Encode(CreateManifest(), writer);
        byte[] bytes = writer.WrittenSpan.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), 99);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => codec.Decode(bytes));

        Assert.Contains("版本", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 manifest 拒绝损坏 magic。
    /// </summary>
    [Fact]
    public void ManifestCodecRejectsBadMagic()
    {
        ManifestCodec codec = new();
        ArrayBufferWriter<byte> writer = new();
        codec.Encode(CreateManifest(), writer);
        byte[] bytes = writer.WrittenSpan.ToArray();
        bytes[0] = 0;

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => codec.Decode(bytes));

        Assert.Contains("magic", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 验证 manifest 拒绝截断输入与多余尾字节。
    /// </summary>
    [Fact]
    public void ManifestCodecRejectsTruncatedOrTrailingBytes()
    {
        ManifestCodec codec = new();
        ArrayBufferWriter<byte> writer = new();
        codec.Encode(CreateManifest(), writer);
        byte[] bytes = writer.WrittenSpan.ToArray();
        byte[] withTail = new byte[bytes.Length + 1];
        bytes.CopyTo(withTail.AsSpan());

        _ = Assert.Throws<InvalidDataException>(() => codec.Decode(bytes.AsSpan(0, bytes.Length - 1)));
        InvalidDataException tail = Assert.Throws<InvalidDataException>(() => codec.Decode(withTail));

        Assert.Contains("尾字节", tail.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 WorldManifest 构造时复制 caller-owned 数组。
    /// </summary>
    [Fact]
    public void WorldManifestCopiesCallerOwnedBuffers()
    {
        byte[] playerState = [1, 2];
        FreeParticleSnapshot[] particles = [new(1, 2, 3, 4, 5, 6, 7)];
        ChunkCoord[] chunks = [new(-1, 0)];

        WorldManifest manifest = new(
            SaveFormatVersions.WorldManifest,
            worldSeed: 1,
            gameTimeTicks: 2,
            playerState,
            new MaterialNameTable([(0, "empty")]),
            particles,
            [],
            chunks);
        playerState[0] = 9;
        particles[0] = new FreeParticleSnapshot(9, 9, 9, 9, 9, 9, 9);
        chunks[0] = new ChunkCoord(9, 9);

        Assert.Equal([1, 2], manifest.PlayerStateBlob.ToArray());
        Assert.Equal(new FreeParticleSnapshot(1, 2, 3, 4, 5, 6, 7), manifest.FreeParticles.Span[0]);
        Assert.Equal(new ChunkCoord(-1, 0), manifest.ChunkIndex.Span[0]);
    }

    private static WorldManifest CreateManifest()
    {
        FreeParticleSnapshot[] particles =
        [
            new(1.25f, 2.5f, 0.5f, -0.25f, 1, 7, 99),
            new(-10.0f, 20.0f, 3.5f, 4.5f, 2, 8, 12),
        ];
        RigidBodySnapshot[] bodies =
        [
            new(
                id: 42,
                width: 2,
                height: 2,
                bodyLocalMask: [1, 0, 1, 1],
                material: [1, 0, 2, 1],
                posX: 11.5f,
                posY: -3.25f,
                rotCos: 0.5f,
                rotSin: 0.75f,
                linVelX: 2.5f,
                linVelY: -1.5f,
                angVel: 0.125f),
        ];

        return new WorldManifest(
            SaveFormatVersions.WorldManifest,
            0xFEED_BEEF_CAFE_BABEUL,
            123456789L,
            [1, 2, 3, 4],
            new MaterialNameTable([(0, "empty"), (2, "water"), (1, "sand")]),
            particles,
            bodies,
            [new ChunkCoord(0, 0), new ChunkCoord(-1, 2), new ChunkCoord(32, -33)]);
    }
}
