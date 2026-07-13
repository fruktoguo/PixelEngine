namespace PixelEngine.Rendering;

/// <summary>
/// 当前渲染窗口显示器度量的唯一来源。采样变化只通过帧边界提交。
/// </summary>
public interface IDisplayMetricsSource
{
    /// <summary>
    /// 最近一次已提交的度量。
    /// </summary>
    DisplayMetricsSnapshot Current { get; }

    /// <summary>
    /// 在 render 前的帧边界采样并提交 monitor、framebuffer scale 与 raw physical DPI。
    /// </summary>
    /// <returns>本帧可安全用于 layout、raster 与 input 的同一 revision 快照。</returns>
    DisplayMetricsSnapshot CommitFrameBoundary();
}
