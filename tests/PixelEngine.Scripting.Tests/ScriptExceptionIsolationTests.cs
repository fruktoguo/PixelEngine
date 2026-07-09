using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// 验证脚本异常隔离不会让 Scene 派发路径崩溃，并会记录诊断与跳过故障脚本。
/// 不变式：脚本异常被隔离记录、Scene 派发路径不崩溃。
/// </summary>
public sealed class ScriptExceptionIsolationTests
{
    /// <summary>
    /// 验证 Behaviour 在 OnUpdate 抛异常时会被标记 Faulted 且禁用，诊断记录保留，健康脚本继续逐帧执行。
    /// </summary>
    [Fact]
    public void BehaviourUpdateExceptionIsReportedAndDoesNotStopHealthyBehaviours()
    {
        // Arrange：准备输入与初始状态
        RecordingDiagnostics diagnostics = new();
        Scene scene = new(diagnostics);
        FakeScriptContext context = new(scene, new FakeGameTime(101));
        ThrowingUpdateBehaviour faulted = scene.CreateEntity().AddComponent<ThrowingUpdateBehaviour>();
        CountingUpdateBehaviour healthy = scene.CreateEntity().AddComponent<CountingUpdateBehaviour>();

        Exception? firstDispatch = Record.Exception(() => scene.DispatchUpdate(context, 0.016f));
        Exception? secondDispatch = Record.Exception(() => scene.DispatchUpdate(context, 0.016f));

        // Assert：验证预期结果
        Assert.Null(firstDispatch);
        Assert.Null(secondDispatch);
        Assert.True(faulted.Faulted);
        Assert.False(faulted.Enabled);
        _ = Assert.IsType<InvalidOperationException>(faulted.LastException);
        Assert.Equal(2, healthy.UpdateCount);
        Assert.Equal(1, scene.ScriptExceptionCount);
        ScriptExceptionRecord record = Assert.Single(diagnostics.Records);
        Assert.Equal(typeof(ThrowingUpdateBehaviour).FullName, record.ScriptType);
        Assert.Equal("OnUpdate", record.Callback);
        Assert.Equal(typeof(InvalidOperationException).FullName, record.ExceptionType);
        Assert.Equal("behaviour boom", record.Message);
        Assert.Equal(101, record.FrameIndex);
    }

    /// <summary>
    /// 验证 ISystem.OnFrame 抛异常时同帧后续系统继续执行，后续帧不再重复上报同一故障系统。
    /// </summary>
    [Fact]
    public void FrameSystemExceptionIsReportedOnceAndDoesNotStopLaterSystems()
    {
        // Arrange：准备输入与初始状态
        RecordingDiagnostics diagnostics = new();
        Scene scene = new(diagnostics);
        List<string> events = [];
        scene.RegisterSystem(new ThrowingFrameSystem());
        scene.RegisterSystem(new RecordingSystem("healthy", events));
        FakeScriptContext context = new(scene, new FakeGameTime(202));

        Exception? firstDispatch = Record.Exception(() => scene.DispatchFrameSystems(context, 0.016f));
        Exception? secondDispatch = Record.Exception(() => scene.DispatchFrameSystems(context, 0.016f));

        // Assert：验证预期结果
        Assert.Null(firstDispatch);
        Assert.Null(secondDispatch);
        Assert.Equal(["frame:healthy", "frame:healthy"], events);
        Assert.Equal(1, scene.ScriptExceptionCount);
        ScriptExceptionRecord record = Assert.Single(diagnostics.Records);
        Assert.Equal(typeof(ThrowingFrameSystem).FullName, record.ScriptType);
        Assert.Equal("ISystem.OnFrame", record.Callback);
        Assert.Equal(typeof(InvalidOperationException).FullName, record.ExceptionType);
        Assert.Equal("frame boom", record.Message);
        Assert.Equal(202, record.FrameIndex);
    }

    /// <summary>
    /// 验证 ISystem.OnSimTick 抛异常时固定步系统派发同样隔离故障系统，并保持后续系统可运行。
    /// </summary>
    [Fact]
    public void SimSystemExceptionIsReportedOnceAndDoesNotStopLaterSystems()
    {
        // Arrange：准备输入与初始状态
        RecordingDiagnostics diagnostics = new();
        Scene scene = new(diagnostics);
        List<string> events = [];
        scene.RegisterSystem(new ThrowingSimSystem());
        scene.RegisterSystem(new RecordingSystem("healthy", events));
        FakeScriptContext context = new(scene, new FakeGameTime(303));

        Exception? firstDispatch = Record.Exception(() => scene.DispatchSimSystems(context));
        Exception? secondDispatch = Record.Exception(() => scene.DispatchSimSystems(context));

        // Assert：验证预期结果
        Assert.Null(firstDispatch);
        Assert.Null(secondDispatch);
        Assert.Equal(["sim:healthy", "sim:healthy"], events);
        Assert.Equal(1, scene.ScriptExceptionCount);
        ScriptExceptionRecord record = Assert.Single(diagnostics.Records);
        Assert.Equal(typeof(ThrowingSimSystem).FullName, record.ScriptType);
        Assert.Equal("ISystem.OnSimTick", record.Callback);
        Assert.Equal(typeof(InvalidOperationException).FullName, record.ExceptionType);
        Assert.Equal("sim boom", record.Message);
        Assert.Equal(303, record.FrameIndex);
    }

    private sealed class ThrowingUpdateBehaviour : Behaviour
    {
        protected override void OnUpdate(float dt)
        {
            throw new InvalidOperationException("behaviour boom");
        }
    }

    private sealed class CountingUpdateBehaviour : Behaviour
    {
        public int UpdateCount { get; private set; }

        protected override void OnUpdate(float dt)
        {
            UpdateCount++;
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

    private sealed class ThrowingFrameSystem : ISystem
    {
        public void OnSimTick(IScriptContext context)
        {
        }

        public void OnFrame(IScriptContext context, float dt)
        {
            throw new InvalidOperationException("frame boom");
        }
    }

    private sealed class ThrowingSimSystem : ISystem
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

    private sealed class FakeScriptContext(Scene scene, IGameTime time) : IScriptContext
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

        public IGameTime Time { get; } = time;

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
}
