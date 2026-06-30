namespace PixelEngine.Simulation;

internal readonly record struct ReactionProbeRecord(
    int X1,
    int Y1,
    ushort MaterialA,
    int X2,
    int Y2,
    ushort MaterialB,
    bool CrossesChunkBoundary);
