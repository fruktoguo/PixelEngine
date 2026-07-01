using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace PixelEngine.Rendering;

/// <summary>
/// 将 material id span 通过 BGRA palette 转为 render buffer 像素。默认路径仅启用已实测收益的窄 SIMD dispatcher；AVX2 gather 路径仅供显式基准验证。
/// </summary>
public static unsafe class PaletteBgraConverter
{
    /// <summary>
    /// 当前运行时是否启用 AVX2 gather 转色路径。
    /// </summary>
    public static bool IsAvx2Accelerated => Avx2.IsSupported;

    /// <summary>
    /// 当前运行时是否启用 SSSE3 16-entry shuffle LUT 转色路径。
    /// </summary>
    public static bool IsSsse3ShuffleAccelerated => Ssse3.IsSupported;

    /// <summary>
    /// 将 material id 转为 BGRA8 像素。
    /// </summary>
    /// <param name="materialIds">源 material id。</param>
    /// <param name="paletteBgra">runtime material id 索引的 BGRA8 palette。</param>
    /// <param name="destination">目标 BGRA8 像素 span。</param>
    public static void Convert(ReadOnlySpan<ushort> materialIds, ReadOnlySpan<uint> paletteBgra, Span<uint> destination)
    {
        ValidateLengths(materialIds, destination);
        if (materialIds.Length >= 16 &&
            paletteBgra.Length > 0 &&
            paletteBgra.Length <= 16 &&
            TryConvertSsse3Shuffle16(materialIds, paletteBgra, destination))
        {
            return;
        }

        ConvertScalar(materialIds, paletteBgra, destination);
    }

    /// <summary>
    /// 标量 palette→BGRA 转色路径，作为 SIMD 路径的正确性 oracle 与 fallback。
    /// </summary>
    /// <param name="materialIds">源 material id。</param>
    /// <param name="paletteBgra">runtime material id 索引的 BGRA8 palette。</param>
    /// <param name="destination">目标 BGRA8 像素 span。</param>
    public static void ConvertScalar(ReadOnlySpan<ushort> materialIds, ReadOnlySpan<uint> paletteBgra, Span<uint> destination)
    {
        ValidateLengths(materialIds, destination);
        for (int i = 0; i < materialIds.Length; i++)
        {
            destination[i] = paletteBgra[materialIds[i]];
        }
    }

    /// <summary>
    /// AVX2 gather 转色实验路径。当前 Ryzen 5800X / .NET 10 短基准慢于标量路径，因此不作为默认热路径。
    /// </summary>
    /// <param name="materialIds">源 material id。</param>
    /// <param name="paletteBgra">runtime material id 索引的 BGRA8 palette。</param>
    /// <param name="destination">目标 BGRA8 像素 span。</param>
    public static void ConvertAvx2Experimental(ReadOnlySpan<ushort> materialIds, ReadOnlySpan<uint> paletteBgra, Span<uint> destination)
    {
        ValidateLengths(materialIds, destination);
        if (!Avx2.IsSupported || materialIds.Length < 8)
        {
            ConvertScalar(materialIds, paletteBgra, destination);
            return;
        }

        ConvertAvx2(materialIds, paletteBgra, destination);
    }

    private static void ConvertAvx2(ReadOnlySpan<ushort> materialIds, ReadOnlySpan<uint> paletteBgra, Span<uint> destination)
    {
        int i = 0;
        fixed (ushort* source = materialIds)
        fixed (uint* palette = paletteBgra)
        fixed (uint* target = destination)
        {
            for (; i <= materialIds.Length - 8; i += 8)
            {
                Vector256<int> indices = Vector256.Create(
                    source[i],
                    source[i + 1],
                    source[i + 2],
                    source[i + 3],
                    source[i + 4],
                    source[i + 5],
                    source[i + 6],
                    source[i + 7]).AsInt32();
                Vector256<uint> colors = Avx2.GatherVector256(palette, indices, 4);
                Unsafe.WriteUnaligned(target + i, colors);
            }

            for (; i < materialIds.Length; i++)
            {
                target[i] = palette[source[i]];
            }
        }
    }

