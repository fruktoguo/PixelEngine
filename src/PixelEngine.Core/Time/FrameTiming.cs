namespace PixelEngine.Core.Time;

/// <summary>
/// 表示单个渲染帧开始时的固定步长时序决策。
/// </summary>
/// <param name="Dt">本帧 sim/physics/particle 共用的固定逻辑步长。</param>
/// <param name="RunSim">本帧是否执行一次 sim step。</param>
/// <param name="RunPhysics">本帧是否执行一次 physics step。</param>
/// <param name="FrameIndex">当前渲染帧索引。</param>
/// <param name="SimTickIndex">当前 sim tick 索引；若本帧不执行 sim，则为已完成 tick 数。</param>
public readonly record struct FrameTiming(
    double Dt,
    bool RunSim,
    bool RunPhysics,
    long FrameIndex,
    long SimTickIndex);
