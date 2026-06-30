namespace PixelEngine.Simulation.Particles;

/// <summary>
/// cell→particle 抛射请求，通常由爆炸或冲击玩法在相位 1 入队。
/// </summary>
public readonly struct EjectionRequest(
    int centerX,
    int centerY,
    int radius,
    float impulseSpeed,
    float impulseJitter,
    EjectMask mask)
{
    /// <summary>
    /// 抛射圆心的世界 cell X 坐标。
    /// </summary>
    public int CenterX { get; } = centerX;

    /// <summary>
    /// 抛射圆心的世界 cell Y 坐标。
    /// </summary>
    public int CenterY { get; } = centerY;

    /// <summary>
    /// 抛射半径，单位为 cell。
    /// </summary>
    public int Radius { get; } = radius;

    /// <summary>
    /// 径向基础冲量速度，单位为 cell/tick。
    /// </summary>
    public float ImpulseSpeed { get; } = impulseSpeed;

    /// <summary>
    /// 基于确定性 hash 的附加速度抖动上限。
    /// </summary>
    public float ImpulseJitter { get; } = impulseJitter;

    /// <summary>
    /// 可被抛射的 cell 类型掩码。
    /// </summary>
    public EjectMask Mask { get; } = mask;
}
