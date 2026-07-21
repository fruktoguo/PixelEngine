using BenchmarkDotNet.Attributes;
using PixelEngine.Demo;
using PixelEngine.Hosting;

namespace PixelEngine.Benchmarks;

/// <summary>
/// 使用 Demo 真实材质目录测量一个 64x64 无限战役 chunk 的确定性生成成本。
/// </summary>
[MemoryDiagnoser]
public class InfiniteTerrainChunkGenerationBenchmarks : IDisposable
{
    private const int ChunkSize = 64;
    private const int TemperatureSize = 16;
    private readonly ushort[] _materialCells = new ushort[ChunkSize * ChunkSize];
    private readonly Half[] _temperatureCells = new Half[TemperatureSize * TemperatureSize];
    private Engine? _engine;
    private PlayableCavernWorldGenerator? _generator;

    /// <summary>
    /// 覆盖西侧盆地、原点安全区和东侧山脉的代表性 chunk。
    /// </summary>
    [Params(-214, 0, 123)]
    public int ChunkX { get; set; }

    /// <summary>
    /// 装载真实 Demo content；内容解析不计入 benchmark 样本。
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        string root = FindRepositoryRoot();
        DemoStartupOptions options = new()
        {
            Headless = true,
            HeadlessTicks = 0,
            HotReloadEnabled = false,
            ContentRoot = Path.Combine(root, "demo", "PixelEngine.Demo", "content"),
            Scene = DemoStartupOptions.DefaultSceneName,
        };

        EngineProject project = DemoProgram.BuildProject(options);
        _engine = DemoProgram.BuildEngine(options, project);
        EngineContentPackage package = _engine.LoadContentPackage();
        _generator = new PlayableCavernWorldGenerator();
        ProceduralWorldBuildRequest request = new(
            PlayableCavernWorldGenerator.Key,
            package.Materials,
            Config: new EngineScriptConfigApi(options.ContentRoot));
        _ = _generator.Describe(in request);
    }

    /// <summary>
    /// 生成横跨自然地表高度的 chunk，并返回采样值避免结果被消除。
    /// </summary>
    [Benchmark]
    public int GenerateChunk()
    {
        Array.Clear(_temperatureCells);
        _generator!.PopulatePreparedChunkForBenchmark(
            ChunkX,
            chunkY: 3,
            _materialCells,
            _temperatureCells);
        return _materialCells[0] ^
            _materialCells[ChunkSize * 21] ^
            _materialCells[(ChunkSize * 42) + 31] ^
            _materialCells[^1];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _engine?.Dispose();
        _engine = null;
        _generator = null;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PixelEngine.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("无法从 benchmark 输出目录定位 PixelEngine.sln。");
    }
}
