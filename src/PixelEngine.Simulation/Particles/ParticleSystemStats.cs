namespace PixelEngine.Simulation.Particles;

/// <summary>
/// 自由粒子系统单 tick 诊断计数。
/// </summary>
/// <remarks>
/// 创建自由粒子诊断快照。
/// </remarks>
public readonly record struct ParticleSystemStats(
    int ActiveCount,
    int Capacity,
    int SpawnedThisTick,
    int DepositedThisTick,
    int KilledByLifetimeThisTick,
    int DroppedThisTick,
    int AudioEventsDroppedThisTick,
    int CellDestructionEventsThisTick);
