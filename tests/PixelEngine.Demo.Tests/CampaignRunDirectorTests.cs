using PixelEngine.Hosting;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using PixelEngine.Scripting;
using Xunit;
using ScriptScene = PixelEngine.Scripting.Scene;

namespace PixelEngine.Demo.Tests;

/// <summary>
/// Campaign / InfiniteSandbox run 生命周期测试。
/// 不变式：战役终结只从结算页换 seed 原子重建，Sandbox 永不进入战役终局或永久死亡。
/// </summary>
public sealed class CampaignRunDirectorTests
{
    /// <summary>
    /// 验证默认战役按主菜单、探索、Holy Mountain、The Laboratory、完成、结算与新 seed 重建顺序推进。
    /// </summary>
    [Fact]
    public void CampaignLifecycleTraversesHolyMountainsLaboratorySummaryAndRequestsNewSeed()
    {
        CampaignConfig config = LoadConfig();
        RecordingRuntime runtime = new(config.InitialRunSeed);
        CampaignRunDirector director = new();
        RuntimeControlSnapshot snapshot = runtime.Capture();

        director.Initialize(config, runtime, in snapshot);

        Assert.Equal(DemoGameMode.Campaign, director.Mode);
        Assert.Equal(CampaignRunState.MainMenu, director.State);
        Assert.Equal(1, runtime.PauseCount);
        Assert.True(director.StartSelectedRun());
        Assert.Equal(CampaignRunState.StartingRun, director.State);

        director.AdvanceRun(config.SurfaceY + 64, 1f);
        Assert.Equal(CampaignRunState.Exploring, director.State);
        Assert.Equal(64, director.CurrentDepthCells);

        long firstHolyMountainY = config.SurfaceY + config.CampaignStartDepthCells + config.RegionHeightCells + 16L;
        director.AdvanceRun(firstHolyMountainY, 1f);
        Assert.Equal(CampaignRunState.HolyMountain, director.State);
        Assert.Equal("Holy Mountain", director.StateDisplayName);

        long secondRegionY = firstHolyMountainY + config.HolyMountainHeightCells;
        director.AdvanceRun(secondRegionY, 1f);
        Assert.Equal(CampaignRunState.Exploring, director.State);
        Assert.Equal(1, director.CurrentRegionIndex);

        long finalRegionY = config.SurfaceY +
            config.CampaignStartDepthCells +
            ((CampaignConfig.RequiredRegionCount - 1L) * (config.RegionHeightCells + config.HolyMountainHeightCells)) +
            16L;
        director.AdvanceRun(finalRegionY, 1f);
        Assert.Equal(CampaignRunState.Laboratory, director.State);
        Assert.Equal("The Laboratory", director.StateDisplayName);
        Assert.Equal("The Laboratory", director.CurrentRegionDisplayName);
        Assert.Equal(CampaignConfig.RequiredRegionCount - 1, director.CurrentRegionIndex);

        long completionY = finalRegionY - 16L + config.RegionHeightCells;
        director.AdvanceRun(completionY, 1f);
        Assert.Equal(CampaignRunState.Completed, director.State);
        Assert.True(director.WasCompleted);
        Assert.True(director.TransitionTerminalToSummary());
        Assert.Equal(CampaignRunState.RunSummary, director.State);
        Assert.Equal(2, runtime.PauseCount);

        RuntimeControlResult restart = director.RequestNextRun();
        Assert.True(restart.Success, restart.Message);
        Assert.True(director.IsRestartRequested);
        Assert.Equal(CampaignRunState.StartingRun, director.State);
        Assert.NotEqual(config.InitialRunSeed, director.RequestedNextSeed);
        Assert.Equal([director.RequestedNextSeed], runtime.RequestedSeeds);
        Assert.Equal(2, runtime.ResumeCount);

        runtime.Snapshot = runtime.Snapshot with { RestartStatus = RuntimeRestartStatus.Failed };
        director.PollRestartStatus();
        Assert.False(director.IsRestartRequested);
        Assert.Equal(CampaignRunState.RunSummary, director.State);
        Assert.Equal(3, runtime.PauseCount);
    }

