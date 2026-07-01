using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace PixelEngine.Rendering;

/// <summary>
/// 将 material id span 通过 BGRA palette 转为 render buffer 像素。标量路径是默认热路径；AVX2 gather 路径仅供显式基准验证。
/// </summary>
public static unsafe class PaletteBgraConverter
{
    /// <summary>
    /// 当前运行时是否启用 AVX2 gather 转色路径。
    /// </summary>
    public static bool IsAvx2Accelerated => Avx2.IsSupported;

    /// <summary>
    /// 将 material id 转为 BGRA8 像素。
    /// </summary>
    /// <param name="materialIds">源 material id。</param>
    /// <param name="paletteBgra">runtime material id 索引的 BGRA8 palette。</param>
    /// <param name="destination">目标 BGRA8 像素 span。</param>
    public static void Convert(ReadOnlySpan<ushort> materialIds, ReadOnlySpan<uint> paletteBgra, Span<uint> destination)
    {
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

    private static void ValidateLengths(ReadOnlySpan<ushort> materialIds, Span<uint> destination)
    {
        if (destination.Length < materialIds.Length)
        {
            throw new ArgumentException("目标 BGRA span 长度不能小于 material id span。", nameof(destination));
        }
    }
}
