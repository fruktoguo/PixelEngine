using System.Buffers;
using System.Buffers.Binary;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Serialization.Tests;

/// <summary>
/// 存档格式版本迁移测试。
/// </summary>
public sealed class VersionMigrationTests
{
    /// <summary>
    /// 验证旧 manifest FormatVersion 经 MigrationChain 逐步升级到当前格式且保留语义。
    /// </summary>
    [Fact]
    public void OldManifestFormatUpgradesToCurrentVersionAndPreservesSemantics()
    {
        byte[] source = V1ManifestPayload.Create();
        byte[] sourceBeforeUpgrade = [.. source];
        V1ToV2ManifestMigrator migrator = new();
        V2ToV3ManifestMigrator damageLaneMigrator = new();
        MigrationChain chain = new(SaveFormatVersions.WorldManifest, [migrator, damageLaneMigrator]);

        byte[] upgraded = chain.Upgrade(source, fromVersion: V1ManifestPayload.FormatVersion);

        Assert.Equal(sourceBeforeUpgrade, source);
        Assert.Equal([V1ManifestPayload.FormatVersion], migrator.ObservedFromVersions);
        Assert.Equal([SaveFormatVersions.WorldManifestBeforeDamageLane], damageLaneMigrator.ObservedFromVersions);
        WorldManifest decoded = new ManifestCodec().Decode(upgraded);
        AssertCurrentManifestSemantics(decoded);
    }

    /// <summary>
    /// 验证单步迁移的 payload 变换是纯函数，可脱离 MigrationChain 独立单测。
    /// </summary>
    [Fact]
    public void ManifestMigrationStepIsPurePayloadTransform()
    {
        byte[] source = V1ManifestPayload.Create();
        byte[] sourceBeforeMigration = [.. source];

        byte[] first = V1ToV2ManifestMigrator.UpgradePayload(source);
        byte[] second = V1ToV2ManifestMigrator.UpgradePayload(source);

        Assert.Equal(sourceBeforeMigration, source);
        Assert.Equal(first, second);
        Assert.NotSame(first, second);
        AssertCurrentManifestSemantics(new ManifestCodec().Decode(first));
    }

    /// <summary>
    /// 验证材质新增 / 重排不触发格式迁移，而是由 MaterialRemap 按稳定 name 处理。
    /// </summary>
    [Fact]
    public void MaterialReorderAndInsertionUsesMaterialRemapWithoutMigration()
    {
        WorldManifest manifest = new(
            SaveFormatVersions.WorldManifest,
            worldSeed: 17,
            gameTimeTicks: 29,
            playerStateBlob: [9, 8],
            materialNames: new MaterialNameTable([(0, "empty"), (1, "sand"), (2, "water")]),
            freeParticles:
            [
                new FreeParticleSnapshot(1, 2, 3, 4, 1, 5, 6),
                new FreeParticleSnapshot(7, 8, 9, 10, 2, 11, 12),
            ],
            rigidBodies:
            [
                new RigidBodySnapshot(
                    id: 3,
                    width: 2,
                    height: 2,
                    bodyLocalMask: [1, 1, 0, 1],
                    material: [1, 2, 0, 1],
                    posX: 0,
                    posY: 1,
                    rotCos: 1,
                    rotSin: 0,
                    linVelX: 0,
                    linVelY: 0,
                    angVel: 0),
            ],
            chunkIndex: [new ChunkCoord(4, -5)]);
        ArrayBufferWriter<byte> writer = new();
        new ManifestCodec().Encode(manifest, writer);
        byte[] source = writer.WrittenSpan.ToArray();
        byte[] sourceBeforeUpgrade = [.. source];
        ThrowingMigrator migrator = new();
        MigrationChain chain = new(SaveFormatVersions.WorldManifest, [migrator]);

        byte[] upgraded = chain.Upgrade(source, fromVersion: SaveFormatVersions.WorldManifest);
        WorldManifest decoded = new ManifestCodec().Decode(upgraded);

        Assert.False(migrator.WasCalled);
        Assert.Equal(sourceBeforeUpgrade, source);
        Assert.Equal(sourceBeforeUpgrade, upgraded);
        Assert.Equal([(0, "empty"), (1, "sand"), (2, "water")], decoded.MaterialNames.Entries.ToArray());
        Assert.Equal([1, 2], decoded.FreeParticles.Span.ToArray().Select(static particle => particle.Material).ToArray());
        Assert.Equal([1, 2, 0, 1], decoded.RigidBodies.Span[0].Material.ToArray());

        MaterialTable currentMaterials = new(
        [
            Material(0, "empty"),
            Material(1, "acid"),
            Material(2, "water"),
            Material(3, "sand"),
        ]);
        MaterialRemap remap = MaterialRemap.Build(decoded.MaterialNames, currentMaterials, fallbackId: 0);
        ushort[] particleMaterials =
        [
            remap.Map(decoded.FreeParticles.Span[0].Material),
            remap.Map(decoded.FreeParticles.Span[1].Material),
        ];
        ushort[] rigidBodyMaterials = decoded.RigidBodies.Span[0].Material.ToArray();

        remap.RemapInPlace(rigidBodyMaterials);

        Assert.Equal([3, 2], particleMaterials);
        Assert.Equal([3, 2, 0, 3], rigidBodyMaterials);
        Assert.Equal(0, remap.FallbackHitCount);
    }

