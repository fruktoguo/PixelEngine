namespace PixelEngine.Scripting;

/// <summary>
/// Hosting 装配脚本运行时时使用的热重载源目录配置。
/// </summary>
/// <param name="AssemblyName">动态脚本程序集名。</param>
/// <param name="SourceDirectory">脚本源文件目录。</param>
/// <param name="PreserveState">热重载时是否保留公开字段与 Persist 字段状态。</param>
/// <param name="SearchPattern">监听与加载的源文件匹配模式。</param>
/// <param name="IncludeSubdirectories">是否递归监听子目录。</param>
/// <param name="DebounceInterval">文件变更去抖时间；为 null 时使用默认值。</param>
public sealed record ScriptHotReloadRuntimeOptions(
    string AssemblyName,
    string SourceDirectory,
    bool PreserveState = true,
    string SearchPattern = "*.cs",
    bool IncludeSubdirectories = true,
    TimeSpan? DebounceInterval = null);
