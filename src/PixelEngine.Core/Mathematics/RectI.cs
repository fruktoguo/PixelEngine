namespace PixelEngine.Core.Mathematics;

/// <summary>
/// 表示二维整数半开矩形，范围为 <c>[Min, Max)</c>。
/// </summary>
/// <remarks>
/// 创建二维整数半开矩形。
/// </remarks>
/// <param name="minX">X 轴最小边界，包含。</param>
/// <param name="minY">Y 轴最小边界，包含。</param>
/// <param name="maxX">X 轴最大边界，不包含。</param>
/// <param name="maxY">Y 轴最大边界，不包含。</param>
public struct RectI(int minX, int minY, int maxX, int maxY) : IEquatable<RectI>
{
    /// <summary>
    /// X 轴最小边界，包含。
    /// </summary>
    public int MinX = minX;

    /// <summary>
    /// Y 轴最小边界，包含。
    /// </summary>
    public int MinY = minY;

    /// <summary>
    /// X 轴最大边界，不包含。
    /// </summary>
    public int MaxX = maxX;

    /// <summary>
    /// Y 轴最大边界，不包含。
    /// </summary>
    public int MaxY = maxY;

    /// <summary>
    /// 空矩形。
    /// </summary>
    public static readonly RectI Empty = default;

    /// <summary>
    /// 获取矩形是否为空。
    /// </summary>
    public readonly bool IsEmpty => MaxX <= MinX || MaxY <= MinY;

    /// <summary>
    /// 获取矩形宽度。
    /// </summary>
    public readonly int Width => Math.Max(0, MaxX - MinX);

    /// <summary>
    /// 获取矩形高度。
    /// </summary>
    public readonly int Height => Math.Max(0, MaxY - MinY);

    /// <summary>
    /// 获取矩形面积。
    /// </summary>
    public readonly int Area => Width * Height;

    /// <summary>
    /// 按边界创建二维整数半开矩形。
    /// </summary>
    /// <param name="minX">X 轴最小边界，包含。</param>
    /// <param name="minY">Y 轴最小边界，包含。</param>
    /// <param name="maxX">X 轴最大边界，不包含。</param>
    /// <param name="maxY">Y 轴最大边界，不包含。</param>
    /// <returns>二维整数半开矩形。</returns>
    public static RectI FromBounds(int minX, int minY, int maxX, int maxY)
    {
        return new(minX, minY, maxX, maxY);
    }

    /// <summary>
    /// 扩张矩形以包含指定 cell。
    /// </summary>
    /// <param name="x">cell 的 X 坐标。</param>
    /// <param name="y">cell 的 Y 坐标。</param>
    public void Encapsulate(int x, int y)
    {
        if (IsEmpty)
        {
            MinX = x;
            MinY = y;
            MaxX = x + 1;
            MaxY = y + 1;
            return;
        }

        MinX = Math.Min(MinX, x);
        MinY = Math.Min(MinY, y);
        MaxX = Math.Max(MaxX, x + 1);
        MaxY = Math.Max(MaxY, y + 1);
    }

    /// <summary>
    /// 扩张矩形以包含另一个矩形。
    /// </summary>
    /// <param name="other">另一个矩形。</param>
    public void Encapsulate(in RectI other)
    {
        if (other.IsEmpty)
        {
            return;
        }

        if (IsEmpty)
        {
            this = other;
            return;
        }

        MinX = Math.Min(MinX, other.MinX);
        MinY = Math.Min(MinY, other.MinY);
        MaxX = Math.Max(MaxX, other.MaxX);
        MaxY = Math.Max(MaxY, other.MaxY);
    }

    /// <summary>
    /// 按 padding 扩张矩形，并将结果钳制在给定边界内。
    /// </summary>
    /// <param name="padding">非负扩张像素数。</param>
    /// <param name="bounds">钳制边界。</param>
    public void ExpandClamped(int padding, in RectI bounds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(padding);

        if (IsEmpty)
        {
            return;
        }

        MinX = Math.Max(bounds.MinX, MinX - padding);
        MinY = Math.Max(bounds.MinY, MinY - padding);
        MaxX = Math.Min(bounds.MaxX, MaxX + padding);
        MaxY = Math.Min(bounds.MaxY, MaxY + padding);

        if (!bounds.IsEmpty && !IsEmpty)
        {
            return;
        }

        this = Empty;
    }

    /// <summary>
    /// 判断指定 cell 是否位于矩形内。
    /// </summary>
    /// <param name="x">cell 的 X 坐标。</param>
    /// <param name="y">cell 的 Y 坐标。</param>
    /// <returns>若 cell 位于半开矩形内则为 <see langword="true"/>。</returns>
    public readonly bool Contains(int x, int y)
    {
        return x >= MinX && x < MaxX && y >= MinY && y < MaxY;
    }

    /// <summary>
    /// 判断两个矩形是否相交。
    /// </summary>
    /// <param name="other">另一个矩形。</param>
    /// <returns>若两个矩形有非空交集则为 <see langword="true"/>。</returns>
    public readonly bool Intersects(in RectI other)
    {
        return MinX < other.MaxX
            && MaxX > other.MinX
            && MinY < other.MaxY
            && MaxY > other.MinY;
    }

    /// <summary>
    /// 返回当前矩形与另一个矩形的交集。
    /// </summary>
    /// <param name="other">另一个矩形。</param>
    /// <returns>交集矩形；若不相交则为空矩形。</returns>
    public readonly RectI Intersection(in RectI other)
    {
        RectI result = new(
            Math.Max(MinX, other.MinX),
            Math.Max(MinY, other.MinY),
            Math.Min(MaxX, other.MaxX),
            Math.Min(MaxY, other.MaxY));

        return result.IsEmpty ? Empty : result;
    }

    /// <summary>
    /// 判断两个矩形边界是否完全相同。
    /// </summary>
    /// <param name="other">另一个矩形。</param>
    /// <returns>若边界完全相同则为 <see langword="true"/>。</returns>
    public readonly bool Equals(RectI other)
    {
        return MinX == other.MinX
            && MinY == other.MinY
            && MaxX == other.MaxX
            && MaxY == other.MaxY;
    }

    /// <inheritdoc />
    public override readonly bool Equals(object? obj)
    {
        return obj is RectI other && Equals(other);
    }

    /// <inheritdoc />
    public override readonly int GetHashCode()
    {
        return HashCode.Combine(MinX, MinY, MaxX, MaxY);
    }

    /// <inheritdoc />
    public override readonly string ToString()
    {
        return $"[{MinX}, {MinY}, {MaxX}, {MaxY})";
    }

    /// <summary>
    /// 判断两个矩形是否相等。
    /// </summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>若两个矩形相等则为 <see langword="true"/>。</returns>
    public static bool operator ==(RectI left, RectI right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// 判断两个矩形是否不相等。
    /// </summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>若两个矩形不相等则为 <see langword="true"/>。</returns>
    public static bool operator !=(RectI left, RectI right)
    {
        return !left.Equals(right);
    }
}
