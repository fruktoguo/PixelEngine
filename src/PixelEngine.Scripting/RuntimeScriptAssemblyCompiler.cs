using System.Reflection;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;

namespace PixelEngine.Scripting;

/// <summary>
/// 运行时脚本程序集编译入口，用于玩家包在启动时加载随包 C# Behaviour。
/// </summary>
public static class RuntimeScriptAssemblyCompiler
{
    /// <summary>
    /// 当前运行时是否支持动态脚本编译与加载。
    /// </summary>
    public static bool IsSupported => RuntimeFeature.IsDynamicCodeSupported;

    /// <summary>
    /// 从目录编译并加载一个脚本程序集。
    /// </summary>
    /// <param name="assemblyName">动态脚本程序集名。</param>
    /// <param name="sourceDirectory">脚本源码目录。</param>
    /// <param name="includeSubdirectories">是否递归搜索子目录。</param>
    /// <returns>编译和加载结果。</returns>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "随包脚本是玩家包内容边界，运行时加载的 Behaviour 类型由脚本程序集自身提供，不属于静态 trim 闭包。")]
    public static RuntimeScriptAssemblyCompileResult CompileAndLoadFromDirectory(
        string assemblyName,
        string sourceDirectory,
        bool includeSubdirectories = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
#if PIXELENGINE_NATIVEAOT
        return RuntimeScriptAssemblyCompileResult.Failed(
            "当前 NativeAOT 运行时不支持动态脚本编译。",
            []);
#else
        if (!IsSupported)
        {
            return RuntimeScriptAssemblyCompileResult.Failed(
                "当前运行时不支持动态脚本编译。",
                []);
        }

        string root = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(root))
        {
            return RuntimeScriptAssemblyCompileResult.NoSources(root);
        }

        SearchOption searchOption = includeSubdirectories
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;
        string[] files = [.. Directory.EnumerateFiles(root, "*.cs", searchOption)
            .Order(StringComparer.OrdinalIgnoreCase)];
        if (files.Length == 0)
        {
            return RuntimeScriptAssemblyCompileResult.NoSources(root);
        }

        ScriptSourceFile[] sources = new ScriptSourceFile[files.Length];
        for (int i = 0; i < files.Length; i++)
        {
            sources[i] = new ScriptSourceFile(files[i], File.ReadAllText(files[i]));
        }

        ScriptCompiler compiler = new();
        ScriptCompilationResult result = compiler.Compile(assemblyName, sources, emitPdb: false);
        string[] diagnostics = [.. result.Diagnostics.Select(static diagnostic => diagnostic.ToString())];
        if (!result.Success)
        {
            return RuntimeScriptAssemblyCompileResult.Failed("随包脚本编译失败。", diagnostics);
        }

        Assembly assembly = result.PdbImage.Length == 0
            ? Assembly.Load(result.PeImage)
            : Assembly.Load(result.PeImage, result.PdbImage);
        return RuntimeScriptAssemblyCompileResult.Succeeded(assembly, diagnostics);
#endif
    }
}

/// <summary>
/// 运行时脚本程序集编译结果。
/// </summary>
public sealed class RuntimeScriptAssemblyCompileResult
{
    private RuntimeScriptAssemblyCompileResult(
        bool success,
        bool hasSources,
        Assembly? assembly,
        string? error,
        string[] diagnostics)
    {
        Success = success;
        HasSources = hasSources;
        Assembly = assembly;
        Error = error;
        Diagnostics = diagnostics;
    }

    /// <summary>
    /// 编译并加载是否成功。
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// 源目录中是否存在脚本源码。
    /// </summary>
    public bool HasSources { get; }

    /// <summary>
    /// 编译成功后加载的脚本程序集。
    /// </summary>
    public Assembly? Assembly { get; }

    /// <summary>
    /// 失败原因。
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// 编译诊断文本。
    /// </summary>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>
    /// 创建成功结果。
    /// </summary>
    public static RuntimeScriptAssemblyCompileResult Succeeded(Assembly assembly, string[] diagnostics)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return new RuntimeScriptAssemblyCompileResult(true, true, assembly, null, diagnostics);
    }

    /// <summary>
    /// 创建无源码结果。
    /// </summary>
    public static RuntimeScriptAssemblyCompileResult NoSources(string sourceDirectory)
    {
        return new RuntimeScriptAssemblyCompileResult(
            success: true,
            hasSources: false,
            assembly: null,
            error: $"脚本源目录不存在或没有 .cs 文件：{sourceDirectory}",
            diagnostics: []);
    }

    /// <summary>
    /// 创建失败结果。
    /// </summary>
    public static RuntimeScriptAssemblyCompileResult Failed(string error, string[] diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new RuntimeScriptAssemblyCompileResult(false, true, null, error, diagnostics);
    }
}
