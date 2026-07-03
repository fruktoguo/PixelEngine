using System.Text.Json.Nodes;

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
        string testingPlan = ReadRepositoryFile("plan", "14-testing-benchmarking.md");

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
        Assert.DoesNotContain("-RequireCounters", script, StringComparison.Ordinal);

        Assert.Contains("tools/hardware-counter-preflight.ps1", report, StringComparison.Ordinal);
        Assert.Contains("blocked_non_admin", report, StringComparison.Ordinal);
        Assert.Contains("tools/hardware-counter-preflight.ps1", plan, StringComparison.Ordinal);

        Assert.Contains("- [x] 六个基准可产出报告", testingPlan, StringComparison.Ordinal);
        Assert.Contains("docs/benchmark-reports/2026-07-02-plan14-short.md", testingPlan, StringComparison.Ordinal);
        Assert.Contains("- [!] 反应 cache-miss / branch-misprediction 硬件计数器报告仍需管理员 ETW Kernel Session", testingPlan, StringComparison.Ordinal);
        Assert.Contains("tools/hardware-counter-preflight.ps1", testingPlan, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证硬件计数器预检脚本在当前宿主上真实写出权限 / 平台边界报告，且默认不运行 benchmark。
    /// </summary>
    [Fact]
    public void HardwareCounterPreflightWritesHostBoundaryReport()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-hardware-counter-preflight-" + Guid.NewGuid().ToString("N"));

        try
        {
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "hardware-counter-preflight.ps1"),
                "-Artifacts",
                temp,
                "-AllowBlocked");

            Assert.Equal(0, result.ExitCode);
            string preflightReport = File.ReadAllText(Path.Combine(temp, "hardware-counter-preflight.md"));
            Assert.Contains("benchmark_requested | False", preflightReport, StringComparison.Ordinal);
            Assert.Contains("Cache Misses; Branch Mispredictions", preflightReport, StringComparison.Ordinal);
            Assert.Contains("PIXELENGINE_BENCH_HARDWARE_COUNTERS", preflightReport, StringComparison.Ordinal);
            Assert.DoesNotContain("counters_present", preflightReport, StringComparison.Ordinal);

            if (!OperatingSystem.IsWindows())
            {
                Assert.Contains("status | blocked_non_windows", preflightReport, StringComparison.Ordinal);
                Assert.Contains("非 Windows runner 不作为 Cache Misses / Branch Mispredictions 验收环境", preflightReport, StringComparison.Ordinal);
            }
            else if (!IsWindowsAdministrator())
            {
                Assert.Contains("status | blocked_non_admin", preflightReport, StringComparison.Ordinal);
                Assert.Contains("elevated ETW Kernel Session", preflightReport, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("status | ready", preflightReport, StringComparison.Ordinal);
                Assert.Contains("追加 -RunBenchmark 可执行 BenchmarkDotNet", preflightReport, StringComparison.Ordinal);
            }
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证目标硬件性能证据预检要求 AVX-512、6 RID cells/frame、帧预算与硬件计数器 scope/hash，不把本机短样本当作通过。
    /// </summary>
    [Fact]
    public void PerformanceTargetEvidencePreflightRequiresManifestScopesAndHashes()
    {
        string script = ReadRepositoryFile("tools", "performance-target-evidence-preflight.ps1");
        string report = ReadRepositoryFile("docs", "benchmark-reports", "2026-07-02-performance-target-evidence.md");
        string plan = ReadRepositoryFile("plan", "16-performance-hardening.md");

        Assert.Contains("EvidenceManifestPath", script, StringComparison.Ordinal);
        Assert.Contains("AllowBlocked", script, StringComparison.Ordinal);
        Assert.Contains("schemaVersion", script, StringComparison.Ordinal);
        Assert.Contains("evidence[]", script, StringComparison.Ordinal);
        Assert.Contains("sha256", script, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", script, StringComparison.Ordinal);
        Assert.Contains("sha256 不匹配", script, StringComparison.Ordinal);
        Assert.Contains("未知 evidence scope", script, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_target_performance_manifest", script, StringComparison.Ordinal);
        Assert.Contains("blocked_invalid_target_performance_evidence", script, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_target_performance_scope_evidence", script, StringComparison.Ordinal);
        Assert.Contains("target_performance_evidence_attached_pending_review", script, StringComparison.Ordinal);
        Assert.Contains("[Console]::Error.WriteLine", script, StringComparison.Ordinal);
        Assert.Contains("Performance target evidence preflight failed", script, StringComparison.Ordinal);
        Assert.Contains("本机短样本", script, StringComparison.Ordinal);
        Assert.Contains("exit 2", script, StringComparison.Ordinal);
        Assert.Contains("exit 5", script, StringComparison.Ordinal);

        Assert.Contains("avx512_downclock_net_loss", script, StringComparison.Ordinal);
        Assert.Contains("hardware_counters_cache_branch", script, StringComparison.Ordinal);
        Assert.Contains("frame_budget_target_hardware", script, StringComparison.Ordinal);
        Assert.Contains("Cache Misses", script, StringComparison.Ordinal);
        Assert.Contains("Branch Mispredictions", script, StringComparison.Ordinal);
        Assert.Contains("cells_frame/$rid", script, StringComparison.Ordinal);
        Assert.Contains("benchmarkDotNet=true", script, StringComparison.Ordinal);
        Assert.Contains("win-x64", script, StringComparison.Ordinal);
        Assert.Contains("win-arm64", script, StringComparison.Ordinal);
        Assert.Contains("linux-x64", script, StringComparison.Ordinal);
        Assert.Contains("linux-arm64", script, StringComparison.Ordinal);
        Assert.Contains("osx-x64", script, StringComparison.Ordinal);
        Assert.Contains("osx-arm64", script, StringComparison.Ordinal);
        Assert.DoesNotContain("status \"passed\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("status = \"passed\"", script, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("tools/performance-target-evidence-preflight.ps1", report, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_target_performance_manifest", report, StringComparison.Ordinal);
        Assert.Contains("blocked_invalid_target_performance_evidence", report, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_target_performance_scope_evidence", report, StringComparison.Ordinal);
        Assert.Contains("target_performance_evidence_attached_pending_review", report, StringComparison.Ordinal);
        Assert.Contains("avx512_downclock_net_loss", report, StringComparison.Ordinal);
        Assert.Contains("hardware_counters_cache_branch", report, StringComparison.Ordinal);
        Assert.Contains("frame_budget_target_hardware", report, StringComparison.Ordinal);
        Assert.Contains("cells_frame/osx-arm64", report, StringComparison.Ordinal);
        Assert.Contains("未知 scope", report, StringComparison.Ordinal);

        Assert.Contains("tools/performance-target-evidence-preflight.ps1", plan, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_target_performance_manifest", plan, StringComparison.Ordinal);
        Assert.Contains("blocked_invalid_target_performance_evidence", plan, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_target_performance_scope_evidence", plan, StringComparison.Ordinal);
        Assert.Contains("target_performance_evidence_attached_pending_review", plan, StringComparison.Ordinal);
        Assert.Contains("avx512_downclock_net_loss", plan, StringComparison.Ordinal);
        Assert.Contains("hardware_counters_cache_branch", plan, StringComparison.Ordinal);
        Assert.Contains("frame_budget_target_hardware", plan, StringComparison.Ordinal);
        Assert.Contains("cells_frame/<rid>", plan, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 plan/README 的证据预检状态索引覆盖全部外部证据入口，并明确待审/本机探针状态不是验收通过。
    /// </summary>
    [Fact]
    public void PlanReadmeIndexesAllEvidencePreflightStatusesAsNonPassing()
    {
        string readme = ReadRepositoryFile("plan", "README.md");

        string[] tools =
        [
            "tools/hardware-counter-preflight.ps1",
            "tools/ci-matrix-evidence-preflight.ps1",
            "tools/performance-target-evidence-preflight.ps1",
            "tools/gpu-particle-benchmark-preflight.ps1",
            "tools/demo-manual-acceptance-preflight.ps1",
            "tools/native-leak-preflight.ps1",
            "tools/release-evidence-preflight.ps1",
        ];
        foreach (string tool in tools)
        {
            Assert.Contains(tool, readme, StringComparison.Ordinal);
        }

        string[] statuses =
        [
            "blocked_non_windows",
            "blocked_non_admin",
            "missing_counter_columns",
            "ready",
            "counters_present",
            "blocked_missing_ci_manifest",
            "blocked_invalid_ci_evidence",
            "blocked_missing_ci_scope_evidence",
            "ci_matrix_evidence_attached_pending_review",
            "blocked_missing_target_performance_manifest",
            "blocked_invalid_target_performance_evidence",
            "blocked_missing_target_performance_scope_evidence",
            "target_performance_evidence_attached_pending_review",
            "blocked_missing_target_gpu_evidence",
            "local_probe_only",
            "blocked_missing_target_gpu_scope_evidence",
            "blocked_invalid_target_gpu_evidence",
            "target_gpu_evidence_attached_pending_review",
            "blocked_missing_manual_evidence",
            "scripted_probe_only",
            "blocked_missing_manual_scope_evidence",
            "blocked_invalid_manual_evidence",
            "manual_evidence_attached_pending_review",
            "blocked_missing_detector",
            "process_smoke_only",
            "detector_report_attached_pending_review",
            "blocked_missing_scope_evidence",
            "blocked_invalid_native_leak_evidence",
            "detector_evidence_attached_pending_review",
            "blocked_missing_release_manifest",
            "blocked_invalid_release_evidence",
            "blocked_missing_release_scope_evidence",
            "blocked_not_tag_release",
            "release_evidence_attached_pending_review",
        ];
        foreach (string status in statuses)
        {
            Assert.Contains(status, readme, StringComparison.Ordinal);
        }

        Assert.Contains("*_pending_review", readme, StringComparison.Ordinal);
        Assert.Contains("local_probe_only", readme, StringComparison.Ordinal);
        Assert.Contains("scripted_probe_only", readme, StringComparison.Ordinal);
        Assert.Contains("process_smoke_only", readme, StringComparison.Ordinal);
        Assert.Contains("ready", readme, StringComparison.Ordinal);
        Assert.Contains("counters_present", readme, StringComparison.Ordinal);
        Assert.Contains("都不是对应 plan 验收通过状态", readme, StringComparison.Ordinal);
        Assert.Contains("本地计数器列检查通过", readme, StringComparison.Ordinal);
        Assert.Contains("对应 plan 条目仍保持 `- [!]`", readme, StringComparison.Ordinal);
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
        Assert.Contains("EvidenceManifestPath", script, StringComparison.Ordinal);
        Assert.Contains("schemaVersion 必须为 1", script, StringComparison.Ordinal);
        Assert.Contains("包含未知 scope", script, StringComparison.Ordinal);
        Assert.Contains("sha256", script, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", script, StringComparison.Ordinal);
        Assert.Contains("sha256 不匹配", script, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_detector", script, StringComparison.Ordinal);
        Assert.Contains("blocked_invalid_native_leak_evidence", script, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_scope_evidence", script, StringComparison.Ordinal);
        Assert.Contains("process_smoke_only", script, StringComparison.Ordinal);
        Assert.Contains("detector_evidence_attached_pending_review", script, StringComparison.Ordinal);
        Assert.Contains("detector_report_attached_pending_review", script, StringComparison.Ordinal);
        Assert.Contains("[Console]::Error.WriteLine", script, StringComparison.Ordinal);
        Assert.Contains("Native leak preflight failed: detector_evidence_attached_pending_review", script, StringComparison.Ordinal);
        Assert.Contains("Native leak preflight failed: detector_report_attached_pending_review", script, StringComparison.Ordinal);
        Assert.Contains("Native leak preflight failed", script, StringComparison.Ordinal);
        Assert.Contains("RunProcessSmoke", script, StringComparison.Ordinal);
        Assert.Contains("PeakWorkingSetMB", script, StringComparison.Ordinal);
        Assert.Contains("AllowBlocked", script, StringComparison.Ordinal);
        Assert.Contains("GL", script, StringComparison.Ordinal);
        Assert.Contains("OpenAL", script, StringComparison.Ordinal);
        Assert.Contains("Box2D", script, StringComparison.Ordinal);
        Assert.Contains("ALC", script, StringComparison.Ordinal);
        Assert.Contains("exit 2", script, StringComparison.Ordinal);
        Assert.Contains("exit 5", script, StringComparison.Ordinal);
        Assert.DoesNotContain("status \"passed\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("status = \"passed\"", script, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("tools/native-leak-preflight.ps1", report, StringComparison.Ordinal);
        Assert.Contains("process_smoke_only", report, StringComparison.Ordinal);
        Assert.Contains("schemaVersion: 1", report, StringComparison.Ordinal);
        Assert.Contains("未知 scope", report, StringComparison.Ordinal);
        Assert.Contains("blocked_invalid_native_leak_evidence", report, StringComparison.Ordinal);
        Assert.Contains("- [x] 子系统装配与**初始化顺序**", plan, StringComparison.Ordinal);
        Assert.Contains("native GL/OpenAL/Box2D 工具级泄漏审计仍由 §5 的 native leak detector 阻塞项闭合", plan, StringComparison.Ordinal);
        Assert.Contains("tools/native-leak-preflight.ps1", plan, StringComparison.Ordinal);
        Assert.Contains("blocked_invalid_native_leak_evidence", plan, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 native leak 预检的真实脚本行为：hash 错误被拒绝为 invalid，证据齐全也保持待审非零退出。
    /// </summary>
    [Fact]
    public void NativeLeakPreflightRejectsBadHashesAndKeepsPendingReviewNonZero()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-native-leak-evidence-" + Guid.NewGuid().ToString("N"));

        try
        {
            string goodManifest = CreateNativeLeakEvidenceManifest(temp);
            string badManifest = CreateNativeLeakEvidenceManifest(temp, corruptHashScope: "gl", suffix: "bad");

            string badArtifacts = Path.Combine(temp, "bad-out");
            ScriptResult bad = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "native-leak-preflight.ps1"),
                "-EvidenceManifestPath",
                badManifest,
                "-Artifacts",
                badArtifacts);
            Assert.Equal(5, bad.ExitCode);
            string badReport = File.ReadAllText(Path.Combine(badArtifacts, "native-leak-preflight.md"));
            Assert.Contains("blocked_invalid_native_leak_evidence", bad.Output + badReport, StringComparison.Ordinal);
            Assert.Contains("sha256 不匹配", badReport, StringComparison.Ordinal);

            string goodArtifacts = Path.Combine(temp, "good-out");
            ScriptResult good = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "native-leak-preflight.ps1"),
                "-EvidenceManifestPath",
                goodManifest,
                "-Artifacts",
                goodArtifacts);
            Assert.Equal(2, good.ExitCode);
            string goodReport = File.ReadAllText(Path.Combine(goodArtifacts, "native-leak-preflight.md"));
            Assert.Contains("detector_evidence_attached_pending_review", good.Output + goodReport, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 native leak 预检会把 schema/JSON 错误落成稳定报告，而不是直接抛出无报告异常。
    /// </summary>
    [Fact]
    public void NativeLeakPreflightRejectsInvalidSchemaWithReport()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-native-leak-schema-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateNativeLeakEvidenceManifest(temp);
            string json = File.ReadAllText(manifest).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);
            File.WriteAllText(manifest, json);

            string artifacts = Path.Combine(temp, "schema-out");
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "native-leak-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "native-leak-preflight.md"));
            Assert.Contains("blocked_invalid_native_leak_evidence", result.Output + report, StringComparison.Ordinal);
            Assert.Contains("schemaVersion 必须为 1", report, StringComparison.Ordinal);
            Assert.DoesNotContain("status | detector_evidence_attached_pending_review", report, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 native leak 预检会真实拒绝缺失必需 detector scope 的 manifest。
    /// </summary>
    [Fact]
    public void NativeLeakPreflightRejectsMissingRequiredScopeWithReport()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-native-leak-missing-scope-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateNativeLeakEvidenceManifest(temp);
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonObject scopes = rootNode["scopes"]!.AsObject();
            _ = scopes.Remove("alc");
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "missing-out");
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "native-leak-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "native-leak-preflight.md"));
            Assert.Contains("blocked_missing_scope_evidence", result.Output + report, StringComparison.Ordinal);
            Assert.Contains("evidence manifest 缺少 scope：alc", report, StringComparison.Ordinal);
            Assert.DoesNotContain("status | detector_evidence_attached_pending_review", report, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证各 evidence preflight 都会把 malformed JSON 写成稳定 invalid 报告，而不是直接抛出无报告异常。
    /// </summary>
    [Theory]
    [InlineData("ci-matrix-evidence-preflight.ps1", "ci-matrix-evidence-preflight.md", "blocked_invalid_ci_evidence")]
    [InlineData("performance-target-evidence-preflight.ps1", "performance-target-evidence-preflight.md", "blocked_invalid_target_performance_evidence")]
    [InlineData("gpu-particle-benchmark-preflight.ps1", "gpu-particle-benchmark-preflight.md", "blocked_invalid_target_gpu_evidence")]
    [InlineData("demo-manual-acceptance-preflight.ps1", "demo-manual-acceptance-preflight.md", "blocked_invalid_manual_evidence")]
    [InlineData("native-leak-preflight.ps1", "native-leak-preflight.md", "blocked_invalid_native_leak_evidence")]
    public void EvidencePreflightsRejectMalformedJsonWithReport(string scriptName, string reportName, string expectedStatus)
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-malformed-evidence-" + Guid.NewGuid().ToString("N"));

        try
        {
            _ = Directory.CreateDirectory(temp);
            string manifest = Path.Combine(temp, "evidence.json");
            File.WriteAllText(manifest, "{ invalid");

            string artifacts = Path.Combine(temp, "out");
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", scriptName),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, reportName));
            Assert.True(
                report.Contains($"status: {expectedStatus}", StringComparison.Ordinal) ||
                report.Contains($"| status | {expectedStatus} |", StringComparison.Ordinal),
                $"report did not contain expected status {expectedStatus}:{Environment.NewLine}{report}");
            Assert.Contains("JSON", report, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 GPU 粒子目标硬件基准预检只收集证据，不把本机短 probe 当作 plan/09 验收。
    /// </summary>
    [Fact]
    public void GpuParticleBenchmarkPreflightRequiresTargetHardwareEvidence()
    {
        string script = ReadRepositoryFile("tools", "gpu-particle-benchmark-preflight.ps1");
        string report = ReadRepositoryFile("docs", "runtime-reports", "2026-07-02-particle-frame-probe.md");
        string plan = ReadRepositoryFile("plan", "09-gpu-compute.md");

        Assert.Contains("EvidenceManifestPath", script, StringComparison.Ordinal);
        Assert.Contains("AllowBlocked", script, StringComparison.Ordinal);
        Assert.Contains("RunProbe", script, StringComparison.Ordinal);
        Assert.Contains("sha256", script, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", script, StringComparison.Ordinal);
        Assert.Contains("sha256 不匹配", script, StringComparison.Ordinal);
        Assert.Contains("未知 evidence scope", script, StringComparison.Ordinal);
        Assert.Contains("--particle-frame-probe", script, StringComparison.Ordinal);
        Assert.Contains("--particle-render-mode", script, StringComparison.Ordinal);
        Assert.Contains("cpu", script, StringComparison.Ordinal);
        Assert.Contains("gpu", script, StringComparison.Ordinal);
        Assert.Contains("particle_frame_probe", script, StringComparison.Ordinal);
        Assert.Contains("targetHardwareReport", script, StringComparison.Ordinal);
        Assert.Contains("cpuProbeReport", script, StringComparison.Ordinal);
        Assert.Contains("gpuProbeReport", script, StringComparison.Ordinal);
        Assert.Contains("comparisonReport", script, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_target_gpu_evidence", script, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_target_gpu_scope_evidence", script, StringComparison.Ordinal);
        Assert.Contains("blocked_invalid_target_gpu_evidence", script, StringComparison.Ordinal);
        Assert.Contains("local_probe_only", script, StringComparison.Ordinal);
        Assert.Contains("target_gpu_evidence_attached_pending_review", script, StringComparison.Ordinal);
        Assert.Contains("[Console]::Error.WriteLine", script, StringComparison.Ordinal);
        Assert.Contains("GPU particle benchmark preflight failed", script, StringComparison.Ordinal);
        Assert.Contains("$exitCode = 2", script, StringComparison.Ordinal);
        Assert.Contains("$exitCode = 5", script, StringComparison.Ordinal);
        Assert.Contains("exit $exitCode", script, StringComparison.Ordinal);
        Assert.DoesNotContain("status \"passed\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("status = \"passed\"", script, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("tools/gpu-particle-benchmark-preflight.ps1", report, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_target_gpu_evidence", report, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_target_gpu_scope_evidence", report, StringComparison.Ordinal);
        Assert.Contains("blocked_invalid_target_gpu_evidence", report, StringComparison.Ordinal);
        Assert.Contains("local_probe_only", report, StringComparison.Ordinal);
        Assert.Contains("target_gpu_evidence_attached_pending_review", report, StringComparison.Ordinal);
        Assert.Contains("未知 scope", report, StringComparison.Ordinal);

        Assert.Contains("tools/gpu-particle-benchmark-preflight.ps1", plan, StringComparison.Ordinal);
        Assert.Contains("local_probe_only", plan, StringComparison.Ordinal);
        Assert.Contains("target_gpu_evidence_attached_pending_review", plan, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 GPU 粒子目标硬件预检的真实脚本行为：缺 scope 被拒绝，证据齐全也保持待审非零退出。
    /// </summary>
    [Fact]
    public void GpuParticleBenchmarkPreflightRejectsMissingScopesAndKeepsPendingReviewNonZero()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-gpu-particle-evidence-" + Guid.NewGuid().ToString("N"));

        try
        {
            string goodManifest = CreateFlatEvidenceManifest(
                temp,
                ["targetHardwareReport", "cpuProbeReport", "gpuProbeReport", "comparisonReport"],
                suffix: "good");
            string badManifest = CreateFlatEvidenceManifest(
                temp,
                ["targetHardwareReport", "cpuProbeReport", "gpuProbeReport"],
                suffix: "bad");

            string badArtifacts = Path.Combine(temp, "bad-out");
            ScriptResult bad = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "gpu-particle-benchmark-preflight.ps1"),
                "-EvidenceManifestPath",
                badManifest,
                "-Artifacts",
                badArtifacts);
            Assert.Equal(5, bad.ExitCode);
            string badReport = File.ReadAllText(Path.Combine(badArtifacts, "gpu-particle-benchmark-preflight.md"));
            Assert.Contains("blocked_missing_target_gpu_scope_evidence", badReport, StringComparison.Ordinal);
            Assert.Contains("comparisonReport", badReport, StringComparison.Ordinal);

            string goodArtifacts = Path.Combine(temp, "good-out");
            ScriptResult good = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "gpu-particle-benchmark-preflight.ps1"),
                "-EvidenceManifestPath",
                goodManifest,
                "-Artifacts",
                goodArtifacts);
            Assert.Equal(2, good.ExitCode);
            string goodReport = File.ReadAllText(Path.Combine(goodArtifacts, "gpu-particle-benchmark-preflight.md"));
            Assert.Contains("target_gpu_evidence_attached_pending_review", good.Output + goodReport, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 GPU 粒子目标硬件预检会实际拒绝 sha256 不匹配的 evidence，而不是只做脚本文本约束。
    /// </summary>
    [Fact]
    public void GpuParticleBenchmarkPreflightRejectsMismatchedEvidenceHash()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-gpu-particle-bad-hash-" + Guid.NewGuid().ToString("N"));

        try
        {
            string badManifest = CreateFlatEvidenceManifest(
                temp,
                ["targetHardwareReport", "cpuProbeReport", "gpuProbeReport", "comparisonReport"],
                suffix: "bad-hash");
            string json = File.ReadAllText(badManifest);
            json = json.Replace("\"sha256\": \"", "\"sha256\": \"0000", StringComparison.Ordinal);
            File.WriteAllText(badManifest, json);

            string badArtifacts = Path.Combine(temp, "bad-hash-out");
            ScriptResult bad = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "gpu-particle-benchmark-preflight.ps1"),
                "-EvidenceManifestPath",
                badManifest,
                "-Artifacts",
                badArtifacts);

            Assert.Equal(5, bad.ExitCode);
            string badReport = File.ReadAllText(Path.Combine(badArtifacts, "gpu-particle-benchmark-preflight.md"));
            Assert.Contains("blocked_invalid_target_gpu_evidence", bad.Output + badReport, StringComparison.Ordinal);
            Assert.Contains("status: blocked_invalid_target_gpu_evidence", badReport, StringComparison.Ordinal);
            Assert.Contains("sha256 不匹配", badReport, StringComparison.Ordinal);
            Assert.Contains("targetHardwareReport", badReport, StringComparison.Ordinal);
            Assert.DoesNotContain("status: target_gpu_evidence_attached_pending_review", badReport, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 GPU 粒子目标硬件预检会真实拒绝未知或重复 evidence scope。
    /// </summary>
    [Fact]
    public void GpuParticleBenchmarkPreflightRejectsUnknownAndDuplicateScopes()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-gpu-particle-scope-" + Guid.NewGuid().ToString("N"));

        try
        {
            string unknownManifest = CreateFlatEvidenceManifest(
                temp,
                ["targetHardwareReport", "cpuProbeReport", "gpuProbeReport", "comparisonReport", "localProbeOnly"],
                suffix: "unknown-scope");
            string duplicateManifest = CreateFlatEvidenceManifest(
                temp,
                ["targetHardwareReport", "cpuProbeReport", "gpuProbeReport", "comparisonReport"],
                suffix: "duplicate-scope");
            AddDuplicateFlatEvidenceScope(duplicateManifest, "comparisonReport");

            string unknownArtifacts = Path.Combine(temp, "unknown-out");
            ScriptResult unknown = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "gpu-particle-benchmark-preflight.ps1"),
                "-EvidenceManifestPath",
                unknownManifest,
                "-Artifacts",
                unknownArtifacts);
            Assert.Equal(5, unknown.ExitCode);
            string unknownReport = File.ReadAllText(Path.Combine(unknownArtifacts, "gpu-particle-benchmark-preflight.md"));
            Assert.Contains("status: blocked_invalid_target_gpu_evidence", unknown.Output + unknownReport, StringComparison.Ordinal);
            Assert.Contains("未知 evidence scope：localProbeOnly", unknownReport, StringComparison.Ordinal);
            Assert.DoesNotContain("status: target_gpu_evidence_attached_pending_review", unknownReport, StringComparison.Ordinal);

            string duplicateArtifacts = Path.Combine(temp, "duplicate-out");
            ScriptResult duplicate = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "gpu-particle-benchmark-preflight.ps1"),
                "-EvidenceManifestPath",
                duplicateManifest,
                "-Artifacts",
                duplicateArtifacts);
            Assert.Equal(5, duplicate.ExitCode);
            string duplicateReport = File.ReadAllText(Path.Combine(duplicateArtifacts, "gpu-particle-benchmark-preflight.md"));
            Assert.Contains("status: blocked_invalid_target_gpu_evidence", duplicate.Output + duplicateReport, StringComparison.Ordinal);
            Assert.Contains("重复 evidence scope：comparisonReport", duplicateReport, StringComparison.Ordinal);
            Assert.DoesNotContain("status: target_gpu_evidence_attached_pending_review", duplicateReport, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 GPU 粒子目标硬件预检会把 schema/JSON 错误落成稳定报告。
    /// </summary>
    [Fact]
    public void GpuParticleBenchmarkPreflightRejectsInvalidSchemaWithReport()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-gpu-particle-schema-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateFlatEvidenceManifest(
                temp,
                ["targetHardwareReport", "cpuProbeReport", "gpuProbeReport", "comparisonReport"],
                suffix: "schema");
            string json = File.ReadAllText(manifest).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);
            File.WriteAllText(manifest, json);

            string artifacts = Path.Combine(temp, "schema-out");
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "gpu-particle-benchmark-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "gpu-particle-benchmark-preflight.md"));
            Assert.Contains("status: blocked_invalid_target_gpu_evidence", report, StringComparison.Ordinal);
            Assert.Contains("schemaVersion 必须为 1", report, StringComparison.Ordinal);
            Assert.DoesNotContain("status: target_gpu_evidence_attached_pending_review", report, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 Demo 人工验收预检只索引视觉/听感/手感证据，不把 scripted probe 当作 plan/13 通过。
    /// </summary>
    [Fact]
    public void DemoManualAcceptancePreflightRequiresHumanEvidence()
    {
        string script = ReadRepositoryFile("tools", "demo-manual-acceptance-preflight.ps1");
        string report = ReadRepositoryFile("docs", "runtime-reports", "2026-07-02-demo-manual-acceptance.md");
        string plan = ReadRepositoryFile("plan", "13-demo-game.md");
        string hostingPlan = ReadRepositoryFile("plan", "18-hosting-runtime.md");

        Assert.Contains("EvidenceManifestPath", script, StringComparison.Ordinal);
        Assert.Contains("RunScriptedProbes", script, StringComparison.Ordinal);
        Assert.Contains("AllowBlocked", script, StringComparison.Ordinal);
        Assert.Contains("sha256", script, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", script, StringComparison.Ordinal);
        Assert.Contains("sha256 不匹配", script, StringComparison.Ordinal);
        Assert.Contains("未知 evidence scope", script, StringComparison.Ordinal);
        Assert.Contains("--scripted-window-demo", script, StringComparison.Ordinal);
        Assert.Contains("--scripted-window-route", script, StringComparison.Ordinal);
        Assert.Contains("--window-ticks", script, StringComparison.Ordinal);
        Assert.Contains("playable-world", script, StringComparison.Ordinal);
        Assert.Contains("route-attempt", script, StringComparison.Ordinal);
        Assert.Contains("scenes/lava-mine.scene", script, StringComparison.Ordinal);
        Assert.Contains("scenes/lava-mine-goal-probe.scene", script, StringComparison.Ordinal);
        Assert.Contains("scenes/lava-mine-health-probe.scene", script, StringComparison.Ordinal);
        Assert.Contains("scenes/lava-mine-camera-probe.scene", script, StringComparison.Ordinal);
        Assert.Contains("scenes/lava-mine-reaction-probe.scene", script, StringComparison.Ordinal);
        Assert.Contains("scenes/lava-mine-audio-probe.scene", script, StringComparison.Ordinal);
        Assert.Contains("scenes/lava-mine-particle-light-probe.scene", script, StringComparison.Ordinal);
        Assert.Contains("RequiredSummaryMarkers", script, StringComparison.Ordinal);
        Assert.Contains("player_visual=present", script, StringComparison.Ordinal);
        Assert.Contains("playable_shots=1", script, StringComparison.Ordinal);
        Assert.Contains("particles=0", script, StringComparison.Ordinal);
        Assert.Contains("fps=", script, StringComparison.Ordinal);
        Assert.Contains("sim_hz=", script, StringComparison.Ordinal);
        Assert.Contains("brush_material=stone", script, StringComparison.Ordinal);
        Assert.Contains("painted_material=13", script, StringComparison.Ordinal);
        Assert.Contains("goal_reached=True", script, StringComparison.Ordinal);
        Assert.Contains("damage_events=", script, StringComparison.Ordinal);
        Assert.Contains("camera_followed=True", script, StringComparison.Ordinal);
        Assert.Contains("reactions_observed=True", script, StringComparison.Ordinal);
        Assert.Contains("phase_transitions_observed=True", script, StringComparison.Ordinal);
        Assert.Contains("audio_probe_one_shot_played=True", script, StringComparison.Ordinal);
        Assert.Contains("particle_light_probe_depleted=True", script, StringComparison.Ordinal);

        Assert.Contains("controlFeelReport", script, StringComparison.Ordinal);
        Assert.Contains("materialBrushAndReactionVideo", script, StringComparison.Ordinal);
        Assert.Contains("rigidBodyGameplayVideo", script, StringComparison.Ordinal);
        Assert.Contains("particleLightingVideo", script, StringComparison.Ordinal);
        Assert.Contains("audioListeningReport", script, StringComparison.Ordinal);
        Assert.Contains("fullRoutePlaythroughVideo", script, StringComparison.Ordinal);
        Assert.Contains("hudMenuEditorVideo", script, StringComparison.Ordinal);
        Assert.Contains("hotReloadWindowReport", script, StringComparison.Ordinal);

        Assert.Contains("blocked_missing_manual_evidence", script, StringComparison.Ordinal);
        Assert.Contains("scripted_probe_only", script, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_manual_scope_evidence", script, StringComparison.Ordinal);
        Assert.Contains("blocked_invalid_manual_evidence", script, StringComparison.Ordinal);
        Assert.Contains("manual_evidence_attached_pending_review", script, StringComparison.Ordinal);
        Assert.Contains("[Console]::Error.WriteLine", script, StringComparison.Ordinal);
        Assert.Contains("Demo manual acceptance preflight failed", script, StringComparison.Ordinal);
        Assert.Contains("$exitCode = 2", script, StringComparison.Ordinal);
        Assert.Contains("$exitCode = 5", script, StringComparison.Ordinal);
        Assert.DoesNotContain("status \"passed\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("status = \"passed\"", script, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("tools/demo-manual-acceptance-preflight.ps1", report, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_manual_evidence", report, StringComparison.Ordinal);
        Assert.Contains("scripted_probe_only", report, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_manual_scope_evidence", report, StringComparison.Ordinal);
        Assert.Contains("blocked_invalid_manual_evidence", report, StringComparison.Ordinal);
        Assert.Contains("manual_evidence_attached_pending_review", report, StringComparison.Ordinal);
        Assert.Contains("playable_shots=1", report, StringComparison.Ordinal);
        Assert.Contains("未知 scope", report, StringComparison.Ordinal);

        Assert.Contains("tools/demo-manual-acceptance-preflight.ps1", plan, StringComparison.Ordinal);
        Assert.Contains("manual_evidence_attached_pending_review", plan, StringComparison.Ordinal);

        Assert.Contains("tools/demo-manual-acceptance-preflight.ps1", hostingPlan, StringComparison.Ordinal);
        Assert.Contains("hudMenuEditorVideo", hostingPlan, StringComparison.Ordinal);
        Assert.Contains("manual_evidence_attached_pending_review", hostingPlan, StringComparison.Ordinal);
        Assert.Contains("- [x] 过载降级按五级顺序触发", hostingPlan, StringComparison.Ordinal);
        Assert.Contains("- [!] Editor 真实窗口观测/覆盖仍缺人工复核证据", hostingPlan, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Demo 人工验收预检的真实脚本行为：缺人工 scope 被拒绝，证据齐全也保持待审非零退出。
    /// </summary>
    [Fact]
    public void DemoManualAcceptancePreflightRejectsMissingScopesAndKeepsPendingReviewNonZero()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-demo-manual-evidence-" + Guid.NewGuid().ToString("N"));

        string[] manualScopes =
        [
            "controlFeelReport",
            "materialBrushAndReactionVideo",
            "rigidBodyGameplayVideo",
            "particleLightingVideo",
            "audioListeningReport",
            "fullRoutePlaythroughVideo",
            "hudMenuEditorVideo",
            "hotReloadWindowReport",
        ];

        try
        {
            string goodManifest = CreateFlatEvidenceManifest(temp, manualScopes, suffix: "good", includeDemoManualMetadata: true);
            string badManifest = CreateFlatEvidenceManifest(temp, manualScopes[..^1], suffix: "bad", includeDemoManualMetadata: true);

            string badArtifacts = Path.Combine(temp, "bad-out");
            ScriptResult bad = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "demo-manual-acceptance-preflight.ps1"),
                "-EvidenceManifestPath",
                badManifest,
                "-Artifacts",
                badArtifacts);
            Assert.Equal(5, bad.ExitCode);
            string badReport = File.ReadAllText(Path.Combine(badArtifacts, "demo-manual-acceptance-preflight.md"));
            Assert.Contains("blocked_missing_manual_scope_evidence", badReport, StringComparison.Ordinal);
            Assert.Contains("hotReloadWindowReport", badReport, StringComparison.Ordinal);

            string goodArtifacts = Path.Combine(temp, "good-out");
            ScriptResult good = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "demo-manual-acceptance-preflight.ps1"),
                "-EvidenceManifestPath",
                goodManifest,
                "-Artifacts",
                goodArtifacts);
            Assert.Equal(2, good.ExitCode);
            string goodReport = File.ReadAllText(Path.Combine(goodArtifacts, "demo-manual-acceptance-preflight.md"));
            Assert.Contains("manual_evidence_attached_pending_review", good.Output + goodReport, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 Demo 人工验收预检会拒绝无效 metadata，避免空视频或无观察说明冒充人工验收。
    /// </summary>
    [Fact]
    public void DemoManualAcceptancePreflightRejectsInvalidMetadata()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-demo-manual-metadata-" + Guid.NewGuid().ToString("N"));

        string[] manualScopes =
        [
            "controlFeelReport",
            "materialBrushAndReactionVideo",
            "rigidBodyGameplayVideo",
            "particleLightingVideo",
            "audioListeningReport",
            "fullRoutePlaythroughVideo",
            "hudMenuEditorVideo",
            "hotReloadWindowReport",
        ];

        try
        {
            string badDurationManifest = CreateFlatEvidenceManifest(temp, manualScopes, suffix: "bad-duration", includeDemoManualMetadata: true);
            SetFlatEvidenceProperty(badDurationManifest, "fullRoutePlaythroughVideo", "durationSeconds", 0.0);

            string badNotesManifest = CreateFlatEvidenceManifest(temp, manualScopes, suffix: "bad-notes", includeDemoManualMetadata: true);
            SetFlatEvidenceProperty(badNotesManifest, "controlFeelReport", "notes", "too short");

            string badDurationArtifacts = Path.Combine(temp, "bad-duration-out");
            ScriptResult badDuration = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "demo-manual-acceptance-preflight.ps1"),
                "-EvidenceManifestPath",
                badDurationManifest,
                "-Artifacts",
                badDurationArtifacts);
            Assert.Equal(5, badDuration.ExitCode);
            string badDurationReport = File.ReadAllText(Path.Combine(badDurationArtifacts, "demo-manual-acceptance-preflight.md"));
            Assert.Contains("status: blocked_invalid_manual_evidence", badDuration.Output + badDurationReport, StringComparison.Ordinal);
            Assert.Contains("fullRoutePlaythroughVideo durationSeconds 必须为正数", badDurationReport, StringComparison.Ordinal);
            Assert.DoesNotContain("status: manual_evidence_attached_pending_review", badDurationReport, StringComparison.Ordinal);

            string badNotesArtifacts = Path.Combine(temp, "bad-notes-out");
            ScriptResult badNotes = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "demo-manual-acceptance-preflight.ps1"),
                "-EvidenceManifestPath",
                badNotesManifest,
                "-Artifacts",
                badNotesArtifacts);
            Assert.Equal(5, badNotes.ExitCode);
            string badNotesReport = File.ReadAllText(Path.Combine(badNotesArtifacts, "demo-manual-acceptance-preflight.md"));
            Assert.Contains("status: blocked_invalid_manual_evidence", badNotes.Output + badNotesReport, StringComparison.Ordinal);
            Assert.Contains("controlFeelReport notes 至少需要 20 个字符", badNotesReport, StringComparison.Ordinal);
            Assert.DoesNotContain("status: manual_evidence_attached_pending_review", badNotesReport, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 Demo 人工验收预检会实际拒绝 sha256 不匹配的人工 evidence，并写出可审计报告。
    /// </summary>
    [Fact]
    public void DemoManualAcceptancePreflightRejectsMismatchedEvidenceHash()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-demo-manual-bad-hash-" + Guid.NewGuid().ToString("N"));
        string[] manualScopes =
        [
            "controlFeelReport",
            "materialBrushAndReactionVideo",
            "rigidBodyGameplayVideo",
            "particleLightingVideo",
            "audioListeningReport",
            "fullRoutePlaythroughVideo",
            "hudMenuEditorVideo",
            "hotReloadWindowReport",
        ];

        try
        {
            string badManifest = CreateFlatEvidenceManifest(temp, manualScopes, suffix: "bad-hash", includeDemoManualMetadata: true);
            string json = File.ReadAllText(badManifest);
            json = json.Replace("\"sha256\": \"", "\"sha256\": \"0000", StringComparison.Ordinal);
            File.WriteAllText(badManifest, json);

            string badArtifacts = Path.Combine(temp, "bad-hash-out");
            ScriptResult bad = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "demo-manual-acceptance-preflight.ps1"),
                "-EvidenceManifestPath",
                badManifest,
                "-Artifacts",
                badArtifacts);

            Assert.Equal(5, bad.ExitCode);
            string badReport = File.ReadAllText(Path.Combine(badArtifacts, "demo-manual-acceptance-preflight.md"));
            Assert.Contains("status: blocked_invalid_manual_evidence", badReport, StringComparison.Ordinal);
            Assert.Contains("sha256 不匹配", badReport, StringComparison.Ordinal);
            Assert.Contains("controlFeelReport", badReport, StringComparison.Ordinal);
            Assert.DoesNotContain("status: manual_evidence_attached_pending_review", badReport, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 Demo 人工验收预检会把 schema/JSON 错误落成稳定报告。
    /// </summary>
    [Fact]
    public void DemoManualAcceptancePreflightRejectsInvalidSchemaWithReport()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-demo-manual-schema-" + Guid.NewGuid().ToString("N"));
        string[] manualScopes =
        [
            "controlFeelReport",
            "materialBrushAndReactionVideo",
            "rigidBodyGameplayVideo",
            "particleLightingVideo",
            "audioListeningReport",
            "fullRoutePlaythroughVideo",
            "hudMenuEditorVideo",
            "hotReloadWindowReport",
        ];

        try
        {
            string manifest = CreateFlatEvidenceManifest(temp, manualScopes, suffix: "schema", includeDemoManualMetadata: true);
            string json = File.ReadAllText(manifest).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);
            File.WriteAllText(manifest, json);

            string artifacts = Path.Combine(temp, "schema-out");
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "demo-manual-acceptance-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "demo-manual-acceptance-preflight.md"));
            Assert.Contains("status: blocked_invalid_manual_evidence", report, StringComparison.Ordinal);
            Assert.Contains("schemaVersion 必须为 1", report, StringComparison.Ordinal);
            Assert.DoesNotContain("status: manual_evidence_attached_pending_review", report, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 CI 矩阵证据预检要求真实 GitHub Actions 运行证据，不把本地 workflow 接线当作 plan/14 通过。
    /// </summary>
    [Fact]
    public void CiMatrixEvidencePreflightRequiresWorkflowRunEvidence()
    {
        string script = ReadRepositoryFile("tools", "ci-matrix-evidence-preflight.ps1");
        string ci = ReadRepositoryFile(".github", "workflows", "ci.yml");
        string report = ReadRepositoryFile("docs", "benchmark-reports", "2026-07-02-ci-matrix-evidence.md");
        string plan = ReadRepositoryFile("plan", "14-testing-benchmarking.md");

        Assert.Contains("EvidenceManifestPath", script, StringComparison.Ordinal);
        Assert.Contains("AllowBlocked", script, StringComparison.Ordinal);
        Assert.Contains("DeclaredSha256", script, StringComparison.Ordinal);
        Assert.Contains("sha256 不匹配", script, StringComparison.Ordinal);
        Assert.Contains("Read-MarkdownEvidenceTable", script, StringComparison.Ordinal);
        Assert.Contains("conclusion", script, StringComparison.Ordinal);
        Assert.Contains("channels", script, StringComparison.Ordinal);
        Assert.Contains("报告 $key 必须为 $expected", script, StringComparison.Ordinal);
        Assert.Contains("workflowRunReport", script, StringComparison.Ordinal);
        Assert.Contains("benchmarkGuard", script, StringComparison.Ordinal);
        Assert.Contains("buildTest", script, StringComparison.Ordinal);
        Assert.Contains("verifyPublish", script, StringComparison.Ordinal);
        Assert.Contains("testsRan=true", script, StringComparison.Ordinal);
        Assert.Contains("win-arm64 当前 CI 设计应为 build-only", script, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_ci_manifest", script, StringComparison.Ordinal);
        Assert.Contains("blocked_invalid_ci_evidence", script, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_ci_scope_evidence", script, StringComparison.Ordinal);
        Assert.Contains("ci_matrix_evidence_attached_pending_review", script, StringComparison.Ordinal);
        Assert.Contains("[Console]::Error.WriteLine", script, StringComparison.Ordinal);
        Assert.Contains("CI matrix evidence preflight failed", script, StringComparison.Ordinal);
        Assert.Contains("exit 2", script, StringComparison.Ordinal);
        Assert.Contains("exit 5", script, StringComparison.Ordinal);
        Assert.DoesNotContain("status \"passed\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("status = \"passed\"", script, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("ci-evidence-build-test-${{ matrix.rid }}", ci, StringComparison.Ordinal);
        Assert.Contains("ci-evidence-benchmark-guard", ci, StringComparison.Ordinal);
        Assert.Contains("ci-evidence-verify-publish-${{ matrix.rid }}", ci, StringComparison.Ordinal);
        Assert.Contains("ci-evidence:", ci, StringComparison.Ordinal);
        Assert.Contains("Download CI evidence", ci, StringComparison.Ordinal);
        Assert.Contains("Build CI evidence manifest", ci, StringComparison.Ordinal);
        Assert.Contains("Preflight CI matrix evidence", ci, StringComparison.Ordinal);
        Assert.Contains("ci-matrix-evidence-preflight.ps1", ci, StringComparison.Ordinal);
        Assert.Contains("evidence.json", ci, StringComparison.Ordinal);
        Assert.Contains("Get-Sha256", ci, StringComparison.Ordinal);
        Assert.Contains("workflowRunSha256", ci, StringComparison.Ordinal);
        Assert.Contains("sha256 = Get-Sha256", ci, StringComparison.Ordinal);
        Assert.Contains("build-test-win-arm64.md", ci, StringComparison.Ordinal);
        Assert.Contains("testsRan = $false", ci, StringComparison.Ordinal);
        Assert.Contains("verify-publish-osx-arm64.md", ci, StringComparison.Ordinal);

        Assert.Contains("tools/ci-matrix-evidence-preflight.ps1", report, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_ci_manifest", report, StringComparison.Ordinal);
        Assert.Contains("blocked_invalid_ci_evidence", report, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_ci_scope_evidence", report, StringComparison.Ordinal);
        Assert.Contains("ci_matrix_evidence_attached_pending_review", report, StringComparison.Ordinal);
        Assert.Contains("conclusion", report, StringComparison.Ordinal);
        Assert.Contains("channels: r2r,aot", report, StringComparison.Ordinal);
        Assert.Contains("tools/ci-matrix-evidence-preflight.ps1", plan, StringComparison.Ordinal);
        Assert.Contains("blocked_invalid_ci_evidence", plan, StringComparison.Ordinal);
        Assert.Contains("ci_matrix_evidence_attached_pending_review", plan, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 CI evidence 预检的真实脚本行为：失败 conclusion 被拒绝，证据齐全也保持待审非零退出。
    /// </summary>
    [Fact]
    public void CiMatrixEvidencePreflightRejectsFailedReportsAndKeepsPendingReviewNonZero()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-ci-evidence-" + Guid.NewGuid().ToString("N"));

        try
        {
            string goodManifest = CreateCiEvidenceManifest(temp, benchmarkConclusion: "success");
            string badManifest = CreateCiEvidenceManifest(temp, benchmarkConclusion: "failure", suffix: "bad");

            string badArtifacts = Path.Combine(temp, "bad-out");
            ScriptResult bad = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "ci-matrix-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                badManifest,
                "-Artifacts",
                badArtifacts);
            Assert.Equal(5, bad.ExitCode);
            string badReport = File.ReadAllText(Path.Combine(badArtifacts, "ci-matrix-evidence-preflight.md"));
            Assert.Contains("报告 conclusion 必须为 success", badReport, StringComparison.Ordinal);

            string goodArtifacts = Path.Combine(temp, "good-out");
            ScriptResult good = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "ci-matrix-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                goodManifest,
                "-Artifacts",
                goodArtifacts);
            Assert.Equal(2, good.ExitCode);
            string goodReport = File.ReadAllText(Path.Combine(goodArtifacts, "ci-matrix-evidence-preflight.md"));
            Assert.Contains("ci_matrix_evidence_attached_pending_review", good.Output + goodReport, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 CI evidence 预检会把 schema/JSON 错误落成稳定报告，而不是直接抛出无报告异常。
    /// </summary>
    [Fact]
    public void CiMatrixEvidencePreflightRejectsInvalidSchemaWithReport()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-ci-schema-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateCiEvidenceManifest(temp, benchmarkConclusion: "success");
            string json = File.ReadAllText(manifest).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);
            File.WriteAllText(manifest, json);

            string artifacts = Path.Combine(temp, "schema-out");
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "ci-matrix-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "ci-matrix-evidence-preflight.md"));
            Assert.Contains("status: blocked_invalid_ci_evidence", report, StringComparison.Ordinal);
            Assert.Contains("schemaVersion 必须为 1", report, StringComparison.Ordinal);
            Assert.DoesNotContain("status: ci_matrix_evidence_attached_pending_review", report, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 CI evidence 预检会真实拒绝 manifest 中声明 hash 与文件内容不一致的证据。
    /// </summary>
    [Fact]
    public void CiMatrixEvidencePreflightRejectsMismatchedEvidenceHash()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-ci-hash-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateCiEvidenceManifest(temp, benchmarkConclusion: "success");
            string json = File.ReadAllText(manifest).Replace("\"sha256\": \"", "\"sha256\": \"0000", StringComparison.Ordinal);
            File.WriteAllText(manifest, json);

            string artifacts = Path.Combine(temp, "hash-out");
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "ci-matrix-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "ci-matrix-evidence-preflight.md"));
            Assert.Contains("status: blocked_missing_ci_scope_evidence", report, StringComparison.Ordinal);
            Assert.Contains("sha256 不匹配", report, StringComparison.Ordinal);
            Assert.Contains("benchmark_guard", report, StringComparison.Ordinal);
            Assert.DoesNotContain("status: ci_matrix_evidence_attached_pending_review", report, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 CI evidence 预检会拒绝把 win-arm64 build-only 结果伪装成真实测试运行。
    /// </summary>
    [Fact]
    public void CiMatrixEvidencePreflightRejectsWinArm64TestsRanMasquerade()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-ci-win-arm64-tests-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateCiEvidenceManifest(temp, benchmarkConclusion: "success");
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonObject buildTest = rootNode["buildTest"]!.AsObject();
            buildTest["win-arm64"]!.AsObject()["testsRan"] = true;
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "win-arm64-out");
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "ci-matrix-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "ci-matrix-evidence-preflight.md"));
            Assert.Contains("status: blocked_missing_ci_scope_evidence", result.Output + report, StringComparison.Ordinal);
            Assert.Contains("buildTest.win-arm64 当前 CI 设计应为 build-only，不能伪装成真实 arm64 测试", report, StringComparison.Ordinal);
            Assert.Contains("build_test/win-arm64/report 报告 tests_ran 必须为 true，实际为 false", report, StringComparison.Ordinal);
            Assert.DoesNotContain("status: ci_matrix_evidence_attached_pending_review", report, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证目标性能证据预检的真实脚本行为：未知 scope 被拒绝，证据齐全也保持待审非零退出。
    /// </summary>
    [Fact]
    public void PerformanceTargetEvidencePreflightRejectsUnknownScopesAndKeepsPendingReviewNonZero()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-performance-target-evidence-" + Guid.NewGuid().ToString("N"));

        try
        {
            string goodManifest = CreatePerformanceTargetEvidenceManifest(temp);
            string badManifest = CreatePerformanceTargetEvidenceManifest(temp, includeUnknownScope: true, suffix: "bad");

            string badArtifacts = Path.Combine(temp, "bad-out");
            ScriptResult bad = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "performance-target-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                badManifest,
                "-Artifacts",
                badArtifacts);
            Assert.Equal(5, bad.ExitCode);
            string badReport = File.ReadAllText(Path.Combine(badArtifacts, "performance-target-evidence-preflight.md"));
            Assert.Contains("未知 evidence scope", badReport, StringComparison.Ordinal);

            string goodArtifacts = Path.Combine(temp, "good-out");
            ScriptResult good = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "performance-target-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                goodManifest,
                "-Artifacts",
                goodArtifacts);
            Assert.Equal(2, good.ExitCode);
            string goodReport = File.ReadAllText(Path.Combine(goodArtifacts, "performance-target-evidence-preflight.md"));
            Assert.Contains("target_performance_evidence_attached_pending_review", good.Output + goodReport, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证目标性能证据预检会把 schema 错误写入报告，而不是直接抛异常丢失审计上下文。
    /// </summary>
    [Fact]
    public void PerformanceTargetEvidencePreflightRejectsInvalidSchemaWithReport()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-performance-target-schema-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreatePerformanceTargetEvidenceManifest(temp);
            string json = File.ReadAllText(manifest);
            json = json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);
            File.WriteAllText(manifest, json);

            string artifacts = Path.Combine(temp, "out");
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "performance-target-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "performance-target-evidence-preflight.md"));
            Assert.Contains("status: blocked_invalid_target_performance_evidence", report, StringComparison.Ordinal);
            Assert.Contains("schemaVersion 必须为 1", report, StringComparison.Ordinal);
            Assert.DoesNotContain("status: target_performance_evidence_attached_pending_review", report, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证目标性能证据预检会实际拒绝 sha256 不匹配的目标硬件 evidence。
    /// </summary>
    [Fact]
    public void PerformanceTargetEvidencePreflightRejectsMismatchedEvidenceHash()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-performance-target-hash-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreatePerformanceTargetEvidenceManifest(temp);
            string json = File.ReadAllText(manifest);
            json = json.Replace("\"sha256\": \"", "\"sha256\": \"0000", StringComparison.Ordinal);
            File.WriteAllText(manifest, json);

            string artifacts = Path.Combine(temp, "out");
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "performance-target-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "performance-target-evidence-preflight.md"));
            Assert.Contains("status: blocked_missing_target_performance_scope_evidence", report, StringComparison.Ordinal);
            Assert.Contains("sha256 不匹配", report, StringComparison.Ordinal);
            Assert.Contains("avx512_downclock_net_loss", report, StringComparison.Ordinal);
            Assert.DoesNotContain("status: target_performance_evidence_attached_pending_review", report, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证目标性能证据预检会真实拒绝缺失必需 scope 的 manifest。
    /// </summary>
    [Fact]
    public void PerformanceTargetEvidencePreflightRejectsMissingRequiredScopeWithReport()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-performance-target-missing-scope-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreatePerformanceTargetEvidenceManifest(temp);
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonArray evidence = rootNode["evidence"]!.AsArray();
            JsonNode? target = evidence.FirstOrDefault(node =>
                string.Equals((string?)node?["scope"], "hardware_counters_cache_branch", StringComparison.Ordinal));
            Assert.NotNull(target);
            _ = evidence.Remove(target);
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "missing-out");
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "performance-target-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "performance-target-evidence-preflight.md"));
            Assert.Contains("status: blocked_missing_target_performance_scope_evidence", report, StringComparison.Ordinal);
            Assert.Contains("缺少 evidence scope：hardware_counters_cache_branch", report, StringComparison.Ordinal);
            Assert.DoesNotContain("status: target_performance_evidence_attached_pending_review", report, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证目标性能证据预检会拒绝未声明 BenchmarkDotNet 实测的 RID cells/frame 节点。
    /// </summary>
    [Fact]
    public void PerformanceTargetEvidencePreflightRejectsCellsFrameWithoutBenchmarkDotNet()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-performance-target-cells-frame-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreatePerformanceTargetEvidenceManifest(temp);
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonObject cellsFrame = rootNode["cellsFrame"]!.AsObject();
            cellsFrame["win-arm64"]!.AsObject()["benchmarkDotNet"] = false;
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "cells-frame-out");
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "performance-target-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "performance-target-evidence-preflight.md"));
            Assert.Contains("status: blocked_missing_target_performance_scope_evidence", result.Output + report, StringComparison.Ordinal);
            Assert.Contains("cellsFrame.win-arm64 必须标记 benchmarkDotNet=true", report, StringComparison.Ordinal);
            Assert.DoesNotContain("status: target_performance_evidence_attached_pending_review", report, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
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
        Assert.Contains("matrix.channel == 'aot' && matrix.shell == 'pwsh'", release, StringComparison.Ordinal);
        Assert.Contains("matrix.channel == 'aot' && matrix.shell == 'bash'", release, StringComparison.Ordinal);
        Assert.Contains("if: always() && matrix.channel == 'aot'", release, StringComparison.Ordinal);
        Assert.Contains("aot-simd-probe.ps1", release, StringComparison.Ordinal);
        Assert.Contains("aot-simd-probe.sh", release, StringComparison.Ordinal);
        Assert.Contains("-simd-output.txt", release, StringComparison.Ordinal);
        Assert.Contains("## Probe output", release, StringComparison.Ordinal);
        Assert.Contains("$probeOutput", release, StringComparison.Ordinal);
        Assert.Contains("ymm/zmm", aotProbePs1, StringComparison.Ordinal);
        Assert.Contains("NEON marker", aotProbePs1, StringComparison.Ordinal);
        Assert.Contains("[yz]mm", aotProbeSh, StringComparison.Ordinal);
        Assert.Contains("NEON marker", aotProbeSh, StringComparison.Ordinal);
        Assert.DoesNotContain("skipped for non-x64", aotProbePs1, StringComparison.Ordinal);
        Assert.DoesNotContain("skipped for non-x64", aotProbeSh, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 PowerShell/Bash 发行产物审计在 AOT Box2D 与 package/SHA256 规则上保持同等严格。
    /// </summary>
    [Fact]
    public void ReleaseArtifactAuditsRejectMispackagedNativeAndChecksumOutputs()
    {
        string auditPs1 = ReadRepositoryFile("tools", "audit-release-artifacts.ps1");
        string auditSh = ReadRepositoryFile("tools", "audit-release-artifacts.sh");
        string packagePs1 = ReadRepositoryFile("tools", "package.ps1");
        string packageSh = ReadRepositoryFile("tools", "package.sh");
        string evidence = ReadRepositoryFile("tools", "release-evidence-preflight.ps1");
        string release = ReadRepositoryFile(".github", "workflows", "release.yml");
        string example = ReadRepositoryFile("docs", "release-reports", "release-evidence-manifest.example.json");
        string releaseReport = ReadRepositoryFile("docs", "release-reports", "2026-07-02-win-x64-publish.md");
        string plan = ReadRepositoryFile("plan", "15-build-packaging-distribution.md");
        string conventions = ReadRepositoryFile("plan", "00-conventions-and-techstack.md");

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

        Assert.Contains("PixelEngine.Tools.DeterministicPackage", packagePs1, StringComparison.Ordinal);
        Assert.Contains("PixelEngine.Tools.DeterministicPackage", packageSh, StringComparison.Ordinal);
        Assert.DoesNotContain("Compress-Archive", packagePs1, StringComparison.Ordinal);
        Assert.DoesNotContain("tar -czf", packagePs1, StringComparison.Ordinal);
        Assert.DoesNotContain("zip -qr", packageSh, StringComparison.Ordinal);
        Assert.DoesNotContain("tar -czf", packageSh, StringComparison.Ordinal);

        Assert.Contains("EvidenceManifestPath", evidence, StringComparison.Ordinal);
        Assert.Contains("schemaVersion", evidence, StringComparison.Ordinal);
        Assert.Contains("DeclaredSha256", evidence, StringComparison.Ordinal);
        Assert.Contains("缺少 sha256", evidence, StringComparison.Ordinal);
        Assert.Contains("sha256 不匹配", evidence, StringComparison.Ordinal);
        Assert.Contains("Read-MarkdownEvidenceTable", evidence, StringComparison.Ordinal);
        Assert.Contains("报告 $key 必须为 $expected", evidence, StringComparison.Ordinal);
        Assert.Contains("packageReport", evidence, StringComparison.Ordinal);
        Assert.Contains("deterministic_hash", evidence, StringComparison.Ordinal);
        Assert.Contains("r2r_lightup", evidence, StringComparison.Ordinal);
        Assert.Contains("未知 RID", evidence, StringComparison.Ordinal);
        Assert.Contains("未知 channel", evidence, StringComparison.Ordinal);
        Assert.Contains("重复 evidence scope", evidence, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_release_manifest", evidence, StringComparison.Ordinal);
        Assert.Contains("blocked_invalid_release_evidence", evidence, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_release_scope_evidence", evidence, StringComparison.Ordinal);
        Assert.Contains("release_evidence_attached_pending_review", evidence, StringComparison.Ordinal);
        Assert.Contains("Release evidence preflight failed: release_evidence_attached_pending_review", evidence, StringComparison.Ordinal);
        Assert.Contains("if (-not $AllowBlocked)", evidence, StringComparison.Ordinal);
        Assert.Contains("exit 2", evidence, StringComparison.Ordinal);
        Assert.Contains("win-x64", evidence, StringComparison.Ordinal);
        Assert.Contains("osx-arm64", evidence, StringComparison.Ordinal);
        Assert.Contains("simdProbe", evidence, StringComparison.Ordinal);
        Assert.Contains("simdProbeKind", evidence, StringComparison.Ordinal);
        Assert.Contains("x64_ymm_zmm", evidence, StringComparison.Ordinal);
        Assert.Contains("arm64_neon", evidence, StringComparison.Ordinal);
        Assert.Contains("不能用 skip 或其它报告冒充 SIMD 证据", evidence, StringComparison.Ordinal);
        Assert.Contains("不能是 skip 报告", evidence, StringComparison.Ordinal);
        Assert.Contains("必须包含 NEON 证据", evidence, StringComparison.Ordinal);
        Assert.Contains("必须包含 ymm 或 zmm 证据", evidence, StringComparison.Ordinal);
        Assert.Contains("codesignReport", evidence, StringComparison.Ordinal);
        Assert.Contains("notarizationReport", evidence, StringComparison.Ordinal);
        Assert.Contains("r2rLightupReport", evidence, StringComparison.Ordinal);
        Assert.Contains("githubRelease", evidence, StringComparison.Ordinal);
        Assert.Contains("deterministicHashReport", evidence, StringComparison.Ordinal);
        Assert.DoesNotContain("status \"passed\"", evidence, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("tools/release-evidence-preflight.ps1", releaseReport, StringComparison.Ordinal);
        Assert.Contains("release_evidence_attached_pending_review", releaseReport, StringComparison.Ordinal);
        Assert.Contains("blocked_invalid_release_evidence", releaseReport, StringComparison.Ordinal);
        Assert.Contains("-AllowBlocked", releaseReport, StringComparison.Ordinal);
        Assert.Contains("sha256", releaseReport, StringComparison.Ordinal);
        Assert.Contains("重新计算", releaseReport, StringComparison.Ordinal);
        Assert.Contains("conclusion: success", releaseReport, StringComparison.Ordinal);
        Assert.Contains("simdProbeKind", releaseReport, StringComparison.Ordinal);
        Assert.Contains("arm64_neon", releaseReport, StringComparison.Ordinal);
        Assert.Contains("pending review 误当成验收通过", releaseReport, StringComparison.Ordinal);
        Assert.Contains("release-evidence-manifest.example.json", releaseReport, StringComparison.Ordinal);
        Assert.Contains("tools/release-evidence-preflight.ps1", plan, StringComparison.Ordinal);

        Assert.Contains("tools/release-evidence-preflight.ps1", conventions, StringComparison.Ordinal);
        Assert.Contains("发行与 Box2D dual-build 工具链已在 `plan/15`", conventions, StringComparison.Ordinal);
        Assert.Contains("tools/audit-release-artifacts.*", conventions, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_release_manifest", conventions, StringComparison.Ordinal);
        Assert.Contains("blocked_invalid_release_evidence", conventions, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_release_scope_evidence", conventions, StringComparison.Ordinal);
        Assert.Contains("release_evidence_attached_pending_review", conventions, StringComparison.Ordinal);

        Assert.Contains("release-evidence-", release, StringComparison.Ordinal);
        Assert.Contains("Upload publish evidence", release, StringComparison.Ordinal);
        Assert.Contains("Upload verify evidence", release, StringComparison.Ordinal);
        Assert.Contains("Upload package evidence", release, StringComparison.Ordinal);
        Assert.Contains("Download release evidence", release, StringComparison.Ordinal);
        Assert.Contains("Verify deterministic package hashes", release, StringComparison.Ordinal);
        Assert.Contains("artifacts/package-deterministic", release, StringComparison.Ordinal);
        Assert.Contains("## Package rebuild comparison", release, StringComparison.Ordinal);
        Assert.Contains("original_hash", release, StringComparison.Ordinal);
        Assert.Contains("rebuilt_hash", release, StringComparison.Ordinal);
        Assert.Contains("deterministic package command exited", release, StringComparison.Ordinal);
        Assert.Contains("if [[ \"$conclusion\" != \"success\" ]]", release, StringComparison.Ordinal);
        Assert.Contains("Build release evidence manifest", release, StringComparison.Ordinal);
        Assert.Contains("blocked_not_tag_release", release, StringComparison.Ordinal);
        Assert.Contains("release_tag", release, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_target_disassembly", release, StringComparison.Ordinal);
        Assert.Contains("Do not treat this report as R2R light-up success", release, StringComparison.Ordinal);
        Assert.Contains("Preflight release evidence", release, StringComparison.Ordinal);
        Assert.Contains("release-evidence-preflight.ps1", release, StringComparison.Ordinal);
        Assert.Contains("evidence.json", release, StringComparison.Ordinal);
        Assert.Contains("r2rLightupReport", release, StringComparison.Ordinal);
        Assert.Contains("deterministicHashReport", release, StringComparison.Ordinal);
        Assert.Contains("function Get-Sha256", release, StringComparison.Ordinal);
        Assert.Contains("workflowRunSha256", release, StringComparison.Ordinal);
        Assert.Contains("publishSha256", release, StringComparison.Ordinal);
        Assert.Contains("packageReportSha256", release, StringComparison.Ordinal);
        Assert.Contains("simdProbeKind", release, StringComparison.Ordinal);
        Assert.Contains("x64_ymm_zmm", release, StringComparison.Ordinal);
        Assert.Contains("arm64_neon", release, StringComparison.Ordinal);

        Assert.Contains("\"schemaVersion\": 1", example, StringComparison.Ordinal);
        Assert.Contains("\"workflowRunReport\"", example, StringComparison.Ordinal);
        Assert.Contains("\"workflowRunSha256\"", example, StringComparison.Ordinal);
        Assert.Contains("\"r2rLightupReport\"", example, StringComparison.Ordinal);
        Assert.Contains("\"r2rLightupSha256\"", example, StringComparison.Ordinal);
        Assert.Contains("\"githubRelease\"", example, StringComparison.Ordinal);
        Assert.Contains("\"simdProbe\"", example, StringComparison.Ordinal);
        Assert.Contains("\"simdProbeSha256\"", example, StringComparison.Ordinal);
        Assert.Contains("\"packageReport\"", example, StringComparison.Ordinal);
        Assert.Contains("\"packageReportSha256\"", example, StringComparison.Ordinal);
        Assert.Contains("\"simdProbeKind\": \"x64_ymm_zmm\"", example, StringComparison.Ordinal);
        Assert.Contains("\"simdProbeKind\": \"arm64_neon\"", example, StringComparison.Ordinal);
        Assert.Contains("\"codesignReport\"", example, StringComparison.Ordinal);
        Assert.Contains("\"codesignSha256\"", example, StringComparison.Ordinal);
        Assert.Contains("\"notarizationReport\"", example, StringComparison.Ordinal);

        using System.Text.Json.JsonDocument exampleManifest = System.Text.Json.JsonDocument.Parse(example);
        System.Text.Json.JsonElement artifacts = exampleManifest.RootElement.GetProperty("artifacts");
        foreach (string rid in new[] { "win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64" })
        {
            Assert.True(artifacts.TryGetProperty(rid, out System.Text.Json.JsonElement ridNode), $"示例 manifest 缺少 {rid}");
            Assert.True(ridNode.TryGetProperty("r2r", out _), $"示例 manifest 缺少 {rid}/r2r");
            Assert.True(ridNode.TryGetProperty("aot", out System.Text.Json.JsonElement aotNode), $"示例 manifest 缺少 {rid}/aot");
            Assert.True(aotNode.TryGetProperty("simdProbeKind", out _), $"示例 manifest 缺少 {rid}/aot simdProbeKind");
        }
    }

    /// <summary>
    /// 验证 deterministic package 工具固定 entry 顺序、时间戳与归档实现，相同内容不同 metadata 仍产出相同 hash。
    /// </summary>
    [Fact]
    public void DeterministicPackageToolProducesStableZipAndTarGzArchives()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-deterministic-package-" + Guid.NewGuid().ToString("N"));

        try
        {
            string sourceA = Path.Combine(temp, "source-a");
            string sourceB = Path.Combine(temp, "source-b");
            CreatePackageSource(sourceA, DateTimeOffset.FromUnixTimeSeconds(1));
            CreatePackageSource(sourceB, DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));

            string zipA = Path.Combine(temp, "a.zip");
            string zipB = Path.Combine(temp, "b.zip");
            string tarA = Path.Combine(temp, "a.tar.gz");
            string tarB = Path.Combine(temp, "b.tar.gz");
            string project = Path.Combine(root, "tools", "PixelEngine.Tools.DeterministicPackage", "PixelEngine.Tools.DeterministicPackage.csproj");

            Assert.Equal(0, RunDotNet(root, "run", "--project", project, "-c", "Release", "--", "--source", sourceA, "--output", zipA, "--root-name", "PixelEngine-Demo-test-win-x64-r2r", "--format", "zip").ExitCode);
            Assert.Equal(0, RunDotNet(root, "run", "--project", project, "-c", "Release", "--", "--source", sourceB, "--output", zipB, "--root-name", "PixelEngine-Demo-test-win-x64-r2r", "--format", "zip").ExitCode);
            Assert.Equal(GetSha256(zipA), GetSha256(zipB));

            Assert.Equal(0, RunDotNet(root, "run", "--project", project, "-c", "Release", "--", "--source", sourceA, "--output", tarA, "--root-name", "PixelEngine-Demo-test-linux-x64-r2r", "--format", "tar.gz").ExitCode);
            Assert.Equal(0, RunDotNet(root, "run", "--project", project, "-c", "Release", "--", "--source", sourceB, "--output", tarB, "--root-name", "PixelEngine-Demo-test-linux-x64-r2r", "--format", "tar.gz").ExitCode);
            Assert.Equal(GetSha256(tarA), GetSha256(tarB));
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 release evidence 预检的真实脚本行为：失败 package 报告被拒绝，证据齐全也保持待审非零退出。
    /// </summary>
    [Fact]
    public void ReleaseEvidencePreflightRejectsFailedReportsAndKeepsPendingReviewNonZero()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-release-evidence-" + Guid.NewGuid().ToString("N"));

        try
        {
            string goodManifest = CreateReleaseEvidenceManifest(temp, packageConclusion: "success");
            string badManifest = CreateReleaseEvidenceManifest(temp, packageConclusion: "failure", suffix: "bad");
            string badR2RManifest = CreateReleaseEvidenceManifest(temp, packageConclusion: "success", r2rLightupConclusion: "failure", suffix: "bad-r2r");

            string badArtifacts = Path.Combine(temp, "bad-out");
            ScriptResult bad = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "release-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                badManifest,
                "-Artifacts",
                badArtifacts);
            Assert.Equal(5, bad.ExitCode);
            string badReport = File.ReadAllText(Path.Combine(badArtifacts, "release-evidence-preflight.md"));
            Assert.Contains("报告 conclusion 必须为 success", badReport, StringComparison.Ordinal);

            string badR2RArtifacts = Path.Combine(temp, "bad-r2r-out");
            ScriptResult badR2R = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "release-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                badR2RManifest,
                "-Artifacts",
                badR2RArtifacts);
            Assert.Equal(5, badR2R.ExitCode);
            string badR2RReport = File.ReadAllText(Path.Combine(badR2RArtifacts, "release-evidence-preflight.md"));
            Assert.Contains("r2r_lightup 报告 conclusion 必须为 success", badR2RReport, StringComparison.Ordinal);

            string goodArtifacts = Path.Combine(temp, "good-out");
            ScriptResult good = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "release-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                goodManifest,
                "-Artifacts",
                goodArtifacts);
            Assert.Equal(2, good.ExitCode);
            string goodReport = File.ReadAllText(Path.Combine(goodArtifacts, "release-evidence-preflight.md"));
            Assert.Contains("release_evidence_attached_pending_review", good.Output + goodReport, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 release evidence 预检会把 schema/JSON 错误落成稳定报告，而不是直接抛出无报告异常。
    /// </summary>
    [Fact]
    public void ReleaseEvidencePreflightRejectsInvalidSchemaWithReport()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-release-schema-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateReleaseEvidenceManifest(temp, packageConclusion: "success");
            string json = File.ReadAllText(manifest).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);
            File.WriteAllText(manifest, json);

            string artifacts = Path.Combine(temp, "schema-out");
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "release-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "release-evidence-preflight.md"));
            Assert.Contains("blocked_invalid_release_evidence", result.Output + report, StringComparison.Ordinal);
            Assert.Contains("schemaVersion 必须为 1", report, StringComparison.Ordinal);
            Assert.DoesNotContain("status | release_evidence_attached_pending_review", report, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 release evidence 预检会把 malformed JSON 落成稳定报告。
    /// </summary>
    [Fact]
    public void ReleaseEvidencePreflightRejectsMalformedJsonWithReport()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-release-malformed-json-" + Guid.NewGuid().ToString("N"));

        try
        {
            _ = Directory.CreateDirectory(temp);
            string manifest = Path.Combine(temp, "release-evidence.json");
            File.WriteAllText(manifest, "{ invalid");

            string artifacts = Path.Combine(temp, "json-out");
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "release-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "release-evidence-preflight.md"));
            Assert.Contains("blocked_invalid_release_evidence", result.Output + report, StringComparison.Ordinal);
            Assert.Contains("manifest JSON 无法解析", report, StringComparison.Ordinal);
            Assert.DoesNotContain("status | release_evidence_attached_pending_review", report, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 release evidence 预检会真实拒绝缺失必需 RID/channel 节点的 manifest。
    /// </summary>
    [Fact]
    public void ReleaseEvidencePreflightRejectsMissingRequiredScopesWithReport()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-release-missing-scope-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateReleaseEvidenceManifest(temp, packageConclusion: "success");
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonObject artifacts = rootNode["artifacts"]!.AsObject();
            _ = artifacts["win-x64"]!.AsObject().Remove("r2r");
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string output = Path.Combine(temp, "missing-out");
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "release-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                output);

            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(output, "release-evidence-preflight.md"));
            Assert.Contains("blocked_missing_release_scope_evidence", result.Output + report, StringComparison.Ordinal);
            Assert.Contains("artifacts.win-x64.r2r 缺失", report, StringComparison.Ordinal);
            Assert.DoesNotContain("status | release_evidence_attached_pending_review", report, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 release evidence 预检会真实拒绝 manifest 中声明 hash 与文件内容不一致的证据。
    /// </summary>
    [Fact]
    public void ReleaseEvidencePreflightRejectsMismatchedEvidenceHash()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-release-hash-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateReleaseEvidenceManifest(temp, packageConclusion: "success");
            string json = File.ReadAllText(manifest).Replace("\"workflowRunSha256\": \"", "\"workflowRunSha256\": \"0000", StringComparison.Ordinal);
            File.WriteAllText(manifest, json);

            string artifacts = Path.Combine(temp, "hash-out");
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "release-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "release-evidence-preflight.md"));
            Assert.Contains("blocked_missing_release_scope_evidence", result.Output + report, StringComparison.Ordinal);
            Assert.Contains("sha256 不匹配", report, StringComparison.Ordinal);
            Assert.Contains("workflow_run", report, StringComparison.Ordinal);
            Assert.DoesNotContain("status | release_evidence_attached_pending_review", report, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证非 tag workflow_dispatch 的 GitHub Release 上传报告不能冒充正式发布成功证据。
    /// </summary>
    [Fact]
    public void ReleaseEvidencePreflightRejectsNonTagUploadReportAsReleaseSuccess()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-release-non-tag-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateReleaseEvidenceManifest(
                temp,
                packageConclusion: "success",
                suffix: "blocked-not-tag",
                githubReleaseConclusion: "blocked_not_tag_release");

            string artifacts = Path.Combine(temp, "non-tag-out");
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "release-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "release-evidence-preflight.md"));
            Assert.Contains("blocked_missing_release_scope_evidence", result.Output + report, StringComparison.Ordinal);
            Assert.Contains("github_release_upload 报告 conclusion 必须为 success，实际为 blocked_not_tag_release", report, StringComparison.Ordinal);
            Assert.Contains("github_release_upload", report, StringComparison.Ordinal);
            Assert.DoesNotContain("status | release_evidence_attached_pending_review", report, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 AOT arm64 SIMD probe 不能用 skip 报告冒充 NEON 证据。
    /// </summary>
    [Fact]
    public void ReleaseEvidencePreflightRejectsSkippedArm64SimdProbe()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-release-arm64-simd-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateReleaseEvidenceManifest(temp, packageConclusion: "success");
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonObject node = rootNode["artifacts"]!["win-arm64"]!["aot"]!.AsObject();
            string simdProbe = (string)node["simdProbe"]!;
            _ = WriteMarkdownEvidence(
                simdProbe,
                new Dictionary<string, string>
                {
                    ["rid"] = "win-arm64",
                    ["channel"] = "aot",
                    ["conclusion"] = "success",
                },
                "skipped for non-x64");
            node["simdProbeSha256"] = GetSha256(simdProbe);
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "arm64-simd-out");
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "release-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "release-evidence-preflight.md"));
            Assert.Contains("blocked_missing_release_scope_evidence", result.Output + report, StringComparison.Ordinal);
            Assert.Contains("artifacts.win-arm64.aot.simdProbe 不能是 skip 报告", report, StringComparison.Ordinal);
            Assert.Contains("artifacts.win-arm64.aot.simdProbe 必须包含 NEON 证据", report, StringComparison.Ordinal);
            Assert.DoesNotContain("status | release_evidence_attached_pending_review", report, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
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

    private static bool IsWindowsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        System.Security.Principal.WindowsPrincipal principal = new(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private static string CreateCiEvidenceManifest(string tempRoot, string benchmarkConclusion, string suffix = "good")
    {
        string evidenceRoot = Path.Combine(tempRoot, suffix, "artifacts", "ci-matrix-evidence");
        _ = Directory.CreateDirectory(evidenceRoot);

        string workflow = WriteMarkdownEvidence(
            Path.Combine(evidenceRoot, "workflow-run.md"),
            new Dictionary<string, string>
            {
                ["run_id"] = "1",
                ["sha"] = "abc",
                ["ref"] = "refs/heads/main",
                ["conclusion"] = "success",
            });

        string benchmark = WriteMarkdownEvidence(
            Path.Combine(evidenceRoot, "benchmark-guard.md"),
            new Dictionary<string, string>
            {
                ["run_id"] = "1",
                ["sha"] = "abc",
                ["conclusion"] = benchmarkConclusion,
            });

        string[] rids = ["win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"];
        string[] verifyRids = ["win-x64", "linux-x64", "osx-x64", "osx-arm64"];
        Dictionary<string, object> buildTest = [];
        foreach (string rid in rids)
        {
            bool testsRan = rid != "win-arm64";
            string report = WriteMarkdownEvidence(
                Path.Combine(evidenceRoot, $"build-test-{rid}.md"),
                new Dictionary<string, string>
                {
                    ["rid"] = rid,
                    ["build_only"] = testsRan ? "false" : "true",
                    ["tests_ran"] = testsRan ? "true" : "false",
                    ["run_id"] = "1",
                    ["sha"] = "abc",
                    ["conclusion"] = "success",
                });
            buildTest[rid] = new Dictionary<string, object>
            {
                ["report"] = report,
                ["sha256"] = GetSha256(report),
                ["testsRan"] = testsRan,
            };
        }

        Dictionary<string, object> verifyPublish = [];
        foreach (string rid in verifyRids)
        {
            string report = WriteMarkdownEvidence(
                Path.Combine(evidenceRoot, $"verify-publish-{rid}.md"),
                new Dictionary<string, string>
                {
                    ["rid"] = rid,
                    ["channels"] = "r2r,aot",
                    ["run_id"] = "1",
                    ["sha"] = "abc",
                    ["conclusion"] = "success",
                });
            verifyPublish[rid] = new Dictionary<string, object>
            {
                ["report"] = report,
                ["sha256"] = GetSha256(report),
            };
        }

        Dictionary<string, object> manifest = new()
        {
            ["schemaVersion"] = 1,
            ["workflowRunReport"] = workflow,
            ["workflowRunSha256"] = GetSha256(workflow),
            ["benchmarkGuard"] = new Dictionary<string, object>
            {
                ["report"] = benchmark,
                ["sha256"] = GetSha256(benchmark),
            },
            ["buildTest"] = buildTest,
            ["verifyPublish"] = verifyPublish,
        };

        string manifestPath = Path.Combine(tempRoot, suffix, "evidence.json");
        File.WriteAllText(
            manifestPath,
            System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return manifestPath;
    }

    private static string CreateReleaseEvidenceManifest(
        string tempRoot,
        string packageConclusion,
        string suffix = "good",
        string deterministicConclusion = "success",
        string r2rLightupConclusion = "success",
        string githubReleaseConclusion = "success")
    {
        string evidenceRoot = Path.Combine(tempRoot, suffix, "artifacts", "release-evidence");
        string packageRoot = Path.Combine(tempRoot, suffix, "artifacts", "package");
        _ = Directory.CreateDirectory(evidenceRoot);
        _ = Directory.CreateDirectory(packageRoot);

        string workflow = WriteMarkdownEvidence(Path.Combine(evidenceRoot, "workflow-run.md"), new Dictionary<string, string> { ["conclusion"] = "success" });
        string upload = WriteMarkdownEvidence(Path.Combine(evidenceRoot, "github-release-upload.md"), new Dictionary<string, string> { ["conclusion"] = githubReleaseConclusion });
        string deterministic = WriteMarkdownEvidence(
            Path.Combine(evidenceRoot, "deterministic-hash.md"),
            new Dictionary<string, string> { ["run_id"] = "1", ["sha"] = "abc", ["conclusion"] = deterministicConclusion });
        string r2rLightup = WriteMarkdownEvidence(
            Path.Combine(evidenceRoot, "r2r-lightup.md"),
            new Dictionary<string, string> { ["run_id"] = "1", ["sha"] = "abc", ["conclusion"] = r2rLightupConclusion });
        string checksum = WriteTextEvidence(Path.Combine(packageRoot, "SHA256SUMS"), "placeholder checksum");

        string[] rids = ["win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"];
        string[] channels = ["r2r", "aot"];
        Dictionary<string, object> artifacts = [];
        foreach (string rid in rids)
        {
            Dictionary<string, object> ridNode = [];
            foreach (string channel in channels)
            {
                string publish = WriteReleaseJobEvidence(evidenceRoot, rid, channel, "publish", "success");
                string verify = WriteReleaseJobEvidence(evidenceRoot, rid, channel, "verify", "success");
                string currentPackageConclusion = rid == "win-x64" && channel == "r2r" ? packageConclusion : "success";
                string packageReport = WriteReleaseJobEvidence(evidenceRoot, rid, channel, "package", currentPackageConclusion);
                string extension = rid.StartsWith("win-", StringComparison.Ordinal) ? "zip" : "tar.gz";
                string package = WriteTextEvidence(Path.Combine(packageRoot, $"PixelEngine-Demo-0.1.0-{rid}-{channel}.{extension}"), $"package {rid} {channel}");

                Dictionary<string, object> node = new()
                {
                    ["publishReport"] = publish,
                    ["publishSha256"] = GetSha256(publish),
                    ["verifyReport"] = verify,
                    ["verifySha256"] = GetSha256(verify),
                    ["packageReport"] = packageReport,
                    ["packageReportSha256"] = GetSha256(packageReport),
                    ["package"] = package,
                    ["packageSha256"] = GetSha256(package),
                    ["checksum"] = checksum,
                    ["checksumSha256"] = GetSha256(checksum),
                };

                if (channel == "aot")
                {
                    string simdExtra = rid.EndsWith("-x64", StringComparison.Ordinal) ? "SIMD evidence contains ymm and zmm." : "SIMD evidence contains NEON.";
                    string simd = WriteReleaseJobEvidence(evidenceRoot, rid, channel, "simd", "success", simdExtra);
                    node["simdProbe"] = simd;
                    node["simdProbeSha256"] = GetSha256(simd);
                    node["simdProbeKind"] = rid.EndsWith("-x64", StringComparison.Ordinal) ? "x64_ymm_zmm" : "arm64_neon";
                }

                if (rid.StartsWith("osx-", StringComparison.Ordinal))
                {
                    string codesign = WriteReleaseJobEvidence(evidenceRoot, rid, channel, "codesign", "success");
                    string notarization = WriteReleaseJobEvidence(evidenceRoot, rid, channel, "notarization", "success");
                    node["codesignReport"] = codesign;
                    node["codesignSha256"] = GetSha256(codesign);
                    node["notarizationReport"] = notarization;
                    node["notarizationSha256"] = GetSha256(notarization);
                }

                ridNode[channel] = node;
            }

            artifacts[rid] = ridNode;
        }

        Dictionary<string, object> manifest = new()
        {
            ["schemaVersion"] = 1,
            ["workflowRunReport"] = workflow,
            ["workflowRunSha256"] = GetSha256(workflow),
            ["deterministicHashReport"] = deterministic,
            ["deterministicHashSha256"] = GetSha256(deterministic),
            ["r2rLightupReport"] = r2rLightup,
            ["r2rLightupSha256"] = GetSha256(r2rLightup),
            ["githubRelease"] = new Dictionary<string, object>
            {
                ["uploadReport"] = upload,
                ["uploadSha256"] = GetSha256(upload),
            },
            ["artifacts"] = artifacts,
        };

        string manifestPath = Path.Combine(tempRoot, suffix, "release-evidence.json");
        File.WriteAllText(
            manifestPath,
            System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return manifestPath;
    }

    private static string CreateNativeLeakEvidenceManifest(string tempRoot, string corruptHashScope = "", string suffix = "good")
    {
        string evidenceRoot = Path.Combine(tempRoot, suffix, "artifacts", "native-leak-evidence");
        _ = Directory.CreateDirectory(evidenceRoot);

        Dictionary<string, object> scopes = [];
        foreach (string scope in new[] { "gl", "openal", "box2d", "alc" })
        {
            string report = WriteTextEvidence(Path.Combine(evidenceRoot, $"{scope}.md"), $"{scope} detector report");
            string hash = scope.Equals(corruptHashScope, StringComparison.Ordinal) ? new string('0', 64) : GetSha256(report);
            scopes[scope] = new Dictionary<string, object>
            {
                ["detector"] = "external-detector",
                ["report"] = report,
                ["sha256"] = hash,
            };
        }

        Dictionary<string, object> manifest = new()
        {
            ["schemaVersion"] = 1,
            ["scopes"] = scopes,
        };

        string manifestPath = Path.Combine(tempRoot, suffix, "native-leak-evidence.json");
        File.WriteAllText(
            manifestPath,
            System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return manifestPath;
    }

    private static string CreateFlatEvidenceManifest(
        string tempRoot,
        IReadOnlyList<string> scopes,
        string suffix,
        bool includeDemoManualMetadata = false)
    {
        string evidenceRoot = Path.Combine(tempRoot, suffix, "artifacts", "flat-evidence");
        _ = Directory.CreateDirectory(evidenceRoot);

        List<Dictionary<string, object>> evidence = [];
        foreach (string scope in scopes)
        {
            string safeScope = scope.Replace('/', '-');
            bool isVideo = includeDemoManualMetadata && scope.EndsWith("Video", StringComparison.Ordinal);
            string extension = isVideo ? ".mp4" : ".md";
            string report = WriteTextEvidence(Path.Combine(evidenceRoot, $"{safeScope}{extension}"), $"{scope} evidence");
            Dictionary<string, object> entry = new()
            {
                ["scope"] = scope,
                ["path"] = report,
                ["sha256"] = GetSha256(report),
            };

            if (includeDemoManualMetadata)
            {
                entry["kind"] = isVideo ? "video" : "report";
                entry["reviewer"] = "test-reviewer";
                entry["capturedAt"] = "2026-07-03T00:00:00Z";
                entry["notes"] = $"{scope} notes";
                if (isVideo)
                {
                    entry["durationSeconds"] = 1.0;
                }
            }

            evidence.Add(entry);
        }

        Dictionary<string, object> manifest = new()
        {
            ["schemaVersion"] = 1,
            ["evidence"] = evidence,
        };

        string manifestPath = Path.Combine(tempRoot, suffix, "flat-evidence.json");
        File.WriteAllText(
            manifestPath,
            System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return manifestPath;
    }

    private static void AddDuplicateFlatEvidenceScope(string manifestPath, string scope)
    {
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        JsonArray evidence = manifest["evidence"]!.AsArray();
        JsonNode? source = evidence.FirstOrDefault(node => string.Equals((string?)node?["scope"], scope, StringComparison.Ordinal));
        Assert.NotNull(source);
        evidence.Add(source.DeepClone());
        File.WriteAllText(manifestPath, manifest.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static void SetFlatEvidenceProperty(string manifestPath, string scope, string propertyName, JsonNode? value)
    {
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        JsonArray evidence = manifest["evidence"]!.AsArray();
        JsonObject? entry = evidence
            .Select(node => node?.AsObject())
            .FirstOrDefault(node => string.Equals((string?)node?["scope"], scope, StringComparison.Ordinal));
        Assert.NotNull(entry);
        entry[propertyName] = value;
        File.WriteAllText(manifestPath, manifest.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static string CreatePerformanceTargetEvidenceManifest(string tempRoot, bool includeUnknownScope = false, string suffix = "good")
    {
        string[] rids = ["win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"];
        string[] scopes =
        [
            "avx512_downclock_net_loss",
            "hardware_counters_cache_branch",
            "frame_budget_target_hardware",
            .. rids.Select(rid => $"cells_frame/{rid}"),
        ];

        string evidenceRoot = Path.Combine(tempRoot, suffix, "artifacts", "performance-target-evidence");
        _ = Directory.CreateDirectory(evidenceRoot);

        List<Dictionary<string, object>> evidence = [];
        foreach (string scope in scopes)
        {
            string safeScope = scope.Replace('/', '-');
            string report = WriteTextEvidence(Path.Combine(evidenceRoot, $"{safeScope}.md"), $"{scope} evidence");
            evidence.Add(new Dictionary<string, object>
            {
                ["scope"] = scope,
                ["path"] = report,
                ["sha256"] = GetSha256(report),
            });
        }

        if (includeUnknownScope)
        {
            string report = WriteTextEvidence(Path.Combine(evidenceRoot, "unknown-scope.md"), "unknown evidence");
            evidence.Add(new Dictionary<string, object>
            {
                ["scope"] = "unexpected_scope",
                ["path"] = report,
                ["sha256"] = GetSha256(report),
            });
        }

        Dictionary<string, object> cellsFrame = [];
        foreach (string rid in rids)
        {
            cellsFrame[rid] = new Dictionary<string, object>
            {
                ["benchmarkDotNet"] = true,
            };
        }

        Dictionary<string, object> manifest = new()
        {
            ["schemaVersion"] = 1,
            ["evidence"] = evidence,
            ["cellsFrame"] = cellsFrame,
        };

        string manifestPath = Path.Combine(tempRoot, suffix, "performance-target-evidence.json");
        File.WriteAllText(
            manifestPath,
            System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return manifestPath;
    }

    private static string WriteMarkdownEvidence(string path, IReadOnlyDictionary<string, string> values, string extra = "")
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        List<string> lines =
        [
            "# evidence",
            "",
            "| Key | Value |",
            "|---|---|",
        ];

        foreach (KeyValuePair<string, string> item in values)
        {
            lines.Add($"| {item.Key} | {item.Value} |");
        }

        if (!string.IsNullOrWhiteSpace(extra))
        {
            lines.Add("");
            lines.Add(extra);
        }

        File.WriteAllLines(path, lines);
        return path;
    }

    private static string WriteReleaseJobEvidence(string evidenceRoot, string rid, string channel, string name, string conclusion, string extra = "")
    {
        string path = Path.Combine(evidenceRoot, $"{rid}-{channel}-{name}.md");
        string written = WriteMarkdownEvidence(
            path,
            new Dictionary<string, string>
            {
                ["rid"] = rid,
                ["channel"] = channel,
                ["run_id"] = "1",
                ["sha"] = "abc",
                ["conclusion"] = conclusion,
            });

        if (!string.IsNullOrWhiteSpace(extra))
        {
            File.AppendAllText(written, Environment.NewLine + extra + Environment.NewLine);
        }

        return written;
    }

    private static string WriteTextEvidence(string path, string content)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string GetSha256(string path)
    {
        using System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static ScriptResult RunPowerShellScript(string workingDirectory, string scriptPath, params string[] arguments)
    {
        using System.Diagnostics.Process process = new();
        process.StartInfo.FileName = "pwsh";
        process.StartInfo.WorkingDirectory = workingDirectory;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(scriptPath);
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        _ = process.Start();
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ScriptResult(process.ExitCode, output);
    }

    private static ScriptResult RunDotNet(string workingDirectory, params string[] arguments)
    {
        using System.Diagnostics.Process process = new();
        process.StartInfo.FileName = "dotnet";
        process.StartInfo.WorkingDirectory = workingDirectory;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        _ = process.Start();
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ScriptResult(process.ExitCode, output);
    }

    private static void CreatePackageSource(string root, DateTimeOffset timestamp)
    {
        _ = Directory.CreateDirectory(Path.Combine(root, "content", "nested"));
        _ = Directory.CreateDirectory(Path.Combine(root, "runtimes", "linux-x64", "native"));
        _ = WriteTextEvidence(Path.Combine(root, "content", "nested", "b.txt"), "same content b");
        _ = WriteTextEvidence(Path.Combine(root, "content", "a.txt"), "same content a");
        _ = WriteTextEvidence(Path.Combine(root, "PixelEngine.Demo"), "executable");
        _ = WriteTextEvidence(Path.Combine(root, "run.sh"), "#!/usr/bin/env bash\necho run\n");
        _ = WriteTextEvidence(Path.Combine(root, "runtimes", "linux-x64", "native", "libbox2d.so"), "native");

        foreach (string path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            if (Directory.Exists(path))
            {
                Directory.SetLastWriteTimeUtc(path, timestamp.UtcDateTime);
            }
            else
            {
                File.SetLastWriteTimeUtc(path, timestamp.UtcDateTime);
            }
        }

        Directory.SetLastWriteTimeUtc(root, timestamp.UtcDateTime);
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

    private readonly record struct ScriptResult(int ExitCode, string Output);
}
