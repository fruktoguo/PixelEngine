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
        scene.FlushDestroyed(new FakeScriptContext(scene));
        Assert.Equal(0, scene.EntityCount);
        _ = Assert.Throws<InvalidOperationException>(entity.AddComponent<TestComponent>);
        Assert.Equal(entity.Id, scene.CreateEntity().Id);
    }

    /// <summary>
    /// 验证 Behaviour 生命周期由 Scene 按相位 1 语义派发。
    /// </summary>
    [Fact]
    public void SceneDispatchesBehaviourLifecycle()
    {
        Scene scene = new();
        FakeScriptContext context = new(scene);
        Entity entity = scene.CreateEntity();
        LifecycleComponent component = entity.AddComponent<LifecycleComponent>();
        List<string> events = [];
        component.Events = events;

        scene.DispatchStart(context);
        scene.DispatchStart(context);
        scene.DispatchUpdate(context, 0.016f);
        scene.DispatchFixedSimTick(context);
        component.Enabled = false;
        scene.DispatchUpdate(context, 0.016f);
        scene.DispatchFixedSimTick(context);
        entity.Destroy();
        scene.FlushDestroyed(context);

        Assert.Equal(["start", "update", "fixed", "destroy"], events);
        Assert.Equal(0, scene.EntityCount);
    }

    /// <summary>
    /// 验证单个 Behaviour 抛异常会被隔离，不影响其它脚本。
    /// </summary>
    [Fact]
    public void SceneIsolatesFaultedBehaviourCallbacks()
    {
        Scene scene = new();
        FakeScriptContext context = new(scene);
        Entity failingEntity = scene.CreateEntity();
        Entity healthyEntity = scene.CreateEntity();
        FailingComponent failing = failingEntity.AddComponent<FailingComponent>();
        LifecycleComponent healthy = healthyEntity.AddComponent<LifecycleComponent>();
        List<string> events = [];
        healthy.Events = events;

        scene.DispatchUpdate(context, 0.016f);
        scene.DispatchUpdate(context, 0.016f);

        Assert.True(failing.Faulted);
        Assert.False(failing.Enabled);
        Assert.NotNull(failing.LastException);
        Assert.Equal(1, scene.ScriptExceptionCount);
        Assert.Equal(["update", "update"], events);

        failing.ResetFault();
        Assert.False(failing.Faulted);
        Assert.True(failing.Enabled);
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

    private sealed class LifecycleComponent : Behaviour
    {
        public List<string> Events { get; set; } = [];

        protected override void OnStart()
        {
            Events.Add("start");
        }

        protected override void OnUpdate(float dt)
        {
            Events.Add("update");
        }

        protected override void OnFixedSimTick()
        {
            Events.Add("fixed");
        }

        protected override void OnDestroy()
        {
            Events.Add("destroy");
        }
    }

    private sealed class FailingComponent : Behaviour
    {
        protected override void OnUpdate(float dt)
        {
            throw new InvalidOperationException("boom");
        }
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
