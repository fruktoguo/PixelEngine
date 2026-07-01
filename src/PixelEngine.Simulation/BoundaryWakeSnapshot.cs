namespace PixelEngine.Simulation;

/// <summary>
/// 单次跨 chunk KeepAlive/dirty 边界唤醒诊断快照。
/// </summary>
/// <param name="TargetCoord">被唤醒的目标 chunk 坐标。</param>
/// <param name="IncomingSlot">目标 chunk 的入站方向槽。</param>
/// <param name="Rect">目标 chunk 本地 dirty rectangle。</param>
public readonly record struct BoundaryWakeSnapshot(ChunkCoord TargetCoord, int IncomingSlot, DirtyRect Rect);
