using System.Reflection;
using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// Scripting 项目的最小程序集加载冒烟测试。
/// 不变式：测试程序集可加载、最小冒烟路径不抛异常。
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
        // Arrange：准备输入与初始状态
        Scene scene = new();
        Entity entity = scene.CreateEntity();

        TestComponent component = entity.AddComponent<TestComponent>();
        IComponent dynamicComponent = entity.AddComponent(typeof(DynamicComponent));
        component.Value = 42;

        // Assert：验证预期结果
        Assert.Equal(1, scene.EntityCount);
        Assert.True(entity.TryGetComponent(out TestComponent loaded));
        Assert.True(entity.TryGetComponent(out DynamicComponent loadedDynamic));
        Assert.Same(component, loaded);
        Assert.Same(dynamicComponent, loadedDynamic);
        Assert.Equal(entity, component.Entity);
        Assert.Equal(entity, loadedDynamic.Entity);
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
    /// 验证 Play Session 恢复会重建 Behaviour，且运行时类型 bucket 仍可接受后续泛型添加。
    /// </summary>
    [Fact]
    public void PlaySessionRestoreRecreatesBehaviourAndPreservesGenericBucketInterop()
    {
        Scene scene = new();
        Entity originalEntity = scene.CreateEntity();
        TestComponent original = originalEntity.AddComponent<TestComponent>();
        original.Value = 7;
        ScriptPlaySessionSnapshot snapshot = scene.CapturePlaySessionSnapshot();
        original.Value = 99;

        scene.RestorePlaySessionSnapshot(snapshot);

        Assert.True(originalEntity.TryGetComponent(out TestComponent restored));
        Assert.NotSame(original, restored);
        Assert.Equal(7, restored.Value);
        Entity addedEntity = scene.CreateEntity();
        TestComponent added = addedEntity.AddComponent<TestComponent>();
        Assert.Same(added, AssertComponent<TestComponent>(addedEntity));
    }

    /// <summary>
    /// 验证 Behaviour 生命周期由 Scene 按相位 1 语义派发。
    /// </summary>
    [Fact]
    public void SceneDispatchesBehaviourLifecycle()
    {
        // Arrange：准备输入与初始状态
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

        // Assert：验证预期结果
        Assert.Equal(["start", "update", "fixed", "destroy"], events);
        Assert.Equal(0, scene.EntityCount);
    }

    /// <summary>
    /// 验证单个 Behaviour 抛异常会被隔离，不影响其它脚本。
    /// </summary>
    [Fact]
    public void SceneIsolatesFaultedBehaviourCallbacks()
    {
        // Arrange：准备输入与初始状态
        RecordingDiagnostics diagnostics = new();
        Scene scene = new(diagnostics);
        FakeScriptContext context = new(scene, new FakeGameTime(123));
        Entity failingEntity = scene.CreateEntity();
        Entity healthyEntity = scene.CreateEntity();
        FailingComponent failing = failingEntity.AddComponent<FailingComponent>();
        LifecycleComponent healthy = healthyEntity.AddComponent<LifecycleComponent>();
        List<string> events = [];
        healthy.Events = events;

        scene.DispatchUpdate(context, 0.016f);
        scene.DispatchUpdate(context, 0.016f);

        // Assert：验证预期结果
        Assert.True(failing.Faulted);
        Assert.False(failing.Enabled);
        Assert.NotNull(failing.LastException);
        Assert.Equal(1, scene.ScriptExceptionCount);
        Assert.Equal(["update", "update"], events);
        ScriptExceptionRecord record = diagnostics.Records[0];
        Assert.Equal(typeof(FailingComponent).FullName, record.ScriptType);
        Assert.Equal("OnUpdate", record.Callback);
        Assert.Equal(typeof(InvalidOperationException).FullName, record.ExceptionType);
        Assert.Equal("boom", record.Message);
        Assert.Equal(123, record.FrameIndex);

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

    /// <summary>
    /// 验证 ISystem 抛异常会被隔离，不影响同帧后续系统，且后续帧不重复刷异常。
    /// </summary>
    [Fact]
    public void SceneIsolatesFaultedSystemCallbacks()
    {
        // Arrange：准备输入与初始状态
        RecordingDiagnostics diagnostics = new();
        Scene scene = new(diagnostics);
        List<string> events = [];
        scene.RegisterSystem(new FailingFrameSystem());
        scene.RegisterSystem(new RecordingSystem("healthy", events));
        FakeScriptContext context = new(scene, new FakeGameTime(456));

        scene.DispatchFrameSystems(context, 0.016f);
        scene.DispatchFrameSystems(context, 0.016f);

        // Assert：验证预期结果
        Assert.Equal(["frame:healthy", "frame:healthy"], events);
        Assert.Equal(1, scene.ScriptExceptionCount);
        ScriptExceptionRecord record = diagnostics.Records[0];
        Assert.Equal(typeof(FailingFrameSystem).FullName, record.ScriptType);
        Assert.Equal("ISystem.OnFrame", record.Callback);
        Assert.Equal(typeof(InvalidOperationException).FullName, record.ExceptionType);
        Assert.Equal("frame boom", record.Message);
        Assert.Equal(456, record.FrameIndex);
    }

    /// <summary>
    /// 验证固定 sim tick 系统异常同样被隔离。
    /// </summary>
    [Fact]
    public void SceneIsolatesFaultedSimSystemCallbacks()
    {
        // Arrange：准备输入与初始状态
        RecordingDiagnostics diagnostics = new();
        Scene scene = new(diagnostics);
        List<string> events = [];
        scene.RegisterSystem(new FailingSimSystem());
        scene.RegisterSystem(new RecordingSystem("healthy", events));
        FakeScriptContext context = new(scene, new FakeGameTime(789));

        scene.DispatchSimSystems(context);
        scene.DispatchSimSystems(context);

        // Assert：验证预期结果
        Assert.Equal(["sim:healthy", "sim:healthy"], events);
        Assert.Equal(1, scene.ScriptExceptionCount);
        ScriptExceptionRecord record = diagnostics.Records[0];
        Assert.Equal(typeof(FailingSimSystem).FullName, record.ScriptType);
        Assert.Equal("ISystem.OnSimTick", record.Callback);
        Assert.Equal(typeof(InvalidOperationException).FullName, record.ExceptionType);
        Assert.Equal("sim boom", record.Message);
        Assert.Equal(789, record.FrameIndex);
    }

    private sealed class TestComponent : Behaviour
    {
        public int Value { get; set; }
    }

    private static TComponent AssertComponent<TComponent>(Entity entity)
        where TComponent : class, IComponent
    {
        Assert.True(entity.TryGetComponent(out TComponent component));
        return component;
    }

    private sealed class DynamicComponent : Behaviour
    {
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

    private sealed class FailingFrameSystem : ISystem
    {
        public void OnSimTick(IScriptContext context)
        {
        }

        public void OnFrame(IScriptContext context, float dt)
        {
            throw new InvalidOperationException("frame boom");
        }
    }

    private sealed class FailingSimSystem : ISystem
    {
        public void OnSimTick(IScriptContext context)
        {
            throw new InvalidOperationException("sim boom");
        }

        public void OnFrame(IScriptContext context, float dt)
        {
        }
    }

    private sealed class RecordingDiagnostics : IScriptDiagnosticSink
    {
        public List<ScriptExceptionRecord> Records { get; } = [];

        public void ReportScriptException(in ScriptExceptionRecord record)
        {
            Records.Add(record);
        }
    }

    private sealed class FakeScriptContext(Scene scene, IGameTime? time = null) : IScriptContext
    {
        public IWorldCellAccess Cells => throw new NotSupportedException();

        public IWorldEffects World => throw new NotSupportedException();

        public IMaterialQuery Materials => throw new NotSupportedException();

        public IParticleSpawner Particles => throw new NotSupportedException();

        public ISolidSampler Solids => throw new NotSupportedException();

        public IRigidBodyApi Bodies => throw new NotSupportedException();

        public ICharacterController Character => throw new NotSupportedException();

        public ICameraApi Camera => throw new NotSupportedException();

        public IInputApi Input => throw new NotSupportedException();

        public ILightingApi Lighting => throw new NotSupportedException();

        public IDiagnosticsApi Diagnostics => throw new NotSupportedException();

        public IEventBus Events => throw new NotSupportedException();

        public IAudioApi Audio => throw new NotSupportedException();

        public IGameTime Time { get; } = time ?? new UnsupportedGameTime();

        public Scene Scene { get; } = scene;
    }

    private sealed class FakeGameTime(long frameCount) : IGameTime
    {
        public float DeltaTime => 0.016f;

        public float FixedStep => 1f / 60f;

        public long FrameCount { get; } = frameCount;

        public float TimeScale => 1f;

        public bool SimSteppedThisFrame => true;
    }

    private sealed class UnsupportedGameTime : IGameTime
    {
        public float DeltaTime => throw new NotSupportedException();

        public float FixedStep => throw new NotSupportedException();

        public long FrameCount => throw new NotSupportedException();

        public float TimeScale => throw new NotSupportedException();

        public bool SimSteppedThisFrame => throw new NotSupportedException();
    }
}
