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
    /// CA 活跃 chunk 少于该值时回退单线程，见架构 §5.7。
    /// </summary>
    public const int SingleThreadChunkThreshold = 4;

    /// <summary>
    /// 流式边界常驻 ring 宽度，单位为 chunk，见架构 §3.4。
    /// </summary>
    public const int BorderRingWidth = 1;

    /// <summary>
    /// cache line 填充宽度，见架构 §12.7。
    /// </summary>
    public const int CacheLineBytes = 64;

    /// <summary>
    /// 默认自由粒子容量，覆盖 20 万活跃粒子目标并留余量，见架构 §7.6。
    /// </summary>
    public const int ParticleCapacityDefault = 262_144;

    /// <summary>
    /// 自由粒子每 tick 重力增量，单位 cell/tick^2，y 轴向下为正。
    /// </summary>
    public const float ParticleGravityPerTick = 0.20f;

    /// <summary>
    /// 自由粒子速度低于该阈值时可进入沉积判定，单位 cell/tick。
    /// </summary>
    public const float ParticleDepositSpeedEpsilon = 0.05f;

    /// <summary>
    /// 自由粒子最大寿命 tick，防止迷途粒子泄漏，见架构 §19 R13。
    /// </summary>
    public const byte ParticleMaxLifetimeTicks = 240;

    /// <summary>
    /// 单 tick cell→particle 抛射数量上限，防止爆炸尖峰。
    /// </summary>
    public const int ParticleEjectMaxPerTick = 4096;

    /// <summary>
    /// GPU compute 默认 work group X 尺寸，见 plan/09 §4.3。
    /// </summary>
    public const int GpuComputeWorkGroupSizeX = 16;

    /// <summary>
    /// GPU compute 默认 work group Y 尺寸，见 plan/09 §4.3。
    /// </summary>
    public const int GpuComputeWorkGroupSizeY = 16;

    /// <summary>
    /// GPU compute 默认 work group Z 尺寸，见 plan/09 §4.3。
    /// </summary>
    public const int GpuComputeWorkGroupSizeZ = 1;

    /// <summary>
    /// Radiance Cascades 默认 cascade 层数，默认关闭时仅作为质量档上限使用，见 plan/09 §4.4。
    /// </summary>
    public const int RadianceCascadeCount = 4;

    /// <summary>
    /// Radiance Cascades 第 0 层角度射线数量，后续层可按质量档扩展，见 plan/09 §4.4。
    /// </summary>
    public const int RadianceCascadeBaseRayCount = 64;

    /// <summary>
    /// Radiance Cascades 第 0 层空间步进像素数。
    /// </summary>
    public const int RadianceCascadeBaseStepPixels = 4;

    /// <summary>
    /// Radiance Cascades 单条射线默认最大步数。
    /// </summary>
    public const int RadianceCascadeMaxRaySteps = 64;
}
