using System.Diagnostics;
using System.Numerics;
using System.Buffers;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Core.Events;
using PixelEngine.Core.Mathematics;
using PixelEngine.Core.Threading;
using PixelEngine.Interop.Box2D;
using PixelEngine.Physics.Geometry;
using PixelEngine.Simulation;

namespace PixelEngine.Physics;

/// <summary>
/// 编排相位 8 的 CA↔刚体双向同步，并集中提供物理生命周期、诊断、事件与存档快照入口。
/// </summary>
public sealed class PhysicsSystem : IDisposable
{
    private readonly RigidDamageQueue? _damageQueue;
    private readonly RigidBodyDestruction? _destruction;
    private readonly JobSystem? _jobs;
    private readonly FrameProfiler? _profiler;
    private readonly EventBus? _eventBus;
    private readonly StaticTerrainColliders? _staticTerrainColliders;
    private readonly Box2DTaskBridge? _taskBridge;
    private readonly bool _ownsWorld;
    private readonly List<RigidDamageEvent> _pendingDamage = new(256);
    private RigidDamageEvent[] _damageScratch = GC.AllocateArray<RigidDamageEvent>(256, pinned: true);
    private bool _shutdown;

    /// <summary>
    /// 创建物理系统 facade。该构造函数不接管 Box2D world 生命周期，适合测试或外部 world 管理。
    /// </summary>
    /// <param name="worldId">Box2D world id。</param>
    /// <param name="physicsWorld">托管刚体表。</param>
    /// <param name="grid">权威 cell 网格。</param>
    /// <param name="registry">刚体 stamp registry。</param>
    /// <param name="damageQueue">相位 4 写入、相位 8a 排空的刚体 damage queue。</param>
    /// <param name="destruction">可选刚体 damage 重建服务。</param>
    /// <param name="jobs">可选持久线程池，用于相位 8a 的重建准备阶段。</param>
    /// <param name="profiler">可选帧诊断 profiler，用于记录物理细分相位。</param>
    /// <param name="eventBus">可选事件总线，刚体破碎时写入音频事件。</param>
    /// <param name="staticTerrainColliders">可选局部静态地形 collider 管理器。</param>
    public PhysicsSystem(
        B2WorldId worldId,
        PhysicsWorld physicsWorld,
        CellGrid grid,
        RigidStampRegistry registry,
        RigidDamageQueue? damageQueue = null,
        RigidBodyDestruction? destruction = null,
        JobSystem? jobs = null,
        FrameProfiler? profiler = null,
        EventBus? eventBus = null,
        StaticTerrainColliders? staticTerrainColliders = null)
        : this(
            worldId,
            physicsWorld,
            grid,
            registry,
            damageQueue,
            destruction,
            jobs,
            profiler,
            eventBus,
            staticTerrainColliders,
            taskBridge: null,
            ownsWorld: false)
    {
    }

    private PhysicsSystem(
        B2WorldId worldId,
        PhysicsWorld physicsWorld,
        CellGrid grid,
        RigidStampRegistry registry,
        RigidDamageQueue? damageQueue,
        RigidBodyDestruction? destruction,
        JobSystem? jobs,
        FrameProfiler? profiler,
        EventBus? eventBus,
        StaticTerrainColliders? staticTerrainColliders,
        Box2DTaskBridge? taskBridge,
        bool ownsWorld)
    {
        WorldId = worldId;
        PhysicsWorld = physicsWorld ?? throw new ArgumentNullException(nameof(physicsWorld));
        Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _damageQueue = damageQueue;
        _destruction = destruction;
        _jobs = jobs;
        _profiler = profiler;
        _eventBus = eventBus;
        _staticTerrainColliders = staticTerrainColliders;
        _taskBridge = taskBridge;
        _ownsWorld = ownsWorld;
    }

