using PixelEngine.Core;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Core.Events;
using PixelEngine.Core.Threading;
using PixelEngine.Core.Time;
using PixelEngine.Rendering;
using PixelEngine.UI;
using System.Runtime;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// EngineBuilder、EngineContext 与 Engine 生命周期测试。
/// </summary>
public sealed class EngineBuilderTests
{
    /// <summary>
    /// 验证 builder 能装配 Core 服务并把配置写入 EngineContext。
    /// </summary>
    [Fact]
    public void BuildCreatesEngineContextWithCoreServices()
    {
        using Engine engine = new EngineBuilder()
            .WithWindow(1920, 1080)
            .WithInternalResolution(960, 540)
            .WithWorkerCount(1)
            .WithGcMode(EngineGcMode.SustainedLowLatency)
            .WithContentRoot("content-test")
            .WithStartScene("scenes/start.scene")
            .WithEventCapacityPerChannel(64)
            .UseVSync(false)
            .Build();

        EngineContext context = engine.Context;
        Assert.Equal(EngineRunState.Created, engine.State);
        Assert.Equal(1920, context.Options.WindowWidth);
        Assert.Equal(1080, context.Options.WindowHeight);
        Assert.Equal(960, context.Options.InternalWidth);
        Assert.Equal(540, context.Options.InternalHeight);
        Assert.Equal(EngineGcMode.SustainedLowLatency, context.Options.GcMode);
        Assert.Equal("content-test", context.Options.ContentRoot);
        Assert.Equal("scenes/start.scene", context.Options.StartScene);
        Assert.False(context.Options.VSync);
        Assert.True(context.Options.EnableGuiRuntime);
        Assert.False(context.Options.EnableGameUi);
        Assert.False(context.Options.PreferComputeSharpBackend);
        Assert.Equal(UiBackendKind.ManagedFallback, context.Options.GameUiBackend);
        Assert.Equal(64, context.Events.CapacityPerChannel);
        Assert.Equal(0, context.Options.NoGcRegionBudgetBytes);
        Assert.Same(context, context.GetService<EngineContext>());
        Assert.Same(context.Jobs, context.GetService<JobSystem>());
        Assert.Same(context.Clock, context.GetService<FrameClock>());
        Assert.Same(context.Events, context.GetService<EventBus>());
        Assert.Same(context.Counters, context.GetService<EngineCounters>());
    }

    /// <summary>
    /// 验证 EngineOptions 保留未显式 ComputeSharp 偏好的公开构造路径，避免破坏外部宿主配置代码。
    /// </summary>
    [Fact]
    public void EngineOptionsLegacyConstructorDefaultsComputeSharpPreferenceOff()
    {
        EngineOptions options = new(
            EngineOptions.DefaultWindowWidth,
            EngineOptions.DefaultWindowHeight,
            EngineOptions.DefaultWindowTitle,
            EngineOptions.DefaultInternalWidth,
            EngineOptions.DefaultInternalHeight,
            workerCount: 0,
            gcMode: EngineGcMode.SustainedLowLatency,
            enableEditor: false,
            headless: false,
            deterministicMode: false,
            enableGpu: true,
            enableGuiRuntime: true,
            enableGameUi: false,
            gameUiBackend: UiBackendKind.ManagedFallback,
            vSync: true,
            contentRoot: "content",
            startScene: null,
            simHz: EngineConstants.DefaultSimHz,
            eventCapacityPerChannel: EngineOptions.DefaultEventCapacityPerChannel,
            noGcRegionBudgetBytes: 0,
            overload: EngineOverloadOptions.CreateDefault());

        Assert.True(options.EnableGpu);
        Assert.False(options.Headless);
        Assert.False(options.PreferComputeSharpBackend);
    }

