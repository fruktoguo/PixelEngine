using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;

namespace PixelEngine.Scripting;

internal sealed class HotReloadService(Scene scene, IScriptContext context, ScriptCompiler compiler, ScriptLoadContext? currentLoadContext = null) : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Scene _scene = scene ?? throw new ArgumentNullException(nameof(scene));
    private readonly IScriptContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly ScriptCompiler _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
    private PendingReload? _pending;
    private ScriptLoadContext? _currentLoadContext = currentLoadContext;
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private HotReloadWatchOptions? _watchOptions;
    private bool _disposed;

    public bool HasPendingReload
    {
        get
        {
            lock (_gate)
            {
                return _pending is not null;
            }
        }
    }

    public Exception? LastWatcherException { get; private set; }

    public void RequestReload(string assemblyName, IReadOnlyList<ScriptSourceFile> sources)
    {
        RequestReload(assemblyName, sources, HotReloadOptions.PreservePublicAndPersisted);
    }

    public void RequestReload(string assemblyName, IReadOnlyList<ScriptSourceFile> sources, HotReloadOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        ArgumentNullException.ThrowIfNull(sources);
        ThrowIfDisposed();
        lock (_gate)
        {
            _pending = new PendingReload(assemblyName, [.. sources], options);
        }
    }

    public void StartWatching(HotReloadWatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfDisposed();
        if (!Directory.Exists(options.SourceDirectory))
        {
            throw new DirectoryNotFoundException($"脚本源目录不存在：{options.SourceDirectory}");
        }

        FileSystemWatcher watcher = new(options.SourceDirectory, options.SearchPattern)
        {
            IncludeSubdirectories = options.IncludeSubdirectories,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
        };
        Timer debounceTimer = new(static state => ((HotReloadService)state!).QueueWatchedReload(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        watcher.Changed += OnWatchedSourceChanged;
        watcher.Created += OnWatchedSourceChanged;
        watcher.Deleted += OnWatchedSourceChanged;
        watcher.Renamed += OnWatchedSourceRenamed;

        lock (_gate)
        {
            DisposeWatcherCore();
            _watchOptions = options;
            _debounceTimer = debounceTimer;
            _watcher = watcher;
            watcher.EnableRaisingEvents = true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public HotReloadResult ApplyPendingReload()
    {
        PendingReload? pending;
        lock (_gate)
        {
            pending = _pending;
            _pending = null;
        }

        if (pending is null)
        {
            return HotReloadResult.NoPending();
        }

        ScriptCompilationResult compilation = _compiler.Compile(pending.AssemblyName, pending.Sources);
        if (!compilation.Success)
        {
            return HotReloadResult.CompileFailed(compilation.Diagnostics);
        }

        ScriptBehaviourRecord[] records = _scene.CaptureBehaviours();
        ScriptStateSnapshot?[] snapshots = pending.Options.PreserveState ? new ScriptStateSnapshot[records.Length] : [];
        string[] typeNames = new string[records.Length];
        for (int i = 0; i < records.Length; i++)
        {
            Behaviour behaviour = records[i].Behaviour;
            if (pending.Options.PreserveState)
            {
                snapshots[i] = ScriptStateSnapshot.Capture(behaviour);
            }

            typeNames[i] = behaviour.GetType().FullName ?? behaviour.GetType().Name;
            _scene.DestroyComponent(records[i].Entity, behaviour.GetType(), _context);
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
            if (snapshots.Length != 0)
            {
                snapshots[i]?.Restore(replacement);
            }

            _scene.AddComponent(records[i].Entity, replacement);
        }

        _scene.DispatchStart(_context);
        records.AsSpan().Clear();
        WeakReference? oldReference = ReplaceCurrentContext(newContext);
        return HotReloadResult.Reloaded(compilation.Diagnostics, oldReference);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_gate)
        {
            DisposeWatcherCore();
            _disposed = true;
        }
    }

    private void OnWatchedSourceChanged(object sender, FileSystemEventArgs args)
    {
        if (IsCSharpSourcePath(args.FullPath))
        {
            RestartDebounceTimer();
        }
    }

    private void OnWatchedSourceRenamed(object sender, RenamedEventArgs args)
    {
        if (IsCSharpSourcePath(args.FullPath) || IsCSharpSourcePath(args.OldFullPath))
        {
            RestartDebounceTimer();
        }
    }

    private void RestartDebounceTimer()
    {
        lock (_gate)
        {
            if (_disposed || _debounceTimer is null || _watchOptions is null)
            {
                return;
            }

            _ = _debounceTimer.Change(_watchOptions.DebounceInterval, Timeout.InfiniteTimeSpan);
        }
    }

    private void QueueWatchedReload()
    {
        HotReloadWatchOptions? options;
        lock (_gate)
        {
            if (_disposed || _watchOptions is null)
            {
                return;
            }

            options = _watchOptions;
        }

        try
        {
            ScriptSourceFile[] sources = LoadSourceFiles(options);
            RequestReload(options.AssemblyName, sources, options.ReloadOptions);
            LastWatcherException = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            LastWatcherException = exception;
        }
    }

    private void DisposeWatcherCore()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnWatchedSourceChanged;
            _watcher.Created -= OnWatchedSourceChanged;
            _watcher.Deleted -= OnWatchedSourceChanged;
            _watcher.Renamed -= OnWatchedSourceRenamed;
            _watcher.Dispose();
            _watcher = null;
        }

        _debounceTimer?.Dispose();
        _debounceTimer = null;
        _watchOptions = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static ScriptSourceFile[] LoadSourceFiles(HotReloadWatchOptions options)
    {
        SearchOption searchOption = options.IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        string[] paths =
        [
            .. Directory.GetFiles(options.SourceDirectory, options.SearchPattern, searchOption)
            .Where(IsCSharpSourcePath)
            .Order(StringComparer.OrdinalIgnoreCase),
        ];
        ScriptSourceFile[] sources = new ScriptSourceFile[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            string path = paths[i];
            string relativePath = Path.GetRelativePath(options.SourceDirectory, path);
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            sources[i] = new ScriptSourceFile(relativePath, reader.ReadToEnd());
        }

        return sources;
    }

    private static bool IsCSharpSourcePath(string path)
    {
        return string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase);
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

    private sealed record PendingReload(string AssemblyName, ScriptSourceFile[] Sources, HotReloadOptions Options);
}

internal sealed record HotReloadWatchOptions(string AssemblyName, string SourceDirectory)
{
    public string SearchPattern { get; init; } = "*.cs";

    public bool IncludeSubdirectories { get; init; } = true;

    public TimeSpan DebounceInterval { get; init; } = TimeSpan.FromMilliseconds(150);

    public HotReloadOptions ReloadOptions { get; init; } = HotReloadOptions.PreservePublicAndPersisted;
}

internal readonly record struct HotReloadOptions(bool PreserveState)
{
    public static HotReloadOptions PreservePublicAndPersisted { get; } = new(PreserveState: true);

    public static HotReloadOptions FullReset { get; } = new(PreserveState: false);
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
