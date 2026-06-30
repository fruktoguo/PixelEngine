namespace PixelEngine.Core;

/// <summary>
/// PixelEngine 的编译期常量入口。
/// </summary>
public static partial class EngineConstants
{
    /// <summary>
    /// chunk 边长为 64px，见架构 §5.1。
    /// </summary>
    public const int ChunkSize = 64;

    /// <summary>
    /// chunk 边长的 log2 值，见架构 §5.1。
    /// </summary>
    public const int ChunkSizeLog2 = 6;

    /// <summary>
    /// 单个 chunk 的 cell 数量，见架构 §5.1。
    /// </summary>
    public const int ChunkArea = ChunkSize * ChunkSize;

    /// <summary>
    /// 单步移动上限为 32px，见架构 §5.8。
    /// </summary>
    public const int MoveCap = 32;

    /// <summary>
    /// 跨界写 halo 宽度等于移动上限，见架构 §5.7。
    /// </summary>
    public const int HaloSize = MoveCap;

    /// <summary>
    /// 物理世界比例：16 像素等于 1 米，见架构 §8.1。
    /// </summary>
    public const int PhysicsPixelsPerMeter = 16;

    /// <summary>
    /// 单像素对应的物理米数，见架构 §8.1。
    /// </summary>
    public const float MetersPerPixel = 1f / PhysicsPixelsPerMeter;

    /// <summary>
    /// 温度场降采样倍率，见架构 §7.5。
    /// </summary>
    public const int TempFieldDownscale = 4;

    /// <summary>
    /// 默认 sim 频率为 60Hz，见架构 §4.1。
    /// </summary>
    public const double DefaultSimHz = 60.0;

    /// <summary>
    /// 降级 sim 频率为 30Hz，见架构 §4.2。
    /// </summary>
    public const double SimHzDownscaled = 30.0;

    /// <summary>
    /// dirty rectangle 额外扩张像素，见架构 §5.4。
    /// </summary>
    public const int DirtyRectPadding = 2;

    /// <summary>
    /// 流式边界常驻 ring 宽度，单位为 chunk，见架构 §3.4。
    /// </summary>
    public const int BorderRingWidth = 1;

    /// <summary>
    /// cache line 填充宽度，见架构 §12.7。
    /// </summary>
    public const int CacheLineBytes = 64;
}
