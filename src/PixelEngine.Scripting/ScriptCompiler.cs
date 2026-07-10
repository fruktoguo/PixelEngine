using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace PixelEngine.Scripting;

/// <summary>
/// 使用 Roslyn 将脚本源文件编译为内存中的动态程序集镜像。
/// </summary>
internal sealed class ScriptCompiler
{
    private const string ImplicitUsingsSource = """
        global using global::System;
        global using global::System.Collections.Generic;
        global using global::System.IO;
        global using global::System.Linq;
        global using global::System.Net.Http;
        global using global::System.Threading;
        global using global::System.Threading.Tasks;
        """;

    private static readonly CSharpCompilationOptions CompilationOptions = new(
        OutputKind.DynamicallyLinkedLibrary,
        optimizationLevel: OptimizationLevel.Release,
        allowUnsafe: false,
        nullableContextOptions: NullableContextOptions.Enable);

    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default
        .WithLanguageVersion(LanguageVersion.CSharp14);

    private readonly MetadataReference[] _references;

    public ScriptCompiler()
        : this(AppDomain.CurrentDomain.GetAssemblies())
    {
    }

    public ScriptCompiler(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        _references = BuildReferences(assemblies);
    }

    /// <summary>
    /// 编译脚本源文件列表为 PE（及可选 PDB）字节数组。
    /// </summary>
    /// <param name="assemblyName">输出程序集名称。</param>
    /// <param name="sources">源文件集合，路径用于诊断定位。</param>
    /// <param name="emitPdb">是否同时发出调试符号。</param>
    public ScriptCompilationResult Compile(string assemblyName, IReadOnlyList<ScriptSourceFile> sources, bool emitPdb = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            throw new ArgumentException("脚本源文件列表不能为空。", nameof(sources));
        }

        // 与 Microsoft.NET.Sdk 的 ImplicitUsings=enable / Nullable=enable 对齐，保证同一份工程源码
        // 在 Player 静态编译和 Editor 热编译中具有相同的基础语言上下文。
        SyntaxTree[] trees = new SyntaxTree[sources.Count + 1];
        trees[0] = CSharpSyntaxTree.ParseText(
            SourceText.From(ImplicitUsingsSource, Encoding.UTF8),
            ParseOptions,
            "PixelEngine.ScriptImplicitUsings.g.cs");
        for (int i = 0; i < sources.Count; i++)
        {
            ScriptSourceFile source = sources[i];
            SourceText text = SourceText.From(source.Source, Encoding.UTF8);
            trees[i + 1] = CSharpSyntaxTree.ParseText(text, ParseOptions, source.Path);
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            trees,
            _references,
            CompilationOptions);

        using MemoryStream peStream = new();
        using MemoryStream? pdbStream = emitPdb ? new MemoryStream() : null;
        EmitResult emit = pdbStream is null
            ? compilation.Emit(peStream)
            : compilation.Emit(peStream, pdbStream);
        ImmutableArray<Diagnostic> diagnostics = emit.Diagnostics;
        return emit.Success
            ? ScriptCompilationResult.Succeeded(peStream.ToArray(), pdbStream?.ToArray() ?? [], diagnostics)
            : ScriptCompilationResult.Failed(diagnostics);
    }

    /// <summary>
    /// 合并可信平台程序集与当前 AppDomain 已加载程序集，构建 Roslyn 元数据引用集。
    /// </summary>
    [UnconditionalSuppressMessage(
        "SingleFile",
        "IL3000",
        Justification = "Runtime script compilation treats empty Assembly.Location as unavailable and falls back to TRUSTED_PLATFORM_ASSEMBLIES.")]
    private static MetadataReference[] BuildReferences(IEnumerable<Assembly> assemblies)
    {
        Dictionary<string, MetadataReference> references = new(StringComparer.OrdinalIgnoreCase);
        AddTrustedPlatformAssemblies(references);
        foreach (Assembly assembly in assemblies)
        {
            // 动态程序集与单文件发布中 Location 为空的程序集无法作为文件引用。
            if (assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location))
            {
                continue;
            }

            string name = assembly.GetName().Name ?? assembly.Location;
            _ = references.TryAdd(name, MetadataReference.CreateFromFile(assembly.Location));
        }

        return [.. references.Values];
    }

    /// <summary>
    /// 从 TRUSTED_PLATFORM_ASSEMBLIES 环境数据补充 BCL 引用，覆盖单文件场景。
    /// </summary>
    private static void AddTrustedPlatformAssemblies(Dictionary<string, MetadataReference> references)
    {
        string? trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            return;
        }

        foreach (string path in trustedPlatformAssemblies.Split(Path.PathSeparator))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            _ = references.TryAdd(name, MetadataReference.CreateFromFile(path));
        }
    }
}

/// <summary>
/// 单份脚本源文件及其虚拟路径。
/// </summary>
/// <param name="Path">相对或逻辑路径，写入 Roslyn 诊断。</param>
/// <param name="Source">UTF-8 源文本。</param>
internal readonly record struct ScriptSourceFile(string Path, string Source);

/// <summary>
/// Roslyn 编译/发出操作的结果。
/// </summary>
internal sealed class ScriptCompilationResult
{
    private ScriptCompilationResult(bool success, byte[] peImage, byte[] pdbImage, ImmutableArray<Diagnostic> diagnostics)
    {
        Success = success;
        PeImage = peImage;
        PdbImage = pdbImage;
        Diagnostics = diagnostics;
    }

    /// <summary>编译与发出是否均成功。</summary>
    public bool Success { get; }

    /// <summary>PE 镜像字节；失败时为空数组。</summary>
    public byte[] PeImage { get; }

    /// <summary>PDB 镜像字节；未发出或失败时为空数组。</summary>
    public byte[] PdbImage { get; }

    /// <summary>Roslyn 诊断集合（含警告与错误）。</summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public static ScriptCompilationResult Succeeded(byte[] peImage, byte[] pdbImage, ImmutableArray<Diagnostic> diagnostics)
    {
        return new ScriptCompilationResult(true, peImage, pdbImage, diagnostics);
    }

    public static ScriptCompilationResult Failed(ImmutableArray<Diagnostic> diagnostics)
    {
        return new ScriptCompilationResult(false, [], [], diagnostics);
    }
}