    /// <summary>
    /// 验证 ComputeSharp 偏好只作为显式配置传入，并受 GPU/headless 门控约束。
    /// </summary>
    [Fact]
    public void PreferComputeSharpBackendWritesOptionsAndRuntimeGatesDisableIt()
    {
        using Engine enabled = new EngineBuilder()
            .WithWorkerCount(1)
            .PreferComputeSharpBackend()
            .Build();

        using Engine gpuDisabled = new EngineBuilder()
            .WithWorkerCount(1)
            .PreferComputeSharpBackend()
            .EnableGpu(false)
            .Build();

        using Engine headless = new EngineBuilder()
            .WithWorkerCount(1)
            .UseHeadless()
            .PreferComputeSharpBackend()
            .Build();

        Assert.True(enabled.Context.Options.PreferComputeSharpBackend);
        Assert.False(gpuDisabled.Context.Options.PreferComputeSharpBackend);
        Assert.False(headless.Context.Options.PreferComputeSharpBackend);
    }

    /// <summary>
    /// 验证宿主可关闭 Hosting 自建脚本 GUI runtime，供独立编辑器保留窗口/上下文所有权。
    /// </summary>
    [Fact]
    public void UseGuiRuntimeWritesOptionsAndHeadlessDisablesIt()
    {
        using Engine disabled = new EngineBuilder()
            .WithWorkerCount(1)
            .UseGuiRuntime(false)
            .Build();

        using Engine headless = new EngineBuilder()
            .WithWorkerCount(1)
            .UseHeadless()
            .UseGuiRuntime()
            .Build();

        Assert.False(disabled.Context.Options.EnableGuiRuntime);
        Assert.False(headless.Context.Options.EnableGuiRuntime);
    }

    /// <summary>
    /// 验证游戏大 UI 配置受 GUI runtime 与 headless 门控约束。
    /// </summary>
    [Fact]
    public void EnableGameUiWritesOptionsAndRuntimeGatesDisableIt()
    {
        using Engine enabled = new EngineBuilder()
            .WithWorkerCount(1)
            .EnableGameUi()
            .UseUiBackend(UiBackendKind.ManagedFallback)
            .Build();

        using Engine guiDisabled = new EngineBuilder()
            .WithWorkerCount(1)
            .EnableGameUi()
            .UseGuiRuntime(false)
            .Build();

        using Engine headless = new EngineBuilder()
            .WithWorkerCount(1)
            .UseHeadless()
            .EnableGameUi()
            .Build();

        Assert.True(enabled.Context.Options.EnableGameUi);
        Assert.Equal(UiBackendKind.ManagedFallback, enabled.Context.Options.GameUiBackend);
        Assert.False(guiDisabled.Context.Options.EnableGameUi);
        Assert.False(headless.Context.Options.EnableGameUi);
    }

