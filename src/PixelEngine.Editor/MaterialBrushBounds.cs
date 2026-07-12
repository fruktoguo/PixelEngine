namespace PixelEngine.Editor;

/// <summary>
/// 世界画刷允许编辑的闭区间 cell 边界。
/// </summary>
public readonly record struct MaterialBrushBounds
{
    /// <summary>
    /// 创建闭区间画刷边界。
    /// </summary>
    /// <param name="minX">最小世界 X。</param>
    /// <param name="minY">最小世界 Y。</param>
    /// <param name="maxX">最大世界 X。</param>
    /// <param name="maxY">最大世界 Y。</param>
    public MaterialBrushBounds(int minX, int minY, int maxX, int maxY)
    {
        if (minX > maxX)
        {
            throw new ArgumentOutOfRangeException(nameof(minX), minX, "画刷边界 MinX 不能大于 MaxX。");
        }

        if (minY > maxY)
        {
            throw new ArgumentOutOfRangeException(nameof(minY), minY, "画刷边界 MinY 不能大于 MaxY。");
        }

        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    /// <summary>
    /// 无边界限制。
    /// </summary>
    public static MaterialBrushBounds Unbounded { get; } = new(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue);

    /// <summary>
    /// 最小世界 X。
    /// </summary>
    public int MinX { get; }

    /// <summary>
    /// 最小世界 Y。
    /// </summary>
    public int MinY { get; }

    /// <summary>
    /// 最大世界 X。
    /// </summary>
    public int MaxX { get; }

    /// <summary>
    /// 最大世界 Y。
    /// </summary>
    public int MaxY { get; }

    /// <summary>
    /// 返回 cell 是否位于边界内。
    /// </summary>
    public bool Contains(int x, int y)
    {
        return x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
    }
}
