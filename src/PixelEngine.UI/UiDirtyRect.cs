namespace PixelEngine.UI;

/// <summary>
/// UI 光栅化脏矩形，坐标单位为 UI viewport pixel。
/// </summary>
/// <param name="X">左上角 X。</param>
/// <param name="Y">左上角 Y。</param>
/// <param name="Width">宽度。</param>
/// <param name="Height">高度。</param>
public readonly record struct UiDirtyRect(int X, int Y, int Width, int Height)
{
    /// <summary>
    /// 右边界，采用 exclusive 坐标。
    /// </summary>
    public int Right => X + Width;

    /// <summary>
    /// 下边界，采用 exclusive 坐标。
    /// </summary>
    public int Bottom => Y + Height;

    /// <summary>
    /// 是否是空矩形。
    /// </summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>
    /// 校验矩形坐标有限且尺寸非负。
    /// </summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(Width);
        ArgumentOutOfRangeException.ThrowIfNegative(Height);
        if (Right < X || Bottom < Y)
        {
            throw new ArgumentOutOfRangeException(nameof(Width), "UI 脏矩形坐标溢出。");
        }
    }

    /// <summary>
    /// 返回两个矩形的最小包围矩形。
    /// </summary>
    public UiDirtyRect Union(in UiDirtyRect other)
    {
        if (IsEmpty)
        {
            return other;
        }

        if (other.IsEmpty)
        {
            return this;
        }

        int x = Math.Min(X, other.X);
        int y = Math.Min(Y, other.Y);
        int right = Math.Max(Right, other.Right);
        int bottom = Math.Max(Bottom, other.Bottom);
        return new UiDirtyRect(x, y, right - x, bottom - y);
    }

    /// <summary>
    /// 判断两个矩形是否重叠或共享边界。
    /// </summary>
    public bool TouchesOrOverlaps(in UiDirtyRect other)
    {
        if (IsEmpty || other.IsEmpty)
        {
            return false;
        }

        bool overlaps = X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;
        bool touchesHorizontalEdge = (Right == other.X || X == other.Right) && Y < other.Bottom && Bottom > other.Y;
        bool touchesVerticalEdge = (Bottom == other.Y || Y == other.Bottom) && X < other.Right && Right > other.X;
        return overlaps || touchesHorizontalEdge || touchesVerticalEdge;
    }
}
