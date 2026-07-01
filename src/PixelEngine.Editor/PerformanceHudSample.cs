namespace PixelEngine.Editor;

/// <summary>
/// 性能 HUD 从诊断快照中聚合出的可测试采样结果。
/// </summary>
public readonly record struct PerformanceHudSample(
    double TotalFrameMs,
    double ParticleMs,
    double CaPassAMs,
    double CaPassBMs,
    double CaPassCMs,
    double CaPassDMs,
    double HeatMs,
    double PhysicsMs,
    double ShapeRebuildMs,
    double RenderMs,
    double UploadMs,
    double AudioMs,
    long ActiveChunks,
    long ActiveCells,
    long FreeParticles,
    long RigidBodies,
    long ResidentChunks,
    long ResidentMemoryBytes,
    double SimHz,
    double TimeScale,
    int DegradationLevel,
    string DegradationName,
    int ConsecutiveOverBudgetFrames)
{
    /// <summary>
    /// CA checkerboard A-D 四遍合计耗时。
    /// </summary>
    public double CaMs => CaPassAMs + CaPassBMs + CaPassCMs + CaPassDMs;

    /// <summary>
    /// 当前是否处于时间膨胀。
    /// </summary>
    public bool IsTimeDilated => TimeScale < 0.999;
}
