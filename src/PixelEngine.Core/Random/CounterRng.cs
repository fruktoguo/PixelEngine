namespace PixelEngine.Core.Random;

/// <summary>
/// 提供无可变状态的 counter-based 随机数工具。
/// </summary>
public static class CounterRng
{
    /// <summary>
    /// 对种子、坐标与计数器做雪崩混合，返回确定性随机位。
    /// </summary>
    /// <param name="seed">基础种子。</param>
    /// <param name="x">X 坐标或调用方自定义维度。</param>
    /// <param name="y">Y 坐标或调用方自定义维度。</param>
    /// <param name="counter">调用计数器。</param>
    /// <returns>确定性随机位。</returns>
    public static uint Hash(uint seed, int x, int y, uint counter)
    {
        uint value = seed;
        value ^= Mix((uint)x + 0x9E3779B9U);
        value = RotateLeft(value, 13) ^ Mix((uint)y + 0x85EBCA6BU);
        value = RotateLeft(value, 17) ^ Mix(counter + 0xC2B2AE35U);
        return Mix(value);
    }

    /// <summary>
    /// 将随机位转换为 <c>[0, 1)</c> 浮点数。
    /// </summary>
    /// <param name="bits">随机位。</param>
    /// <returns>随机浮点数。</returns>
    public static float ToFloat01(uint bits)
    {
        return (bits >> 8) * (1.0f / 16777216.0f);
    }

    /// <summary>
    /// 使用坐标与递增 counter 返回下一个确定性随机数。
    /// </summary>
    /// <param name="seed">基础种子。</param>
    /// <param name="x">X 坐标或调用方自定义维度。</param>
    /// <param name="y">Y 坐标或调用方自定义维度。</param>
    /// <param name="counter">调用计数器，会在返回前递增。</param>
    /// <returns>确定性随机位。</returns>
    public static uint NextUInt(uint seed, int x, int y, ref uint counter)
    {
        uint value = Hash(seed, x, y, counter);
        counter++;
        return value;
    }

    private static uint Mix(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352DU;
        value ^= value >> 15;
        value *= 0x846CA68BU;
        value ^= value >> 16;
        return value;
    }

    private static uint RotateLeft(uint value, int offset)
    {
        return (value << offset) | (value >> (32 - offset));
    }
}
