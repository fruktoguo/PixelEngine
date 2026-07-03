namespace PixelEngine.Scripting;

/// <summary>
/// NativeAOT 发行通道的脚本热重载门面；动态编译与可卸载 ALC 在该通道不可用。
/// </summary>
/// <param name="scene">要重载的脚本场景。</param>
/// <param name="context">脚本上下文。</param>
public sealed class ScriptHotReloadController(Scene scene, IScriptContext context) : IDisposable
{
    private readonly Scene _scene = scene ?? throw new ArgumentNullException(nameof(scene));
    private readonly IScriptContext _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>
    /// NativeAOT 通道永远不会排队热重载请求。
    /// </summary>
    public bool HasPendingReload => false;

    /// <summary>
    /// NativeAOT 通道没有文件监听器，因此不会产生监听异常。
    /// </summary>
    public Exception? LastWatcherException => null;

    /// <summary>
    /// NativeAOT 通道不支持从源码目录动态编译脚本。
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
        _ = assemblyName;
        _ = sourceDirectory;
        _ = preserveState;
        _ = searchPattern;
        _ = includeSubdirectories;
        ThrowNativeAotNotSupported();
    }

    /// <summary>
    /// NativeAOT 通道不支持监听源码目录并动态编译脚本。
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
        _ = assemblyName;
        _ = sourceDirectory;
        _ = preserveState;
        _ = searchPattern;
        _ = includeSubdirectories;
        _ = debounceInterval;
        ThrowNativeAotNotSupported();
    }

    /// <summary>
    /// NativeAOT 通道没有待处理热重载请求，始终返回 NoPendingReload。
    /// </summary>
    /// <returns>热重载结果与诊断文本。</returns>
    public ScriptHotReloadApplyResult ApplyPendingReload()
    {
        _ = _scene;
        _ = _context;
        return new ScriptHotReloadApplyResult(
            ScriptHotReloadStatus.NoPendingReload,
            [],
            OldContextUnloaded: true);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private static void ThrowNativeAotNotSupported()
    {
        throw new NotSupportedException("NativeAOT 发行通道不支持 Roslyn 动态脚本热重载。");
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
/// <param name="Diagnostics">诊断文本。</param>
/// <param name="OldContextUnloaded">旧 ALC 是否已卸载。</param>
public readonly record struct ScriptHotReloadApplyResult(
    ScriptHotReloadStatus Status,
    string[] Diagnostics,
    bool OldContextUnloaded);
