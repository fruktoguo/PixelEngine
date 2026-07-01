using System.Buffers;
using System.Buffers.Binary;
using PixelEngine.Simulation;

namespace PixelEngine.Serialization;

/// <summary>
/// world manifest 紧凑二进制编解码器。
/// </summary>
public sealed class ManifestCodec
{
    private const uint Magic = 0x4D57_4550u;
    private const int HeaderSize = sizeof(uint) + sizeof(int) + sizeof(ulong) + sizeof(long) + (sizeof(int) * 4);
    private const int ParticleSize = (sizeof(float) * 4) + sizeof(ushort) + sizeof(byte) + sizeof(byte);
    private const int RigidBodyHeaderSize = (sizeof(int) * 3) + (sizeof(float) * 9);
    private const int ChunkCoordSize = sizeof(int) * 2;

    /// <summary>
    /// 写入 world manifest。
    /// </summary>
    public void Encode(WorldManifest manifest, IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(writer);
        if (manifest.FormatVersion != SaveFormatVersions.WorldManifest)
        {
            throw new InvalidDataException($"不支持的 manifest 版本：{manifest.FormatVersion}。");
        }

        ReadOnlySpan<byte> playerState = manifest.PlayerStateBlob.Span;
        ReadOnlySpan<FreeParticleSnapshot> particles = manifest.FreeParticles.Span;
        ReadOnlySpan<RigidBodySnapshot> bodies = manifest.RigidBodies.Span;
        ReadOnlySpan<ChunkCoord> chunks = manifest.ChunkIndex.Span;

        Span<byte> header = writer.GetSpan(HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(header[..sizeof(uint)], Magic);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(4, 4), manifest.FormatVersion);
        BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(8, 8), manifest.WorldSeed);
        BinaryPrimitives.WriteInt64LittleEndian(header.Slice(16, 8), manifest.GameTimeTicks);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(24, 4), playerState.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(28, 4), particles.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(32, 4), bodies.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(36, 4), chunks.Length);
        writer.Advance(HeaderSize);

        WriteBytes(playerState, writer);
        manifest.MaterialNames.Write(writer);
        WriteParticles(particles, writer);
        WriteRigidBodies(bodies, writer);
        WriteChunkIndex(chunks, writer);
    }

    /// <summary>
    /// 读取 world manifest。
    /// </summary>
    public WorldManifest Decode(ReadOnlySpan<byte> source)
    {
        if (source.Length < HeaderSize)
        {
            throw new InvalidDataException("world manifest header 不完整。");
        }

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(source[..sizeof(uint)]);
        if (magic != Magic)
        {
            throw new InvalidDataException("world manifest magic 不匹配。");
        }

        int formatVersion = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(4, 4));
        if (formatVersion != SaveFormatVersions.WorldManifest)
        {
            throw new InvalidDataException($"不支持的 manifest 版本：{formatVersion}。");
        }

        ulong worldSeed = BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(8, 8));
        long gameTimeTicks = BinaryPrimitives.ReadInt64LittleEndian(source.Slice(16, 8));
        int playerStateBytes = ReadNonNegativeCount(source.Slice(24, 4), "player state 长度");
        int particleCount = ReadNonNegativeCount(source.Slice(28, 4), "particle 数量");
        int bodyCount = ReadNonNegativeCount(source.Slice(32, 4), "rigid body 数量");
        int chunkCount = ReadNonNegativeCount(source.Slice(36, 4), "chunk 索引数量");

        int offset = HeaderSize;
        byte[] playerState = ReadByteArray(source, ref offset, playerStateBytes, "player state");
        MaterialNameTable materialNames = MaterialNameTable.Read(source[offset..], out int materialBytes);
        offset = checked(offset + materialBytes);
        FreeParticleSnapshot[] particles = ReadParticles(source, ref offset, particleCount);
        RigidBodySnapshot[] bodies = ReadRigidBodies(source, ref offset, bodyCount);
        ChunkCoord[] chunks = ReadChunkIndex(source, ref offset, chunkCount);
        EnsureFullyConsumed(offset, source.Length);

        return new WorldManifest(
            formatVersion,
            worldSeed,
            gameTimeTicks,
            playerState,
            materialNames,
            particles,
            bodies,
            chunks);
    }

    private static void WriteParticles(ReadOnlySpan<FreeParticleSnapshot> particles, IBufferWriter<byte> writer)
    {
        for (int i = 0; i < particles.Length; i++)
        {
            Span<byte> span = writer.GetSpan(ParticleSize);
            WriteSingle(span[..4], particles[i].X);
            WriteSingle(span.Slice(4, 4), particles[i].Y);
            WriteSingle(span.Slice(8, 4), particles[i].Vx);
            WriteSingle(span.Slice(12, 4), particles[i].Vy);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(16, 2), particles[i].Material);
            span[18] = particles[i].ColorVariant;
            span[19] = particles[i].Life;
            writer.Advance(ParticleSize);
        }
    }

    private static FreeParticleSnapshot[] ReadParticles(ReadOnlySpan<byte> source, ref int offset, int count)
    {
        FreeParticleSnapshot[] particles = new FreeParticleSnapshot[count];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> span = ReadSlice(source, ref offset, ParticleSize, "particle");
            particles[i] = new FreeParticleSnapshot(
                ReadSingle(span[..4]),
                ReadSingle(span.Slice(4, 4)),
                ReadSingle(span.Slice(8, 4)),
                ReadSingle(span.Slice(12, 4)),
                BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(16, 2)),
                span[18],
                span[19]);
        }

        return particles;
    }

    private static void WriteRigidBodies(ReadOnlySpan<RigidBodySnapshot> bodies, IBufferWriter<byte> writer)
    {
        for (int i = 0; i < bodies.Length; i++)
        {
            RigidBodySnapshot body = bodies[i];
            Span<byte> header = writer.GetSpan(RigidBodyHeaderSize);
            BinaryPrimitives.WriteInt32LittleEndian(header[..4], body.Id);
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(4, 4), body.Width);
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(8, 4), body.Height);
            WriteSingle(header.Slice(12, 4), body.PosX);
            WriteSingle(header.Slice(16, 4), body.PosY);
            WriteSingle(header.Slice(20, 4), body.RotCos);
            WriteSingle(header.Slice(24, 4), body.RotSin);
            WriteSingle(header.Slice(28, 4), body.LinVelX);
            WriteSingle(header.Slice(32, 4), body.LinVelY);
            WriteSingle(header.Slice(36, 4), body.AngVel);
            WriteSingle(header.Slice(40, 4), body.LocalOriginX);
            WriteSingle(header.Slice(44, 4), body.LocalOriginY);
            writer.Advance(RigidBodyHeaderSize);

            WriteBytes(body.BodyLocalMask.Span, writer);
            WriteU16Span(body.Material.Span, writer);
        }
    }

    private static RigidBodySnapshot[] ReadRigidBodies(ReadOnlySpan<byte> source, ref int offset, int count)
    {
        RigidBodySnapshot[] bodies = new RigidBodySnapshot[count];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> header = ReadSlice(source, ref offset, RigidBodyHeaderSize, "rigid body header");
            int id = BinaryPrimitives.ReadInt32LittleEndian(header[..4]);
            int width = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(4, 4));
            int height = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(8, 4));
            if (width <= 0 || height <= 0)
            {
                throw new InvalidDataException("rigid body 尺寸非法。");
            }

            int area = checked(width * height);
            byte[] mask = ReadByteArray(source, ref offset, area, "rigid body mask");
            ushort[] material = ReadU16Array(source, ref offset, area, "rigid body material");
            bodies[i] = new RigidBodySnapshot(
                id,
                width,
                height,
                mask,
                material,
                ReadSingle(header.Slice(12, 4)),
                ReadSingle(header.Slice(16, 4)),
                ReadSingle(header.Slice(20, 4)),
                ReadSingle(header.Slice(24, 4)),
                ReadSingle(header.Slice(28, 4)),
                ReadSingle(header.Slice(32, 4)),
                ReadSingle(header.Slice(36, 4)),
                ReadSingle(header.Slice(40, 4)),
                ReadSingle(header.Slice(44, 4)));
        }

        return bodies;
    }

    private static void WriteChunkIndex(ReadOnlySpan<ChunkCoord> chunks, IBufferWriter<byte> writer)
    {
        for (int i = 0; i < chunks.Length; i++)
        {
            Span<byte> span = writer.GetSpan(ChunkCoordSize);
            BinaryPrimitives.WriteInt32LittleEndian(span[..4], chunks[i].X);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4, 4), chunks[i].Y);
            writer.Advance(ChunkCoordSize);
        }
    }

    private static ChunkCoord[] ReadChunkIndex(ReadOnlySpan<byte> source, ref int offset, int count)
    {
        ChunkCoord[] chunks = new ChunkCoord[count];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> span = ReadSlice(source, ref offset, ChunkCoordSize, "chunk index");
            chunks[i] = new ChunkCoord(
                BinaryPrimitives.ReadInt32LittleEndian(span[..4]),
                BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4)));
        }

        return chunks;
    }

    private static void WriteBytes(ReadOnlySpan<byte> source, IBufferWriter<byte> writer)
    {
        Span<byte> destination = writer.GetSpan(source.Length);
        source.CopyTo(destination);
        writer.Advance(source.Length);
    }

    private static void WriteU16Span(ReadOnlySpan<ushort> source, IBufferWriter<byte> writer)
    {
        Span<byte> destination = writer.GetSpan(checked(source.Length * sizeof(ushort)));
        for (int i = 0; i < source.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(i * sizeof(ushort), sizeof(ushort)), source[i]);
        }

        writer.Advance(source.Length * sizeof(ushort));
    }

    private static byte[] ReadByteArray(ReadOnlySpan<byte> source, ref int offset, int length, string label)
    {
        return ReadSlice(source, ref offset, length, label).ToArray();
    }

    private static ushort[] ReadU16Array(ReadOnlySpan<byte> source, ref int offset, int count, string label)
    {
        ReadOnlySpan<byte> bytes = ReadSlice(source, ref offset, checked(count * sizeof(ushort)), label);
        ushort[] values = new ushort[count];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(i * sizeof(ushort), sizeof(ushort)));
        }

        return values;
    }

    private static ReadOnlySpan<byte> ReadSlice(ReadOnlySpan<byte> source, ref int offset, int length, string label)
    {
        bool hasEnoughBytes = length >= 0 && source.Length - offset >= length;
        if (!hasEnoughBytes)
        {
            throw new InvalidDataException($"world manifest {label} 字节不完整。");
        }

        ReadOnlySpan<byte> slice = source.Slice(offset, length);
        offset += length;
        return slice;
    }

    private static int ReadNonNegativeCount(ReadOnlySpan<byte> source, string label)
    {
        int value = BinaryPrimitives.ReadInt32LittleEndian(source);
        return value >= 0
            ? value
            : throw new InvalidDataException($"world manifest {label} 不能为负。");
    }

    private static void EnsureFullyConsumed(int offset, int length)
    {
        if (offset == length)
        {
            return;
        }

        throw new InvalidDataException("world manifest 含多余尾字节。");
    }

    private static void WriteSingle(Span<byte> destination, float value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination, BitConverter.SingleToUInt32Bits(value));
    }

    private static float ReadSingle(ReadOnlySpan<byte> source)
    {
        return BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32LittleEndian(source));
    }
}
