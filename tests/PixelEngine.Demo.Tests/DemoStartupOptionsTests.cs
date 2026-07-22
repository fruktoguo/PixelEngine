using PixelEngine.Hosting;
using PixelEngine.Rendering;
using PixelEngine.Serialization;
using PixelEngine.Simulation;
using PixelEngine.Scripting;
using PixelEngine.UI;
using PixelEngine.World;
using Xunit;

namespace PixelEngine.Demo.Tests;

/// <summary>
/// Demo 启动参数解析测试。
/// 不变式：命令行开关解析确定、非法组合给出明确默认值或错误。
/// </summary>
public sealed class DemoStartupOptionsTests
{
    /// <summary>
    /// 验证 Editor/Build 使用的 Player Settings 与独立 Demo startup 保持同一场景、窗口和 RmlUi 后端。
    /// </summary>
    [Fact]
    public void PlayerSettingsAndStandaloneStartupUseTheSameRuntimeDefaults()
    {
        string projectRoot = Path.Combine(FindRepositoryRoot(), "demo", "PixelEngine.Demo");
        PlayerSettingsDto player = EngineProjectSettingsStore.LoadPlayerSettings(projectRoot);
        EngineProjectStartupSettings startup = EngineProjectSettingsStore.LoadStartupSettings(
            Path.Combine(projectRoot, "content"));

        Assert.Equal(startup.StartScene, player.StartupScene);
        Assert.Equal(startup.WindowWidth, player.WindowWidth);
        Assert.Equal(startup.WindowHeight, player.WindowHeight);
        Assert.Equal(startup.WindowMode, player.WindowMode);
        Assert.Equal(startup.VSync, player.VSync);
        Assert.Equal(startup.RuntimeUiBackend, player.RuntimeUiBackend);
        Assert.Equal(UiBackendKind.RmlUi, player.RuntimeUiBackend);
    }

    /// <summary>
    /// 验证默认启动进入窗口模式，并默认进入玩家包声明的真实可玩关卡。
    /// </summary>
    [Fact]
    public void DefaultOptionsSelectWindowedRuntime()
    {
        DemoStartupOptions options = DemoStartupOptions.Parse([]);

        Assert.False(options.Headless);
        Assert.True(options.HotReloadEnabled);
        Assert.Equal("scenes/infinite-sandbox.scene", options.Scene);
        Assert.Equal("playable-world", DemoStartupOptions.DefaultSceneName);
    }

