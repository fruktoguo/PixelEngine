using PixelEngine.Editor.Shell;
using PixelEngine.Simulation;
using PixelEngine.Scripting;
using Xunit;
using ScriptScene = PixelEngine.Scripting.Scene;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Demo 运行态脚本后端装配测试。
/// </summary>
public sealed class DemoRuntimeScriptingTests
{
    /// <summary>
    /// 验证 Engine 能基于已注册脚本程序集物化当前 scene 的 Behaviour，并经 Hosting 相位驱动生命周期。
    /// </summary>
    [Fact]
    public void EngineMaterializesAndDrivesCurrentSceneBehaviour()
    {
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
        engine.RunHeadlessTicks(1);

        Scene? current = engine.Context.GetService<ISceneService>().Current;
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
        engine.RunHeadlessTicks(1);

        Assert.Equal(1, lighting.PointLights.Length);
        Assert.Equal(0xFF_40_80_FFu, lighting.PointLights[0].ColorBgra);
    }

    /// <summary>
    /// 验证 Hosting 生产装配会把脚本源目录 watcher 接到 ScriptRuntime，并在帧边界应用热重载。
    /// </summary>
    [Fact]
    public void AttachScriptingFromServicesAppliesWatchedHotReloadAtFrameBoundary()
    {
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

            engine.RunHeadlessTicks(1);
            Behaviour initial = GetSingleBehaviour<HostingHotReloadProbeBehaviour>(context.Scene);
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
    /// 验证 EditorShell Console 脚本运行时不会静默吞掉 watcher 热重载编译诊断。
    /// </summary>
    [Fact]
    public void EditorConsoleScriptRuntimeReportsWatchedHotReloadCompileDiagnostics()
    {
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
            _ = engine.AttachScriptingFromServices(new EditorConsoleScriptRuntime(
                console,
                new ScriptHotReloadRuntimeOptions(
                    $"PixelEngine.Hosting.Tests.ConsoleHotReload.{Guid.NewGuid():N}",
                    scriptDirectory,
                    DebounceInterval: TimeSpan.FromMilliseconds(30))));

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
