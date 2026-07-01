using System.Diagnostics;
using System.Numerics;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Core.Events;
using PixelEngine.Core.Mathematics;
using PixelEngine.Core.Threading;
using PixelEngine.Interop.Box2D;
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

    private CellGrid Grid { get; }

    /// <summary>
    /// 获取由 <see cref="Initialize"/> 创建的 task bridge worker 数；外部 world 构造时为 0。
    /// </summary>
    public int TaskBridgeWorkerCount => _taskBridge?.WorkerCount ?? 0;

    /// <summary>
    /// 获取 task bridge native 回调兜底捕获的异常次数。
    /// </summary>
    public int TaskBridgeFaultedCallbackCount => _taskBridge?.FaultedCallbackCount ?? 0;

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
    /// 相位 8：排空 damage queue、破坏重建、erase、Box2D step、读回 transform、inverse-sample re-stamp。
    /// </summary>
    /// <param name="dt">固定逻辑步长秒数。</param>
    /// <param name="subStepCount">Box2D sub-step 数，默认 4。</param>
    public void SyncStep(float dt, int subStepCount = 4)
    {
        ObjectDisposedException.ThrowIf(_shutdown, this);
        if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt))
        {
            throw new ArgumentOutOfRangeException(nameof(dt), dt, "dt 必须是有限正数。");
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
        if (_pendingDamage.Count > 0)
        {
            long sumX = 0;
            long sumY = 0;
            for (int i = 0; i < _pendingDamage.Count; i++)
            {
                sumX += _pendingDamage[i].WorldX;
                sumY += _pendingDamage[i].WorldY;
            }

            cellX = (int)(sumX / _pendingDamage.Count);
            cellY = (int)(sumY / _pendingDamage.Count);
        }

        AudioEvent audioEvent = new(
            AudioEventType.RigidbodyShatter,
            cellX,
            cellY,
            materialId: 0,
            magnitude: result.CreatedBodies + result.FragmentPixels,
            count: (ushort)Math.Min(ushort.MaxValue, Math.Max(1, result.DamagedBodies)));
        _ = _eventBus.Channel<AudioEvent>().TryEnqueue(in audioEvent);
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
