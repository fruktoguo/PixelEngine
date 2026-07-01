using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// plan/16 profiling 与反汇编工具链纪律测试。
/// </summary>
public sealed class PerformanceHardeningToolingDisciplineTests
{
    /// <summary>
    /// 验证 BenchmarkDotNet 入口默认接入内存、线程与反汇编 diagnoser。
    /// </summary>
    [Fact]
    public void BenchmarkProgramEnablesRequiredDiagnosers()
    {
        string program = ReadRepositoryFile("bench", "PixelEngine.Benchmarks", "Program.cs");

        Assert.Contains("MemoryDiagnoser.Default", program, StringComparison.Ordinal);
        Assert.Contains("ThreadingDiagnoser.Default", program, StringComparison.Ordinal);
        Assert.Contains("DisassemblyDiagnoser", program, StringComparison.Ordinal);
        Assert.Contains("DisassemblyDiagnoserConfig", program, StringComparison.Ordinal);
        Assert.Contains("BenchmarkSwitcher.FromAssembly", program, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 CI 运行反汇编守门与 BenchmarkDotNet 性能回归门禁。
    /// </summary>
    [Fact]
    public void CiRunsDisassemblyAndBenchmarkRegressionGuards()
    {
        string ci = ReadRepositoryFile(".github", "workflows", "ci.yml");
        string regression = ReadRepositoryFile("tools", "benchmark-regression.ps1");
        string disassembly = ReadRepositoryFile("tools", "disassembly-guard.ps1");
        string baseline = ReadRepositoryFile("bench", "PixelEngine.Benchmarks", "baselines", "ci-baseline.json");

        Assert.Contains("benchmark-guard", ci, StringComparison.Ordinal);
        Assert.Contains("./tools/disassembly-guard.ps1", ci, StringComparison.Ordinal);
        Assert.Contains("./tools/benchmark-regression.ps1", ci, StringComparison.Ordinal);
        Assert.Contains("BenchmarkDotNet regression run", regression, StringComparison.Ordinal);
        Assert.Contains("maxRatio", regression, StringComparison.Ordinal);
        Assert.Contains("RNGCHKFAIL", disassembly, StringComparison.Ordinal);
        Assert.Contains("ymm|zmm", disassembly, StringComparison.Ordinal);
        Assert.Contains("\"benchmarks\"", baseline, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 JIT/BDN/IDE 反汇编流程有可复现命令文档。
    /// </summary>
    [Fact]
    public void DisassemblyWorkflowIsDocumentedWithReproducibleCommands()
    {
        string document = ReadRepositoryFile("docs", "performance-disassembly-workflow.md");

        Assert.Contains("DOTNET_JitDisasm", document, StringComparison.Ordinal);
        Assert.Contains("Disasmo", document, StringComparison.Ordinal);
        Assert.Contains("Rider", document, StringComparison.Ordinal);
        Assert.Contains("tools/disassembly-guard.ps1", document, StringComparison.Ordinal);
        Assert.Contains("tools/benchmark-regression.ps1", document, StringComparison.Ordinal);
        Assert.Contains("RNGCHKFAIL", document, StringComparison.Ordinal);
        Assert.Contains("ymm", document, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] relativePath)
    {
        return File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. relativePath]));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PixelEngine.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("无法从测试输出目录定位 PixelEngine.sln。");
    }
}