    /// <summary>
    /// 创建 Box2D world、注入 task bridge，并返回接管 world 生命周期的物理系统 facade。
    /// </summary>
    /// <param name="grid">权威 cell 网格。</param>
    /// <param name="jobs">Box2D task bridge 与重建准备阶段共用的持久线程池。</param>
    /// <param name="physicsWorld">可选托管刚体表；缺省时创建空表。</param>
    /// <param name="registry">可选刚体 stamp registry；缺省时创建空 registry。</param>
    /// <param name="damageQueue">可选 damage queue。</param>
    /// <param name="destruction">可选刚体破坏重建服务。</param>
    /// <param name="profiler">可选帧诊断 profiler。</param>
    /// <param name="eventBus">可选事件总线。</param>
    /// <param name="forceSingleThread">是否强制 Box2D task bridge 串行执行。</param>
    /// <param name="worldDef">可选 Box2D world 定义；为 null 时使用默认定义。</param>
    public static PhysicsSystem Initialize(
        CellGrid grid,
        JobSystem jobs,
        PhysicsWorld? physicsWorld = null,
        RigidStampRegistry? registry = null,
        RigidDamageQueue? damageQueue = null,
        RigidBodyDestruction? destruction = null,
        FrameProfiler? profiler = null,
        EventBus? eventBus = null,
        bool forceSingleThread = false,
        B2WorldDef? worldDef = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(jobs);

        PhysicsScale.ConfigureBox2DLengthUnits();
        Box2DTaskBridge bridge = new(jobs, forceSingleThread);
        B2WorldDef def = worldDef ?? Box2D.b2DefaultWorldDef();
        bridge.ConfigureWorldDef(ref def);
        B2WorldId worldId = Box2D.b2CreateWorld(in def);
        StaticTerrainColliders staticColliders = new(worldId);
        return new PhysicsSystem(
            worldId,
            physicsWorld ?? new PhysicsWorld(),
            grid,
            registry ?? new RigidStampRegistry(),
            damageQueue,
            destruction,
            jobs,
            profiler,
            eventBus,
            staticColliders,
            bridge,
            ownsWorld: true);
    }

    /// <summary>
    /// 获取 Box2D world id。
    /// </summary>
    public B2WorldId WorldId { get; }

    /// <summary>
    /// 获取托管刚体表。
    /// </summary>
    public PhysicsWorld PhysicsWorld { get; }

    /// <summary>
    /// 获取刚体 stamp registry。
    /// </summary>
    public RigidStampRegistry Registry { get; }

    /// <summary>
    /// 获取 Physics 当前耦合的权威 cell 网格。
    /// </summary>
    public CellGrid Grid { get; }

    /// <summary>
    /// 获取由 <see cref="Initialize"/> 创建的 task bridge worker 数；外部 world 构造时为 0。
    /// </summary>
    public int TaskBridgeWorkerCount => _taskBridge?.WorkerCount ?? 0;

    /// <summary>
    /// 获取 task bridge native 回调兜底捕获的异常次数。
    /// </summary>
    public int TaskBridgeFaultedCallbackCount => _taskBridge?.FaultedCallbackCount ?? 0;

    /// <summary>
    /// 当前 PhysicsSystem 负责追踪的 live Box2D body 数，用于 native leak detector 采集关闭前后计数。
    /// </summary>
    public int LiveBodyCount => _shutdown && _ownsWorld
        ? 0
        : PhysicsWorld.ActiveBodyCount + (_staticTerrainColliders?.LiveBodyCount ?? 0);

    /// <summary>
    /// Box2D 内部 step 子步数。相位 8 默认使用该值；它不是额外 CA tick，见架构 §4.1。
    /// </summary>
    public int SubStepCount { get; private set; } = 4;

    /// <summary>
    /// 当前 Box2D world 重力向量。
    /// </summary>
    public Vector2 Gravity
    {
        get
        {
            B2Vec2 gravity = Box2D.b2World_GetGravity(WorldId);
            return new Vector2(gravity.X, gravity.Y);
        }
    }

    /// <summary>
    /// 当前刚体破坏碎片阈值；未配置破坏服务时为 0。
    /// </summary>
    public int FragmentPixelThreshold => _destruction?.FragmentPixelThreshold ?? 0;

    /// <summary>
    /// 最近一次同步排空的 damage 事件。
    /// </summary>
    public IReadOnlyList<RigidDamageEvent> PendingDamage => _pendingDamage;

    /// <summary>
    /// 最近一次同步擦除的刚体 cell 数。
    /// </summary>
    public int LastErasedCellCount { get; private set; }

    /// <summary>
    /// 最近一次同步写回的刚体 cell 数。
    /// </summary>
    public int LastStampedCellCount { get; private set; }

    /// <summary>
    /// 最近一次同步执行的刚体破坏重建结果。
    /// </summary>
    public RigidDestructionResult LastDestructionResult { get; private set; }

