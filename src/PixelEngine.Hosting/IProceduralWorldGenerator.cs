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
    /// 校验描述合法性。
    /// </summary>
    /// <returns>当前描述。</returns>
    public ProceduralWorldDescriptor Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(WidthCells);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(HeightCells);
        return this;
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
