using System.Reflection;
using Microsoft.CodeAnalysis;
using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// Roslyn 脚本编译与可回收装载上下文测试。
/// </summary>
public sealed class ScriptCompilerTests
{
    /// <summary>
    /// 验证脚本编译器能引用引擎公开 API 并产出可装载程序集。
    /// </summary>
    [Fact]
    public void CompilerBuildsAssemblyAgainstScriptingPublicApi()
    {
        ScriptCompiler compiler = new();
        ScriptCompilationResult result = compiler.Compile(
            "PixelEngine.UserScripts.Tests",
            [new ScriptSourceFile("PlayerScript.cs", ValidScriptSource)]);

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.NotEmpty(result.PeImage);
        ScriptLoadContext loadContext = new("script-test-success");
        Assembly assembly = loadContext.LoadFromImages(result.PeImage, result.PdbImage);
        Type? type = assembly.GetType("UserScripts.PlayerScript");

        Assert.NotNull(type);
        Assert.True(typeof(Behaviour).IsAssignableFrom(type));
        Assert.Same(typeof(Behaviour).Assembly, type.BaseType?.Assembly);
        loadContext.Unload();
    }

    /// <summary>
    /// 验证编译失败会返回诊断且不产出程序集字节。
    /// </summary>
    [Fact]
    public void CompilerReturnsDiagnosticsWithoutImageOnFailure()
    {
        ScriptCompiler compiler = new();
        ScriptCompilationResult result = compiler.Compile(
            "PixelEngine.UserScripts.Broken",
            [new ScriptSourceFile("Broken.cs", "namespace UserScripts; public sealed class Broken : MissingBase { }")]);

        Assert.False(result.Success);
        Assert.Empty(result.PeImage);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>
    /// 验证用户脚本程序集所在 ALC 可以卸载，且引擎类型不会被复制进用户 ALC。
    /// </summary>
    [Fact]
    public void ScriptLoadContextCanUnloadCompiledAssembly()
    {
        WeakReference reference = CompileAndUnload();

        for (int i = 0; reference.IsAlive && i < 10; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(reference.IsAlive);
    }

    private static WeakReference CompileAndUnload()
    {
        ScriptCompiler compiler = new();
        ScriptCompilationResult result = compiler.Compile(
            $"PixelEngine.UserScripts.Unload.{Guid.NewGuid():N}",
            [new ScriptSourceFile("UnloadScript.cs", ValidScriptSource)]);
        Assert.True(result.Success, FormatDiagnostics(result));

        ScriptLoadContext loadContext = new("script-test-unload");
        Assembly assembly = loadContext.LoadFromImages(result.PeImage, result.PdbImage);
        Assert.NotNull(assembly.GetType("UserScripts.PlayerScript"));
        WeakReference reference = new(loadContext, trackResurrection: false);
        loadContext.Unload();
        return reference;
    }

    private static string FormatDiagnostics(ScriptCompilationResult result)
    {
        return string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    }

    private const string ValidScriptSource = """
        using PixelEngine.Scripting;

        namespace UserScripts;

        public sealed class PlayerScript : Behaviour
        {
            protected override void OnUpdate(float dt)
            {
            }
        }
        """;
}
