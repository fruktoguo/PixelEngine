namespace PixelEngine.Scripting;

/// <summary>
/// 脚本热重载诊断类别，用于 Hosting 或 Editor 订阅编译与监听状态。
/// </summary>
public enum ScriptHotReloadDiagnosticKind
{
    /// <summary>
    /// 文件监听器已成功启动。
    /// </summary>
    WatcherStarted,

    /// <summary>
    /// 文件监听器启动失败。
    /// </summary>
    WatcherStartFailed,

    /// <summary>
    /// 文件监听器运行中遇到异常。
    /// </summary>
    WatcherException,

    /// <summary>
    /// 一次热重载应用结果。
    /// </summary>
    ReloadResult,
}

/// <summary>
/// 脚本热重载诊断事件；由 Scripting runtime 在相位 1 或 Hosting 装配期发布。
/// </summary>
/// <param name="Timestamp">诊断产生时间。</param>
/// <param name="Kind">诊断类别。</param>
/// <param name="Status">热重载状态；非重载结果诊断使用 <see cref="ScriptHotReloadStatus.NoPendingReload" />。</param>
/// <param name="Message">面向工具或 Console 的诊断摘要。</param>
/// <param name="Diagnostics">Roslyn 或 watcher 的详细诊断文本。</param>
public readonly record struct ScriptHotReloadDiagnostic(
    DateTimeOffset Timestamp,
    ScriptHotReloadDiagnosticKind Kind,
    ScriptHotReloadStatus Status,
    string Message,
    string[] Diagnostics);

/// <summary>
/// 脚本热重载诊断 sink；Hosting/Scripting 通过它把编译失败、应用失败与 watcher 诊断发布给 Editor Console 等消费者。
/// </summary>
public interface IScriptHotReloadDiagnosticSink
{
    /// <summary>
    /// 接收一条脚本热重载诊断。
    /// </summary>
    /// <param name="diagnostic">诊断事件。</param>
    void Report(ScriptHotReloadDiagnostic diagnostic);
}
