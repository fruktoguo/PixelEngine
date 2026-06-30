using System.Numerics;

namespace PixelEngine.Core.Mathematics;

/// <summary>
/// 表示以 cell 为单位的二维整数坐标或偏移。
/// </summary>
/// <remarks>
/// 创建二维整数向量。
/// </remarks>
/// <param name="x">X 轴整数分量。</param>
/// <param name="y">Y 轴整数分量。</param>
public readonly struct Vector2i(int x, int y) : IEquatable<Vector2i>
{
    /// <summary>
    /// X 轴整数分量。
    /// </summary>
    public readonly int X = x;

    /// <summary>
    /// Y 轴整数分量。
    /// </summary>
    public readonly int Y = y;

    /// <summary>
    /// 零向量。
    /// </summary>
    public static readonly Vector2i Zero = new(0, 0);

    /// <summary>
    /// 两个分量均为 1 的向量。
    /// </summary>
    public static readonly Vector2i One = new(1, 1);

    /// <summary>
    /// X 轴单位向量。
    /// </summary>
    public static readonly Vector2i UnitX = new(1, 0);

    /// <summary>
    /// Y 轴单位向量。
    /// </summary>
    public static readonly Vector2i UnitY = new(0, 1);

    /// <summary>
    /// 获取曼哈顿长度 <c>|X| + |Y|</c>。
    /// </summary>
    public int ManhattanLength => Math.Abs(X) + Math.Abs(Y);

    /// <summary>
    /// 将整数向量转换为 <see cref="Vector2"/>。
    /// </summary>
    /// <returns>包含相同分量的浮点向量。</returns>
    public Vector2 ToVector2()
    {
        return new(X, Y);
    }

    /// <summary>
    /// 对浮点向量逐分量向下取整。
    /// </summary>
    /// <param name="value">输入浮点向量。</param>
    /// <returns>逐分量 floor 后的整数向量。</returns>
    public static Vector2i Floor(Vector2 value)
    {
        return new((int)MathF.Floor(value.X), (int)MathF.Floor(value.Y));
    }

    /// <summary>
    /// 对浮点向量逐分量四舍五入。
    /// </summary>
    /// <param name="value">输入浮点向量。</param>
    /// <returns>逐分量 round 后的整数向量。</returns>
    public static Vector2i Round(Vector2 value)
    {
        return new((int)MathF.Round(value.X), (int)MathF.Round(value.Y));
    }

    /// <summary>
    /// 对两个向量逐分量取较小值。
    /// </summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>逐分量最小值。</returns>
    public static Vector2i Min(Vector2i left, Vector2i right)
    {
        return new(Math.Min(left.X, right.X), Math.Min(left.Y, right.Y));
    }

    /// <summary>
    /// 对两个向量逐分量取较大值。
    /// </summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>逐分量最大值。</returns>
    public static Vector2i Max(Vector2i left, Vector2i right)
    {
        return new(Math.Max(left.X, right.X), Math.Max(left.Y, right.Y));
    }

    /// <summary>
    /// 判断两个向量是否逐分量相等。
    /// </summary>
    /// <param name="other">另一个向量。</param>
    /// <returns>若两个分量均相等则为 <see langword="true"/>。</returns>
    public bool Equals(Vector2i other)
    {
        return X == other.X && Y == other.Y;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is Vector2i other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"({X}, {Y})";
    }

    /// <summary>
    /// 两个整数向量逐分量相加。
    /// </summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>相加后的向量。</returns>
    public static Vector2i operator +(Vector2i left, Vector2i right)
    {
        return new(left.X + right.X, left.Y + right.Y);
    }

    /// <summary>
    /// 两个整数向量逐分量相减。
    /// </summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>相减后的向量。</returns>
    public static Vector2i operator -(Vector2i left, Vector2i right)
    {
        return new(left.X - right.X, left.Y - right.Y);
    }

    /// <summary>
    /// 对整数向量取负。
    /// </summary>
    /// <param name="value">输入向量。</param>
    /// <returns>取负后的向量。</returns>
    public static Vector2i operator -(Vector2i value)
    {
        return new(-value.X, -value.Y);
    }

    /// <summary>
    /// 将整数向量乘以标量。
    /// </summary>
    /// <param name="value">输入向量。</param>
    /// <param name="scalar">整数标量。</param>
    /// <returns>缩放后的向量。</returns>
    public static Vector2i operator *(Vector2i value, int scalar)
    {
        return new(value.X * scalar, value.Y * scalar);
    }

    /// <summary>
    /// 将整数向量乘以标量。
    /// </summary>
    /// <param name="scalar">整数标量。</param>
    /// <param name="value">输入向量。</param>
    /// <returns>缩放后的向量。</returns>
    public static Vector2i operator *(int scalar, Vector2i value)
    {
        return value * scalar;
    }

    /// <summary>
    /// 判断两个向量是否相等。
    /// </summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>若两个向量相等则为 <see langword="true"/>。</returns>
    public static bool operator ==(Vector2i left, Vector2i right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// 判断两个向量是否不相等。
    /// </summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>若两个向量不相等则为 <see langword="true"/>。</returns>
    public static bool operator !=(Vector2i left, Vector2i right)
    {
        return !left.Equals(right);
    }
}
