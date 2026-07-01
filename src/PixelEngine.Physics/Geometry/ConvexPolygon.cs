using System.Numerics;

namespace PixelEngine.Physics.Geometry;

/// <summary>
/// 顶点数不超过 8 的凸多边形。
/// </summary>
public struct ConvexPolygon
{
    private Vector2Buffer8 _vertices;

    /// <summary>
    /// 顶点数量。
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// 获取指定顶点。
    /// </summary>
    /// <param name="index">顶点索引。</param>
    /// <returns>顶点坐标。</returns>
    public readonly Vector2 this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)Count, nameof(index));
            return _vertices[index];
        }
    }

    /// <summary>
    /// 从顶点序列创建凸多边形。
    /// </summary>
    /// <param name="vertices">顶点序列。</param>
    /// <returns>凸多边形。</returns>
    public static ConvexPolygon From(ReadOnlySpan<Vector2> vertices)
    {
        if (vertices.Length is < 3 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(vertices), vertices.Length, "凸片顶点数必须位于 [3,8]。");
        }

        ConvexPolygon polygon = default;
        polygon.Count = vertices.Length;
        for (int i = 0; i < vertices.Length; i++)
        {
            polygon._vertices[i] = vertices[i];
        }

        return polygon;
    }

    /// <summary>
    /// 将顶点写入目标缓冲。
    /// </summary>
    /// <param name="destination">目标缓冲。</param>
    /// <returns>写入数量。</returns>
    public readonly int CopyTo(Span<Vector2> destination)
    {
        if (destination.Length < Count)
        {
            throw new ArgumentException("destination 缓冲不足。", nameof(destination));
        }

        for (int i = 0; i < Count; i++)
        {
            destination[i] = _vertices[i];
        }

        return Count;
    }
}
