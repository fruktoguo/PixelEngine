using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// plan/14 §4.5 的脚本热重载端到端验收测试。
/// 不变式：端到端热重载后脚本行为更新、未引用旧 ALC。
/// </summary>
public sealed class HotReloadTests
{
    /// <summary>
    /// 验证热重载会在原实体上替换同名 Behaviour、恢复公开字段，并释放旧 ALC。
    /// </summary>
    [Fact]
    public void RuntimeFrameBoundaryReloadReplacesBehaviourOnSameEntityAndPreservesState()
    {
        ReloadProbe probe = ExecuteReload();

        Assert.Equal(HotReloadStatus.Reloaded, probe.Status);
        Assert.Equal(1, probe.EntityId);
        Assert.Equal("v2", probe.Version);
        Assert.Equal(17, probe.Counter);
        Assert.Equal(1, probe.StartedByReload);
        Assert.True(WaitForUnload(probe.OldContext));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ReloadProbe ExecuteReload()
    {
        Scene scene = new();
        FakeScriptContext context = new(scene);
        Entity entity = scene.CreateEntity();
        HotReloadService service = CreateInitialService(scene, context, entity);
        service.RequestReload(
            $"UserScripts.EndToEnd.{Guid.NewGuid():N}",
            [new ScriptSourceFile("ReloadableScript.cs", VersionTwoSource)]);

        HotReloadResult result = service.ApplyPendingReload();
        ScriptBehaviourRecord record = scene.CaptureBehaviours()[0];
        Behaviour replacement = record.Behaviour;

        return new ReloadProbe(
            result.Status,
            record.Entity.Id,
            ReadProperty<string>(replacement, "Version"),
            ReadField<int>(replacement, "Counter"),
            ReadField<int>(replacement, "StartedByReload"),
            result.UnloadedContext!);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static HotReloadService CreateInitialService(Scene scene, FakeScriptContext context, Entity entity)
    {
        ScriptCompiler compiler = new();
        ScriptCompilationResult compilation = compiler.Compile(
            $"UserScripts.Initial.{Guid.NewGuid():N}",
            [new ScriptSourceFile("ReloadableScript.cs", VersionOneSource)]);
        Assert.True(compilation.Success, FormatDiagnostics(compilation));

        ScriptLoadContext loadContext = new($"script-initial-{Guid.NewGuid():N}");
        Assembly assembly = loadContext.LoadFromImages(compilation.PeImage, compilation.PdbImage);
        Type type = assembly.GetType("UserScripts.ReloadableScript", throwOnError: true)!;
        Behaviour behaviour = (Behaviour)Activator.CreateInstance(type)!;
        WriteField(behaviour, "Counter", 17);
        scene.AddComponent(entity, behaviour);
        return new HotReloadService(scene, context, compiler, loadContext);
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

    private readonly record struct ReloadProbe(
        HotReloadStatus Status,
        int EntityId,
        string Version,
        int Counter,
        int StartedByReload,
        WeakReference OldContext);

    private sealed class FakeScriptContext(Scene scene) : IScriptContext
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

        public IGameTime Time => throw new NotSupportedException();

        public Scene Scene { get; } = scene;
    }

    private const string VersionOneSource = """
        using PixelEngine.Scripting;

        namespace UserScripts;

        public sealed class ReloadableScript : Behaviour
        {
            public int Counter;
            public string Version => "v1";
        }
        """;

    private const string VersionTwoSource = """
        using PixelEngine.Scripting;

        namespace UserScripts;

        public sealed class ReloadableScript : Behaviour
        {
            public int Counter;
            public int StartedByReload;
            public string Version => "v2";

            protected override void OnStart()
            {
                StartedByReload++;
            }
        }
        """;
}
