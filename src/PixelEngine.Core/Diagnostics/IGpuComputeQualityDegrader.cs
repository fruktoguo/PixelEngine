namespace PixelEngine.Core.Diagnostics;

/// <summary>
/// GPU compute 渲染质量降级接口，由渲染后端注册给 Hosting 的过载策略调用。
/// </summary>
public interface IGpuComputeQualityDegrader
{
    /// <summary>
    /// 按 plan/09 §4.7 的顺序降低一档 GPU compute 渲染质量。
    /// </summary>
    /// <returns>若成功降低一档返回 true；已经在最低档时返回 false。</returns>
    bool DegradeGpuComputeOneStep();
}
