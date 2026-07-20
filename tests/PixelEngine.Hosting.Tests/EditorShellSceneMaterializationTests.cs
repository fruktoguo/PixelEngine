using System.Numerics;
using PixelEngine.Editor.Shell;
using PixelEngine.Scripting;
using PixelEngine.Simulation;
using PixelEngine.UI;
using PixelEngine.World;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 独立编辑器 authoring 场景物化与命令栈测试。
/// 不变式：authoring 场景物化可撤销、实体/组件与命令栈一致。
/// </summary>
public sealed class EditorShellSceneMaterializationTests
{
    /// <summary>
    /// 验证 Editor 先物化项目脚本 Behaviour，再解析 .scene 声明的程序化世界生成器。
    /// </summary>
    [Fact]
    public void EditorSessionRegistersSceneScriptsBeforeAttachingDeclaredProceduralWorld()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "apps",
            "PixelEngine.Editor.Shell",
            "EditorProjectSession.cs"));
        int openStart = source.IndexOf(
            "public static EditorProjectSession Open",
            StringComparison.Ordinal);
        int runTickStart = source.IndexOf(
            "public void RunOneTick",
            openStart,
            StringComparison.Ordinal);
        string open = source[openStart..runTickStart];

        int content = open.IndexOf("AttachContent(engine);", StringComparison.Ordinal);
        int scene = open.IndexOf("LoadSceneModel(project, sceneRelativePath);", StringComparison.Ordinal);
        int scripts = open.IndexOf("RegisterInitialProjectScriptAssembly", StringComparison.Ordinal);
        int projection = open.IndexOf("ProjectAuthoringScene(engine, sceneModel);", StringComparison.Ordinal);
        int world = open.IndexOf("AttachWorld(engine);", StringComparison.Ordinal);

        Assert.True(content >= 0 && content < scene);
        Assert.True(scene < scripts);
        Assert.True(scripts < projection);
        Assert.True(projection < world);
    }

    /// <summary>
    /// 验证 Editor 流送配置覆盖 720x480 authoring 画布最远 chunk，而非只覆盖运行视口。
    /// </summary>
    [Fact]
    public void EditorStreamingResidencyCoversDefaultAuthoringCanvas()
    {
        WorldStreamingConfig config = EditorProjectSession.CreateEditorWorldStreamingConfig();
        WorldCamera camera = new(
            focusX: 0,
            focusY: 208,
            viewportCellsX: 640,
            viewportCellsY: 360);
        ActivationPolicy policy = new();
        ChunkRect active = policy.ComputeActive(camera, config);
        ChunkRect border = policy.ComputeBorder(active, config);

        Assert.True(border.Contains(new ChunkCoord(0, 0)));
        Assert.True(border.Contains(new ChunkCoord(11, 7)));
    }

    /// <summary>
    /// 验证 EditorShell fallback 材质查询不会退化为仅返回 Name/Density/IsSolid 的窄摘要。
    /// </summary>
    [Fact]
    public void ShellFallbackMaterialQueryPublishesFullMaterialInfo()
    {
        // Arrange：准备输入与初始状态
        MaterialTable materials = new(
        [
            new MaterialDef
            {
                Id = 0,
                Name = "empty",
                Type = CellType.Empty,
                HeatCapacity = 1f,
                TextureId = -1,
                LegendVisible = false,
            },
            new MaterialDef
            {
                Id = 1,
                Name = "lava",
                Type = CellType.Liquid,
                Density = 210,
                Dispersion = 5,
                HeatCapacity = 1f,
                TextureId = -1,
                BaseColorBGRA = 0xFF1030F0,
                DisplayName = "Lava",
                LegendCategory = MaterialLegendCategory.Hazard,
                MineYield = 2,
                RenderStyle = MaterialRenderStyle.Emissive,
                Hardness = 9,
                MaxIntegrity = 77,
                Flammability = 180,
                AutoIgnitionTemp = 450,
                FireHp = -1,
                TemperatureOfFire = 240,
                GeneratesSmoke = 12,
                HeatConduct = 250,
                PropertyFlags = MaterialProperty.Emissive | MaterialProperty.Acid,
            },
            new MaterialDef
            {
                Id = 2,
                Name = "crystal",
                Type = CellType.Solid,
                Density = 220,
                HeatCapacity = 1f,
                TextureId = -1,
                DisplayName = "Crystal",
                LegendCategory = MaterialLegendCategory.Resource,
                BaseColorBGRA = 0xFFFF80FF,
                MineYield = 1,
                Durability = 12,
                MaxIntegrity = 48,
            },
        ]);
        EditorProjectSession.ShellMaterialQuery query = new(materials);

        // Assert：验证预期结果
        Assert.True(query.TryResolve("lava", out MaterialId lavaId));
        MaterialInfo lava = query.GetInfo(lavaId);
        Assert.Equal("Lava", lava.DisplayName);
        Assert.Equal("Hazard", lava.LegendCategory);
        Assert.True(lava.LegendVisible);
        Assert.Equal(0xFF1030F0u, lava.BaseColorBgra);
        Assert.Equal((byte)2, lava.MineYield);
        Assert.Equal(CellType.Liquid, lava.CellType);
        Assert.Equal(MaterialLegendCategory.Hazard, lava.Category);
        Assert.True(lava.Emissive);
        Assert.Equal((byte)9, lava.Hardness);
        Assert.Equal((ushort)77, lava.MaxIntegrity);
        Assert.False(lava.IsDestructible);
        Assert.Equal((byte)5, lava.FlowRate);
        Assert.Equal((byte)180, lava.Flammability);
        Assert.Equal((ushort)450, lava.AutoIgnitionTemp);
        Assert.Equal(-1, lava.FireHp);
        Assert.Equal((byte)240, lava.TemperatureOfFire);
        Assert.Equal((byte)12, lava.GeneratesSmoke);
        Assert.Equal((byte)250, lava.HeatConduct);
        Assert.Equal(1f, lava.HeatCapacity);
        Assert.Equal(MaterialRenderStyle.Emissive, lava.RenderStyle);
        Assert.True((lava.Properties & MaterialProperty.Acid) != 0);

        MaterialInfo crystal = query.GetInfo(query.Resolve("crystal"));
        Assert.Equal("Crystal", crystal.DisplayName);
        Assert.Equal("Resource", crystal.LegendCategory);
        Assert.Equal(MaterialLegendCategory.Resource, crystal.Category);
        Assert.True(crystal.IsSolid);
        Assert.True(crystal.IsDestructible);
        Assert.Equal((byte)12, crystal.Hardness);
        Assert.Equal((ushort)48, crystal.MaxIntegrity);
        Assert.Equal((byte)1, crystal.MineYield);
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

        throw new DirectoryNotFoundException("无法定位 PixelEngine 仓库根目录。");
    }

    /// <summary>
    /// 验证 authoring 层级投影到运行时场景时烘焙世界 TRS、保持 StableId 映射，并绑定 Vector2/MaterialId 字段。
    /// </summary>
    [Fact]
    public void RuntimeProjectionBakesHierarchyAndBindsStableIdsVector2AndMaterialId()
    {
        // Arrange：准备输入与初始状态
        EditorSceneModel model = EditorSceneModel.FromDocument(new EngineSceneDocument
        {
            FormatVersion = 2,
            Name = "projection",
            Entities =
            [
                new EngineSceneEntityDocument
                {
                    StableId = 10,
                    Name = "root",
                    Enabled = false,
                    Transform = new EngineSceneTransformDocument { X = 10, Y = 20, ScaleX = 2, ScaleY = 3 },
                },
                new EngineSceneEntityDocument
                {
                    StableId = 20,
                    Name = "child",
                    ParentId = 10,
                    Transform = new EngineSceneTransformDocument { X = 5, Y = 6, RotationRadians = 0.25f, ScaleX = 4, ScaleY = 5 },
                    Behaviours =
                    [
                        new EngineSceneBehaviourDocument
                        {
                            TypeName = typeof(EditorShellProjectionProbe).FullName,
                            SerializedFields = new Dictionary<string, string>
                            {
                                ["Label"] = "child",
                                ["Material"] = "4",
                                ["Position"] = "3.5,4.25",
                                ["TextureReference"] = ScriptAssetReference.Encode("asset_texture", "textures/sand.png", ScriptAssetKind.Texture),
                                ["_privateTextureReference"] = ScriptAssetReference.Encode("asset_private_texture", "textures/private.png", ScriptAssetKind.Texture),
                            },
                        },
                    ],
                },
            ],
        });
        ScriptAssemblyRegistry scripts = new();
        scripts.Register(typeof(EditorShellProjectionProbe).Assembly);

        Assert.False(model.Get(10).Enabled);
        Assert.False(Assert.Single(model.ToDocument().Entities!, entity => entity.StableId == 10).Enabled!.Value);

        EditorSceneRuntimeProjection projection = EditorSceneRuntimeProjection.Build(model, scripts);

        // Assert：验证预期结果
        Assert.True(projection.TryGetRuntimeEntityId(10, out int rootRuntimeId));
        Assert.True(projection.TryGetRuntimeEntityId(20, out int childRuntimeId));
        Assert.NotEqual(rootRuntimeId, childRuntimeId);
        ScriptEntityInspection child = Assert.Single(
            projection.Scene.CaptureInspectionSnapshot(),
            entity => entity.EntityId == childRuntimeId);
        Assert.Equal(20, child.Transform!.X);
        Assert.Equal(38, child.Transform.Y);
        Assert.Equal(0.25f, child.Transform.RotationRadians);
        Assert.Equal(8, child.Transform.ScaleX);
        Assert.Equal(15, child.Transform.ScaleY);
        EditorShellProjectionProbe probe = Assert.IsType<EditorShellProjectionProbe>(Assert.Single(child.Components).Behaviour);
        Assert.False(probe.Enabled);
        Assert.Equal("child", probe.Label);
        Assert.Equal(new Vector2(3.5f, 4.25f), probe.Position);
        Assert.Equal(new MaterialId(4), probe.Material);
        Assert.Equal(new ScriptAssetReference(ScriptAssetKind.Texture, "asset_texture", "textures/sand.png"), probe.TextureReference);
        Assert.Equal(new ScriptAssetReference(ScriptAssetKind.Texture, "asset_private_texture", "textures/private.png"), probe.PrivateTextureReference);
    }

    /// <summary>
    /// 验证 Editor authoring 模型加载、替换与再序列化不会丢失场景初始世界存档来源。
    /// </summary>
    [Fact]
    public void EditorSceneModelRoundTripsInitialSaveDirectoryThroughReplace()
    {
        EditorSceneModel loaded = EditorSceneModel.FromDocument(new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "saved-world",
            InitialSaveDirectory = "../saves/checkpoint",
            Entities = [],
        });
        Assert.Equal("../saves/checkpoint", loaded.ToDocument().InitialSaveDirectory);

        EditorSceneModel active = EditorSceneModel.Empty("active");
        active.ReplaceWith(loaded, markDirty: false);

        EngineSceneDocument roundTrip = active.ToDocument();
        Assert.Equal("saved-world", roundTrip.Name);
        Assert.Equal("../saves/checkpoint", roundTrip.InitialSaveDirectory);
        Assert.False(active.IsDirty);
    }

    /// <summary>
    /// 验证 Editor authoring 模型不会在加载、替换或保存时丢失流式程序化世界生成器键。
    /// </summary>
    [Fact]
    public void EditorSceneModelRoundTripsProceduralWorldGeneratorThroughReplace()
    {
        EditorSceneModel loaded = EditorSceneModel.FromDocument(new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "infinite-world",
            ProceduralWorldGenerator = "Game.InfiniteWorldDirector",
            Entities = [],
        });
        Assert.Equal("Game.InfiniteWorldDirector", loaded.ToDocument().ProceduralWorldGenerator);

        EditorSceneModel active = EditorSceneModel.Empty("active");
        active.ReplaceWith(loaded, markDirty: false);

        EngineSceneDocument roundTrip = active.ToDocument();
        Assert.Equal("infinite-world", roundTrip.Name);
        Assert.Equal("Game.InfiniteWorldDirector", roundTrip.ProceduralWorldGenerator);
        Assert.Null(roundTrip.InitialSaveDirectory);
        Assert.False(active.IsDirty);
    }

    /// <summary>
    /// 验证复制 Web Canvas 时保留 manifest/scaler 但清除 copied primary，避免一次快捷复制制造非法双 primary。
    /// </summary>
    [Fact]
    public void DuplicateCanvasPreservesSettingsButClearsPrimaryIdentity()
    {
        EditorSceneModel model = EditorSceneModel.FromDocument(new EngineSceneDocument
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "canvas-duplicate",
            Entities =
            [
                new EngineSceneEntityDocument
                {
                    StableId = 1,
                    Name = "HUD Canvas",
                    Enabled = true,
                    WebCanvas = new EngineSceneWebCanvasDocument
                    {
                        ManifestPath = "ui/ui-manifest.json",
                        InitialScreenId = "hud",
                        Enabled = true,
                        SortingOrder = 100,
                        Primary = true,
                    },
                    CanvasScaler = new EngineSceneCanvasScalerDocument
                    {
                        ScaleMode = UiScaleMode.ScaleWithScreenSize,
                        ReferenceWidth = 1920,
                        ReferenceHeight = 1080,
                        MatchWidthOrHeight = 0.35f,
                        ReferencePixelsPerUnit = 120f,
                    },
                },
            ],
        });
        EditorUndoStack undo = new();

        undo.Execute(model, new DuplicateGameObjectCommand(1));
        EditorGameObject duplicate = model.Get(model.SelectedStableId!.Value);

        Assert.NotEqual(1, duplicate.StableId);
        Assert.Equal("ui/ui-manifest.json", duplicate.WebCanvas!.ManifestPath);
        Assert.Equal("hud", duplicate.WebCanvas.InitialScreenId);
        Assert.Equal(100, duplicate.WebCanvas.SortingOrder);
        Assert.False(duplicate.WebCanvas.Primary);
        Assert.Equal(UiScaleMode.ScaleWithScreenSize, duplicate.CanvasScaler!.Settings.ScaleMode);
        Assert.Equal(1920f, duplicate.CanvasScaler.Settings.ReferenceWidth);
        Assert.Equal(0.35f, duplicate.CanvasScaler.Settings.MatchWidthOrHeight);
        EngineSceneCanvasSet canvasSet = EngineSceneCanvasResolver.Resolve(model.ToDocument());
        Assert.Equal(2, canvasSet.Count);
        Assert.Equal(GameUiCanvasIdentity.FromStableId(1), canvasSet.PrimaryId);

        Assert.True(undo.Undo(model));
        Assert.Equal(1, model.Count);
        Assert.True(undo.Redo(model));
        Assert.False(model.Get(model.SelectedStableId!.Value).WebCanvas!.Primary);
    }

    /// <summary>Prefab 资产与实例永不持久 primary，但保留 Web Canvas 与完整 scaler baseline。</summary>
    [Fact]
    public void PrefabCanvasClearsPrimaryAndPreservesScalerBaseline()
    {
        string contentRoot = Path.Combine(Path.GetTempPath(), $"pixelengine-canvas-prefab-{Guid.NewGuid():N}");
        try
        {
            EditorPrefabAssetStore prefabs = new(contentRoot);
            EditorSceneModel authoring = EditorSceneModel.Empty("canvas-prefab-source");
            EditorGameObject source = authoring.Create("HUD Canvas");
            source.WebCanvas = new EditorWebCanvasComponent
            {
                ManifestPath = "ui/ui-manifest.json",
                InitialScreenId = "hud",
                Enabled = true,
                SortingOrder = 50,
                Primary = true,
            };
            source.CanvasScaler = new EditorCanvasScalerComponent
            {
                Settings = UiCanvasScalerSettings.Default with
                {
                    ScaleMode = UiScaleMode.ConstantPhysicalSize,
                    PhysicalUnit = UiPhysicalUnit.Millimeters,
                    FallbackScreenDpi = 144f,
                    ReferencePixelsPerUnit = 110f,
                },
            };
            const string AssetPath = "prefabs/hud.prefab";

            prefabs.CreatePrefabFromSubtree(authoring, source.StableId, AssetPath);
            EngineSceneEntityDocument persisted = Assert.Single(
                EngineSceneDocumentLoader.LoadDocument(Path.Combine(contentRoot, "prefabs", "hud.prefab")).Entities!);
            EditorSceneModel scene = EditorSceneModel.Empty("instance");
            EditorGameObject instance = prefabs.InstantiatePrefab(scene, AssetPath, parentId: null);
            EditorGameObject secondInstance = prefabs.InstantiatePrefab(scene, AssetPath, parentId: null);
            EngineSceneCanvasSet runtimeCanvases = EngineSceneCanvasResolver.Resolve(scene.ToDocument());

            Assert.False(persisted.WebCanvas!.Primary);
            Assert.Equal("ui/ui-manifest.json", persisted.WebCanvas.ManifestPath);
            Assert.Equal(UiScaleMode.ConstantPhysicalSize, persisted.CanvasScaler!.ScaleMode);
            Assert.False(instance.WebCanvas!.Primary);
            Assert.Equal(50, instance.WebCanvas.SortingOrder);
            Assert.Equal(UiPhysicalUnit.Millimeters, instance.CanvasScaler!.Settings.PhysicalUnit);
            Assert.Equal(144f, instance.CanvasScaler.Settings.FallbackScreenDpi);
            Assert.NotEqual(instance.StableId, secondInstance.StableId);
            Assert.Equal(2, runtimeCanvases.Count);
            Assert.Contains(runtimeCanvases.Canvases.ToArray(), item =>
                item.Id == GameUiCanvasIdentity.FromStableId(instance.StableId));
            Assert.Contains(runtimeCanvases.Canvases.ToArray(), item =>
                item.Id == GameUiCanvasIdentity.FromStableId(secondInstance.StableId));
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
    /// 验证编辑器命令栈覆盖创建、删除、重父、重命名、复制、组件字段与 Transform 修改的 Undo/Redo 往返。
    /// </summary>
    [Fact]
    public void UndoRedoCommandStackRoundTripsAuthoringMutations()
    {
        // Arrange：准备输入与初始状态
        EditorSceneModel model = EditorSceneModel.Empty("commands");
        EditorUndoStack undo = new();

        undo.Execute(model, new CreateGameObjectCommand("Root"));
        int rootId = model.SelectedStableId!.Value;
        undo.Execute(model, new CreateGameObjectCommand("Child", rootId));
        int childId = model.SelectedStableId!.Value;
        EditorComponentModel component = new(typeof(EditorShellProjectionProbe).FullName!);
        component.SerializedFields["Label"] = "initial";
        undo.Execute(model, new AddComponentCommand(childId, component));
        undo.Execute(model, new SetComponentFieldCommand(childId, componentIndex: 0, fieldName: "Label", value: "edited"));
        undo.Execute(model, new RenameGameObjectCommand(rootId, "Root Renamed"));
        undo.Execute(model, new SetTransformCommand(childId, new EditorSceneTransform { X = 3, Y = 4, RotationRadians = 0.5f, ScaleX = 2, ScaleY = 2 }));
        undo.Execute(model, new ReparentGameObjectCommand(childId, newParentId: null));
        undo.Execute(model, new DuplicateGameObjectCommand(childId));
        int duplicateId = model.SelectedStableId!.Value;
        undo.Execute(model, new DeleteGameObjectCommand(duplicateId));

        AssertAuthoringAfterRedo(model, rootId, childId);
        for (int i = 0; i < 9; i++)
        {
            // Assert：验证预期结果
            Assert.True(undo.Undo(model));
        }

        Assert.Equal(0, model.Count);
        Assert.False(undo.CanUndo);
        Assert.True(undo.CanRedo);
        for (int i = 0; i < 9; i++)
        {
            Assert.True(undo.Redo(model));
        }

        AssertAuthoringAfterRedo(model, rootId, childId);
    }

    /// <summary>
    /// 验证属性命令 Undo/Redo 同时恢复 prefab override 元数据，避免视觉值已撤销但保存时仍残留 override。
    /// </summary>
    [Fact]
    public void TransformUndoRedoRestoresPrefabOverrideMetadataSymmetrically()
    {
        EditorSceneModel scene = EditorSceneModel.Empty("prefab-command-undo");
        EditorGameObject instance = scene.Create("Instance");
        instance.PrefabLink = new EditorPrefabLink
        {
            AssetId = "prefab-rock",
            AssetPath = "prefabs/rock.prefab",
            SourceStableId = "1",
        };
        EditorUndoStack undo = new();
        EditorSceneTransform after = new() { X = 12f, Y = 18f, RotationRadians = 0.2f, ScaleX = 2f, ScaleY = 3f };

        undo.Execute(scene, new SetTransformCommand(instance.StableId, after));

        Assert.Equal(5, instance.PrefabLink.Overrides.Count);
        Assert.True(undo.Undo(scene));
        Assert.Equal(0f, instance.Transform.X);
        Assert.Empty(instance.PrefabLink.Overrides);
        Assert.True(undo.Redo(scene));
        Assert.Equal(12f, instance.Transform.X);
        Assert.Equal(5, instance.PrefabLink.Overrides.Count);
    }

    /// <summary>
    /// 验证 prefab 实例化、override、Revert 与资产 baseline 更新传播。
    /// </summary>
    [Fact]
    public void PrefabInstancesApplyOverridesRevertAndRefreshFromAsset()
    {
        // Arrange：准备输入与初始状态
        string contentRoot = Path.Combine(Path.GetTempPath(), $"pixelengine-prefab-{Guid.NewGuid():N}");
        try
        {
            EditorPrefabAssetStore prefabs = new(contentRoot);
            EditorSceneModel scene = EditorSceneModel.Empty("prefab");
            EditorGameObject source = scene.Create("Rock");
            source.Transform = new EditorSceneTransform { X = 1, Y = 2, ScaleX = 1, ScaleY = 1 };
            EditorComponentModel component = new(typeof(EditorShellProjectionProbe).FullName!);
            component.SerializedFields["Label"] = "baseline";
            source.Components.Add(component);
            const string AssetPath = "prefabs/rock.prefab";
            prefabs.CreatePrefabFromSubtree(scene, source.StableId, AssetPath);

            EditorGameObject instance = prefabs.InstantiatePrefab(scene, AssetPath, parentId: null);
            EditorUndoStack undo = new();
            undo.Execute(scene, new SetTransformCommand(instance.StableId, new EditorSceneTransform { X = 9, Y = 8, RotationRadians = 0.25f, ScaleX = 2, ScaleY = 2 }));
            undo.Execute(scene, new SetComponentFieldCommand(instance.StableId, componentIndex: 0, fieldName: "Label", value: "override"));
            prefabs.RefreshPrefabInstances(scene);

            // Assert：验证预期结果
            Assert.Equal(9, instance.Transform.X);
            Assert.Equal("override", instance.Components[0].SerializedFields["Label"]);
            undo.Execute(scene, new RevertPrefabOverridesCommand(instance.StableId));
            prefabs.RefreshPrefabInstances(scene);
            Assert.Equal(1, instance.Transform.X);
            Assert.Equal("baseline", instance.Components[0].SerializedFields["Label"]);

            SavePrefabDocument(
                contentRoot,
                AssetPath,
                name: "Rock",
                label: "propagated",
                transform: new EngineSceneTransformDocument { X = 4, Y = 5, ScaleX = 1, ScaleY = 1 });
            prefabs.RefreshPrefabInstances(scene);

            Assert.Equal(4, instance.Transform.X);
            Assert.Equal(5, instance.Transform.Y);
            Assert.Equal("propagated", instance.Components[0].SerializedFields["Label"]);
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
    /// 验证嵌套 prefab 会展开并接收底层 prefab 资产更新。
    /// </summary>
    [Fact]
    public void NestedPrefabInstancesExpandAndPropagateNestedAssetUpdates()
    {
        // Arrange：准备输入与初始状态
        string contentRoot = Path.Combine(Path.GetTempPath(), $"pixelengine-nested-prefab-{Guid.NewGuid():N}");
        try
        {
            EditorPrefabAssetStore prefabs = new(contentRoot);
            const string LeafPath = "prefabs/leaf.prefab";
            const string ParentPath = "prefabs/parent.prefab";
            SavePrefabDocument(
                contentRoot,
                LeafPath,
                name: "Leaf",
                label: "leaf-a",
                transform: new EngineSceneTransformDocument { X = 2, Y = 3, ScaleX = 1, ScaleY = 1 });
            EditorSceneModel authoring = EditorSceneModel.Empty("authoring");
            EditorGameObject parent = authoring.Create("Parent");
            EditorGameObject nestedLeaf = prefabs.InstantiatePrefab(authoring, LeafPath, parent.StableId);
            prefabs.CreatePrefabFromSubtree(authoring, parent.StableId, ParentPath);
            EditorSceneModel scene = EditorSceneModel.Empty("scene");

            EditorGameObject parentInstance = prefabs.InstantiatePrefab(scene, ParentPath, parentId: null);
            EditorGameObject childInstance = scene.Get(parentInstance.Children[0]);
            // Assert：验证预期结果
            Assert.Equal(nestedLeaf.Name, childInstance.Name);
            Assert.Equal("leaf-a", childInstance.Components[0].SerializedFields["Label"]);

            SavePrefabDocument(
                contentRoot,
                LeafPath,
                name: "Leaf",
                label: "leaf-b",
                transform: new EngineSceneTransformDocument { X = 6, Y = 7, ScaleX = 1, ScaleY = 1 });
            prefabs.RefreshPrefabInstances(scene);

            Assert.Equal("leaf-b", childInstance.Components[0].SerializedFields["Label"]);
            Assert.Equal(6, childInstance.Transform.X);
            Assert.Equal(7, childInstance.Transform.Y);
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
    /// 验证 .scene loader 会把 stable asset reference 绑定到强类型脚本字段。
    /// </summary>
    [Fact]
    public void SceneDocumentLoaderBindsScriptAssetReferenceFields()
    {
        // Arrange：准备输入与初始状态
        EngineSceneDocument document = new()
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "asset-reference-loader",
            Entities =
            [
                new EngineSceneEntityDocument
                {
                    StableId = 1,
                    Name = "asset-reference",
                    Transform = new EngineSceneTransformDocument(),
                    Behaviours =
                    [
                        new EngineSceneBehaviourDocument
                        {
                            TypeName = typeof(EditorShellProjectionProbe).FullName,
                            SerializedFields = new Dictionary<string, string>
                            {
                                ["TextureReference"] = ScriptAssetReference.Encode("asset_texture", "textures/sand.png", ScriptAssetKind.Texture),
                                ["_privateTextureReference"] = ScriptAssetReference.Encode("asset_private_texture", "textures/private.png", ScriptAssetKind.Texture),
                            },
                        },
                    ],
                },
            ],
        };
        ScriptAssemblyRegistry scripts = new();
        scripts.Register(typeof(EditorShellProjectionProbe).Assembly);

        Scripting.Scene scene = EngineSceneDocumentLoader.Build(document, scripts);

        // Assert：验证预期结果
        ScriptEntityInspection entity = Assert.Single(scene.CaptureInspectionSnapshot());
        EditorShellProjectionProbe probe = Assert.IsType<EditorShellProjectionProbe>(Assert.Single(entity.Components).Behaviour);
        Assert.Equal(new ScriptAssetReference(ScriptAssetKind.Texture, "asset_texture", "textures/sand.png"), probe.TextureReference);
        Assert.Equal(new ScriptAssetReference(ScriptAssetKind.Texture, "asset_private_texture", "textures/private.png"), probe.PrivateTextureReference);
    }

    /// <summary>
    /// 验证统一 SerializedFields 绑定规则拒绝隐藏字段和 readonly 字段，避免绕过 Inspector 暴露边界。
    /// </summary>
    [Theory]
    [InlineData("_hiddenLabel")]
    [InlineData("_readonlyLabel")]
    public void SerializedFieldBindingRejectsHiddenAndReadonlyFields(string fieldName)
    {
        // Arrange：准备输入与初始状态
        EngineSceneDocument document = new()
        {
            FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
            Name = "rejected-field-binding",
            Entities =
            [
                new EngineSceneEntityDocument
                {
                    StableId = 1,
                    Name = "rejected",
                    Transform = new EngineSceneTransformDocument(),
                    Behaviours =
                    [
                        new EngineSceneBehaviourDocument
                        {
                            TypeName = typeof(EditorShellProjectionProbe).FullName,
                            SerializedFields = new Dictionary<string, string>
                            {
                                [fieldName] = "mutated",
                            },
                        },
                    ],
                },
            ],
        };
        ScriptAssemblyRegistry scripts = new();
        scripts.Register(typeof(EditorShellProjectionProbe).Assembly);
        EditorSceneModel model = EditorSceneModel.FromDocument(document);

        // Assert：验证预期结果
        _ = Assert.Throws<InvalidOperationException>(() => EngineSceneDocumentLoader.Build(document, scripts));
        _ = Assert.Throws<InvalidOperationException>(() => EditorSceneRuntimeProjection.Build(model, scripts));
    }

    private static void AssertAuthoringAfterRedo(EditorSceneModel model, int rootId, int childId)
    {
        Assert.Equal(2, model.Count);
        EditorGameObject root = model.Get(rootId);
        EditorGameObject child = model.Get(childId);
        Assert.Equal("Root Renamed", root.Name);
        Assert.Null(child.ParentId);
        Assert.Equal(3, child.Transform.X);
        Assert.Equal(4, child.Transform.Y);
        Assert.Equal(0.5f, child.Transform.RotationRadians);
        Assert.Equal(2, child.Transform.ScaleX);
        Assert.Equal(2, child.Transform.ScaleY);
        EditorComponentModel component = Assert.Single(child.Components);
        Assert.Equal("edited", component.SerializedFields["Label"]);
    }

    private static void SavePrefabDocument(string contentRoot, string assetPath, string name, string label, EngineSceneTransformDocument transform)
    {
        string fullPath = Path.Combine(contentRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        EngineSceneDocumentLoader.SaveDocument(
            new EngineSceneDocument
            {
                FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
                Name = name,
                Entities =
                [
                    new EngineSceneEntityDocument
                    {
                        StableId = 1,
                        Name = name,
                        Transform = transform,
                        Behaviours =
                        [
                            new EngineSceneBehaviourDocument
                            {
                                TypeName = typeof(EditorShellProjectionProbe).FullName,
                                SerializedFields = new Dictionary<string, string>
                                {
                                    ["Label"] = label,
                                },
                            },
                        ],
                    },
                ],
            },
            fullPath);
    }

    /// <summary>
    /// 投影字段绑定测试用 Behaviour。
    /// </summary>
    public sealed class EditorShellProjectionProbe : Behaviour
    {
        /// <summary>
        /// 测试字符串字段。
        /// </summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// 测试 Vector2 字段。
        /// </summary>
        public Vector2 Position { get; set; }

        /// <summary>
        /// 测试 MaterialId 字段。
        /// </summary>
        public MaterialId Material { get; set; }

        /// <summary>
        /// 测试 typed asset reference 字段。
        /// </summary>
        public ScriptAssetReference TextureReference { get; set; }

#pragma warning disable IDE0032 // SerializedFieldBinder 测试需要显式反射字段，不能改成 auto property。
#pragma warning disable IDE0044 // _privateTextureReference 必须保持非 readonly，才能验证 [SerializeField] private 字段可被绑定。
        [SerializeField]
        [AssetField(ScriptAssetKind.Texture)]
        private ScriptAssetReference _privateTextureReference = ScriptAssetReference.Empty;
#pragma warning restore IDE0044

        [SerializeField]
        [HideInInspector]
        private readonly string _hiddenLabel = "hidden";

        [SerializeField]
        private readonly string _readonlyLabel = "readonly";
#pragma warning restore IDE0032

        /// <summary>
        /// 暴露 private SerializeField 字段供测试断言。
        /// </summary>
        public ScriptAssetReference PrivateTextureReference => _privateTextureReference;

        /// <summary>
        /// 暴露隐藏字段的当前值，避免反射测试字段被编译器视为未使用。
        /// </summary>
        public string HiddenLabelProbe => _hiddenLabel;

        /// <summary>
        /// 暴露 readonly 字段的当前值，避免反射测试字段被编译器视为未使用。
        /// </summary>
        public string ReadonlyLabelProbe => _readonlyLabel;
    }
}
