using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace PixelEngine.Scripting;

internal sealed class ScriptCompiler
{
    private static readonly CSharpCompilationOptions CompilationOptions = new(
        OutputKind.DynamicallyLinkedLibrary,
        optimizationLevel: OptimizationLevel.Release,
        allowUnsafe: false);

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

    public ScriptCompilationResult Compile(string assemblyName, IReadOnlyList<ScriptSourceFile> sources, bool emitPdb = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            throw new ArgumentException("脚本源文件列表不能为空。", nameof(sources));
        }

        SyntaxTree[] trees = new SyntaxTree[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            ScriptSourceFile source = sources[i];
            SourceText text = SourceText.From(source.Source, Encoding.UTF8);
            trees[i] = CSharpSyntaxTree.ParseText(text, ParseOptions, source.Path);
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
            if (assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location))
            {
                continue;
            }

            string name = assembly.GetName().Name ?? assembly.Location;
            _ = references.TryAdd(name, MetadataReference.CreateFromFile(assembly.Location));
        }

        return [.. references.Values];
    }

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

internal readonly record struct ScriptSourceFile(string Path, string Source);

internal sealed class ScriptCompilationResult
{
    private ScriptCompilationResult(bool success, byte[] peImage, byte[] pdbImage, ImmutableArray<Diagnostic> diagnostics)
    {
        Success = success;
        PeImage = peImage;
        PdbImage = pdbImage;
        Diagnostics = diagnostics;
    }

    public bool Success { get; }

    public byte[] PeImage { get; }

    public byte[] PdbImage { get; }

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
