using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;

namespace PixelEngine.Scripting;

/// <summary>
/// 脚本热重载服务；负责排队编译请求、监视源文件变更、替换程序集并迁移 Behaviour 状态。
/// </summary>
internal sealed class HotReloadService(Scene scene, IScriptContext context, ScriptCompiler compiler, ScriptLoadContext? currentLoadContext = null) : IDisposable
{
    private readonly Lock _gate = new();
    private Scene _scene = scene ?? throw new ArgumentNullException(nameof(scene));
    private readonly IScriptContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly ScriptCompiler _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
    private readonly List<WeakReference> _unloadedContexts = [];
    private PendingReload? _pending;
    private ScriptLoadContext? _currentLoadContext = currentLoadContext;
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private HotReloadWatchOptions? _watchOptions;
    private bool _disposed;

    /// <summary>
    /// 是否存在尚未在相位 1 应用的热重载请求。
    /// </summary>
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

    /// <summary>
    /// 文件监视触发重载时最近一次 IO/权限类异常；成功排队后清零。
    /// </summary>
    public Exception? LastWatcherException { get; private set; }

    /// <summary>
    /// 已卸载但尚未被 GC 回收的 <see cref="ScriptLoadContext"/> 数量。
    /// </summary>
    public int UnloadedLoadContextAliveCount => CountUnloadedLoadContexts(forceFullCollection: false);

    /// <summary>
    /// 强制多次 GC 后统计仍存活的已卸载 LoadContext，用于诊断卸载泄漏。
    /// </summary>
    public int CollectAndCountUnloadedLoadContextsAlive()
    {
        return CountUnloadedLoadContexts(forceFullCollection: true);
    }

    /// <summary>
    /// 以默认选项（保留公开与持久化字段）排队一次热重载。
    /// </summary>
    public void RequestReload(string assemblyName, IReadOnlyList<ScriptSourceFile> sources)
    {
        RequestReload(assemblyName, sources, HotReloadOptions.PreservePublicAndPersisted);
    }

    /// <summary>
    /// 排队一次热重载；实际编译与应用在 <see cref="ApplyPendingReload"/> 中执行。
    /// </summary>
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

    /// <summary>
    /// 替换热重载所操作的场景引用；场景切换后由运行时调用。
    /// </summary>
    public void ReplaceScene(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ThrowIfDisposed();
        lock (_gate)
        {
            _scene = scene;
        }
    }

    /// <summary>
    /// 启动对脚本源目录的文件监视；变更经防抖后自动排队重载。
    /// </summary>
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

