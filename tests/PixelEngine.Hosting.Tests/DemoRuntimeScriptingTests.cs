using PixelEngine.Editor.Shell;
using PixelEngine.Simulation;
using PixelEngine.Scripting;
using Xunit;
using ScriptScene = PixelEngine.Scripting.Scene;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Demo 运行态脚本后端装配测试。
/// 不变式：Demo 脚本后端经 Hosting 装配、热重载探针可观测且不影响 headless tick。
/// </summary>
public sealed class DemoRuntimeScriptingTests
{
    /// <summary>
    /// 验证 Engine 能基于已注册脚本程序集物化当前 scene 的 Behaviour，并经 Hosting 相位驱动生命周期。
    /// </summary>
    [Fact]
    public void EngineMaterializesAndDrivesCurrentSceneBehaviour()
    {
        // Arrange：搭建测试场景与依赖
        MaterialTable materials = Materials(("empty", CellType.Empty));
        using Engine engine = new EngineBuilder()
            .UseHeadless()
            .UseDeterministicMode()
            .AddScene(new SceneDescriptor("demo", SceneSourceKind.Procedural, typeof(DemoRuntimeBehaviour).FullName!))
            .WithStartScene("demo")
            .Build();
        engine.Context.RegisterService(materials);
        _ = engine.AttachResidentSimulationWorld(worldWidthCells: 64, worldHeightCells: 64, particleCapacity: 16);
        ScriptInputApi input = new();
        engine.Context.RegisterService<IInputApi>(EngineServiceRole.Input, input);
        engine.Context.RegisterService(input);

        engine.RegisterScriptAssembly(typeof(DemoRuntimeBehaviour).Assembly);
        ScriptScene scriptScene = engine.Context.GetService<ScriptScene>();
        DemoRuntimeBehaviour behaviour = GetSingleBehaviour<DemoRuntimeBehaviour>(scriptScene);
        List<string> events = [];
        behaviour.Events = events;
        _ = engine.AttachPhysics();

        ScriptSimulationContext scriptContext = engine.AttachScriptingFromServices();
        // Act：执行被测操作
        engine.RunHeadlessTicks(1);

        Scene? current = engine.Context.GetService<ISceneService>().Current;
        // Assert：验证不变式与预期结果
        Assert.NotNull(current);
        Assert.Same(scriptScene, current.ScriptScene);
        Assert.Same(scriptContext, engine.Context.GetService<IScriptContext>());
        Assert.Same(input, scriptContext.Input);
        _ = scriptContext.Bodies;
        _ = scriptContext.Character;
        _ = scriptContext.Camera;
        _ = scriptContext.Lighting;
        _ = scriptContext.Events;
        _ = scriptContext.Time;
        Assert.Same(scriptScene, scriptContext.Scene);
        Assert.True(engine.Context.TryGetService(out ScriptCameraSynchronizer _));
        Assert.True(engine.Context.TryGetService(out ScriptLightingSynchronizer _));
        Assert.True(engine.Context.IsServiceAvailable(EngineServiceRole.Scripting));
        Assert.True(engine.Context.IsServiceAvailable(EngineServiceRole.WorldAccess));
        Assert.True(engine.Context.IsServiceAvailable(EngineServiceRole.ParticleService));
        Assert.True(engine.Context.IsServiceAvailable(EngineServiceRole.MaterialRegistry));
        Assert.True(engine.Context.IsServiceAvailable(EngineServiceRole.PhysicsService));
        Assert.Equal(["start", "update"], events);
    }

