namespace PixelEngine.Core.Mathematics;

/// <summary>
/// 表示 Q32.32 定点数，用于确定性模式下避免浮点平台差异。
/// </summary>
public readonly struct Fixed : IEquatable<Fixed>, IComparable<Fixed>
{
    /// <summary>
    /// 小数位数量。
    /// </summary>
    public const int FractionalBits = 32;

    private const long OneRaw = 1L << FractionalBits;
    private const long HalfRaw = OneRaw >> 1;

    /// <summary>
    /// 定点数的原始 Q32.32 表示。
    /// </summary>
    public readonly long Raw;

    /// <summary>
    /// 零值。
    /// </summary>
    public static readonly Fixed Zero = new(0);

    /// <summary>
    /// 一。
    /// </summary>
    public static readonly Fixed One = new(OneRaw);

    /// <summary>
    /// 二分之一。
    /// </summary>
    public static readonly Fixed Half = new(HalfRaw);

    private Fixed(long raw)
    {
        Raw = raw;
    }

    /// <summary>
    /// 从整数创建定点数。
    /// </summary>
    /// <param name="value">整数值。</param>
    /// <returns>对应的定点数。</returns>
    public static Fixed FromInt(int value)
    {
        return new((long)value << FractionalBits);
    }

    /// <summary>
    /// 从原始 Q32.32 表示创建定点数。
    /// </summary>
    /// <param name="raw">原始 Q32.32 值。</param>
    /// <returns>对应的定点数。</returns>
    public static Fixed FromRaw(long raw)
    {
        return new(raw);
    }

    /// <summary>
    /// 对定点数求平方根。
    /// </summary>
    /// <param name="value">输入值，必须非负。</param>
    /// <returns>平方根。</returns>
    public static Fixed Sqrt(Fixed value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value.Raw);

        if (value.Raw == 0)
        {
            return Zero;
        }

        ulong raw = (ulong)value.Raw;
        ulong root = 0;
        ulong bit = 1UL << 62;

        while (bit > raw)
        {
            bit >>= 2;
        }

        while (bit != 0)
        {
            ulong trial = root + bit;
            if (raw >= trial)
            {
                raw -= trial;
                root = (root >> 1) + bit;
            }
            else
            {
                root >>= 1;
            }

            bit >>= 2;
        }

        return new Fixed((long)(root << (FractionalBits / 2)));
    }

    /// <summary>
    /// 将定点数截断为整数。
    /// </summary>
    /// <returns>截断后的整数。</returns>
    public int ToInt()
    {
        return (int)(Raw / OneRaw);
    }

    /// <summary>
    /// 使用固定的 half-away-from-zero 规则将定点数舍入为整数。
    /// </summary>
    /// <returns>舍入后的整数。</returns>
    public int RoundToInt()
    {
        long adjusted = Raw >= 0 ? Raw + HalfRaw : Raw - HalfRaw;
        return (int)(adjusted / OneRaw);
    }

    /// <inheritdoc />
    public int CompareTo(Fixed other)
    {
        return Raw.CompareTo(other.Raw);
    }

    /// <summary>
    /// 判断两个定点数是否相等。
    /// </summary>
    /// <param name="other">另一个定点数。</param>
    /// <returns>若原始值相同则为 <see langword="true"/>。</returns>
    public bool Equals(Fixed other)
    {
        return Raw == other.Raw;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is Fixed other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return Raw.GetHashCode();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return ((float)this).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 将浮点数显式转换为定点数。仅用于初始化或调试，不用于确定性热路径。
    /// </summary>
    /// <param name="value">浮点输入。</param>
    public static explicit operator Fixed(float value)
    {
        return new((long)MathF.Round(value * OneRaw));
    }

    /// <summary>
    /// 将定点数显式转换为浮点数。
    /// </summary>
    /// <param name="value">定点输入。</param>
    public static explicit operator float(Fixed value)
    {
        return value.Raw / (float)OneRaw;
    }

    /// <summary>
    /// 两个定点数相加。
    /// </summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>相加结果。</returns>
    public static Fixed operator +(Fixed left, Fixed right)
    {
        return new(left.Raw + right.Raw);
    }

    /// <summary>
    /// 两个定点数相减。
    /// </summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>相减结果。</returns>
    public static Fixed operator -(Fixed left, Fixed right)
    {
        return new(left.Raw - right.Raw);
    }

    /// <summary>
    /// 对定点数取负。
    /// </summary>
    /// <param name="value">输入值。</param>
    /// <returns>取负结果。</returns>
    public static Fixed operator -(Fixed value)
    {
        return new(-value.Raw);
    }

    /// <summary>
    /// 两个定点数相乘。
    /// </summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>相乘结果。</returns>
    public static Fixed operator *(Fixed left, Fixed right)
    {
        return new((long)(((Int128)left.Raw * right.Raw) >> FractionalBits));
    }

    /// <summary>
    /// 两个定点数相除。
    /// </summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>相除结果。</returns>
    public static Fixed operator /(Fixed left, Fixed right)
    {
        ArgumentOutOfRangeException.ThrowIfZero(right.Raw);
        return new Fixed((long)(((Int128)left.Raw << FractionalBits) / right.Raw));
    }

    /// <summary>
    /// 判断两个定点数是否相等。
    /// </summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>若相等则为 <see langword="true"/>。</returns>
    public static bool operator ==(Fixed left, Fixed right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// 判断两个定点数是否不相等。
    /// </summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>若不相等则为 <see langword="true"/>。</returns>
    public static bool operator !=(Fixed left, Fixed right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// 判断左操作数是否小于右操作数。
    /// </summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>若小于则为 <see langword="true"/>。</returns>
    public static bool operator <(Fixed left, Fixed right)
    {
        return left.Raw < right.Raw;
    }

    /// <summary>
    /// 判断左操作数是否大于右操作数。
    /// </summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>若大于则为 <see langword="true"/>。</returns>
    public static bool operator >(Fixed left, Fixed right)
    {
        return left.Raw > right.Raw;
    }

    /// <summary>
    /// 判断左操作数是否小于或等于右操作数。
    /// </summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>若小于或等于则为 <see langword="true"/>。</returns>
    public static bool operator <=(Fixed left, Fixed right)
    {
        return left.Raw <= right.Raw;
    }

    /// <summary>
    /// 判断左操作数是否大于或等于右操作数。
    /// </summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>若大于或等于则为 <see langword="true"/>。</returns>
    public static bool operator >=(Fixed left, Fixed right)
    {
        return left.Raw >= right.Raw;
    }
}
