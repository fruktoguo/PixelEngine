using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// 热重载服务测试。
/// </summary>
public sealed class HotReloadServiceTests
{
    /// <summary>
    /// 验证编译失败时保留旧组件实例。
    /// </summary>
    [Fact]
    public void CompileFailureKeepsExistingScripts()
    {
        Scene scene = new();
        FakeScriptContext context = new(scene);
        Entity entity = scene.CreateEntity();
        HotReloadService service = CreateServiceWithVersionOne(scene, context, entity);

        service.RequestReload("UserScripts.Broken", [new ScriptSourceFile("Broken.cs", "namespace UserScripts; public sealed class ReloadableScript : MissingBase { }")]);
        HotReloadResult result = service.ApplyPendingReload();
        Behaviour behaviour = scene.CaptureBehaviours()[0].Behaviour;

        Assert.Equal(HotReloadStatus.CompileFailed, result.Status);
        Assert.Equal("v1", ReadVersion(behaviour));
        Assert.Equal(42, ReadField<int>(behaviour, "Counter"));
    }

    /// <summary>
    /// 验证成功热重载会替换同名脚本类型、恢复状态并卸载旧 ALC。
    /// </summary>
    [Fact]
    public void SuccessfulReloadReplacesScriptsRestoresStateAndUnloadsOldContext()
    {
        SuccessfulReloadProbe probe = ExecuteSuccessfulReload();

        Assert.Equal(HotReloadStatus.Reloaded, probe.Status);
        Assert.Equal("v2", probe.Version);
        Assert.Equal(42, probe.Counter);
        Assert.Equal(99, probe.Persisted);
        Assert.Equal(123, probe.Hidden);
        Assert.True(WaitForUnload(probe.OldContext));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static SuccessfulReloadProbe ExecuteSuccessfulReload()
    {
        Scene scene = new();
        FakeScriptContext context = new(scene);
        Entity entity = scene.CreateEntity();
        HotReloadService service = CreateServiceWithVersionOne(scene, context, entity);

        service.RequestReload($"UserScripts.Reload.{Guid.NewGuid():N}", [new ScriptSourceFile("ReloadableScript.cs", VersionTwoSource)]);
        HotReloadResult result = service.ApplyPendingReload();
        Behaviour replacement = scene.CaptureBehaviours()[0].Behaviour;

        return new SuccessfulReloadProbe(
            result.Status,
            result.UnloadedContext!,
            ReadVersion(replacement),
            ReadField<int>(replacement, "Counter"),
            ReadProperty<int>(replacement, "Persisted"),
            ReadField<int>(replacement, "Hidden"));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static HotReloadService CreateServiceWithVersionOne(Scene scene, FakeScriptContext context, Entity entity)
    {
        ScriptCompiler compiler = new();
        ScriptCompilationResult compilation = compiler.Compile(
            $"UserScripts.Initial.{Guid.NewGuid():N}",
            [new ScriptSourceFile("ReloadableScript.cs", VersionOneSource)]);
        Assert.True(compilation.Success, FormatDiagnostics(compilation));

        ScriptLoadContext loadContext = new($"initial-{Guid.NewGuid():N}");
        Assembly assembly = loadContext.LoadFromImages(compilation.PeImage, compilation.PdbImage);
        Type type = assembly.GetType("UserScripts.ReloadableScript", throwOnError: true)!;
        Behaviour behaviour = (Behaviour)Activator.CreateInstance(type)!;
        WriteField(behaviour, "Counter", 42);
        WriteProperty(behaviour, "Persisted", 99);
        WriteField(behaviour, "Hidden", 7);
        scene.AddComponent(entity, behaviour);
        return new HotReloadService(scene, context, compiler, loadContext);
    }

    private static string ReadVersion(Behaviour behaviour)
    {
        return ReadProperty<string>(behaviour, "Version");
    }

    private static T ReadField<T>(Behaviour behaviour, string name)
    {
        FieldInfo field = behaviour.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        return (T)field.GetValue(behaviour)!;
    }

    private static void WriteField<T>(Behaviour behaviour, string name, T value)
    {
        FieldInfo field = behaviour.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        field.SetValue(behaviour, value);
    }

    private static T ReadProperty<T>(Behaviour behaviour, string name)
    {
        PropertyInfo property = behaviour.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        return (T)property.GetValue(behaviour)!;
    }

    private static void WriteProperty<T>(Behaviour behaviour, string name, T value)
    {
        PropertyInfo property = behaviour.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        property.SetValue(behaviour, value);
    }

    private static string FormatDiagnostics(ScriptCompilationResult result)
    {
        return string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    }

    private static bool WaitForUnload(WeakReference reference)
    {
        for (int i = 0; reference.IsAlive && i < 50; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        return !reference.IsAlive;
    }

    private readonly record struct SuccessfulReloadProbe(
        HotReloadStatus Status,
        WeakReference OldContext,
        string Version,
        int Counter,
        int Persisted,
        int Hidden);

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

    private const string VersionOneSource = """
        using PixelEngine.Scripting;

        namespace UserScripts;

        public sealed class ReloadableScript : Behaviour
        {
            public int Counter;
            [Persist] private int Persisted { get; set; }
            [HideInInspector] public int Hidden = 11;
            public string Version => "v1";
        }
        """;

    private const string VersionTwoSource = """
        using PixelEngine.Scripting;

        namespace UserScripts;

        public sealed class ReloadableScript : Behaviour
        {
            public int Counter;
            [Persist] private int Persisted { get; set; }
            [HideInInspector] public int Hidden = 123;
            public string Version => "v2";
        }
        """;
}
