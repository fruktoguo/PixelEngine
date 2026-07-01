using BenchmarkDotNet.Attributes;
using PixelEngine.Rendering;

namespace PixelEngine.Benchmarks;

/// <summary>
/// material palette 到 BGRA8 render buffer 的标量与 SIMD 转色基准。
/// </summary>
[MemoryDiagnoser]
public class PaletteBgraConversionBenchmarks
{
    private readonly ushort[] _materials = new ushort[256 * 256];
    private readonly uint[] _palette = new uint[256];
    private readonly uint[] _destination = new uint[256 * 256];

    /// <summary>
    /// 创建 palette 转色 benchmark fixture。
    /// </summary>
    public PaletteBgraConversionBenchmarks()
    {
        for (int i = 0; i < _palette.Length; i++)
        {
            _palette[i] = 0xFF000000u | (uint)(i * 0x00010203u);
        }

        for (int i = 0; i < _materials.Length; i++)
        {
            _materials[i] = (ushort)(((i * 31) + (i >> 4)) & 255);
        }
    }

    /// <summary>
    /// 标量 palette 转 BGRA8。
    /// </summary>
    [Benchmark(Baseline = true)]
    public void ConvertScalar()
    {
        PaletteBgraConverter.ConvertScalar(_materials, _palette, _destination);
    }

    /// <summary>
    /// 运行时 light-up 转色路径；AVX2 可用时使用 gather，否则回落标量。
    /// </summary>
    [Benchmark]
    public void ConvertAvx2Experimental()
    {
        PaletteBgraConverter.ConvertAvx2Experimental(_materials, _palette, _destination);
    }
}
