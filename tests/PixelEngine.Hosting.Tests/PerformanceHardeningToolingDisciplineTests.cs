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
        Assert.Contains("HardwareCounter.CacheMisses", program, StringComparison.Ordinal);
        Assert.Contains("HardwareCounter.BranchMispredictions", program, StringComparison.Ordinal);
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
        Assert.Contains("rowContains", regression, StringComparison.Ordinal);
        Assert.Contains("RNGCHKFAIL", disassembly, StringComparison.Ordinal);
        Assert.Contains("ymm|zmm", disassembly, StringComparison.Ordinal);
        Assert.Contains("HardwareIntrinsics", disassembly, StringComparison.Ordinal);
        Assert.Contains("\"benchmarks\"", baseline, StringComparison.Ordinal);
        Assert.Contains("CellThroughputBenchmark.StepJobSystem.FullActiveLiquid", baseline, StringComparison.Ordinal);
        Assert.Contains("CellThroughputBenchmark.StepJobSystem.TypicalDirtyRect", baseline, StringComparison.Ordinal);
        Assert.Contains("ReactionLookupBenchmark.FindDirect", baseline, StringComparison.Ordinal);
        Assert.Contains("ParticleIntegrationBenchmark.IntegrateFlyingParticles.200000", baseline, StringComparison.Ordinal);
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
    /// 验证硬件计数器阻塞项有显式预检脚本，非管理员 ETW 场景不会被静默当作成功。
    /// </summary>
    [Fact]
    public void HardwareCounterPreflightReportsPrivilegeAndCounterColumns()
    {
        string script = ReadRepositoryFile("tools", "hardware-counter-preflight.ps1");
        string report = ReadRepositoryFile("docs", "benchmark-reports", "2026-07-02-latency-branch-calibration.md");
        string plan = ReadRepositoryFile("plan", "16-performance-hardening.md");

        Assert.Contains("PIXELENGINE_BENCH_HARDWARE_COUNTERS", script, StringComparison.Ordinal);
        Assert.Contains("HardwareCounter.CacheMisses", script, StringComparison.Ordinal);
        Assert.Contains("HardwareCounter.BranchMispredictions", script, StringComparison.Ordinal);
        Assert.Contains("WindowsBuiltInRole", script, StringComparison.Ordinal);
        Assert.Contains("Administrator", script, StringComparison.Ordinal);
        Assert.Contains("ETW Kernel Session", script, StringComparison.Ordinal);
        Assert.Contains("blocked_non_admin", script, StringComparison.Ordinal);
        Assert.Contains("blocked_non_windows", script, StringComparison.Ordinal);
        Assert.Contains("Hardware counter preflight failed", script, StringComparison.Ordinal);
        Assert.Contains("AllowBlocked", script, StringComparison.Ordinal);
        Assert.Contains("LASTEXITCODE", script, StringComparison.Ordinal);
        Assert.Contains("ReactionLookupBenchmark.FindDirect", script, StringComparison.Ordinal);
        Assert.Contains("Cache Misses", script, StringComparison.Ordinal);
        Assert.Contains("Branch Mispredictions", script, StringComparison.Ordinal);

        Assert.Contains("tools/hardware-counter-preflight.ps1", report, StringComparison.Ordinal);
        Assert.Contains("blocked_non_admin", report, StringComparison.Ordinal);
        Assert.Contains("tools/hardware-counter-preflight.ps1", plan, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 native 资源泄漏预检不会把进程 smoke 误当作 GL/OpenAL/Box2D/ALC 泄漏验收。
    /// </summary>
    [Fact]
    public void NativeLeakPreflightRequiresExternalDetectorEvidence()
    {
        string script = ReadRepositoryFile("tools", "native-leak-preflight.ps1");
        string report = ReadRepositoryFile("docs", "runtime-reports", "2026-07-02-demo-window-longrun.md");
        string plan = ReadRepositoryFile("plan", "18-hosting-runtime.md");

        Assert.Contains("DetectorReportPath", script, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_detector", script, StringComparison.Ordinal);
        Assert.Contains("process_smoke_only", script, StringComparison.Ordinal);
        Assert.Contains("Native leak preflight failed", script, StringComparison.Ordinal);
        Assert.Contains("RunProcessSmoke", script, StringComparison.Ordinal);
        Assert.Contains("PeakWorkingSetMB", script, StringComparison.Ordinal);
        Assert.Contains("AllowBlocked", script, StringComparison.Ordinal);
        Assert.Contains("GL", script, StringComparison.Ordinal);
        Assert.Contains("OpenAL", script, StringComparison.Ordinal);
        Assert.Contains("Box2D", script, StringComparison.Ordinal);
        Assert.Contains("ALC", script, StringComparison.Ordinal);
        Assert.Contains("exit 2", script, StringComparison.Ordinal);

        Assert.Contains("tools/native-leak-preflight.ps1", report, StringComparison.Ordinal);
        Assert.Contains("process_smoke_only", report, StringComparison.Ordinal);
        Assert.Contains("tools/native-leak-preflight.ps1", plan, StringComparison.Ordinal);
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
    /// 验证 PowerShell/Bash 发行产物审计在 AOT Box2D 与 package/SHA256 规则上保持同等严格。
    /// </summary>
    [Fact]
    public void ReleaseArtifactAuditsRejectMispackagedNativeAndChecksumOutputs()
    {
        string auditPs1 = ReadRepositoryFile("tools", "audit-release-artifacts.ps1");
        string auditSh = ReadRepositoryFile("tools", "audit-release-artifacts.sh");

        Assert.Contains("Assert-NoDynamicBox2D", auditPs1, StringComparison.Ordinal);
        Assert.Contains("box2d.dll", auditPs1, StringComparison.Ordinal);
        Assert.Contains("libbox2d.so", auditPs1, StringComparison.Ordinal);
        Assert.Contains("libbox2d.dylib", auditPs1, StringComparison.Ordinal);
        Assert.Contains("AOT 产物不应携带动态 Box2D", auditPs1, StringComparison.Ordinal);
        Assert.Contains("assert_no_dynamic_box2d", auditSh, StringComparison.Ordinal);

        Assert.Contains("package 文件名不符合发行命名", auditPs1, StringComparison.Ordinal);
        Assert.Contains("同一 RID/channel 存在多个 package", auditPs1, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS 只允许 package root 下的文件名", auditPs1, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS 包含 package root 下不存在或非发行包的条目", auditPs1, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS 重复条目", auditPs1, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS 条目数与 package 数不一致", auditPs1, StringComparison.Ordinal);
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
