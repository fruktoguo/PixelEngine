using BenchmarkDotNet.Attributes;
using PixelEngine.Rendering;
using PixelEngine.Simulation;

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
    /// 验证Convert Scalar。
    /// </summary>
    [Benchmark(Baseline = true)]
    public void ConvertScalar()
    {
        PaletteBgraConverter.ConvertScalar(_materials, _palette, _destination);
    }

    /// <summary>
    /// 验证Convert。
    /// </summary>
    [Benchmark]
    public void Convert()
    {
        PaletteBgraConverter.Convert(_materials, _palette, _destination);
    }

    /// <summary>
    /// 验证Convert Avx2Experimental。
    /// </summary>
    [Benchmark]
    public void ConvertAvx2Experimental()
    {
        PaletteBgraConverter.ConvertAvx2Experimental(_materials, _palette, _destination);
    }
}

/// <summary>
/// BGRA8 color-noise 混合标量与 SIMD 路径基准。
/// </summary>
[MemoryDiagnoser]
public class BgraColorNoiseBenchmarks
{
    private readonly ushort[] _materials = new ushort[256 * 256];
    private readonly uint[] _pixels = new uint[256 * 256];
    private byte[] _noise = [];

    /// <summary>
    /// material 表大小。
    /// </summary>
    [Params(16, 256)]
    public int MaterialCount { get; set; }

    /// <summary>
    /// 初始化 color-noise benchmark fixture。
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _noise = new byte[MaterialCount];
        for (int i = 0; i < _noise.Length; i++)
        {
            _noise[i] = (byte)((i * 37) & 0xFF);
        }

        for (int i = 0; i < _materials.Length; i++)
        {
            _materials[i] = (ushort)(((i * 11) + (i >> 3)) & (MaterialCount - 1));
            _pixels[i] = 0xFF202040u + (uint)(i * 0x00030507u);
        }
    }

    /// <summary>
    /// 验证Apply Color Noise Scalar。
    /// </summary>
    [Benchmark(Baseline = true)]
    public void ApplyColorNoiseScalar()
    {
        BgraColorMixer.ApplyColorNoiseScalar(_materials, _noise, _pixels, worldX: -37, worldY: 19);
    }

    /// <summary>
    /// 验证Apply Color Noise。
    /// </summary>
    [Benchmark]
    public void ApplyColorNoise()
    {
        BgraColorMixer.ApplyColorNoise(_materials, _noise, _pixels, worldX: -37, worldY: 19);
    }
}

/// <summary>
/// RenderStyle 开启时，未破损纯固体段走 palette SIMD / color-noise SIMD 的相位 9 基准入口。
/// </summary>
[MemoryDiagnoser]
public class RenderStyleSegmentedBenchmarks
{
    private const ushort Stone = 1;
    private readonly Chunk _chunk = new(new ChunkCoord(0, 0));
    private readonly TestChunkSource _chunks;
    private readonly MaterialTable _materials;
    private readonly TemperatureField _temperature = new();
    private readonly RenderBuffer _target = new(256, 256);
    private readonly RenderAuxBuffers _aux = new(256, 256);
    private readonly RenderBufferBuilder _builder = new();
    private readonly RenderFrameContext _context;

    /// <summary>
    /// 初始化 RenderStyle 分段快路径 benchmark fixture。
    /// </summary>
    public RenderStyleSegmentedBenchmarks()
    {
        _chunks = new TestChunkSource(_chunk);
        _materials = new MaterialTable(
        [
            new MaterialDef
            {
                Id = 0,
                Name = "empty",
                Type = CellType.Empty,
                BaseColorBGRA = 0,
                TextureId = -1,
                HeatCapacity = 1f,
            },
            new MaterialDef
            {
                Id = Stone,
                Name = "stone",
                Type = CellType.Solid,
                BaseColorBGRA = 0xFF304050u,
                ColorNoise = 16,
                TextureId = -1,
                HeatCapacity = 1f,
                RenderStyle = MaterialRenderStyle.Solid,
            },
        ]);
        _context = new RenderFrameContext(
            _chunks,
            _materials,
            _temperature,
            CameraState.OneToOne(0, 0, 256, 256),
            simStepped: true);

        for (int i = 0; i < _chunk.MaterialBuffer.Length; i++)
        {
            _chunk.MaterialBuffer[i] = Stone;
        }
    }

    /// <summary>
    /// 验证Build Render Buffer Styled Segmented行为符合预期。
    /// </summary>
    [Benchmark]
    public void BuildRenderBufferStyledSegmented()
    {
        _builder.Build(_context, _target, _aux);
    }

    private sealed class TestChunkSource(Chunk chunk) : IChunkSource
    {
        private readonly Chunk[] _resident = [chunk];

        public ReadOnlySpan<Chunk> ResidentChunks => _resident;

        public bool TryGetChunk(ChunkCoord coord, out Chunk result)
        {
            if (coord == chunk.Coord)
            {
                result = chunk;
                return true;
            }

            result = null!;
            return false;
        }

        public bool ResolveNeighborhood(ChunkCoord center, out ChunkNeighborhood neighborhood)
        {
            neighborhood = default;
            return false;
        }
    }
}

/// <summary>
/// RenderStyle 分段 eligibility scanner 的 SIMD / scalar fallback 反汇编基准。
/// </summary>
[MemoryDiagnoser]
public class RenderStyleSegmentScannerBenchmarks
{
    private const ushort Stone = 1;
    private readonly ushort[] _materials = new ushort[1024];
    private readonly byte[] _damage = new byte[1024];

    /// <summary>
    /// 初始化 scanner benchmark fixture。
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        Array.Fill(_materials, Stone);
        Array.Clear(_damage);
        _damage[^1] = 1;
    }

    /// <summary>
    /// 验证Count Solid Unbroken Run。
    /// </summary>
    [Benchmark]
    public int CountSolidUnbrokenRun()
    {
        return RenderStyleSegmentScanner.CountSolidUnbrokenRun(
            _materials,
            _damage,
            0,
            _materials.Length,
            Stone);
    }
}
