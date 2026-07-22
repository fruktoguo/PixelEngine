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
    private int _chunkX;
    private int _chunkY;

    /// <summary>
    /// 覆盖既有三组地表以及 DEMO-008 的主区、侧区、Portal/Holy Mountain 和终局区域。
    /// </summary>
    [Params(
        TerrainGenerationScenario.SurfaceWest,
        TerrainGenerationScenario.SurfaceOrigin,
        TerrainGenerationScenario.SurfaceEast,
        TerrainGenerationScenario.MinesDeep,
        TerrainGenerationScenario.FungalCaverns,
        TerrainGenerationScenario.PortalAndHolyMountain,
        TerrainGenerationScenario.LaboratoryDeep)]
    public TerrainGenerationScenario Scenario { get; set; }

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
        EngineScriptConfigApi configApi = new(options.ContentRoot);
        ProceduralWorldBuildRequest request = new(
            PlayableCavernWorldGenerator.Key,
            package.Materials,
            Config: configApi);
        _ = _generator.Describe(in request);
        ResolveScenario(configApi);
    }

    /// <summary>
    /// 生成横跨自然地表高度的 chunk，并返回采样值避免结果被消除。
    /// </summary>
    [Benchmark]
    public int GenerateChunk()
    {
        Array.Clear(_temperatureCells);
        _generator!.PopulatePreparedChunkForBenchmark(
            _chunkX,
            _chunkY,
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

    private void ResolveScenario(EngineScriptConfigApi configApi)
    {
        CampaignConfig config = CampaignConfig.Load(configApi);
        BiomeCatalog catalog = BiomeCatalog.Load(configApi, config);
        (_chunkX, _chunkY) = Scenario switch
        {
            TerrainGenerationScenario.SurfaceWest => (-214, 3),
            TerrainGenerationScenario.SurfaceOrigin => (0, 3),
            TerrainGenerationScenario.SurfaceEast => (123, 3),
            TerrainGenerationScenario.MinesDeep =>
                (16, ChunkCoordinate(config.RegionStartCellY(0) + (config.RegionHeightCells / 2L))),
            TerrainGenerationScenario.FungalCaverns => ResolveFungalCaverns(config, catalog),
            TerrainGenerationScenario.PortalAndHolyMountain => ResolveFirstPortal(config, catalog),
            TerrainGenerationScenario.LaboratoryDeep =>
                (23, ChunkCoordinate(config.RegionStartCellY(7) + (config.RegionHeightCells / 2L))),
            _ => throw new ArgumentOutOfRangeException(nameof(Scenario)),
        };
    }

    private static (int X, int Y) ResolveFungalCaverns(CampaignConfig config, BiomeCatalog catalog)
    {
        BiomeConnectionDefinition connection = catalog.Connections[0];
        int regionIndex = catalog.FindMainPathIndex(connection.From);
        long startY = config.RegionStartCellY(regionIndex) + connection.FromLocalDepthCells;
        long endY = config.RegionStartCellY(regionIndex) + connection.ToLocalDepthCells;
        long anchorY = startY + ((endY - startY) / 2);
        long centerX = PlayableCavernWorldGenerator.MainPathCenterX(
            anchorY,
            config,
            config.InitialRunSeed) - connection.OffsetCells;
        return (ChunkCoordinate(centerX), ChunkCoordinate(anchorY));
    }

    private static (int X, int Y) ResolveFirstPortal(CampaignConfig config, BiomeCatalog catalog)
    {
        CampaignPortalAnchor portal = PlayableCavernWorldGenerator.ResolvePortalAnchor(
            config,
            catalog.PortalNetwork,
            holyMountainIndex: 0,
            portalIndex: catalog.PortalNetwork.PortalsPerHolyMountain / 2,
            worldSeed: config.InitialRunSeed);
        return (ChunkCoordinate(portal.SourceX), ChunkCoordinate(portal.SourceY));
    }

    private static int ChunkCoordinate(long worldCell)
    {
        return checked((int)Math.Floor(worldCell / (double)ChunkSize));
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

/// <summary>无限战役 chunk 生成器的代表性地图场景。</summary>
public enum TerrainGenerationScenario : byte
{
    /// <summary>西侧自然地表。</summary>
    SurfaceWest,

    /// <summary>原点安全区地表。</summary>
    SurfaceOrigin,

    /// <summary>东侧自然地表。</summary>
    SurfaceEast,

    /// <summary>Mines 深处。</summary>
    MinesDeep,

    /// <summary>Fungal Caverns 侧区。</summary>
    FungalCaverns,

    /// <summary>首个 Portal 供能池与 Holy Mountain 交界。</summary>
    PortalAndHolyMountain,

    /// <summary>The Laboratory 深处。</summary>
    LaboratoryDeep,
}
