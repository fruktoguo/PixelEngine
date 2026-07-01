namespace PixelEngine.Simulation;

/// <summary>
/// CA 本帧实际迭代的 chunk dirty rectangle 快照，用于调试叠层确认 sleeping 区未被扫描。
/// </summary>
public readonly record struct CaIterationSnapshot(ChunkCoord Coord, DirtyRect Rect);
