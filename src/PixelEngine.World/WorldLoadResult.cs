namespace PixelEngine.World;

/// <summary>
/// WorldSaveService 读档结果。
/// </summary>
public readonly record struct WorldLoadResult(
    long GameTimeTicks,
    ulong WorldSeed,
    int LoadedChunkCount,
    long MaterialFallbackHitCount);
