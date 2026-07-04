using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace PixelEngine.Rendering;

/// <summary>
/// RenderStyle 分段快路径的向量化 eligibility 扫描器，只判断 material 连续且 Damage 全零。
/// </summary>
internal static class RenderStyleSegmentScanner
{
    /// <summary>
    /// 返回从 <paramref name="start" /> 开始，material 全等且 Damage 全零的最大连续长度。
    /// </summary>
    public static int CountSolidUnbrokenRun(
        ReadOnlySpan<ushort> materials,
        ReadOnlySpan<byte> damage,
        int start,
        int remaining,
        ushort materialId)
    {
        int index = 0;
        ref ushort materialBase = ref MemoryMarshal.GetReference(materials);
        ref byte damageBase = ref MemoryMarshal.GetReference(damage);
        if (Vector256.IsHardwareAccelerated)
        {
            Vector256<ushort> materialVector = Vector256.Create(materialId);
            Vector256<byte> zeroDamage = Vector256<byte>.Zero;
            for (; index <= remaining - Vector256<ushort>.Count; index += Vector256<ushort>.Count)
            {
                Vector256<ushort> candidateMaterial = Vector256.LoadUnsafe(ref Unsafe.Add(ref materialBase, start + index));
                Vector128<byte> candidateDamage128 = Vector128.LoadUnsafe(ref Unsafe.Add(ref damageBase, start + index));
                Vector256<byte> candidateDamage = Vector256.Create(
                    candidateDamage128,
                    Vector128<byte>.Zero);
                uint materialMask = Vector256.Equals(candidateMaterial, materialVector).ExtractMostSignificantBits();
                uint damageMask = Vector256.Equals(candidateDamage, zeroDamage).ExtractMostSignificantBits() & 0xFFFFu;
                uint validMask = materialMask & damageMask;
                if (validMask != 0xFFFFu)
                {
                    return index + CountLeadingValid16(validMask);
                }
            }
        }

        if (Vector128.IsHardwareAccelerated)
        {
            Vector128<ushort> materialVector = Vector128.Create(materialId);
            Vector128<byte> zeroDamage = Vector128<byte>.Zero;
            for (; index <= remaining - Vector128<ushort>.Count; index += Vector128<ushort>.Count)
            {
                Vector128<ushort> candidateMaterial = Vector128.LoadUnsafe(ref Unsafe.Add(ref materialBase, start + index));
                Vector64<byte> candidateDamage64 = Vector64.LoadUnsafe(ref Unsafe.Add(ref damageBase, start + index));
                Vector128<byte> candidateDamage = Vector128.Create(
                    candidateDamage64,
                    Vector64<byte>.Zero);
                uint materialMask = Vector128.Equals(candidateMaterial, materialVector).ExtractMostSignificantBits();
                uint damageMask = Vector128.Equals(candidateDamage, zeroDamage).ExtractMostSignificantBits() & 0xFFu;
                uint validMask = materialMask & damageMask;
                if (validMask != 0xFFu)
                {
                    return index + CountLeadingValid8(validMask);
                }
            }
        }

        for (; index < remaining; index++)
        {
            int local = start + index;
            if (Unsafe.Add(ref materialBase, local) != materialId ||
                Unsafe.Add(ref damageBase, local) != 0)
            {
                return index;
            }
        }

        return remaining;
    }

    private static int CountLeadingValid16(uint mask)
    {
        return CountLeadingValid(mask, 16);
    }

    private static int CountLeadingValid8(uint mask)
    {
        return CountLeadingValid(mask, 8);
    }

    private static int CountLeadingValid(uint mask, int width)
    {
        for (int i = 0; i < width; i++)
        {
            if (((mask >> i) & 1u) == 0)
            {
                return i;
            }
        }

        return width;
    }
}
