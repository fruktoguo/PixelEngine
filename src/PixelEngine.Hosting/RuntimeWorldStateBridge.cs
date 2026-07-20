using System.Numerics;
using System.Runtime.InteropServices;
using PixelEngine.Core.Mathematics;
using PixelEngine.Physics;
using PixelEngine.Serialization;
using PixelEngine.Simulation.Particles;
using SerializationRigidBodySnapshot = PixelEngine.Serialization.RigidBodySnapshot;

namespace PixelEngine.Hosting;

/// <summary>
/// Hosting 侧世界全局态桥，负责在 WorldSaveService 与当前 ParticleSystem/Physics 后端之间转换快照。
/// </summary>
internal sealed class RuntimeWorldStateBridge(ParticleSystem particles) : IWorldStateSnapshotSource, IWorldStateSnapshotSink
{
    private readonly ParticleSystem _particles = particles ?? throw new ArgumentNullException(nameof(particles));
    private readonly List<SerializationRigidBodySnapshot> _pendingRigidBodies = [];
    private PhysicsSystem? _physics;

    /// <summary>
    /// 当前在飞自由粒子数量。
    /// </summary>
    public int FreeParticleCount => _particles.ActiveCount;

    /// <summary>
    /// 当前刚体数量；Physics 尚未接入时返回读档暂存的刚体数量。
    /// </summary>
    public int RigidBodyCount => _physics?.PhysicsWorld.ActiveBodyCount ?? _pendingRigidBodies.Count;

    /// <summary>
    /// 接入 Physics 后端，并恢复此前读档暂存的刚体快照。
    /// </summary>
    public void AttachPhysics(PhysicsSystem physics)
    {
        ArgumentNullException.ThrowIfNull(physics);
        _physics = physics;
        if (_pendingRigidBodies.Count == 0)
        {
            return;
        }

        RestoreRigidBodies(CollectionsMarshal.AsSpan(_pendingRigidBodies));
        _pendingRigidBodies.Clear();
    }

    /// <summary>
    /// 清除 world/session 替换前的自由粒子、动态刚体、角色代理与暂存快照。
    /// </summary>
    public void ResetRuntimeState()
    {
        _particles.Clear();
        _pendingRigidBodies.Clear();
        _physics?.ResetRuntimeState();
    }

    /// <summary>
    /// 将当前自由粒子活跃前缀转换为存档 DTO。
    /// </summary>
    /// <param name="destination">目标快照缓冲区。</param>
    public void CopyFreeParticles(Span<FreeParticleSnapshot> destination)
    {
        ReadOnlySpan<Particle> active = _particles.ActiveReadOnly;
        if (destination.Length < active.Length)
        {
            throw new ArgumentException("自由粒子快照目标缓冲区不足。", nameof(destination));
        }

        for (int i = 0; i < active.Length; i++)
        {
            Particle particle = active[i];
            destination[i] = new FreeParticleSnapshot(
                particle.X,
                particle.Y,
                particle.Vx,
                particle.Vy,
                particle.Material,
                particle.ColorVariant,
                particle.Life);
        }
    }

    /// <summary>
    /// 导出刚体快照；Physics 后端接入前不应被请求。
    /// </summary>
    /// <param name="destination">目标刚体快照缓冲区。</param>
    public void CopyRigidBodies(Span<SerializationRigidBodySnapshot> destination)
    {
        if (_physics is null)
        {
            if (destination.Length < _pendingRigidBodies.Count)
            {
                throw new ArgumentException("刚体快照目标缓冲区不足。", nameof(destination));
            }

            CollectionsMarshal.AsSpan(_pendingRigidBodies).CopyTo(destination);
            return;
        }

        Physics.RigidBodySnapshot[] runtime = new Physics.RigidBodySnapshot[destination.Length];
        int written = _physics.CopyBodySnapshots(runtime);
        if (written != destination.Length)
        {
            throw new InvalidOperationException("Physics 刚体数量在快照导出期间发生变化。");
        }

        for (int i = 0; i < written; i++)
        {
            Physics.RigidBodySnapshot snapshot = runtime[i];
            BodyLocalMask mask = snapshot.Mask;
            destination[i] = new SerializationRigidBodySnapshot(
                snapshot.BodyKey,
                mask.Width,
                mask.Height,
                mask.SolidBits,
                mask.Materials,
                snapshot.Transform.Position.X,
                snapshot.Transform.Position.Y,
                snapshot.Transform.Cos,
                snapshot.Transform.Sin,
                snapshot.LinearVelocityPixelsPerSecond.X,
                snapshot.LinearVelocityPixelsPerSecond.Y,
                snapshot.AngularVelocityRadiansPerSecond,
                mask.LocalOrigin.X,
                mask.LocalOrigin.Y);
        }
    }

    /// <summary>
    /// 将已完成材质重映射的自由粒子存档 DTO 恢复到 ParticleSystem。
    /// </summary>
    /// <param name="snapshots">自由粒子快照。</param>
    public void RestoreFreeParticles(ReadOnlySpan<FreeParticleSnapshot> snapshots)
    {
        Particle[] particles = new Particle[snapshots.Length];
        for (int i = 0; i < snapshots.Length; i++)
        {
            FreeParticleSnapshot snapshot = snapshots[i];
            particles[i] = new Particle
            {
                X = snapshot.X,
                Y = snapshot.Y,
                Vx = snapshot.Vx,
                Vy = snapshot.Vy,
                Material = snapshot.Material,
                ColorVariant = snapshot.ColorVariant,
                Life = snapshot.Life,
            };
        }

        _particles.RestoreFrom(particles);
    }

    /// <summary>
    /// 恢复刚体快照；Physics 后端接入前暂存，接入后按快照替换当前动态刚体集合。
    /// </summary>
    /// <param name="bodies">刚体快照。</param>
    public void RestoreRigidBodies(ReadOnlySpan<SerializationRigidBodySnapshot> bodies)
    {
        if (_physics is null)
        {
            _pendingRigidBodies.Clear();
            for (int i = 0; i < bodies.Length; i++)
            {
                _pendingRigidBodies.Add(bodies[i]);
            }

            return;
        }

        Physics.RigidBodySnapshot[] runtime = new Physics.RigidBodySnapshot[bodies.Length];
        for (int i = 0; i < bodies.Length; i++)
        {
            SerializationRigidBodySnapshot body = bodies[i];
            BodyLocalMask mask = new(
                body.Width,
                body.Height,
                new Vector2(body.LocalOriginX, body.LocalOriginY),
                body.BodyLocalMask.Span,
                body.Material.Span);
            runtime[i] = new Physics.RigidBodySnapshot(
                body.Id,
                new Transform2D(new Vector2(body.PosX, body.PosY), body.RotCos, body.RotSin),
                new Vector2(body.LinVelX, body.LinVelY),
                body.AngVel,
                mask);
        }

        _ = _physics.RestoreBodySnapshots(runtime);
    }
}
