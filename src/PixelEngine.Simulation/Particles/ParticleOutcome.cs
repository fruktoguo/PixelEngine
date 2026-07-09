namespace PixelEngine.Simulation.Particles;

/// <summary>
/// 单颗粒子单步模拟后的结果：继续飞行、请求沉积或死亡。
/// </summary>
internal struct ParticleOutcome
{
    /// <summary>结果种类。</summary>
    public ParticleOutcomeKind Kind;
    /// <summary>沉积目标世界 X；仅 <see cref="ParticleOutcomeKind.WantsDeposit"/> 时有效。</summary>
    public int X;
    /// <summary>沉积目标世界 Y；仅 <see cref="ParticleOutcomeKind.WantsDeposit"/> 时有效。</summary>
    public int Y;

    /// <summary>粒子仍在飞行，本步无状态变更。</summary>
    public static ParticleOutcome Flying => default;

    /// <summary>
    /// 粒子请求在指定世界坐标沉积为格子材质。
    /// </summary>
    public static ParticleOutcome WantsDeposit(int x, int y)
    {
        return new ParticleOutcome
        {
            Kind = ParticleOutcomeKind.WantsDeposit,
            X = x,
            Y = y,
        };
    }

    /// <summary>粒子已耗尽寿命或碰撞销毁。</summary>
    public static ParticleOutcome Dead => new()
    {
        Kind = ParticleOutcomeKind.Dead,
    };
}
