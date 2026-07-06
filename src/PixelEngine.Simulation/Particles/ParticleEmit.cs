namespace PixelEngine.Simulation.Particles;

/// <summary>
/// 富速度分布粒子发射请求；用于枪口火花、光束命中火花等不读取 cell 的直接发射。
/// </summary>
/// <param name="X">发射原点 X 坐标。</param>
/// <param name="Y">发射原点 Y 坐标。</param>
/// <param name="Material">粒子材质 id；0 表示不发射。</param>
/// <param name="Count">请求发射的粒子数量。</param>
/// <param name="DirAngleRad">中心方向角，单位弧度。</param>
/// <param name="DirSpreadRad">方向半角扩散，单位弧度。</param>
/// <param name="BaseSpeed">基础速度；后端消费时为每 tick 位移，脚本 facade 输入时为 cell/秒 并在 flush 时缩放。</param>
/// <param name="SpeedJitter">速度抖动半径；实际速度落在 <c>BaseSpeed±SpeedJitter</c> 后钳到非负。</param>
/// <param name="LifeTicks">粒子 lifetime；0 表示使用粒子系统默认最大 lifetime。</param>
public readonly record struct ParticleEmit(
    float X,
    float Y,
    ushort Material,
    int Count,
    float DirAngleRad,
    float DirSpreadRad,
    float BaseSpeed,
    float SpeedJitter,
    ushort LifeTicks);
