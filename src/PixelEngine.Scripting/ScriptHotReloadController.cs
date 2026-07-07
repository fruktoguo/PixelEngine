using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;

namespace PixelEngine.Scripting;

/// <summary>
/// Editor 可调用的脚本热重载门面，内部使用 Roslyn 编译与可卸载 ALC。
/// </summary>
/// <param name="scene">要重载的脚本场景。</param>
/// <param name="context">脚本上下文，用于销毁旧组件与启动新组件。</param>
public sealed class ScriptHotReloadController(Scene scene, IScriptContext context) : IDisposable
{
    private readonly HotReloadService _service = new(
        scene ?? throw new ArgumentNullException(nameof(scene)),
        context ?? throw new ArgumentNullException(nameof(context)),
        new ScriptCompiler());

    /// <summary>
    /// 是否已有待应用的热重载请求。
    /// </summary>
    public bool HasPendingReload => _service.HasPendingReload;

    /// <summary>
    /// 文件监听器最近一次遇到的异常。
    /// </summary>
    public Exception? LastWatcherException => _service.LastWatcherException;

    /// <summary>
    /// 已调用 Unload 但尚未被 GC 回收的旧脚本 ALC 数量。
    /// </summary>
    public int UnloadedLoadContextAliveCount => _service.UnloadedLoadContextAliveCount;

    /// <summary>
    /// 触发完整 GC 后返回仍存活的旧脚本 ALC 数量，用于泄漏检测证据采集。
    /// </summary>
    /// <returns>已卸载但仍存活的旧脚本 ALC 数量。</returns>
    public int CollectAndCountUnloadedLoadContextsAlive()
    {
        return _service.CollectAndCountUnloadedLoadContextsAlive();
    }

    /// <summary>
    /// 从源目录读取 C# 脚本并排队一次热重载。
    /// </summary>
    /// <param name="assemblyName">动态脚本程序集名。</param>
    /// <param name="sourceDirectory">脚本源目录。</param>
    /// <param name="preserveState">是否保留公开字段与 Persist 字段状态。</param>
    /// <param name="searchPattern">源文件搜索模式。</param>
    /// <param name="includeSubdirectories">是否递归搜索子目录。</param>
    public void RequestReloadFromDirectory(
        string assemblyName,
        string sourceDirectory,
        bool preserveState = true,
        string searchPattern = "*.cs",
        bool includeSubdirectories = true)
    {
        ScriptSourceFile[] sources = LoadSourceFiles(sourceDirectory, searchPattern, includeSubdirectories);
        _service.RequestReload(assemblyName, sources, new HotReloadOptions(preserveState));
    }

    /// <summary>
    /// 开始监听脚本源目录，并在变更 debounce 后排队热重载。
    /// </summary>
    /// <param name="assemblyName">动态脚本程序集名。</param>
    /// <param name="sourceDirectory">脚本源目录。</param>
    /// <param name="preserveState">是否保留公开字段与 Persist 字段状态。</param>
    /// <param name="searchPattern">源文件搜索模式。</param>
    /// <param name="includeSubdirectories">是否递归搜索子目录。</param>
    /// <param name="debounceInterval">变更 debounce 时间。</param>
    public void StartWatching(
        string assemblyName,
        string sourceDirectory,
        bool preserveState = true,
        string searchPattern = "*.cs",
        bool includeSubdirectories = true,
        TimeSpan? debounceInterval = null)
    {
        _service.StartWatching(new HotReloadWatchOptions(assemblyName, sourceDirectory)
        {
            SearchPattern = searchPattern,
            IncludeSubdirectories = includeSubdirectories,
            DebounceInterval = debounceInterval ?? TimeSpan.FromMilliseconds(150),
            ReloadOptions = new HotReloadOptions(preserveState),
        });
    }

    /// <summary>
    /// 应用当前待处理热重载请求。
    /// </summary>
    /// <returns>热重载结果与诊断文本。</returns>
    public ScriptHotReloadApplyResult ApplyPendingReload()
    {
        HotReloadResult result = _service.ApplyPendingReload();
        return new ScriptHotReloadApplyResult(
            MapStatus(result.Status),
            DiagnosticsToStrings(result.Diagnostics, result.Exception),
            result.OldContextUnloaded,
            result.LoadedAssembly);
    }

    /// <summary>
    /// 将热重载目标切换到新的脚本 Scene，供编辑态 authoring projection 刷新后继续复用同一 controller。
    /// </summary>
    /// <param name="scene">新的脚本 Scene。</param>
    public void ReplaceScene(Scene scene)
    {
        _service.ReplaceScene(scene);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _service.Dispose();
    }

    private static ScriptHotReloadStatus MapStatus(HotReloadStatus status)
    {
        return status switch
        {
            HotReloadStatus.NoPendingReload => ScriptHotReloadStatus.NoPendingReload,
            HotReloadStatus.CompileFailed => ScriptHotReloadStatus.CompileFailed,
            HotReloadStatus.ApplyFailed => ScriptHotReloadStatus.ApplyFailed,
            HotReloadStatus.Reloaded => ScriptHotReloadStatus.Reloaded,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "未知热重载状态。"),
        };
    }

    private static string[] DiagnosticsToStrings(ImmutableArray<Diagnostic> diagnostics, Exception? exception)
    {
        string[] values = new string[diagnostics.Length + (exception is null ? 0 : 1)];
        for (int i = 0; i < diagnostics.Length; i++)
        {
            values[i] = diagnostics[i].ToString();
        }

        if (exception is not null)
        {
            values[^1] = exception.ToString();
        }

        return values;
    }

    private static ScriptSourceFile[] LoadSourceFiles(string sourceDirectory, string searchPattern, bool includeSubdirectories)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"脚本源目录不存在：{sourceDirectory}");
        }

        SearchOption searchOption = includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        string[] paths =
        [
            .. Directory.GetFiles(sourceDirectory, searchPattern, searchOption)
                .Where(static path => string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
        if (paths.Length == 0)
        {
            throw new InvalidOperationException($"脚本源目录没有匹配的 C# 文件：{sourceDirectory}");
        }

        ScriptSourceFile[] sources = new ScriptSourceFile[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            string path = paths[i];
            string relativePath = Path.GetRelativePath(sourceDirectory, path);
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            sources[i] = new ScriptSourceFile(relativePath, reader.ReadToEnd());
        }

        return sources;
    }
}

/// <summary>
/// Editor 可见的脚本热重载状态。
/// </summary>
public enum ScriptHotReloadStatus
{
    /// <summary>
    /// 没有待处理热重载请求。
    /// </summary>
    NoPendingReload,

    /// <summary>
    /// 脚本编译失败。
    /// </summary>
    CompileFailed,

    /// <summary>
    /// 脚本编译成功，但装载、实例化或状态恢复失败；旧脚本保持运行。
    /// </summary>
    ApplyFailed,

    /// <summary>
    /// 脚本已完成重载。
    /// </summary>
    Reloaded,
}

/// <summary>
/// Editor 可见的脚本热重载应用结果。
/// </summary>
/// <param name="Status">热重载状态。</param>
/// <param name="Diagnostics">Roslyn 诊断文本。</param>
/// <param name="OldContextUnloaded">旧 ALC 是否已卸载。</param>
/// <param name="LoadedAssembly">本次热重载成功加载的动态脚本程序集。</param>
public readonly record struct ScriptHotReloadApplyResult(
    ScriptHotReloadStatus Status,
    string[] Diagnostics,
    bool OldContextUnloaded,
    Assembly? LoadedAssembly);
