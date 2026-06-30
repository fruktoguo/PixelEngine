using System.Runtime.CompilerServices;
using Xunit;

namespace PixelEngine.Serialization.Tests;

/// <summary>
/// world manifest 全局态 DTO 与快照契约测试。
/// </summary>
public sealed class WorldStateSnapshotDtoTests
{
    /// <summary>
    /// 验证自由粒子 DTO 与 plan/05 粒子布局保持 20B。
    /// </summary>
    [Fact]
    public void FreeParticleSnapshotIsTwentyBytes()
    {
        Assert.Equal(20, Unsafe.SizeOf<FreeParticleSnapshot>());
    }

    /// <summary>
    /// 验证刚体快照深拷贝 body-local mask 与 material。
    /// </summary>
    [Fact]
    public void RigidBodySnapshotCopiesShapeAndMaterial()
    {
        byte[] mask = [1, 0, 1, 1];
        ushort[] material = [2, 0, 3, 4];

        RigidBodySnapshot snapshot = new(
            id: 9,
            width: 2,
            height: 2,
            bodyLocalMask: mask,
            material: material,
            posX: 10.5f,
            posY: 20.5f,
            rotCos: 0.5f,
            rotSin: 0.75f,
            linVelX: 1.25f,
            linVelY: 2.25f,
            angVel: 3.25f);

        mask[0] = 0;
        material[0] = 99;

        Assert.Equal(9, snapshot.Id);
        Assert.Equal(2, snapshot.Width);
        Assert.Equal(2, snapshot.Height);
        Assert.Equal([1, 0, 1, 1], snapshot.BodyLocalMask.ToArray());
        Assert.Equal([2, 0, 3, 4], snapshot.Material.ToArray());
        Assert.Equal(10.5f, snapshot.PosX);
        Assert.Equal(20.5f, snapshot.PosY);
        Assert.Equal(0.5f, snapshot.RotCos);
        Assert.Equal(0.75f, snapshot.RotSin);
        Assert.Equal(1.25f, snapshot.LinVelX);
        Assert.Equal(2.25f, snapshot.LinVelY);
        Assert.Equal(3.25f, snapshot.AngVel);
    }

    /// <summary>
    /// 验证刚体快照拒绝不匹配的 body-local 数据长度。
    /// </summary>
    [Fact]
    public void RigidBodySnapshotRejectsInvalidShapeLengths()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateRigidBody(width: 0, height: 1, mask: [1], material: [1]));
        _ = Assert.Throws<ArgumentException>(() =>
            CreateRigidBody(width: 2, height: 2, mask: [1, 1, 1], material: [1, 1, 1, 1]));
        _ = Assert.Throws<ArgumentException>(() =>
            CreateRigidBody(width: 2, height: 2, mask: [1, 1, 1, 1], material: [1, 1, 1]));
    }

    /// <summary>
    /// 验证 snapshot source/sink 契约能用 caller-owned 缓冲区传递全局态。
    /// </summary>
    [Fact]
    public void WorldStateSnapshotContractsCopyAndRestoreCallerBuffers()
    {
        FreeParticleSnapshot particle = new(1, 2, 3, 4, 5, 6, 7);
        RigidBodySnapshot body = CreateRigidBody(1, 1, [1], [5]);
        FakeWorldStateBridge bridge = new([particle], [body]);
        FreeParticleSnapshot[] particleBuffer = new FreeParticleSnapshot[bridge.FreeParticleCount];
        RigidBodySnapshot[] bodyBuffer = new RigidBodySnapshot[bridge.RigidBodyCount];

        bridge.CopyFreeParticles(particleBuffer);
        bridge.CopyRigidBodies(bodyBuffer);
        bridge.RestoreFreeParticles(particleBuffer);
        bridge.RestoreRigidBodies(bodyBuffer);

        Assert.Equal([particle], bridge.RestoredParticles);
        Assert.Same(body, bridge.RestoredBodies[0]);
    }

    private static RigidBodySnapshot CreateRigidBody(int width, int height, byte[] mask, ushort[] material)
    {
        return new RigidBodySnapshot(
            id: 1,
            width,
            height,
            mask,
            material,
            posX: 0,
            posY: 0,
            rotCos: 1,
            rotSin: 0,
            linVelX: 0,
            linVelY: 0,
            angVel: 0);
    }

    private sealed class FakeWorldStateBridge(
        FreeParticleSnapshot[] particles,
        RigidBodySnapshot[] bodies) : IWorldStateSnapshotSource, IWorldStateSnapshotSink
    {
        public int FreeParticleCount => particles.Length;

        public int RigidBodyCount => bodies.Length;

        public FreeParticleSnapshot[] RestoredParticles { get; private set; } = [];

        public RigidBodySnapshot[] RestoredBodies { get; private set; } = [];

        public void CopyFreeParticles(Span<FreeParticleSnapshot> destination)
        {
            particles.CopyTo(destination);
        }

        public void CopyRigidBodies(Span<RigidBodySnapshot> destination)
        {
            bodies.CopyTo(destination);
        }

        public void RestoreFreeParticles(ReadOnlySpan<FreeParticleSnapshot> restoredParticles)
        {
            RestoredParticles = restoredParticles.ToArray();
        }

        public void RestoreRigidBodies(ReadOnlySpan<RigidBodySnapshot> restoredBodies)
        {
            RestoredBodies = restoredBodies.ToArray();
        }
    }
}
