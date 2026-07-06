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
