using System.Runtime.CompilerServices;

namespace PixelEngine.Simulation;

/// <summary>
/// 单 cell 的运行时 flag 位布局。
/// </summary>
public static class CellFlags
{
    /// <summary>
    /// 当前帧 parity 时钟位。
    /// </summary>
    public const byte Parity = 1 << 0;

    /// <summary>
    /// settled/sleep 标记。
    /// </summary>
    public const byte Settled = 1 << 1;

    /// <summary>
    /// burning 标记。
    /// </summary>
    public const byte Burning = 1 << 2;

    /// <summary>
    /// free-falling 标记。
    /// </summary>
    public const byte FreeFalling = 1 << 3;

    /// <summary>
    /// 该 cell 当前由刚体像素占用，语义由 physics 层拥有。
    /// </summary>
    public const byte RigidOwned = 1 << 4;

    /// <summary>
    /// 判断 flag 是否包含指定 mask。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Has(byte flags, byte mask)
    {
        return (flags & mask) == mask;
    }

    /// <summary>
    /// 设置指定 mask。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte Set(byte flags, byte mask)
    {
        return (byte)(flags | mask);
    }

    /// <summary>
    /// 清除指定 mask。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte Clear(byte flags, byte mask)
    {
        return (byte)(flags & ~mask);
    }

    /// <summary>
    /// 判断 cell parity 是否已经等于当前帧 parity 位。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool MatchesFrame(byte flags, byte parityBit)
    {
        return (flags & Parity) == (parityBit & Parity);
    }

    /// <summary>
    /// 将 cell parity 写成当前帧 parity 位。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte SetParity(byte flags, byte parityBit)
    {
        return (byte)((flags & ~Parity) | (parityBit & Parity));
    }

    /// <summary>
    /// 判断 cell parity 位是否为 1。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasParity(byte flags)
    {
        return (flags & Parity) != 0;
    }
}