    /// <summary>
    /// 验证默认项目模型进入承载流式生成器的无限沙盒场景文件。
    /// </summary>
    [Fact]
    public void DefaultProjectUsesInfiniteSandboxScene()
    {
        DemoStartupOptions options = DemoStartupOptions.Parse([]);

        EngineProject project = DemoProgram.BuildProject(options);
        SceneDescriptor scene = project.Scenes[0];

        Assert.Equal("infinite-sandbox", project.StartScene);
        Assert.Equal(SceneSourceKind.SceneFile, scene.SourceKind);
        Assert.False(string.IsNullOrEmpty(scene.Source));
        string source = scene.Source;
        Assert.EndsWith("content/scenes/infinite-sandbox.scene", source.Replace('\\', '/'), StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证玩家包 content/startup.json 能覆盖默认启动场景，且显式 --scene 仍有最高优先级。
    /// </summary>
    [Fact]
    public void StartupJsonSelectsPackagedStartSceneUnlessSceneIsExplicit()
    {
        // Arrange：准备输入与初始状态
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-startup-json-" + Guid.NewGuid().ToString("N"));
        try
        {
            _ = Directory.CreateDirectory(temp);
            File.WriteAllText(
                Path.Combine(temp, "startup.json"),
                                     /*lang=json,strict*/
                                     """
                {
                  "startScene": "scenes/lava-mine.scene"
                }
                """);

            DemoStartupOptions packaged = DemoStartupOptions.Parse(["--content", temp]);
            // Assert：验证预期结果
            Assert.Equal("scenes/lava-mine.scene", packaged.Scene);

            DemoStartupOptions explicitScene = DemoStartupOptions.Parse([
                "--content", temp,
                "--scene", "scenes/other.scene",
            ]);
            Assert.Equal("scenes/other.scene", explicitScene.Scene);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证玩家包 startup.json 会把 Player Settings 窗口与 UI 后端字段投影到 Demo runtime。
    /// </summary>
    [Fact]
    public void StartupJsonFeedsPlayerWindowAndRuntimeSettings()
    {
        // Arrange：准备输入与初始状态
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-startup-player-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            _ = Directory.CreateDirectory(temp);
            File.WriteAllText(
                Path.Combine(temp, "startup.json"),
                                     /*lang=json,strict*/
                                     """
                {
                  "startScene": "scenes/player-settings.scene",
                  "windowTitle": "Player Settings Runtime",
                  "windowWidth": 1440,
                  "windowHeight": 810,
                  "windowMode": "MaximizedWindow",
                  "vSync": false,
                  "runtimeUiBackend": "Ultralight",
                  "releaseChannel": "Production"
                }
                """);

            DemoStartupOptions options = DemoStartupOptions.Parse(["--content", temp]);
            DemoStartupOptions explicitMode = DemoStartupOptions.Parse(
                ["--window-mode", "BorderlessFullscreen", "--content", temp]);
            EngineProject project = DemoProgram.BuildProject(options);
            using Engine engine = DemoProgram.BuildEngine(options, project);

            // Assert：验证预期结果
            Assert.Equal("scenes/player-settings.scene", options.Scene);
            Assert.Equal("Player Settings Runtime", options.WindowTitle);
            Assert.Equal(1440, options.WindowWidth);
            Assert.Equal(810, options.WindowHeight);
            Assert.Equal(PlayerWindowMode.MaximizedWindow, options.WindowMode);
            Assert.Equal(PlayerWindowMode.BorderlessFullscreen, explicitMode.WindowMode);
            Assert.False(options.VSync);
            Assert.Equal(UiBackendKind.Ultralight, options.RuntimeUiBackend);
            Assert.Equal(PlayerReleaseChannel.Production, options.ReleaseChannel);
            Assert.Equal("Player Settings Runtime", engine.Context.Options.WindowTitle);
            Assert.Equal(1440, engine.Context.Options.WindowWidth);
            Assert.Equal(810, engine.Context.Options.WindowHeight);
            Assert.Equal(PlayerWindowMode.MaximizedWindow, engine.Context.Options.WindowMode);
            Assert.False(engine.Context.Options.VSync);
            Assert.True(engine.Context.Options.EnableGameUi);
            Assert.Equal(UiBackendKind.Ultralight, engine.Context.Options.GameUiBackend);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证玩家包 startup.json 指向存档目录时，Demo 项目模型会走 SaveDirectory 来源并由 Hosting 恢复世界。
    /// </summary>
    [Fact]
    public void StartupJsonSaveDirectoryRestoresWorldThroughPlayerProject()
    {
        // Arrange：准备输入与初始状态
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-startup-save-" + Guid.NewGuid().ToString("N"));
        try
        {
            string savePath = Path.Combine(temp, "saves", "mine");
            _ = Directory.CreateDirectory(temp);
            File.WriteAllText(
                Path.Combine(temp, "startup.json"),
                                     /*lang=json,strict*/
                                     """
                {
                  "startScene": "saves/mine"
                }
                """);

            MaterialTable materials = new(
                [
                    new MaterialDef { Id = 0, Name = "empty", Type = CellType.Empty },
                    new MaterialDef { Id = 1, Name = "sand", Type = CellType.Powder },
                ]);
            ResidentChunkMap chunks = new();
            Chunk chunk = new(new ChunkCoord(0, 0));
            chunk.MaterialBuffer[0] = 1;
            chunks.Add(chunk);
            _ = new WorldSaveService().SaveAll(
                new WorldSaveContext(
                    chunks,
                    new ResidencyTable(),
                    new TemperatureField(),
                    materials,
                    worldSeed: 1234,
                    gameTimeTicks: 56,
                    playerStateBlob: ReadOnlyMemory<byte>.Empty,
                    isFrameBoundary: true),
                EmptyWorldStateSnapshotSource.Instance,
                savePath);

            DemoStartupOptions options = DemoStartupOptions.Parse(["--content", temp]);
            EngineProject project = DemoProgram.BuildProject(options);
            // Assert：验证预期结果
            Assert.Equal(1, project.Scenes.Length);
            SceneDescriptor scene = project.Scenes[0];
            Assert.Equal("mine", project.StartScene);
            Assert.Equal(SceneSourceKind.SaveDirectory, scene.SourceKind);
            Assert.Equal(Path.GetFullPath(savePath), Path.GetFullPath(scene.Source!));

            using Engine engine = DemoProgram.BuildEngine(options, project);
            engine.Context.RegisterService(materials);
            WorldLoadResult? result = engine.AttachCurrentSceneWorld(particleCapacity: 4);

            Assert.True(result.HasValue);
            Assert.Equal(1234UL, result.Value.WorldSeed);
            Assert.Equal(56L, result.Value.GameTimeTicks);
            Assert.Equal(1, engine.Context.GetService<CellGrid>().GetMaterial(0, 0));
            Assert.Equal(56L, engine.Context.Clock.FrameIndex);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 Demo 已下线内嵌编辑器入口，编辑器只能通过独立 Shell 进程启动。
    /// </summary>
    [Fact]
    public void EditorFlagIsNoLongerAcceptedByDemo()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => DemoStartupOptions.Parse(["--editor"]));

        Assert.Contains("未知 Demo 参数：--editor", exception.Message, StringComparison.Ordinal);
    }

    private sealed class EmptyWorldStateSnapshotSource : IWorldStateSnapshotSource
    {
        public static readonly EmptyWorldStateSnapshotSource Instance = new();

        public int FreeParticleCount => 0;

        public int RigidBodyCount => 0;

        public void CopyFreeParticles(Span<FreeParticleSnapshot> destination)
        {
        }

        public void CopyRigidBodies(Span<RigidBodySnapshot> destination)
        {
        }
    }

    /// <summary>
    /// 验证默认程序化入口装配可向负坐标延伸的无限自然地形，并保持安全出生区与确定性地貌。
    /// </summary>
    [Fact]
    public void DefaultPlayableWorldBuildsDeterministicInfiniteNaturalTerrain()
    {
        string contentRoot = Path.Combine(FindRepositoryRoot(), "demo", "PixelEngine.Demo", "content");
        string worldRoot = Path.Combine(Path.GetTempPath(), "PixelEngine.Demo.InfiniteTerrain", Guid.NewGuid().ToString("N"));
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
            Assert.False(engine.Context.Options.EnableGameUi);
            PlayableCavernWorldGenerator generator = new();
            engine.RegisterStreamingProceduralWorldGenerator(PlayableCavernWorldGenerator.Key, generator);
            EngineContentPackage package = engine.LoadContentPackage();
            Assert.True(package.MaterialCount > 0);

            WorldLoadResult? worldLoad = engine.AttachCurrentSceneWorld(proceduralWorldRoot: worldRoot);
            Assert.Null(worldLoad);
            Assert.True(engine.IsSimulationWorldAttached);

            MaterialTable materials = engine.Context.GetService<MaterialTable>();
            CellGrid grid = engine.Context.GetService<CellGrid>();
            WorldManager world = engine.Context.GetService<WorldManager>();
            ProceduralWorldDescriptor descriptor = generator.Describe(default);
            Assert.Equal(ProceduralWorldExtent.Infinite, descriptor.Extent);
            Assert.Equal(PlayableCavernWorldGenerator.PersistenceKey, descriptor.PersistenceKey);
            Assert.True(world.Chunks.TryGetChunk(new ChunkCoord(-1, 0), out _));
            Assert.True(world.Chunks.TryGetChunk(new ChunkCoord(0, 0), out _));

            Assert.True(materials.TryGetId("empty", out ushort empty));
            Assert.True(materials.TryGetId("stone", out ushort stone));
            Assert.True(materials.TryGetId("dirt", out ushort dirt));
            Assert.True(materials.TryGetId("sand", out ushort sand));
            Assert.Equal(empty, grid.MaterialAt(0, PlayableCavernWorldGenerator.SafeSurfaceY - 1));
            Assert.Contains(
                grid.MaterialAt(0, PlayableCavernWorldGenerator.SafeSurfaceY),
                new[] { stone, dirt, sand });
            Assert.False(PlayableCavernWorldGenerator.IsCaveAt(
                0,
                PlayableCavernWorldGenerator.SafeSurfaceY + 48,
                PlayableCavernWorldGenerator.SafeSurfaceY));

            int minimumSurface = int.MaxValue;
            int maximumSurface = int.MinValue;
            int caveSamples = 0;
            for (long x = -32_768; x <= 32_768; x += 64)
            {
                int surface = PlayableCavernWorldGenerator.SurfaceYAt(x);
                Assert.Equal(surface, PlayableCavernWorldGenerator.SurfaceYAt(x));
                minimumSurface = Math.Min(minimumSurface, surface);
                maximumSurface = Math.Max(maximumSurface, surface);
                for (long y = surface + 24; y <= surface + 280; y += 16)
                {
                    caveSamples += PlayableCavernWorldGenerator.IsCaveAt(x, y, surface) ? 1 : 0;
                }
            }

            Assert.True(minimumSurface < 145, $"应出现高山地表，minY={minimumSurface}");
            Assert.True(maximumSurface > 265, $"应出现深盆地地表，maxY={maximumSurface}");
            Assert.True(maximumSurface - minimumSurface > 130, $"地貌高差不足，min={minimumSurface}, max={maximumSurface}");
            Assert.True(caveSamples > 40, $"应在深层形成可辨识洞穴，samples={caveSamples}");
        }
        finally
        {
            if (Directory.Exists(worldRoot))
            {
                Directory.Delete(worldRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证默认无限沙盒跨正负远距离流送时内存有界，且原点修改经卸载 / 重入持久保留。
    /// </summary>
    [Fact]
    public void InfiniteSandboxStreamsBothDirectionsWithinBudgetAndPersistsEdits()
    {
        string contentRoot = Path.Combine(FindRepositoryRoot(), "demo", "PixelEngine.Demo", "content");
        string worldRoot = Path.Combine(Path.GetTempPath(), "PixelEngine.Demo.StreamingPersistence", Guid.NewGuid().ToString("N"));
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
            _ = engine.LoadContentPackage();
            int chunkBytes = ChunkMemoryBudget.EstimatedResidentChunkBytes;
            long capBytes = chunkBytes * 200L;
            _ = engine.AttachCurrentSceneWorld(
                streamingConfig: new WorldStreamingConfig
                {
                    ActivationMarginChunks = 1,
                    BorderRingWidth = 1,
                    ResidentMemoryCapBytes = capBytes,
                    EvictionTargetBytes = chunkBytes * 192L,
                    MaxStreamOpsPerFrame = 512,
                },
                proceduralWorldRoot: worldRoot);

            WorldManager world = engine.Context.GetService<WorldManager>();
            CellGrid grid = engine.Context.GetService<CellGrid>();
            IMaterialQuery materials = engine.Context.GetService<IMaterialQuery>();
            ISimulationEditApi edit = engine.Context.GetService<ISimulationEditApi>();
            int editedY = PlayableCavernWorldGenerator.SafeSurfaceY;
            Assert.NotEqual(materials.Resolve("empty").Value, grid.MaterialAt(0, editedY));
            edit.PaintCell(0, editedY, materials.Resolve("empty").Value);

            long frame = 1;
            long[] focusX = [-8_192, 8_192, 0];
            foreach (long focus in focusX)
            {
                world.UpdateCamera(focus, PlayableCavernWorldGenerator.SafeSurfaceY - 16);
                for (int iteration = 0; iteration < 4; iteration++)
                {
                    world.ApplyResidency(frame++);
                    _ = world.Streamer.ProcessIoOnce(engine.Context.Jobs);
                }

                world.ApplyResidency(frame++);
                Assert.InRange(world.MemoryBudget.ResidentBytes, 0, capBytes);
                Assert.True(world.Chunks.Contains(world.Camera.FocusChunk));
            }

            Assert.Equal(materials.Resolve("empty").Value, grid.MaterialAt(0, editedY));
            Assert.InRange(world.MemoryBudget.ResidentBytes, 0, capBytes);
        }
        finally
        {
            if (Directory.Exists(worldRoot))
            {
                Directory.Delete(worldRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 Demo 内容包通过引擎公开加载路径提供 crystal 采集目标材质与可玩性 / 视觉字段。
    /// </summary>
    [Fact]
    public void DemoContentMaterialsExposeCrystalGameplayAndLegendFields()
    {
        // Arrange：准备输入与初始状态
        string contentRoot = Path.Combine(FindRepositoryRoot(), "demo", "PixelEngine.Demo", "content");
        DemoStartupOptions options = DemoStartupOptions.Parse([
            "--headless",
            "--no-hot-reload",
            "--content",
            contentRoot,
        ]);
        EngineProject project = DemoProgram.BuildProject(options);
        using Engine engine = DemoProgram.BuildEngine(options, project);

        _ = engine.LoadContentPackage();

        MaterialTable materials = engine.Context.GetService<MaterialTable>();
        // Assert：验证预期结果
        Assert.True(materials.TryGetId("crystal", out ushort crystalId));
        ref readonly MaterialDef crystal = ref materials.Get(crystalId);
        Assert.Equal(CellType.Solid, crystal.Type);
        Assert.Equal(1, crystal.MineYield);
        Assert.Equal(MaterialRenderStyle.Destructible, crystal.RenderStyle);
        Assert.Equal(MaterialLegendCategory.Resource, crystal.LegendCategory);
        Assert.Equal("Crystal", crystal.DisplayName);
        Assert.True(crystal.LegendVisible);
        Assert.True(crystal.Integrity > 0);
        Assert.Equal(materials.GetIdOrFallback("gravel", 0), crystal.DestroyedTarget);
        Assert.True(crystal.DebrisCount > 0);
    }

    /// <summary>
    /// 验证材质纯色回退与内容纹理的物理语义一致，避免水、冰、木材等在 Editor/无纹理路径中串色。
    /// </summary>
    [Fact]
    public void DemoContentMaterialFallbackPaletteMatchesMaterialSemantics()
    {
        string contentRoot = Path.Combine(FindRepositoryRoot(), "demo", "PixelEngine.Demo", "content");
        DemoStartupOptions options = DemoStartupOptions.Parse([
            "--headless",
            "--no-hot-reload",
            "--content",
            contentRoot,
        ]);
        EngineProject project = DemoProgram.BuildProject(options);
        using Engine engine = DemoProgram.BuildEngine(options, project);
        _ = engine.LoadContentPackage();
        MaterialTable materials = engine.Context.GetService<MaterialTable>();
        (string Name, uint BaseColorBgra)[] expected =
        [
            ("sand", 0xFF_DD_C5_7B),
            ("dirt", 0xFF_64_42_2A),
            ("ash", 0xFF_60_60_5C),
            ("water", 0xFF_31_70_BE),
            ("oil", 0xFF_28_20_32),
            ("acid", 0xFF_64_DE_35),
            ("lava", 0xFF_CF_59_13),
            ("molten_metal", 0xFF_C7_7C_38),
            ("steam", 0xFF_D9_E0_E5),
            ("smoke", 0xFF_4F_4F_53),
            ("acid_gas", 0xFF_7C_C9_5E),
            ("fire", 0xFF_FF_95_2C),
            ("stone", 0xFF_63_63_69),
            ("wood", 0xFF_72_44_1F),
            ("ice", 0xFF_AF_E0_F5),
            ("metal", 0xFF_96_9C_A0),
            ("glass", 0xFF_A8_D6_E4),
            ("gravel", 0xFF_63_63_69),
            ("crystal", 0xFF_69_E0_F2),
        ];

        foreach ((string name, uint baseColorBgra) in expected)
        {
            Assert.True(materials.TryGetId(name, out ushort id), $"Demo 缺少材质 {name}。");
            Assert.Equal(baseColorBgra, materials.Get(id).BaseColorBGRA);
        }

        uint water = materials.Get(materials.GetIdOrFallback("water", 0)).BaseColorBGRA;
        Assert.True(Blue(water) > Green(water) && Blue(water) > Red(water));
        uint acid = materials.Get(materials.GetIdOrFallback("acid", 0)).BaseColorBGRA;
        Assert.True(Green(acid) > Red(acid) && Green(acid) > Blue(acid));

        static byte Red(uint bgra)
        {
            return (byte)(bgra >> 16);
        }

        static byte Green(uint bgra)
        {
            return (byte)(bgra >> 8);
        }

        static byte Blue(uint bgra)
        {
            return (byte)bgra;
        }
    }

    /// <summary>
    /// 验证真实 Demo materials.json 的抗性数值会驱动 DamageCircle 差异化破坏。
    /// </summary>
    [Fact]
    public void DemoContentMaterialsDriveStructuralDamageResistance()
    {
        // Arrange：准备输入与初始状态
        string contentRoot = Path.Combine(FindRepositoryRoot(), "demo", "PixelEngine.Demo", "content");
        DemoStartupOptions options = DemoStartupOptions.Parse([
            "--headless",
            "--no-hot-reload",
            "--content",
            contentRoot,
        ]);
        EngineProject project = DemoProgram.BuildProject(options);
        using Engine engine = DemoProgram.BuildEngine(options, project);
        _ = engine.LoadContentPackage();
        MaterialTable materials = engine.Context.GetService<MaterialTable>();
        // Assert：验证预期结果
        Assert.True(materials.TryGetId("sand", out ushort sand));
        Assert.True(materials.TryGetId("dirt", out ushort dirt));
        Assert.True(materials.TryGetId("stone", out ushort stone));
        Assert.True(materials.TryGetId("metal", out ushort metal));
        Assert.True(materials.TryGetId("gravel", out ushort gravel));
        Assert.True(materials.TryGetId("boundary_stone", out ushort boundaryStone));
        ResidentChunkMap chunks = new();
        Chunk chunk = new(new ChunkCoord(0, 0));
        chunks.Add(chunk);
        SimulationKernel kernel = new(chunks, new MaterialPropsTable(materials.Hot));
        SetLocal(chunk, 20, 20, sand);
        SetLocal(chunk, 21, 20, dirt);
        SetLocal(chunk, 22, 20, stone);
        SetLocal(chunk, 23, 20, metal);
        SetLocal(chunk, 24, 20, boundaryStone);

        int destroyed = kernel.DamageCircle(22, 20, radius: 2, damage: 120, falloff: false);

        Assert.Equal(2, destroyed);
        Assert.Equal(0, GetLocal(chunk, 20, 20));
        Assert.Equal(0, GetLocal(chunk, 21, 20));
        Assert.Equal(stone, GetLocal(chunk, 22, 20));
        Assert.Equal(0, GetDamage(chunk, 22, 20));
        Assert.Equal(metal, GetLocal(chunk, 23, 20));
        Assert.Equal(0, GetDamage(chunk, 23, 20));
        Assert.Equal(boundaryStone, GetLocal(chunk, 24, 20));

        Assert.Equal(0, kernel.DamageCircle(22, 20, radius: 0, damage: 300, falloff: false));
        Assert.Equal(stone, GetLocal(chunk, 22, 20));
        Assert.True(GetDamage(chunk, 22, 20) > 0);

        Assert.Equal(1, kernel.DamageCircle(22, 20, radius: 0, damage: 300, falloff: false));
        Assert.Equal(gravel, GetLocal(chunk, 22, 20));
        Assert.Equal(0, GetDamage(chunk, 22, 20));

        Assert.Equal(0, kernel.DamageCircle(23, 20, radius: 0, damage: 300, falloff: false));
        Assert.Equal(metal, GetLocal(chunk, 23, 20));
        Assert.True(GetDamage(chunk, 23, 20) > 0);

        Assert.Equal(1, kernel.DamageCircle(23, 20, radius: 0, damage: 511, falloff: false));
        Assert.Equal(gravel, GetLocal(chunk, 23, 20));
        Assert.Equal(0, GetDamage(chunk, 23, 20));

        Assert.Equal(0, kernel.DamageCircle(24, 20, radius: 0, damage: 511, falloff: false));
        Assert.Equal(boundaryStone, GetLocal(chunk, 24, 20));
        Assert.Equal(0, GetDamage(chunk, 24, 20));
    }

    /// <summary>
    /// 验证武器目录经 Engine Content/Config API 加载，Demo 不直接解析 JSON。
    /// </summary>
    [Fact]
    public void WeaponCatalogLoadsThroughEngineConfigApi()
    {
        // Arrange：准备输入与初始状态
        string contentRoot = Path.Combine(FindRepositoryRoot(), "demo", "PixelEngine.Demo", "content");
        using Engine engine = new EngineBuilder()
            .UseHeadless()
            .WithContentRoot(contentRoot)
            .Build();

        _ = engine.LoadContentPackage();
        MaterialTable materials = engine.Context.GetService<MaterialTable>();
        WeaponCatalog catalog = WeaponCatalog.Parse(engine.ReadConfigText("weapons.json"));

        // Assert：验证预期结果
        Assert.Equal(6, catalog.Weapons.Length);
        Assert.Equal(
            [WeaponKind.SingleShot, WeaponKind.Laser, WeaponKind.Grenade, WeaponKind.Bomb, WeaponKind.Excavator, WeaponKind.Builder],
            catalog.Weapons.Select(weapon => weapon.Kind).ToArray());
        Assert.All(catalog.Weapons, weapon =>
        {
            Assert.False(string.IsNullOrWhiteSpace(weapon.Id));
            Assert.False(string.IsNullOrWhiteSpace(weapon.DisplayName));
            Assert.StartsWith("#FF", weapon.HudColor, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(weapon.MuzzleCue));
            Assert.False(string.IsNullOrWhiteSpace(weapon.ImpactCue));
            Assert.True(weapon.Radius >= 0);
            Assert.InRange(weapon.Range, 32f, 2_048f);
            Assert.True(weapon.CooldownSeconds >= 0f);
            Assert.True(weapon.AmmoMax > 0);
        });
        WeaponDefinition laser = Assert.Single(catalog.Weapons, weapon => weapon.Kind == WeaponKind.Laser);
        Assert.Equal(WeaponFalloff.None, laser.Falloff);
        Assert.True(laser.BeamDps > 0f);
        Assert.True(laser.HeatPerCell > 0f);
        WeaponDefinition pistol = Assert.Single(catalog.Weapons, weapon => weapon.Kind == WeaponKind.SingleShot);
        Assert.True(pistol.Range > 180f, "一号枪射程应覆盖当前可见战斗区域，而不是沿用旧 180-cell 固定上限。");
        WeaponDefinition grenade = Assert.Single(catalog.Weapons, weapon => weapon.Kind == WeaponKind.Grenade);
        Assert.True(grenade.FuseSeconds > 0f);
        Assert.True(grenade.Impulse > 0f);
        Assert.True(grenade.Radius >= 12);
        Assert.True(grenade.Damage >= 2_500f);
        WeaponDefinition bomb = Assert.Single(catalog.Weapons, weapon => weapon.Kind == WeaponKind.Bomb);
        Assert.Equal(WeaponFalloff.Quadratic, bomb.Falloff);
        Assert.True(bomb.Impulse > 0f);
        WeaponDefinition excavator = Assert.Single(catalog.Weapons, weapon => weapon.Kind == WeaponKind.Excavator);
        Assert.Equal(WeaponFalloff.None, excavator.Falloff);
        Assert.True(excavator.Radius > 0);
        WeaponDefinition builder = Assert.Single(catalog.Weapons, weapon => weapon.Kind == WeaponKind.Builder);
        Assert.Equal("stone", builder.SpawnMaterial);
        Assert.True(materials.TryGetId(builder.SpawnMaterial, out _));
    }

    /// <summary>
    /// 验证武器目录缺失或 JSON 非法时由 Engine Config API 给出明确诊断，而不是静默使用空目录。
    /// </summary>
    [Fact]
    public void WeaponCatalogConfigApiReportsMissingAndInvalidFiles()
    {
        // Arrange：准备输入与初始状态
        string contentRoot = Path.Combine(Path.GetTempPath(), "PixelEngine.WeaponCatalogLoadTests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(contentRoot);
        try
        {
            using Engine engine = new EngineBuilder()
                .UseHeadless()
                .WithContentRoot(contentRoot)
                .Build();

            // Assert：验证预期结果
            FileNotFoundException missing = Assert.Throws<FileNotFoundException>(
                () => WeaponCatalog.Parse(engine.ReadConfigText("weapons.json")));
            Assert.Contains("weapons.json", missing.FileName, StringComparison.OrdinalIgnoreCase);

            File.WriteAllText(Path.Combine(contentRoot, "weapons.json"), "{ \"weapons\": [");
            System.Text.Json.JsonException invalid = Assert.ThrowsAny<System.Text.Json.JsonException>(
                () => WeaponCatalog.Parse(engine.ReadConfigText("weapons.json")));
            Assert.False(string.IsNullOrWhiteSpace(invalid.Message));

            File.WriteAllText(Path.Combine(contentRoot, "weapons.json"), /*lang=json,strict*/ "{ \"weapons\": [{ \"id\": \"bad\", \"displayName\": \"Bad\", \"kind\": \"wrongKind\" }] }");
            InvalidDataException invalidKind = Assert.Throws<InvalidDataException>(
                () => WeaponCatalog.Parse(engine.ReadConfigText("weapons.json")));
            Assert.False(string.IsNullOrWhiteSpace(invalidKind.Message));

            File.WriteAllText(Path.Combine(contentRoot, "weapons.json"), /*lang=json,strict*/ "{ \"weapons\": [{ \"displayName\": \"Broken\", \"kind\": \"builder\", \"ammoMax\": 1 }] }");
            InvalidDataException missingField = Assert.Throws<InvalidDataException>(
                () => WeaponCatalog.Parse(engine.ReadConfigText("weapons.json")));
            Assert.Contains("id", missingField.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
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

        throw new InvalidOperationException("无法从测试输出目录定位 PixelEngine.sln。");
    }

    private static void SetLocal(Chunk chunk, int x, int y, ushort material)
    {
        chunk.MaterialBuffer[CellAddressing.LocalIndexFromLocal(x, y)] = material;
    }

    private static ushort GetLocal(Chunk chunk, int x, int y)
    {
        return chunk.MaterialBuffer[CellAddressing.LocalIndexFromLocal(x, y)];
    }

    private static byte GetDamage(Chunk chunk, int x, int y)
    {
        return chunk.DamageBuffer[CellAddressing.LocalIndexFromLocal(x, y)];
    }

    /// <summary>
    /// 验证默认 Demo 使用玩家友好的窗口尺寸，并把内部渲染画布固定为 720x480。
    /// </summary>
    [Fact]
    public void DefaultEngineUsesPlayableWindowSize()
    {
        string contentRoot = Path.Combine(FindRepositoryRoot(), "demo", "PixelEngine.Demo", "content");
        DemoStartupOptions options = DemoStartupOptions.Parse(["--no-hot-reload", "--content", contentRoot]);
        EngineProject project = DemoProgram.BuildProject(options);
        using Engine engine = DemoProgram.BuildEngine(options, project);

        Assert.Equal(1080, engine.Context.Options.WindowWidth);
        Assert.Equal(720, engine.Context.Options.WindowHeight);
        Assert.Equal(720, engine.Context.Options.InternalWidth);
        Assert.Equal(480, engine.Context.Options.InternalHeight);
        Assert.True(engine.Context.Options.EnableGameUi);
        Assert.Equal(UiBackendKind.RmlUi, engine.Context.Options.GameUiBackend);
        Assert.Equal(1000.0 / 30.0, engine.Context.Options.Overload.FrameBudgetMs, precision: 3);
        Assert.Equal(30, engine.Context.Options.Overload.SustainWindow);
    }

    /// <summary>
    /// 验证窗口 VSync 默认开启，并可通过 --no-vsync 显式关闭供性能 baseline 使用。
    /// </summary>
    [Fact]
    public void WindowVSyncDefaultsOnAndCanBeDisabledForProfiling()
    {
        DemoStartupOptions defaultOptions = DemoStartupOptions.Parse(["--window-ticks", "1"]);
        DemoStartupOptions noVSync = DemoStartupOptions.Parse(["--window-ticks", "1", "--no-vsync"]);
        EngineProject project = DemoProgram.BuildProject(noVSync);
        using Engine engine = DemoProgram.BuildEngine(noVSync, project);

        Assert.True(defaultOptions.VSync);
        Assert.False(noVSync.VSync);
        Assert.False(engine.Context.Options.VSync);
    }

    /// <summary>
    /// 验证发行冒烟参数显式进入 headless、禁用 hot reload，并只执行一个 tick。
    /// </summary>
    [Fact]
    public void SmokeOptionSelectsHeadlessSingleTickWithoutHotReload()
    {
        DemoStartupOptions options = DemoStartupOptions.Parse(["--smoke"]);

        Assert.True(options.Headless);
        Assert.False(options.HotReloadEnabled);
        Assert.Equal(1, options.HeadlessTicks);
    }

    /// <summary>
    /// 验证发行包中真实程序集位于 app/ 时，默认 content 目录仍指向玩家可见的包根 content/。
    /// </summary>
    [Fact]
    public void DefaultContentRootPrefersPackageRootContentWhenBaseDirectoryIsApp()
    {
        string packageRoot = Path.Combine(Path.GetTempPath(), "pixelengine-content-root-" + Guid.NewGuid().ToString("N"));
        try
        {
            string app = Path.Combine(packageRoot, "app");
            string content = Path.Combine(packageRoot, "content");
            _ = Directory.CreateDirectory(app);
            _ = Directory.CreateDirectory(content);

            Assert.Equal(content, DemoStartupOptions.ResolveDefaultContentRoot(app + Path.DirectorySeparatorChar));
        }
        finally
        {
            if (Directory.Exists(packageRoot))
            {
                Directory.Delete(packageRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证窗口短跑参数保持真实窗口模式，但允许测试/发行脚本在固定 tick 后退出。
    /// </summary>
    [Fact]
    public void WindowTicksSelectsFiniteWindowedRuntime()
    {
        DemoStartupOptions options = DemoStartupOptions.Parse(["--no-hot-reload", "--window-ticks", "60", "--capture-frame", "artifacts/demo.bmp"]);

        Assert.False(options.Headless);
        Assert.False(options.HotReloadEnabled);
        Assert.Equal(60, options.WindowTicks);
        Assert.Equal("artifacts/demo.bmp", options.CaptureFramePath);
    }

    /// <summary>
    /// 验证物理 UI 输入观察器只能绑定有限窗口短跑，且不会在普通产品启动中隐式开启。
    /// </summary>
    [Fact]
    public void PhysicalUiInputProbeRequiresFiniteWindowTicks()
    {
        string readyFile = Path.GetFullPath("artifacts/physical-ui-ready.flag");
        DemoStartupOptions options = DemoStartupOptions.Parse(
            [
                "--window-ticks", "1200",
                "--physical-ui-input-probe",
                "--physical-ui-input-ready-file", readyFile,
            ]);

        Assert.True(options.PhysicalUiInputProbe);
        Assert.Equal(1200, options.WindowTicks);
        Assert.Equal(readyFile, options.PhysicalUiInputReadyFile);
        Assert.False(DemoStartupOptions.Parse([]).PhysicalUiInputProbe);
        _ = Assert.Throws<ArgumentException>(() => DemoStartupOptions.Parse(["--physical-ui-input-probe"]));
        _ = Assert.Throws<ArgumentException>(() =>
            DemoStartupOptions.Parse(["--headless", "--physical-ui-input-probe"]));
        _ = Assert.Throws<ArgumentException>(() =>
            DemoStartupOptions.Parse(["--physical-ui-input-ready-file", readyFile]));
    }

    /// <summary>
    /// 验证脚本化窗口 Demo 只能绑定有限窗口短跑，避免伪装成 headless 验收。
    /// </summary>
    [Fact]
    public void ScriptedWindowDemoRequiresFiniteWindowTicks()
    {
        DemoStartupOptions options = DemoStartupOptions.Parse(["--no-hot-reload", "--window-ticks", "60", "--scripted-window-demo"]);

        Assert.True(options.ScriptedWindowDemo);
        Assert.False(options.ScriptedWindowRoute);
        Assert.Equal(60, options.WindowTicks);
        _ = Assert.Throws<ArgumentException>(() => DemoStartupOptions.Parse(["--scripted-window-demo"]));
    }

    /// <summary>
    /// 验证完整路线窗口探针复用真实窗口短跑约束，并显式进入 route 输入脚本。
    /// </summary>
    [Fact]
    public void ScriptedWindowRouteEnablesRouteInputOnlyForFiniteWindowRuns()
    {
        DemoStartupOptions options = DemoStartupOptions.Parse(["--no-hot-reload", "--window-ticks", "600", "--scripted-window-route"]);

        Assert.True(options.ScriptedWindowDemo);
        Assert.True(options.ScriptedWindowRoute);
        Assert.Equal(600, options.WindowTicks);
        _ = Assert.Throws<ArgumentException>(() => DemoStartupOptions.Parse(["--scripted-window-route"]));
    }

    /// <summary>
    /// 验证窗口态可显式请求 CPU/GPU 粒子渲染模式，作为真实窗口粒子帧时间 probe 的前置开关。
    /// </summary>
    [Fact]
    public void ParticleRenderModeOptionIsWindowOnlyAndParsesCpuGpu()
    {
        DemoStartupOptions cpu = DemoStartupOptions.Parse(["--window-ticks", "1", "--particle-render-mode", "cpu"]);
        DemoStartupOptions gpu = DemoStartupOptions.Parse(["--window-ticks", "1", "--particle-render-mode", "gpu"]);

        Assert.Equal(ParticleRenderMode.CpuStamp, cpu.ParticleRenderMode);
        Assert.Equal(ParticleRenderMode.GpuPointSprite, gpu.ParticleRenderMode);
        _ = Assert.Throws<ArgumentException>(() => DemoStartupOptions.Parse(["--headless", "--particle-render-mode", "gpu"]));
        _ = Assert.Throws<ArgumentException>(() => DemoStartupOptions.Parse(["--window-ticks", "1", "--particle-render-mode", "bad"]));
    }

    /// <summary>
    /// 验证高密度粒子帧时间探针只能绑定有限窗口短跑，并解析粒子数与预热帧。
    /// </summary>
    [Fact]
    public void ParticleFrameProbeRequiresFiniteWindowTicksAndParsesParameters()
    {
        DemoStartupOptions options = DemoStartupOptions.Parse([
            "--window-ticks", "12",
            "--particle-frame-probe",
            "--particle-count", "100000",
            "--particle-probe-warmup", "3",
            "--particle-probe-run-id", "run-123",
        ]);

        Assert.True(options.ParticleFrameProbe);
        Assert.Equal(100_000, options.ParticleProbeCount);
        Assert.Equal(3, options.ParticleProbeWarmupFrames);
        Assert.Equal("run-123", options.ParticleProbeRunId);
        _ = Assert.Throws<ArgumentException>(() => DemoStartupOptions.Parse(["--particle-frame-probe"]));
        _ = Assert.Throws<ArgumentException>(() => DemoStartupOptions.Parse(["--headless", "--particle-frame-probe"]));
        _ = Assert.Throws<ArgumentException>(() => DemoStartupOptions.Parse(["--window-ticks", "1", "--particle-frame-probe", "--particle-probe-run-id", " "]));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => DemoStartupOptions.Parse(["--window-ticks", "1", "--particle-frame-probe", "--particle-count", "262145"]));
    }

    /// <summary>
    /// 验证窗口短跑不能和 headless 冒烟混用，避免调用方误以为覆盖了窗口路径。
    /// </summary>
    [Fact]
    public void WindowTicksRejectsHeadlessRuntime()
    {
        _ = Assert.Throws<ArgumentException>(() => DemoStartupOptions.Parse(["--headless", "--window-ticks", "1"]));
    }

    /// <summary>
    /// 验证 NativeAOT 等不支持动态代码的运行时会显式禁用脚本热重载，而不是尝试走 Roslyn/ALC 路径。
    /// </summary>
    [Fact]
    public void HotReloadRequiresDynamicCodeSupport()
    {
        DemoStartupOptions options = DemoStartupOptions.Parse([]);

        Assert.True(DemoProgram.CanEnableHotReload(options, dynamicCodeSupported: true));
        Assert.False(DemoProgram.CanEnableHotReload(options, dynamicCodeSupported: false));
        Assert.False(DemoProgram.CanEnableHotReload(DemoStartupOptions.Parse(["--no-hot-reload"]), dynamicCodeSupported: true));
    }

    /// <summary>
    /// 验证玩家包 content/scripts 中的 Behaviour 会在 scene 物化前注册。
    /// </summary>
    [Fact]
    public void RegisterPackagedScriptAssembliesLoadsContentScriptsBeforeSceneMaterialization()
    {
        string contentRoot = Path.Combine(Path.GetTempPath(), "pixelengine-packaged-scripts-" + Guid.NewGuid().ToString("N"), "content");
        try
        {
            string scripts = Path.Combine(contentRoot, "scripts");
            _ = Directory.CreateDirectory(scripts);
            string scenes = Path.Combine(contentRoot, "scenes");
            _ = Directory.CreateDirectory(scenes);
            File.WriteAllText(Path.Combine(contentRoot, "startup.json"), /*lang=json,strict*/ """
                {
                  "startScene": "scenes/main.scene"
                }
                """);
            File.WriteAllText(Path.Combine(scenes, "main.scene"), /*lang=json,strict*/ """
                {
                  "formatVersion": 2,
                  "name": "main",
                  "entities": [
                    {
                      "stableId": 1,
                      "name": "Probe",
                      "transform": { "x": 0, "y": 0, "rotationRadians": 0, "scaleX": 1, "scaleY": 1 },
                      "behaviours": [
                        { "typeName": "PackagedDemoBehaviour" }
                      ]
                    }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(scripts, "PackagedDemoBehaviour.cs"), """
                using PixelEngine.Scripting;

                public sealed class PackagedDemoBehaviour : Behaviour
                {
                }
                """);
            DemoStartupOptions options = DemoStartupOptions.Parse(["--content", contentRoot]);
            EngineProject project = DemoProgram.BuildProject(options);
            using Engine engine = DemoProgram.BuildEngine(options, project);

            DemoProgram.RegisterPackagedScriptAssemblies(engine, options, dynamicCodeSupported: true);

            ScriptAssemblyRegistry registry = engine.Context.GetService<ScriptAssemblyRegistry>();
            Assert.Contains(registry.Assemblies, assembly => assembly.GetType("PackagedDemoBehaviour", throwOnError: false) is not null);

            DemoProgram.AttachMinimalSmokeWorld(engine);
            ScriptSimulationContext scriptContext = engine.AttachScriptingFromServices();
            ScriptEntityInspection entity = Assert.Single(scriptContext.Scene.CaptureInspectionSnapshot());
            ScriptComponentInspection behaviour = Assert.Single(entity.Components);
            Assert.Equal("PackagedDemoBehaviour", behaviour.TypeName);
        }
        finally
        {
            string? root = Directory.GetParent(contentRoot)?.FullName;
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 Demo Player 注册自身程序集后仍会加载外部工程随包分发的 Behaviour。
    /// </summary>
    [Fact]
    public void RegisterPackagedScriptAssembliesLoadsExternalProjectScriptsAlongsideDemoAssembly()
    {
        string contentRoot = Path.Combine(Path.GetTempPath(), "pixelengine-static-scripts-" + Guid.NewGuid().ToString("N"), "content");
        try
        {
            string scripts = Path.Combine(contentRoot, "scripts");
            _ = Directory.CreateDirectory(scripts);
            File.WriteAllText(Path.Combine(scripts, "ExternalProjectBehaviour.cs"), """
                using PixelEngine.Scripting;

                public sealed class ExternalProjectBehaviour : Behaviour
                {
                }
                """);
            DemoStartupOptions options = DemoStartupOptions.Parse(["--content", contentRoot]);
            using Engine engine = new EngineBuilder()
                .UseHeadless()
                .WithContentRoot(contentRoot)
                .Build();
            engine.RegisterScriptAssembly(typeof(DemoProgram).Assembly);

            DemoProgram.RegisterPackagedScriptAssemblies(
                engine,
                options,
                dynamicCodeSupported: true);

            ScriptAssemblyRegistry registry = engine.Context.GetService<ScriptAssemblyRegistry>();
            Assert.Contains(registry.Assemblies, assembly =>
                assembly.GetType("ExternalProjectBehaviour", throwOnError: false) is not null);
        }
        finally
        {
            string? root = Directory.GetParent(contentRoot)?.FullName;
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证空工程玩家包缺少 materials/reactions 时仍会接入最小可渲染 world，窗口 Probe 不会半初始化闪退。
    /// </summary>
    [Fact]
    public void MinimalSmokeWorldRegistersSimulationForContentlessWindowProbe()
    {
        string contentRoot = Path.Combine(Path.GetTempPath(), "pixelengine-contentless-probe-" + Guid.NewGuid().ToString("N"), "content");
        try
        {
            _ = Directory.CreateDirectory(contentRoot);
            DemoStartupOptions options = DemoStartupOptions.Parse(["--content", contentRoot]);
            EngineProject project = DemoProgram.BuildProject(options);
            using Engine engine = DemoProgram.BuildEngine(options, project);

            DemoProgram.AttachMinimalSmokeWorld(engine);
            engine.RegisterScriptAssembly(typeof(DemoProgram).Assembly);
            ScriptSimulationContext scriptContext = engine.AttachScriptingFromServices();

            MaterialTable materials = engine.Context.GetService<MaterialTable>();
            Assert.True(materials.TryGetId("empty", out ushort empty));
            Assert.Equal(0, empty);
            Assert.True(engine.Context.TryGetService(out SimulationPhaseDriver _));
            Assert.True(engine.Context.TryGetService(out CellGrid _));
            Assert.Same(scriptContext, engine.Context.GetService<ScriptSimulationContext>());
        }
        finally
        {
            string? root = Directory.GetParent(contentRoot)?.FullName;
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
