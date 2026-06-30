using PixelEngine.Core.Events;
using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// 脚本事件总线适配测试。
/// </summary>
public sealed class ScriptEventBusTests
{
    /// <summary>
    /// 验证脚本事件订阅在相位 1 drain 时分发，并且释放订阅后不再调用。
    /// </summary>
    [Fact]
    public void ScriptEventBusDrainsSubscribedCoreEvents()
    {
        EventBus coreEvents = new(capacityPerChannel: 8);
        using ScriptEventBus scriptEvents = new(coreEvents);
        List<int> values = [];
        using IDisposable subscription = scriptEvents.Subscribe<TestEvent>(item => values.Add(item.Value));

        Assert.True(coreEvents.Channel<TestEvent>().TryEnqueue(new TestEvent(7)));
        scriptEvents.DrainEvents();
        subscription.Dispose();
        Assert.True(coreEvents.Channel<TestEvent>().TryEnqueue(new TestEvent(11)));
        scriptEvents.DrainEvents();

        Assert.Equal([7], values);
    }

    /// <summary>
    /// 验证 ScriptRuntime.Update 会在 Behaviour OnUpdate 前排空脚本事件。
    /// </summary>
    [Fact]
    public void ScriptRuntimeDrainsEventsBeforeUpdateCallbacks()
    {
        EventBus coreEvents = new(capacityPerChannel: 8);
        using ScriptEventBus scriptEvents = new(coreEvents);
        Scene scene = new();
        FakeScriptContext context = new(scene, scriptEvents);
        EventAwareBehaviour behaviour = scene.CreateEntity().AddComponent<EventAwareBehaviour>();
        List<string> events = [];
        behaviour.Events = events;
        _ = scriptEvents.Subscribe<TestEvent>(item => events.Add($"event:{item.Value}"));
        ScriptRuntime runtime = new();
        runtime.Initialize(context);

        Assert.True(coreEvents.Channel<TestEvent>().TryEnqueue(new TestEvent(3)));
        runtime.BeginFrame();
        runtime.Update(0.016f);

        Assert.Equal(["event:3", "update"], events);
    }

    private readonly struct TestEvent(int value)
    {
        public int Value { get; } = value;
    }

    private sealed class EventAwareBehaviour : Behaviour
    {
        public List<string> Events { get; set; } = [];

        protected override void OnUpdate(float dt)
        {
            Events.Add("update");
        }
    }

    private sealed class FakeScriptContext(Scene scene, IEventBus events) : IScriptContext
    {
        public IWorldCellAccess Cells => throw new NotSupportedException();

        public IMaterialQuery Materials => throw new NotSupportedException();

        public IParticleSpawner Particles => throw new NotSupportedException();

        public ISolidSampler Solids => throw new NotSupportedException();

        public IRigidBodyApi Bodies => throw new NotSupportedException();

        public ICharacterController Character => throw new NotSupportedException();

        public ICameraApi Camera => throw new NotSupportedException();

        public IInputApi Input => throw new NotSupportedException();

        public IEventBus Events { get; } = events;

        public IAudioApi Audio => throw new NotSupportedException();

        public IGameTime Time => throw new NotSupportedException();

        public Scene Scene { get; } = scene;
    }
}