    /// <summary>
    /// 验证原子重建后的新脚本生命周期从新世界 seed 自动进入 StartingRun，而不退回旧主菜单。
    /// </summary>
    [Fact]
    public void SuccessfulProceduralRestartInitializesNewLifecycleAtStartingRun()
    {
        CampaignConfig config = WithDefaultMode(LoadConfig(), "infiniteSandbox");
        ulong newSeed = CampaignRunDirector.DeriveNextSeed(config.InitialRunSeed);
        RecordingRuntime runtime = new(newSeed)
        {
            Snapshot = new RuntimeControlSnapshot(
                IsPlaying: false,
                IsShutdownRequested: false,
                RequestedSimHz: 60,
                FrameCount: 0,
                WorldSeed: newSeed,
                RestartStatus: RuntimeRestartStatus.Succeeded),
        };
        CampaignRunDirector director = new();
        RuntimeControlSnapshot snapshot = runtime.Capture();

        director.Initialize(config, runtime, in snapshot);

        Assert.Equal(CampaignRunState.StartingRun, director.State);
        Assert.Equal(DemoGameMode.Campaign, director.Mode);
        Assert.Equal(newSeed, director.RunSeed);
        Assert.Equal(newSeed.ToString("X16", System.Globalization.CultureInfo.InvariantCulture), director.RunSeedText);
        Assert.Equal(1, runtime.ResumeCount);
        Assert.Equal(0, runtime.PauseCount);
    }

    /// <summary>
    /// 验证 InfiniteSandbox 可无限越过战役完成深度，生命归零仍交回安全重生且不允许战役新轮请求。
    /// </summary>
    [Fact]
    public void InfiniteSandboxNeverCompletesOrConsumesPlayerDeath()
    {
        CampaignConfig config = LoadConfig();
        RecordingRuntime runtime = new(config.InitialRunSeed);
        CampaignRunDirector director = new();
        RuntimeControlSnapshot snapshot = runtime.Capture();
        director.Initialize(config, runtime, in snapshot);

        Assert.True(director.SelectMode(DemoGameMode.InfiniteSandbox));
        Assert.True(director.StartSelectedRun());
        director.AdvanceRun(config.SurfaceY + 250_000f, 2f);

        Assert.Equal(DemoGameMode.InfiniteSandbox, director.Mode);
        Assert.Equal(CampaignRunState.Exploring, director.State);
        Assert.False(director.WasCompleted);
        Assert.False(director.HandlePlayerDeath());
        Assert.False(director.RequestNextRun().Success);
        Assert.Empty(runtime.RequestedSeeds);
    }

    /// <summary>
    /// 验证 seed 链确定、非零且不会原地复用旧世界。
    /// </summary>
    [Fact]
    public void NextRunSeedChainIsDeterministicNonZeroAndChangesEveryStep()
    {
        ulong first = CampaignRunDirector.DeriveNextSeed(PlayableCavernWorldGenerator.Seed);
        ulong repeated = CampaignRunDirector.DeriveNextSeed(PlayableCavernWorldGenerator.Seed);
        ulong second = CampaignRunDirector.DeriveNextSeed(first);

        Assert.Equal(first, repeated);
        Assert.NotEqual(0UL, first);
        Assert.NotEqual(PlayableCavernWorldGenerator.Seed, first);
        Assert.NotEqual(first, second);
        Assert.NotEqual(0UL, second);
    }

