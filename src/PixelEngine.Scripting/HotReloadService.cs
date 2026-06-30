using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace PixelEngine.Scripting;

internal sealed class HotReloadService(Scene scene, IScriptContext context, ScriptCompiler compiler, ScriptLoadContext? currentLoadContext = null)
{
    private readonly Scene _scene = scene ?? throw new ArgumentNullException(nameof(scene));
    private readonly IScriptContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly ScriptCompiler _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
    private PendingReload? _pending;
    private ScriptLoadContext? _currentLoadContext = currentLoadContext;

    public bool HasPendingReload => _pending is not null;

    public void RequestReload(string assemblyName, IReadOnlyList<ScriptSourceFile> sources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        ArgumentNullException.ThrowIfNull(sources);
        _pending = new PendingReload(assemblyName, [.. sources]);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public HotReloadResult ApplyPendingReload()
    {
        if (_pending is null)
        {
            return HotReloadResult.NoPending();
        }

        PendingReload pending = _pending;
        _pending = null;
        ScriptCompilationResult compilation = _compiler.Compile(pending.AssemblyName, pending.Sources);
        if (!compilation.Success)
        {
            return HotReloadResult.CompileFailed(compilation.Diagnostics);
        }

        ScriptBehaviourRecord[] records = _scene.CaptureBehaviours();
        ScriptStateSnapshot[] snapshots = new ScriptStateSnapshot[records.Length];
        string[] typeNames = new string[records.Length];
        for (int i = 0; i < records.Length; i++)
        {
            Behaviour behaviour = records[i].Behaviour;
            snapshots[i] = ScriptStateSnapshot.Capture(behaviour);
            typeNames[i] = behaviour.GetType().FullName ?? behaviour.GetType().Name;
            behaviour.InvokeDestroy(_context);
            _scene.RemoveComponent(records[i].Entity, behaviour.GetType());
        }

        ScriptLoadContext newContext = new($"script-reload-{Guid.NewGuid():N}");
        Assembly assembly = newContext.LoadFromImages(compilation.PeImage, compilation.PdbImage);
        for (int i = 0; i < records.Length; i++)
        {
            Type? newType = assembly.GetType(typeNames[i], throwOnError: false);
            if (newType is null || !typeof(Behaviour).IsAssignableFrom(newType))
            {
                continue;
            }

            Behaviour replacement = (Behaviour)Activator.CreateInstance(newType)!;
            snapshots[i].Restore(replacement);
            _scene.AddComponent(records[i].Entity, replacement);
        }

        records.AsSpan().Clear();
        WeakReference? oldReference = ReplaceCurrentContext(newContext);
        return HotReloadResult.Reloaded(compilation.Diagnostics, oldReference);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private WeakReference? ReplaceCurrentContext(ScriptLoadContext newContext)
    {
        ScriptLoadContext? context = _currentLoadContext;
        _currentLoadContext = newContext;
        if (context is null)
        {
            return null;
        }

        WeakReference reference = new(context, trackResurrection: false);
        context.Unload();
        return reference;
    }

    private sealed record PendingReload(string AssemblyName, ScriptSourceFile[] Sources);
}

internal sealed class HotReloadResult
{
    private HotReloadResult(HotReloadStatus status, ImmutableArray<Diagnostic> diagnostics, WeakReference? unloadedContext)
    {
        Status = status;
        Diagnostics = diagnostics;
        UnloadedContext = unloadedContext;
    }

    public HotReloadStatus Status { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public WeakReference? UnloadedContext { get; }

    public bool OldContextUnloaded => UnloadedContext is null || WaitForUnload(UnloadedContext);

    public static HotReloadResult NoPending()
    {
        return new HotReloadResult(HotReloadStatus.NoPendingReload, [], null);
    }

    public static HotReloadResult CompileFailed(ImmutableArray<Diagnostic> diagnostics)
    {
        return new HotReloadResult(HotReloadStatus.CompileFailed, diagnostics, null);
    }

    public static HotReloadResult Reloaded(ImmutableArray<Diagnostic> diagnostics, WeakReference? unloadedContext)
    {
        return new HotReloadResult(HotReloadStatus.Reloaded, diagnostics, unloadedContext);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
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
}

internal enum HotReloadStatus
{
    NoPendingReload,
    CompileFailed,
    Reloaded,
}