    /// <summary>
    /// 最近一次同步后的轻量诊断快照。
    /// </summary>
    public PhysicsSystemStats Stats => new(
        PhysicsWorld.ActiveBodyCount,
        _pendingDamage.Count,
        LastErasedCellCount,
        LastStampedCellCount,
        LastDestructionResult,
        TaskBridgeWorkerCount,
        TaskBridgeFaultedCallbackCount);

    /// <summary>
    /// 设置后续相位 8 默认使用的 Box2D 子步数。
    /// </summary>
    /// <param name="subStepCount">Box2D 内部子步数量。</param>
    public void SetSubStepCount(int subStepCount)
    {
        ObjectDisposedException.ThrowIf(_shutdown, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(subStepCount);
        SubStepCount = subStepCount;
    }

    /// <summary>
    /// 设置 Box2D world 重力，后续 step 立即使用。
    /// </summary>
    /// <param name="gravity">重力向量。</param>
    public void SetGravity(Vector2 gravity)
    {
        ObjectDisposedException.ThrowIf(_shutdown, this);
        if (!float.IsFinite(gravity.X) || !float.IsFinite(gravity.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(gravity), gravity, "重力必须是有限数值。");
        }

        Box2D.b2World_SetGravity(WorldId, new B2Vec2 { X = gravity.X, Y = gravity.Y });
    }

    /// <summary>
    /// 设置后续 damage 重建使用的碎片像素阈值。
    /// </summary>
    /// <param name="fragmentPixelThreshold">小于该像素数的连通块转为自由粒子。</param>
    public void SetFragmentPixelThreshold(int fragmentPixelThreshold)
    {
        ObjectDisposedException.ThrowIf(_shutdown, this);
        if (_destruction is null)
        {
            throw new InvalidOperationException("当前 PhysicsSystem 未配置 RigidBodyDestruction，不能设置碎片阈值。");
        }

        _destruction.SetFragmentPixelThreshold(fragmentPixelThreshold);
    }

    /// <summary>
    /// 相位 8：排空 damage queue、破坏重建、erase、Box2D step、读回 transform、inverse-sample re-stamp。
    /// </summary>
    /// <param name="dt">固定逻辑步长秒数。</param>
    /// <param name="subStepCount">Box2D sub-step 数，默认 4。</param>
    public void SyncStep(float dt, int subStepCount = 0)
    {
        ObjectDisposedException.ThrowIf(_shutdown, this);
        if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt))
        {
            throw new ArgumentOutOfRangeException(nameof(dt), dt, "dt 必须是有限正数。");
        }

        if (subStepCount == 0)
        {
            subStepCount = SubStepCount;
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(subStepCount);

        DrainDamageQueue();
        LastDestructionResult = RebuildDamagedBodies();
        PublishShatterAudioEvent(LastDestructionResult);
        LastErasedCellCount = Measure(FrameSubPhase.PhysicsErase, EraseAllBodies);
        Registry.Clear();

        long stepStarted = Stopwatch.GetTimestamp();
        Box2D.b2World_Step(WorldId, dt, subStepCount);
        RecordSub(FrameSubPhase.PhysicsStep, stepStarted);

        LastStampedCellCount = Measure(FrameSubPhase.PhysicsInverseSample, StampAllBodies);
    }

    /// <summary>
    /// 更新由 <see cref="Initialize"/> 创建的局部静态地形 collider，并记录诊断耗时。
    /// </summary>
    /// <param name="chunks">驻留 chunk 源。</param>
    public void UpdateStaticTerrainColliders(IChunkSource chunks)
    {
        ObjectDisposedException.ThrowIf(_shutdown, this);
        if (_staticTerrainColliders is null)
        {
            throw new InvalidOperationException("当前 PhysicsSystem 未配置 StaticTerrainColliders。");
        }

        ArgumentNullException.ThrowIfNull(chunks);
        long started = Stopwatch.GetTimestamp();
        _staticTerrainColliders.Update(chunks, PhysicsWorld);
        RecordSub(FrameSubPhase.StaticCollider, started);
    }

    /// <summary>
    /// 执行像素场角色控制器移动，并记录诊断耗时。
    /// </summary>
    /// <param name="controller">角色控制器。</param>
    /// <param name="desired">请求位移，单位 cell。</param>
    /// <param name="info">移动结果。</param>
    public void MoveCharacter(CharacterController controller, in Vector2 desired, out CharacterCollisionInfo info)
    {
        ObjectDisposedException.ThrowIf(_shutdown, this);
        ArgumentNullException.ThrowIfNull(controller);
        long started = Stopwatch.GetTimestamp();
        controller.Move(in desired, out info);
        RecordSub(FrameSubPhase.CharacterController, started);
    }

