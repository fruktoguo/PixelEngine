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
    private uint[] _palette = [];
    private readonly uint[] _destination = new uint[256 * 256];

    /// <summary>
    /// palette 大小。16 触发 SSSE3 shuffle LUT 候选路径；256 覆盖通用 runtime material palette。
    /// </summary>
    [Params(16, 256)]
    public int PaletteSize { get; set; }

    /// <summary>
    /// 初始化 palette 转色 benchmark fixture。
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _palette = new uint[PaletteSize];
        for (int i = 0; i < _palette.Length; i++)
        {
            _palette[i] = 0xFF000000u | (uint)(i * 0x00010203u);
        }

        for (int i = 0; i < _materials.Length; i++)
        {
            _materials[i] = (ushort)(((i * 31) + (i >> 4)) & (PaletteSize - 1));
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
    /// 默认运行时 dispatcher；支持时可使用已验证的窄 SIMD 路径，否则回落标量。
    /// </summary>
    [Benchmark]
    public void Convert()
    {
        PaletteBgraConverter.Convert(_materials, _palette, _destination);
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
