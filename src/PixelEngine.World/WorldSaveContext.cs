using PixelEngine.Simulation;

namespace PixelEngine.World;

/// <summary>
/// WorldSaveService 存档时需要读取的 World 一致快照上下文。
/// </summary>
public sealed class WorldSaveContext
{
    /// <summary>
    /// 创建存档上下文。
    /// </summary>
    public WorldSaveContext(
        ResidentChunkMap chunks,
        ResidencyTable residency,
        TemperatureField temperature,
        MaterialTable materials,
        ulong worldSeed,
        long gameTimeTicks,
        ReadOnlyMemory<byte> playerStateBlob,
        bool isFrameBoundary)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(residency);
        ArgumentNullException.ThrowIfNull(temperature);
        ArgumentNullException.ThrowIfNull(materials);
        if (gameTimeTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gameTimeTicks), "游戏时间不能为负。");
        }

        Chunks = chunks;
        Residency = residency;
        Temperature = temperature;
        Materials = materials;
        WorldSeed = worldSeed;
        GameTimeTicks = gameTimeTicks;
        PlayerStateBlob = playerStateBlob;
        IsFrameBoundary = isFrameBoundary;
    }

    /// <summary>
    /// live chunk map；调用者必须保证当前位于相位 2 / 暂停点。
    /// </summary>
    public ResidentChunkMap Chunks { get; }

    /// <summary>
    /// World 侧驻留元数据。
    /// </summary>
    public ResidencyTable Residency { get; }

    /// <summary>
    /// 温度场。
    /// </summary>
    public TemperatureField Temperature { get; }

    /// <summary>
    /// 当前运行时材质表。
    /// </summary>
    public MaterialTable Materials { get; }

    /// <summary>
    /// 世界种子。
    /// </summary>
    public ulong WorldSeed { get; }

    /// <summary>
    /// 当前游戏时间 tick。
    /// </summary>
    public long GameTimeTicks { get; }

    /// <summary>
    /// 宿主提供的不透明玩家状态。
    /// </summary>
    public ReadOnlyMemory<byte> PlayerStateBlob { get; }

    /// <summary>
    /// 调用方确认当前处于相位 2 或专门暂停点，可读取一致快照。
    /// </summary>
    public bool IsFrameBoundary { get; }
}
