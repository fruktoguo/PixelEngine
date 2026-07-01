namespace PixelEngine.Physics;

/// <summary>
/// 表示物理系统最近一次同步后的轻量诊断计数。
/// </summary>
/// <param name="ActiveBodyCount">当前活跃刚体数量。</param>
/// <param name="PendingDamageCount">最近一次同步排空的 damage 事件数量。</param>
/// <param name="LastErasedCellCount">最近一次同步擦除的刚体 cell 数。</param>
/// <param name="LastStampedCellCount">最近一次同步写回的刚体 cell 数。</param>
/// <param name="LastDestructionResult">最近一次刚体破坏重建结果。</param>
/// <param name="TaskBridgeWorkerCount">Box2D task bridge worker 数；未接管 world 时为 0。</param>
/// <param name="TaskBridgeFaultedCallbackCount">Box2D task bridge native 回调兜底捕获的异常次数。</param>
public readonly record struct PhysicsSystemStats(
    int ActiveBodyCount,
    int PendingDamageCount,
    int LastErasedCellCount,
    int LastStampedCellCount,
    RigidDestructionResult LastDestructionResult,
    int TaskBridgeWorkerCount,
    int TaskBridgeFaultedCallbackCount);
