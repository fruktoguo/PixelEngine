using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace PixelEngine.Simulation;

/// <summary>
/// 连续 cell SoA span 的批量扫描工具；用于 plan/16 的 SIMD/popcount 热路径纪律。
/// </summary>
internal static class CellSpanOps
{
    /// <summary>
    /// 统计非零 ushort cell 数；用于 resident material span 扫描。
    /// </summary>
    public static int CountNonZeroUShort(ReadOnlySpan<ushort> values)
    {
        int count = 0;
        int index = 0;
        ref ushort baseRef = ref MemoryMarshal.GetReference(values);
        if (Vector256.IsHardwareAccelerated)
        {
            Vector256<ushort> zero = Vector256<ushort>.Zero;
            for (; index <= values.Length - Vector256<ushort>.Count; index += Vector256<ushort>.Count)
            {
                Vector256<ushort> vector = Vector256.LoadUnsafe(ref Unsafe.Add(ref baseRef, index));
                Vector256<ushort> equalsZero = Vector256.Equals(vector, zero);
                uint zeroMask = equalsZero.ExtractMostSignificantBits();
                count += Vector256<ushort>.Count - BitOperations.PopCount(zeroMask);
            }
        }

        if (Vector128.IsHardwareAccelerated)
        {
            Vector128<ushort> zero = Vector128<ushort>.Zero;
            for (; index <= values.Length - Vector128<ushort>.Count; index += Vector128<ushort>.Count)
            {
                Vector128<ushort> vector = Vector128.LoadUnsafe(ref Unsafe.Add(ref baseRef, index));
                Vector128<ushort> equalsZero = Vector128.Equals(vector, zero);
                uint zeroMask = equalsZero.ExtractMostSignificantBits();
                count += Vector128<ushort>.Count - BitOperations.PopCount(zeroMask);
            }
        }

        for (; index < values.Length; index++)
        {
            count += Unsafe.Add(ref baseRef, index) != 0 ? 1 : 0;
        }

        return count;
    }

    /// <summary>
    /// 仅对 material 或 flags 非零的 cell 写入 parity，空 cell 保持 flags=0。
    /// </summary>
    public static void SetParityForOccupiedCells(ReadOnlySpan<ushort> material, Span<byte> flags, byte parityBit)
    {
        if (flags.Length < material.Length)
        {
            throw new ArgumentException("flags span 长度不能小于 material span。", nameof(flags));
        }

        int index = 0;
        ref ushort materialBase = ref MemoryMarshal.GetReference(material);
        ref byte flagsBase = ref MemoryMarshal.GetReference(flags);
        if (Vector256.IsHardwareAccelerated)
        {
            Vector256<ushort> zeroMaterial = Vector256<ushort>.Zero;
            Vector128<byte> zeroFlags = Vector128<byte>.Zero;
            for (; index <= material.Length - Vector256<ushort>.Count; index += Vector256<ushort>.Count)
            {
                Vector256<ushort> materialVector = Vector256.LoadUnsafe(ref Unsafe.Add(ref materialBase, index));
                Vector128<byte> flagsVector = Vector128.LoadUnsafe(ref Unsafe.Add(ref flagsBase, index));
                uint materialZeroMask = Vector256.Equals(materialVector, zeroMaterial).ExtractMostSignificantBits();
                uint flagsZeroMask = Vector128.Equals(flagsVector, zeroFlags).ExtractMostSignificantBits();
                uint occupiedMask = (~materialZeroMask | ~flagsZeroMask) & 0xFFFFu;
                while (occupiedMask != 0)
                {
                    int lane = BitOperations.TrailingZeroCount(occupiedMask);
                    ref byte flag = ref Unsafe.Add(ref flagsBase, index + lane);
                    flag = CellFlags.SetParity(flag, parityBit);
                    occupiedMask &= occupiedMask - 1;
                }
            }
        }

        if (Vector128.IsHardwareAccelerated)
        {
            Vector128<ushort> zeroMaterial = Vector128<ushort>.Zero;
            Vector64<byte> zeroFlags = Vector64<byte>.Zero;
            for (; index <= material.Length - Vector128<ushort>.Count; index += Vector128<ushort>.Count)
            {
                Vector128<ushort> materialVector = Vector128.LoadUnsafe(ref Unsafe.Add(ref materialBase, index));
                Vector64<byte> flagsVector = Vector64.LoadUnsafe(ref Unsafe.Add(ref flagsBase, index));
                uint materialZeroMask = Vector128.Equals(materialVector, zeroMaterial).ExtractMostSignificantBits();
                uint flagsZeroMask = Vector64.Equals(flagsVector, zeroFlags).ExtractMostSignificantBits();
                uint occupiedMask = (~materialZeroMask | ~flagsZeroMask) & 0xFFu;
                while (occupiedMask != 0)
                {
                    int lane = BitOperations.TrailingZeroCount(occupiedMask);
                    ref byte flag = ref Unsafe.Add(ref flagsBase, index + lane);
                    flag = CellFlags.SetParity(flag, parityBit);
                    occupiedMask &= occupiedMask - 1;
                }
            }
        }

        for (; index < material.Length; index++)
        {
            ref byte flag = ref Unsafe.Add(ref flagsBase, index);
            if (Unsafe.Add(ref materialBase, index) != 0 || flag != 0)
            {
                flag = CellFlags.SetParity(flag, parityBit);
            }
        }
    }
}
