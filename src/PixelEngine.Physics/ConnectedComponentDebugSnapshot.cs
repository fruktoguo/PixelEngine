using PixelEngine.Core.Mathematics;

namespace PixelEngine.Physics;

/// <summary>
/// Editor 调试叠层使用的刚体连通块只读快照。
/// </summary>
/// <param name="BodyKey">所属刚体 key。</param>
/// <param name="Label">连通块标签。</param>
/// <param name="PixelCount">连通块像素数。</param>
/// <param name="WorldBounds">连通块世界坐标 AABB。</param>
/// <param name="IsFragment">是否低于碎片阈值。</param>
public readonly record struct ConnectedComponentDebugSnapshot(
    int BodyKey,
    int Label,
    int PixelCount,
    RectI WorldBounds,
    bool IsFragment);
