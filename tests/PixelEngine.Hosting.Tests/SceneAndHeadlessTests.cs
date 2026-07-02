using PixelEngine.Serialization;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using PixelEngine.Scripting;
using PixelEngine.World;
using Xunit;
using PhysicsSystem = PixelEngine.Physics.PhysicsSystem;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 场景、项目模型与 headless 驱动测试。
/// </summary>
public sealed class SceneAndHeadlessTests
{
    private const ushort Empty = 0;
    private const ushort Fire = 1;
    private const ushort Wood = 2;
    private const ushort Stone = 3;
    private const ushort Ash = 4;

    /// <summary>
    /// 验证 EngineBuilder 能从项目模型注册场景并切到起始场景。
    /// </summary>
    [Fact]
    public void BuildLoadsProjectScenesAndStartScene()
    {
        EngineProject project = new(
            "game-content",
            "start",
            [
                new SceneDescriptor("start", SceneSourceKind.Procedural, "terrain-a"),
                new SceneDescriptor("menu"),
            ]);

        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .WithProject(project)
            .Build();

        ISceneService scenes = engine.Context.GetService<ISceneService>();

        Assert.Equal("game-content", engine.Context.Options.ContentRoot);
        Assert.Equal("start", engine.Context.Options.StartScene);
        Assert.True(engine.Context.IsServiceAvailable(EngineServiceRole.SceneService));
        Assert.NotNull(scenes.Current);
        Assert.Equal("start", scenes.Current.Name);
        Assert.Equal("terrain-a", scenes.Current.ResolvedSource);
        Assert.True(scenes.Current.WorldConstructionPending);
        Assert.True(scenes.TryGet("menu", out SceneDescriptor menu));
        Assert.Equal(SceneSourceKind.Empty, menu.SourceKind);
    }

    /// <summary>
    /// 验证手动场景切换和卸载。
    /// </summary>
    [Fact]
    public void SceneServiceSwitchesAndUnloadsCurrentScene()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .AddScene(new SceneDescriptor("a"))
            .AddScene(new SceneDescriptor("b", SceneSourceKind.SaveDirectory, "saves/b"))
            .AddScene(new SceneDescriptor("c", SceneSourceKind.SceneFile, "scenes/c.scene"))
            .Build();
        ISceneService scenes = engine.Context.GetService<ISceneService>();

        Scene loaded = engine.LoadScene("b");

        Assert.Same(loaded, scenes.Current);
        Assert.Equal(SceneSourceKind.SaveDirectory, loaded.Descriptor.SourceKind);
        Assert.Equal("saves/b", loaded.Descriptor.Source);
        Assert.Equal(Path.GetFullPath(Path.Combine(engine.Context.Options.ContentRoot, "saves/b")), loaded.ResolvedSource);
        Assert.True(loaded.WorldConstructionPending);

        Scene sceneFile = scenes.SwitchTo("c");
        Assert.Equal(SceneSourceKind.SceneFile, sceneFile.Descriptor.SourceKind);
        Assert.Equal(Path.GetFullPath(Path.Combine(engine.Context.Options.ContentRoot, "scenes/c.scene")), sceneFile.ResolvedSource);
        Assert.True(sceneFile.WorldConstructionPending);

