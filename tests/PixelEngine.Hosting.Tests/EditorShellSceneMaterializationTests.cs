using System.Numerics;
using PixelEngine.Editor.Shell;
using PixelEngine.Scripting;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 独立编辑器 authoring 场景物化与命令栈测试。
/// </summary>
public sealed class EditorShellSceneMaterializationTests
{
    /// <summary>
    /// 验证 EditorShell fallback 材质查询不会退化为仅返回 Name/Density/IsSolid 的窄摘要。
    /// </summary>
    [Fact]
    public void ShellFallbackMaterialQueryPublishesFullMaterialInfo()
    {
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

    /// <summary>
    /// 验证 authoring 层级投影到运行时场景时烘焙世界 TRS、保持 StableId 映射，并绑定 Vector2/MaterialId 字段。
    /// </summary>
    [Fact]
    public void RuntimeProjectionBakesHierarchyAndBindsStableIdsVector2AndMaterialId()
    {
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
                            TypeName = typeof(EditorShellProjectionProbe).FullName!,
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

        EditorSceneRuntimeProjection projection = EditorSceneRuntimeProjection.Build(model, scripts);

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
        Assert.Equal("child", probe.Label);
        Assert.Equal(new Vector2(3.5f, 4.25f), probe.Position);
        Assert.Equal(new MaterialId(4), probe.Material);
        Assert.Equal(new ScriptAssetReference(ScriptAssetKind.Texture, "asset_texture", "textures/sand.png"), probe.TextureReference);
        Assert.Equal(new ScriptAssetReference(ScriptAssetKind.Texture, "asset_private_texture", "textures/private.png"), probe.PrivateTextureReference);
    }

    /// <summary>
    /// 验证编辑器命令栈覆盖创建、删除、重父、重命名、复制、组件字段与 Transform 修改的 Undo/Redo 往返。
    /// </summary>
    [Fact]
    public void UndoRedoCommandStackRoundTripsAuthoringMutations()
    {
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
    /// 验证 prefab 实例化、override、Revert 与资产 baseline 更新传播。
    /// </summary>
    [Fact]
    public void PrefabInstancesApplyOverridesRevertAndRefreshFromAsset()
    {
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
                            TypeName = typeof(EditorShellProjectionProbe).FullName!,
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

        PixelEngine.Scripting.Scene scene = EngineSceneDocumentLoader.Build(document, scripts);

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
                            TypeName = typeof(EditorShellProjectionProbe).FullName!,
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
                                TypeName = typeof(EditorShellProjectionProbe).FullName!,
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

        /// <summary>
        /// 测试 private SerializeField typed asset reference 字段。
        /// </summary>
        [SerializeField]
        [AssetField(ScriptAssetKind.Texture)]
        private ScriptAssetReference _privateTextureReference = ScriptAssetReference.Empty;

        /// <summary>
        /// 测试隐藏字段不会被 SerializedFields 绑定。
        /// </summary>
        [SerializeField]
        [HideInInspector]
        private string _hiddenLabel = "hidden";

        /// <summary>
        /// 测试 readonly 字段不会被 SerializedFields 绑定。
        /// </summary>
        [SerializeField]
        private readonly string _readonlyLabel = "readonly";

        /// <summary>
        /// 暴露 private SerializeField 字段供测试断言。
        /// </summary>
        public ScriptAssetReference PrivateTextureReference => _privateTextureReference;

        /// <summary>
        /// 暴露隐藏字段供测试断言。
        /// </summary>
        public string HiddenLabel => _hiddenLabel;

        /// <summary>
        /// 暴露 readonly 字段供测试断言。
        /// </summary>
        public string ReadonlyLabel => _readonlyLabel;
    }
}
