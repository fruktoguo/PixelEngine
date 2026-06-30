namespace PixelEngine.Simulation.Particles;

/// <summary>
/// 离网格的自由粒子。粒子做弹道运动，飞行时不参与 CA，颜色在渲染相位由材质与 ColorVariant 生成。
/// </summary>
public struct Particle
{
    /// <summary>
    /// 世界 cell 坐标 X，允许亚像素浮点。
    /// </summary>
    public float X;

    /// <summary>
    /// 世界 cell 坐标 Y，y 轴向下。
    /// </summary>
    public float Y;

    /// <summary>
    /// X 方向速度，单位 cell/tick。
    /// </summary>
    public float Vx;

    /// <summary>
    /// Y 方向速度，单位 cell/tick，向下为正。
    /// </summary>
    public float Vy;

    /// <summary>
    /// 运行时材质 id，仅作数组索引，入盘时由 plan/07 映射为稳定 name。
    /// </summary>
    public ushort Material;

    /// <summary>
    /// 渲染相位使用的色彩噪声种子，粒子本身不存 RGBA。
    /// </summary>
    public byte ColorVariant;

    /// <summary>
    /// 剩余寿命 tick，归零后由生命周期 pass 清理。
    /// </summary>
    public byte Life;
}
