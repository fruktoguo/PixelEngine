using System.Numerics;

namespace PixelEngine.Core.Mathematics;

/// <summary>
/// 提供引擎底层使用的标量数学工具。
/// </summary>
public static class Mathx
{
    /// <summary>
    /// 计算向下取整除法，确保负坐标映射到正确的 chunk。
    /// </summary>
    /// <param name="a">被除数。</param>
    /// <param name="b">正除数。</param>
    /// <returns><c>floor(a / b)</c>。</returns>
    public static int FloorDiv(int a, int b)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(b);

        int quotient = Math.DivRem(a, b, out int remainder);
        return remainder < 0 ? quotient - 1 : quotient;
    }

    /// <summary>
    /// 计算 floored modulo，结果始终位于 <c>[0, b)</c>。
    /// </summary>
    /// <param name="a">被除数。</param>
    /// <param name="b">正除数。</param>
    /// <returns>非负余数。</returns>
    public static int Mod(int a, int b)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(b);

        int remainder = a % b;
        return remainder < 0 ? remainder + b : remainder;
    }

    /// <summary>
    /// 计算向上取整除法。
    /// </summary>
    /// <param name="a">被除数。</param>
    /// <param name="b">正除数。</param>
    /// <returns><c>ceil(a / b)</c>。</returns>
    public static int CeilDiv(int a, int b)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(b);

        int quotient = Math.DivRem(a, b, out int remainder);
        return remainder > 0 ? quotient + 1 : quotient;
    }

    /// <summary>
    /// 将整数值钳制到闭区间。
    /// </summary>
    /// <param name="value">输入值。</param>
    /// <param name="min">最小值。</param>
    /// <param name="max">最大值。</param>
    /// <returns>钳制后的值。</returns>
    public static int Clamp(int value, int min, int max)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(min, max);
        return Math.Min(Math.Max(value, min), max);
    }

    /// <summary>
    /// 将浮点数钳制到 <c>[0, 1]</c>。
    /// </summary>
    /// <param name="value">输入值。</param>
    /// <returns>钳制后的值。</returns>
    public static float Clamp01(float value)
    {
        return Math.Min(Math.Max(value, 0f), 1f);
    }

    /// <summary>
    /// 线性插值。
    /// </summary>
    /// <param name="a">起点值。</param>
    /// <param name="b">终点值。</param>
    /// <param name="t">插值因子。</param>
    /// <returns>插值结果。</returns>
    public static float Lerp(float a, float b, float t)
    {
        return a + ((b - a) * t);
    }

    /// <summary>
    /// 返回大于或等于输入值的最小 2 的幂。
    /// </summary>
    /// <param name="value">正整数输入。</param>
    /// <returns>下一个 2 的幂。</returns>
    public static int NextPowerOfTwo(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        return value > 1 << 30
            ? throw new ArgumentOutOfRangeException(nameof(value), value, "结果会超出正 int 范围。")
            : 1 << (32 - BitOperations.LeadingZeroCount((uint)(value - 1)));
    }

    /// <summary>
    /// 判断输入值是否为 2 的幂。
    /// </summary>
    /// <param name="value">输入值。</param>
    /// <returns>若为正的 2 的幂则为 <see langword="true"/>。</returns>
    public static bool IsPowerOfTwo(int value)
    {
        return value > 0 && (value & (value - 1)) == 0;
    }

    /// <summary>
    /// 返回整数以 2 为底的向下取整对数。
    /// </summary>
    /// <param name="value">正整数输入。</param>
    /// <returns><c>floor(log2(value))</c>。</returns>
    public static int Log2Int(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        return BitOperations.Log2((uint)value);
    }
}