    private static void AssertCurrentManifestSemantics(WorldManifest manifest)
    {
        Assert.Equal(SaveFormatVersions.WorldManifest, manifest.FormatVersion);
        Assert.Equal(0x1234_5678_9ABC_DEF0UL, manifest.WorldSeed);
        Assert.Equal(987654321L, manifest.GameTimeTicks);
        Assert.Equal([1, 2, 3], manifest.PlayerStateBlob.ToArray());
        Assert.Equal([(0, "empty"), (1, "sand"), (2, "water")], manifest.MaterialNames.Entries.ToArray());
        Assert.Equal(
            [
                new FreeParticleSnapshot(1.25f, 2.5f, -0.5f, 4.75f, 1, 7, 99),
                new FreeParticleSnapshot(-3.5f, 6.25f, 0.125f, -0.25f, 2, 8, 12),
            ],
            manifest.FreeParticles.ToArray());
        Assert.Empty(manifest.ChunkIndex.ToArray());

        RigidBodySnapshot body = manifest.RigidBodies.Span[0];
        Assert.Equal(42, body.Id);
        Assert.Equal(2, body.Width);
        Assert.Equal(2, body.Height);
        Assert.Equal([1, 0, 1, 1], body.BodyLocalMask.ToArray());
        Assert.Equal([1, 2, 0, 1], body.Material.ToArray());
        Assert.Equal(11.5f, body.PosX);
        Assert.Equal(-3.25f, body.PosY);
        Assert.Equal(0.5f, body.RotCos);
        Assert.Equal(0.75f, body.RotSin);
        Assert.Equal(2.5f, body.LinVelX);
        Assert.Equal(-1.5f, body.LinVelY);
        Assert.Equal(0.125f, body.AngVel);
        Assert.Equal(1.25f, body.LocalOriginX);
        Assert.Equal(1.75f, body.LocalOriginY);
    }

    private static MaterialDef Material(ushort id, string name)
    {
        return new MaterialDef
        {
            Id = id,
            Name = name,
            Type = id == 0 ? CellType.Empty : CellType.Solid,
            HeatCapacity = 1,
            TextureId = -1,
            MeltPoint = float.NaN,
            FreezePoint = float.NaN,
            BoilPoint = float.NaN,
        };
    }

    private sealed class V1ToV2ManifestMigrator : ISaveMigrator
    {
        public int FromVersion => V1ManifestPayload.FormatVersion;

        public List<int> ObservedFromVersions { get; } = [];

        public static byte[] UpgradePayload(ReadOnlySpan<byte> payload)
        {
            return V1ManifestPayload.UpgradeToV2(payload);
        }

        public void Migrate(MigrationContext context)
        {
            ObservedFromVersions.Add(context.FormatVersion);
            context.ReplacePayload(UpgradePayload(context.Payload), FromVersion + 1);
        }
    }

    private sealed class V2ToV3ManifestMigrator : ISaveMigrator
    {
        public int FromVersion => SaveFormatVersions.WorldManifestBeforeDamageLane;

        public List<int> ObservedFromVersions { get; } = [];

