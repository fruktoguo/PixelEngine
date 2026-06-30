namespace PixelEngine.Simulation;

internal readonly record struct BoundaryWakeRecord(ChunkCoord TargetCoord, int IncomingSlot, DirtyRect Rect);
