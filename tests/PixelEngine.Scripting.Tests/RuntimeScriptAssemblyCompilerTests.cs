using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// 玩家包随包脚本编译测试。
/// </summary>
public sealed class RuntimeScriptAssemblyCompilerTests
{
    /// <summary>
    /// 验证运行时能从目录编译并加载 Behaviour 程序集。
    /// </summary>
    [Fact]
    public void CompileAndLoadFromDirectoryLoadsBehaviourAssembly()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PixelEngine.RuntimeScriptAssemblyCompilerTests", Guid.NewGuid().ToString("N"));
        try
        {
            _ = Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "PackagedProbeBehaviour.cs"), """
                using PixelEngine.Scripting;

                public sealed class PackagedProbeBehaviour : Behaviour
                {
                }
                """);

            RuntimeScriptAssemblyCompileResult result = RuntimeScriptAssemblyCompiler.CompileAndLoadFromDirectory(
                $"PixelEngine.Tests.PackagedScripts.{Guid.NewGuid():N}",
                directory);

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.True(result.HasSources);
            Assert.NotNull(result.Assembly);
            Type? type = result.Assembly.GetType("PackagedProbeBehaviour", throwOnError: false);
            Assert.NotNull(type);
            Assert.True(typeof(Behaviour).IsAssignableFrom(type));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