    /// <summary>
    /// 验证窗口运行时先注册相机/光照同步、随后再接脚本时，脚本当帧提交的点光源仍会在渲染前同步。
    /// </summary>
    [Fact]
    public void AttachScriptingFromServicesResyncsLightingAfterScriptsWhenSynchronizerAlreadyExists()
    {
        // Arrange：搭建测试场景与依赖
        MaterialTable materials = Materials(("empty", CellType.Empty));
        using Engine engine = new EngineBuilder()
            .UseHeadless()
            .UseDeterministicMode()
            .AddScene(new SceneDescriptor("lighting", SceneSourceKind.Procedural, typeof(HostingLightingProbeBehaviour).FullName!))
            .WithStartScene("lighting")
            .Build();
        engine.Context.RegisterService(materials);
        _ = engine.AttachResidentSimulationWorld(worldWidthCells: 64, worldHeightCells: 64, particleCapacity: 16);
        ScriptInputApi input = new();
        engine.Context.RegisterService<IInputApi>(EngineServiceRole.Input, input);
        engine.Context.RegisterService(input);
        _ = engine.AttachCameraSynchronization();
        ScriptLightingSynchronizer lighting = engine.AttachLightingSynchronization();

        engine.RegisterScriptAssembly(typeof(HostingLightingProbeBehaviour).Assembly);
        _ = engine.AttachScriptingFromServices();
        // Act：执行被测操作
        engine.RunHeadlessTicks(1);

        // Assert：验证不变式与预期结果
        Assert.Equal(1, lighting.PointLights.Length);
        Assert.Equal(0xFF_40_80_FFu, lighting.PointLights[0].ColorBgra);
    }

