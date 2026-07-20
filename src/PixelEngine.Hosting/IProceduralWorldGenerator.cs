using PixelEngine.Scripting;
using PixelEngine.Simulation;

namespace PixelEngine.Hosting;

/// <summary>
/// Hosting 程序化世界生成器；用于 <see cref="SceneSourceKind.Procedural" /> 场景在运行时构建起始世界。
/// </summary>
public interface IProceduralWorldGenerator
{
    /// <summary>
    /// 读取程序化世界的尺寸与确定性种子。Hosting 会先据此装配 resident Simulation world。
    /// </summary>
    /// <param name="request">构建请求，包含生成器键与材质查询能力。</param>
    /// <returns>程序化世界描述。</returns>
    ProceduralWorldDescriptor Describe(in ProceduralWorldBuildRequest request);

    /// <summary>
    /// 在已装配的 Simulation world 上写入初始 cell / 温度等内容。
    /// </summary>
    /// <param name="context">程序化构建上下文。</param>
    void Populate(in ProceduralWorldBuildContext context);
}

/// <summary>
/// Hosting 流式程序化世界生成器；只为尚未入盘的 chunk 生成初始内容。
/// </summary>
/// <remarks>
/// <see cref="PopulateChunk" /> 可由引擎 worker 并行调用。实现必须是线程安全的全局坐标纯函数，
/// 不得读取 live world 或依赖 chunk 的生成顺序。
/// </remarks>
public interface IStreamingProceduralWorldGenerator
{
    /// <summary>
    /// 读取无限世界 seed、初始焦点与持久化身份。
    /// </summary>
    /// <param name="request">构建请求，包含生成器键与材质查询能力。</param>
    /// <returns>无限程序化世界描述。</returns>
    ProceduralWorldDescriptor Describe(in ProceduralWorldBuildRequest request);

    /// <summary>
    /// 初始化一个尚未入盘、尚未发布到 live world 的 chunk。
    /// </summary>
    /// <param name="context">chunk 全局坐标、材质查询和可写初始数据。</param>
    void PopulateChunk(in ProceduralChunkBuildContext context);
}

/// <summary>
/// 程序化世界的空间范围类型。
/// </summary>
public enum ProceduralWorldExtent
{
    /// <summary>
    /// 一次性装配固定尺寸 resident world。
    /// </summary>
    Finite,

    /// <summary>
    /// 由相机驱动 chunk 流送、没有预制边界的无限世界。
    /// </summary>
    Infinite,
}

/// <summary>
/// 程序化世界描述；Hosting 用它确定 resident chunk 范围和 deterministic seed。
/// </summary>
/// <param name="WidthCells">世界宽度，单位 cell。</param>
/// <param name="HeightCells">世界高度，单位 cell。</param>
/// <param name="WorldSeed">世界确定性种子。</param>
/// <param name="FrameIndex">初始帧序号。</param>
public readonly record struct ProceduralWorldDescriptor(
    int WidthCells,
    int HeightCells,
    ulong WorldSeed = 0,
    uint FrameIndex = 0)
{
    /// <summary>
    /// 世界空间范围；默认保持既有有限 resident world 语义。
    /// </summary>
    public ProceduralWorldExtent Extent { get; init; } = ProceduralWorldExtent.Finite;

    /// <summary>
    /// 无限世界初始相机焦点 X，单位 cell。
    /// </summary>
    public long InitialFocusX { get; init; }

    /// <summary>
    /// 无限世界初始相机焦点 Y，单位 cell。
    /// </summary>
    public long InitialFocusY { get; init; }

    /// <summary>
    /// 无限世界稳定持久化键；改变生成算法或存档不兼容时必须升级该键。
    /// </summary>
    public string? PersistenceKey { get; init; }

    /// <summary>
    /// 创建没有预制边界的流式程序化世界描述。
    /// </summary>
    /// <param name="worldSeed">确定性世界 seed。</param>
    /// <param name="initialFocusX">初始相机焦点 X，单位 cell。</param>
    /// <param name="initialFocusY">初始相机焦点 Y，单位 cell。</param>
    /// <param name="persistenceKey">稳定持久化键，只允许 ASCII 字母、数字、点、短横线与下划线。</param>
    /// <param name="frameIndex">初始帧序号。</param>
    /// <returns>无限程序化世界描述。</returns>
    public static ProceduralWorldDescriptor CreateInfinite(
        ulong worldSeed,
        long initialFocusX,
        long initialFocusY,
        string persistenceKey,
        uint frameIndex = 0)
    {
        return new ProceduralWorldDescriptor(0, 0, worldSeed, frameIndex)
        {
            Extent = ProceduralWorldExtent.Infinite,
            InitialFocusX = initialFocusX,
            InitialFocusY = initialFocusY,
            PersistenceKey = persistenceKey,
        }.Validate();
    }

    /// <summary>
    /// 校验描述合法性。
    /// </summary>
    /// <returns>当前描述。</returns>
    public ProceduralWorldDescriptor Validate()
    {
        if (!Enum.IsDefined(Extent))
        {
            throw new ArgumentOutOfRangeException(nameof(Extent), Extent, "未知程序化世界范围类型。");
        }

        if (Extent == ProceduralWorldExtent.Finite)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(WidthCells);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(HeightCells);
            return this;
        }

        if (WidthCells != 0 || HeightCells != 0)
        {
            throw new ArgumentException("无限程序化世界不能声明固定 WidthCells / HeightCells。");
        }

        ValidatePersistenceKey(PersistenceKey);
        return this;
    }

    private static void ValidatePersistenceKey(string? persistenceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(persistenceKey);
        if (persistenceKey.Length > 80 || persistenceKey is "." or "..")
        {
            throw new ArgumentException("无限世界 persistence key 长度必须为 1-80 且不能是相对目录标记。", nameof(PersistenceKey));
        }

        for (int i = 0; i < persistenceKey.Length; i++)
        {
            char character = persistenceKey[i];
            bool valid = character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_' or '.';
            if (!valid)
            {
                throw new ArgumentException("无限世界 persistence key 只允许 ASCII 字母、数字、点、短横线与下划线。", nameof(PersistenceKey));
            }
        }
    }
}