    /// <summary>
    /// 将当前全部活跃刚体复制为只读快照；目标 span 不足时只写入可容纳的前 N 个。
    /// </summary>
    /// <param name="destination">快照目标缓冲。</param>
    /// <returns>写入的快照数量。</returns>
    public int CopyBodySnapshots(Span<RigidBodySnapshot> destination)
    {
        ObjectDisposedException.ThrowIf(_shutdown, this);
        int written = 0;
        int slotCount = PhysicsWorld.BodySlotCount;
        for (int i = 0; i < slotCount && written < destination.Length; i++)
        {
            if (!PhysicsWorld.TryGetBody(i, out PixelRigidBody? body))
            {
                continue;
            }

            B2Transform nativeTransform = Box2D.b2Body_GetTransform(body.BodyId);
            B2Vec2 nativeVelocity = Box2D.b2Body_GetLinearVelocity(body.BodyId);
            destination[written++] = new RigidBodySnapshot(
                body.BodyKey,
                PhysicsScale.ToTransform2D(nativeTransform),
                new Vector2(
                    PhysicsScale.PhysicsToPixel(nativeVelocity.X),
                    PhysicsScale.PhysicsToPixel(nativeVelocity.Y)),
                Box2D.b2Body_GetAngularVelocity(body.BodyId),
                body.Mask);
        }

        return written;
    }

    /// <summary>
    /// 从只读快照恢复运行时刚体，重建 Box2D shape、速度与 RigidOwned stamp。
    /// </summary>
    /// <param name="snapshots">刚体快照。</param>
    /// <returns>成功恢复的刚体数量。</returns>
    public int RestoreBodySnapshots(ReadOnlySpan<RigidBodySnapshot> snapshots)
    {
        ObjectDisposedException.ThrowIf(_shutdown, this);
        ClearDynamicBodiesForSnapshotRestore();
        int restored = 0;
        for (int i = 0; i < snapshots.Length; i++)
        {
            RigidBodySnapshot snapshot = snapshots[i];
            BodyLocalMask mask = snapshot.Mask ?? throw new InvalidDataException("刚体快照缺少 body-local mask。");
            if (!RigidBodyMaskShapeBuilder.TryBuildConvexPieces(mask, out ConvexPolygon[] pieces, out int pieceCount))
            {
                throw new InvalidDataException($"刚体 {snapshot.BodyKey} 的 body-local mask 无法生成有效凸片。");
            }

            B2BodyId bodyId = ShapeBuilder.BuildBody(
                WorldId,
                pieces.AsSpan(0, pieceCount),
                snapshot.Transform.Position);
            Box2D.b2Body_SetTransform(
                bodyId,
                new B2Vec2
                {
                    X = PhysicsScale.PixelToPhysics(snapshot.Transform.Position.X),
                    Y = PhysicsScale.PixelToPhysics(snapshot.Transform.Position.Y),
                },
                new B2Rot
                {
                    C = snapshot.Transform.Cos,
                    S = snapshot.Transform.Sin,
                });
            Box2D.b2Body_SetLinearVelocity(
                bodyId,
                new B2Vec2
                {
                    X = PhysicsScale.PixelToPhysics(snapshot.LinearVelocityPixelsPerSecond.X),
                    Y = PhysicsScale.PixelToPhysics(snapshot.LinearVelocityPixelsPerSecond.Y),
                });
            Box2D.b2Body_SetAngularVelocity(bodyId, snapshot.AngularVelocityRadiansPerSecond);
            PixelRigidBody body = PhysicsWorld.AddBody(bodyId, mask, snapshot.BodyKey);
            _ = RigidBodyRasterizer.StampInverseSampling(body, snapshot.Transform, Grid, Registry);
            restored++;
        }

        return restored;
    }

    private void ClearDynamicBodiesForSnapshotRestore()
    {
        int slotCount = PhysicsWorld.BodySlotCount;
        for (int bodyKey = 0; bodyKey < slotCount; bodyKey++)
        {
            if (PhysicsWorld.TryGetBody(bodyKey, out PixelRigidBody? body))
            {
                Box2D.b2DestroyBody(body.BodyId);
            }
        }

        PhysicsWorld.Clear();
        Registry.Clear();
        _pendingDamage.Clear();
        LastErasedCellCount = 0;
        LastStampedCellCount = 0;
        LastDestructionResult = default;
    }

