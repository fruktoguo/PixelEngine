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
        Assert.Contains("EvidenceManifestPath", script, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_detector", script, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_scope_evidence", script, StringComparison.Ordinal);
        Assert.Contains("process_smoke_only", script, StringComparison.Ordinal);
        Assert.Contains("detector_evidence_attached_pending_review", script, StringComparison.Ordinal);
        Assert.Contains("detector_report_attached_pending_review", script, StringComparison.Ordinal);
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
        Assert.Contains("tools/native-leak-preflight.ps1", plan, StringComparison.Ordinal);
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
        Assert.Contains("local_probe_only", script, StringComparison.Ordinal);
        Assert.Contains("target_gpu_evidence_attached_pending_review", script, StringComparison.Ordinal);
        Assert.Contains("GPU particle benchmark preflight failed", script, StringComparison.Ordinal);
        Assert.Contains("$exitCode = 2", script, StringComparison.Ordinal);
        Assert.Contains("$exitCode = 5", script, StringComparison.Ordinal);
        Assert.Contains("exit $exitCode", script, StringComparison.Ordinal);
        Assert.DoesNotContain("status \"passed\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("status = \"passed\"", script, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("tools/gpu-particle-benchmark-preflight.ps1", report, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_target_gpu_evidence", report, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_target_gpu_scope_evidence", report, StringComparison.Ordinal);
        Assert.Contains("local_probe_only", report, StringComparison.Ordinal);
        Assert.Contains("target_gpu_evidence_attached_pending_review", report, StringComparison.Ordinal);

        Assert.Contains("tools/gpu-particle-benchmark-preflight.ps1", plan, StringComparison.Ordinal);
        Assert.Contains("local_probe_only", plan, StringComparison.Ordinal);
        Assert.Contains("target_gpu_evidence_attached_pending_review", plan, StringComparison.Ordinal);
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

        Assert.Contains("EvidenceManifestPath", script, StringComparison.Ordinal);
        Assert.Contains("RunScriptedProbes", script, StringComparison.Ordinal);
        Assert.Contains("AllowBlocked", script, StringComparison.Ordinal);
        Assert.Contains("--scripted-window-demo", script, StringComparison.Ordinal);
        Assert.Contains("--window-ticks", script, StringComparison.Ordinal);
        Assert.Contains("scenes/lava-mine.scene", script, StringComparison.Ordinal);
        Assert.Contains("scenes/lava-mine-goal-probe.scene", script, StringComparison.Ordinal);
        Assert.Contains("scenes/lava-mine-audio-probe.scene", script, StringComparison.Ordinal);
        Assert.Contains("scenes/lava-mine-particle-light-probe.scene", script, StringComparison.Ordinal);

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
        Assert.Contains("manual_evidence_attached_pending_review", script, StringComparison.Ordinal);
        Assert.Contains("Demo manual acceptance preflight failed", script, StringComparison.Ordinal);
        Assert.Contains("$exitCode = 2", script, StringComparison.Ordinal);
        Assert.Contains("$exitCode = 5", script, StringComparison.Ordinal);
        Assert.DoesNotContain("status \"passed\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("status = \"passed\"", script, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("tools/demo-manual-acceptance-preflight.ps1", report, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_manual_evidence", report, StringComparison.Ordinal);
        Assert.Contains("scripted_probe_only", report, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_manual_scope_evidence", report, StringComparison.Ordinal);
        Assert.Contains("manual_evidence_attached_pending_review", report, StringComparison.Ordinal);

        Assert.Contains("tools/demo-manual-acceptance-preflight.ps1", plan, StringComparison.Ordinal);
        Assert.Contains("manual_evidence_attached_pending_review", plan, StringComparison.Ordinal);
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
        Assert.Contains("workflowRunReport", script, StringComparison.Ordinal);
        Assert.Contains("benchmarkGuard", script, StringComparison.Ordinal);
        Assert.Contains("buildTest", script, StringComparison.Ordinal);
        Assert.Contains("verifyPublish", script, StringComparison.Ordinal);
        Assert.Contains("testsRan=true", script, StringComparison.Ordinal);
        Assert.Contains("win-arm64 当前 CI 设计应为 build-only", script, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_ci_manifest", script, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_ci_scope_evidence", script, StringComparison.Ordinal);
        Assert.Contains("ci_matrix_evidence_attached_pending_review", script, StringComparison.Ordinal);
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
        Assert.Contains("build-test-win-arm64.md", ci, StringComparison.Ordinal);
        Assert.Contains("testsRan = $false", ci, StringComparison.Ordinal);
        Assert.Contains("verify-publish-osx-arm64.md", ci, StringComparison.Ordinal);

        Assert.Contains("tools/ci-matrix-evidence-preflight.ps1", report, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_ci_manifest", report, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_ci_scope_evidence", report, StringComparison.Ordinal);
        Assert.Contains("ci_matrix_evidence_attached_pending_review", report, StringComparison.Ordinal);
        Assert.Contains("tools/ci-matrix-evidence-preflight.ps1", plan, StringComparison.Ordinal);
        Assert.Contains("ci_matrix_evidence_attached_pending_review", plan, StringComparison.Ordinal);
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
        string evidence = ReadRepositoryFile("tools", "release-evidence-preflight.ps1");
        string release = ReadRepositoryFile(".github", "workflows", "release.yml");
        string example = ReadRepositoryFile("docs", "release-reports", "release-evidence-manifest.example.json");
        string releaseReport = ReadRepositoryFile("docs", "release-reports", "2026-07-02-win-x64-publish.md");
        string plan = ReadRepositoryFile("plan", "15-build-packaging-distribution.md");

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

        Assert.Contains("EvidenceManifestPath", evidence, StringComparison.Ordinal);
        Assert.Contains("schemaVersion", evidence, StringComparison.Ordinal);
        Assert.Contains("未知 RID", evidence, StringComparison.Ordinal);
        Assert.Contains("未知 channel", evidence, StringComparison.Ordinal);
        Assert.Contains("重复 evidence scope", evidence, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_release_manifest", evidence, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_release_scope_evidence", evidence, StringComparison.Ordinal);
        Assert.Contains("release_evidence_attached_pending_review", evidence, StringComparison.Ordinal);
        Assert.Contains("win-x64", evidence, StringComparison.Ordinal);
        Assert.Contains("osx-arm64", evidence, StringComparison.Ordinal);
        Assert.Contains("simdProbe", evidence, StringComparison.Ordinal);
        Assert.Contains("codesignReport", evidence, StringComparison.Ordinal);
        Assert.Contains("notarizationReport", evidence, StringComparison.Ordinal);
        Assert.Contains("r2rLightupReport", evidence, StringComparison.Ordinal);
        Assert.Contains("githubRelease", evidence, StringComparison.Ordinal);
        Assert.Contains("deterministicHashReport", evidence, StringComparison.Ordinal);
        Assert.DoesNotContain("status \"passed\"", evidence, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("tools/release-evidence-preflight.ps1", releaseReport, StringComparison.Ordinal);
        Assert.Contains("release_evidence_attached_pending_review", releaseReport, StringComparison.Ordinal);
        Assert.Contains("release-evidence-manifest.example.json", releaseReport, StringComparison.Ordinal);
        Assert.Contains("tools/release-evidence-preflight.ps1", plan, StringComparison.Ordinal);

        Assert.Contains("release-evidence-", release, StringComparison.Ordinal);
        Assert.Contains("Upload publish evidence", release, StringComparison.Ordinal);
        Assert.Contains("Upload verify evidence", release, StringComparison.Ordinal);
        Assert.Contains("Upload package evidence", release, StringComparison.Ordinal);
        Assert.Contains("Download release evidence", release, StringComparison.Ordinal);
        Assert.Contains("Build release evidence manifest", release, StringComparison.Ordinal);
        Assert.Contains("Preflight release evidence", release, StringComparison.Ordinal);
        Assert.Contains("release-evidence-preflight.ps1", release, StringComparison.Ordinal);
        Assert.Contains("evidence.json", release, StringComparison.Ordinal);
        Assert.Contains("r2rLightupReport", release, StringComparison.Ordinal);
        Assert.Contains("deterministicHashReport", release, StringComparison.Ordinal);

        Assert.Contains("\"schemaVersion\": 1", example, StringComparison.Ordinal);
        Assert.Contains("\"workflowRunReport\"", example, StringComparison.Ordinal);
        Assert.Contains("\"r2rLightupReport\"", example, StringComparison.Ordinal);
        Assert.Contains("\"githubRelease\"", example, StringComparison.Ordinal);
        Assert.Contains("\"simdProbe\"", example, StringComparison.Ordinal);
        Assert.Contains("\"codesignReport\"", example, StringComparison.Ordinal);
        Assert.Contains("\"notarizationReport\"", example, StringComparison.Ordinal);
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