/// <summary>
/// 程序化世界构建请求。
/// </summary>
/// <param name="Key">场景描述中的程序化生成器键。</param>
/// <param name="Materials">材质查询能力。</param>
public readonly record struct ProceduralWorldBuildRequest(string Key, IMaterialQuery Materials);

/// <summary>
/// 程序化世界填充上下文。
/// </summary>
/// <param name="Key">场景描述中的程序化生成器键。</param>
/// <param name="Materials">材质查询能力。</param>
/// <param name="Edit">Simulation 编辑门面，写入会进入合法 dirty/parity 路径。</param>
/// <param name="WidthCells">世界宽度，单位 cell。</param>
/// <param name="HeightCells">世界高度，单位 cell。</param>
public readonly record struct ProceduralWorldBuildContext(
    string Key,
    IMaterialQuery Materials,
    ISimulationEditApi Edit,
    int WidthCells,
    int HeightCells);

/// <summary>
/// 流式程序化 chunk 构建上下文；材质数组为 64x64，温度数组为 16x16，均按 row-major 排列。
/// </summary>
public readonly ref struct ProceduralChunkBuildContext
{
    internal ProceduralChunkBuildContext(
        string key,
        IMaterialQuery materials,
        int chunkX,
        int chunkY,
        long originCellX,
        long originCellY,
        int sizeCells,
        int temperatureSizeCells,
        Span<ushort> materialCells,
        Span<Half> temperatureCells)
    {
        Key = key;
        Materials = materials;
        ChunkX = chunkX;
        ChunkY = chunkY;
        OriginCellX = originCellX;
        OriginCellY = originCellY;
        SizeCells = sizeCells;
        TemperatureSizeCells = temperatureSizeCells;
        MaterialCells = materialCells;
        TemperatureCells = temperatureCells;
    }

    /// <summary>
    /// 场景描述中的程序化生成器键。
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// 只读材质查询能力。
    /// </summary>
    public IMaterialQuery Materials { get; }

    /// <summary>
    /// chunk X 坐标。
    /// </summary>
    public int ChunkX { get; }

    /// <summary>
    /// chunk Y 坐标。
    /// </summary>
    public int ChunkY { get; }

    /// <summary>
    /// chunk 左上角全局 cell X。
    /// </summary>
    public long OriginCellX { get; }

    /// <summary>
    /// chunk 左上角全局 cell Y。
    /// </summary>
    public long OriginCellY { get; }

    /// <summary>
    /// 材质 chunk 边长。
    /// </summary>
    public int SizeCells { get; }

    /// <summary>
    /// 降采样温度 chunk 边长。
    /// </summary>
    public int TemperatureSizeCells { get; }

    /// <summary>
    /// 可写初始材质 id。
    /// </summary>
    public Span<ushort> MaterialCells { get; }

    /// <summary>
    /// 可写降采样初始温度。
    /// </summary>
    public Span<Half> TemperatureCells { get; }
}
