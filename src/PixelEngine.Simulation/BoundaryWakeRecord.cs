namespace PixelEngine.Simulation;

/// <summary>
/// 单次跨 chunk 边界唤醒的内部诊断记录。
/// </summary>
/// <param name="TargetCoord">被唤醒的目标 chunk 坐标。</param>
/// <param name="IncomingSlot">目标 chunk 的入站方向槽索引。</param>
/// <param name="Rect">目标 chunk 本地 dirty rectangle。</param>
internal readonly record struct BoundaryWakeRecord(ChunkCoord TargetCoord, int IncomingSlot, DirtyRect Rect);
