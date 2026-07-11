using PixelEngine.Scripting;
using Xunit;
using ScriptScene = PixelEngine.Scripting.Scene;

namespace PixelEngine.Editor.Tests;

/// <summary>
/// 场景层级面板测试。
/// 不变式：层级树与场景实体一一对应。
/// </summary>
public sealed class SceneHierarchyPanelTests
{
    /// <summary>
    /// 验证运行时层级数据源可从脚本 Scene 枚举实体。
    /// </summary>
    [Fact]
    public void RuntimeDataSourceCapturesScriptEntities()
    {
        ScriptScene scene = new();
        Entity entity = scene.CreateEntity();
        _ = entity.AddComponent<HierarchyBehaviour>();
        RuntimeSceneHierarchyDataSource source = new(scene);

        SceneHierarchySnapshot snapshot = source.Capture();

        SceneHierarchyEntityItem item = Assert.Single(snapshot.Entities);
        Assert.Equal($"script:{entity.Id}", item.Handle);
        Assert.Equal($"HierarchyBehaviour · Entity {entity.Id}", item.DisplayName);
        Assert.Equal(1, item.ComponentCount);
        Assert.Empty(snapshot.Bodies);
    }

    /// <summary>
    /// 验证动态 provider 在 authoring projection 被替换后读取新 Scene，且 runtime Inspector 的
    /// Transform/Behaviour 临时修改可完整恢复。
    /// </summary>
    [Fact]
    public void RuntimeDataSourceFollowsCurrentSceneAndRestoresTemporaryEdits()
    {
        ScriptScene first = new();
        Entity firstEntity = first.CreateEntity();
        Transform firstTransform = firstEntity.AddComponent<Transform>();
        firstTransform.SetPosition(10f, 20f);
        EditableHierarchyBehaviour firstBehaviour = firstEntity.AddComponent<EditableHierarchyBehaviour>();
        firstBehaviour.Speed = 3f;
        ScriptScene current = first;
        RuntimeSceneHierarchyDataSource source = RuntimeSceneHierarchyDataSource.CreateDynamic(() => current);

        Assert.True(source.TrySetEntityTransform($"script:{firstEntity.Id}", 40f, 50f, 0.25f, 2f, 3f));
        Assert.True(source.TrySetBehaviourField($"script:{firstEntity.Id}", 0, nameof(EditableHierarchyBehaviour.Speed), 9f));
        Assert.Equal(40f, firstTransform.X);
        Assert.Equal(9f, firstBehaviour.Speed);

        source.RestoreTemporaryEdits();
        Assert.Equal(10f, firstTransform.X);
        Assert.Equal(20f, firstTransform.Y);
        Assert.Equal(3f, firstBehaviour.Speed);

        ScriptScene replacement = new();
        _ = replacement.CreateEntity().AddComponent<HierarchyBehaviour>();
        _ = replacement.CreateEntity().AddComponent<HierarchyBehaviour>();
        current = replacement;

        Assert.Equal(2, source.Capture().Entities.Count);
    }

    /// <summary>
    /// 验证层级面板选择实体和刚体时联动 EditorSelection 与视口聚焦。
    /// </summary>
    [Fact]
    public void SceneHierarchyPanelSelectsEntityAndBody()
    {
        RecordingHierarchySource source = new(new SceneHierarchySnapshot(
            [new SceneHierarchyEntityItem("script:1", "Entity 1", 2)],
            [new SceneHierarchyBodyItem(7, "Body 7", 12.5f, 18.25f)]));
        RecordingFocus focus = new();
        SceneHierarchyPanel panel = new(source, focus);
        EditorSelection selection = new();
        selection.SelectFolder("levels");

        bool entitySelected = panel.SelectEntity("script:1", selection);

        Assert.True(entitySelected);
        Assert.Equal("script:1", selection.EntityHandle);
        Assert.Null(selection.AssetPath);
        Assert.Null(selection.FolderPath);
        Assert.Null(selection.BodyId);

        selection.SelectFolder("levels");
        bool bodySelected = panel.SelectBody(7, selection);

        Assert.True(bodySelected);
        Assert.Equal(7, selection.BodyId);
        Assert.Null(selection.AssetPath);
        Assert.Null(selection.FolderPath);
        Assert.Null(selection.EntityHandle);
        Assert.Null(selection.GameObjectStableId);
        Assert.Equal([(12.5f, 18.25f)], focus.Points);
    }

    private sealed class HierarchyBehaviour : Behaviour
    {
    }

    private sealed class EditableHierarchyBehaviour : Behaviour
    {
        public float Speed { get; set; }
    }

    private sealed class RecordingHierarchySource(SceneHierarchySnapshot snapshot) : ISceneHierarchyDataSource
    {
        public SceneHierarchySnapshot Capture()
        {
            return snapshot;
        }
    }

    private sealed class RecordingFocus : IViewportFocusService
    {
        public List<(float X, float Y)> Points { get; } = [];

        public void Focus(float worldX, float worldY)
        {
            Points.Add((worldX, worldY));
        }
    }
}