    /// <summary>
    /// 验证真实 Demo Host 从永久死亡结算请求新 seed 后，会在 safe-point 替换 world/script/entity 并清除旧粒子。
    /// </summary>
    [Fact]
    public void CampaignSummaryAtomicallyReplacesLiveWorldScriptEntitiesAndParticles()
    {
        string contentRoot = ContentRoot();
        string worldRoot = Path.Combine(Path.GetTempPath(), "PixelEngine.CampaignRestart", Guid.NewGuid().ToString("N"));
        try
        {
            DemoStartupOptions options = DemoStartupOptions.Parse([
                "--headless",
                "--no-hot-reload",
                "--content",
                contentRoot,
                "--scene",
                DemoStartupOptions.DefaultSceneName,
            ]);
            EngineProject project = DemoProgram.BuildProject(options);
            using Engine engine = DemoProgram.BuildEngine(options, project);
            engine.RegisterStreamingProceduralWorldGenerator(
                PlayableCavernWorldGenerator.Key,
                new PlayableCavernWorldGenerator());
            engine.RegisterScriptAssembly(typeof(DemoProgram).Assembly);
            _ = engine.LoadContentPackage();
            _ = engine.AttachCurrentSceneWorld(proceduralWorldRoot: worldRoot);
            engine.Context.RegisterService<IAudioApi>(EngineServiceRole.AudioService, SilentAudioApi.Instance);
            _ = engine.AttachScriptingFromServices();

            RunFrames(engine, 4);
            ScriptScene originalScene = engine.Context.GetService<ScriptScene>();
            Assert.True(originalScene.TryGetFirstComponent(out CampaignRunDirector? originalRun));
            Assert.True(originalScene.TryGetFirstComponent(out PlayerHealth? originalHealth));
            Assert.Equal(CampaignRunState.MainMenu, originalRun.State);
            Assert.True(originalRun.StartSelectedRun());

            originalHealth.ApplyExternalDamage(originalHealth.MaxHealth * 2f);
            Assert.Equal(CampaignRunState.Dead, originalRun.State);
            Assert.Equal(1, originalHealth.DamageEventCount);
            _ = engine.RunOneTick(1.0 / 60.0);
            Assert.Equal(CampaignRunState.RunSummary, originalRun.State);
            Assert.Equal(0, originalHealth.RespawnCount);

            ParticleSystem oldParticles = engine.Context.GetService<ParticleSystem>();
            ParticleSpawn spawn = new(0, 0, 0, 0, Material: 1, ColorVariant: 0, Life: 60);
            Assert.True(oldParticles.TrySpawn(in spawn));
            Assert.Equal(1, oldParticles.ActiveCount);
            ulong oldSeed = engine.Context.GetService<SimulationKernel>().WorldSeed;

            RuntimeControlResult request = originalRun.RequestNextRun();
            Assert.True(request.Success, request.Message);
            ulong nextSeed = originalRun.RequestedNextSeed;
            Assert.NotEqual(oldSeed, nextSeed);
            _ = engine.RunOneTick(1.0 / 60.0);
            RunFrames(engine, 4);

            ScriptScene replacementScene = engine.Context.GetService<ScriptScene>();
            Assert.Same(originalScene, replacementScene);
            Assert.True(replacementScene.TryGetFirstComponent(out CampaignRunDirector? replacementRun));
            Assert.True(replacementScene.TryGetFirstComponent(out PlayerHealth? replacementHealth));
            Assert.NotSame(originalRun, replacementRun);
            Assert.NotSame(originalHealth, replacementHealth);
            Assert.Equal(nextSeed, engine.Context.GetService<SimulationKernel>().WorldSeed);
            Assert.Equal(nextSeed, replacementRun.RunSeed);
            Assert.Contains(
                replacementRun.State,
                new[] { CampaignRunState.StartingRun, CampaignRunState.Exploring });
            Assert.Equal(replacementHealth.MaxHealth, replacementHealth.Health);
            Assert.Equal(0, replacementHealth.DamageEventCount);
            Assert.Equal(0, replacementHealth.RespawnCount);
            Assert.Equal(0, engine.Context.GetService<ParticleSystem>().ActiveCount);
        }
        finally
        {
            if (Directory.Exists(worldRoot))
            {
                Directory.Delete(worldRoot, recursive: true);
            }
        }
    }

