using System.Reflection;
using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// Scripting 项目的最小程序集加载冒烟测试。
/// </summary>
public sealed class SmokeTests
{
    /// <summary>
    /// 验证项目程序集可以加载，并且运行时可以枚举其中的类型。
    /// </summary>
    [Fact]
    public void ProjectAssemblyCanBeLoadedAndTypesEnumerated()
    {
        Assembly assembly = Assembly.Load("PixelEngine.Scripting");
        Type[] types = assembly.GetTypes();

        Assert.Equal("PixelEngine.Scripting", assembly.GetName().Name);
        Assert.NotNull(types);
        Assert.Contains(types, type => type == typeof(Behaviour));
        Assert.Contains(types, type => type == typeof(IScriptContext));
    }

    /// <summary>
    /// 验证脚本 Scene 能创建实体并按类型管理组件。
    /// </summary>
    [Fact]
    public void SceneStoresComponentsByEntity()
    {
        Scene scene = new();
        Entity entity = scene.CreateEntity();

        TestComponent component = entity.AddComponent<TestComponent>();
        component.Value = 42;

        Assert.Equal(1, scene.EntityCount);
        Assert.True(entity.TryGetComponent(out TestComponent loaded));
        Assert.Same(component, loaded);
        Assert.Equal(entity, component.Entity);
        Assert.Equal(42, loaded.Value);

        entity.RemoveComponent<TestComponent>();
        Assert.False(entity.TryGetComponent<TestComponent>(out _));

        entity.Destroy();
        Assert.Equal(0, scene.EntityCount);
        _ = Assert.Throws<InvalidOperationException>(entity.AddComponent<TestComponent>);
    }

    /// <summary>
    /// 验证脚本系统按注册顺序派发。
    /// </summary>
    [Fact]
    public void SceneDispatchesSystemsInRegistrationOrder()
    {
        Scene scene = new();
        List<string> events = [];
        scene.RegisterSystem(new RecordingSystem("a", events));
        scene.RegisterSystem(new RecordingSystem("b", events));

        scene.DispatchFrameSystems(new FakeScriptContext(scene), 0.016f);
        scene.DispatchSimSystems(new FakeScriptContext(scene));

        Assert.Equal(["frame:a", "frame:b", "sim:a", "sim:b"], events);
    }

    private sealed class TestComponent : Behaviour
    {
        public int Value { get; set; }
    }

    private sealed class RecordingSystem(string name, List<string> events) : ISystem
    {
        public void OnSimTick(IScriptContext context)
        {
            events.Add($"sim:{name}");
        }

        public void OnFrame(IScriptContext context, float dt)
        {
            events.Add($"frame:{name}");
        }
    }

    private sealed class FakeScriptContext(Scene scene) : IScriptContext
    {
        public IWorldCellAccess Cells => throw new NotSupportedException();

        public IMaterialQuery Materials => throw new NotSupportedException();

        public IParticleSpawner Particles => throw new NotSupportedException();

        public ISolidSampler Solids => throw new NotSupportedException();

        public IRigidBodyApi Bodies => throw new NotSupportedException();

        public ICharacterController Character => throw new NotSupportedException();

        public ICameraApi Camera => throw new NotSupportedException();

        public IInputApi Input => throw new NotSupportedException();

        public IEventBus Events => throw new NotSupportedException();

        public IAudioApi Audio => throw new NotSupportedException();

        public IGameTime Time => throw new NotSupportedException();

        public Scene Scene { get; } = scene;
    }
}
