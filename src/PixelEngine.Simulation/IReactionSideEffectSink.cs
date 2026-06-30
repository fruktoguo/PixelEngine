using PixelEngine.Simulation.Particles;

namespace PixelEngine.Simulation;

/// <summary>
/// 反应副作用接收器。温度、粒子与产烟子系统通过该接口接收 ReactionEngine 的副作用。
/// </summary>
public interface IReactionSideEffectSink
{
    /// <summary>
    /// 注入反应热量。
    /// </summary>
    void AddHeat(int wx, int wy, ushort sourceMaterial, byte heat);

    /// <summary>
    /// 请求把指定 cell 抛射为自由粒子。
    /// </summary>
    bool RequestParticleEjection(in EjectionRequest request);

    /// <summary>
    /// 记录产烟请求。
    /// </summary>
    void EmitSmoke(int wx, int wy, ushort sourceMaterial, byte amount);
}
