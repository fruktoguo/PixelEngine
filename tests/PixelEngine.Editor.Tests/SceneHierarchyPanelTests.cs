using PixelEngine.Scripting;
using Xunit;
using ScriptScene = PixelEngine.Scripting.Scene;

namespace PixelEngine.Editor.Tests;

/// <summary>
/// 场景层级面板测试。
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
        Assert.Equal(1, item.ComponentCount);
        Assert.Empty(snapshot.Bodies);
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

        bool entitySelected = panel.SelectEntity("script:1", selection);
        bool bodySelected = panel.SelectBody(7, selection);

        Assert.True(entitySelected);
        Assert.Equal("script:1", selection.EntityHandle);
        Assert.True(bodySelected);
        Assert.Equal(7, selection.BodyId);
        Assert.Equal([(12.5f, 18.25f)], focus.Points);
    }

    private sealed class HierarchyBehaviour : Behaviour
    {
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