    private static void RunFrames(Engine engine, int count)
    {
        for (int i = 0; i < count; i++)
        {
            _ = engine.RunOneTick(1.0 / 60.0);
        }
    }

    private static CampaignConfig LoadConfig()
    {
        return CampaignConfig.Load(new EngineScriptConfigApi(ContentRoot()));
    }

    private static CampaignConfig WithDefaultMode(CampaignConfig source, string mode)
    {
        return new CampaignConfig
        {
            SchemaVersion = source.SchemaVersion,
            DefaultMode = mode,
            InitialRunSeed = source.InitialRunSeed,
            SurfaceY = source.SurfaceY,
            CampaignStartDepthCells = source.CampaignStartDepthCells,
            RegionHeightCells = source.RegionHeightCells,
            HolyMountainHeightCells = source.HolyMountainHeightCells,
            MainPathHalfWidthCells = source.MainPathHalfWidthCells,
            MainPathEntranceX = source.MainPathEntranceX,
            MainPathWanderCells = source.MainPathWanderCells,
            HolyMountainHalfWidthCells = source.HolyMountainHalfWidthCells,
            HolyMountainShellMaterial = source.HolyMountainShellMaterial,
            HolyMountainPlatformMaterial = source.HolyMountainPlatformMaterial,
            Regions = source.Regions,
        }.Validate();
    }

    private static string ContentRoot()
    {
        return Path.Combine(FindRepositoryRoot(), "demo", "PixelEngine.Demo", "content");
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

        throw new InvalidOperationException("无法从测试输出目录定位 PixelEngine.sln。");
    }

    private sealed class RecordingRuntime(ulong worldSeed) : IRuntimeControlApi
    {
        public RuntimeControlSnapshot Snapshot { get; set; } = new(
            IsPlaying: false,
            IsShutdownRequested: false,
            RequestedSimHz: 60,
            FrameCount: 0,
            WorldSeed: worldSeed);

        public int PauseCount { get; private set; }

        public int ResumeCount { get; private set; }

        public List<ulong> RequestedSeeds { get; } = [];

        public RuntimeControlSnapshot Capture()
        {
            return Snapshot;
        }

        public void PauseSimulation()
        {
            PauseCount++;
            Snapshot = Snapshot with { IsPlaying = false };
        }

        public void ResumeSimulation()
        {
            ResumeCount++;
            Snapshot = Snapshot with { IsPlaying = true };
        }

        public RuntimeControlResult RequestShutdown()
        {
            return new RuntimeControlResult(true, "shutdown");
        }

        public RuntimeControlResult OpenEditor()
        {
            return new RuntimeControlResult(false, "unsupported");
        }

        public RuntimeControlResult RequestRestartCurrentScene()
        {
            return new RuntimeControlResult(true, "scene restart");
        }

        public RuntimeControlResult RequestRestartCurrentProceduralWorld(ulong requestedSeed)
        {
            RequestedSeeds.Add(requestedSeed);
            Snapshot = Snapshot with { RestartStatus = RuntimeRestartStatus.Pending };
            return new RuntimeControlResult(true, "procedural restart");
        }

        public RuntimeSettingsSnapshot CaptureSettings()
        {
            return new RuntimeSettingsSnapshot(true, true, true, true);
        }

        public RuntimeControlResult SetVSyncEnabled(bool enabled)
        {
            return new RuntimeControlResult(true, enabled.ToString());
        }

        public RuntimeControlResult SetAudioEnabled(bool enabled)
        {
            return new RuntimeControlResult(true, enabled.ToString());
        }
    }

    private sealed class SilentAudioApi : IAudioApi
    {
        public static SilentAudioApi Instance { get; } = new();

        public void PlayOneShot(string cue, float volume = 1f)
        {
        }

        public void PlayAt(string cue, float x, float y, float volume = 1f)
        {
        }
    }
}