    private static bool TryConvertSsse3Shuffle16(ReadOnlySpan<ushort> materialIds, ReadOnlySpan<uint> paletteBgra, Span<uint> destination)
    {
        if (!Ssse3.IsSupported)
        {
            return false;
        }

        Span<byte> blueLut = stackalloc byte[16];
        Span<byte> greenLut = stackalloc byte[16];
        Span<byte> redLut = stackalloc byte[16];
        Span<byte> alphaLut = stackalloc byte[16];
        for (int lutIndex = 0; lutIndex < paletteBgra.Length; lutIndex++)
        {
            uint color = paletteBgra[lutIndex];
            blueLut[lutIndex] = (byte)color;
            greenLut[lutIndex] = (byte)(color >> 8);
            redLut[lutIndex] = (byte)(color >> 16);
            alphaLut[lutIndex] = (byte)(color >> 24);
        }

        Vector128<byte> blue = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(blueLut));
        Vector128<byte> green = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(greenLut));
        Vector128<byte> red = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(redLut));
        Vector128<byte> alpha = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(alphaLut));
        Vector128<ushort> zero = Vector128<ushort>.Zero;
        Vector128<ushort> maxIndex = Vector128.Create((ushort)(paletteBgra.Length - 1));
        int index = 0;
        fixed (ushort* source = materialIds)
        fixed (uint* target = destination)
        {
            byte* targetBytes = (byte*)target;
            for (; index <= materialIds.Length - 16; index += 16)
            {
                Vector128<ushort> low = Vector128.LoadUnsafe(ref source[index]);
                Vector128<ushort> high = Vector128.LoadUnsafe(ref source[index + 8]);
                if (!AllLessOrEqual(low, maxIndex, zero) || !AllLessOrEqual(high, maxIndex, zero))
                {
                    return false;
                }

                Vector128<byte> indices = Sse2.PackUnsignedSaturate(low.AsInt16(), high.AsInt16());
                Vector128<byte> b = Ssse3.Shuffle(blue, indices);
                Vector128<byte> g = Ssse3.Shuffle(green, indices);
                Vector128<byte> r = Ssse3.Shuffle(red, indices);
                Vector128<byte> a = Ssse3.Shuffle(alpha, indices);
                StoreBgra16(b, g, r, a, targetBytes + (index * sizeof(uint)));
            }

            for (; index < materialIds.Length; index++)
            {
                ushort material = source[index];
                if (material >= paletteBgra.Length)
                {
                    return false;
                }

                target[index] = paletteBgra[material];
            }
        }

        return true;
    }

    private static bool AllLessOrEqual(Vector128<ushort> values, Vector128<ushort> max, Vector128<ushort> zero)
    {
        Vector128<ushort> over = Sse2.SubtractSaturate(values, max);
        Vector128<byte> equalsZero = Sse2.CompareEqual(over.AsByte(), zero.AsByte());
        return Sse2.MoveMask(equalsZero) == ushort.MaxValue;
    }

    private static void StoreBgra16(Vector128<byte> b, Vector128<byte> g, Vector128<byte> r, Vector128<byte> a, byte* destination)
    {
        Vector128<byte> bgLo = Sse2.UnpackLow(b, g);
        Vector128<byte> bgHi = Sse2.UnpackHigh(b, g);
        Vector128<byte> raLo = Sse2.UnpackLow(r, a);
        Vector128<byte> raHi = Sse2.UnpackHigh(r, a);

        Vector128<byte> pixels0 = Sse2.UnpackLow(bgLo.AsUInt16(), raLo.AsUInt16()).AsByte();
        Vector128<byte> pixels1 = Sse2.UnpackHigh(bgLo.AsUInt16(), raLo.AsUInt16()).AsByte();
        Vector128<byte> pixels2 = Sse2.UnpackLow(bgHi.AsUInt16(), raHi.AsUInt16()).AsByte();
        Vector128<byte> pixels3 = Sse2.UnpackHigh(bgHi.AsUInt16(), raHi.AsUInt16()).AsByte();

        Unsafe.WriteUnaligned(destination, pixels0);
        Unsafe.WriteUnaligned(destination + 16, pixels1);
        Unsafe.WriteUnaligned(destination + 32, pixels2);
        Unsafe.WriteUnaligned(destination + 48, pixels3);
    }

    private static void ValidateLengths(ReadOnlySpan<ushort> materialIds, Span<uint> destination)
    {
        if (destination.Length < materialIds.Length)
        {
            throw new ArgumentException("目标 BGRA span 长度不能小于 material id span。", nameof(destination));
        }
    }
}
