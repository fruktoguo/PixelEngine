using BenchmarkDotNet.Attributes;
using PixelEngine.Audio;
using PixelEngine.Demo;
using PixelEngine.Hosting;

namespace PixelEngine.Benchmarks;

/// <summary>
/// 使用真实 lava-mine 内容、脚本与 640×360 常驻世界的反应/温度 tick 基准。
/// 通过每次迭代重建场景，保证每个样本都从同一高密度反应候选布局开始。
/// </summary>
[MemoryDiagnoser]
[InvocationCount(1, unrollFactor: 1)]
public class LavaMineReactionTemperatureBenchmarks
{
    private const int WorldWidthCells = 640;
    private const int WorldHeightCells = 360;
    // 第 1 tick 布置 probe，后两 tick 完成反应副作用与温度 halo 的 scratch 容量预热；三者均不计入 BDN 样本。
    private const int StableWarmupTicks = 3;
    private Engine? _engine;

    /// <summary>
    /// 准备真实内容中存在反应表命中的 lava-mine 热点。
    /// </summary>
    [IterationSetup(Target = nameof(LavaMineReactionHitAndTemperatureTick))]
    public void SetupReactionHit()
    {
        SetupScenario(reactionHit: true);
    }

    /// <summary>
    /// 准备真实内容中存在反应表查找但无匹配规则的 lava-mine 热点。
    /// </summary>
    [IterationSetup(Target = nameof(LavaMineReactionMissAndTemperatureTick))]
    public void SetupReactionMiss()
    {
        SetupScenario(reactionHit: false);
    }

    /// <summary>
    /// 清理本迭代专属 Engine，避免把前一 tick 的反应产物带入下一个样本。
    /// </summary>
    [IterationCleanup]
    public void Cleanup()
    {
        _engine?.Dispose();
        _engine = null;
    }

    /// <summary>
    /// 真实 lava-mine 内容上的反应命中与温度 stencil tick。
    /// </summary>
    [Benchmark(Baseline = true)]
    public void LavaMineReactionHitAndTemperatureTick()
    {
        _engine!.RunHeadlessTicks(1);
    }

    /// <summary>
    /// 真实 lava-mine 内容上的反应查找未命中与温度 stencil tick。
    /// </summary>
    [Benchmark]
    public void LavaMineReactionMissAndTemperatureTick()
    {
        _engine!.RunHeadlessTicks(1);
    }

