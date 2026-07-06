using System.Numerics;
using PixelEngine.Editor.Shell;
using PixelEngine.Scripting;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 独立编辑器 authoring 场景物化与命令栈测试。
/// </summary>
public sealed class EditorShellSceneMaterializationTests
{
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
    }
}
