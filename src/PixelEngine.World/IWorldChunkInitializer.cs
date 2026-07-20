using PixelEngine.Core;
using PixelEngine.Simulation;

namespace PixelEngine.World;

/// <summary>
/// 为流式世界中尚未落盘的 chunk 生成初始内容。
/// </summary>
/// <remarks>
/// 同一实例可被持久 <see cref="Core.Threading.JobSystem" /> 并行调用。实现必须只依赖稳定 seed、
/// <see cref="WorldChunkInitializationContext" /> 与不可变配置，不得访问 live resident world。
/// </remarks>
public interface IWorldChunkInitializer
{
    /// <summary>
    /// 初始化一个已经清空、尚未发布到 live world 的 chunk。
    /// </summary>
    /// <param name="context">当前 chunk 的全局坐标与可写初始数据。</param>
    void Initialize(in WorldChunkInitializationContext context);
}

/// <summary>
/// 缺失 chunk 初始化上下文；只开放权威初始材质与降采样温度，瞬时 flags / lifetime / damage 保持清零。
/// </summary>
public readonly ref struct WorldChunkInitializationContext
{
    internal WorldChunkInitializationContext(Chunk chunk, Span<Half> temperatureCells)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (temperatureCells.Length != TemperatureField.BlockArea)
        {
            throw new ArgumentException("温度子块长度必须等于 TemperatureField.BlockArea。", nameof(temperatureCells));
        }

        ChunkX = chunk.Coord.X;
        ChunkY = chunk.Coord.Y;
        OriginCellX = (long)chunk.Coord.X * EngineConstants.ChunkSize;
        OriginCellY = (long)chunk.Coord.Y * EngineConstants.ChunkSize;
        MaterialCells = chunk.MaterialBuffer;
        TemperatureCells = temperatureCells;
    }

    /// <summary>
    /// chunk X 坐标。
    /// </summary>
    public int ChunkX { get; }

    /// <summary>
    /// chunk Y 坐标。
    /// </summary>
    public int ChunkY { get; }

    /// <summary>
    /// chunk 左上角的全局 cell X 坐标。
    /// </summary>
    public long OriginCellX { get; }

    /// <summary>
    /// chunk 左上角的全局 cell Y 坐标。
    /// </summary>
    public long OriginCellY { get; }

    /// <summary>
    /// chunk 边长，单位 cell。
    /// </summary>
    public int SizeCells => EngineConstants.ChunkSize;

    /// <summary>
    /// 温度子块边长；温度场按 <see cref="EngineConstants.TempFieldDownscale" /> 降采样。
    /// </summary>
    public int TemperatureSizeCells => TemperatureField.BlockSize;

    /// <summary>
    /// 以 row-major 排列的 64x64 运行时材质 id。
    /// </summary>
    public Span<ushort> MaterialCells { get; }

    /// <summary>
    /// 以 row-major 排列的降采样初始温度。
    /// </summary>
    public Span<Half> TemperatureCells { get; }
}
