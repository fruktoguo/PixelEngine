using PixelEngine.Interop.Box2D;
using PixelEngine.Simulation;

namespace PixelEngine.Physics;

/// <summary>
/// 编排相位 8 的 CA↔刚体双向同步。
/// </summary>
/// <param name="worldId">Box2D world id。</param>
/// <param name="physicsWorld">托管刚体表。</param>
/// <param name="grid">权威 cell 网格。</param>
/// <param name="registry">刚体 stamp registry。</param>
/// <param name="damageQueue">相位 4 写入、相位 8a 排空的刚体 damage queue。</param>
public sealed class PhysicsSystem(
    B2WorldId worldId,
    PhysicsWorld physicsWorld,
    CellGrid grid,
    RigidStampRegistry registry,
    RigidDamageQueue? damageQueue = null)
{
    private readonly B2WorldId _worldId = worldId;
    private readonly PhysicsWorld _physicsWorld = physicsWorld ?? throw new ArgumentNullException(nameof(physicsWorld));
    private readonly CellGrid _grid = grid ?? throw new ArgumentNullException(nameof(grid));
    private readonly RigidStampRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly RigidDamageQueue? _damageQueue = damageQueue;
    private readonly List<RigidDamageEvent> _pendingDamage = new(256);
    private RigidDamageEvent[] _damageScratch = GC.AllocateArray<RigidDamageEvent>(256, pinned: true);

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
    /// 相位 8：排空 damage queue、erase、Box2D step、读回 transform、inverse-sample re-stamp。
    /// </summary>
    /// <param name="dt">固定逻辑步长秒数。</param>
    /// <param name="subStepCount">Box2D sub-step 数，默认 4。</param>
    public void SyncStep(float dt, int subStepCount = 4)
    {
        if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt))
        {
            throw new ArgumentOutOfRangeException(nameof(dt), dt, "dt 必须是有限正数。");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(subStepCount);

        DrainDamageQueue();
        LastErasedCellCount = EraseAllBodies();
        _registry.Clear();

        Box2D.b2World_Step(_worldId, dt, subStepCount);

        LastStampedCellCount = StampAllBodies();
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

    private int EraseAllBodies()
    {
        int erased = 0;
        int slotCount = _physicsWorld.BodySlotCount;
        for (int i = 0; i < slotCount; i++)
        {
            if (_physicsWorld.TryGetBody(i, out PixelRigidBody? body))
            {
                erased += RigidBodyRasterizer.EraseAtCurrentTransform(body, _grid, _registry);
            }
        }

        return erased;
    }

    private int StampAllBodies()
    {
        int stamped = 0;
        int slotCount = _physicsWorld.BodySlotCount;
        for (int i = 0; i < slotCount; i++)
        {
            if (!_physicsWorld.TryGetBody(i, out PixelRigidBody? body))
            {
                continue;
            }

            B2Transform nativeTransform = Box2D.b2Body_GetTransform(body.BodyId);
            PixelEngine.Core.Mathematics.Transform2D transform = PhysicsScale.ToTransform2D(nativeTransform);
            stamped += RigidBodyRasterizer.StampInverseSampling(body, in transform, _grid, _registry);
            body.PreviousTransform = transform;
        }

        return stamped;
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