    /// <summary>
    /// 验证 Hosting 生产装配会把脚本源目录 watcher 接到 ScriptRuntime，并在帧边界应用热重载。
    /// </summary>
    [Fact]
    public void AttachScriptingFromServicesAppliesWatchedHotReloadAtFrameBoundary()
    {
        // Arrange：搭建测试场景与依赖
        string scriptDirectory = Path.Combine(Path.GetTempPath(), "PixelEngineHotReload", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(scriptDirectory);
        try
        {
            MaterialTable materials = Materials(("empty", CellType.Empty));
            using Engine engine = new EngineBuilder()
                .UseHeadless()
                .UseDeterministicMode()
                .AddScene(new SceneDescriptor("reload", SceneSourceKind.Procedural, typeof(HostingHotReloadProbeBehaviour).FullName!))
                .WithStartScene("reload")
                .Build();
            engine.Context.RegisterService(materials);
            _ = engine.AttachResidentSimulationWorld(worldWidthCells: 64, worldHeightCells: 64, particleCapacity: 16);
            engine.RegisterScriptAssembly(typeof(HostingHotReloadProbeBehaviour).Assembly);
            ScriptSimulationContext context = engine.AttachScriptingFromServices(
                hotReload: new ScriptHotReloadRuntimeOptions(
                    $"PixelEngine.Hosting.Tests.HotReload.{Guid.NewGuid():N}",
                    scriptDirectory,
                    DebounceInterval: TimeSpan.FromMilliseconds(30)));
            ScriptHotReloadController controller = engine.Context.GetService<ScriptHotReloadController>();

            // Act：执行被测操作
            engine.RunHeadlessTicks(1);
            Behaviour initial = GetSingleBehaviour<HostingHotReloadProbeBehaviour>(context.Scene);
            // Assert：验证不变式与预期结果
            Assert.Equal("v1", ReadProperty<string>(initial, "Version"));
            Assert.Equal(2, ReadField<int>(initial, "Counter"));

            File.WriteAllText(Path.Combine(scriptDirectory, "HostingHotReloadProbeBehaviour.cs"), HotReloadProbeVersionTwoSource);

            Assert.True(
                SpinWait.SpinUntil(() => controller.HasPendingReload || controller.LastWatcherException is not null, TimeSpan.FromSeconds(5)),
                controller.LastWatcherException?.ToString() ?? "watcher 未在超时时间内触发 reload");
            Assert.Null(controller.LastWatcherException);

            engine.RunHeadlessTicks(1);
            Behaviour reloaded = engine.Context.GetService<ScriptScene>().CaptureInspectionSnapshot()[0].Components[0].Behaviour;

            Assert.Equal("v2", ReadProperty<string>(reloaded, "Version"));
            Assert.Equal(12, ReadField<int>(reloaded, "Counter"));
        }
        finally
        {
            Directory.Delete(scriptDirectory, recursive: true);
        }
    }

    /// <summary>
    /// 验证 Hosting-owned 热重载会把新脚本程序集注册进 Behaviour registry，使默认工作台新脚本可直接挂载。
    /// </summary>
    [Fact]
    public void AttachScriptingFromServicesRegistersNewHotReloadBehaviourForEditorMounting()
    {
        // Arrange：搭建测试场景与依赖
        string scriptDirectory = Path.Combine(Path.GetTempPath(), "PixelEngineDefaultWorkbenchHotReload", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(scriptDirectory);
        try
        {
            File.WriteAllText(Path.Combine(scriptDirectory, "DefaultWorkbenchBehaviour.cs"), DefaultWorkbenchBehaviourSource);
            MaterialTable materials = Materials(("empty", CellType.Empty));
            using Engine engine = new EngineBuilder()
                .UseHeadless()
                .UseDeterministicMode()
                .AddScene(new SceneDescriptor("empty", SceneSourceKind.Empty))
                .WithStartScene("empty")
                .Build();
            engine.Context.RegisterService(materials);
            _ = engine.AttachResidentSimulationWorld(worldWidthCells: 64, worldHeightCells: 64, particleCapacity: 16);
            engine.AttachScriptScene(new ScriptScene());
            _ = engine.AttachScriptingFromServices(
                hotReload: new ScriptHotReloadRuntimeOptions(
                    $"PixelEngine.Hosting.Tests.DefaultWorkbench.{Guid.NewGuid():N}",
                    scriptDirectory,
                    DebounceInterval: TimeSpan.FromMilliseconds(30)));
            ScriptHotReloadController controller = engine.Context.GetService<ScriptHotReloadController>();
            controller.RequestReloadFromDirectory(
                $"PixelEngine.Hosting.Tests.DefaultWorkbench.{Guid.NewGuid():N}",
                scriptDirectory);

            // Act：执行被测操作
            engine.RunHeadlessTicks(1);

            ScriptAssemblyRegistry scripts = engine.Context.GetService<ScriptAssemblyRegistry>();
            // Assert：验证不变式与预期结果
            Assert.Contains(scripts.Assemblies, assembly => assembly.GetType("DefaultWorkbenchBehaviour", throwOnError: false) is not null);
            EditorSceneModel scene = EditorSceneModel.Empty("default-workbench-hot-reload");
            EditorGameObject gameObject = scene.Create("Receiver");
            EditorUndoStack undo = new();
            EditorAssetDropPayload payload = new("asset_script", "scripts/DefaultWorkbenchBehaviour.cs", EditorAssetType.Script);

            EditorAssetDropResult result = EditorAssetDropService.DropScriptOnComponentList(scene, undo, scripts, payload, gameObject.StableId);

            Assert.True(result.Succeeded, result.Diagnostic);
            EditorComponentModel component = Assert.Single(gameObject.Components);
            Assert.Equal("DefaultWorkbenchBehaviour", component.TypeName);
        }
        finally
        {
            Directory.Delete(scriptDirectory, recursive: true);
        }
    }

    /// <summary>
    /// 验证 Hosting-owned 脚本运行时会把 watcher 热重载编译诊断汇入 Editor Console sink。
    /// </summary>
    [Fact]
    public void AttachScriptingFromServicesReportsWatchedHotReloadCompileDiagnosticsToConsoleSink()
    {
        // Arrange：准备输入与初始状态
        string scriptDirectory = Path.Combine(Path.GetTempPath(), "PixelEngineConsoleHotReload", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(scriptDirectory);
        try
        {
            MaterialTable materials = Materials(("empty", CellType.Empty));
            using Engine engine = new EngineBuilder()
                .UseHeadless()
                .UseDeterministicMode()
                .AddScene(new SceneDescriptor("reload", SceneSourceKind.Procedural, typeof(HostingHotReloadProbeBehaviour).FullName!))
                .WithStartScene("reload")
                .Build();
            engine.Context.RegisterService(materials);
            _ = engine.AttachResidentSimulationWorld(worldWidthCells: 64, worldHeightCells: 64, particleCapacity: 16);
            engine.RegisterScriptAssembly(typeof(HostingHotReloadProbeBehaviour).Assembly);
            EditorConsoleStore console = new();
            engine.Context.RegisterService<IScriptHotReloadDiagnosticSink>(new EditorConsoleScriptHotReloadDiagnosticSink(console));
            _ = engine.AttachScriptingFromServices(
                hotReload: new ScriptHotReloadRuntimeOptions(
                    $"PixelEngine.Hosting.Tests.ConsoleHotReload.{Guid.NewGuid():N}",
                    scriptDirectory,
                    DebounceInterval: TimeSpan.FromMilliseconds(30)));
            // Assert：验证预期结果
            Assert.True(engine.Context.TryGetService(out ScriptHotReloadController _));
            Assert.Contains(console.Snapshot(), entry =>
                entry.Category == EditorConsoleCategory.Script &&
                entry.Severity == EditorConsoleSeverity.Info &&
                entry.Text.Contains("脚本热重载监听已启动", StringComparison.Ordinal));

            engine.RunHeadlessTicks(1);
            File.WriteAllText(Path.Combine(scriptDirectory, "BrokenBehaviour.cs"), "public sealed class BrokenBehaviour {");

            Assert.True(
                SpinWait.SpinUntil(
                    () =>
                    {
                        engine.RunHeadlessTicks(1);
                        EditorConsoleEntry[] entries = console.Snapshot();
                        return entries.Any(entry =>
                            entry.Category == EditorConsoleCategory.Script &&
                            entry.Severity == EditorConsoleSeverity.Error &&
                            entry.Text.Contains("脚本编译失败", StringComparison.Ordinal));
                    },
                    TimeSpan.FromSeconds(5)),
                string.Join(Environment.NewLine, console.Snapshot().Select(static entry => $"[{entry.Severity}] {entry.Text}")));
            Assert.Contains(console.Snapshot(), entry =>
                entry.Category == EditorConsoleCategory.Script &&
                entry.Severity == EditorConsoleSeverity.Error &&
                entry.Text.Contains("error", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(scriptDirectory, recursive: true);
        }
    }

    /// <summary>
    /// 验证合法但不存在的脚本源目录只降级 watcher，不阻止 Hosting 装配脚本 runtime。
    /// </summary>
    [Fact]
    public void AttachScriptingFromServicesReportsMissingWatcherSourceAndKeepsRuntimeRunning()
    {
        string scriptDirectory = Path.Combine(Path.GetTempPath(), "PixelEngineMissingHotReload", Guid.NewGuid().ToString("N"), "scripts");
        Assert.False(Directory.Exists(scriptDirectory));

        MaterialTable materials = Materials(("empty", CellType.Empty));
        using Engine engine = new EngineBuilder()
            .UseHeadless()
            .UseDeterministicMode()
            .AddScene(new SceneDescriptor("reload", SceneSourceKind.Procedural, typeof(HostingHotReloadProbeBehaviour).FullName!))
            .WithStartScene("reload")
            .Build();
        engine.Context.RegisterService(materials);
        _ = engine.AttachResidentSimulationWorld(worldWidthCells: 64, worldHeightCells: 64, particleCapacity: 16);
        engine.RegisterScriptAssembly(typeof(HostingHotReloadProbeBehaviour).Assembly);
        EditorConsoleStore console = new();
        engine.Context.RegisterService<IScriptHotReloadDiagnosticSink>(new EditorConsoleScriptHotReloadDiagnosticSink(console));

        ScriptSimulationContext context = engine.AttachScriptingFromServices(
            hotReload: new ScriptHotReloadRuntimeOptions(
                $"PixelEngine.Hosting.Tests.MissingHotReload.{Guid.NewGuid():N}",
                scriptDirectory,
                DebounceInterval: TimeSpan.FromMilliseconds(30)));

        Assert.True(engine.Context.TryGetService(out ScriptHotReloadController controller));
        Assert.False(controller.HasPendingReload);
        Assert.Null(controller.LastWatcherException);
        Assert.Same(context, engine.Context.GetService<IScriptContext>());
        engine.RunHeadlessTicks(1);
        Behaviour initial = GetSingleBehaviour<HostingHotReloadProbeBehaviour>(context.Scene);
        Assert.Equal("v1", ReadProperty<string>(initial, "Version"));
        Assert.Equal(2, ReadField<int>(initial, "Counter"));
        EditorConsoleEntry[] entries = console.Snapshot();
        Assert.Contains(entries, entry =>
            entry.Category == EditorConsoleCategory.Script &&
            entry.Severity == EditorConsoleSeverity.Error &&
            entry.Source == "script-hot-reload" &&
            entry.Text.Contains("WatcherStartFailed", StringComparison.Ordinal) &&
            entry.Text.Contains("无 watcher", StringComparison.Ordinal));
        Assert.DoesNotContain(entries, entry => entry.Text.Contains("脚本热重载监听已启动", StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证真正的热重载配置错误仍按致命装配错误抛出，不被 watcher 降级逻辑吞掉。
    /// </summary>
    [Fact]
    public void AttachScriptingFromServicesDoesNotSwallowFatalHotReloadConfigurationErrors()
    {
        string scriptDirectory = Path.Combine(Path.GetTempPath(), "PixelEngineFatalHotReload", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(scriptDirectory);
        try
        {
            MaterialTable materials = Materials(("empty", CellType.Empty));
            using Engine engine = new EngineBuilder()
                .UseHeadless()
                .UseDeterministicMode()
                .AddScene(new SceneDescriptor("reload", SceneSourceKind.Procedural, typeof(HostingHotReloadProbeBehaviour).FullName!))
                .WithStartScene("reload")
                .Build();
            engine.Context.RegisterService(materials);
            _ = engine.AttachResidentSimulationWorld(worldWidthCells: 64, worldHeightCells: 64, particleCapacity: 16);
            engine.RegisterScriptAssembly(typeof(HostingHotReloadProbeBehaviour).Assembly);
            EditorConsoleStore console = new();
            engine.Context.RegisterService<IScriptHotReloadDiagnosticSink>(new EditorConsoleScriptHotReloadDiagnosticSink(console));

            _ = Assert.ThrowsAny<ArgumentException>(() => engine.AttachScriptingFromServices(
                hotReload: new ScriptHotReloadRuntimeOptions(
                    $"PixelEngine.Hosting.Tests.FatalHotReload.{Guid.NewGuid():N}",
                    scriptDirectory,
                    SearchPattern: null!)));
            Assert.Empty(console.Snapshot());
        }
        finally
        {
            Directory.Delete(scriptDirectory, recursive: true);
        }
    }

    private static MaterialTable Materials(params (string Name, CellType Type)[] definitions)
    {
        MaterialDef[] materials = new MaterialDef[definitions.Length];
        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = new MaterialDef
            {
                Id = (ushort)i,
                Name = definitions[i].Name,
                Type = definitions[i].Type,
                Density = i == 0 ? (byte)0 : (byte)100,
                HeatCapacity = 1,
                HeatConduct = 255,
                TextureId = -1,
                MeltPoint = float.NaN,
                FreezePoint = float.NaN,
                BoilPoint = float.NaN,
            };
        }

        return new MaterialTable(materials);
    }

    private static TBehaviour GetSingleBehaviour<TBehaviour>(ScriptScene scene)
        where TBehaviour : Behaviour
    {
        ScriptEntityInspection[] snapshot = scene.CaptureInspectionSnapshot();
        _ = Assert.Single(snapshot);
        _ = Assert.Single(snapshot[0].Components);
        return Assert.IsType<TBehaviour>(snapshot[0].Components[0].Behaviour);
    }

    private static T ReadField<T>(Behaviour behaviour, string name)
    {
        return (T)behaviour.GetType()
            .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(behaviour)!;
    }

    private static T ReadProperty<T>(Behaviour behaviour, string name)
    {
        return (T)behaviour.GetType()
            .GetProperty(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(behaviour)!;
    }

    private sealed class DemoRuntimeBehaviour : Behaviour
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
    }

    private sealed class HostingLightingProbeBehaviour : Behaviour
    {
        protected override void OnUpdate(float dt)
        {
            _ = dt;
            Context.Lighting.AddPointLight(12, 14, 6, 0xFF_40_80_FF, 0.75f);
        }
    }

    private const string HotReloadProbeVersionTwoSource = """
        using PixelEngine.Scripting;

        namespace PixelEngine.Hosting.Tests;

        public sealed class HostingHotReloadProbeBehaviour : Behaviour
        {
            public int Counter;

            public string Version => "v2";

            protected override void OnStart()
            {
                Counter += 10;
            }
        }
        """;

    private const string DefaultWorkbenchBehaviourSource = """
        using PixelEngine.Scripting;

        public sealed class DefaultWorkbenchBehaviour : Behaviour
        {
        }
        """;
}

/// <summary>
/// Hosting 热重载测试使用的初始脚本类型；动态脚本程序集会用同名类型替换它。
/// </summary>
public sealed class HostingHotReloadProbeBehaviour : Behaviour
{
    /// <summary>
    /// 用于验证热重载状态恢复的公开字段。
    /// </summary>
    public int Counter = 1;

    /// <summary>
    /// 当前脚本版本。
    /// </summary>
    public string Version => "v1";

    /// <inheritdoc />
    protected override void OnStart()
    {
        Counter++;
    }
}
