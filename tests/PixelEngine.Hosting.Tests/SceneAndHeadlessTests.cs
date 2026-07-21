using PixelEngine.Editor.Shell;
using PixelEngine.Serialization;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using PixelEngine.Scripting;
using PixelEngine.World;
using System.Numerics;
using Xunit;
using PhysicsSystem = PixelEngine.Physics.PhysicsSystem;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 场景服务、项目模型与 headless 引擎驱动测试。
/// 不变式：场景切换/卸载生命周期正确、无窗口 tick 可推进 CA/物理/脚本相位且反应表已接入。
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
        // Arrange：准备输入与初始状态
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

        // Assert：验证预期结果
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
        // Arrange：搭建测试场景与依赖
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .AddScene(new SceneDescriptor("a"))
            .AddScene(new SceneDescriptor("b", SceneSourceKind.SaveDirectory, "saves/b"))
            .AddScene(new SceneDescriptor("c", SceneSourceKind.SceneFile, "scenes/c.scene"))
            .Build();
        ISceneService scenes = engine.Context.GetService<ISceneService>();

        // Act：执行被测操作
        Scene loaded = engine.LoadScene("b");

        // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
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

        // Act：执行被测操作
        _ = engine.RunOneTick(1.0 / 60.0);

        // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
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

            // Act：执行被测操作
            Scene loaded = engine.LoadScene("c");

            // Assert：验证不变式与预期结果
            Assert.NotNull(loaded.ScriptScene);
            Assert.Equal(1, loaded.ScriptScene.EntityCount);
            ScriptEntityInspection[] snapshot = loaded.ScriptScene.CaptureInspectionSnapshot();
            SceneFileTestBehaviour behaviour = Assert.IsType<SceneFileTestBehaviour>(snapshot[0].Components[0].Behaviour);
            Assert.Equal("hero", behaviour.Label);
            Assert.Equal(42, behaviour.Health);
            Assert.Equal(Vector2.Zero, behaviour.Position);
            Assert.False(behaviour.Enabled);
            Assert.Same(loaded.ScriptScene, engine.Context.GetService<Scripting.Scene>());
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
    /// 验证旧 .scene 缺失 GameObject enabled 字段时仍按启用读取，而不是被 JSON bool 默认值误判为禁用。
    /// </summary>
    [Fact]
    public void LegacySceneWithoutGameObjectEnabledDefaultsToActive()
    {
        string contentRoot = Path.Combine(Path.GetTempPath(), $"pixelengine-scene-enabled-{Guid.NewGuid():N}");
        try
        {
            string scenePath = Path.Combine(contentRoot, "scenes", "legacy.scene");
            _ = Directory.CreateDirectory(Path.GetDirectoryName(scenePath)!);
            File.WriteAllText(
                scenePath,
                $$"""
                {
                  "formatVersion": 2,
                  "name": "legacy",
                  "entities": [
                    {
                      "stableId": 1,
                      "name": "active-by-default",
                      "behaviours": [
                        { "typeName": "{{typeof(SceneFileTestBehaviour).FullName}}" }
                      ]
                    }
                  ]
                }
                """);
            EngineSceneDocument document = EngineSceneDocumentLoader.LoadDocument(scenePath);
            Assert.Null(Assert.Single(document.Entities!).Enabled);

            ScriptAssemblyRegistry scripts = new();
            scripts.Register(typeof(SceneFileTestBehaviour).Assembly);
            Scripting.Scene runtime = EngineSceneDocumentLoader.Build(document, scripts);
            SceneFileTestBehaviour behaviour = Assert.IsType<SceneFileTestBehaviour>(
                Assert.Single(Assert.Single(runtime.CaptureInspectionSnapshot()).Components).Behaviour);
            Assert.True(behaviour.Enabled);

            EditorSceneModel editor = EditorSceneModel.FromDocument(document);
            Assert.True(editor.Get(1).Enabled);
            Assert.True(Assert.Single(editor.ToDocument().Entities!).Enabled!.Value);
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
    /// 验证外部编辑态物化的脚本 Scene 可接入当前 Hosting 场景，并被脚本运行时复用。
    /// </summary>
    [Fact]
    public void AttachScriptSceneOverridesSceneFileMaterialization()
    {
        // Arrange：准备输入与初始状态
        string contentRoot = Path.Combine(Path.GetTempPath(), $"pixelengine-authoring-scene-{Guid.NewGuid():N}");
        try
        {
            string sceneDirectory = Path.Combine(contentRoot, "scenes");
            _ = Directory.CreateDirectory(sceneDirectory);
            File.WriteAllText(
                Path.Combine(sceneDirectory, "authoring.scene"),
                                     /*lang=json,strict*/
                                     """
                {
                  "formatVersion": 2,
                  "name": "authoring",
                  "entities": []
                }
                """);
            using Engine engine = new EngineBuilder()
                .WithWorkerCount(1)
                .WithContentRoot(contentRoot)
                .AddScene(new SceneDescriptor("authoring", SceneSourceKind.SceneFile, "scenes/authoring.scene"))
                .WithStartScene("authoring")
                .Build();
            Scripting.Scene authoringScene = new();
            Entity entity = authoringScene.CreateEntity();
            Transform transform = entity.AddComponent<Transform>();
            transform.SetPosition(12, 34);

            engine.Context.RegisterService(Materials(("empty", CellType.Empty), ("stone", CellType.Solid)));
            _ = engine.AttachResidentSimulationWorld(64, 64, particleCapacity: 8);
            engine.AttachScriptScene(authoringScene);
            ScriptSimulationContext context = engine.AttachScriptingFromServices();

            Scene current = engine.Context.GetService<ISceneService>().Current!;
            // Assert：验证预期结果
            Assert.Same(authoringScene, current.ScriptScene);
            Assert.Same(authoringScene, engine.Context.GetService<Scripting.Scene>());
            Assert.Same(authoringScene, context.Scene);
            ScriptEntityInspection snapshot = Assert.Single(authoringScene.CaptureInspectionSnapshot());
            Assert.Equal(12, snapshot.Transform!.X);
            Assert.Equal(34, snapshot.Transform!.Y);
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
    /// 验证编辑态 authoring projection 刷新可在脚本 runtime 已接入后替换当前脚本 Scene。
    /// </summary>
    [Fact]
    public void AttachScriptSceneReplacesAuthoringProjectionAfterScriptingRuntimeAttached()
    {
        // Arrange：准备输入与初始状态
        MaterialTable materials = Materials(("empty", CellType.Empty), ("stone", CellType.Solid));
        Scripting.Scene first = new();
        first.CreateEntity().AddComponent<SnapshotCounterBehaviour>().Score = 1;
        Scripting.Scene replacement = new();
        replacement.CreateEntity().AddComponent<SnapshotCounterBehaviour>().Score = 2;
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .AddScene(new SceneDescriptor("authoring"))
            .WithStartScene("authoring")
            .Build();
        engine.Context.RegisterService(materials);
        _ = engine.AttachResidentSimulationWorld(64, 64, particleCapacity: 8);
        engine.AttachScriptScene(first);
        ScriptSimulationContext context = engine.AttachScriptingFromServices();

        engine.AttachScriptScene(replacement);

        Scene current = engine.Context.GetService<ISceneService>().Current!;
        // Assert：验证预期结果
        Assert.Same(replacement, current.ScriptScene);
        Assert.Same(replacement, engine.Context.GetService<Scripting.Scene>());
        Assert.Same(replacement, context.Scene);
        ScriptEntityInspection snapshot = Assert.Single(context.Scene.CaptureInspectionSnapshot());
        ScriptComponentInspection component = Assert.Single(snapshot.Components);
        SnapshotCounterBehaviour script = Assert.IsType<SnapshotCounterBehaviour>(component.Behaviour);
        Assert.Equal(2, script.Score);
    }

    /// <summary>
    /// 验证 Hosting 可稳定写出 .scene v3，并在读回时保持实体排序与字段排序。
    /// </summary>
    [Fact]
    public void SaveSceneDocumentWritesStableV3Json()
    {
        // Arrange：准备输入与初始状态
        string contentRoot = Path.Combine(Path.GetTempPath(), $"pixelengine-scene-save-{Guid.NewGuid():N}");
        try
        {
            string scenePath = Path.Combine(contentRoot, "scenes", "saved.scene");
            using Engine engine = new EngineBuilder()
                .UseHeadless()
                .UseDeterministicMode()
                .WithContentRoot(contentRoot)
                .Build();
            EngineSceneDocument document = new()
            {
                FormatVersion = 1,
                Name = "saved",
                Entities =
                [
                    new EngineSceneEntityDocument
                    {
                        StableId = 20,
                        Name = "child",
                        ParentId = 10,
                        Transform = new EngineSceneTransformDocument { X = 2, Y = 3, ScaleX = 2, ScaleY = 2 },
                        Prefab = new EngineScenePrefabDocument
                        {
                            AssetPath = "prefabs/rock.prefab",
                            SourceStableId = "2",
                            Overrides =
                            [
                                new EngineScenePrefabOverrideDocument
                                {
                                    SourceStableId = "2",
                                    PropertyPath = "Transform.X",
                                    Value = "42",
                                },
                            ],
                        },
                        Behaviours =
                        [
                            new EngineSceneBehaviourDocument
                            {
                                TypeName = typeof(SceneFileTestBehaviour).FullName!,
                                SerializedFields = new Dictionary<string, string>
                                {
                                    ["Position"] = "8,9",
                                    ["Label"] = "child",
                                },
                            },
                        ],
                    },
                    new EngineSceneEntityDocument
                    {
                        StableId = 10,
                        Name = "root",
                        Transform = new EngineSceneTransformDocument { X = 5, Y = 7, RotationRadians = 0.5f },
                    },
                ],
            };

            engine.SaveSceneDocument(document, scenePath);

            string json = File.ReadAllText(scenePath);
            // Assert：验证预期结果
            Assert.Contains("\"formatVersion\":3", json, StringComparison.Ordinal);
            Assert.True(json.IndexOf("\"stableId\":10", StringComparison.Ordinal) < json.IndexOf("\"stableId\":20", StringComparison.Ordinal));
            Assert.True(json.IndexOf("\"Label\"", StringComparison.Ordinal) < json.IndexOf("\"Position\"", StringComparison.Ordinal));
            EngineSceneDocument loaded = EngineSceneDocumentLoader.LoadDocument(scenePath);
            Assert.Equal(EngineSceneDocumentLoader.CurrentFormatVersion, loaded.FormatVersion);
            EngineSceneEntityDocument[] loadedEntities = loaded.Entities!;
            Assert.Equal([10, 20], [.. loadedEntities.Select(static entity => entity.StableId)]);
            EngineScenePrefabDocument prefab = loadedEntities[1].Prefab!;
            Assert.Equal("prefabs/rock.prefab", prefab.AssetPath);
            EngineScenePrefabOverrideDocument prefabOverride = Assert.Single(prefab.Overrides!);
            Assert.Equal("Transform.X", prefabOverride.PropertyPath);
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
    /// 验证 .scene v2 保存、读取、再保存后保持 ParentId、Transform、Vector2 字段与稳定排序一致。
    /// </summary>
    [Fact]
    public void SceneDocumentV2RoundTripsParentTransformVector2AndStableOrder()
    {
        // Arrange：准备输入与初始状态
        string contentRoot = Path.Combine(Path.GetTempPath(), $"pixelengine-scene-roundtrip-{Guid.NewGuid():N}");
        try
        {
            string firstPath = Path.Combine(contentRoot, "scenes", "first.scene");
            string secondPath = Path.Combine(contentRoot, "scenes", "second.scene");
            using Engine engine = new EngineBuilder()
                .UseHeadless()
                .UseDeterministicMode()
                .WithContentRoot(contentRoot)
                .Build();
            EngineSceneDocument source = new()
            {
                FormatVersion = 2,
                Name = "roundtrip",
                InitialSaveDirectory = "../saves/checkpoint",
                Entities =
                [
                    new EngineSceneEntityDocument
                    {
                        StableId = 30,
                        Name = "child",
                        ParentId = 20,
                        Enabled = false,
                        Transform = new EngineSceneTransformDocument { X = 3, Y = 4, RotationRadians = 0.25f, ScaleX = 0.5f, ScaleY = 0.75f },
                        Prefab = new EngineScenePrefabDocument
                        {
                            AssetPath = "prefabs/actor.prefab",
                            SourceStableId = "root/child",
                            Overrides =
                            [
                                new EngineScenePrefabOverrideDocument { SourceStableId = "root/child", PropertyPath = "Transform.Y", Value = "4" },
                                new EngineScenePrefabOverrideDocument { SourceStableId = "root/child", PropertyPath = "Component:SceneFileTestBehaviour:Position", Value = "3.5,4.25" },
                            ],
                        },
                        Behaviours =
                        [
                            new EngineSceneBehaviourDocument
                            {
                                TypeName = typeof(SceneFileTestBehaviour).FullName!,
                                SerializedFields = new Dictionary<string, string>
                                {
                                    ["Position"] = "3.5,4.25",
                                    ["Health"] = "77",
                                    ["Label"] = "child",
                                },
                            },
                        ],
                    },
                    new EngineSceneEntityDocument
                    {
                        StableId = 10,
                        Name = "root",
                        Transform = new EngineSceneTransformDocument { X = 10, Y = 20, RotationRadians = 0.5f, ScaleX = 2, ScaleY = 2 },
                    },
                    new EngineSceneEntityDocument
                    {
                        StableId = 20,
                        Name = "middle",
                        ParentId = 10,
                        Transform = new EngineSceneTransformDocument { X = 1, Y = 2, RotationRadians = 0.125f, ScaleX = 1.5f, ScaleY = 1.25f },
                    },
                ],
            };

            engine.SaveSceneDocument(source, firstPath);
            string firstJson = File.ReadAllText(firstPath);
            EngineSceneDocument loaded = EngineSceneDocumentLoader.LoadDocument(firstPath);
            engine.SaveSceneDocument(loaded, secondPath);
            string secondJson = File.ReadAllText(secondPath);
            EngineSceneDocument loadedAgain = EngineSceneDocumentLoader.LoadDocument(secondPath);

            // Assert：验证预期结果
            Assert.Equal(firstJson, secondJson);
            Assert.Equal(EngineSceneDocumentLoader.CurrentFormatVersion, loadedAgain.FormatVersion);
            Assert.Equal("roundtrip", loadedAgain.Name);
            Assert.Equal("../saves/checkpoint", loadedAgain.InitialSaveDirectory);
            Assert.Equal([10, 20, 30], [.. loadedAgain.Entities!.Select(static entity => entity.StableId)]);
            EngineSceneEntityDocument child = EntityByStableId(loadedAgain, 30);
            Assert.Equal(20, child.ParentId);
            Assert.False(child.Enabled!.Value);
            AssertTransform(child.Transform!, x: 3, y: 4, rotation: 0.25f, scaleX: 0.5f, scaleY: 0.75f);
            Assert.Equal(["Health", "Label", "Position"], [.. child.Behaviours![0].SerializedFields!.Keys]);
            Assert.Equal("3.5,4.25", child.Behaviours[0].SerializedFields!["Position"]);
            EngineScenePrefabOverrideDocument[] overrides = child.Prefab!.Overrides!;
            Assert.Equal("Component:SceneFileTestBehaviour:Position", overrides[0].PropertyPath);
            Assert.Equal("Transform.Y", overrides[1].PropertyPath);

            ScriptAssemblyRegistry scripts = new();
            scripts.Register(typeof(SceneFileTestBehaviour).Assembly);
            Scripting.Scene runtimeScene = EngineSceneDocumentLoader.Build(loadedAgain, scripts);
            SceneFileTestBehaviour behaviour = Assert.IsType<SceneFileTestBehaviour>(Assert.Single(runtimeScene.CaptureInspectionSnapshot()[2].Components).Behaviour);
            Assert.False(behaviour.Enabled);
            Assert.Equal(new Vector2(3.5f, 4.25f), behaviour.Position);
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
    /// 验证 v1 旧 .scene 缺失 ParentId/Transform 时可按根实体和单位 TRS 读取，并在另存时升级为 v2。
    /// </summary>
    [Fact]
    public void SceneDocumentV1LoadsRootDefaultsAndSavesAsV2()
    {
        // Arrange：准备输入与初始状态
        string contentRoot = Path.Combine(Path.GetTempPath(), $"pixelengine-scene-v1-upgrade-{Guid.NewGuid():N}");
        try
        {
            string scenePath = Path.Combine(contentRoot, "scenes", "legacy.scene");
            string upgradedPath = Path.Combine(contentRoot, "scenes", "upgraded.scene");
            _ = Directory.CreateDirectory(Path.GetDirectoryName(scenePath)!);
            File.WriteAllText(
                scenePath,
                                     /*lang=json,strict*/
                                     """
                {
                  "formatVersion": 1,
                  "name": "legacy",
                  "entities": [
                    {
                      "stableId": 7,
                      "name": "legacy-root"
                    }
                  ]
                }
                """);
            using Engine engine = new EngineBuilder()
                .UseHeadless()
                .UseDeterministicMode()
                .WithContentRoot(contentRoot)
                .Build();

            EngineSceneDocument loaded = EngineSceneDocumentLoader.LoadDocument(scenePath);
            ScriptAssemblyRegistry scripts = new();
            Scripting.Scene runtimeScene = EngineSceneDocumentLoader.Build(loaded, scripts);
            engine.SaveSceneDocument(loaded, upgradedPath);
            EngineSceneDocument upgraded = EngineSceneDocumentLoader.LoadDocument(upgradedPath);

            // Assert：验证预期结果
            EngineSceneEntityDocument loadedEntity = Assert.Single(loaded.Entities!);
            Assert.Equal(1, loaded.FormatVersion);
            Assert.Null(loadedEntity.ParentId);
            Assert.Null(loadedEntity.Transform);
            Transform runtimeTransform = Assert.Single(runtimeScene.CaptureInspectionSnapshot()).Transform!;
            Assert.Equal(0, runtimeTransform.X);
            Assert.Equal(0, runtimeTransform.Y);
            Assert.Equal(0, runtimeTransform.RotationRadians);
            Assert.Equal(1, runtimeTransform.ScaleX);
            Assert.Equal(1, runtimeTransform.ScaleY);
            EngineSceneEntityDocument upgradedEntity = Assert.Single(upgraded.Entities!);
            Assert.Equal(EngineSceneDocumentLoader.CurrentFormatVersion, upgraded.FormatVersion);
            Assert.Null(upgradedEntity.ParentId);
            AssertTransform(upgradedEntity.Transform!, x: 0, y: 0, rotation: 0, scaleX: 1, scaleY: 1);
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
    /// 验证 .scene v2 会物化 Transform、烘焙父子层级，并支持 Vector2 字段绑定。
    /// </summary>
    [Fact]
    public void SceneDocumentV2BuildsTransformHierarchyAndVector2Fields()
    {
        // Arrange：准备输入与初始状态
        EngineSceneDocument document = new()
        {
            FormatVersion = 2,
            Name = "v2",
            Entities =
            [
                new EngineSceneEntityDocument
                {
                    StableId = 1,
                    Name = "root",
                    Transform = new EngineSceneTransformDocument { X = 10, Y = 20, ScaleX = 2, ScaleY = 3 },
                },
                new EngineSceneEntityDocument
                {
                    StableId = 2,
                    Name = "child",
                    ParentId = 1,
                    Transform = new EngineSceneTransformDocument { X = 5, Y = 6, RotationRadians = 0.25f, ScaleX = 4, ScaleY = 5 },
                    Behaviours =
                    [
                        new EngineSceneBehaviourDocument
                        {
                            TypeName = typeof(SceneFileTestBehaviour).FullName!,
                            SerializedFields = new Dictionary<string, string>
                            {
                                ["Position"] = "3.5,4.25",
                                ["Label"] = "child",
                            },
                        },
                    ],
                },
            ],
        };
        ScriptAssemblyRegistry scripts = new();
        scripts.Register(typeof(SceneFileTestBehaviour).Assembly);

        Scripting.Scene scene = EngineSceneDocumentLoader.Build(document, scripts);

        ScriptEntityInspection[] snapshot = scene.CaptureInspectionSnapshot();
        // Assert：验证预期结果
        Assert.Equal(2, snapshot.Length);
        Assert.Equal(10, snapshot[0].Transform!.X);
        Assert.Equal(20, snapshot[0].Transform!.Y);
        Assert.Equal(20, snapshot[1].Transform!.X);
        Assert.Equal(38, snapshot[1].Transform!.Y);
        Assert.Equal(0.25f, snapshot[1].Transform!.RotationRadians);
        Assert.Equal(8, snapshot[1].Transform!.ScaleX);
        Assert.Equal(15, snapshot[1].Transform!.ScaleY);
        SceneFileTestBehaviour behaviour = Assert.IsType<SceneFileTestBehaviour>(Assert.Single(snapshot[1].Components).Behaviour);
        Assert.Equal("child", behaviour.Label);
        Assert.Equal(new Vector2(3.5f, 4.25f), behaviour.Position);
    }

    /// <summary>
    /// 验证 .scene v2 父级旋转与缩放会按 TRS 烘焙到运行时扁平 Transform。
    /// </summary>
    [Fact]
    public void SceneDocumentV2BakesParentRotationAndScaleIntoRuntimeTransform()
    {
        // Arrange：准备输入与初始状态
        EngineSceneDocument document = new()
        {
            FormatVersion = 2,
            Entities =
            [
                new EngineSceneEntityDocument
                {
                    StableId = 1,
                    Transform = new EngineSceneTransformDocument
                    {
                        X = 10,
                        Y = 20,
                        RotationRadians = MathF.PI / 2f,
                        ScaleX = 2,
                        ScaleY = 3,
                    },
                },
                new EngineSceneEntityDocument
                {
                    StableId = 2,
                    ParentId = 1,
                    Transform = new EngineSceneTransformDocument { X = 5, Y = 6 },
                },
            ],
        };
        ScriptAssemblyRegistry scripts = new();

        Scripting.Scene scene = EngineSceneDocumentLoader.Build(document, scripts);

        Transform child = scene.CaptureInspectionSnapshot()[1].Transform!;
        // Assert：验证预期结果
        Assert.InRange(child.X, -8.001f, -7.999f);
        Assert.InRange(child.Y, 29.999f, 30.001f);
        Assert.Equal(MathF.PI / 2f, child.RotationRadians);
    }

    /// <summary>
    /// 验证 .scene 层级会拒绝重复 id、缺失父级与循环引用。
    /// </summary>
    [Fact]
    public void SceneDocumentV2RejectsInvalidHierarchy()
    {
        // Arrange：准备输入与初始状态
        ScriptAssemblyRegistry scripts = new();

        // Assert：验证预期结果
        _ = Assert.Throws<InvalidOperationException>(() => EngineSceneDocumentLoader.Build(new EngineSceneDocument
        {
            FormatVersion = 2,
            Entities =
            [
                new EngineSceneEntityDocument { StableId = 1 },
                new EngineSceneEntityDocument { StableId = 1 },
            ],
        }, scripts));
        _ = Assert.Throws<InvalidOperationException>(() => EngineSceneDocumentLoader.Build(new EngineSceneDocument
        {
            FormatVersion = 2,
            Entities = [new EngineSceneEntityDocument { StableId = 1, ParentId = 99 }],
        }, scripts));
        _ = Assert.Throws<InvalidOperationException>(() => EngineSceneDocumentLoader.Build(new EngineSceneDocument
        {
            FormatVersion = 2,
            Entities =
            [
                new EngineSceneEntityDocument { StableId = 1, ParentId = 2 },
                new EngineSceneEntityDocument { StableId = 2, ParentId = 1 },
            ],
        }, scripts));
    }

    /// <summary>
    /// 验证 procedural scene source 会在脚本程序集注册后物化入口 Behaviour。
    /// </summary>
    [Fact]
    public void RegisterScriptAssemblyMaterializesCurrentProceduralScene()
    {
        // Arrange：准备输入与初始状态
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .AddScene(new SceneDescriptor("proc", SceneSourceKind.Procedural, nameof(ProceduralEntryBehaviour)))
            .WithStartScene("proc")
            .Build();

        engine.RegisterScriptAssembly(typeof(ProceduralEntryBehaviour).Assembly);

        Scene? current = engine.Context.GetService<ISceneService>().Current;
        // Assert：验证预期结果
        Assert.NotNull(current);
        Assert.NotNull(current.ScriptScene);
        Assert.Equal(1, current.ScriptScene.EntityCount);
        ScriptEntityInspection[] snapshot = current.ScriptScene.CaptureInspectionSnapshot();
        _ = Assert.IsType<ProceduralEntryBehaviour>(snapshot[0].Components[0].Behaviour);
        Assert.Same(current.ScriptScene, engine.Context.GetService<Scripting.Scene>());
    }

    /// <summary>
    /// 验证手动 LoadScene 也会物化 procedural Behaviour。
    /// </summary>
    [Fact]
    public void LoadSceneMaterializesProceduralBehaviour()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .AddScene(new SceneDescriptor("proc", SceneSourceKind.Procedural, typeof(ProceduralEntryBehaviour).FullName))
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
        // Arrange：准备输入与初始状态
        MaterialTable materials = Materials(("empty", CellType.Empty), ("stone", CellType.Solid));
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .AddScene(new SceneDescriptor("proc-world", SceneSourceKind.Procedural, "test-world"))
            .WithStartScene("proc-world")
            .Build();
        engine.Context.RegisterService(materials);
        engine.RegisterProceduralWorldGenerator("test-world", new TestProceduralWorldGenerator());

        WorldLoadResult? result = engine.AttachCurrentSceneWorld(particleCapacity: 8);

        // Assert：验证预期结果
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
    /// 验证流式 procedural scene 会同步生成完整 initial active/border，支持负坐标并接入 WorldManager。
    /// </summary>
    [Fact]
    public void AttachCurrentSceneWorldBuildsStreamingProceduralWorldAcrossNegativeCoordinates()
    {
        string worldRoot = Path.Combine(
            Path.GetTempPath(),
            "PixelEngine.Hosting.StreamingProcedural",
            Guid.NewGuid().ToString("N"));
        try
        {
            MaterialTable materials = Materials(("empty", CellType.Empty), ("stone", CellType.Solid));
            RecordingStreamingWorldGenerator generator = new();
            using Engine engine = new EngineBuilder()
                .WithWorkerCount(2)
                .AddScene(new SceneDescriptor("stream-world", SceneSourceKind.Procedural, "stream-test"))
                .WithStartScene("stream-world")
                .Build();
            engine.Context.RegisterService(materials);
            engine.RegisterStreamingProceduralWorldGenerator("stream-test", generator);

            WorldLoadResult? result = engine.AttachCurrentSceneWorld(
                particleCapacity: 8,
                streamingConfig: new WorldStreamingConfig
                {
                    ActivationMarginChunks = 0,
                    BorderRingWidth = 1,
                    MaxStreamOpsPerFrame = 128,
                },
                proceduralWorldRoot: worldRoot);

            Assert.Null(result);
            Assert.True(engine.IsSimulationWorldAttached);
            WorldManager world = engine.Context.GetService<WorldManager>();
            Assert.Equal(-32, world.Camera.FocusX);
            Assert.Equal(32, world.Camera.FocusY);
            ChunkRect active = world.ComputeVisibleChunks().Expand(world.Config.ActivationMarginChunks);
            Assert.Equal(active.Expand(world.Config.BorderRingWidth).Count, world.Chunks.Count);
            Assert.Equal(0, world.Streamer.PendingRequestCount);
            Assert.Equal(0, world.Streamer.PendingCompletedCount);
            Assert.Contains((-1, 0), generator.GeneratedCoords);
            Assert.Contains((0, 0), generator.GeneratedCoords);
            Assert.Equal(99UL, engine.Context.GetService<SimulationKernel>().WorldSeed);
            Assert.Equal(1, engine.Context.GetService<CellGrid>().GetMaterial(-64, 0));
            Assert.Equal(14f, engine.Context.GetService<TemperatureField>().GetTemperature(-64, 0));
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
    /// 验证公开 runtime API 会以新 seed 原子替换流式 world、脚本基线与瞬态粒子，并切换 seed 隔离存储。
    /// </summary>
    [Fact]
    public void RuntimeRestartCurrentProceduralWorldRebuildsSeededWorldAndScriptBaseline()
    {
        string worldRoot = Path.Combine(
            Path.GetTempPath(),
            "PixelEngine.Hosting.ProceduralRestart",
            Guid.NewGuid().ToString("N"));
        try
        {
            MaterialTable materials = Materials(
                ("empty", CellType.Empty),
                ("stone", CellType.Solid),
                ("metal", CellType.Solid));
            SeedSwitchingStreamingWorldGenerator generator = new();
            Scripting.Scene scriptScene = new();
            SnapshotCounterBehaviour script = scriptScene.CreateEntity().AddComponent<SnapshotCounterBehaviour>();
            RestartableCharacterBehaviour character = scriptScene.CreateEntity().AddComponent<RestartableCharacterBehaviour>();
            DeferredProceduralRestartBehaviour restartRequester = scriptScene.CreateEntity().AddComponent<DeferredProceduralRestartBehaviour>();
            script.Score = 7;
            restartRequester.TargetSeed = 202;
            using Engine engine = new EngineBuilder()
                .WithWorkerCount(2)
                .AddScene(new SceneDescriptor("seeded-world", SceneSourceKind.Procedural, "seed-switch"))
                .WithStartScene("seeded-world")
                .Build();
            engine.Context.RegisterService(materials);
            engine.RegisterStreamingProceduralWorldGenerator("seed-switch", generator);
            _ = engine.AttachCurrentSceneWorld(
                particleCapacity: 8,
                streamingConfig: new WorldStreamingConfig
                {
                    ActivationMarginChunks = 0,
                    BorderRingWidth = 1,
                    MaxStreamOpsPerFrame = 128,
                },
                proceduralWorldRoot: worldRoot);
            engine.AttachScriptScene(scriptScene);
            ScriptSimulationContext context = engine.AttachScriptingFromServices();

            _ = engine.RunOneTick();
            Assert.Equal(101UL, context.Runtime.Capture().WorldSeed);
            Assert.Equal(1, context.Cells.GetMaterial(0, 0).Value);
            Assert.Equal(1, character.StartCount);
            Assert.True(character.HasLiveHandle);
            context.Cells.SetCell(0, 0, new MaterialId(1));
            script.Score = 99;
            ParticleSystem particles = engine.Context.GetService<ParticleSystem>();
            ParticleSpawn spawn = new(0, 0, 0, 0, Material: 1, ColorVariant: 0, Life: 10);
            Assert.True(particles.TrySpawn(in spawn));
            restartRequester.Requested = true;

            _ = engine.RunOneTick();

            Assert.True(restartRequester.WasAccepted);
            Assert.Equal(RuntimeRestartStatus.Pending, restartRequester.StatusWhenAccepted);
            Assert.Equal(RuntimeRestartStatus.Succeeded, context.Runtime.Capture().RestartStatus);
            Assert.Equal(202UL, engine.Context.GetService<SimulationKernel>().WorldSeed);
            Assert.Equal(202UL, context.Runtime.Capture().WorldSeed);
            Assert.Equal(7, script.Score);
            Assert.Equal(0, particles.ActiveCount);
            Assert.Equal(2, context.Cells.GetMaterial(0, 0).Value);
            Assert.Equal(2f, engine.Context.GetService<TemperatureField>().GetTemperature(0, 0));
            Assert.Equal(0, engine.Context.GetService<WorldManager>().Streamer.PendingRequestCount);
            Assert.False(character.HasLiveHandle);

            _ = engine.RunOneTick();
            Assert.Equal(2, context.Cells.GetMaterial(0, 0).Value);
            Assert.Equal(2, character.StartCount);
            Assert.True(character.HasLiveHandle);
            Assert.False(character.Faulted);
            Assert.Equal(0, scriptScene.ScriptExceptionCount);
            Assert.Equal(2, generator.DescribedSeeds.Length);
            Assert.Contains(101UL, generator.DescribedSeeds);
            Assert.Contains(202UL, generator.DescribedSeeds);
            Assert.Contains(202UL, generator.PopulatedSeeds);
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
    /// 验证无限 descriptor 拒绝可逃逸持久化根的键。
    /// </summary>
    [Fact]
    public void InfiniteProceduralDescriptorRejectsUnsafePersistenceKey()
    {
        _ = Assert.Throws<ArgumentException>(() =>
            ProceduralWorldDescriptor.CreateInfinite(1, 0, 0, "../outside"));
    }

    /// <summary>
    /// 验证可编辑 .scene 文件可声明流式程序化世界，同时保留 SceneFile authoring 来源。
    /// </summary>
    [Fact]
    public void SceneFileCanAttachDeclaredStreamingProceduralWorld()
    {
        string temp = Path.Combine(Path.GetTempPath(), "PixelEngine.Hosting.SceneStreaming", Guid.NewGuid().ToString("N"));
        try
        {
            string scenePath = Path.Combine(temp, "content", "scenes", "infinite.scene");
            EngineSceneDocumentLoader.SaveDocument(new EngineSceneDocument
            {
                FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
                Name = "infinite",
                ProceduralWorldGenerator = "auto-stream",
                Entities =
                [
                    new EngineSceneEntityDocument
                    {
                        StableId = 1,
                        Name = "Streaming Director",
                        Behaviours =
                        [
                            new EngineSceneBehaviourDocument
                            {
                                TypeName = typeof(StreamingProceduralEntryBehaviour).FullName,
                            },
                        ],
                    },
                ],
            }, scenePath);
            MaterialTable materials = Materials(("empty", CellType.Empty), ("stone", CellType.Solid));
            using Engine engine = new EngineBuilder()
                .WithWorkerCount(2)
                .WithContentRoot(Path.Combine(temp, "content"))
                .AddScene(new SceneDescriptor("infinite", SceneSourceKind.SceneFile, scenePath))
                .WithStartScene("infinite")
                .Build();
            engine.Context.RegisterService(materials);
            engine.RegisterScriptAssembly(typeof(StreamingProceduralEntryBehaviour).Assembly);

            WorldLoadResult? result = engine.AttachCurrentSceneWorld(
                particleCapacity: 8,
                streamingConfig: new WorldStreamingConfig
                {
                    ActivationMarginChunks = 0,
                    BorderRingWidth = 1,
                    MaxStreamOpsPerFrame = 128,
                },
                proceduralWorldRoot: Path.Combine(temp, "worlds"));

            Assert.Null(result);
            Assert.True(engine.IsSimulationWorldAttached);
            Assert.Equal(SceneSourceKind.SceneFile, engine.CurrentScene!.Descriptor.SourceKind);
            Assert.True(engine.Context.TryGetService(out WorldManager _));
            Assert.Equal(1, engine.Context.GetService<CellGrid>().GetMaterial(-64, 0));
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
    /// 验证 Engine world 快照可恢复 resident chunks、温度与游戏 tick。
    /// </summary>
    [Fact]
    public void EngineWorldSnapshotStoreRestoresResidentWorld()
    {
        // Arrange：准备输入与初始状态
        MaterialTable materials = Materials(("empty", CellType.Empty), ("stone", CellType.Solid));
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();
        engine.Context.RegisterService(materials);
        _ = engine.AttachResidentSimulationWorld(128, 128, particleCapacity: 8);
        ISimulationEditApi edit = engine.Context.GetService<ISimulationEditApi>();
        edit.PaintCell(4, 5, 1);
        edit.SetTemperature(4, 5, 24.5f);
        engine.Context.Clock.RestoreCounters(frameIndex: 9, simTickIndex: 9);
        using EngineWorldSnapshotStore store = new(engine);

        SaveLoadOperationResult save = store.SaveTemporarySnapshot();
        edit.PaintCell(4, 5, 0);
        edit.SetTemperature(4, 5, 80f);
        engine.Context.Clock.RestoreCounters(frameIndex: 13, simTickIndex: 13);
        SaveLoadOperationResult restore = store.RestoreTemporarySnapshot();

        // Assert：验证预期结果
        Assert.True(save.Success, save.Message);
        Assert.True(restore.Success, restore.Message);
        Assert.Equal(1, engine.Context.GetService<CellGrid>().GetMaterial(4, 5));
        Assert.Equal(24.5f, engine.Context.GetService<TemperatureField>().GetTemperature(4, 5));
        Assert.Equal(9L, engine.Context.Clock.FrameIndex);
        Assert.Equal(9L, engine.Context.Clock.SimTickIndex);
    }

    /// <summary>
    /// 验证 Engine 公开持久存读档 API 会恢复 resident world、温度、kernel 帧状态与 FrameClock。
    /// </summary>
    [Fact]
    public void EnginePersistentWorldSaveLoadRestoresRuntimeCounters()
    {
        // Arrange：准备输入与初始状态
        string savePath = Path.Combine(Path.GetTempPath(), $"pixelengine-host-persistent-save-{Guid.NewGuid():N}");
        try
        {
            MaterialTable materials = Materials(("empty", CellType.Empty), ("stone", CellType.Solid));
            using Engine engine = new EngineBuilder()
                .WithWorkerCount(1)
                .Build();
            engine.Context.RegisterService(materials);
            _ = engine.AttachResidentSimulationWorld(128, 128, particleCapacity: 8);
            ISimulationEditApi edit = engine.Context.GetService<ISimulationEditApi>();
            edit.PaintCell(4, 5, 1);
            edit.SetTemperature(4, 5, 24.5f);
            const ulong SavedWorldSeed = 0x1234_5678_9ABC_DEF0UL;
            engine.Context.GetService<SimulationKernel>().RestoreWorldState(
                SavedWorldSeed,
                frameIndex: 11,
                currentParity: CellFlags.Parity);
            engine.Context.Clock.RestoreCounters(frameIndex: 11, simTickIndex: 11);

            _ = engine.SaveWorldToDirectory(savePath);
            edit.PaintCell(4, 5, 0);
            edit.SetTemperature(4, 5, 80f);
            engine.Context.GetService<SimulationKernel>().RestoreWorldState(
                worldSeed: 17,
                frameIndex: 17,
                currentParity: 0);
            engine.Context.Clock.RestoreCounters(frameIndex: 17, simTickIndex: 17);
            WorldLoadResult result = engine.LoadWorldFromDirectory(savePath);

            // Assert：验证预期结果
            Assert.Equal(11L, result.GameTimeTicks);
            Assert.Equal(1, engine.Context.GetService<CellGrid>().GetMaterial(4, 5));
            Assert.Equal(24.5f, engine.Context.GetService<TemperatureField>().GetTemperature(4, 5));
            Assert.Equal(11L, engine.Context.Clock.FrameIndex);
            Assert.Equal(11L, engine.Context.Clock.SimTickIndex);
            Assert.Equal(11u, engine.Context.GetService<SimulationKernel>().FrameIndex);
            Assert.Equal(SavedWorldSeed, result.WorldSeed);
            Assert.Equal(SavedWorldSeed, engine.Context.GetService<SimulationKernel>().WorldSeed);
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
    /// 验证覆盖读档会用快照中的刚体集合替换当前 Physics 状态，而不是叠加恢复。
    /// </summary>
    [Fact]
    public void EnginePersistentWorldLoadClearsRuntimeRigidBodiesWhenSnapshotHasNone()
    {
        // Arrange：准备输入与初始状态
        string savePath = Path.Combine(Path.GetTempPath(), $"pixelengine-host-rigidbody-clear-{Guid.NewGuid():N}");
        try
        {
            MaterialTable materials = Materials(("empty", CellType.Empty), ("stone", CellType.Solid));
            using Engine engine = new EngineBuilder()
                .WithWorkerCount(1)
                .Build();
            engine.Context.RegisterService(materials);
            _ = engine.AttachResidentSimulationWorld(128, 128, particleCapacity: 8);
            _ = engine.AttachPhysics();
            _ = engine.SaveWorldToDirectory(savePath);

            ISimulationEditApi edit = engine.Context.GetService<ISimulationEditApi>();
            _ = edit.PaintRect(16, 16, 31, 31, material: 1);
            PhysicsSystem physics = engine.Context.GetService<PhysicsSystem>();
            int bodyKey = physics.CreateBodyFromRegion(16, 16, 16, 16);
            // Assert：验证预期结果
            Assert.Equal(0, bodyKey);
            Assert.Equal(1, physics.PhysicsWorld.ActiveBodyCount);

            _ = engine.LoadWorldFromDirectory(savePath);

            Assert.Equal(0, physics.PhysicsWorld.ActiveBodyCount);
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
    /// 验证 Engine world 快照后端也会恢复存活脚本 Behaviour 的字段状态。
    /// </summary>
    [Fact]
    public void EngineWorldSnapshotStoreRestoresScriptBehaviourFields()
    {
        // Arrange：准备输入与初始状态
        MaterialTable materials = Materials(("empty", CellType.Empty), ("stone", CellType.Solid));
        Scripting.Scene scriptScene = new();
        Entity entity = scriptScene.CreateEntity();
        SnapshotCounterBehaviour script = entity.AddComponent<SnapshotCounterBehaviour>();
        script.Score = 7;
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();
        engine.Context.RegisterService(materials);
        engine.Context.RegisterService(scriptScene);
        _ = engine.AttachResidentSimulationWorld(128, 128, particleCapacity: 8);
        _ = engine.AttachScriptingFromServices();
        using EngineWorldSnapshotStore store = new(engine);

        SaveLoadOperationResult save = store.SaveTemporarySnapshot();
        script.Score = 99;
        SaveLoadOperationResult restore = store.RestoreTemporarySnapshot();

        // Assert：验证预期结果
        Assert.True(save.Success, save.Message);
        Assert.True(restore.Success, restore.Message);
        Assert.Equal(7, script.Score);
    }

    /// <summary>
    /// 验证临时 Play 退出会恢复脚本 Scene 拓扑，删除 Play 中新增实体并重建被删 Behaviour。
    /// </summary>
    [Fact]
    public void TemporaryPlayExitRestoresScriptSceneTopology()
    {
        // Arrange：搭建测试场景与依赖
        MaterialTable materials = Materials(("empty", CellType.Empty), ("stone", CellType.Solid));
        Scripting.Scene scriptScene = new();
        Entity entity = scriptScene.CreateEntity();
        SnapshotCounterBehaviour script = entity.AddComponent<SnapshotCounterBehaviour>();
        script.Score = 7;
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();
        engine.Context.RegisterService(materials);
        engine.Context.RegisterService(scriptScene);
        _ = engine.AttachResidentSimulationWorld(128, 128, particleCapacity: 8);
        _ = engine.AttachScriptingFromServices();
        using EngineWorldSnapshotStore store = new(engine);
        EngineEditorPlaySessionService session = new(engine, store);

        EditorPlaySessionResult play = session.EnterPlayTemporary();
        script.Score = 99;
        Entity transient = scriptScene.CreateEntity();
        transient.AddComponent<SnapshotCounterBehaviour>().Score = 123;
        entity.Destroy();
        // Act：执行被测操作
        _ = engine.RunOneTick();
        EditorPlaySessionResult exit = session.ExitPlay();

        ScriptEntityInspection[] restored = scriptScene.CaptureInspectionSnapshot();
        // Assert：验证不变式与预期结果
        Assert.True(play.Succeeded, play.Message);
        Assert.True(exit.Succeeded, exit.Message);
        Assert.Equal(EngineExecutionMode.Edit, engine.Mode);
        _ = Assert.Single(restored);
        ScriptComponentInspection component = Assert.Single(restored[0].Components);
        SnapshotCounterBehaviour restoredScript = Assert.IsType<SnapshotCounterBehaviour>(component.Behaviour);
        Assert.Equal(7, restoredScript.Score);
    }

    /// <summary>
    /// 验证脚本运行时重开关卡会恢复首个脚本 tick 后捕获的 world 与脚本字段基线，且快照可重复使用。
    /// </summary>
    [Fact]
    public void RuntimeRestartRestoresCapturedWorldAndScriptBaseline()
    {
        // Arrange：搭建测试场景与依赖
        MaterialTable materials = Materials(("empty", CellType.Empty), ("stone", CellType.Solid));
        Scripting.Scene scriptScene = new();
        Entity entity = scriptScene.CreateEntity();
        SnapshotCounterBehaviour script = entity.AddComponent<SnapshotCounterBehaviour>();
        script.Score = 7;
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();
        engine.Context.RegisterService(materials);
        engine.Context.RegisterService(scriptScene);
        _ = engine.AttachResidentSimulationWorld(128, 128, particleCapacity: 8);
        _ = engine.AttachScriptingFromServices();
        ISimulationEditApi edit = engine.Context.GetService<ISimulationEditApi>();
        IRuntimeControlApi runtime = engine.Context.GetService<IRuntimeControlApi>();

        // Act：执行被测操作
        _ = engine.RunOneTick();
        edit.PaintCell(4, 5, 1);
        script.Score = 99;
        engine.EnterEditMode();
        RuntimeControlResult firstRestart = runtime.RequestRestartCurrentScene();
        edit.PaintCell(4, 5, 1);
        script.Score = 55;
        RuntimeControlResult secondRestart = runtime.RequestRestartCurrentScene();

        // Assert：验证不变式与预期结果
        Assert.True(firstRestart.Success, firstRestart.Message);
        Assert.True(secondRestart.Success, secondRestart.Message);
        Assert.Equal(EngineExecutionMode.Play, engine.Mode);
        Assert.Equal(0, engine.Context.GetService<CellGrid>().GetMaterial(4, 5));
        Assert.Equal(7, script.Score);
    }

    /// <summary>
    /// 验证 Hosting 能从 save directory 物化 live World/Simulation 后端，并恢复自由粒子快照。
    /// </summary>
    [Fact]
    public void AttachWorldFromSaveDirectoryLoadsWorldAndRegistersRuntimeServices()
    {
        // Arrange：准备输入与初始状态
        string savePath = Path.Combine(Path.GetTempPath(), $"pixelengine-host-save-{Guid.NewGuid():N}");
        try
        {
            MaterialTable materials = Materials(("empty", CellType.Empty), ("sand", CellType.Powder));
            ResidentChunkMap savedChunks = new();
            ResidencyTable savedResidency = new();
            TemperatureField savedTemperature = new();
            ChunkCoord coord = new(0, 0);
            Chunk chunk = new(coord);
            chunk.MaterialBuffer[0] = 1;
            chunk.LifetimeBuffer[0] = 9;
            savedChunks.Add(chunk);
            savedTemperature.AddHeat(0, 0, 24.5f);
            FakeWorldStateBridge savedState = new(
                [new FreeParticleSnapshot(2, 3, 0.5f, -0.25f, 1, 7, 8)],
                []);
            _ = new WorldSaveService().SaveAll(
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

            // Assert：验证预期结果
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
        // Arrange：准备输入与初始状态
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
            _ = new WorldSaveService().SaveAll(
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

            // Assert：验证预期结果
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
        // Arrange：准备输入与初始状态
        string contentRoot = Path.Combine(Path.GetTempPath(), $"pixelengine-scene-world-{Guid.NewGuid():N}");
        try
        {
            string sceneDirectory = Path.Combine(contentRoot, "scenes");
            string savePath = Path.Combine(contentRoot, "saves", "mine");
            _ = Directory.CreateDirectory(sceneDirectory);
            MaterialTable materials = Materials(("empty", CellType.Empty), ("sand", CellType.Powder));
            ResidentChunkMap savedChunks = new();
            Chunk chunk = new(new ChunkCoord(0, 0));
            chunk.MaterialBuffer[0] = 1;
            savedChunks.Add(chunk);
            _ = new WorldSaveService().SaveAll(
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
                                     /*lang=json,strict*/
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

            // Assert：验证预期结果
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
        // Arrange：搭建测试场景与依赖
        using Engine headless = new EngineBuilder()
            .UseHeadless()
            .UseDeterministicMode()
            .Build();

        // Act：执行被测操作
        headless.RunHeadlessTicks(5);

        // Assert：验证不变式与预期结果
        Assert.Equal(5, headless.Context.Clock.FrameIndex);
        Assert.Equal(5, headless.Context.Clock.SimTickIndex);
        Assert.False(headless.Context.Options.EnableGpu);

        using Engine windowed = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();
        _ = Assert.Throws<InvalidOperationException>(() => windowed.RunHeadlessTicks(1));
    }

    private static EngineSceneEntityDocument EntityByStableId(EngineSceneDocument document, int stableId)
    {
        return Assert.Single(document.Entities!, entity => entity.StableId == stableId);
    }

    private static void AssertTransform(
        EngineSceneTransformDocument transform,
        float x,
        float y,
        float rotation,
        float scaleX,
        float scaleY)
    {
        Assert.Equal(x, transform.X);
        Assert.Equal(y, transform.Y);
        Assert.Equal(rotation, transform.RotationRadians);
        Assert.Equal(scaleX, transform.ScaleX);
        Assert.Equal(scaleY, transform.ScaleY);
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

        /// <summary>
        /// 测试 Vector2 字段。
        /// </summary>
        public Vector2 Position { get; set; }
    }

    /// <summary>
    /// procedural scene 测试入口 Behaviour。
    /// </summary>
    public sealed class ProceduralEntryBehaviour : Behaviour
    {
    }

    /// <summary>
    /// 测试 .scene 内声明且可由 Editor 动态脚本自动发现的流式世界入口。
    /// </summary>
    public sealed class StreamingProceduralEntryBehaviour : Behaviour, IStreamingProceduralWorldGenerator
    {
        /// <inheritdoc />
        public ProceduralWorldDescriptor Describe(in ProceduralWorldBuildRequest request)
        {
            Assert.Equal("auto-stream", request.Key);
            return ProceduralWorldDescriptor.CreateInfinite(101, -32, 32, "auto-stream-v1");
        }

        /// <inheritdoc />
        public void PopulateChunk(in ProceduralChunkBuildContext context)
        {
            context.MaterialCells[0] = context.Materials.Resolve("stone").Value;
        }
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

    private sealed class RecordingStreamingWorldGenerator : IStreamingProceduralWorldGenerator
    {
        private readonly Lock _gate = new();
        private readonly HashSet<(int X, int Y)> _generatedCoords = [];

        public (int X, int Y)[] GeneratedCoords
        {
            get
            {
                lock (_gate)
                {
                    return [.. _generatedCoords];
                }
            }
        }

        public ProceduralWorldDescriptor Describe(in ProceduralWorldBuildRequest request)
        {
            Assert.Equal("stream-test", request.Key);
            Assert.NotNull(request.Config);
            return ProceduralWorldDescriptor.CreateInfinite(
                worldSeed: 99,
                initialFocusX: -32,
                initialFocusY: 32,
                persistenceKey: "stream-test-v1");
        }

        public void PopulateChunk(in ProceduralChunkBuildContext context)
        {
            MaterialId stone = context.Materials.Resolve("stone");
            context.MaterialCells[0] = stone.Value;
            context.TemperatureCells[0] = (Half)14f;
            lock (_gate)
            {
                _ = _generatedCoords.Add((context.ChunkX, context.ChunkY));
            }
        }
    }

    private sealed class SeedSwitchingStreamingWorldGenerator : IStreamingProceduralWorldGenerator
    {
        private readonly Lock _gate = new();
        private readonly HashSet<ulong> _describedSeeds = [];
        private readonly HashSet<ulong> _populatedSeeds = [];

        public ulong[] DescribedSeeds
        {
            get
            {
                lock (_gate)
                {
                    return [.. _describedSeeds];
                }
            }
        }

        public ulong[] PopulatedSeeds
        {
            get
            {
                lock (_gate)
                {
                    return [.. _populatedSeeds];
                }
            }
        }

        public ProceduralWorldDescriptor Describe(in ProceduralWorldBuildRequest request)
        {
            Assert.NotNull(request.Config);
            ulong seed = request.WorldSeedOverride ?? 101UL;
            lock (_gate)
            {
                _ = _describedSeeds.Add(seed);
            }

            return ProceduralWorldDescriptor.CreateInfinite(seed, 0, 0, "seed-switch-v1");
        }

        public void PopulateChunk(in ProceduralChunkBuildContext context)
        {
            lock (_gate)
            {
                _ = _populatedSeeds.Add(context.WorldSeed);
            }

            context.MaterialCells[0] = context.WorldSeed % 2 == 0
                ? context.Materials.Resolve("metal").Value
                : context.Materials.Resolve("stone").Value;
            context.TemperatureCells[0] = (Half)(context.WorldSeed % 50);
        }
    }

    private sealed class RestartableCharacterBehaviour : Behaviour
    {
        private CharacterHandle _handle;

        public int StartCount { get; set; }

        public bool HasLiveHandle { get; private set; }

        protected override void OnStart()
        {
            _handle = Context.Character.Create(4, 4, 4, 6);
            _ = Context.Character.GetState(_handle);
            HasLiveHandle = true;
            StartCount++;
        }

        protected override void OnUpdate(float dt)
        {
            _ = dt;
            if (HasLiveHandle)
            {
                _ = Context.Character.GetState(_handle);
            }
        }

        protected override void OnDestroy()
        {
            _handle = default;
            HasLiveHandle = false;
        }
    }

    private sealed class DeferredProceduralRestartBehaviour : Behaviour
    {
        public ulong TargetSeed { get; set; }

        public bool Requested { get; set; }

        public bool WasAccepted { get; private set; }

        public RuntimeRestartStatus StatusWhenAccepted { get; private set; }

        protected override void OnUpdate(float dt)
        {
            _ = dt;
            if (!Requested)
            {
                return;
            }

            Requested = false;
            RuntimeControlResult result = Context.Runtime.RequestRestartCurrentProceduralWorld(TargetSeed);
            WasAccepted = result.Success;
            StatusWhenAccepted = Context.Runtime.Capture().RestartStatus;
        }
    }

    private sealed class SnapshotCounterBehaviour : Behaviour
    {
        public int Score { get; set; }
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
