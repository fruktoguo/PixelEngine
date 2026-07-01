using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace PixelEngine.Rendering;

/// <summary>
/// BGRA8 连续像素混合工具。用于 render buffer 规则 span pass，复杂纹理/调试着色仍走标量采样路径。
/// </summary>
public static unsafe class BgraColorMixer
{
    private const int HashX = 73856093;
    private const int HashY = 19349663;

    /// <summary>
    /// 当前运行时是否启用 BGRA color-noise SIMD 路径。
    /// </summary>
    public static bool IsColorNoiseSimdAccelerated => Sse41.IsSupported && Ssse3.IsSupported;

    /// <summary>
    /// 对 BGRA8 span 应用与标量渲染路径一致的材质 color-noise。
    /// </summary>
    /// <param name="materialIds">每个像素对应的 material id。</param>
    /// <param name="noiseByMaterial">按 runtime material id 索引的噪声幅度。</param>
    /// <param name="pixels">目标 BGRA8 像素 span，原地修改。</param>
    /// <param name="worldX">span 首像素世界 X。</param>
    /// <param name="worldY">span 所在世界 Y。</param>
    public static void ApplyColorNoise(
        ReadOnlySpan<ushort> materialIds,
        ReadOnlySpan<byte> noiseByMaterial,
        Span<uint> pixels,
        int worldX,
        int worldY)
    {
        ValidateLengths(materialIds, pixels);
        if (!IsColorNoiseSimdAccelerated || materialIds.Length < Vector128<int>.Count)
        {
            ApplyColorNoiseScalar(materialIds, noiseByMaterial, pixels, worldX, worldY);
            return;
        }

        int index = 0;
        Vector128<int> hashX = Vector128.Create(HashX);
        Vector128<int> yHash = Vector128.Create(unchecked(worldY * HashY));
        Vector128<int> laneOffsets = Vector128.Create(0, 1, 2, 3);
        Vector128<int> mask255 = Vector128.Create(0xFF);
        Vector128<int> bias128 = Vector128.Create(128);
        Vector128<int> zero = Vector128<int>.Zero;
        Vector128<int> one = Vector128.Create(1);
        Vector128<int> maxByte = Vector128.Create(255);
        Vector128<int> alphaMask = Vector128.Create(unchecked((int)0xFF000000));

        fixed (ushort* materials = materialIds)
        fixed (uint* target = pixels)
        {
            for (; index <= materialIds.Length - Vector128<int>.Count; index += Vector128<int>.Count)
            {
                Vector128<int> amount = Vector128.Create(
                    noiseByMaterial[materials[index]],
                    noiseByMaterial[materials[index + 1]],
                    noiseByMaterial[materials[index + 2]],
                    noiseByMaterial[materials[index + 3]]);
                if (Sse41.TestZ(amount, amount))
                {
                    continue;
                }

                Vector128<int> x = Sse2.Add(Vector128.Create(unchecked(worldX + index)), laneOffsets);
                Vector128<int> hash = Sse2.Xor(Sse41.MultiplyLow(x, hashX), yHash);
                Vector128<int> centeredHash = Sse2.Subtract(Sse2.And(hash, mask255), bias128);
                Vector128<int> delta = DivideBy255TowardZero(Sse41.MultiplyLow(centeredHash, amount), zero, one);
                Vector128<int> bgra = Vector128.LoadUnsafe(ref Unsafe.AsRef<int>(target + index));

                Vector128<int> b = ClampByte(Sse2.Add(Sse2.And(bgra, mask255), delta), zero, maxByte);
                Vector128<int> g = ClampByte(Sse2.Add(Sse2.And(Sse2.ShiftRightLogical(bgra.AsUInt32(), 8).AsInt32(), mask255), delta), zero, maxByte);
                Vector128<int> r = ClampByte(Sse2.Add(Sse2.And(Sse2.ShiftRightLogical(bgra.AsUInt32(), 16).AsInt32(), mask255), delta), zero, maxByte);
                Vector128<int> a = Sse2.And(bgra, alphaMask);
                Vector128<int> mixed = Sse2.Or(
                    Sse2.Or(b, Sse2.ShiftLeftLogical(g, 8)),
                    Sse2.Or(Sse2.ShiftLeftLogical(r, 16), a));
                mixed.StoreUnsafe(ref Unsafe.AsRef<int>(target + index));
            }

            for (; index < materialIds.Length; index++)
            {
                target[index] = ApplyColorNoiseScalar(target[index], noiseByMaterial[materials[index]], worldX + index, worldY);
            }
        }
    }

    /// <summary>
    /// 标量 color-noise oracle，与 <see cref="RenderBufferBuilder" /> 慢路径保持 byte-exact。
    /// </summary>
    public static void ApplyColorNoiseScalar(
        ReadOnlySpan<ushort> materialIds,
        ReadOnlySpan<byte> noiseByMaterial,
        Span<uint> pixels,
        int worldX,
        int worldY)
    {
        ValidateLengths(materialIds, pixels);
        for (int i = 0; i < materialIds.Length; i++)
        {
            pixels[i] = ApplyColorNoiseScalar(pixels[i], noiseByMaterial[materialIds[i]], worldX + i, worldY);
        }
    }

    private static Vector128<int> DivideBy255TowardZero(Vector128<int> value, Vector128<int> zero, Vector128<int> one)
    {
        Vector128<int> abs = Ssse3.Abs(value).AsInt32();
        Vector128<int> quotientAbs = Sse2.ShiftRightLogical(
            Sse2.Add(Sse2.Add(abs, one), Sse2.ShiftRightLogical(abs.AsUInt32(), 8).AsInt32()).AsUInt32(),
            8).AsInt32();
        Vector128<int> negativeMask = Sse2.CompareGreaterThan(zero, value);
        return Sse2.Subtract(Sse2.Xor(quotientAbs, negativeMask), negativeMask);
    }

    private static Vector128<int> ClampByte(Vector128<int> value, Vector128<int> zero, Vector128<int> maxByte)
    {
        return Sse41.Min(Sse41.Max(value, zero), maxByte);
    }

    private static uint ApplyColorNoiseScalar(uint bgra, byte amount, int worldX, int worldY)
    {
        if (amount == 0)
        {
            return bgra;
        }

        uint hash = unchecked((uint)(worldX * HashX) ^ (uint)(worldY * HashY));
        int delta = ((int)(hash & 0xFF) - 128) * amount / 255;
        byte b = Adjust((byte)(bgra & 0xFF), delta);
        byte g = Adjust((byte)((bgra >> 8) & 0xFF), delta);
        byte r = Adjust((byte)((bgra >> 16) & 0xFF), delta);
        byte a = (byte)(bgra >> 24);
        return b | ((uint)g << 8) | ((uint)r << 16) | ((uint)a << 24);
    }

    private static byte Adjust(byte value, int delta)
    {
        return (byte)Math.Clamp(value + delta, 0, 255);
    }

    private static void ValidateLengths(ReadOnlySpan<ushort> materialIds, Span<uint> pixels)
    {
        if (pixels.Length < materialIds.Length)
        {
            throw new ArgumentException("目标 BGRA span 长度不能小于 material id span。", nameof(pixels));
        }
    }
}
