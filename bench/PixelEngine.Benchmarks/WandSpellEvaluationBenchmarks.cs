using BenchmarkDotNet.Attributes;
using PixelEngine.Demo;
using PixelEngine.Hosting;

namespace PixelEngine.Benchmarks;

/// <summary>
/// 测量 Demo Wand evaluator 的真实 deck/trigger 热路径。
/// 每次操作先复位到可施法状态，确保样本包含状态写入但不包含 JSON/目录加载。
/// </summary>
[MemoryDiagnoser]
public class WandSpellEvaluationBenchmarks
{
    private const ulong RunSeed = 0x5049_5845_4C57_414EUL;
    private WandSpellCatalog _catalog = null!;
    private WandDefinition _wand = null!;
    private WandRuntimeState _state = null!;
    private WandCastBuffer _buffer = null!;
    private int _startSlot;

    /// <summary>待测的 deck 分支。</summary>
    [Params(
        WandEvaluationScenario.ApprenticeModifier,
        WandEvaluationScenario.TriggerPayload,
        WandEvaluationScenario.GeomancerUtility,
        WandEvaluationScenario.ShuffledChaos)]
    public WandEvaluationScenario Scenario { get; set; }

    /// <summary>装载真实 Demo content 与原创 Wand 目录。</summary>
    [GlobalSetup]
    public void Setup()
    {
        string contentRoot = Path.Combine(FindRepositoryRoot(), "demo", "PixelEngine.Demo", "content");
        _catalog = WandSpellCatalog.Load(new EngineScriptConfigApi(contentRoot));
        string wandId = Scenario switch
        {
            WandEvaluationScenario.ApprenticeModifier => "apprentice-wand",
            WandEvaluationScenario.TriggerPayload => "trigger-wand",
            WandEvaluationScenario.GeomancerUtility => "geomancer-wand",
            WandEvaluationScenario.ShuffledChaos => "chaos-wand",
            _ => throw new ArgumentOutOfRangeException(),
        };
        _wand = _catalog.Wands[_catalog.FindWandIndex(wandId)];
        _state = new WandRuntimeState(_catalog, _wand, RunSeed);
        _buffer = new WandCastBuffer(_catalog.Limits);
        _startSlot = Scenario switch
        {
            WandEvaluationScenario.ApprenticeModifier => 1,
            WandEvaluationScenario.TriggerPayload => 0,
            WandEvaluationScenario.GeomancerUtility => 3,
            WandEvaluationScenario.ShuffledChaos => 0,
            _ => 0,
        };
    }

    /// <summary>每次操作执行 256 个完整 cast，返回计划索引避免消除。</summary>
    [Benchmark(OperationsPerInvoke = 256)]
    public int EvaluateCastBatch()
    {
        int checksum = 0;
        for (int i = 0; i < 256; i++)
        {
            _state.Reset(_catalog, _wand);
            _state.DeckCursor = _startSlot;
            WandCastResult result = WandSpellEvaluator.Evaluate(
                _catalog,
                _wand,
                _state,
                RunSeed + (uint)i,
                _buffer);
            checksum += result.ProjectileCount;
            if (result.ProjectileCount > 0)
            {
                checksum += _buffer.Projectiles[0].SpellIndex;
            }
        }

        return checksum;
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

/// <summary>Wand evaluator 的代表性 cast 分支。</summary>
public enum WandEvaluationScenario : byte
{
    /// <summary>modifier 自动 draw 到 projectile。</summary>
    ApprenticeModifier,

    /// <summary>hit/timer nested trigger payload。</summary>
    TriggerPayload,

    /// <summary>utility dig projectile。</summary>
    GeomancerUtility,

    /// <summary>带确定性 shuffle 的多发射 Wand。</summary>
    ShuffledChaos,
}
