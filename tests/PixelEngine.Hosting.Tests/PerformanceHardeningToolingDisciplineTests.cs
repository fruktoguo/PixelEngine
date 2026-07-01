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
        Assert.Contains("HardwareIntrinsics", disassembly, StringComparison.Ordinal);
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

    /// <summary>
    /// 验证发行编译模式保持默认 R2R 运行时 light-up，AOT 显式 ISA 并跑 SIMD 反汇编探针。
    /// </summary>
    [Fact]
    public void ReleasePublishModesPreserveR2RLightUpAndAotIsaAudit()
    {
        string props = ReadRepositoryFile("Directory.Build.props");
        string release = ReadRepositoryFile(".github", "workflows", "release.yml");
        string aotProbePs1 = ReadRepositoryFile("tools", "aot-simd-probe.ps1");
        string aotProbeSh = ReadRepositoryFile("tools", "aot-simd-probe.sh");

        Assert.Contains("Condition=\"'$(Channel)' == 'R2R'\"", props, StringComparison.Ordinal);
        Assert.Contains("<PublishReadyToRun>true</PublishReadyToRun>", props, StringComparison.Ordinal);
        Assert.Contains("<PublishReadyToRunComposite>true</PublishReadyToRunComposite>", props, StringComparison.Ordinal);
        Assert.Contains("<TieredPGO>true</TieredPGO>", props, StringComparison.Ordinal);
        Assert.Contains("不设置 baseline ISA", props, StringComparison.Ordinal);

        Assert.Contains("Condition=\"'$(Channel)' == 'AOT'\"", props, StringComparison.Ordinal);
        Assert.Contains("<PublishAot>true</PublishAot>", props, StringComparison.Ordinal);
        Assert.Contains("<IlcInstructionSet>x86-64-v3</IlcInstructionSet>", props, StringComparison.Ordinal);
        Assert.Contains("<IlcInstructionSet>armv8.2-a,lse,rcpc</IlcInstructionSet>", props, StringComparison.Ordinal);
        Assert.Contains("<IlcInstructionSet>apple-m1,lse,rcpc</IlcInstructionSet>", props, StringComparison.Ordinal);

        Assert.Contains("AOT SIMD probe", release, StringComparison.Ordinal);
        Assert.Contains("matrix.channel == 'aot' && endsWith(matrix.rid, '-x64')", release, StringComparison.Ordinal);
        Assert.Contains("aot-simd-probe.ps1", release, StringComparison.Ordinal);
        Assert.Contains("aot-simd-probe.sh", release, StringComparison.Ordinal);
        Assert.Contains("ymm/zmm", aotProbePs1, StringComparison.Ordinal);
        Assert.Contains("[yz]mm", aotProbeSh, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 CA 最内层邻居访问经 3x3 窗口基址与 Unsafe.Add 漫游，不在热更新器内直接数组索引。
    /// </summary>
    [Fact]
    public void SimulationHotNeighborAccessUsesUnsafeBaseRefs()
    {
        string chunk = ReadRepositoryFile("src", "PixelEngine.Simulation", "Chunk.cs");
        string window = ReadRepositoryFile("src", "PixelEngine.Simulation", "NeighborWindow.cs");
        string updater = ReadRepositoryFile("src", "PixelEngine.Simulation", "ChunkUpdater.cs");

        Assert.Contains("MemoryMarshal.GetArrayDataReference(Material)", chunk, StringComparison.Ordinal);
        Assert.Contains("MemoryMarshal.GetArrayDataReference(Flags)", chunk, StringComparison.Ordinal);
        Assert.Contains("MemoryMarshal.GetArrayDataReference(Lifetime)", chunk, StringComparison.Ordinal);

        Assert.Contains("ref struct NeighborWindow", window, StringComparison.Ordinal);
        Assert.Contains("ref ushort _matBase0", window, StringComparison.Ordinal);
        Assert.Contains("ref byte _flagsBase0", window, StringComparison.Ordinal);
        Assert.Contains("ref byte _lifeBase0", window, StringComparison.Ordinal);
        Assert.Contains("Unsafe.Add(ref SelectMaterialBase(slot), local)", window, StringComparison.Ordinal);
        Assert.Contains("Unsafe.Add(ref SelectFlagsBase(slot), local)", window, StringComparison.Ordinal);
        Assert.Contains("Unsafe.Add(ref SelectLifetimeBase(slot), local)", window, StringComparison.Ordinal);

        Assert.Contains("NeighborWindow window = new(chunks, chunk.Coord);", updater, StringComparison.Ordinal);
        Assert.Contains("ref ushort materialBase = ref chunk.GetMaterialBase();", updater, StringComparison.Ordinal);
        Assert.Contains("ref byte flagsBase = ref chunk.GetFlagsBase();", updater, StringComparison.Ordinal);
        Assert.Contains("int localOffset = (ly * EngineConstants.ChunkSize) + rect.MinX;", updater, StringComparison.Ordinal);
        Assert.Contains("Unsafe.Add(ref materialBase, localOffset)", updater, StringComparison.Ordinal);
        Assert.Contains("Unsafe.Add(ref flagsBase, localOffset)", updater, StringComparison.Ordinal);
        Assert.Contains("localOffset++", updater, StringComparison.Ordinal);
        Assert.DoesNotContain("chunk.Material[", updater, StringComparison.Ordinal);
        Assert.DoesNotContain("chunk.Flags[", updater, StringComparison.Ordinal);
        Assert.DoesNotContain("chunk.Lifetime[", updater, StringComparison.Ordinal);
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
