using System.Numerics;
using PixelEngine.Scripting;
using PixelEngine.Simulation;
using Xunit;
using RangeAttribute = PixelEngine.Scripting.RangeAttribute;

namespace PixelEngine.Editor.Tests;

/// <summary>
/// 脚本 Inspector 面板测试。
/// 不变式：Inspector 面板与脚本反射字段同步。
/// </summary>
public sealed class ScriptInspectorPanelTests
{
    /// <summary>
    /// 验证面板从脚本 Scene 捕获实体与 Behaviour 组件快照。
    /// </summary>
    [Fact]
    public void RefreshCapturesScriptEntitiesAndBehaviours()
    {
        Scene scene = new();
        Entity entity = scene.CreateEntity();
        _ = entity.AddComponent<InspectableBehaviour>();
        ScriptInspectorPanel panel = new(scene);

        ScriptEntityInspection[] snapshot = panel.Refresh();

        ScriptEntityInspection inspected = Assert.Single(snapshot);
        Assert.Equal(entity.Id, inspected.EntityId);
        Assert.Equal($"script:{entity.Id}", inspected.Handle);
        ScriptComponentInspection component = Assert.Single(inspected.Components);
        Assert.Contains(nameof(InspectableBehaviour), component.TypeName, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证面板能写回基础类型、向量、枚举和材质引用字段。
    /// </summary>
    [Fact]
    public void TrySetFieldValueSupportsBasicVectorEnumAndMaterialFields()
    {
        Scene scene = new();
        Entity entity = scene.CreateEntity();
        InspectableBehaviour behaviour = entity.AddComponent<InspectableBehaviour>();
        ScriptInspectorPanel panel = new(scene, CreateMaterials());
        string handle = $"script:{entity.Id}";

        Assert.True(panel.TrySetFieldValue(handle, 0, nameof(InspectableBehaviour.EnabledFlag), false));
        Assert.True(panel.TrySetFieldValue(handle, 0, nameof(InspectableBehaviour.Count), 6));
        Assert.True(panel.TrySetFieldValue(handle, 0, nameof(InspectableBehaviour.Speed), 2.5f));
        Assert.True(panel.TrySetFieldValue(handle, 0, nameof(InspectableBehaviour.Label), "edited"));
        Assert.True(panel.TrySetFieldValue(handle, 0, nameof(InspectableBehaviour.Position), new Vector2(3f, 4f)));
        Assert.True(panel.TrySetFieldValue(handle, 0, nameof(InspectableBehaviour.Mode), "Run"));
        Assert.True(panel.TrySetFieldValue(handle, 0, nameof(InspectableBehaviour.Material), "sand"));

        Assert.False(behaviour.EnabledFlag);
        Assert.Equal(6, behaviour.Count);
        Assert.Equal(2.5f, behaviour.Speed);
        Assert.Equal("edited", behaviour.Label);
        Assert.Equal(new Vector2(3f, 4f), behaviour.Position);
        Assert.Equal(TestMode.Run, behaviour.Mode);
        Assert.Equal(new MaterialId(1), behaviour.Material);
    }

    /// <summary>
    /// 验证范围字段拒绝超出 Inspector Range 的写入。
    /// </summary>
    [Fact]
    public void TrySetFieldValueRejectsOutOfRangeSliderValue()
    {
        Scene scene = new();
        Entity entity = scene.CreateEntity();
        InspectableBehaviour behaviour = entity.AddComponent<InspectableBehaviour>();
        ScriptInspectorPanel panel = new(scene);
        string handle = $"script:{entity.Id}";

        Assert.False(panel.TrySetFieldValue(handle, 0, nameof(InspectableBehaviour.Count), 99));
        Assert.Equal(3, behaviour.Count);
    }

    /// <summary>
    /// 验证 Inspector 热重载按钮调用配置的热重载入口并更新状态。
    /// </summary>
    [Fact]
    public void TriggerHotReloadUsesConfiguredReloadEntry()
    {
        Scene scene = new();
        RecordingHotReload reload = new();
        ScriptInspectorPanel panel = new(scene, hotReload: reload);

        ScriptInspectorHotReloadResult result = panel.TriggerHotReload();

        Assert.True(result.Success);
        Assert.Equal(1, reload.CallCount);
        Assert.Equal("reloaded", panel.Status);
    }

    private static MaterialTable CreateMaterials()
    {
        return new MaterialTable(
        [
            new MaterialDef { Id = 0, Name = "empty", Type = CellType.Empty, HeatCapacity = 1f, TextureId = -1 },
            new MaterialDef { Id = 1, Name = "sand", Type = CellType.Powder, HeatCapacity = 1f, TextureId = -1 },
        ]);
    }

    private sealed class InspectableBehaviour : Behaviour
    {
        public bool EnabledFlag = true;

        [Range(0, 10)]
        public int Count = 3;

        public float Speed = 1f;

        public string Label = "start";

        public Vector2 Position = new(1f, 2f);

        public TestMode Mode = TestMode.Idle;

        public MaterialId Material = new(0);
    }

    private enum TestMode
    {
        Idle,
        Run,
    }

    private sealed class RecordingHotReload : IScriptInspectorHotReload
    {
        public int CallCount { get; private set; }

        public bool CanReload => true;

        public ScriptInspectorHotReloadResult ReloadNow()
        {
            CallCount++;
            return new ScriptInspectorHotReloadResult(true, "reloaded", []);
        }
    }
}
