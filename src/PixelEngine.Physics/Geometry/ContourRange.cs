namespace PixelEngine.Physics.Geometry;

/// <summary>
/// 轮廓点缓冲中的一个闭合 contour 范围。
/// </summary>
/// <param name="Start">起始点索引。</param>
/// <param name="Count">点数量，包含重复闭合终点。</param>
/// <param name="IsHole">是否为内孔。</param>
public readonly record struct ContourRange(int Start, int Count, bool IsHole);
