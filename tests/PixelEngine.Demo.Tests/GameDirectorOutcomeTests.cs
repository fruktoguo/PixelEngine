using PixelEngine.Hosting;
using PixelEngine.Scripting;
using PixelEngine.Simulation;
using ScriptScene = PixelEngine.Scripting.Scene;
using Xunit;

namespace PixelEngine.Demo.Tests;

/// <summary>
/// 熔岩矿洞逃生胜负状态机与计分验收。
/// </summary>
public sealed class GameDirectorOutcomeTests
{
    /// <summary>
    /// 验证集齐 6 个水晶并进入撤离区后任务胜利，且分数由剩余时间、弹药与无伤奖励确定。
    /// </summary>
    [Fact]
    public void CollectingRequiredCrystalsAndEnteringExtractionWinsWithDeterministicScore()
    {
        string contentRoot = CreateTemporaryWeaponContent(
            """
            {
              "weapons": [
                { "id": "shot", "displayName": "Shot", "kind": "singleShot", "damage": 12, "radius": 1, "falloff": "none", "impulse": 1, "cooldownSeconds": 0, "ammoMax": 5, "tracerDuration": 0.01, "muzzleCue": "ui_click", "impactCue": "explosion", "hudColor": "#FFFFFFFF" },
                { "id": "laser", "displayName": "Laser", "kind": "laser", "radius": 1, "falloff": "none", "cooldownSeconds": 0, "ammoMax": 7, "heatPerCell": 1, "beamDps": 1, "muzzleCue": "ui_click", "impactCue": "sizzle_lava_water", "hudColor": "#FFFFFFFF" }
              ]
            }
            """);
        try
        {
            using Engine engine = CreateMissionEngine(contentRoot, out ScriptScene scene);
            MissionFixture fixture = CreateMissionEntity(scene);
            MissionDirector mission = fixture.Mission;
            mission.RequiredCrystals = 6;
            mission.TimeLimitSeconds = 60f;
            mission.TimeScorePerSecond = 10;
            mission.AmmoScorePerRound = 5;
            mission.UndamagedBonus = 500;
            mission.InitialLavaSurfaceY = 80f;
            mission.LavaRiseCellsPerSecond = 0f;
            ExtractionTrigger extraction = fixture.Entity.AddComponent<ExtractionTrigger>();
            extraction.X = 10f;
            extraction.Y = 10f;
            extraction.Width = 16f;
            extraction.Height = 24f;
            extraction.CelebrationParticleCount = 0;

            engine.RunHeadlessTicks(1);
            Assert.Equal(MissionState.Playing, mission.State);
            Assert.Equal("目标水晶未集齐或任务已结束。", extraction.BlockedReason);

            Assert.True(engine.Context.Events.Channel<MineYieldEvent>().TryEnqueue(new MineYieldEvent(20, 20, 1, 2)));
            Assert.True(engine.Context.Events.Channel<MineYieldEvent>().TryEnqueue(new MineYieldEvent(21, 20, 1, 2)));
            Assert.True(engine.Context.Events.Channel<MineYieldEvent>().TryEnqueue(new MineYieldEvent(22, 20, 1, 2)));
            engine.RunHeadlessTicks(1);

            int expectedScore =
                ((int)MathF.Floor(mission.RemainingSeconds) * mission.TimeScorePerSecond) +
                (fixture.Weapons.TotalRemainingAmmo * mission.AmmoScorePerRound) +
                mission.UndamagedBonus;
            Assert.True(extraction.Reached);
            Assert.Equal(6, mission.CrystalsCollected);
            Assert.Equal(MissionState.Won, mission.State);
            Assert.Equal("extraction_reached", mission.ResultReason);
            Assert.Equal(expectedScore, mission.Score);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    /// <summary>
    /// 验证玩家死亡会让任务进入失败状态，并记录明确失败原因。
    /// </summary>
    [Fact]
    public void PlayerDeathMarksMissionLost()
    {
        using Engine engine = CreateMissionEngine(contentRoot: null, out ScriptScene scene);
        MissionFixture fixture = CreateMissionEntity(scene);
        PlayerHealth health = fixture.Health;
        health.MaxHealth = 1f;
        health.LavaDamagePerSecond = 200f;
        health.ForceHazardForProbe = true;
        MissionDirector mission = fixture.Mission;
        mission.TimeLimitSeconds = 60f;
        mission.InitialLavaSurfaceY = 80f;
        mission.LavaRiseCellsPerSecond = 0f;

        engine.RunHeadlessTicks(2);

        Assert.Equal(MissionState.Lost, mission.State);
        Assert.Equal("player_death", mission.ResultReason);
        Assert.True(health.RespawnCount > 0);
    }

    /// <summary>
    /// 验证上涨熔岩按配置速率推进熔岩线，并同步到任务导演。
    /// </summary>
    [Fact]
    public void RisingHazardUsesConfiguredRiseRateAndSynchronizesMissionSurface()
    {
        using Engine engine = CreateMissionEngine(contentRoot: null, out ScriptScene scene);
        MissionFixture fixture = CreateMissionEntity(scene);
        MissionDirector mission = fixture.Mission;
        mission.InitialLavaSurfaceY = 100f;
        mission.LavaRiseCellsPerSecond = 0f;
        RisingHazardDirector hazard = fixture.Entity.AddComponent<RisingHazardDirector>();
        hazard.StartSurfaceY = 100f;
        hazard.TargetSurfaceY = 40f;
        hazard.RiseSeconds = 1f / 30f;
        hazard.LossSurfaceY = 73f;
        hazard.EmitterCount = 1;
        hazard.FillIntervalSeconds = 10f;

        engine.RunHeadlessTicks(1);

        Assert.InRange(hazard.CurrentSurfaceY, 69f, 72f);
        Assert.Equal(hazard.CurrentSurfaceY, mission.LavaSurfaceY, precision: 3);
    }

    private static Engine CreateMissionEngine(string? contentRoot, out ScriptScene scene)
    {
        EngineBuilder builder = new EngineBuilder()
            .UseHeadless()
            .UseDeterministicMode();
        if (!string.IsNullOrWhiteSpace(contentRoot))
        {
            _ = builder.WithContentRoot(contentRoot);
        }

        Engine engine = builder.Build();
        MaterialTable materials = Materials(
            ("empty", CellType.Empty),
            ("sand", CellType.Powder),
            ("stone", CellType.Solid),
            ("lava", CellType.Liquid),
            ("fire", CellType.Fire),
            ("acid", CellType.Liquid),
            ("ash", CellType.Powder),
            ("crystal", CellType.Solid));
        engine.Context.RegisterService(materials);
        _ = engine.AttachResidentSimulationWorld(worldWidthCells: 96, worldHeightCells: 96, particleCapacity: 64);
        scene = new ScriptScene();
        engine.Context.RegisterService(scene);
        ScriptInputApi input = new();
        ScriptCameraApi camera = new(viewportWidth: 40, viewportHeight: 20, centerX: 20, centerY: 10, zoom: 1);
        engine.Context.RegisterService<IInputApi>(EngineServiceRole.Input, input);
        engine.Context.RegisterService(input);
        engine.Context.RegisterService<ICameraApi>(EngineServiceRole.Camera, camera);
        engine.Context.RegisterService(camera);
        engine.Context.RegisterService<IAudioApi>(EngineServiceRole.AudioService, NoopAudioApi.Instance);
        _ = engine.AttachScriptingFromServices();
        return engine;
    }

    private static MissionFixture CreateMissionEntity(ScriptScene scene)
    {
        Entity entity = scene.CreateEntity();
        _ = entity.AddComponent<Transform>();
        PlayerController player = entity.AddComponent<PlayerController>();
        player.SpawnX = 12f;
        player.SpawnY = 12f;
        PlayerHealth health = entity.AddComponent<PlayerHealth>();
        WeaponController weapons = entity.AddComponent<WeaponController>();
        MissionDirector mission = entity.AddComponent<MissionDirector>();
        return new MissionFixture(entity, player, health, weapons, mission);
    }

    private static MaterialTable Materials(params (string Name, CellType Type)[] definitions)
    {
        MaterialDef[] materials = new MaterialDef[definitions.Length];
        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = new MaterialDef
            {
                Id = (ushort)i,
                Name = definitions[i].Name,
                Type = definitions[i].Type,
                Density = i == 0 ? (byte)0 : (byte)100,
                HeatCapacity = 1,
                HeatConduct = 255,
                TextureId = -1,
                MeltPoint = float.NaN,
                FreezePoint = float.NaN,
                BoilPoint = float.NaN,
                Integrity = definitions[i].Type == CellType.Solid ? (ushort)40 : (ushort)0,
                DestroyedTarget = definitions[i].Type == CellType.Solid ? (ushort)1 : (ushort)0,
                MineYield = definitions[i].Name == "crystal" ? (byte)1 : (byte)0,
            };
        }

        return new MaterialTable(materials);
    }

    private static string CreateTemporaryWeaponContent(string weaponsJson)
    {
        string directory = Path.Combine(Path.GetTempPath(), "pixelengine-outcome-tests-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "weapons.json"), weaponsJson);
        return directory;
    }

    private readonly record struct MissionFixture(
        Entity Entity,
        PlayerController Player,
        PlayerHealth Health,
        WeaponController Weapons,
        MissionDirector Mission);

    private sealed class NoopAudioApi : IAudioApi
    {
        public static NoopAudioApi Instance { get; } = new();

        public void PlayOneShot(string cue, float volume = 1f)
        {
            _ = cue;
            _ = volume;
        }

        public void PlayAt(string cue, float x, float y, float volume = 1f)
        {
            _ = cue;
            _ = x;
            _ = y;
            _ = volume;
        }
    }
}
