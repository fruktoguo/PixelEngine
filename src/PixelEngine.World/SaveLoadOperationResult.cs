namespace PixelEngine.World;

/// <summary>
/// 存档点摘要。
/// </summary>
/// <param name="Id">存档点 id。</param>
/// <param name="Path">存档目录绝对路径。</param>
/// <param name="TimestampUtc">manifest 修改时间。</param>
/// <param name="FormatVersion">manifest 格式版本。</param>
/// <param name="WorldSeed">世界种子。</param>
/// <param name="GameTimeTicks">游戏时间 tick。</param>
/// <param name="ChunkCount">chunk 数量。</param>
public readonly record struct SaveSlotInfo(
    string Id,
    string Path,
    DateTimeOffset TimestampUtc,
    int FormatVersion,
    ulong WorldSeed,
    long GameTimeTicks,
    int ChunkCount);

/// <summary>
/// 存读档操作结果。
/// </summary>
/// <param name="Success">操作是否成功。</param>
/// <param name="Message">诊断文本。</param>
/// <param name="Slot">相关存档点。</param>
/// <param name="LoadResult">读档结果；保存操作时为 null。</param>
public readonly record struct SaveLoadOperationResult(
    bool Success,
    string Message,
    SaveSlotInfo? Slot,
    WorldLoadResult? LoadResult);
