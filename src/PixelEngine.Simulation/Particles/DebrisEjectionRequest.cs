namespace PixelEngine.Simulation.Particles;

/// <summary>
/// 结构破坏完成后同步生成碎屑自由粒子的请求。
/// </summary>
/// <param name="CenterX">破坏 cell 的世界 X 坐标。</param>
/// <param name="CenterY">破坏 cell 的世界 Y 坐标。</param>
/// <param name="Material">碎屑粒子材质 id；为 0 时不生成。</param>
/// <param name="Count">请求生成的碎屑数量；0 表示不生成。</param>
/// <param name="BaseSpeed">基础径向速度，单位 cell/tick。</param>
/// <param name="SpeedJitter">额外速度抖动上限，单位 cell/tick。</param>
/// <param name="LifeTicks">粒子寿命；0 表示使用粒子系统寿命上限。</param>
public readonly record struct DebrisEjectionRequest(
    int CenterX,
    int CenterY,
    ushort Material,
    byte Count,
    float BaseSpeed,
    float SpeedJitter,
    byte LifeTicks);