        scenes.UnloadCurrent();
        Assert.Null(scenes.Current);
        _ = Assert.Throws<InvalidOperationException>(() => scenes.SwitchTo("missing"));
    }

    /// <summary>
    /// 验证 Hosting 装配 Simulation world 时会把已加载 ReactionTable 接入 CA 主循环。
    /// </summary>
    [Fact]
    public void ResidentSimulationWorldRunsLoadedReactionTable()
    {
        MaterialDef[] definitions = CreateReactionMaterials();
        MaterialTable materials = new(definitions);
        ReactionTable reactions = new(
            [
                new Reaction
                {
                    InputA = Fire,
                    InputB = Wood,
                    OutputA = Stone,
                    OutputB = Ash,
                    Probability = byte.MaxValue,
                    Flags = ReactionFlags.Fast,
                },
                new Reaction
                {
                    InputA = Wood,
                    InputB = Fire,
                    OutputA = Ash,
                    OutputB = Stone,
                    Probability = byte.MaxValue,
                    Flags = ReactionFlags.Fast,
                },
            ],
            definitions);

        using Engine engine = new EngineBuilder()
            .UseHeadless()
            .WithWorkerCount(1)
            .Build();
        engine.Context.RegisterService(materials);
        engine.Context.RegisterService(reactions);
        SimulationPhaseDriver simulation = engine.AttachResidentSimulationWorld(128, 128);
        simulation.Kernel.EditCellAtInputPhase(10, 10, Fire, persistentFlags: 0);
        simulation.Kernel.EditCellAtInputPhase(11, 10, Wood, persistentFlags: 0);

        _ = engine.RunOneTick(1.0 / 60.0);

        Assert.Equal(Stone, simulation.Grid.GetMaterial(10, 10));
        Assert.Equal(Ash, simulation.Grid.GetMaterial(11, 10));
        Assert.True(engine.Context.GetService<ReactionEngine>() is not null);
    }

    /// <summary>
    /// 验证 Engine.LoadScene 会从 .scene 文件物化脚本实体与 Behaviour 参数。
    /// </summary>
    [Fact]
    public void LoadSceneFileInstantiatesScriptBehaviours()
    {
        string contentRoot = Path.Combine(Path.GetTempPath(), $"pixelengine-scene-file-{Guid.NewGuid():N}");
        try
        {
            string sceneDirectory = Path.Combine(contentRoot, "scenes");
            _ = Directory.CreateDirectory(sceneDirectory);
            string scenePath = Path.Combine(sceneDirectory, "c.scene");
            string behaviourType = typeof(SceneFileTestBehaviour).FullName!;
            File.WriteAllText(
                scenePath,
                $$"""
                {
                  "formatVersion": 1,
                  "name": "c",
                  "entities": [
                    {
                      "stableId": 7,
                      "name": "player",
                      "behaviours": [
                        {
                          "typeName": "{{behaviourType}}",
                          "serializedFields": {
                            "Label": "hero",
                            "Health": "42",
                            "Enabled": "false"
                          }
                        }
                      ]
                    }
                  ]
                }
                """);
            using Engine engine = new EngineBuilder()
                .WithWorkerCount(1)
                .WithContentRoot(contentRoot)
                .AddScene(new SceneDescriptor("c", SceneSourceKind.SceneFile, "scenes/c.scene"))
                .Build();
            engine.RegisterScriptAssembly(typeof(SceneFileTestBehaviour).Assembly);

            Scene loaded = engine.LoadScene("c");

            Assert.NotNull(loaded.ScriptScene);
            Assert.Equal(1, loaded.ScriptScene.EntityCount);
            ScriptEntityInspection[] snapshot = loaded.ScriptScene.CaptureInspectionSnapshot();
            SceneFileTestBehaviour behaviour = Assert.IsType<SceneFileTestBehaviour>(snapshot[0].Components[0].Behaviour);
            Assert.Equal("hero", behaviour.Label);
            Assert.Equal(42, behaviour.Health);
            Assert.False(behaviour.Enabled);
            Assert.Same(loaded.ScriptScene, engine.Context.GetService<PixelEngine.Scripting.Scene>());
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 procedural scene source 会在脚本程序集注册后物化入口 Behaviour。
    /// </summary>
    [Fact]
    public void RegisterScriptAssemblyMaterializesCurrentProceduralScene()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .AddScene(new SceneDescriptor("proc", SceneSourceKind.Procedural, nameof(ProceduralEntryBehaviour)))
            .WithStartScene("proc")
            .Build();

        engine.RegisterScriptAssembly(typeof(ProceduralEntryBehaviour).Assembly);

        Scene? current = engine.Context.GetService<ISceneService>().Current;
        Assert.NotNull(current);
        Assert.NotNull(current.ScriptScene);
        Assert.Equal(1, current.ScriptScene.EntityCount);
        ScriptEntityInspection[] snapshot = current.ScriptScene.CaptureInspectionSnapshot();
        _ = Assert.IsType<ProceduralEntryBehaviour>(snapshot[0].Components[0].Behaviour);
        Assert.Same(current.ScriptScene, engine.Context.GetService<PixelEngine.Scripting.Scene>());
    }

    /// <summary>
    /// 验证手动 LoadScene 也会物化 procedural Behaviour。
    /// </summary>
    [Fact]
    public void LoadSceneMaterializesProceduralBehaviour()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .AddScene(new SceneDescriptor("proc", SceneSourceKind.Procedural, typeof(ProceduralEntryBehaviour).FullName!))
            .Build();
        engine.RegisterScriptAssembly(typeof(ProceduralEntryBehaviour).Assembly);

        Scene loaded = engine.LoadScene("proc");

        Assert.NotNull(loaded.ScriptScene);
        ScriptEntityInspection[] snapshot = loaded.ScriptScene.CaptureInspectionSnapshot();
        _ = Assert.IsType<ProceduralEntryBehaviour>(snapshot[0].Components[0].Behaviour);
    }

    /// <summary>
    /// 验证 procedural scene source 可通过注册生成器构建真实 resident Simulation world。
    /// </summary>
    [Fact]
    public void AttachCurrentSceneWorldBuildsRegisteredProceduralWorld()
    {
        MaterialTable materials = Materials(("empty", CellType.Empty), ("stone", CellType.Solid));
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .AddScene(new SceneDescriptor("proc-world", SceneSourceKind.Procedural, "test-world"))
            .WithStartScene("proc-world")
            .Build();
        engine.Context.RegisterService(materials);
        engine.RegisterProceduralWorldGenerator("test-world", new TestProceduralWorldGenerator());

        WorldLoadResult? result = engine.AttachCurrentSceneWorld(particleCapacity: 8);

        Assert.Null(result);
        Assert.Equal(77L, engine.Context.Clock.FrameIndex);
        Assert.Equal(77L, engine.Context.Clock.SimTickIndex);
        SimulationKernel kernel = engine.Context.GetService<SimulationKernel>();
        Assert.Equal(42UL, kernel.WorldSeed);
        Assert.Equal(77u, kernel.FrameIndex);
        CellGrid grid = engine.Context.GetService<CellGrid>();
        Assert.Equal(1, grid.GetMaterial(4, 5));
        Assert.Equal(32.5f, engine.Context.GetService<TemperatureField>().GetTemperature(4, 5));
        Assert.Equal(1, engine.Phases.Count(EnginePhase.CaSimulation));
        Assert.True(engine.Context.IsServiceAvailable(EngineServiceRole.WorldAccess));
    }

    /// <summary>
    /// 验证 Hosting 能从 save directory 物化 live World/Simulation 后端，并恢复自由粒子快照。
    /// </summary>
    [Fact]
    public void AttachWorldFromSaveDirectoryLoadsWorldAndRegistersRuntimeServices()
    {
        string savePath = Path.Combine(Path.GetTempPath(), $"pixelengine-host-save-{Guid.NewGuid():N}");
        try
        {
            MaterialTable materials = Materials(("empty", CellType.Empty), ("sand", CellType.Powder));
            ResidentChunkMap savedChunks = new();
            ResidencyTable savedResidency = new();
            TemperatureField savedTemperature = new();
            ChunkCoord coord = new(0, 0);
            Chunk chunk = new(coord);
            chunk.Material[0] = 1;
            chunk.Lifetime[0] = 9;
            savedChunks.Add(chunk);
            savedTemperature.AddHeat(0, 0, 24.5f);
            FakeWorldStateBridge savedState = new(
                [new FreeParticleSnapshot(2, 3, 0.5f, -0.25f, 1, 7, 8)],
                []);
            new WorldSaveService().SaveAll(
                new WorldSaveContext(
                    savedChunks,
                    savedResidency,
                    savedTemperature,
                    materials,
                    worldSeed: 123,
                    gameTimeTicks: 456,
                    playerStateBlob: ReadOnlyMemory<byte>.Empty,
                    isFrameBoundary: true),
                savedState,
                savePath);

            using Engine engine = new EngineBuilder()
                .WithWorkerCount(1)
                .AddScene(new SceneDescriptor("save", SceneSourceKind.SaveDirectory, savePath))
                .WithStartScene("save")
                .Build();
            engine.Context.RegisterService(materials);

            Scene current = engine.Context.GetService<ISceneService>().Current!;
            WorldLoadResult result = engine.AttachWorldFromSaveDirectory(current.ResolvedSource!, particleCapacity: 4);

            Assert.Equal(123UL, result.WorldSeed);
            Assert.Equal(456L, result.GameTimeTicks);
            Assert.Equal(1, result.LoadedChunkCount);
            Assert.Equal(456L, engine.Context.Clock.FrameIndex);
            Assert.Equal(456L, engine.Context.Clock.SimTickIndex);
            CellGrid grid = engine.Context.GetService<CellGrid>();
            Assert.Equal(1, grid.GetMaterial(0, 0));
            Assert.Equal(9, grid.LifetimeAt(0, 0));
            Assert.Equal(24.5f, engine.Context.GetService<TemperatureField>().GetTemperature(0, 0));
            ParticleSystem particles = engine.Context.GetService<ParticleSystem>();
            Assert.Equal(1, particles.ActiveCount);
            Assert.Equal((ushort)1, particles.ActiveReadOnly[0].Material);
            WorldManager world = engine.Context.GetService<WorldManager>();
            Assert.Same(world.Chunks, engine.Context.GetService<ResidentChunkMap>());
            Assert.Equal(1, engine.Phases.Count(EnginePhase.ResidencyApply));
            Assert.Equal(1, engine.Phases.Count(EnginePhase.WorldStreaming));
            Assert.Equal(1, engine.Phases.Count(EnginePhase.CaSimulation));
            SimulationKernel kernel = engine.Context.GetService<SimulationKernel>();
            Assert.Equal(123UL, kernel.WorldSeed);
            Assert.Equal(456u, kernel.FrameIndex);
        }
        finally
        {
            if (Directory.Exists(savePath))
            {
                Directory.Delete(savePath, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证含刚体快照的 save directory 会自动接入 Physics 并恢复刚体。
    /// </summary>
    [Fact]
    public void AttachWorldFromSaveDirectoryRestoresRigidBodySnapshotsThroughPhysics()
    {
        string savePath = Path.Combine(Path.GetTempPath(), $"pixelengine-host-rigidbody-save-{Guid.NewGuid():N}");
        try
        {
            MaterialTable materials = Materials(("empty", CellType.Empty), ("stone", CellType.Solid));
            ResidentChunkMap savedChunks = new();
            Chunk chunk = new(new ChunkCoord(0, 0));
            savedChunks.Add(chunk);
            byte[] mask = new byte[256];
            ushort[] bodyMaterials = new ushort[256];
            Array.Fill(mask, (byte)1);
            Array.Fill(bodyMaterials, (ushort)1);
            FakeWorldStateBridge savedState = new(
                [],
                [
                    new RigidBodySnapshot(
                        id: 1,
                        width: 16,
                        height: 16,
                        bodyLocalMask: mask,
                        material: bodyMaterials,
                        posX: 16,
                        posY: 16,
                        rotCos: 1,
                        rotSin: 0,
                        linVelX: 0,
                        linVelY: 0,
                        angVel: 0,
                        localOriginX: 8,
                        localOriginY: 8),
                ]);
            new WorldSaveService().SaveAll(
                new WorldSaveContext(
                    savedChunks,
                    new ResidencyTable(),
                    new TemperatureField(),
                    materials,
                    worldSeed: 1,
                    gameTimeTicks: 2,
                    playerStateBlob: ReadOnlyMemory<byte>.Empty,
                    isFrameBoundary: true),
                savedState,
                savePath);

            using Engine engine = new EngineBuilder()
                .WithWorkerCount(1)
                .Build();
            engine.Context.RegisterService(materials);

            WorldLoadResult result = engine.AttachWorldFromSaveDirectory(savePath);

            Assert.Equal(1, result.LoadedChunkCount);
            Assert.True(engine.Context.IsServiceAvailable(EngineServiceRole.PhysicsService));
            PhysicsSystem physics = engine.Context.GetService<PhysicsSystem>();
            Assert.Equal(1, physics.PhysicsWorld.ActiveBodyCount);
            Assert.Equal(1, engine.Phases.Count(EnginePhase.PhysicsSync));
            Assert.True(CellFlags.Has(engine.Context.GetService<CellGrid>().FlagsAt(16, 16), CellFlags.RigidOwned));
            Assert.Equal(1, engine.Context.GetService<CellGrid>().GetMaterial(16, 16));
        }
        finally
        {
            if (Directory.Exists(savePath))
            {
                Directory.Delete(savePath, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 .scene 的 InitialSaveDirectory 会按 .scene 文件目录解析，并显式装配当前场景世界。
    /// </summary>
    [Fact]
    public void AttachCurrentSceneWorldLoadsSceneFileInitialSaveDirectory()
    {
        string contentRoot = Path.Combine(Path.GetTempPath(), $"pixelengine-scene-world-{Guid.NewGuid():N}");
        try
        {
            string sceneDirectory = Path.Combine(contentRoot, "scenes");
            string savePath = Path.Combine(contentRoot, "saves", "mine");
            _ = Directory.CreateDirectory(sceneDirectory);
            MaterialTable materials = Materials(("empty", CellType.Empty), ("sand", CellType.Powder));
            ResidentChunkMap savedChunks = new();
            Chunk chunk = new(new ChunkCoord(0, 0));
            chunk.Material[0] = 1;
            savedChunks.Add(chunk);
            new WorldSaveService().SaveAll(
                new WorldSaveContext(
                    savedChunks,
                    new ResidencyTable(),
                    new TemperatureField(),
                    materials,
                    worldSeed: 9,
                    gameTimeTicks: 10,
                    playerStateBlob: ReadOnlyMemory<byte>.Empty,
                    isFrameBoundary: true),
                new FakeWorldStateBridge([], []),
                savePath);
            string scenePath = Path.Combine(sceneDirectory, "mine.scene");
            File.WriteAllText(
                scenePath,
                """
                {
                  "formatVersion": 1,
                  "name": "mine",
                  "initialSaveDirectory": "../saves/mine",
                  "entities": []
                }
                """);
            using Engine engine = new EngineBuilder()
                .WithWorkerCount(1)
                .WithContentRoot(contentRoot)
                .AddScene(new SceneDescriptor("mine", SceneSourceKind.SceneFile, "scenes/mine.scene"))
                .WithStartScene("mine")
                .Build();
            engine.Context.RegisterService(materials);

            WorldLoadResult? result = engine.AttachCurrentSceneWorld(particleCapacity: 4);

            Assert.True(result.HasValue);
            Assert.Equal(9UL, result.Value.WorldSeed);
            Assert.Equal(10L, result.Value.GameTimeTicks);
            Assert.Equal(1, engine.Context.GetService<CellGrid>().GetMaterial(0, 0));
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证场景来源配置会快速拒绝无效组合。
    /// </summary>
    [Fact]
    public void SceneDescriptorRejectsInvalidSourceConfiguration()
    {
        _ = Assert.Throws<ArgumentException>(() => new SceneDescriptor("empty", SceneSourceKind.Empty, "unexpected"));
        _ = Assert.Throws<ArgumentException>(() => new SceneDescriptor("save", SceneSourceKind.SaveDirectory));
        _ = Assert.Throws<ArgumentException>(() => new SceneDescriptor("scene", SceneSourceKind.SceneFile));
        _ = Assert.Throws<ArgumentException>(() => new SceneDescriptor("proc", SceneSourceKind.Procedural));
    }

    /// <summary>
    /// 验证 headless 模式可以按固定步数驱动，且非 headless 禁止使用该入口。
    /// </summary>
    [Fact]
    public void RunHeadlessTicksAdvancesFixedNumberOfFrames()
    {
        using Engine headless = new EngineBuilder()
            .UseHeadless()
            .UseDeterministicMode()
            .Build();

        headless.RunHeadlessTicks(5);

        Assert.Equal(5, headless.Context.Clock.FrameIndex);
        Assert.Equal(5, headless.Context.Clock.SimTickIndex);
        Assert.False(headless.Context.Options.EnableGpu);

        using Engine windowed = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();
        _ = Assert.Throws<InvalidOperationException>(() => windowed.RunHeadlessTicks(1));
    }

    /// <summary>
    /// .scene 加载测试用 Behaviour。
    /// </summary>
    public sealed class SceneFileTestBehaviour : Behaviour
    {
        /// <summary>
        /// 测试字符串字段。
        /// </summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// 测试数值字段。
        /// </summary>
        public int Health { get; set; }
    }

    /// <summary>
    /// procedural scene 测试入口 Behaviour。
    /// </summary>
    public sealed class ProceduralEntryBehaviour : Behaviour
    {
    }

    private sealed class TestProceduralWorldGenerator : IProceduralWorldGenerator
    {
        public ProceduralWorldDescriptor Describe(in ProceduralWorldBuildRequest request)
        {
            Assert.Equal("test-world", request.Key);
            Assert.True(request.Materials.TryResolve("stone", out MaterialId stone));
            Assert.Equal(1, stone.Value);
            return new ProceduralWorldDescriptor(128, 96, WorldSeed: 42, FrameIndex: 77);
        }

        public void Populate(in ProceduralWorldBuildContext context)
        {
            Assert.Equal("test-world", context.Key);
            Assert.Equal(128, context.WidthCells);
            Assert.Equal(96, context.HeightCells);
            MaterialId stone = context.Materials.Resolve("stone");
            context.Edit.PaintCell(4, 5, stone.Value);
            context.Edit.SetTemperature(4, 5, 32.5f);
        }
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
                TextureId = -1,
                MeltPoint = float.NaN,
                FreezePoint = float.NaN,
                BoilPoint = float.NaN,
            };
        }

        return new MaterialTable(materials);
    }

    private static MaterialDef[] CreateReactionMaterials()
    {
        return
        [
            Material(Empty, "empty", CellType.Empty, reactionStart: 0, reactionCount: 0),
            Material(Fire, "fire", CellType.Fire, reactionStart: 0, reactionCount: 1),
            Material(Wood, "wood", CellType.Solid, reactionStart: 1, reactionCount: 1),
            Material(Stone, "stone", CellType.Solid, reactionStart: 0, reactionCount: 0),
            Material(Ash, "ash", CellType.Solid, reactionStart: 0, reactionCount: 0),
        ];
    }

    private static MaterialDef Material(ushort id, string name, CellType type, int reactionStart, byte reactionCount)
    {
        return new MaterialDef
        {
            Id = id,
            Name = name,
            Type = type,
            Density = id == 0 ? (byte)0 : (byte)100,
            ReactionStart = reactionStart,
            ReactionCount = reactionCount,
            DefaultLifetime = type == CellType.Gas ? (ushort)120 : (ushort)0,
            HeatCapacity = 1,
            TextureId = -1,
            MeltPoint = float.NaN,
            FreezePoint = float.NaN,
            BoilPoint = float.NaN,
        };
    }

    private sealed class FakeWorldStateBridge(
        FreeParticleSnapshot[] particles,
        RigidBodySnapshot[] bodies) : IWorldStateSnapshotSource
    {
        public int FreeParticleCount => particles.Length;

        public int RigidBodyCount => bodies.Length;

        public void CopyFreeParticles(Span<FreeParticleSnapshot> destination)
        {
            particles.CopyTo(destination);
        }

        public void CopyRigidBodies(Span<RigidBodySnapshot> destination)
        {
            bodies.CopyTo(destination);
        }
    }
}