    /// <summary>
    /// 从权威网格中的非空像素区域创建动态刚体，并把源像素转为 RigidOwned stamp。
    /// </summary>
    /// <param name="x">区域左上角 X 坐标。</param>
    /// <param name="y">区域左上角 Y 坐标。</param>
    /// <param name="width">区域宽度。</param>
    /// <param name="height">区域高度。</param>
    /// <returns>新建刚体的 bodyKey。</returns>
    public int CreateBodyFromRegion(int x, int y, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_shutdown, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        int area = checked(width * height);
        byte[] solid = ArrayPool<byte>.Shared.Rent(area);
        ushort[] materials = ArrayPool<ushort>.Shared.Rent(area);

        try
        {
            solid.AsSpan(0, area).Clear();
            materials.AsSpan(0, area).Clear();
            int solidCount = CopyRegionMask(x, y, width, height, solid.AsSpan(0, area), materials.AsSpan(0, area));
            if (solidCount == 0)
            {
                throw new InvalidOperationException("指定区域没有可转换为刚体的非空像素。");
            }

            Vector2 localOrigin = new(width * 0.5f, height * 0.5f);
            BodyLocalMask mask = new(width, height, localOrigin, solid.AsSpan(0, area), materials.AsSpan(0, area));
            if (!RigidBodyMaskShapeBuilder.TryBuildConvexPieces(mask, out ConvexPolygon[] pieces, out int pieceCount))
            {
                throw new InvalidOperationException("指定区域无法生成有效刚体凸片。");
            }

            Vector2 position = new(x + localOrigin.X, y + localOrigin.Y);
            B2BodyId bodyId = ShapeBuilder.BuildBody(WorldId, pieces.AsSpan(0, pieceCount), position);
            PixelRigidBody body = PhysicsWorld.AddBody(bodyId, mask);
            ClearRegionMaskSource(x, y, width, height, solid.AsSpan(0, area));
            Transform2D transform = new(position, 1f, 0f);
            _ = RigidBodyRasterizer.StampInverseSampling(body, in transform, Grid, Registry);
            body.PreviousTransform = transform;
            return body.BodyKey;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(solid, clearArray: true);
            ArrayPool<ushort>.Shared.Return(materials, clearArray: true);
        }
    }

    /// <summary>
    /// 尝试读取刚体当前 Box2D 变换。
    /// </summary>
    /// <param name="bodyKey">刚体 bodyKey。</param>
    /// <param name="transform">读取成功时返回像素坐标系变换。</param>
    /// <returns>刚体存在时返回 true。</returns>
    public bool TryGetBodyTransform(int bodyKey, out Transform2D transform)
    {
        ObjectDisposedException.ThrowIf(_shutdown, this);
        if (!PhysicsWorld.TryGetBody(bodyKey, out PixelRigidBody? body))
        {
            transform = default;
            return false;
        }

        transform = PhysicsScale.ToTransform2D(Box2D.b2Body_GetTransform(body.BodyId));
        return true;
    }

    /// <summary>
    /// 对指定刚体施加线性冲量，冲量单位为像素质量单位每秒。
    /// </summary>
    /// <param name="bodyKey">刚体 bodyKey。</param>
    /// <param name="impulsePixelsX">X 方向冲量。</param>
    /// <param name="impulsePixelsY">Y 方向冲量。</param>
    /// <returns>刚体存在并已施加时返回 true。</returns>
    public bool ApplyLinearImpulse(int bodyKey, float impulsePixelsX, float impulsePixelsY)
    {
        ObjectDisposedException.ThrowIf(_shutdown, this);
        if (!float.IsFinite(impulsePixelsX) || !float.IsFinite(impulsePixelsY))
        {
            throw new ArgumentOutOfRangeException(nameof(impulsePixelsX), "冲量必须是有限数值。");
        }

        if (!PhysicsWorld.TryGetBody(bodyKey, out PixelRigidBody? body))
        {
            return false;
        }

        B2Vec2 point = Box2D.b2Body_GetPosition(body.BodyId);
        Box2D.b2Body_ApplyLinearImpulse(
            body.BodyId,
            new B2Vec2
            {
                X = PhysicsScale.PixelToPhysics(impulsePixelsX),
                Y = PhysicsScale.PixelToPhysics(impulsePixelsY),
            },
            point,
            wake: 1);
        return true;
    }

