using BenchmarkDotNet.Attributes;
using PixelEngine.Demo;
using PixelEngine.Hosting;

namespace PixelEngine.Benchmarks;

/// <summary>
/// 区分 BitmapCaves 冷 block 栅格化、驻留采样和游戏实际 marker 扫描窗口的成本。
/// </summary>
[MemoryDiagnoser]
public class NoitaBitmapCavesBenchmarks
{
    private const ulong WorldSeed = 0x5049_5845_4C53_4248UL;
    private readonly NoitaWangMarkerAnchor[] _anchors = new NoitaWangMarkerAnchor[96];
    private DecodedNoitaBitmapCaves? _caves;
    private NoitaWangTerrainCatalog? _wang;
    private CampaignConfig? _campaign;
    private BiomeCatalog? _biomes;
    private ulong _salt;
    private int _coldBlock;

    /// <summary>加载真实 Demo 目录并在测量前完成线程缓存的一次性分配。</summary>
    [GlobalSetup]
    public void Setup()
    {
        string contentRoot = Path.Combine(FindRepositoryRoot(), "demo", "PixelEngine.Demo", "content");
        EngineScriptConfigApi configApi = new(contentRoot);
        _campaign = CampaignConfig.Load(configApi);
        _biomes = BiomeCatalog.Load(configApi, _campaign);
        _wang = NoitaWangTerrainCatalog.Load(configApi);
        _caves = _wang.FindDefinitionForReferenceBiome("coalmine").DecodedBitmapCaves ??
            throw new InvalidOperationException("coalmine 缺少 BitmapCaves。");
        _salt = StableIdSalt("coalmine");

        _ = _caves.TrySample(0, 0, WorldSeed, _salt, out _);
        _ = PlayableCavernWorldGenerator.CollectWangMarkerAnchors(
            _biomes,
            _wang,
            _campaign,
            WorldSeed,
            minimumX: -256,
            minimumY: 448,
            maximumX: 768,
            maximumY: 1_216,
            _anchors);
    }

    /// <summary>每次访问新的 512x256 block，包含几何编译、空间索引和语义栅格化。</summary>
    [Benchmark]
    public int ColdBitmapBlock()
    {
        int block = ++_coldBlock;
        bool overridden = _caves!.TrySample(
            (long)block * _caves.SizeX,
            (long)(block * 17) * _caves.SizeY,
            WorldSeed,
            _salt,
            out byte semantic);
        return overridden ? semantic + 1 : 0;
    }

    /// <summary>已驻留 block 的单 cell 查询。</summary>
    [Benchmark]
    public int HotBitmapSample()
    {
        bool overridden = _caves!.TrySample(256, 128, WorldSeed, _salt, out byte semantic);
        return overridden ? semantic + 1 : 0;
    }

    /// <summary>NoitaWangMarkerContentSystem 每 0.35 秒使用的实际玩家附近窗口。</summary>
    [Benchmark]
    public int MarkerWindow()
    {
        return PlayableCavernWorldGenerator.CollectWangMarkerAnchors(
            _biomes!,
            _wang!,
            _campaign!,
            WorldSeed,
            minimumX: -256,
            minimumY: 448,
            maximumX: 768,
            maximumY: 1_216,
            _anchors);
    }

    private static ulong StableIdSalt(string value)
    {
        ulong hash = 14_695_981_039_346_656_037UL;
        for (int i = 0; i < value.Length; i++)
        {
            hash ^= value[i];
            hash *= 1_099_511_628_211UL;
        }

        return hash;
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