    /// <summary>
    /// 在相位 1 应用待处理热重载：编译、捕获状态、替换 Behaviour 并切换 LoadContext。
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Hot reload loads Behaviour types from Roslyn-compiled runtime script assemblies; this is an editor/script boundary outside trimmed engine hot paths.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072",
        Justification = "Reloaded Behaviour constructors live in the runtime script assembly and cannot be described by the trimmed engine closure.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "Hot reload depends on runtime-generated script assemblies and is intentionally outside NativeAOT-compatible execution.")]
    public HotReloadResult ApplyPendingReload()
    {
        PendingReload? pending;
        Scene scene;
        lock (_gate)
        {
            pending = _pending;
            _pending = null;
            scene = _scene;
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

        // 先捕获所有 Behaviour 实例与类型名，再在新程序集中构造替换体并恢复状态。
        ScriptBehaviourRecord[] records = scene.CaptureBehaviours();
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
        }

        ScriptLoadContext newContext = new($"script-reload-{Guid.NewGuid():N}");
        Assembly assembly;
        ReloadReplacement[] replacements;
        try
        {
            assembly = newContext.LoadFromImages(compilation.PeImage, compilation.PdbImage);
            replacements = new ReloadReplacement[records.Length];
            for (int i = 0; i < records.Length; i++)
            {
                Type? newType = assembly.GetType(typeNames[i], throwOnError: false);
                if (!IsReloadableBehaviour(newType))
                {
                    throw new InvalidOperationException($"热重载程序集缺少可替换脚本类型：{typeNames[i]}。");
                }

                Behaviour replacement = CreateReloadedBehaviour(newType);
                if (snapshots.Length != 0)
                {
                    snapshots[i]?.Restore(replacement);
                }

                replacements[i] = new ReloadReplacement(records[i].Entity, records[i].Behaviour.GetType(), replacement);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            newContext.Unload();
            return HotReloadResult.ApplyFailed(compilation.Diagnostics, exception);
        }

        // 先销毁旧组件再挂载新实例，避免同一实体上并存两个同类型 Behaviour。
        for (int i = 0; i < replacements.Length; i++)
        {
            ReloadReplacement replacement = replacements[i];
            scene.DestroyComponent(replacement.Entity, replacement.OldType, _context);
        }

        for (int i = 0; i < replacements.Length; i++)
        {
            ReloadReplacement replacement = replacements[i];
            if (replacement.Component is not null)
            {
                scene.AddComponent(replacement.Entity, replacement.Component);
            }
        }

        records.AsSpan().Clear();
        WeakReference? oldReference = ReplaceCurrentContext(newContext);
        scene.DispatchStart(_context);
        return HotReloadResult.Reloaded(compilation.Diagnostics, oldReference, assembly);
    }

    /// <summary>
    /// 停止文件监视并标记服务已释放。
    /// </summary>
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

    /// <summary>
    /// 重置防抖计时器；连续保存只触发一次重载排队。
    /// </summary>
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

    /// <summary>
    /// 防抖到期后读取源目录全部 .cs 文件并排队重载。
    /// </summary>
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

    /// <summary>
    /// 从监视目录收集 .cs 源文件；以相对路径作为 Roslyn 语法树路径。
    /// </summary>
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
            // 以共享读方式打开，避免编辑器保存时文件被独占锁定。
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

    /// <summary>
    /// 切换当前 LoadContext 并卸载旧上下文；返回弱引用供调用方等待 GC 回收。
    /// </summary>
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
        lock (_gate)
        {
            _unloadedContexts.Add(reference);
        }

        return reference;
    }

