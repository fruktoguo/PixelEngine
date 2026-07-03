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
        string[] requiredMachineFields =
        [
            "targetCpuName",
            "dotnetVersion",
            "vector512HardwareAccelerated",
            "avx512Enabled",
            "noNetDownclockLoss",
            "elevatedEtwKernelSession",
            "cacheMissesPresent",
            "branchMispredictionsPresent",
            "targetHardware",
            "sampleSeconds",
            "caP99Ms",
            "renderP99Ms",
            "physicsP99Ms",
            "logicAudioP99Ms",
            "representativeHardware",
            "activeCellsPerFrame",
            "caFrameMs",
            "measuredIterations",
        ];
        foreach (string field in requiredMachineFields)
        {
            Assert.Contains(field, script, StringComparison.Ordinal);
            Assert.Contains(field, report, StringComparison.Ordinal);
            Assert.Contains(field, plan, StringComparison.Ordinal);
        }

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
        Assert.Contains("conclusion 必须为 no_leaks", script, StringComparison.Ordinal);
        Assert.Contains("glObjectsLiveAfterShutdown", script, StringComparison.Ordinal);
        Assert.Contains("openAlObjectsLiveAfterShutdown", script, StringComparison.Ordinal);
        Assert.Contains("box2DBodiesLiveAfterShutdown", script, StringComparison.Ordinal);
        Assert.Contains("alcLoadContextsAliveAfterUnload", script, StringComparison.Ordinal);
        Assert.Contains("必须为 0", script, StringComparison.Ordinal);
        Assert.Contains("scope 缺少 detector", script, StringComparison.Ordinal);
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
    /// 验证 native leak 预检会拒绝未知 detector scope，避免额外报告冒充必需四类审计。
    /// </summary>
    [Fact]
    public void NativeLeakPreflightRejectsUnknownScopeWithReport()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-native-leak-unknown-scope-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateNativeLeakEvidenceManifest(temp);
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonObject scopes = rootNode["scopes"]!.AsObject();
            string report = WriteTextEvidence(Path.Combine(temp, "unknown", "d3d.md"), "unexpected detector report");
            scopes["d3d"] = new JsonObject
            {
                ["detector"] = "external-detector",
                ["report"] = report,
                ["sha256"] = GetSha256(report),
            };
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "unknown-out");
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "native-leak-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            Assert.Equal(5, result.ExitCode);
            string preflightReport = File.ReadAllText(Path.Combine(artifacts, "native-leak-preflight.md"));
            Assert.Contains("blocked_invalid_native_leak_evidence", result.Output + preflightReport, StringComparison.Ordinal);
            Assert.Contains("evidence manifest 包含未知 scope：d3d", preflightReport, StringComparison.Ordinal);
            Assert.DoesNotContain("status | detector_evidence_attached_pending_review", preflightReport, StringComparison.Ordinal);
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
    /// 验证 native leak 预检会拒绝缺少 no_leaks 结论的 detector 报告。
    /// </summary>
    [Fact]
    public void NativeLeakPreflightRejectsDetectorReportWithoutNoLeaksConclusion()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-native-leak-bad-conclusion-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateNativeLeakEvidenceManifest(temp);
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonObject gl = rootNode["scopes"]!["gl"]!.AsObject();
            string report = (string)gl["report"]!;
            _ = WriteMarkdownEvidence(
                report,
                new Dictionary<string, string>
                {
                    ["scope"] = "gl",
                    ["detector"] = "external-detector",
                    ["conclusion"] = "leaks_detected",
                });
            gl["sha256"] = GetSha256(report);
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "bad-conclusion-out");
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "native-leak-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            Assert.Equal(5, result.ExitCode);
            string preflightReport = File.ReadAllText(Path.Combine(artifacts, "native-leak-preflight.md"));
            Assert.Contains("blocked_invalid_native_leak_evidence", result.Output + preflightReport, StringComparison.Ordinal);
            Assert.Contains("evidence report gl conclusion 必须为 no_leaks，实际为 leaks_detected", preflightReport, StringComparison.Ordinal);
            Assert.DoesNotContain("status | detector_evidence_attached_pending_review", preflightReport, StringComparison.Ordinal);
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
    /// 验证 native leak 预检会拒绝缺少释放后 live-object 归零证据的 detector 报告。
    /// </summary>
    [Fact]
    public void NativeLeakPreflightRejectsDetectorReportWithoutZeroLiveCounts()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-native-leak-live-count-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateNativeLeakEvidenceManifest(temp);
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonObject gl = rootNode["scopes"]!["gl"]!.AsObject();
            string report = (string)gl["report"]!;
            _ = WriteMarkdownEvidence(
                report,
                new Dictionary<string, string>
                {
                    ["scope"] = "gl",
                    ["detector"] = "external-detector",
                    ["conclusion"] = "no_leaks",
                    ["glObjectsLiveAfterShutdown"] = "1",
                });
            gl["sha256"] = GetSha256(report);
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "live-count-out");
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "native-leak-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            Assert.Equal(5, result.ExitCode);
            string preflightReport = File.ReadAllText(Path.Combine(artifacts, "native-leak-preflight.md"));
            Assert.Contains("blocked_invalid_native_leak_evidence", result.Output + preflightReport, StringComparison.Ordinal);
            Assert.Contains("evidence report gl glObjectsLiveAfterShutdown 必须为 0，实际为 1", preflightReport, StringComparison.Ordinal);
            Assert.DoesNotContain("status | detector_evidence_attached_pending_review", preflightReport, StringComparison.Ordinal);
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
        Assert.Contains("Convert-ParticleProbeSummaryToMetrics", script, StringComparison.Ordinal);
        Assert.Contains("Assert-LocalProbeMetrics", script, StringComparison.Ordinal);
        Assert.Contains("Write-LocalComparison", script, StringComparison.Ordinal);
        Assert.Contains("DotNetPath", script, StringComparison.Ordinal);
        Assert.Contains("local-comparison.md", script, StringComparison.Ordinal);
        Assert.Contains("local-comparison.json", script, StringComparison.Ordinal);
        Assert.Contains("local_only: true", script, StringComparison.Ordinal);
        Assert.Contains("target_gpu_evidence: false", script, StringComparison.Ordinal);
        Assert.Contains("blocked_invalid_local_probe", script, StringComparison.Ordinal);
        Assert.Contains("probe 退出码为", script, StringComparison.Ordinal);
        Assert.Contains("gpu_available 必须为 True", script, StringComparison.Ordinal);
        Assert.Contains("实际 mode=", script, StringComparison.Ordinal);
        Assert.Contains("targetHardwareReport", script, StringComparison.Ordinal);
        Assert.Contains("cpuProbeReport", script, StringComparison.Ordinal);
        Assert.Contains("gpuProbeReport", script, StringComparison.Ordinal);
        Assert.Contains("comparisonReport", script, StringComparison.Ordinal);
        Assert.Contains("Assert-TargetHardwareReport", script, StringComparison.Ordinal);
        Assert.Contains("Assert-TargetProbeReport", script, StringComparison.Ordinal);
        Assert.Contains("Assert-TargetProbePair", script, StringComparison.Ordinal);
        Assert.Contains("targetGpuName", script, StringComparison.Ordinal);
        Assert.Contains("targetGpuDriver", script, StringComparison.Ordinal);
        Assert.Contains("gpuBackend", script, StringComparison.Ordinal);
        Assert.Contains("particleCount", script, StringComparison.Ordinal);
        Assert.Contains("measured_frames 必须至少为 300", script, StringComparison.Ordinal);
        Assert.Contains("sampleSeconds 必须至少为 10 秒", script, StringComparison.Ordinal);
        Assert.Contains("speedupRatio 必须大于 1", script, StringComparison.Ordinal);
        Assert.Contains("gpuFasterThanCpu", script, StringComparison.Ordinal);
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
        Assert.Contains("gpuFasterThanCpu", report, StringComparison.Ordinal);
        Assert.Contains("targetGpuName", report, StringComparison.Ordinal);
        Assert.Contains("measured_frames", report, StringComparison.Ordinal);
        Assert.Contains("sampleSeconds", report, StringComparison.Ordinal);
        Assert.Contains("local-comparison.md", report, StringComparison.Ordinal);
        Assert.Contains("target_gpu_evidence: false", report, StringComparison.Ordinal);
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
            string goodManifest = CreateGpuParticleEvidenceManifest(temp, suffix: "good");
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
    /// 验证本机 GPU 粒子 probe 子进程失败会被标为 invalid local probe，不能误报 local_probe_only。
    /// </summary>
    [Fact]
    public void GpuParticleBenchmarkPreflightRejectsFailedLocalProbe()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-gpu-particle-local-fail-" + Guid.NewGuid().ToString("N"));

        try
        {
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "gpu-particle-benchmark-preflight.ps1"),
                "-RunProbe",
                "-Project",
                Path.Combine(temp, "missing.csproj"),
                "-Artifacts",
                temp);

            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(temp, "gpu-particle-benchmark-preflight.md"));
            Assert.Contains("status: blocked_invalid_local_probe", report, StringComparison.Ordinal);
            Assert.Contains("本机 probe 失败", report, StringComparison.Ordinal);
            Assert.Contains("probe 退出码", report, StringComparison.Ordinal);
            Assert.DoesNotContain("status: local_probe_only", report, StringComparison.Ordinal);
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
    /// 验证 GPU 请求若实际回退成 CPU summary，即使子进程 0 退出也不能报告 local_probe_only。
    /// </summary>
    [Fact]
    public void GpuParticleBenchmarkPreflightRejectsGpuProbeThatFallsBackToCpuSummary()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-gpu-particle-local-mode-" + Guid.NewGuid().ToString("N"));

        try
        {
            _ = Directory.CreateDirectory(temp);
            string fakeDotNet = Path.Combine(temp, "fake-dotnet.cmd");
            File.WriteAllText(
                fakeDotNet,
                """
                @echo off
                echo particle_frame_probe mode=cpu, gpu_available=False, requested_count=100000, active_count=100000, warmup_frames=1, measured_frames=3, wall_avg_ms=1.000, wall_p50_ms=1.000, wall_p95_ms=1.000, wall_max_ms=1.000, particle_stamp_avg_ms=0.800, particle_stamp_p50_ms=0.800, particle_stamp_p95_ms=0.800, particle_stamp_max_ms=0.800, gpu_particle_avg_ms=0.000, gpu_particle_p50_ms=0.000, gpu_particle_p95_ms=0.000, gpu_particle_max_ms=0.000, gpu_upload_avg_ms=0.000, gpu_upload_p50_ms=0.000, gpu_upload_p95_ms=0.000, gpu_upload_max_ms=0.000, lighting_avg_ms=0.000, lighting_p50_ms=0.000, lighting_p95_ms=0.000, lighting_max_ms=0.000, bloom_avg_ms=0.000, bloom_p50_ms=0.000, bloom_p95_ms=0.000, bloom_max_ms=0.000, present_avg_ms=0.000, present_p50_ms=0.000, present_p95_ms=0.000, present_max_ms=0.000
                exit /b 0
                """);

            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "gpu-particle-benchmark-preflight.ps1"),
                "-RunProbe",
                "-DotNetPath",
                fakeDotNet,
                "-WindowTicks",
                "4",
                "-WarmupFrames",
                "1",
                "-ParticleCount",
                "100000",
                "-Artifacts",
                temp);

            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(temp, "gpu-particle-benchmark-preflight.md"));
            Assert.Contains("status: blocked_invalid_local_probe", report, StringComparison.Ordinal);
            Assert.Contains("gpu probe 实际 mode=cpu", report, StringComparison.Ordinal);
            Assert.DoesNotContain("status: local_probe_only", report, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(temp, "local-comparison.md")));
            Assert.False(File.Exists(Path.Combine(temp, "local-comparison.json")));
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
    /// 验证 GPU 粒子目标硬件预检要求 comparisonReport 明确声明 GPU 总帧时间优于 CPU stamp。
    /// </summary>
    [Fact]
    public void GpuParticleBenchmarkPreflightRejectsComparisonReportWithoutGpuFasterThanCpu()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-gpu-particle-comparison-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateGpuParticleEvidenceManifest(temp, suffix: "bad-comparison");
            SetFlatEvidenceFileContent(manifest, "comparisonReport", "gpuFasterThanCpu: false");

            string artifacts = Path.Combine(temp, "bad-comparison-out");
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
            Assert.Contains("comparisonReport", report, StringComparison.Ordinal);
            Assert.Contains("gpuFasterThanCpu", report, StringComparison.Ordinal);
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
    /// 验证 comparisonReport 不能用不同于 probe summary 的 wall_avg_ms 伪造 GPU 更快结论。
    /// </summary>
    [Fact]
    public void GpuParticleBenchmarkPreflightRejectsComparisonWallValuesThatDoNotMatchProbeSummaries()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-gpu-particle-wall-mismatch-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateGpuParticleEvidenceManifest(
                temp,
                suffix: "bad-wall",
                cpuWallAvgMs: 1.0,
                gpuWallAvgMs: 10.0,
                comparisonCpuWallAvgMs: 10.0,
                comparisonGpuWallAvgMs: 1.0,
                comparisonSpeedupRatio: 10.0);

            string artifacts = Path.Combine(temp, "bad-wall-out");
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
            Assert.Contains("comparisonReport cpuWallAvgMs 必须与 probe summary 一致", report, StringComparison.Ordinal);
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
    /// 验证 comparisonReport 的 speedupRatio 必须由 cpuWallAvgMs/gpuWallAvgMs 重算得到。
    /// </summary>
    [Fact]
    public void GpuParticleBenchmarkPreflightRejectsComparisonSpeedupRatioThatDoesNotMatchWallValues()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-gpu-particle-ratio-mismatch-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateGpuParticleEvidenceManifest(
                temp,
                suffix: "bad-ratio",
                cpuWallAvgMs: 6.0,
                gpuWallAvgMs: 3.0,
                comparisonSpeedupRatio: 9.0);

            string artifacts = Path.Combine(temp, "bad-ratio-out");
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
            Assert.Contains("comparisonReport speedupRatio", report, StringComparison.Ordinal);
            Assert.Contains("重算值", report, StringComparison.Ordinal);
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
    /// 验证目标 GPU comparisonReport 必须声明足够长的采样时长。
    /// </summary>
    [Fact]
    public void GpuParticleBenchmarkPreflightRejectsComparisonWithTooShortSampleSeconds()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-gpu-particle-short-sample-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateGpuParticleEvidenceManifest(temp, suffix: "bad-sample", sampleSeconds: 5.0);

            string artifacts = Path.Combine(temp, "bad-sample-out");
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
            Assert.Contains("comparisonReport sampleSeconds 必须至少为 10 秒", report, StringComparison.Ordinal);
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
    /// 验证 GPU 粒子目标硬件预检要求硬件报告包含可审查的机器字段。
    /// </summary>
    [Fact]
    public void GpuParticleBenchmarkPreflightRejectsTargetHardwareReportWithoutMachineFields()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-gpu-particle-hardware-fields-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateGpuParticleEvidenceManifest(temp, suffix: "bad-hardware");
            SetFlatEvidenceFileContent(manifest, "targetHardwareReport", "targetGpuName: Test GPU");

            string artifacts = Path.Combine(temp, "bad-hardware-out");
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
            Assert.Contains("targetHardwareReport 缺少机器可读字段 targetGpuDriver", report, StringComparison.Ordinal);
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
    /// 验证目标硬件报告必须声明 particleCount，不能让硬件描述与 CPU/GPU probe 粒子数脱钩。
    /// </summary>
    [Fact]
    public void GpuParticleBenchmarkPreflightRejectsTargetHardwareReportWithoutParticleCount()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-gpu-particle-hardware-count-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateGpuParticleEvidenceManifest(temp, suffix: "bad-hardware-count");
            SetFlatEvidenceFileContent(
                manifest,
                "targetHardwareReport",
                """
                targetGpuName: Test GPU 4090
                targetGpuDriver: 999.1
                gpuBackend: OpenGL
                operatingSystem: TestOS
                cpuName: Test CPU
                dotnetVersion: 10.0.8
                gitCommit: abcdef123456
                """);

            string artifacts = Path.Combine(temp, "bad-hardware-count-out");
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
            Assert.Contains("targetHardwareReport 缺少机器可读字段 particleCount", report, StringComparison.Ordinal);
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
    /// 验证 GPU 粒子目标硬件预检要求目标 probe 至少包含长样本帧数。
    /// </summary>
    [Fact]
    public void GpuParticleBenchmarkPreflightRejectsTargetProbeWithTooFewMeasuredFrames()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-gpu-particle-probe-frames-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateGpuParticleEvidenceManifest(temp, suffix: "bad-frames", measuredFrames: 120);

            string artifacts = Path.Combine(temp, "bad-frames-out");
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
            Assert.Contains("cpuProbeReport measured_frames 必须至少为 300", report, StringComparison.Ordinal);
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
    /// 验证 GPU 粒子目标硬件预检会实际拒绝 sha256 不匹配的 evidence，而不是只做脚本文本约束。
    /// </summary>
    [Fact]
    public void GpuParticleBenchmarkPreflightRejectsMismatchedEvidenceHash()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-gpu-particle-bad-hash-" + Guid.NewGuid().ToString("N"));

        try
        {
            string badManifest = CreateGpuParticleEvidenceManifest(temp, suffix: "bad-hash");
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
            string duplicateManifest = CreateGpuParticleEvidenceManifest(temp, suffix: "duplicate-scope");
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
            string manifest = CreateGpuParticleEvidenceManifest(temp, suffix: "schema");
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
        Assert.Contains("--capture-frame", script, StringComparison.Ordinal);
        Assert.Contains("Get-BmpFrameEvidence", script, StringComparison.Ordinal);
        Assert.Contains("capture.bmp", script, StringComparison.Ordinal);
        Assert.Contains("capture_sha256", script, StringComparison.Ordinal);
        Assert.Contains("capture_dimensions", script, StringComparison.Ordinal);
        Assert.Contains("captureBitsPerPixel", script, StringComparison.Ordinal);
        Assert.Contains("uniqueVisiblePixels", script, StringComparison.Ordinal);
        Assert.Contains("不能作为窗口画面证据", script, StringComparison.Ordinal);
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
        Assert.Contains("playable_shots=", script, StringComparison.Ordinal);
        Assert.Contains("720x480", script, StringComparison.Ordinal);
        Assert.Contains("particles=", script, StringComparison.Ordinal);
        Assert.Contains("fps=", script, StringComparison.Ordinal);
        Assert.Contains("frame_ms=", script, StringComparison.Ordinal);
        Assert.Contains("frame_p99_ms=", script, StringComparison.Ordinal);
        Assert.Contains("frame_low1_fps=", script, StringComparison.Ordinal);
        Assert.Contains("frame_jitter_ms=", script, StringComparison.Ordinal);
        Assert.Contains("frame_samples=", script, StringComparison.Ordinal);
        Assert.Contains("sim_hz=", script, StringComparison.Ordinal);
        Assert.Contains("brush_material=", script, StringComparison.Ordinal);
        Assert.Contains("brush_radius=", script, StringComparison.Ordinal);
        Assert.Contains("painted_material=", script, StringComparison.Ordinal);
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
        Assert.Contains("minDurationSeconds", script, StringComparison.Ordinal);
        string[] manualChecklistKeys =
        [
            "runJumpWallKick",
            "sandPileTraversal",
            "rigidOwnedStanding",
            "realMouseWheelDigits",
            "sandWaterOilGasObserved",
            "reactionTemperatureObserved",
            "pushAndImpact",
            "digBridgeCollapse",
            "continuedDamage",
            "particlesVisible",
            "bloomFogLighting",
            "noParticleLeak",
            "materialImpacts",
            "ambientAndReaction",
            "spatialMix",
            "routeCompleted",
            "materialsReactionsBodiesShown",
            "audioLightingHudShown",
            "hudReadable",
            "menuButtonsClicked",
            "editorDockspaceOpened",
            "behaviourSourceEdited",
            "alcReloadObserved",
            "statePreserved",
        ];
        Assert.Contains("checklist", script, StringComparison.Ordinal);
        Assert.Contains("criteria", script, StringComparison.Ordinal);
        foreach (string key in manualChecklistKeys)
        {
            Assert.Contains(key, script, StringComparison.Ordinal);
            Assert.Contains(key, report, StringComparison.Ordinal);
        }

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
        Assert.Contains("--capture-frame", report, StringComparison.Ordinal);
        Assert.Contains("capture.bmp", report, StringComparison.Ordinal);
        Assert.Contains("capture_unique_visible_pixels", report, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_manual_scope_evidence", report, StringComparison.Ordinal);
        Assert.Contains("blocked_invalid_manual_evidence", report, StringComparison.Ordinal);
        Assert.Contains("manual_evidence_attached_pending_review", report, StringComparison.Ordinal);
        Assert.Contains("playable_shots=", report, StringComparison.Ordinal);
        Assert.Contains("未知 scope", report, StringComparison.Ordinal);

        Assert.Contains("tools/demo-manual-acceptance-preflight.ps1", plan, StringComparison.Ordinal);
        Assert.Contains("capture.bmp", plan, StringComparison.Ordinal);
        Assert.Contains("checklist", plan, StringComparison.Ordinal);
        Assert.Contains("criteria", plan, StringComparison.Ordinal);
        Assert.Contains("runJumpWallKick", plan, StringComparison.Ordinal);
        Assert.Contains("routeCompleted", plan, StringComparison.Ordinal);
        Assert.Contains("statePreserved", plan, StringComparison.Ordinal);
        Assert.Contains("manual_evidence_attached_pending_review", plan, StringComparison.Ordinal);

        Assert.Contains("tools/demo-manual-acceptance-preflight.ps1", hostingPlan, StringComparison.Ordinal);
        Assert.Contains("capture.bmp", hostingPlan, StringComparison.Ordinal);
        Assert.Contains("hudMenuEditorVideo", hostingPlan, StringComparison.Ordinal);
        Assert.Contains("criteria", hostingPlan, StringComparison.Ordinal);
        Assert.Contains("hudReadable", hostingPlan, StringComparison.Ordinal);
        Assert.Contains("menuButtonsClicked", hostingPlan, StringComparison.Ordinal);
        Assert.Contains("editorDockspaceOpened", hostingPlan, StringComparison.Ordinal);
        Assert.Contains("manual_evidence_attached_pending_review", hostingPlan, StringComparison.Ordinal);
        Assert.Contains("- [x] 过载降级按五级顺序触发", hostingPlan, StringComparison.Ordinal);
        Assert.Contains("- [!] Editor 真实窗口观测/覆盖仍缺人工复核证据", hostingPlan, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 scripted probe 截图证据会拒绝纯黑 BMP，避免黑屏截图进入机器 probe 报告。
    /// </summary>
    [Fact]
    public void DemoManualAcceptancePreflightRejectsBlankScriptedProbeCapture()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-demo-manual-blank-bmp-" + Guid.NewGuid().ToString("N"));

        try
        {
            _ = Directory.CreateDirectory(temp);
            string bmp = Path.Combine(temp, "blank.bmp");
            WriteBmp24(bmp, width: 8, height: 8, static (_, _) => (0, 0, 0));

            string source = ReadRepositoryFile("tools", "demo-manual-acceptance-preflight.ps1");
            int start = source.IndexOf("function Resolve-RepositoryRoot", StringComparison.Ordinal);
            int end = source.IndexOf("function Get-ManualScopes", StringComparison.Ordinal);
            Assert.True(start >= 0);
            Assert.True(end > start);
            string harness = string.Concat(
                "param([string]$Root,[string]$Bmp)\n",
                source[start..end],
                "\ntry {\n",
                "  $capture = Get-BmpFrameEvidence -Root $Root -Path $Bmp\n",
                "  $capture | ConvertTo-Json -Compress\n",
                "  exit 0\n",
                "} catch {\n",
                "  Write-Output $_.Exception.Message\n",
                "  exit 5\n",
                "}\n");
            string harnessPath = Path.Combine(temp, "bmp-harness.ps1");
            File.WriteAllText(harnessPath, harness);

            ScriptResult result = RunPowerShellScript(root, harnessPath, root, bmp);

            Assert.Equal(5, result.ExitCode);
            Assert.Contains("只有黑/白空白像素", result.Output, StringComparison.Ordinal);
            Assert.Contains("不能作为窗口画面证据", result.Output, StringComparison.Ordinal);
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
            SetFlatEvidenceProperty(badDurationManifest, "fullRoutePlaythroughVideo", "durationSeconds", 1.0);

            string badNotesManifest = CreateFlatEvidenceManifest(temp, manualScopes, suffix: "bad-notes", includeDemoManualMetadata: true);
            SetFlatEvidenceProperty(badNotesManifest, "controlFeelReport", "notes", "too short");
            string badChecklistManifest = CreateFlatEvidenceManifest(temp, manualScopes, suffix: "bad-checklist", includeDemoManualMetadata: true);
            SetFlatEvidenceProperty(
                badChecklistManifest,
                "controlFeelReport",
                "checklist",
                new JsonObject
                {
                    ["runJumpWallKick"] = true,
                    ["sandPileTraversal"] = false,
                    ["rigidOwnedStanding"] = true,
                });
            string badCriteriaManifest = CreateFlatEvidenceManifest(temp, manualScopes, suffix: "bad-criteria", includeDemoManualMetadata: true);
            SetFlatEvidenceProperty(
                badCriteriaManifest,
                "hudMenuEditorVideo",
                "criteria",
                new JsonObject
                {
                    ["hudReadable"] = "HUD readable in screenshot",
                    ["menuButtonsClicked"] = "too short",
                    ["editorDockspaceOpened"] = "Editor dockspace is opened and visible during the manual video.",
                });

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
            Assert.Contains("fullRoutePlaythroughVideo durationSeconds 必须至少为 30 秒", badDurationReport, StringComparison.Ordinal);
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

            string badChecklistArtifacts = Path.Combine(temp, "bad-checklist-out");
            ScriptResult badChecklist = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "demo-manual-acceptance-preflight.ps1"),
                "-EvidenceManifestPath",
                badChecklistManifest,
                "-Artifacts",
                badChecklistArtifacts);
            Assert.Equal(5, badChecklist.ExitCode);
            string badChecklistReport = File.ReadAllText(Path.Combine(badChecklistArtifacts, "demo-manual-acceptance-preflight.md"));
            Assert.Contains("status: blocked_invalid_manual_evidence", badChecklist.Output + badChecklistReport, StringComparison.Ordinal);
            Assert.Contains("controlFeelReport checklist.sandPileTraversal 必须为 true", badChecklistReport, StringComparison.Ordinal);
            Assert.DoesNotContain("status: manual_evidence_attached_pending_review", badChecklistReport, StringComparison.Ordinal);

            string badCriteriaArtifacts = Path.Combine(temp, "bad-criteria-out");
            ScriptResult badCriteria = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "demo-manual-acceptance-preflight.ps1"),
                "-EvidenceManifestPath",
                badCriteriaManifest,
                "-Artifacts",
                badCriteriaArtifacts);
            Assert.Equal(5, badCriteria.ExitCode);
            string badCriteriaReport = File.ReadAllText(Path.Combine(badCriteriaArtifacts, "demo-manual-acceptance-preflight.md"));
            Assert.Contains("status: blocked_invalid_manual_evidence", badCriteria.Output + badCriteriaReport, StringComparison.Ordinal);
            Assert.Contains("hudMenuEditorVideo criteria.menuButtonsClicked 至少需要 20 个字符", badCriteriaReport, StringComparison.Ordinal);
            Assert.DoesNotContain("status: manual_evidence_attached_pending_review", badCriteriaReport, StringComparison.Ordinal);
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
        Assert.Contains("Get-ExpectedRunIdentity", script, StringComparison.Ordinal);
        Assert.Contains("Add-RunIdentityCheck", script, StringComparison.Ordinal);
        Assert.Contains("必须与 workflow_run 一致", script, StringComparison.Ordinal);
        Assert.Contains("benchmarkGuard", script, StringComparison.Ordinal);
        Assert.Contains("buildTest", script, StringComparison.Ordinal);
        Assert.Contains("verifyPublish", script, StringComparison.Ordinal);
        Assert.Contains("缺少 testsRan 字段", script, StringComparison.Ordinal);
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
        Assert.Contains("同一个 GitHub Actions run", report, StringComparison.Ordinal);
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
    /// 验证 CI evidence 预检要求每个 buildTest RID 显式声明 testsRan 字段。
    /// </summary>
    [Fact]
    public void CiMatrixEvidencePreflightRequiresExplicitTestsRanField()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-ci-tests-ran-required-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateCiEvidenceManifest(temp, benchmarkConclusion: "success");
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonObject buildTest = rootNode["buildTest"]!.AsObject();
            _ = buildTest["win-arm64"]!.AsObject().Remove("testsRan");
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "missing-tests-ran-out");
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
            Assert.Contains("buildTest.win-arm64 缺少 testsRan 字段", report, StringComparison.Ordinal);
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
    /// 验证 CI evidence 预检要求所有报告来自同一个 workflow run / commit。
    /// </summary>
    [Fact]
    public void CiMatrixEvidencePreflightRejectsMismatchedRunIdentity()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-ci-run-identity-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateCiEvidenceManifest(temp, benchmarkConclusion: "success");
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonObject buildTest = rootNode["buildTest"]!.AsObject();
            JsonObject linux = buildTest["linux-x64"]!.AsObject();
            string reportPath = (string)linux["report"]!;
            string text = File.ReadAllText(reportPath).Replace("| sha | abc |", "| sha | different-commit |", StringComparison.Ordinal);
            File.WriteAllText(reportPath, text);
            linux["sha256"] = GetSha256(reportPath);
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "run-identity-out");
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
            Assert.Contains("build_test/linux-x64/report 报告 sha 必须与 workflow_run 一致", report, StringComparison.Ordinal);
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
    /// 验证 AVX-512 evidence 必须声明无净降频损失，不能只附一个空报告。
    /// </summary>
    [Fact]
    public void PerformanceTargetEvidencePreflightRejectsAvx512ReportWithoutNoNetDownclockLoss()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-performance-target-avx512-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreatePerformanceTargetEvidenceManifest(temp);
            SetFlatEvidenceFileContent(
                manifest,
                "avx512_downclock_net_loss",
                """
                targetCpuName: Test AVX512 CPU
                dotnetVersion: 10.0.8
                benchmarkDotNet: true
                vector512HardwareAccelerated: true
                avx512Enabled: true
                noNetDownclockLoss: false
                """);

            string artifacts = Path.Combine(temp, "avx512-out");
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
            Assert.Contains("avx512_downclock_net_loss noNetDownclockLoss 必须为 true", report, StringComparison.Ordinal);
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
    /// 验证硬件计数器 evidence 必须同时包含 Cache Misses 与 Branch Mispredictions。
    /// </summary>
    [Fact]
    public void PerformanceTargetEvidencePreflightRejectsHardwareCounterReportWithoutBranchMispredictions()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-performance-target-counters-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreatePerformanceTargetEvidenceManifest(temp);
            SetFlatEvidenceFileContent(
                manifest,
                "hardware_counters_cache_branch",
                """
                benchmarkDotNet: true
                elevatedEtwKernelSession: true
                cacheMissesPresent: true
                branchMispredictionsPresent: false

                | Method | Cache Misses |
                |---|---:|
                | Reaction | 100 |
                """);

            string artifacts = Path.Combine(temp, "counters-out");
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
            Assert.Contains("hardware_counters_cache_branch branchMispredictionsPresent 必须为 true", report, StringComparison.Ordinal);
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
    /// 验证目标硬件帧预算 evidence 必须满足 plan/16 的 p99 阈值。
    /// </summary>
    [Fact]
    public void PerformanceTargetEvidencePreflightRejectsFrameBudgetAbovePlanThreshold()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-performance-target-frame-budget-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreatePerformanceTargetEvidenceManifest(temp);
            SetFlatEvidenceFileContent(
                manifest,
                "frame_budget_target_hardware",
                """
                targetHardware: representative-target
                sampleSeconds: 120
                caP99Ms: 8.5
                renderP99Ms: 3.5
                physicsP99Ms: 3.5
                logicAudioP99Ms: 0.8
                """);

            string artifacts = Path.Combine(temp, "frame-budget-out");
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
            Assert.Contains("frame_budget_target_hardware caP99Ms 必须 <= 8ms", report, StringComparison.Ordinal);
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
    /// 验证 cells/frame evidence 必须与对应 RID 匹配并达到 2M active cells / 8ms 目标。
    /// </summary>
    [Fact]
    public void PerformanceTargetEvidencePreflightRejectsCellsFrameReportBelowTarget()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-performance-target-cells-target-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreatePerformanceTargetEvidenceManifest(temp);
            SetFlatEvidenceFileContent(
                manifest,
                "cells_frame/linux-x64",
                """
                rid: linux-x64
                benchmarkDotNet: true
                representativeHardware: true
                activeCellsPerFrame: 1500000
                caFrameMs: 7.2
                measuredIterations: 5
                """);

            string artifacts = Path.Combine(temp, "cells-target-out");
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
            Assert.Contains("cells_frame/linux-x64 activeCellsPerFrame 必须至少为 2000000", report, StringComparison.Ordinal);
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
    /// 验证 cells/frame evidence 必须来自代表性硬件，不能用非代表环境冒充目标硬件实测。
    /// </summary>
    [Fact]
    public void PerformanceTargetEvidencePreflightRejectsCellsFrameWithoutRepresentativeHardware()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-performance-target-cells-representative-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreatePerformanceTargetEvidenceManifest(temp);
            SetFlatEvidenceFileContent(
                manifest,
                "cells_frame/win-arm64",
                """
                rid: win-arm64
                benchmarkDotNet: true
                representativeHardware: false
                activeCellsPerFrame: 2500000
                caFrameMs: 7.2
                measuredIterations: 5
                """);

            string artifacts = Path.Combine(temp, "cells-representative-out");
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
            Assert.Contains("cells_frame/win-arm64 representativeHardware 必须为 true", report, StringComparison.Ordinal);
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
    /// 验证 cells/frame evidence 必须有足够 BenchmarkDotNet 迭代次数。
    /// </summary>
    [Fact]
    public void PerformanceTargetEvidencePreflightRejectsCellsFrameWithTooFewMeasuredIterations()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-performance-target-cells-iterations-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreatePerformanceTargetEvidenceManifest(temp);
            SetFlatEvidenceFileContent(
                manifest,
                "cells_frame/osx-arm64",
                """
                rid: osx-arm64
                benchmarkDotNet: true
                representativeHardware: true
                activeCellsPerFrame: 2500000
                caFrameMs: 7.2
                measuredIterations: 2
                """);

            string artifacts = Path.Combine(temp, "cells-iterations-out");
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
            Assert.Contains("cells_frame/osx-arm64 measuredIterations 必须至少为 3", report, StringComparison.Ordinal);
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
        Assert.Contains("Assert-FriendlyPackageLayout", auditPs1, StringComparison.Ordinal);
        Assert.Contains("Assert-FriendlyExpandedPackageLayout", auditPs1, StringComparison.Ordinal);
        Assert.Contains("Assert-NoDuplicateContentUnderApp", auditPs1, StringComparison.Ordinal);
        Assert.Contains("Assert-NoDuplicateWindowsLauncherUnderApp", auditPs1, StringComparison.Ordinal);
        Assert.Contains("不应在 app/ 下重复打包 content", auditPs1, StringComparison.Ordinal);
        Assert.Contains("不应在 app/ 下重复保留原始启动 exe", auditPs1, StringComparison.Ordinal);
        Assert.Contains("package 缺少同名展开目录", auditPs1, StringComparison.Ordinal);
        Assert.Contains("展开 package 目录缺少对应归档", auditPs1, StringComparison.Ordinal);
        Assert.Contains("展开 package 根目录不应包含运行时依赖，请放入 app/", auditPs1, StringComparison.Ordinal);
        Assert.Contains("package 根目录不应包含运行时依赖，请放入 app/", auditPs1, StringComparison.Ordinal);
        Assert.Contains("package 根目录只允许启动入口/README/SHA256SUMS/许可文件与 app/content 目录", auditPs1, StringComparison.Ordinal);
        Assert.Contains("Test-DisallowedPlayerPackageFile", auditPs1, StringComparison.Ordinal);
        Assert.Contains("package 不应包含玩家无关的调试、文档或本地化卫星资源文件", auditPs1, StringComparison.Ordinal);
        Assert.Contains("展开 package 不应包含玩家无关的调试、文档或本地化卫星资源文件", auditPs1, StringComparison.Ordinal);
        Assert.Contains("package 内 SHA256SUMS 未覆盖文件", auditPs1, StringComparison.Ordinal);
        Assert.Contains("content/materials.json", auditPs1, StringComparison.Ordinal);
        Assert.Contains("assert_friendly_package_layout", auditSh, StringComparison.Ordinal);
        Assert.Contains("assert_friendly_expanded_package_layout", auditSh, StringComparison.Ordinal);
        Assert.Contains("assert_no_duplicate_content_under_app", auditSh, StringComparison.Ordinal);
        Assert.Contains("assert_no_duplicate_windows_launcher_under_app", auditSh, StringComparison.Ordinal);
        Assert.Contains("不应在 app/ 下重复打包 content", auditSh, StringComparison.Ordinal);
        Assert.Contains("不应在 app/ 下重复保留原始启动 exe", auditSh, StringComparison.Ordinal);
        Assert.Contains("package 缺少同名展开目录", auditSh, StringComparison.Ordinal);
        Assert.Contains("展开 package 目录缺少对应归档", auditSh, StringComparison.Ordinal);
        Assert.Contains("展开 package 根目录不应包含运行时依赖，请放入 app/", auditSh, StringComparison.Ordinal);
        Assert.Contains("package 根目录不应包含运行时依赖，请放入 app/", auditSh, StringComparison.Ordinal);
        Assert.Contains("package 根目录只允许启动入口/README/SHA256SUMS/许可文件与 app/content 目录", auditSh, StringComparison.Ordinal);
        Assert.Contains("is_disallowed_player_package_file", auditSh, StringComparison.Ordinal);
        Assert.Contains("package 不应包含玩家无关的调试、文档或本地化卫星资源文件", auditSh, StringComparison.Ordinal);
        Assert.Contains("展开 package 不应包含玩家无关的调试、文档或本地化卫星资源文件", auditSh, StringComparison.Ordinal);
        Assert.Contains("package 内 SHA256SUMS 未覆盖文件", auditSh, StringComparison.Ordinal);
        Assert.Contains("content/materials.json", auditSh, StringComparison.Ordinal);

        Assert.Contains("PixelEngine.Tools.DeterministicPackage", packagePs1, StringComparison.Ordinal);
        Assert.Contains("PixelEngine.Tools.DeterministicPackage", packageSh, StringComparison.Ordinal);
        Assert.Contains("$appDir = Join-Path $stagingDir 'app'", packagePs1, StringComparison.Ordinal);
        Assert.Contains("PixelEngine Demo.exe", packagePs1, StringComparison.Ordinal);
        Assert.Contains("Set-AppHostRelativeAssemblyPath", packagePs1, StringComparison.Ordinal);
        Assert.Contains("PixelEngine Demo.sh", packagePs1, StringComparison.Ordinal);
        Assert.Contains("README.txt", packagePs1, StringComparison.Ordinal);
        Assert.Contains("Remove-PlayerPackageNoise", packagePs1, StringComparison.Ordinal);
        Assert.Contains(".resources.dll", packagePs1, StringComparison.Ordinal);
        Assert.Contains("Join-Path $stagingDir 'SHA256SUMS'", packagePs1, StringComparison.Ordinal);
        Assert.Contains("$packageDir = Join-Path $OutputRoot $packageName", packagePs1, StringComparison.Ordinal);
        Assert.Contains("Move-Item -LiteralPath $stagingDir -Destination $packageDir -Force", packagePs1, StringComparison.Ordinal);
        Assert.Contains("$samePairPattern", packagePs1, StringComparison.Ordinal);
        Assert.Contains("app_dir=\"$staging_dir/app\"", packageSh, StringComparison.Ordinal);
        Assert.Contains("PixelEngine Demo.exe", packageSh, StringComparison.Ordinal);
        Assert.Contains("patch_apphost_relative_assembly", packageSh, StringComparison.Ordinal);
        Assert.Contains("PixelEngine Demo.sh", packageSh, StringComparison.Ordinal);
        Assert.Contains("README.txt", packageSh, StringComparison.Ordinal);
        Assert.Contains("remove_player_package_noise", packageSh, StringComparison.Ordinal);
        Assert.Contains("rm -rf \"$app_dir/content\" \"$content_dir\"", packageSh, StringComparison.Ordinal);
        Assert.Contains("*.resources.dll", packageSh, StringComparison.Ordinal);
        Assert.Contains("package_checksum_path=\"$staging_dir/SHA256SUMS\"", packageSh, StringComparison.Ordinal);
        Assert.Contains("package_dir=\"$output_root/$package_name\"", packageSh, StringComparison.Ordinal);
        Assert.Contains("mv \"$staging_dir\" \"$package_dir\"", packageSh, StringComparison.Ordinal);
        Assert.Contains("PixelEngine-Demo-*-$rid-$channel.zip", packageSh, StringComparison.Ordinal);
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
        Assert.Contains("Get-ExpectedRunIdentity", evidence, StringComparison.Ordinal);
        Assert.Contains("Add-RunIdentityCheck", evidence, StringComparison.Ordinal);
        Assert.Contains("必须与 workflow_run 一致", evidence, StringComparison.Ordinal);
        Assert.Contains("Get-ReleaseTagVersion", evidence, StringComparison.Ordinal);
        Assert.Contains("workflow_run ref 必须是 refs/tags/v<semver>", evidence, StringComparison.Ordinal);
        Assert.Contains("github_release_upload release_tag 必须为 true", evidence, StringComparison.Ordinal);
        Assert.Contains("Test-PackageVersionsMatchReleaseTag", evidence, StringComparison.Ordinal);
        Assert.Contains("packageReport", evidence, StringComparison.Ordinal);
        Assert.Contains("artifactAuditReport", evidence, StringComparison.Ordinal);
        Assert.Contains("artifact_audit", evidence, StringComparison.Ordinal);
        Assert.Contains("aot_dynamic_box2d_rejected", evidence, StringComparison.Ordinal);
        Assert.Contains("package_layout_checked", evidence, StringComparison.Ordinal);
        Assert.Contains("checksum_checked", evidence, StringComparison.Ordinal);
        Assert.Contains("deterministic_hash", evidence, StringComparison.Ordinal);
        Assert.Contains("Test-ReleaseChecksumRows", evidence, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS 缺少 package 条目", evidence, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS 包含无效行", evidence, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS $name hash 必须匹配 packageSha256", evidence, StringComparison.Ordinal);
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
        Assert.Contains("SHA256SUMS", releaseReport, StringComparison.Ordinal);
        Assert.Contains("同一个 GitHub Actions run", releaseReport, StringComparison.Ordinal);
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
        Assert.Contains("artifactAuditReport", release, StringComparison.Ordinal);
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
        Assert.Contains("\"artifactAuditReport\"", example, StringComparison.Ordinal);
        Assert.Contains("\"artifactAuditSha256\"", example, StringComparison.Ordinal);
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
    /// 验证 package.ps1 输出玩家友好启动布局：包根有启动 exe/content，运行时依赖位于 app/。
    /// </summary>
    [Fact]
    public void PackageScriptPlacesRuntimeFilesUnderAppAndAuditRejectsRootClutter()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-friendly-package-" + Guid.NewGuid().ToString("N"));

        try
        {
            string publish = Path.Combine(temp, "publish");
            string content = Path.Combine(temp, "content");
            string packageRoot = Path.Combine(temp, "package");
            _ = Directory.CreateDirectory(publish);
            _ = Directory.CreateDirectory(Path.Combine(publish, "runtimes", "win-x64", "native"));
            _ = Directory.CreateDirectory(Path.Combine(publish, "zh-Hans"));
            _ = Directory.CreateDirectory(Path.Combine(content, "scenes"));
            _ = Directory.CreateDirectory(Path.Combine(packageRoot, "PixelEngine-Demo-8.8.8-win-x64-r2r"));
            _ = Directory.CreateDirectory(Path.Combine(packageRoot, "PixelEngine-Demo-8.8.8-win-x64-aot"));
            _ = WriteTextEvidence(Path.Combine(packageRoot, "PixelEngine-Demo-8.8.8-win-x64-r2r.zip"), "stale archive");
            _ = WriteTextEvidence(Path.Combine(packageRoot, "PixelEngine-Demo-8.8.8-win-x64-r2r", "stale.txt"), "stale expanded package");
            _ = WriteTextEvidence(Path.Combine(packageRoot, "PixelEngine-Demo-8.8.8-win-x64-aot", "sentinel.txt"), "other channel");
            File.WriteAllBytes(
                Path.Combine(publish, "PixelEngine.Demo.exe"),
                System.Text.Encoding.ASCII.GetBytes("fake-apphost-prefix PixelEngine.Demo.dll\0\0\0\0\0\0\0\0\0\0\0\0fake-suffix"));
            _ = WriteTextEvidence(Path.Combine(publish, "PixelEngine.Demo.dll"), "dll");
            _ = WriteTextEvidence(Path.Combine(publish, "PixelEngine.Demo.pdb"), "pdb");
            _ = WriteTextEvidence(Path.Combine(publish, "PixelEngine.Demo.xml"), "xml");
            _ = WriteTextEvidence(Path.Combine(publish, "zh-Hans", "PixelEngine.Demo.resources.dll"), "localized");
            _ = WriteTextEvidence(Path.Combine(publish, "PixelEngine.Demo.deps.json"), "{}");
            _ = WriteTextEvidence(Path.Combine(publish, "PixelEngine.Demo.runtimeconfig.json"), "{}");
            _ = WriteTextEvidence(Path.Combine(publish, "runtimes", "win-x64", "native", "box2d.dll"), "native");
            _ = WriteTextEvidence(Path.Combine(content, "materials.json"), "{}");
            _ = WriteTextEvidence(Path.Combine(content, "reactions.json"), "{}");
            _ = WriteTextEvidence(Path.Combine(content, "scenes", "lava-mine.scene"), "scene");

            ScriptResult package = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "package.ps1"),
                "-Rid",
                "win-x64",
                "-Channel",
                "r2r",
                "-Version",
                "9.9.9",
                "-PublishDir",
                publish,
                "-OutputRoot",
                packageRoot,
                "-ContentRoot",
                content);
            Assert.Equal(0, package.ExitCode);

            string archive = Path.Combine(packageRoot, "PixelEngine-Demo-9.9.9-win-x64-r2r.zip");
            Assert.True(File.Exists(archive), package.Output);
            Assert.False(File.Exists(Path.Combine(packageRoot, "PixelEngine-Demo-8.8.8-win-x64-r2r.zip")));
            Assert.False(Directory.Exists(Path.Combine(packageRoot, "PixelEngine-Demo-8.8.8-win-x64-r2r")));
            Assert.True(Directory.Exists(Path.Combine(packageRoot, "PixelEngine-Demo-8.8.8-win-x64-aot")));
            string expandedPackageDir = Path.Combine(packageRoot, "PixelEngine-Demo-9.9.9-win-x64-r2r");
            Assert.True(Directory.Exists(expandedPackageDir), package.Output);
            Assert.True(File.Exists(Path.Combine(expandedPackageDir, "PixelEngine Demo.exe")));
            Assert.True(File.Exists(Path.Combine(expandedPackageDir, "app", "PixelEngine.Demo.dll")));
            Assert.True(File.Exists(Path.Combine(expandedPackageDir, "content", "materials.json")));
            Assert.False(File.Exists(Path.Combine(expandedPackageDir, "PixelEngine.Demo.dll")));
            Assert.False(File.Exists(Path.Combine(expandedPackageDir, "PixelEngine.Demo.pdb")));
            Assert.False(File.Exists(Path.Combine(expandedPackageDir, "PixelEngine.Demo.xml")));
            Assert.False(File.Exists(Path.Combine(expandedPackageDir, "app", "PixelEngine.Demo.pdb")));
            Assert.False(File.Exists(Path.Combine(expandedPackageDir, "app", "PixelEngine.Demo.xml")));
            Assert.False(File.Exists(Path.Combine(expandedPackageDir, "app", "zh-Hans", "PixelEngine.Demo.resources.dll")));
            Assert.False(Directory.Exists(Path.Combine(expandedPackageDir, "app", "zh-Hans")));
            Assert.False(Directory.Exists(Path.Combine(expandedPackageDir, "app", "content")));
            string extract = Path.Combine(temp, "extract");
            System.IO.Compression.ZipFile.ExtractToDirectory(archive, extract);
            string packageDir = Path.Combine(extract, "PixelEngine-Demo-9.9.9-win-x64-r2r");
            Assert.True(File.Exists(Path.Combine(packageDir, "PixelEngine Demo.exe")));
            Assert.True(File.Exists(Path.Combine(packageDir, "README.txt")));
            string packageChecksums = Path.Combine(packageDir, "SHA256SUMS");
            Assert.True(File.Exists(packageChecksums));
            Assert.False(File.Exists(Path.Combine(packageDir, "app", "PixelEngine.Demo.exe")));
            Assert.True(File.Exists(Path.Combine(packageDir, "app", "PixelEngine.Demo.dll")));
            Assert.False(File.Exists(Path.Combine(packageDir, "app", "PixelEngine.Demo.pdb")));
            Assert.False(File.Exists(Path.Combine(packageDir, "app", "PixelEngine.Demo.xml")));
            Assert.False(File.Exists(Path.Combine(packageDir, "app", "zh-Hans", "PixelEngine.Demo.resources.dll")));
            Assert.False(Directory.Exists(Path.Combine(packageDir, "app", "zh-Hans")));
            Assert.True(File.Exists(Path.Combine(packageDir, "app", "PixelEngine.Demo.deps.json")));
            Assert.True(File.Exists(Path.Combine(packageDir, "app", "PixelEngine.Demo.runtimeconfig.json")));
            Assert.True(File.Exists(Path.Combine(packageDir, "content", "materials.json")));
            Assert.True(File.Exists(Path.Combine(packageDir, "content", "reactions.json")));
            Assert.True(File.Exists(Path.Combine(packageDir, "content", "scenes", "lava-mine.scene")));
            Assert.False(Directory.Exists(Path.Combine(packageDir, "app", "content")));
            Assert.False(File.Exists(Path.Combine(packageDir, "PixelEngine.Demo.dll")));
            Assert.False(File.Exists(Path.Combine(packageDir, "PixelEngine.Demo.pdb")));
            Assert.False(File.Exists(Path.Combine(packageDir, "PixelEngine.Demo.xml")));
            Assert.False(File.Exists(Path.Combine(packageDir, "PixelEngine.Demo.deps.json")));
            Assert.False(File.Exists(Path.Combine(packageDir, "PixelEngine.Demo.runtimeconfig.json")));
            string packageChecksumsText = File.ReadAllText(packageChecksums);
            Assert.Contains("PixelEngine Demo.exe", packageChecksumsText, StringComparison.Ordinal);
            Assert.Contains("app/PixelEngine.Demo.dll", packageChecksumsText, StringComparison.Ordinal);
            Assert.DoesNotContain("PixelEngine.Demo.pdb", packageChecksumsText, StringComparison.Ordinal);
            Assert.DoesNotContain("PixelEngine.Demo.xml", packageChecksumsText, StringComparison.Ordinal);
            Assert.DoesNotContain("PixelEngine.Demo.resources.dll", packageChecksumsText, StringComparison.Ordinal);
            Assert.Contains("content/materials.json", packageChecksumsText, StringComparison.Ordinal);

            ScriptResult staleExpandedAudit = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "audit-release-artifacts.ps1"),
                "-PublishRoot",
                Path.Combine(temp, "missing-publish"),
                "-PackageRoot",
                packageRoot);
            Assert.NotEqual(0, staleExpandedAudit.ExitCode);
            Assert.Contains("展开 package 目录缺少对应归档", staleExpandedAudit.Output, StringComparison.Ordinal);

            Directory.Delete(Path.Combine(packageRoot, "PixelEngine-Demo-8.8.8-win-x64-aot"), recursive: true);
            _ = WriteTextEvidence(Path.Combine(expandedPackageDir, "PixelEngine.Demo.dll"), "root clutter");
            ScriptResult clutterAudit = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "audit-release-artifacts.ps1"),
                "-PublishRoot",
                Path.Combine(temp, "missing-publish"),
                "-PackageRoot",
                packageRoot);
            Assert.NotEqual(0, clutterAudit.ExitCode);
            Assert.Contains("展开 package 根目录不应包含运行时依赖，请放入 app/", clutterAudit.Output, StringComparison.Ordinal);
            File.Delete(Path.Combine(expandedPackageDir, "PixelEngine.Demo.dll"));

            _ = WriteTextEvidence(Path.Combine(expandedPackageDir, "app", "PixelEngine.Demo.pdb"), "debug symbol");
            ScriptResult appNoiseAudit = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "audit-release-artifacts.ps1"),
                "-PublishRoot",
                Path.Combine(temp, "missing-publish"),
                "-PackageRoot",
                packageRoot);
            Assert.NotEqual(0, appNoiseAudit.ExitCode);
            Assert.Contains("展开 package 不应包含玩家无关的调试、文档或本地化卫星资源文件", appNoiseAudit.Output, StringComparison.Ordinal);
            File.Delete(Path.Combine(expandedPackageDir, "app", "PixelEngine.Demo.pdb"));

            _ = Directory.CreateDirectory(Path.Combine(expandedPackageDir, "app", "content"));
            _ = WriteTextEvidence(Path.Combine(expandedPackageDir, "app", "content", "materials.json"), "{}");
            ScriptResult duplicateContentAudit = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "audit-release-artifacts.ps1"),
                "-PublishRoot",
                Path.Combine(temp, "missing-publish"),
                "-PackageRoot",
                packageRoot);
            Assert.NotEqual(0, duplicateContentAudit.ExitCode);
            Assert.Contains("展开 package 不应在 app/ 下重复打包 content", duplicateContentAudit.Output, StringComparison.Ordinal);
            Directory.Delete(Path.Combine(expandedPackageDir, "app", "content"), recursive: true);

            _ = WriteTextEvidence(Path.Combine(expandedPackageDir, "app", "PixelEngine.Demo.exe"), "duplicate launcher");
            ScriptResult duplicateLauncherAudit = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "audit-release-artifacts.ps1"),
                "-PublishRoot",
                Path.Combine(temp, "missing-publish"),
                "-PackageRoot",
                packageRoot);
            Assert.NotEqual(0, duplicateLauncherAudit.ExitCode);
            Assert.Contains("展开 package 不应在 app/ 下重复保留原始启动 exe", duplicateLauncherAudit.Output, StringComparison.Ordinal);
            File.Delete(Path.Combine(expandedPackageDir, "app", "PixelEngine.Demo.exe"));

            ScriptResult audit = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "audit-release-artifacts.ps1"),
                "-PublishRoot",
                Path.Combine(temp, "missing-publish"),
                "-PackageRoot",
                packageRoot);
            Assert.Equal(0, audit.ExitCode);
            Assert.Contains("Package audit passed. Packages: 1. Expanded: 1.", audit.Output, StringComparison.Ordinal);
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
    /// 验证 release evidence 预检要求所有报告来自同一个 workflow run / commit。
    /// </summary>
    [Fact]
    public void ReleaseEvidencePreflightRejectsMismatchedRunIdentity()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-release-run-identity-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateReleaseEvidenceManifest(temp, packageConclusion: "success");
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonObject publishNode = rootNode["artifacts"]!["linux-x64"]!["r2r"]!.AsObject();
            string publishReport = (string)publishNode["publishReport"]!;
            string text = File.ReadAllText(publishReport).Replace("| sha | abc |", "| sha | different-commit |", StringComparison.Ordinal);
            File.WriteAllText(publishReport, text);
            publishNode["publishSha256"] = GetSha256(publishReport);
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "run-identity-out");
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
            Assert.Contains("linux-x64/r2r/publish 报告 sha 必须与 workflow_run 一致", report, StringComparison.Ordinal);
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
    /// 验证 release evidence 预检会解析 SHA256SUMS 内容，拒绝占位或伪造 checksum 文件。
    /// </summary>
    [Fact]
    public void ReleaseEvidencePreflightRejectsInvalidSha256SumsContent()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-release-checksum-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateReleaseEvidenceManifest(temp, packageConclusion: "success");
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonObject artifactsNode = rootNode["artifacts"]!.AsObject();
            string checksum = (string)artifactsNode["win-x64"]!["r2r"]!["checksum"]!;
            File.WriteAllText(checksum, "placeholder checksum");
            string checksumHash = GetSha256(checksum);
            foreach (KeyValuePair<string, JsonNode?> ridNode in artifactsNode)
            {
                foreach (KeyValuePair<string, JsonNode?> channelNode in ridNode.Value!.AsObject())
                {
                    channelNode.Value!.AsObject()["checksumSha256"] = checksumHash;
                }
            }

            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "checksum-out");
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
            Assert.Contains("SHA256SUMS 包含无效行", report, StringComparison.Ordinal);
            Assert.Contains("SHA256SUMS 缺少 package hash 行", report, StringComparison.Ordinal);
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
    /// 验证 release evidence 预检要求 artifact audit 报告进入 manifest 且声明 require-all 审计成功。
    /// </summary>
    [Fact]
    public void ReleaseEvidencePreflightRejectsMissingArtifactAuditReport()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-release-audit-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateReleaseEvidenceManifest(temp, packageConclusion: "success");
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            _ = rootNode.Remove("artifactAuditReport");
            _ = rootNode.Remove("artifactAuditSha256");
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "audit-out");
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
            Assert.Contains("artifact_audit 缺少路径", report, StringComparison.Ordinal);
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
    /// 验证 release upload 即使写 success，也必须来自 tag ref 且 release_tag=true。
    /// </summary>
    [Fact]
    public void ReleaseEvidencePreflightRejectsSuccessfulUploadFromNonTagRef()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-release-success-non-tag-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateReleaseEvidenceManifest(
                temp,
                packageConclusion: "success",
                suffix: "success-non-tag",
                workflowRef: "refs/heads/main",
                releaseTag: "false",
                uploadTag: "main");

            string artifacts = Path.Combine(temp, "success-non-tag-out");
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
            Assert.Contains("workflow_run ref 必须是 refs/tags/v<semver>", report, StringComparison.Ordinal);
            Assert.Contains("github_release_upload release_tag 必须为 true，实际为 false", report, StringComparison.Ordinal);
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
    /// 验证 deterministic hash 报告不能只靠 conclusion=success 冒充全部 RID/channel hash 匹配。
    /// </summary>
    [Fact]
    public void ReleaseEvidencePreflightRejectsDeterministicHashRowMismatch()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-release-deterministic-row-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateReleaseEvidenceManifest(temp, packageConclusion: "success");
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            string deterministicReport = (string)rootNode["deterministicHashReport"]!;
            _ = WriteDeterministicHashEvidence(
                deterministicReport,
                conclusion: "success",
                rids: ["win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"],
                channels: ["r2r", "aot"],
                mismatchRid: "win-x64",
                mismatchChannel: "r2r");
            rootNode["deterministicHashSha256"] = GetSha256(deterministicReport);
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "deterministic-row-out");
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
            Assert.Contains("deterministic_hash 报告 win-x64/r2r result 必须为 match", report, StringComparison.Ordinal);
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
        string githubReleaseConclusion = "success",
        string workflowRef = "refs/tags/v0.1.0",
        string releaseTag = "true",
        string uploadTag = "v0.1.0")
    {
        string evidenceRoot = Path.Combine(tempRoot, suffix, "artifacts", "release-evidence");
        string packageRoot = Path.Combine(tempRoot, suffix, "artifacts", "package");
        _ = Directory.CreateDirectory(evidenceRoot);
        _ = Directory.CreateDirectory(packageRoot);

        string workflow = WriteMarkdownEvidence(
            Path.Combine(evidenceRoot, "workflow-run.md"),
            new Dictionary<string, string> { ["run_id"] = "1", ["sha"] = "abc", ["ref"] = workflowRef, ["conclusion"] = "success" });
        string upload = WriteMarkdownEvidence(
            Path.Combine(evidenceRoot, "github-release-upload.md"),
            new Dictionary<string, string> { ["tag"] = uploadTag, ["run_id"] = "1", ["sha"] = "abc", ["release_tag"] = releaseTag, ["conclusion"] = githubReleaseConclusion });
        string r2rLightup = WriteMarkdownEvidence(
            Path.Combine(evidenceRoot, "r2r-lightup.md"),
            new Dictionary<string, string> { ["run_id"] = "1", ["sha"] = "abc", ["conclusion"] = r2rLightupConclusion });
        string artifactAudit = WriteMarkdownEvidence(
            Path.Combine(evidenceRoot, "artifact-audit.md"),
            new Dictionary<string, string>
            {
                ["run_id"] = "1",
                ["sha"] = "abc",
                ["conclusion"] = "success",
                ["require_all"] = "true",
                ["package_count"] = "12",
                ["expanded_package_count"] = "12",
                ["rids"] = "win-x64,win-arm64,linux-x64,linux-arm64,osx-x64,osx-arm64",
                ["channels"] = "r2r,aot",
                ["aot_dynamic_box2d_rejected"] = "true",
                ["package_layout_checked"] = "true",
                ["checksum_checked"] = "true",
            });
        string checksum = Path.Combine(packageRoot, "SHA256SUMS");
        List<(string Name, string Hash)> packageChecksums = [];
        List<Dictionary<string, object>> packageNodes = [];

        string[] rids = ["win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"];
        string[] channels = ["r2r", "aot"];
        string deterministic = WriteDeterministicHashEvidence(
            Path.Combine(evidenceRoot, "deterministic-hash.md"),
            deterministicConclusion,
            rids,
            channels);
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
                string packageHash = GetSha256(package);
                packageChecksums.Add((Path.GetFileName(package), packageHash));

                Dictionary<string, object> node = new()
                {
                    ["publishReport"] = publish,
                    ["publishSha256"] = GetSha256(publish),
                    ["verifyReport"] = verify,
                    ["verifySha256"] = GetSha256(verify),
                    ["packageReport"] = packageReport,
                    ["packageReportSha256"] = GetSha256(packageReport),
                    ["package"] = package,
                    ["packageSha256"] = packageHash,
                    ["checksum"] = checksum,
                };
                packageNodes.Add(node);

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

        File.WriteAllLines(
            checksum,
            packageChecksums
                .OrderBy(item => item.Name, StringComparer.Ordinal)
                .Select(item => $"{item.Hash}  {item.Name}"));
        string checksumHash = GetSha256(checksum);
        foreach (Dictionary<string, object> node in packageNodes)
        {
            node["checksumSha256"] = checksumHash;
        }

        Dictionary<string, object> manifest = new()
        {
            ["schemaVersion"] = 1,
            ["workflowRunReport"] = workflow,
            ["workflowRunSha256"] = GetSha256(workflow),
            ["artifactAuditReport"] = artifactAudit,
            ["artifactAuditSha256"] = GetSha256(artifactAudit),
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
            string report = WriteMarkdownEvidence(
                Path.Combine(evidenceRoot, $"{scope}.md"),
                new Dictionary<string, string>
                {
                    ["scope"] = scope,
                    ["detector"] = "external-detector",
                    ["conclusion"] = "no_leaks",
                    [GetNativeLeakScopeRequiredMetric(scope)] = "0",
                });
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

    private static string GetNativeLeakScopeRequiredMetric(string scope)
    {
        return scope switch
        {
            "gl" => "glObjectsLiveAfterShutdown",
            "openal" => "openAlObjectsLiveAfterShutdown",
            "box2d" => "box2DBodiesLiveAfterShutdown",
            "alc" => "alcLoadContextsAliveAfterUnload",
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown native leak scope."),
        };
    }

    private static string CreateGpuParticleEvidenceManifest(
        string tempRoot,
        string suffix = "good",
        int particleCount = 100_000,
        int measuredFrames = 600,
        double sampleSeconds = 20.0,
        double cpuWallAvgMs = 6.4,
        double gpuWallAvgMs = 3.2,
        double? comparisonCpuWallAvgMs = null,
        double? comparisonGpuWallAvgMs = null,
        double? comparisonSpeedupRatio = null)
    {
        string evidenceRoot = Path.Combine(tempRoot, suffix, "artifacts", "gpu-particle-evidence");
        _ = Directory.CreateDirectory(evidenceRoot);
        double comparisonCpuWall = comparisonCpuWallAvgMs ?? cpuWallAvgMs;
        double comparisonGpuWall = comparisonGpuWallAvgMs ?? gpuWallAvgMs;
        double comparisonSpeedup = comparisonSpeedupRatio ?? (comparisonCpuWall / comparisonGpuWall);

        string hardware = WriteTextEvidence(
            Path.Combine(evidenceRoot, "target-hardware.md"),
            $"""
            # Target GPU hardware

            targetGpuName: Test GPU 4090
            targetGpuDriver: 999.1
            gpuBackend: OpenGL
            operatingSystem: Windows 11
            cpuName: Test CPU
            dotnetVersion: 10.0.8
            gitCommit: abcdef123456
            particleCount: {particleCount}
            """);

        string cpuProbe = WriteTextEvidence(
            Path.Combine(evidenceRoot, "cpu-probe.md"),
            "particle_frame_probe mode=cpu, gpu_available=False, requested_count=" + particleCount.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ", active_count=" + particleCount.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ", warmup_frames=60, measured_frames=" + measuredFrames.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ", wall_avg_ms=" + cpuWallAvgMs.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
            ", wall_p50_ms=6.000, wall_p95_ms=7.000, wall_max_ms=8.000, particle_stamp_avg_ms=2.400, particle_stamp_p50_ms=2.300, particle_stamp_p95_ms=2.600, particle_stamp_max_ms=2.900, gpu_particle_avg_ms=0.000");

        string gpuProbe = WriteTextEvidence(
            Path.Combine(evidenceRoot, "gpu-probe.md"),
            "particle_frame_probe mode=gpu, gpu_available=True, requested_count=" + particleCount.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ", active_count=" + particleCount.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ", warmup_frames=60, measured_frames=" + measuredFrames.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ", wall_avg_ms=" + gpuWallAvgMs.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
            ", wall_p50_ms=3.000, wall_p95_ms=4.000, wall_max_ms=5.000, particle_stamp_avg_ms=0.000, gpu_particle_avg_ms=0.900, gpu_particle_p50_ms=0.850, gpu_particle_p95_ms=1.050, gpu_particle_max_ms=1.200");

        string comparison = WriteTextEvidence(
            Path.Combine(evidenceRoot, "comparison.md"),
            $"""
            # Target GPU comparison

            gpuFasterThanCpu: true
            cpuWallAvgMs: {comparisonCpuWall.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}
            gpuWallAvgMs: {comparisonGpuWall.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}
            speedupRatio: {comparisonSpeedup.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}
            measuredFrames: {measuredFrames}
            sampleSeconds: {sampleSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}
            """);

        Dictionary<string, object>[] evidence =
        [
            new() { ["scope"] = "targetHardwareReport", ["path"] = hardware, ["sha256"] = GetSha256(hardware) },
            new() { ["scope"] = "cpuProbeReport", ["path"] = cpuProbe, ["sha256"] = GetSha256(cpuProbe) },
            new() { ["scope"] = "gpuProbeReport", ["path"] = gpuProbe, ["sha256"] = GetSha256(gpuProbe) },
            new() { ["scope"] = "comparisonReport", ["path"] = comparison, ["sha256"] = GetSha256(comparison) },
        ];

        Dictionary<string, object> manifest = new()
        {
            ["schemaVersion"] = 1,
            ["evidence"] = evidence,
        };

        string manifestPath = Path.Combine(tempRoot, suffix, "gpu-particle-evidence.json");
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
                entry["checklist"] = GetDemoManualChecklist(scope)
                    .ToDictionary(static key => key, static _ => (object)true);
                entry["criteria"] = GetDemoManualChecklist(scope)
                    .ToDictionary(
                        static key => key,
                        key => (object)$"{scope} {key} manual criterion confirms the recorded evidence covers this requirement.");
                if (isVideo)
                {
                    entry["durationSeconds"] = 60.0;
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

    private static IReadOnlyList<string> GetDemoManualChecklist(string scope)
    {
        return scope switch
        {
            "controlFeelReport" =>
            [
                "runJumpWallKick",
                "sandPileTraversal",
                "rigidOwnedStanding",
            ],
            "materialBrushAndReactionVideo" =>
            [
                "realMouseWheelDigits",
                "sandWaterOilGasObserved",
                "reactionTemperatureObserved",
            ],
            "rigidBodyGameplayVideo" =>
            [
                "pushAndImpact",
                "digBridgeCollapse",
                "continuedDamage",
            ],
            "particleLightingVideo" =>
            [
                "particlesVisible",
                "bloomFogLighting",
                "noParticleLeak",
            ],
            "audioListeningReport" =>
            [
                "materialImpacts",
                "ambientAndReaction",
                "spatialMix",
            ],
            "fullRoutePlaythroughVideo" =>
            [
                "routeCompleted",
                "materialsReactionsBodiesShown",
                "audioLightingHudShown",
            ],
            "hudMenuEditorVideo" =>
            [
                "hudReadable",
                "menuButtonsClicked",
                "editorDockspaceOpened",
            ],
            "hotReloadWindowReport" =>
            [
                "behaviourSourceEdited",
                "alcReloadObserved",
                "statePreserved",
            ],
            _ => [],
        };
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

    private static void SetFlatEvidenceFileContent(string manifestPath, string scope, string content)
    {
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        JsonArray evidence = manifest["evidence"]!.AsArray();
        JsonObject? entry = evidence
            .Select(node => node?.AsObject())
            .FirstOrDefault(node => string.Equals((string?)node?["scope"], scope, StringComparison.Ordinal));
        Assert.NotNull(entry);
        string path = (string?)entry["path"] ?? throw new InvalidOperationException("evidence entry 缺少 path。");
        File.WriteAllText(path, content);
        entry["sha256"] = GetSha256(path);
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
            string content = scope switch
            {
                "avx512_downclock_net_loss" => """
                    targetCpuName: Test AVX512 CPU
                    dotnetVersion: 10.0.8
                    benchmarkDotNet: true
                    vector512HardwareAccelerated: true
                    avx512Enabled: true
                    noNetDownclockLoss: true
                    """,
                "hardware_counters_cache_branch" => """
                    benchmarkDotNet: true
                    elevatedEtwKernelSession: true
                    cacheMissesPresent: true
                    branchMispredictionsPresent: true

                    | Method | Cache Misses | Branch Mispredictions |
                    |---|---:|---:|
                    | Reaction | 100 | 12 |
                    """,
                "frame_budget_target_hardware" => """
                    targetHardware: representative-target
                    sampleSeconds: 120
                    caP99Ms: 7.5
                    renderP99Ms: 3.5
                    physicsP99Ms: 3.5
                    logicAudioP99Ms: 0.8
                    """,
                _ when scope.StartsWith("cells_frame/", StringComparison.Ordinal) => $"""
                    rid: {scope["cells_frame/".Length..]}
                    benchmarkDotNet: true
                    representativeHardware: true
                    activeCellsPerFrame: 2500000
                    caFrameMs: 7.2
                    measuredIterations: 5
                    """,
                _ => $"{scope} evidence",
            };
            string report = WriteTextEvidence(Path.Combine(evidenceRoot, $"{safeScope}.md"), content);
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

    private static string WriteDeterministicHashEvidence(
        string path,
        string conclusion,
        IReadOnlyList<string> rids,
        IReadOnlyList<string> channels,
        string mismatchRid = "",
        string mismatchChannel = "")
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        List<string> lines =
        [
            "# Deterministic hash evidence",
            "",
            "| Key | Value |",
            "|---|---|",
            "| run_id | 1 |",
            "| sha | abc |",
            $"| conclusion | {conclusion} |",
            "",
            "## Package rebuild comparison",
            "",
            "| RID | Channel | Result | Detail |",
            "|---|---|---|---|",
        ];

        foreach (string rid in rids)
        {
            foreach (string channel in channels)
            {
                bool mismatch = string.Equals(rid, mismatchRid, StringComparison.Ordinal) &&
                    string.Equals(channel, mismatchChannel, StringComparison.Ordinal);
                lines.Add(mismatch
                    ? $"| {rid} | {channel} | mismatch | original=abc rebuilt=def |"
                    : $"| {rid} | {channel} | match | abc |");
            }
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

    private static void WriteBmp24(string path, int width, int height, Func<int, int, (byte R, byte G, byte B)> pixel)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        int stride = ((width * 3) + 3) & ~3;
        int imageSize = stride * height;
        int fileSize = 54 + imageSize;
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream);
        writer.Write((ushort)0x4D42);
        writer.Write(fileSize);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((ushort)1);
        writer.Write((ushort)24);
        writer.Write(0);
        writer.Write(imageSize);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);

        Span<byte> row = stackalloc byte[stride];
        for (int y = height - 1; y >= 0; y--)
        {
            row.Clear();
            for (int x = 0; x < width; x++)
            {
                (byte r, byte g, byte b) = pixel(x, y);
                int offset = x * 3;
                row[offset] = b;
                row[offset + 1] = g;
                row[offset + 2] = r;
            }

            writer.Write(row);
        }
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
