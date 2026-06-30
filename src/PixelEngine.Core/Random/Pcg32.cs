namespace PixelEngine.Core.Random;

/// <summary>
/// 表示 PCG XSH RR 32 位有状态随机数源。
/// </summary>
public struct Pcg32 : IRandomSource
{
    private ulong _state;
    private readonly ulong _increment;

    /// <summary>
    /// 使用种子与流编号创建随机数源。
    /// </summary>
    /// <param name="seed">初始种子。</param>
    /// <param name="stream">流编号，不同流编号产生独立序列。</param>
    public Pcg32(ulong seed, ulong stream)
    {
        _state = 0;
        _increment = (stream << 1) | 1UL;
        _ = NextUInt();
        _state += seed;
        _ = NextUInt();
    }

    /// <inheritdoc />
    public uint NextUInt()
    {
        ulong oldState = _state;
        _state = unchecked((oldState * 6364136223846793005UL) + _increment);
        uint xorshifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
        int rotation = (int)(oldState >> 59);
        return (xorshifted >> rotation) | (xorshifted << ((-rotation) & 31));
    }

    /// <inheritdoc />
    public int NextInt(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);

        uint bound = (uint)maxExclusive;
        uint threshold = unchecked(0U - bound) % bound;
        while (true)
        {
            uint value = NextUInt();
            if (value >= threshold)
            {
                return (int)(value % bound);
            }
        }
    }

    /// <inheritdoc />
    public float NextFloat()
    {
        return CounterRng.ToFloat01(NextUInt());
    }
}