        public void Migrate(MigrationContext context)
        {
            ObservedFromVersions.Add(context.FormatVersion);
            byte[] upgraded = [.. context.Payload];
            BinaryPrimitives.WriteInt32LittleEndian(upgraded.AsSpan(4, 4), SaveFormatVersions.WorldManifest);
            context.ReplacePayload(upgraded, SaveFormatVersions.WorldManifest);
        }
    }

    private sealed class ThrowingMigrator : ISaveMigrator
    {
        public int FromVersion => 1;

        public bool WasCalled { get; private set; }

        public void Migrate(MigrationContext context)
        {
            WasCalled = true;
            throw new InvalidOperationException("当前版本 payload 不应触发材质 remap 迁移。");
        }
    }

    private static class V1ManifestPayload
    {
        public const int FormatVersion = 1;

        private const uint Magic = 0x4D57_4550u;
        private const int V1HeaderSize = sizeof(uint) + sizeof(int) + sizeof(ulong) + sizeof(long) + (sizeof(int) * 3);
        private const int CurrentHeaderSize = V1HeaderSize + sizeof(int);
        private const int ParticleSize = (sizeof(float) * 4) + sizeof(ushort) + sizeof(byte) + sizeof(byte);
        private const int RigidBodyHeaderSize = (sizeof(int) * 3) + (sizeof(float) * 9);

        public static byte[] Create()
        {
            byte[] playerState = [1, 2, 3];
            FreeParticleSnapshot[] particles =
            [
                new(1.25f, 2.5f, -0.5f, 4.75f, 1, 7, 99),
                new(-3.5f, 6.25f, 0.125f, -0.25f, 2, 8, 12),
            ];
            RigidBodySnapshot[] bodies =
            [
                new(
                    id: 42,
                    width: 2,
                    height: 2,
                    bodyLocalMask: [1, 0, 1, 1],
                    material: [1, 2, 0, 1],
                    posX: 11.5f,
                    posY: -3.25f,
                    rotCos: 0.5f,
                    rotSin: 0.75f,
                    linVelX: 2.5f,
                    linVelY: -1.5f,
                    angVel: 0.125f,
                    localOriginX: 1.25f,
                    localOriginY: 1.75f),
            ];

            ArrayBufferWriter<byte> writer = new();
            Span<byte> header = writer.GetSpan(V1HeaderSize);
            BinaryPrimitives.WriteUInt32LittleEndian(header[..4], Magic);
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(4, 4), FormatVersion);
            BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(8, 8), 0x1234_5678_9ABC_DEF0UL);
            BinaryPrimitives.WriteInt64LittleEndian(header.Slice(16, 8), 987654321L);
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(24, 4), playerState.Length);
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(28, 4), particles.Length);
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(32, 4), bodies.Length);
            writer.Advance(V1HeaderSize);

            WriteBytes(playerState, writer);
            new MaterialNameTable([(0, "empty"), (1, "sand"), (2, "water")]).Write(writer);
            WriteParticles(particles, writer);
            WriteRigidBodies(bodies, writer);
            return writer.WrittenSpan.ToArray();
        }

        public static byte[] UpgradeToV2(ReadOnlySpan<byte> source)
        {
            if (source.Length < V1HeaderSize)
            {
                throw new InvalidDataException("v1 manifest header 不完整。");
            }

            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(source[..4]);
            int formatVersion = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(4, 4));
            if (magic != Magic || formatVersion != FormatVersion)
            {
                throw new InvalidDataException("v1 manifest magic 或版本不匹配。");
            }

            ArrayBufferWriter<byte> writer = new(source.Length + sizeof(int));
            Span<byte> header = writer.GetSpan(CurrentHeaderSize);
            BinaryPrimitives.WriteUInt32LittleEndian(header[..4], Magic);
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(4, 4), SaveFormatVersions.WorldManifestBeforeDamageLane);
            source[8..V1HeaderSize].CopyTo(header[8..V1HeaderSize]);
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(36, 4), 0);
            writer.Advance(CurrentHeaderSize);
            WriteBytes(source[V1HeaderSize..], writer);
            return writer.WrittenSpan.ToArray();
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

        private static void WriteSingle(Span<byte> destination, float value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination, BitConverter.SingleToUInt32Bits(value));
        }
    }
}
