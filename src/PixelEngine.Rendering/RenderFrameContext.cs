using PixelEngine.Simulation;

namespace PixelEngine.Rendering;

/// <summary>
/// 相位 9 构建 render buffer 所需的只读输入。
/// </summary>
/// <param name="chunks">驻留 chunk 源。</param>
/// <param name="materials">材质表。</param>
/// <param name="temperature">温度场。</param>
/// <param name="camera">相机快照。</param>
/// <param name="simStepped">本帧 sim 是否实际步进。</param>
/// <param name="debugCellColors">可选逐 cell 调试着色钩子。</param>
public sealed class RenderFrameContext(
    IChunkSource chunks,
    MaterialTable materials,
    TemperatureField temperature,
    CameraState camera,
    bool simStepped,
    IDebugCellColorProvider? debugCellColors = null)
{
    /// <summary>
    /// 驻留 chunk 源。
    /// </summary>
    public IChunkSource Chunks { get; } = chunks ?? throw new ArgumentNullException(nameof(chunks));

    /// <summary>
    /// 材质表。
    /// </summary>
    public MaterialTable Materials { get; } = materials ?? throw new ArgumentNullException(nameof(materials));

    /// <summary>
    /// 温度场。
    /// </summary>
    public TemperatureField Temperature { get; } = temperature ?? throw new ArgumentNullException(nameof(temperature));

    /// <summary>
    /// 相机快照。
    /// </summary>
    public CameraState Camera { get; } = camera;

    /// <summary>
    /// 本帧 sim 是否实际执行。为 false 时 render buffer 复用上帧内容。
    /// </summary>
    public bool SimStepped { get; } = simStepped;

    /// <summary>
    /// 可选逐 cell 调试着色钩子；用于 editor overlay，不改变权威 cell 数据。
    /// </summary>
    public IDebugCellColorProvider? DebugCellColors { get; } = debugCellColors;
}