    private int CountUnloadedLoadContexts(bool forceFullCollection)
    {
        if (forceFullCollection)
        {
            // 多次 Collect 以促使 AssemblyLoadContext 终结器运行。
            for (int i = 0; i < 50; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        lock (_gate)
        {
            for (int i = _unloadedContexts.Count - 1; i >= 0; i--)
            {
                if (!_unloadedContexts[i].IsAlive)
                {
                    _unloadedContexts.RemoveAt(i);
                }
            }

            return _unloadedContexts.Count;
        }
    }

    /// <summary>
    /// 判断类型是否为可热替换的具体 Behaviour（非抽象、有无参构造）。
    /// </summary>
    private static bool IsReloadableBehaviour(
        [NotNullWhen(true)]
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        Type? type)
    {
        return type is not null &&
            !type.IsAbstract &&
            typeof(Behaviour).IsAssignableFrom(type) &&
            type.GetConstructor(Type.EmptyTypes) is not null;
    }

    private static Behaviour CreateReloadedBehaviour(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        Type type)
    {
        return (Behaviour)Activator.CreateInstance(type)!;
    }

    /// <summary>
    /// 待应用的热重载请求快照。
    /// </summary>
    private sealed record PendingReload(string AssemblyName, ScriptSourceFile[] Sources, HotReloadOptions Options);

    /// <summary>
    /// 单个 Behaviour 的热替换映射：实体、旧类型与新实例。
    /// </summary>
    private readonly record struct ReloadReplacement(Entity Entity, Type OldType, Behaviour? Component);
}

/// <summary>
/// 文件监视热重载的配置项。
/// </summary>
internal sealed record HotReloadWatchOptions(string AssemblyName, string SourceDirectory)
{
    /// <summary>
    /// 源文件搜索模式，默认匹配所有 .cs 文件。
    /// </summary>
    public string SearchPattern { get; init; } = "*.cs";

    /// <summary>
    /// 是否递归监视子目录。
    /// </summary>
    public bool IncludeSubdirectories { get; init; } = true;

    /// <summary>
    /// 文件变更防抖间隔，避免连续保存触发多次编译。
    /// </summary>
    public TimeSpan DebounceInterval { get; init; } = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// 监视触发重载时使用的状态保留策略。
    /// </summary>
    public HotReloadOptions ReloadOptions { get; init; } = HotReloadOptions.PreservePublicAndPersisted;
}

/// <summary>
/// 热重载时的脚本状态保留策略。
/// </summary>
internal readonly record struct HotReloadOptions(bool PreserveState)
{
    /// <summary>
    /// 保留公开字段/属性及带 <see cref="PersistAttribute"/> 的成员。
    /// </summary>
    public static HotReloadOptions PreservePublicAndPersisted { get; } = new(PreserveState: true);

    /// <summary>
    /// 完全重置，不迁移任何字段状态。
    /// </summary>
    public static HotReloadOptions FullReset { get; } = new(PreserveState: false);
}

/// <summary>
/// 单次热重载应用的结果载体。
/// </summary>
internal sealed class HotReloadResult
{
    private HotReloadResult(
        HotReloadStatus status,
        ImmutableArray<Diagnostic> diagnostics,
        WeakReference? unloadedContext,
        Exception? exception,
        Assembly? loadedAssembly)
    {
        Status = status;
        Diagnostics = diagnostics;
        UnloadedContext = unloadedContext;
        Exception = exception;
        LoadedAssembly = loadedAssembly;
    }

    /// <summary>
    /// 热重载结果状态码。
    /// </summary>
    public HotReloadStatus Status { get; }

    /// <summary>
    /// Roslyn 编译/发出阶段产生的诊断信息。
    /// </summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    /// <summary>
    /// 被替换的旧 LoadContext 弱引用；用于检测卸载是否完成。
    /// </summary>
    public WeakReference? UnloadedContext { get; }

    /// <summary>
    /// 应用阶段异常；编译失败时为 null。
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// 成功加载的新脚本程序集。
    /// </summary>
    public Assembly? LoadedAssembly { get; }

    /// <summary>
    /// 旧 LoadContext 是否已被 GC 回收（或无旧上下文）。
    /// </summary>
    public bool OldContextUnloaded => UnloadedContext is null || WaitForUnload(UnloadedContext);

    /// <summary>构造“无待处理请求”结果。</summary>
    public static HotReloadResult NoPending()
    {
        return new HotReloadResult(HotReloadStatus.NoPendingReload, [], null, null, null);
    }

    /// <summary>构造 Roslyn 编译失败结果。</summary>
    public static HotReloadResult CompileFailed(ImmutableArray<Diagnostic> diagnostics)
    {
        return new HotReloadResult(HotReloadStatus.CompileFailed, diagnostics, null, null, null);
    }

    /// <summary>构造编译成功但替换/挂载失败结果。</summary>
    public static HotReloadResult ApplyFailed(ImmutableArray<Diagnostic> diagnostics, Exception exception)
    {
        return new HotReloadResult(HotReloadStatus.ApplyFailed, diagnostics, null, exception, null);
    }

    /// <summary>构造热重载成功结果。</summary>
    public static HotReloadResult Reloaded(ImmutableArray<Diagnostic> diagnostics, WeakReference? unloadedContext, Assembly loadedAssembly)
    {
        ArgumentNullException.ThrowIfNull(loadedAssembly);
        return new HotReloadResult(HotReloadStatus.Reloaded, diagnostics, unloadedContext, null, loadedAssembly);
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

/// <summary>
/// 热重载应用结果的状态枚举。
/// </summary>
internal enum HotReloadStatus
{
    /// <summary>无待处理请求。</summary>
    NoPendingReload,
    /// <summary>编译失败，未替换程序集。</summary>
    CompileFailed,
    /// <summary>编译成功但替换/挂载阶段失败。</summary>
    ApplyFailed,
    /// <summary>重载成功完成。</summary>
    Reloaded,
}
