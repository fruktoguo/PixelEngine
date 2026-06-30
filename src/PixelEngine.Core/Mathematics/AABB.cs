using System.Numerics;

namespace PixelEngine.Core.Mathematics;

/// <summary>
/// 表示二维浮点轴对齐包围盒。
/// </summary>
/// <remarks>
/// 创建二维浮点轴对齐包围盒。
/// </remarks>
/// <param name="min">最小角点。</param>
/// <param name="max">最大角点。</param>
public readonly struct AABB(Vector2 min, Vector2 max)
{
    /// <summary>
    /// 包围盒最小角点。
    /// </summary>
    public readonly Vector2 Min = min;

    /// <summary>
    /// 包围盒最大角点。
    /// </summary>
    public readonly Vector2 Max = max;

    /// <summary>
    /// 获取包围盒中心点。
    /// </summary>
    public Vector2 Center => (Min + Max) * 0.5f;

    /// <summary>
    /// 获取包围盒半尺寸。
    /// </summary>
    public Vector2 Extents => (Max - Min) * 0.5f;

    /// <summary>
    /// 判断点是否位于包围盒内，边界视为包含。
    /// </summary>
    /// <param name="point">待测试点。</param>
    /// <returns>若点位于包围盒内则为 <see langword="true"/>。</returns>
    public bool Contains(Vector2 point)
    {
        return point.X >= Min.X
            && point.X <= Max.X
            && point.Y >= Min.Y
            && point.Y <= Max.Y;
    }

    /// <summary>
    /// 判断两个包围盒是否相交，接触边界视为相交。
    /// </summary>
    /// <param name="other">另一个包围盒。</param>
    /// <returns>若两个包围盒相交则为 <see langword="true"/>。</returns>
    public bool Intersects(in AABB other)
    {
        return Min.X <= other.Max.X
            && Max.X >= other.Min.X
            && Min.Y <= other.Max.Y
            && Max.Y >= other.Min.Y;
    }

    /// <summary>
    /// 返回包含当前包围盒与另一个包围盒的并集。
    /// </summary>
    /// <param name="other">另一个包围盒。</param>
    /// <returns>并集包围盒。</returns>
    public AABB Union(in AABB other)
    {
        return new AABB(
            Vector2.Min(Min, other.Min),
            Vector2.Max(Max, other.Max));
    }

    /// <summary>
    /// 按指定边距扩展包围盒。
    /// </summary>
    /// <param name="margin">扩展边距，可为负数以收缩。</param>
    /// <returns>扩展后的包围盒。</returns>
    public AABB Expand(float margin)
    {
        Vector2 delta = new(margin, margin);
        return new AABB(Min - delta, Max + delta);
    }

    /// <summary>
    /// 将浮点包围盒转换为覆盖相同区域的整数半开矩形。
    /// </summary>
    /// <returns>使用 floor(min) 与 ceil(max) 得到的整数矩形。</returns>
    public RectI ToRectI()
    {
        return RectI.FromBounds(
            (int)MathF.Floor(Min.X),
            (int)MathF.Floor(Min.Y),
            (int)MathF.Ceiling(Max.X),
            (int)MathF.Ceiling(Max.Y));
    }
}