    /// <summary>
    /// 对半径内全部活跃刚体施加由中心向外的径向冲量。
    /// </summary>
    /// <param name="centerX">爆炸中心 X 坐标，单位像素。</param>
    /// <param name="centerY">爆炸中心 Y 坐标，单位像素。</param>
    /// <param name="radius">作用半径，单位像素。</param>
    /// <param name="impulsePixels">中心处冲量强度，单位为像素质量单位每秒。</param>
    /// <returns>成功施加冲量的刚体数量。</returns>
    public int ApplyRadialImpulse(float centerX, float centerY, float radius, float impulsePixels)
    {
        ObjectDisposedException.ThrowIf(_shutdown, this);
        if (!float.IsFinite(centerX) || !float.IsFinite(centerY) ||
            !float.IsFinite(radius) || !float.IsFinite(impulsePixels))
        {
            throw new ArgumentOutOfRangeException(nameof(centerX), "径向冲量参数必须是有限数值。");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(impulsePixels);
        float radiusSquared = radius * radius;
        int applied = 0;
        int slotCount = PhysicsWorld.BodySlotCount;
        for (int bodyKey = 0; bodyKey < slotCount; bodyKey++)
        {
            if (!PhysicsWorld.TryGetBody(bodyKey, out PixelRigidBody? body))
            {
                continue;
            }

            Transform2D transform = PhysicsScale.ToTransform2D(Box2D.b2Body_GetTransform(body.BodyId));
            float dx = transform.Position.X - centerX;
            float dy = transform.Position.Y - centerY;
            float distanceSquared = (dx * dx) + (dy * dy);
            if (distanceSquared > radiusSquared)
            {
                continue;
            }

            float distance = MathF.Sqrt(distanceSquared);
            float normalX;
            float normalY;
            if (distance <= 0.001f)
            {
                normalX = 0f;
                normalY = -1f;
                distance = 0f;
            }
            else
            {
                float invDistance = 1f / distance;
                normalX = dx * invDistance;
                normalY = dy * invDistance;
            }

            float falloff = 1f - Math.Clamp(distance / radius, 0f, 1f);
            if (falloff <= 0f)
            {
                continue;
            }

            B2Vec2 point = Box2D.b2Body_GetPosition(body.BodyId);
            Box2D.b2Body_ApplyLinearImpulse(
                body.BodyId,
                new B2Vec2
                {
                    X = PhysicsScale.PixelToPhysics(normalX * impulsePixels * falloff),
                    Y = PhysicsScale.PixelToPhysics(normalY * impulsePixels * falloff),
                },
                point,
                wake: 1);
            applied++;
        }

        return applied;
    }

    /// <summary>
    /// 销毁指定刚体，并清除它上一帧 stamp 的 RigidOwned 像素。
    /// </summary>
    /// <param name="bodyKey">刚体 bodyKey。</param>
    /// <returns>刚体存在并已销毁时返回 true。</returns>
    public bool DestroyBody(int bodyKey)
    {
        ObjectDisposedException.ThrowIf(_shutdown, this);
        if (!PhysicsWorld.TryGetBody(bodyKey, out PixelRigidBody? body))
        {
            return false;
        }

        _ = RigidBodyRasterizer.EraseAtCurrentTransform(body, Grid, Registry);
        Box2D.b2DestroyBody(body.BodyId);
        PhysicsWorld.RemoveBody(bodyKey);
        return true;
    }

    /// <summary>
    /// 将当前活跃刚体 mask 的 CCL 连通块复制为 editor 调试快照；目标 span 不足时只写入可容纳的前 N 个。
    /// </summary>
    public int CopyConnectedComponentDebugSnapshots(Span<ConnectedComponentDebugSnapshot> destination, int fragmentPixelThreshold = 1)
    {
        ObjectDisposedException.ThrowIf(_shutdown, this);
        ArgumentOutOfRangeException.ThrowIfNegative(fragmentPixelThreshold);
        int written = 0;
        int slotCount = PhysicsWorld.BodySlotCount;
        for (int i = 0; i < slotCount && written < destination.Length; i++)
        {
            if (!PhysicsWorld.TryGetBody(i, out PixelRigidBody? body))
            {
                continue;
            }

            B2Transform nativeTransform = Box2D.b2Body_GetTransform(body.BodyId);
            Transform2D transform = PhysicsScale.ToTransform2D(nativeTransform);
            written += CopyBodyConnectedComponents(body, in transform, fragmentPixelThreshold, destination[written..]);
        }

        return written;
    }

    /// <summary>
    /// 关闭物理系统，按所有权销毁 Box2D world 与 task bridge。
    /// </summary>
    public void Shutdown()
    {
        if (_shutdown)
        {
            return;
        }

        _shutdown = true;
        _staticTerrainColliders?.Dispose();
        if (_ownsWorld)
        {
            Box2D.b2DestroyWorld(WorldId);
            PhysicsWorld.Clear();
        }

        _taskBridge?.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Shutdown();
    }

    private RigidDestructionResult RebuildDamagedBodies()
    {
        if (_destruction is null)
        {
            return default;
        }

        RigidDestructionResult result = _destruction.RebuildDirty(WorldId, PhysicsWorld, Grid, Registry, _pendingDamage, _jobs);
        _profiler?.RecordSub(FrameSubPhase.PhysicsCcl, _destruction.LastPreparationMilliseconds);
        _profiler?.RecordSub(FrameSubPhase.ShapeRebuild, _destruction.LastApplyMilliseconds);
        return result;
    }

    private void DrainDamageQueue()
    {
        _pendingDamage.Clear();
        if (_damageQueue is null)
        {
            return;
        }

        while (true)
        {
            int drained = _damageQueue.DrainTo(_damageScratch);
            if (drained == 0)
            {
                return;
            }

            EnsurePendingCapacity(_pendingDamage.Count + drained);
            for (int i = 0; i < drained; i++)
            {
                _pendingDamage.Add(_damageScratch[i]);
            }

            if (drained < _damageScratch.Length)
            {
                return;
            }
        }
    }

    private void PublishShatterAudioEvent(in RigidDestructionResult result)
    {
        if (_eventBus is null || (result.CreatedBodies == 0 && result.FragmentPixels == 0))
        {
            return;
        }

        int cellX = 0;
        int cellY = 0;
        ushort material = 0;
        if (_pendingDamage.Count > 0)
        {
            long sumX = 0;
            long sumY = 0;
            for (int i = 0; i < _pendingDamage.Count; i++)
            {
                RigidDamageEvent damage = _pendingDamage[i];
                sumX += damage.WorldX;
                sumY += damage.WorldY;
                if (material == 0)
                {
                    material = damage.Material;
                }
            }

            cellX = (int)(sumX / _pendingDamage.Count);
            cellY = (int)(sumY / _pendingDamage.Count);
        }

        AudioEvent audioEvent = new(
            AudioEventType.RigidbodyShatter,
            cellX,
            cellY,
            material,
            magnitude: result.CreatedBodies + result.FragmentPixels,
            count: (ushort)Math.Min(ushort.MaxValue, Math.Max(1, result.DamagedBodies)));
        _ = _eventBus.Channel<AudioEvent>().TryEnqueue(in audioEvent);
    }

    private int CopyRegionMask(
        int worldX,
        int worldY,
        int width,
        int height,
        Span<byte> solid,
        Span<ushort> materials)
    {
        int solidCount = 0;
        for (int ly = 0; ly < height; ly++)
        {
            for (int lx = 0; lx < width; lx++)
            {
                int index = (ly * width) + lx;
                int wx = worldX + lx;
                int wy = worldY + ly;
                ushort material = Grid.GetMaterial(wx, wy);
                byte flags = Grid.FlagsAt(wx, wy);
                if (material == 0 || CellFlags.Has(flags, CellFlags.RigidOwned))
                {
                    continue;
                }

                solid[index] = 1;
                materials[index] = material;
                solidCount++;
            }
        }

        return solidCount;
    }

    private void ClearRegionMaskSource(int worldX, int worldY, int width, int height, ReadOnlySpan<byte> solid)
    {
        for (int ly = 0; ly < height; ly++)
        {
            for (int lx = 0; lx < width; lx++)
            {
                int index = (ly * width) + lx;
                if (solid[index] == 0)
                {
                    continue;
                }

                int wx = worldX + lx;
                int wy = worldY + ly;
                Grid.MaterialAt(wx, wy) = 0;
                Grid.FlagsAt(wx, wy) = 0;
                Grid.LifetimeAt(wx, wy) = 0;
                Grid.DamageAt(wx, wy) = 0;
                Grid.MarkDirty(wx, wy);
            }
        }
    }

    private int EraseAllBodies()
    {
        int erased = 0;
        int slotCount = PhysicsWorld.BodySlotCount;
        for (int i = 0; i < slotCount; i++)
        {
            if (PhysicsWorld.TryGetBody(i, out PixelRigidBody? body))
            {
                erased += RigidBodyRasterizer.EraseAtCurrentTransform(body, Grid, Registry);
            }
        }

        return erased;
    }

    private int StampAllBodies()
    {
        int stamped = 0;
        int slotCount = PhysicsWorld.BodySlotCount;
        for (int i = 0; i < slotCount; i++)
        {
            if (!PhysicsWorld.TryGetBody(i, out PixelRigidBody? body))
            {
                continue;
            }

            B2Transform nativeTransform = Box2D.b2Body_GetTransform(body.BodyId);
            Transform2D transform = PhysicsScale.ToTransform2D(nativeTransform);
            stamped += RigidBodyRasterizer.StampInverseSampling(body, in transform, Grid, Registry);
            body.PreviousTransform = transform;
        }

        return stamped;
    }

    private static int CopyBodyConnectedComponents(
        PixelRigidBody body,
        in Transform2D transform,
        int fragmentPixelThreshold,
        Span<ConnectedComponentDebugSnapshot> destination)
    {
        BodyLocalMask mask = body.Mask;
        int area = mask.Width * mask.Height;
        int[] labels = ArrayPool<int>.Shared.Rent(area);
        ConnectedComponent[] components = ArrayPool<ConnectedComponent>.Shared.Rent(area);
        try
        {
            int componentCount = ConnectedComponentLabeler.Label(
                mask.SolidBits,
                mask.Width,
                mask.Height,
                labels.AsSpan(0, area),
                components.AsSpan(0, area),
                Connectivity.Four,
                fragmentPixelThreshold);
            int written = 0;
            for (int i = 0; i < componentCount && written < destination.Length; i++)
            {
                ConnectedComponent component = components[i];
                destination[written++] = new ConnectedComponentDebugSnapshot(
                    body.BodyKey,
                    component.Label,
                    component.PixelCount,
                    LocalBoundsToWorld(mask, in transform, component.Bounds),
                    component.IsFragment);
            }

            return written;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(labels);
            ArrayPool<ConnectedComponent>.Shared.Return(components);
        }
    }

    private static RectI LocalBoundsToWorld(BodyLocalMask mask, in Transform2D transform, in RectI bounds)
    {
        Vector2 origin = mask.LocalOrigin;
        Vector2 p0 = transform.TransformPoint(new Vector2(bounds.MinX, bounds.MinY) - origin);
        Vector2 p1 = transform.TransformPoint(new Vector2(bounds.MaxX, bounds.MinY) - origin);
        Vector2 p2 = transform.TransformPoint(new Vector2(bounds.MaxX, bounds.MaxY) - origin);
        Vector2 p3 = transform.TransformPoint(new Vector2(bounds.MinX, bounds.MaxY) - origin);

        float minX = MathF.Min(MathF.Min(p0.X, p1.X), MathF.Min(p2.X, p3.X));
        float minY = MathF.Min(MathF.Min(p0.Y, p1.Y), MathF.Min(p2.Y, p3.Y));
        float maxX = MathF.Max(MathF.Max(p0.X, p1.X), MathF.Max(p2.X, p3.X));
        float maxY = MathF.Max(MathF.Max(p0.Y, p1.Y), MathF.Max(p2.Y, p3.Y));

        return RectI.FromBounds(
            (int)MathF.Floor(minX),
            (int)MathF.Floor(minY),
            (int)MathF.Ceiling(maxX),
            (int)MathF.Ceiling(maxY));
    }

    private int Measure(FrameSubPhase phase, Func<int> action)
    {
        long started = Stopwatch.GetTimestamp();
        int result = action();
        RecordSub(phase, started);
        return result;
    }

    private void RecordSub(FrameSubPhase phase, long started)
    {
        if (_profiler is null)
        {
            return;
        }

        long elapsed = Stopwatch.GetTimestamp() - started;
        _profiler.RecordSub(phase, elapsed * 1000.0 / Stopwatch.Frequency);
    }

    private void EnsurePendingCapacity(int required)
    {
        if (_pendingDamage.Capacity < required)
        {
            _pendingDamage.Capacity = required;
        }

        if (_damageScratch.Length >= required)
        {
            return;
        }

        int capacity = _damageScratch.Length;
        while (capacity < required)
        {
            capacity *= 2;
        }

        _damageScratch = GC.AllocateArray<RigidDamageEvent>(capacity, pinned: true);
    }
}