    /// <summary>
    /// 验证窗口运行时会记录请求/实际 Game UI 后端，并在 RmlUi 不可用时显式降级。
    /// </summary>
    [Fact]
    public void GameUiBackendSelectionIsRecordedWhenGlSmokeIsEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PIXELENGINE_RENDERING_GL_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine Game UI backend selection smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.Auto,
        });
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .WithContentRoot(Path.Combine(FindRepositoryRoot(), "demo", "PixelEngine.Demo", "content"))
            .EnableGameUi()
            .UseUiBackend(UiBackendKind.RmlUi)
            .Build();

        _ = engine.LoadContentPackage();
        _ = engine.AttachResidentSimulationWorld(64, 64, particleCapacity: 8);
        _ = engine.AttachWindowRuntime(window);

        GameUiBackendSelection selection = engine.Context.GetService<GameUiBackendSelection>();
        Assert.Equal(UiBackendKind.RmlUi, selection.RequestedBackend);
        if (window.Backend == RenderBackend.GlEs30Angle || window.Capabilities.IsGles || window.Capabilities.IsAngle || !RmlUiNativeInfo.TryQuery(out _))
        {
            Assert.Equal(UiBackendKind.ManagedFallback, selection.ActiveBackend);
            Assert.True(selection.UsedFallback);
            Assert.False(string.IsNullOrWhiteSpace(selection.FallbackReason));
            Assert.Contains("ManagedFallback", selection.FallbackReason, StringComparison.Ordinal);
            if (window.Backend == RenderBackend.GlEs30Angle || window.Capabilities.IsGles || window.Capabilities.IsAngle)
            {
                Assert.Contains("GL3 renderer", selection.FallbackReason, StringComparison.Ordinal);
                Assert.Contains("GLES3/ANGLE renderer", selection.FallbackReason, StringComparison.Ordinal);
            }
        }
        else
        {
            Assert.Equal(UiBackendKind.RmlUi, selection.ActiveBackend);
            Assert.False(selection.UsedFallback);
            Assert.Null(selection.FallbackReason);
        }
    }

    /// <summary>
    /// 验证未激活的 Ultralight 可选后端不会伪造实现或崩溃，而是记录原因后回退托管基线。
    /// </summary>
    [Fact]
    public void UltralightGameUiBackendFallsBackToManagedWhenGlSmokeIsEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PIXELENGINE_RENDERING_GL_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine Ultralight fallback smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.Auto,
        });
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .WithContentRoot(Path.Combine(FindRepositoryRoot(), "demo", "PixelEngine.Demo", "content"))
            .EnableGameUi()
            .UseUiBackend(UiBackendKind.Ultralight)
            .Build();

        _ = engine.LoadContentPackage();
        _ = engine.AttachResidentSimulationWorld(64, 64, particleCapacity: 8);
        _ = engine.AttachWindowRuntime(window);

        GameUiBackendSelection selection = engine.Context.GetService<GameUiBackendSelection>();
        Assert.Equal(UiBackendKind.Ultralight, selection.RequestedBackend);
        Assert.Equal(UiBackendKind.ManagedFallback, selection.ActiveBackend);
        Assert.True(selection.UsedFallback);
        Assert.Contains("Ultralight", selection.FallbackReason, StringComparison.Ordinal);
        Assert.Contains("optional profile inactive", selection.FallbackReason, StringComparison.Ordinal);
        Assert.Contains("commercial redistribution license", selection.FallbackReason, StringComparison.Ordinal);
        Assert.Contains("ManagedFallback", selection.FallbackReason, StringComparison.Ordinal);
        Assert.True(engine.Context.TryGetService(out GameUiHost _));
        Assert.True(engine.Context.TryGetService(out GameUiPhaseDriver _));
    }

    /// <summary>
    /// 验证 Ultralight 当前只作为未激活 optional profile 暴露，默认回退保持托管基线。
    /// </summary>
    [Fact]
    public void UltralightOptionalProfileGateDefaultsToInactiveManagedFallback()
    {
        Assert.False(UltralightOptionalProfileGate.IsActive);
        Assert.Equal(UiBackendKind.ManagedFallback, UltralightOptionalProfileGate.FallbackBackend);
        Assert.Contains("Ultralight optional profile inactive", UltralightOptionalProfileGate.InactiveReason, StringComparison.Ordinal);
        Assert.Contains("commercial redistribution license", UltralightOptionalProfileGate.InactiveReason, StringComparison.Ordinal);
        Assert.Contains("runtime surface/JS bridge", UltralightOptionalProfileGate.InactiveReason, StringComparison.Ordinal);
        Assert.Contains("release artifact evidence", UltralightOptionalProfileGate.InactiveReason, StringComparison.Ordinal);
        Assert.Contains("ManagedFallback", UltralightOptionalProfileGate.InactiveReason, StringComparison.Ordinal);

        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .EnableGameUi()
            .UseUiBackend(UiBackendKind.Ultralight)
            .Build();

        Assert.Equal(UiBackendKind.Ultralight, engine.Context.Options.GameUiBackend);
    }

    /// <summary>
    /// 验证禁用游戏大 UI 时，真实窗口运行时不会注册 GameUi 服务、相位 driver 或 UI 计时开销。
    /// </summary>
    [Fact]
    public void DisabledGameUiDoesNotRegisterRuntimeServicesWhenWindowIsAttached()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PIXELENGINE_RENDERING_GL_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine disabled Game UI smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.Auto,
        });
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .WithContentRoot(Path.Combine(FindRepositoryRoot(), "demo", "PixelEngine.Demo", "content"))
            .EnableGameUi(false)
            .Build();

        _ = engine.LoadContentPackage();
        _ = engine.AttachResidentSimulationWorld(64, 64, particleCapacity: 8);
        _ = engine.AttachWindowRuntime(window);
        _ = engine.RunOneTick(realDeltaSeconds: 1.0 / 60.0);

        Assert.False(engine.Context.Options.EnableGameUi);
        Assert.False(engine.Context.TryGetService(out GameUiHost _));
        Assert.False(engine.Context.TryGetService(out GameUiPhaseDriver _));
        Assert.False(engine.Context.TryGetService(out GameUiServiceBridge _));
        Assert.False(engine.Context.TryGetService(out GameUiBackendSelection _));
        Assert.False(engine.Context.TryGetService(out PixelEngine.Scripting.IGameUiService _));
        Assert.Equal(0.0, engine.Context.Counters.UiUpdateMilliseconds);
        Assert.Equal(0.0, engine.Context.Counters.UiCompositeMilliseconds);
        Assert.Equal(0.0, engine.Context.Counters.UiPaintMilliseconds);
        Assert.Equal(0.0, engine.Context.Counters.UiUploadMilliseconds);
    }

    /// <summary>
    /// 验证默认构建会把托管 GC 延迟模式写入 SustainedLowLatency。
    /// </summary>
    [Fact]
    public void BuildAppliesSustainedLowLatencyGcModeByDefault()
    {
        GCLatencyMode original = GCSettings.LatencyMode;
        try
        {
            using Engine engine = new EngineBuilder()
                .WithWorkerCount(1)
                .Build();

            Assert.Equal(EngineGcMode.SustainedLowLatency, engine.Context.Options.GcMode);
            Assert.Equal(GCLatencyMode.SustainedLowLatency, GCSettings.LatencyMode);
        }
        finally
        {
            GCSettings.LatencyMode = original;
        }
    }

    /// <summary>
    /// 验证 headless 与确定性模式会关闭 GPU/Editor 并固定单 worker。
    /// </summary>
    [Fact]
    public void HeadlessDeterministicBuildAppliesRuntimeFlags()
    {
        using Engine engine = new EngineBuilder()
            .UseHeadless()
            .UseDeterministicMode()
            .Build();

        Assert.True(engine.Context.Options.Headless);
        Assert.True(engine.Context.Options.DeterministicMode);
        Assert.False(engine.Context.Options.EnableEditor);
        Assert.False(engine.Context.Options.EnableGpu);
        Assert.False(engine.Context.Options.PreferComputeSharpBackend);
        Assert.Equal(1, engine.Context.Options.WorkerCount);
        Assert.Equal(1, engine.Context.Jobs.WorkerCount);
    }

    /// <summary>
    /// 验证 RunOneTick 只推进一次固定步长时钟，不追补多步。
    /// </summary>
    [Fact]
    public void RunOneTickAdvancesFrameClockWithoutCatchUp()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .WithSimHz(EngineConstants.SimHzDownscaled)
            .Build();

        FrameTiming first = engine.RunOneTick(realDeltaSeconds: 1.0);
        FrameTiming second = engine.RunOneTick(realDeltaSeconds: 1.0);

        Assert.Equal(EngineRunState.Running, engine.State);
        Assert.True(first.RunSim);
        Assert.False(second.RunSim);
        Assert.Equal(1, first.SimTickIndex);
        Assert.Equal(1, second.SimTickIndex);
        Assert.Equal(2, second.FrameIndex);
        Assert.Equal(EngineConstants.SimHzDownscaled, engine.Context.Counters.SimHz);
    }

    /// <summary>
    /// 验证 no-GC region 默认关闭，避免未配置时改变宿主 GC 行为。
    /// </summary>
    [Fact]
    public void NoGcRegionIsDisabledByDefault()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();

        _ = engine.RunOneTick();

        Assert.Equal(0, engine.Context.Options.NoGcRegionBudgetBytes);
        Assert.Equal(0, engine.Context.Counters.NoGcRegionBudgetBytes);
        Assert.Equal(0, engine.Context.Counters.NoGcRegionStartAttempts);
        Assert.False(engine.Context.Counters.NoGcRegionStartedLastFrame);
    }

    /// <summary>
    /// 验证配置预算后，单帧关键段会尝试进入并正常结束 no-GC region。
    /// </summary>
    [Fact]
    public void RunOneTickCanWrapCriticalFrameInNoGcRegion()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .WithNoGcRegionBudget(64 * 1024 * 1024)
            .Build();

        _ = engine.RunOneTick();

        Assert.Equal(64 * 1024 * 1024, engine.Context.Options.NoGcRegionBudgetBytes);
        Assert.Equal(64 * 1024 * 1024, engine.Context.Counters.NoGcRegionBudgetBytes);
        Assert.Equal(1, engine.Context.Counters.NoGcRegionStartAttempts);
        Assert.Equal(0, engine.Context.Counters.NoGcRegionStartFailures);
        Assert.Equal(1, engine.Context.Counters.NoGcRegionSuccessfulFrames);
        Assert.Equal(0, engine.Context.Counters.NoGcRegionEndFailures);
        Assert.True(engine.Context.Counters.NoGcRegionStartedLastFrame);
    }

    /// <summary>
    /// 验证 Shutdown 释放 JobSystem，关闭后禁止继续 tick。
    /// </summary>
    [Fact]
    public void ShutdownDisposesCoreRuntime()
    {
        Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();
        JobSystem jobs = engine.Context.Jobs;

        engine.Shutdown();

        Assert.Equal(EngineRunState.Shutdown, engine.State);
        _ = Assert.Throws<ObjectDisposedException>(() => engine.RunOneTick());
        _ = Assert.Throws<ObjectDisposedException>(() => jobs.ParallelRange(1, 1, static (_, _, _, _) => { }));
        engine.Dispose();
    }

    /// <summary>
    /// 验证 Hosting 子系统按注册顺序初始化，并在 Shutdown 时逆序关闭。
    /// </summary>
    [Fact]
    public void SubsystemsInitializeInOrderAndShutdownInReverseOrder()
    {
        List<string> events = [];
        RecordingSubsystem first = new("first", events);
        RecordingSubsystem second = new("second", events);
        Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .AddSubsystem(first)
            .AddSubsystem(second)
            .Build();

        EngineLifecycle lifecycle = engine.Context.GetService<EngineLifecycle>();

        Assert.Equal(2, lifecycle.Count);
        Assert.Equal(2, lifecycle.InitializedCount);
        Assert.Equal(["init:first", "init:second"], events);

        engine.Shutdown();

        Assert.Equal(
            ["init:first", "init:second", "shutdown:second", "shutdown:first"],
            events);
        Assert.Equal(0, lifecycle.InitializedCount);
        engine.Dispose();
    }

    /// <summary>
    /// 验证后续子系统初始化失败时，已初始化子系统会立即逆序关闭。
    /// </summary>
    [Fact]
    public void BuildRollsBackInitializedSubsystemsWhenInitializationFails()
    {
        List<string> events = [];
        RecordingSubsystem first = new("first", events);
        RecordingSubsystem failing = new("failing", events, failInitialize: true);

        _ = Assert.Throws<InvalidOperationException>(() => new EngineBuilder()
            .WithWorkerCount(1)
            .AddSubsystem(first)
            .AddSubsystem(failing)
            .Build());

        Assert.Equal(
            ["init:first", "init:failing", "shutdown:first"],
            events);
    }

    /// <summary>
    /// 验证 builder 对非法配置快速失败。
    /// </summary>
    [Fact]
    public void BuilderRejectsInvalidConfiguration()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new EngineBuilder().WithWorkerCount(-1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new EngineBuilder().WithWindow(0, 720));
        _ = Assert.Throws<ArgumentException>(() => new EngineBuilder().WithContentRoot(""));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new EngineBuilder().WithNoGcRegionBudget(-1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new EngineBuilder().WithEventCapacityPerChannel(63).Build());
    }

    /// <summary>
    /// 验证 Hosting 只把已有真实后端的服务角色标为可用。
    /// </summary>
    [Fact]
    public void ServiceRolesExposeOnlyRegisteredBackendsAsAvailable()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();

        Assert.True(engine.Context.IsServiceAvailable(EngineServiceRole.EventBus));
        Assert.True(engine.Context.IsServiceAvailable(EngineServiceRole.Diagnostics));
        Assert.False(engine.Context.IsServiceAvailable(EngineServiceRole.WorldAccess));
        Assert.False(engine.Context.IsServiceAvailable(EngineServiceRole.PhysicsService));
        Assert.False(engine.Context.IsServiceAvailable(EngineServiceRole.Scripting));

        EngineServiceAvailability audio = engine.Context.GetServiceAvailability(EngineServiceRole.AudioService);
        Assert.False(audio.Available);
        Assert.Null(audio.ServiceType);
    }

    /// <summary>
    /// 验证角色服务注册会同时进入 typed service 表与能力表。
    /// </summary>
    [Fact]
    public void RegisterServiceRolePublishesTypedBackend()
    {
        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .Build();
        FakeWorldAccess world = new();

        engine.Context.RegisterService(EngineServiceRole.WorldAccess, world);

        Assert.True(engine.Context.IsServiceAvailable(EngineServiceRole.WorldAccess));
        Assert.Same(world, engine.Context.GetService<FakeWorldAccess>());
        Assert.Equal(typeof(FakeWorldAccess), engine.Context.GetServiceAvailability(EngineServiceRole.WorldAccess).ServiceType);
    }

    /// <summary>
    /// 验证独立编辑器壳扩展通过中性接口注册，Hosting 不需要引用 Editor 程序集。
    /// </summary>
    [Fact]
    public void BuilderRegistersEditorHostExtensionsAsNeutralServices()
    {
        RecordingEditorHostExtension extension = new();

        using Engine engine = new EngineBuilder()
            .WithWorkerCount(1)
            .AddEditorHostExtension(extension)
            .Build();

        IReadOnlyList<IEditorHostExtension> extensions = engine.Context.GetService<IReadOnlyList<IEditorHostExtension>>();
        Assert.Same(extension, Assert.Single(extensions));
    }

    private sealed class FakeWorldAccess
    {
    }

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "PixelEngine.sln")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException("找不到 PixelEngine 仓库根目录。");
    }

    private sealed class RecordingEditorHostExtension : IEditorHostExtension
    {
        public IDisposable? Attach(Engine engine, RenderWindow window, RenderPipeline pipeline)
        {
            throw new NotSupportedException("本测试只验证中性注册路径。");
        }
    }

    private sealed class RecordingSubsystem(string name, List<string> events, bool failInitialize = false) : IEngineSubsystem
    {
        public string Name { get; } = name;

        public void Initialize(EngineContext context)
        {
            events.Add($"init:{Name}");
            if (failInitialize)
            {
                throw new InvalidOperationException($"{Name} init failed.");
            }
        }

        public void Shutdown()
        {
            events.Add($"shutdown:{Name}");
        }
    }
}