    private void SetupScenario(bool reactionHit)
    {
        Cleanup();

        string root = FindRepositoryRoot();
        DemoStartupOptions options = new()
        {
            Headless = true,
            HeadlessTicks = 0,
            HotReloadEnabled = false,
            ContentRoot = Path.Combine(root, "demo", "PixelEngine.Demo", "content"),
            Scene = Path.Combine("scenes", "lava-mine.scene"),
        };
        EngineProject project = DemoProgram.BuildProject(options);
        Engine engine = DemoProgram.BuildEngine(options, project);
        try
        {
            _ = engine.LoadContentPackage();
            _ = engine.AttachCurrentSceneWorld();
            _ = engine.AttachResidentSimulationWorld(WorldWidthCells, WorldHeightCells);
            _ = engine.AttachPhysics();
            _ = engine.AttachAudioFromContentAsync(new NullAudioBackend()).AsTask().GetAwaiter().GetResult();
            engine.RegisterScriptAssembly(typeof(DemoProgram).Assembly);
            _ = engine.AttachScriptingFromServices();

            LavaMineThermalReactionScenario scenario = new(engine.Probe, reactionHit);
            scenario.RegisterPhases(engine.Phases);
            engine.RunHeadlessTicks(StableWarmupTicks);
            _engine = engine;
        }
        catch
        {
            engine.Dispose();
            throw;
        }
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

    /// <summary>
    /// 只通过 Hosting 的公开 <see cref="EngineProbeApi"/> 在输入相位布置反应与温度热点。
    /// </summary>
    private sealed class LavaMineThermalReactionScenario(EngineProbeApi probe, bool reactionHit) : IEnginePhaseDriver
    {
        private readonly EngineProbeApi _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        private readonly bool _reactionHit = reactionHit;
        private bool _initialized;
        private ushort _empty;
        private ushort _water;
        private ushort _lava;
        private ushort _stone;
        private ushort _steam;
        private ushort _moltenMetal;
        private ushort _metal;
        private ushort _fire;
        private ushort _wood;
        private ushort _oil;
        private ushort _acid;
        private ushort _ice;
        private ushort _sand;
        private ushort _glass;
        private ushort _dirt;
        private ushort _gravel;
        private ushort _crystal;

        public void RegisterPhases(EnginePhasePipeline phases)
        {
            ArgumentNullException.ThrowIfNull(phases);
            phases.Register(EnginePhase.GameLogicAndScripts, Initialize);
        }

        private void Initialize(EngineTickContext context)
        {
            _ = context;
            if (_initialized)
            {
                return;
            }

            ResolveMaterials();
            ClearArea(24, 24, 360, 280);
            BuildReactionCandidates();
            BuildTemperatureStencils();
            _initialized = true;
        }

        private void ResolveMaterials()
        {
            _empty = Require("empty");
            _water = Require("water");
            _lava = Require("lava");
            _stone = Require("stone");
            _steam = Require("steam");
            _moltenMetal = Require("molten_metal");
            _metal = Require("metal");
            _fire = Require("fire");
            _wood = Require("wood");
            _oil = Require("oil");
            _acid = Require("acid");
            _ice = Require("ice");
            _sand = Require("sand");
            _glass = Require("glass");
            _dirt = Require("dirt");
            _gravel = Require("gravel");
            _crystal = Require("crystal");
        }

        private void BuildReactionCandidates()
        {
            if (_reactionHit)
            {
                FillPairs(32, 32, _lava, _water);
                FillPairs(88, 32, _moltenMetal, _water);
                FillPairs(144, 32, _water, _fire);
                FillPairs(200, 32, _fire, _wood);
                FillPairs(256, 32, _fire, _oil);
                FillPairs(312, 32, _acid, _stone);
                FillPairs(32, 64, _steam, _stone);
                return;
            }

            // water/lava 都含反应切片，但右邻材质不匹配任何 content/reactions.json 规则。
            FillPairs(32, 32, _water, _glass);
            FillPairs(88, 32, _lava, _metal);
            FillPairs(144, 32, _water, _crystal);
            FillPairs(200, 32, _lava, _gravel);
            FillPairs(256, 32, _dirt, _glass);
            FillPairs(312, 32, _sand, _crystal);
            FillPairs(32, 64, _oil, _stone);
        }

        private void BuildTemperatureStencils()
        {
            FillBlockWithTemperature(32, 176, _ice, 20f);
            FillBlockWithTemperature(64, 176, _water, 140f);
            FillBlockWithTemperature(96, 176, _water, -20f);
            FillBlockWithTemperature(128, 176, _lava, 100f);
            FillBlockWithTemperature(160, 176, _metal, 1_050f);
            FillBlockWithTemperature(192, 176, _sand, 1_000f);
        }

        private void FillPairs(int x, int y, ushort left, ushort right)
        {
            BuildBasin(x, y, 50, 20);
            for (int yy = y + 1; yy < y + 19; yy++)
            {
                for (int xx = x + 1; xx < x + 49; xx += 2)
                {
                    WriteCell(xx, yy, left);
                    WriteCell(xx + 1, yy, right);
                }
            }
        }

        private void FillBlockWithTemperature(int x, int y, ushort material, float targetTemperature)
        {
            BuildBasin(x, y, 18, 18);
            for (int yy = y + 1; yy < y + 17; yy++)
            {
                for (int xx = x + 1; xx < x + 17; xx++)
                {
                    WriteCell(xx, yy, material);
                }
            }

            for (int yy = y + 1; yy < y + 17; yy += 4)
            {
                for (int xx = x + 1; xx < x + 17; xx += 4)
                {
                    _probe.SetTemperature(xx, yy, targetTemperature);
                }
            }
        }

        private void BuildBasin(int x, int y, int width, int height)
        {
            for (int yy = y; yy < y + height; yy++)
            {
                for (int xx = x; xx < x + width; xx++)
                {
                    if (yy == y || yy == y + height - 1 || xx == x || xx == x + width - 1)
                    {
                        WriteCell(xx, yy, _glass);
                    }
                }
            }
        }

        private void ClearArea(int minX, int minY, int maxX, int maxY)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    WriteCell(x, y, _empty);
                }
            }
        }

        private ushort Require(string materialName)
        {
            return _probe.ResolveMaterial(materialName);
        }

        private void WriteCell(int x, int y, ushort material)
        {
            _probe.EditCellAtInputPhase(x, y, material);
        }
    }
}
