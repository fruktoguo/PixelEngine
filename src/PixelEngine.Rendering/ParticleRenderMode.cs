namespace PixelEngine.Rendering;

/// <summary>
/// 自由粒子渲染模式。
/// </summary>
public enum ParticleRenderMode
{
    /// <summary>
    /// CPU stamp 到 render buffer 与 emissive 副输出。
    /// </summary>
    CpuStamp,

    /// <summary>
    /// GPU point-sprite 批绘，只读 plan/05 粒子缓冲。
    /// </summary>
    GpuPointSprite,
}
