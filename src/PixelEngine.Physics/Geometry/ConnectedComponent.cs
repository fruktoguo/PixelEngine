using PixelEngine.Core.Mathematics;

namespace PixelEngine.Physics.Geometry;

/// <summary>
/// 二值像素 mask 的单个连通分量摘要。
/// </summary>
/// <param name="Label">从 1 开始的标签。</param>
/// <param name="PixelCount">分量像素数。</param>
/// <param name="Bounds">包含分量的半开整数 bounds。</param>
/// <param name="TouchesBorder">是否接触 mask 边界。</param>
/// <param name="IsFragment">是否小于碎片阈值。</param>
public readonly record struct ConnectedComponent(
    int Label,
    int PixelCount,
    RectI Bounds,
    bool TouchesBorder,
    bool IsFragment);
