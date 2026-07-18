using System.Text.Json.Nodes;

using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// plan/16 性能硬化工具链纪律测试：BenchmarkDotNet、反汇编守门、各类证据预检脚本与发行审计不变式。
/// 不变式：CI 门禁可复现、证据 manifest 哈希一致、缺 scope/坏结论必须失败且 pending review 非零。
/// </summary>
public sealed class PerformanceHardeningToolingDisciplineTests
{
    /// <summary>
    /// 验证 BenchmarkDotNet 入口默认接入内存、线程与反汇编 diagnoser。
    /// </summary>
    [Fact]

    // —— BenchmarkDotNet 与 CI 回归门禁 ——
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
    /// 验证 EventPipe profiler 只在显式诊断运行中启用，默认吞吐基准不承担 trace 开销。
    /// </summary>
    [Fact]
    public void BenchmarkProgramGatesEventPipeProfilerBehindExplicitEnvironmentSwitch()
    {
        string program = ReadRepositoryFile("bench", "PixelEngine.Benchmarks", "Program.cs");

        Assert.Contains("PIXELENGINE_BENCH_EVENTPIPE", program, StringComparison.Ordinal);
        Assert.Contains("new EventPipeProfiler()", program, StringComparison.Ordinal);
        Assert.Contains("StringComparison.Ordinal", program, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 CI 运行反汇编守门与 BenchmarkDotNet 性能回归门禁。
    /// </summary>
    [Fact]
    public void CiRunsDisassemblyAndBenchmarkRegressionGuards()
    {
        // Arrange：准备输入与初始状态
        string ci = ReadRepositoryFile(".github", "workflows", "ci.yml");
        string runner = ReadRepositoryFile("tools", "run-benchmark.ps1");
        string regression = ReadRepositoryFile("tools", "benchmark-regression.ps1");
        string disassembly = ReadRepositoryFile("tools", "disassembly-guard.ps1");
        string baseline = ReadRepositoryFile("bench", "PixelEngine.Benchmarks", "baselines", "ci-baseline.json");

        // Assert：验证预期结果
        Assert.Contains("benchmark-guard", ci, StringComparison.Ordinal);
        Assert.Contains("./tools/disassembly-guard.ps1", ci, StringComparison.Ordinal);
        Assert.Contains("./tools/benchmark-regression.ps1", ci, StringComparison.Ordinal);
        Assert.Contains("Copy-RepositoryForBenchmark", runner, StringComparison.Ordinal);
        Assert.Contains("\".claude\"", runner, StringComparison.Ordinal);
        Assert.Contains("\"out\"", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("\"runtimes\"", runner, StringComparison.Ordinal);
        Assert.Contains("Push-Location $tempRoot", runner, StringComparison.Ordinal);
        Assert.Contains("Expand-BenchmarkDotNetArguments", runner, StringComparison.Ordinal);
        Assert.Contains("ValueFromRemainingArguments = $true", runner, StringComparison.Ordinal);
        Assert.Contains("$BenchmarkDotNetArgs + $RemainingBenchmarkDotNetArgs", runner, StringComparison.Ordinal);
        Assert.Contains("$argument.IndexOf('=', [StringComparison]::Ordinal)", runner, StringComparison.Ordinal);
        Assert.Contains("$BenchmarkDotNetArgs = @(", runner, StringComparison.Ordinal);
        Assert.Contains("& dotnet build-server shutdown", runner, StringComparison.Ordinal);
        Assert.Contains("\"--disable-build-servers\"", runner, StringComparison.Ordinal);
        Assert.Contains("Generate Exception", runner, StringComparison.Ordinal);
        Assert.Contains("There are not any results runs", runner, StringComparison.Ordinal);
        Assert.Contains("DllNotFoundException", runner, StringComparison.Ordinal);
        Assert.Contains("BenchmarkDotNet artifacts were not produced", runner, StringComparison.Ordinal);
        Assert.Contains("BenchmarkDotNet produced no report files", runner, StringComparison.Ordinal);
        Assert.Contains("ERROR(S):", runner, StringComparison.Ordinal);
        Assert.Contains("executed benchmarks:\\s*[1-9][0-9]*\\b", runner, StringComparison.Ordinal);
        Assert.Contains("tools/run-benchmark.ps1", regression, StringComparison.Ordinal);
        Assert.Contains("tools/run-benchmark.ps1", disassembly, StringComparison.Ordinal);
        Assert.Contains("BenchmarkDotNet regression run", regression, StringComparison.Ordinal);
        Assert.Contains("maxRatio", regression, StringComparison.Ordinal);
        Assert.Contains("benchmarkType", regression, StringComparison.Ordinal);
        Assert.Contains("parameters", regression, StringComparison.Ordinal);
        Assert.Contains("RNGCHKFAIL", disassembly, StringComparison.Ordinal);
        Assert.Contains("ymm|zmm", disassembly, StringComparison.Ordinal);
        Assert.Contains("HardwareIntrinsics", disassembly, StringComparison.Ordinal);
        Assert.Contains("\"benchmarks\"", baseline, StringComparison.Ordinal);
        Assert.Contains("CellThroughputBenchmark.StepJobSystem.FullActiveLiquid", baseline, StringComparison.Ordinal);
        Assert.Contains("CellThroughputBenchmark.StepJobSystem.TypicalDirtyRect", baseline, StringComparison.Ordinal);
        Assert.Contains("ReactionLookupBenchmark.FindDirect", baseline, StringComparison.Ordinal);
        Assert.Contains("ParticleIntegrationBenchmark.IntegrateFlyingParticles.200000", baseline, StringComparison.Ordinal);
        Assert.DoesNotContain("rowContains", baseline, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证本机测试入口先构建一次，再顺序以 no-build 执行各测试工程，避免并发写同一 obj 目录造成 CS2012 误判。
    /// </summary>
    [Fact]
    public void LocalTestRunnerBuildsOnceThenRunsProjectsSequentiallyNoBuild()
    {
        string runner = ReadRepositoryFile("tools", "run-tests.ps1");

        Assert.Contains("dotnet @Arguments", runner, StringComparison.Ordinal);
        Assert.Contains("\"build-server\", \"shutdown\"", runner, StringComparison.Ordinal);
        Assert.Contains("\"build\", \"PixelEngine.sln\"", runner, StringComparison.Ordinal);
        Assert.Contains("\"--disable-build-servers\"", runner, StringComparison.Ordinal);
        Assert.Contains("\"-m:1\"", runner, StringComparison.Ordinal);
        Assert.Contains("\"--no-build\"", runner, StringComparison.Ordinal);
        Assert.Contains("foreach ($projectPath in $testProjects)", runner, StringComparison.Ordinal);
        Assert.Contains("Sort-Object FullName", runner, StringComparison.Ordinal);
        Assert.Contains("\"--filter\", $Filter", runner, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Benchmark 回归门禁按 Markdown 表头解析 Mean 列，不会把 Error/StdDev 等时间列误当作均值。
    /// </summary>
    [Fact]
    public void BenchmarkRegressionGateParsesMeanColumnFromSyntheticReport()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-benchmark-regression-" + Guid.NewGuid().ToString("N"));

        try
        {
            string reports = Path.Combine(temp, "reports");
            _ = Directory.CreateDirectory(reports);
            string reportPath = Path.Combine(reports, "PixelEngine.Benchmarks.CellThroughputBenchmark-report-github.md");
            File.WriteAllText(
                reportPath,
                """
                | Method | Profile | Mean | Error | StdDev |
                |------- |--------- |-----:|------:|-------:|
                | StepJobSystem | FullActiveLiquidLegacy | 99.999 ms | 1.000 ns | 2.000 ns |
                | StepJobSystem | FullActiveLiquid | 38.327 ms | 1.000 ns | 2.000 ns |
                | StepJobSystem | TypicalDirtyRect | 413.100 us | 1.000 ns | 2.000 ns |
                """);
            File.WriteAllText(
                Path.Combine(reports, "PixelEngine.Benchmarks.OtherCellThroughputBenchmark-report-github.md"),
                """
                | Method | Profile | Mean |
                |------- |-------- |-----:|
                | StepJobSystem | FullActiveLiquid | 1.000 ns |
                | StepJobSystem | TypicalDirtyRect | 1.000 ns |
                """);

            string baselinePath = Path.Combine(temp, "baseline.json");
            File.WriteAllText(
                baselinePath,
                                     /*lang=json,strict*/
                                     """
                {
                  "benchmarks": [
                    {
                      "name": "PixelEngine.Benchmarks.CellThroughputBenchmark.StepJobSystem.FullActiveLiquid",
                      "benchmarkType": "PixelEngine.Benchmarks.CellThroughputBenchmark",
                      "method": "StepJobSystem",
                      "filter": "*CellThroughputBenchmark.StepJobSystem*",
                      "parameters": { "Profile": "FullActiveLiquid" },
                      "baselineMeanNs": 38327000.0,
                      "maxRatio": 1.0
                    },
                    {
                      "name": "PixelEngine.Benchmarks.CellThroughputBenchmark.StepJobSystem.TypicalDirtyRect",
                      "benchmarkType": "PixelEngine.Benchmarks.CellThroughputBenchmark",
                      "method": "StepJobSystem",
                      "filter": "*CellThroughputBenchmark.StepJobSystem*",
                      "parameters": { "Profile": "TypicalDirtyRect" },
                      "baselineMeanNs": 413100.0,
                      "maxRatio": 1.0
                    }
                  ]
                }
                """);

            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "benchmark-regression.ps1"),
                "-BaselinePath",
                baselinePath,
                "-ReportsPath",
                reports);

            // Assert：验证不变式与预期结果
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("FullActiveLiquid mean=38327000", result.Output, StringComparison.Ordinal);
            Assert.Contains("TypicalDirtyRect mean=413100", result.Output, StringComparison.Ordinal);
            Assert.Contains("Benchmark regression gate passed.", result.Output, StringComparison.Ordinal);
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
    /// 验证参数化 benchmark 若缺精确参数契约会失败，防止同名 Method 的多行报告被任意第一行误配。
    /// </summary>
    [Fact]
    public void BenchmarkRegressionGateRequiresExactParametersForAmbiguousParameterizedRows()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-benchmark-regression-ambiguous-" + Guid.NewGuid().ToString("N"));

        try
        {
            string reports = Path.Combine(temp, "results");
            _ = Directory.CreateDirectory(reports);
            File.WriteAllText(
                Path.Combine(reports, "PixelEngine.Benchmarks.CellThroughputBenchmark-report-github.md"),
                """
                | Method | Profile | Mean |
                |------- |--------- |-----:|
                | StepJobSystem | FullActiveLiquid | 38.327 ms |
                | StepJobSystem | TypicalDirtyRect | 413.100 us |
                """);

            string baselinePath = Path.Combine(temp, "baseline.json");
            File.WriteAllText(
                baselinePath,
                                     /*lang=json,strict*/
                                     """
                {
                  "benchmarks": [
                    {
                      "name": "PixelEngine.Benchmarks.CellThroughputBenchmark.StepJobSystem",
                      "benchmarkType": "PixelEngine.Benchmarks.CellThroughputBenchmark",
                      "method": "StepJobSystem",
                      "filter": "*CellThroughputBenchmark.StepJobSystem*",
                      "baselineMeanNs": 38327000.0,
                      "maxRatio": 1.0
                    }
                  ]
                }
                """);

            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "benchmark-regression.ps1"),
                "-BaselinePath",
                baselinePath,
                "-ReportsPath",
                reports);

            // Assert：验证不变式与预期结果
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("multiple parameterized rows", result.Output, StringComparison.Ordinal);
            Assert.Contains("exact parameters object", result.Output, StringComparison.Ordinal);
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
    /// 验证多个 baseline 条目共享同一 BDN filter 时只执行一次，并从该次参数表中精确选择各自的行。
    /// </summary>
    [Fact]
    public void BenchmarkRegressionGateRunsEachDistinctFilterExactlyOnce()
    {
        // Arrange：用可观察的 runner 生成一张含两行参数的真实 Markdown 表。
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-benchmark-regression-filter-group-" + Guid.NewGuid().ToString("N"));

        try
        {
            _ = Directory.CreateDirectory(temp);
            string callsPath = Path.Combine(temp, "runner-calls.txt");
            string runnerPath = Path.Combine(temp, "fake-benchmark-runner.ps1");
            File.WriteAllText(
                runnerPath,
                """
                param(
                  [string]$Project,
                  [string]$Artifacts,
                  [string[]]$BenchmarkDotNetArgs
                )

                Add-Content -LiteralPath '__CALLS__' -Value ($BenchmarkDotNetArgs -join ' ')
                $results = Join-Path $Artifacts 'results'
                New-Item -ItemType Directory -Force -Path $results | Out-Null
                @'
                | Method | Profile | Mean |
                |------- |-------- |-----:|
                | StepJobSystem | FullActiveLiquid | 38.327 ms |
                | StepJobSystem | TypicalDirtyRect | 413.100 us |
                '@ | Set-Content -LiteralPath (Join-Path $results 'PixelEngine.Benchmarks.CellThroughputBenchmark-report-github.md')
                """.Replace("__CALLS__", callsPath.Replace("'", "''")));

            string baselinePath = Path.Combine(temp, "baseline.json");
            File.WriteAllText(
                baselinePath,
                                     /*lang=json,strict*/
                                     """
                {
                  "benchmarks": [
                    {
                      "name": "PixelEngine.Benchmarks.CellThroughputBenchmark.StepJobSystem.FullActiveLiquid",
                      "benchmarkType": "PixelEngine.Benchmarks.CellThroughputBenchmark",
                      "method": "StepJobSystem",
                      "filter": "*CellThroughputBenchmark.StepJobSystem*",
                      "parameters": { "Profile": "FullActiveLiquid" },
                      "baselineMeanNs": 38327000.0,
                      "maxRatio": 1.0
                    },
                    {
                      "name": "PixelEngine.Benchmarks.CellThroughputBenchmark.StepJobSystem.TypicalDirtyRect",
                      "benchmarkType": "PixelEngine.Benchmarks.CellThroughputBenchmark",
                      "method": "StepJobSystem",
                      "filter": "*CellThroughputBenchmark.StepJobSystem*",
                      "parameters": { "Profile": "TypicalDirtyRect" },
                      "baselineMeanNs": 413100.0,
                      "maxRatio": 1.0
                    }
                  ]
                }
                """);

            // Act：走与 CI 相同的“执行 benchmark”分支，不提供预制 ReportsPath。
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "benchmark-regression.ps1"),
                "-BaselinePath",
                baselinePath,
                "-Artifacts",
                Path.Combine(temp, "artifacts"),
                "-BenchmarkRunnerPath",
                runnerPath);

            // Assert：共享 filter 仅调用一次，且两个精确参数 baseline 均被校验。
            Assert.Equal(0, result.ExitCode);
            string[] calls = File.ReadAllLines(callsPath);
            string call = Assert.Single(calls);
            Assert.Contains("--filter *CellThroughputBenchmark.StepJobSystem*", call, StringComparison.Ordinal);
            Assert.Contains("FullActiveLiquid mean=38327000", result.Output, StringComparison.Ordinal);
            Assert.Contains("TypicalDirtyRect mean=413100", result.Output, StringComparison.Ordinal);
            Assert.Contains("Benchmark regression gate passed.", result.Output, StringComparison.Ordinal);
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
    /// 验证 JIT/BDN/IDE 反汇编流程有可复现命令文档。
    /// </summary>
    [Fact]

    // —— 反汇编与硬件计数器预检 ——
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
        // Arrange：准备输入与初始状态
        string script = ReadRepositoryFile("tools", "hardware-counter-preflight.ps1");
        string report = ReadRepositoryFile("docs", "benchmark-reports", "2026-07-02-latency-branch-calibration.md");
        string plan = ReadRepositoryFile("plan", "16-performance-hardening.md");
        string testingPlan = ReadRepositoryFile("plan", "14-testing-benchmarking.md");
        string readme = ReadRepositoryFile("plan", "tasks", "70-evidence-contracts.md");

        // Assert：验证预期结果
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

        Assert.Contains("BenchmarkDotNet Windows ETW 硬件计数器路径", readme, StringComparison.Ordinal);
        Assert.Contains("非 Windows runner 不作为 Cache Misses / Branch Mispredictions 验收环境", readme, StringComparison.Ordinal);
        Assert.Contains("elevated ETW Kernel Session", readme, StringComparison.Ordinal);
        Assert.Contains("`-RunBenchmark`", readme, StringComparison.Ordinal);
        Assert.Contains("同时出现 `Cache Misses` 与 `Branch Mispredictions` 列", readme, StringComparison.Ordinal);
        Assert.Contains("`ready` 只表示权限预检通过", readme, StringComparison.Ordinal);
        Assert.Contains("`counters_present` 只表示本地列检查通过", readme, StringComparison.Ordinal);
        Assert.Contains("不能解除 `PERF-009`", readme, StringComparison.Ordinal);
        Assert.Contains("hardware_counters_cache_branch", readme, StringComparison.Ordinal);
        Assert.Contains("benchmarkRunId", readme, StringComparison.Ordinal);
        Assert.Contains("gitCommit", readme, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证硬件计数器预检脚本在当前宿主上真实写出权限 / 平台边界报告，且默认不运行 benchmark。
    /// </summary>
    [Fact]
    public void HardwareCounterPreflightWritesHostBoundaryReport()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-hardware-counter-preflight-" + Guid.NewGuid().ToString("N"));

        try
        {
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "hardware-counter-preflight.ps1"),
                "-Artifacts",
                temp,
                "-AllowBlocked");

            // Assert：验证不变式与预期结果
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
    /// 验证 2.17M full-active 目标基准用独立同初态 kernel 拉长测量窗口，不把连续帧 dirty 收缩冒充 full scan。
    /// </summary>
    [Fact]
    public void FullActiveCaTargetBenchmarkUsesIndependentFullDirtyFrames()
    {
        string benchmark = ReadRepositoryFile("bench", "PixelEngine.Benchmarks", "FullActiveCellThroughputBenchmark.cs");
        string source = ReadRepositoryFile("bench", "PixelEngine.Benchmarks", "BenchmarkChunkSource.cs");

        Assert.Contains("private const int FramesPerInvoke = 16;", benchmark, StringComparison.Ordinal);
        Assert.Contains("private readonly FullActiveFrame[] _frames", benchmark, StringComparison.Ordinal);
        Assert.Contains("[Benchmark(OperationsPerInvoke = FramesPerInvoke)]", benchmark, StringComparison.Ordinal);
        Assert.Contains("StepJobSystemFullActive2MIndependentFrames", benchmark, StringComparison.Ordinal);
        Assert.Contains("_frames[i].Reset();", benchmark, StringComparison.Ordinal);
        Assert.Contains("CoveredCells != ActiveCellsPerFrame", benchmark, StringComparison.Ordinal);
        Assert.Contains("chunk.SetCurrentDirty(DirtyRect.Full);", benchmark, StringComparison.Ordinal);
        Assert.Contains("kernel.StepCa(jobs);", benchmark, StringComparison.Ordinal);
        Assert.Contains("internal sealed class BenchmarkChunkSource", source, StringComparison.Ordinal);
        Assert.Contains("ResolveNeighborhood", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 typical dirty 回归基准用独立单帧 kernel 拉长 workload，不靠连续帧或短 iteration 得出结论。
    /// </summary>
    [Fact]
    public void TypicalDirtyBenchmarkUsesIndependentSingleFrames()
    {
        string benchmark = ReadRepositoryFile("bench", "PixelEngine.Benchmarks", "TypicalDirtyCellThroughputBenchmark.cs");

        Assert.Contains("private const int FramesPerInvoke = 12_288;", benchmark, StringComparison.Ordinal);
        Assert.Contains("private readonly TypicalDirtyFrame[] _frames", benchmark, StringComparison.Ordinal);
        Assert.Contains("private Chunk[] _sharedGuards", benchmark, StringComparison.Ordinal);
        Assert.Contains("[Benchmark(OperationsPerInvoke = FramesPerInvoke)]", benchmark, StringComparison.Ordinal);
        Assert.Contains("StepJobSystemTypicalDirtyIndependentFrames", benchmark, StringComparison.Ordinal);
        Assert.Contains("_frames[i].Reset();", benchmark, StringComparison.Ordinal);
        Assert.Contains("CoveredCells != ActiveCellsPerFrame", benchmark, StringComparison.Ordinal);
        Assert.Contains("[IterationCleanup]", benchmark, StringComparison.Ordinal);
        Assert.Contains("ValidateSharedGuards();", benchmark, StringComparison.Ordinal);
        Assert.Contains("chunks[4] = _center;", benchmark, StringComparison.Ordinal);
        Assert.Contains("chunks[7] = _south;", benchmark, StringComparison.Ordinal);
        Assert.Contains("chunk.GetIncomingDirty(slot).IsEmpty", benchmark, StringComparison.Ordinal);
        Assert.Contains("chunk.Material[cell] != 0", benchmark, StringComparison.Ordinal);
        Assert.Contains("Kernel.RestoreFrameState(frameIndex: 0, currentParity: 0);", benchmark, StringComparison.Ordinal);
        Assert.Contains("_center.SetCurrentDirty(new DirtyRect", benchmark, StringComparison.Ordinal);
        Assert.Contains("kernel.StepCa(jobs);", benchmark, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证目标硬件性能证据预检要求 AVX-512、6 RID cells/frame、帧预算与硬件计数器 scope/hash，不把本机短样本当作通过。
    /// </summary>
    [Fact]

    // —— 性能目标证据预检 ——
    public void PerformanceTargetEvidencePreflightRequiresManifestScopesAndHashes()
    {
        // Arrange：准备输入与初始状态
        string script = ReadRepositoryFile("tools", "performance-target-evidence-preflight.ps1");
        string report = ReadRepositoryFile("docs", "benchmark-reports", "2026-07-02-performance-target-evidence.md");
        string readme = ReadRepositoryFile("plan", "tasks", "70-evidence-contracts.md");
        string plan = ReadRepositoryFile("plan", "16-performance-hardening.md");

        // Assert：验证预期结果
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
        Assert.Contains("FullActiveCellThroughputBenchmark\\.StepJobSystemFullActive2MIndependentFrames", script, StringComparison.Ordinal);
        Assert.Contains("FullActive2M cells/frame", script, StringComparison.Ordinal);
        string[] requiredMachineFields =
        [
            "targetCpuName",
            "dotnetVersion",
            "benchmarkRunId",
            "gitCommit",
            "vector512HardwareAccelerated",
            "avx512Enabled",
            "noNetDownclockLoss",
            "elevatedEtwKernelSession",
            "cacheMissesPresent",
            "branchMispredictionsPresent",
            "targetHardware",
            "source",
            "scenario",
            "demoScene",
            "sampleSeconds",
            "frameSamples",
            "fixedTickNoCatchUp",
            "playerPackageRun",
            "realWindowRun",
            "degradationPolicyObserved",
            "frameTimelineCaptured",
            "caP99Ms",
            "renderP99Ms",
            "physicsP99Ms",
            "logicAudioP99Ms",
            "representativeHardware",
            "activeCellsPerFrame",
            "caFrameMs",
            "measuredIterations",
            "iterationCount",
        ];
        foreach (string field in requiredMachineFields)
        {
            Assert.Contains(field, script, StringComparison.Ordinal);
            Assert.Contains(field, report, StringComparison.Ordinal);
            Assert.Contains(field, readme, StringComparison.Ordinal);
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
        Assert.Contains("BenchmarkDotNet v", report, StringComparison.Ordinal);
        Assert.Contains("FullActiveCellThroughputBenchmark.StepJobSystemFullActive2MIndependentFrames", report, StringComparison.Ordinal);
        Assert.Contains("FullActive2M", report, StringComparison.Ordinal);
        Assert.Contains("未知 scope", report, StringComparison.Ordinal);

        Assert.Contains("tools/performance-target-evidence-preflight.ps1", readme, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_target_performance_manifest", readme, StringComparison.Ordinal);
        Assert.Contains("blocked_invalid_target_performance_evidence", readme, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_target_performance_scope_evidence", readme, StringComparison.Ordinal);
        Assert.Contains("target_performance_evidence_attached_pending_review", readme, StringComparison.Ordinal);
        Assert.Contains("avx512_downclock_net_loss", readme, StringComparison.Ordinal);
        Assert.Contains("hardware_counters_cache_branch", readme, StringComparison.Ordinal);
        Assert.Contains("frame_budget_target_hardware", readme, StringComparison.Ordinal);
        Assert.Contains("cells_frame/osx-arm64", readme, StringComparison.Ordinal);
        Assert.Contains("sampleSeconds>=60", readme, StringComparison.Ordinal);
        Assert.Contains("frameSamples>=3600", readme, StringComparison.Ordinal);
        Assert.Contains("caP99Ms<=8", readme, StringComparison.Ordinal);
        Assert.Contains("renderP99Ms<=4", readme, StringComparison.Ordinal);
        Assert.Contains("physicsP99Ms<=4", readme, StringComparison.Ordinal);
        Assert.Contains("logicAudioP99Ms<=1", readme, StringComparison.Ordinal);
        Assert.Contains("activeCellsPerFrame>=2000000", readme, StringComparison.Ordinal);
        Assert.Contains("caFrameMs<=8", readme, StringComparison.Ordinal);
        Assert.Contains("measuredIterations>=3", readme, StringComparison.Ordinal);
        Assert.Contains("iterationCount>=measuredIterations", readme, StringComparison.Ordinal);
        Assert.Contains("BenchmarkDotNet v", readme, StringComparison.Ordinal);
        Assert.Contains("FullActiveCellThroughputBenchmark.StepJobSystemFullActive2MIndependentFrames", readme, StringComparison.Ordinal);
        Assert.Contains("FullActive2M", readme, StringComparison.Ordinal);

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
    /// 验证 canonical evidence 合同覆盖全部外部证据入口，并明确待审/本机探针状态不是验收通过。
    /// </summary>
    [Fact]

    // —— 热路径与计划文档索引 ——
    public void TaskEvidenceCatalogIndexesAllEvidencePreflightStatusesAsNonPassing()
    {
        // Arrange：准备输入与初始状态
        string readme = ReadRepositoryFile("plan", "tasks", "70-evidence-contracts.md");

        string[] tools =
        [
            "tools/hardware-counter-preflight.ps1",
            "tools/ci-matrix-evidence-preflight.ps1",
            "tools/performance-target-evidence-preflight.ps1",
            "tools/gpu-particle-benchmark-preflight.ps1",
            "tools/demo-manual-acceptance-preflight.ps1",
            "tools/native-leak-preflight.ps1",
            "tools/ui-runtime-evidence-preflight.ps1",
            "tools/editor-ux-evidence-preflight.ps1",
            "tools/release-evidence-preflight.ps1",
        ];
        foreach (string tool in tools)
        {
            // Assert：验证预期结果
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
            "blocked_missing_ui_runtime_evidence",
            "blocked_invalid_ui_runtime_evidence",
            "blocked_missing_ui_runtime_scope_evidence",
            "ui_runtime_evidence_attached_pending_review",
            "blocked_missing_editor_ux_evidence",
            "blocked_invalid_editor_ux_evidence",
            "blocked_missing_editor_ux_scope_evidence",
            "editor_ux_evidence_attached_pending_review",
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
        Assert.Contains("都不是对应 canonical task 的完成状态", readme, StringComparison.Ordinal);
        Assert.Contains("本地计数器列检查通过", readme, StringComparison.Ordinal);
        Assert.Contains("对应 canonical task 仍保持 `[!]`", readme, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 native 资源泄漏预检不会把进程 smoke 误当作 GL/OpenAL/Box2D/ALC 泄漏验收。
    /// </summary>
    [Fact]

    // —— 原生泄漏检测证据预检 ——
    public void NativeLeakPreflightRequiresExternalDetectorEvidence()
    {
        // Arrange：准备输入与初始状态
        string script = ReadRepositoryFile("tools", "native-leak-preflight.ps1");
        string report = ReadRepositoryFile("docs", "runtime-reports", "2026-07-02-demo-window-longrun.md");
        string plan = ReadRepositoryFile("plan", "18-hosting-runtime.md");
        string readme = ReadRepositoryFile("plan", "tasks", "70-evidence-contracts.md");

        // Assert：验证预期结果
        Assert.Contains("DetectorReportPath", script, StringComparison.Ordinal);
        Assert.Contains("EvidenceManifestPath", script, StringComparison.Ordinal);
        Assert.Contains("schemaVersion 必须为 1", script, StringComparison.Ordinal);
        Assert.Contains("包含未知 scope", script, StringComparison.Ordinal);
        Assert.Contains("conclusion 必须为 no_leaks", script, StringComparison.Ordinal);
        Assert.Contains("glObjectsLiveAfterShutdown", script, StringComparison.Ordinal);
        Assert.Contains("openAlObjectsLiveAfterShutdown", script, StringComparison.Ordinal);
        Assert.Contains("box2DBodiesLiveAfterShutdown", script, StringComparison.Ordinal);
        Assert.Contains("alcLoadContextsAliveAfterUnload", script, StringComparison.Ordinal);
        Assert.Contains("单 detector report", script, StringComparison.Ordinal);
        Assert.Contains("必须为 0", script, StringComparison.Ordinal);
        Assert.Contains("scope 缺少 detector", script, StringComparison.Ordinal);
        Assert.Contains("sha256", script, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", script, StringComparison.Ordinal);
        Assert.Contains("sha256 不匹配", script, StringComparison.Ordinal);
        Assert.Contains("detectorRunId", script, StringComparison.Ordinal);
        Assert.Contains("gitCommit", script, StringComparison.Ordinal);
        Assert.Contains("必须与 manifest detectorRunId 一致", script, StringComparison.Ordinal);
        Assert.Contains("必须与 manifest gitCommit 一致", script, StringComparison.Ordinal);
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

        Assert.Contains("Native leak", readme, StringComparison.Ordinal);
        Assert.Contains("detector_report_attached_pending_review", readme, StringComparison.Ordinal);
        Assert.Contains("detector_evidence_attached_pending_review", readme, StringComparison.Ordinal);
        Assert.Contains("schemaVersion=1", readme, StringComparison.Ordinal);
        Assert.Contains("detectorRunId", readme, StringComparison.Ordinal);
        Assert.Contains("gitCommit", readme, StringComparison.Ordinal);
        Assert.Contains("scope 仅允许 `gl` / `openal` / `box2d` / `alc`", readme, StringComparison.Ordinal);
        Assert.Contains("path + sha256 + detector", readme, StringComparison.Ordinal);
        Assert.Contains("conclusion=no_leaks", readme, StringComparison.Ordinal);
        Assert.Contains("glObjectsLiveAfterShutdown=0", readme, StringComparison.Ordinal);
        Assert.Contains("openAlObjectsLiveAfterShutdown=0", readme, StringComparison.Ordinal);
        Assert.Contains("box2DBodiesLiveAfterShutdown=0", readme, StringComparison.Ordinal);
        Assert.Contains("alcLoadContextsAliveAfterUnload=0", readme, StringComparison.Ordinal);
        Assert.Contains("process_smoke_only", readme, StringComparison.Ordinal);
        Assert.Contains("gl_context_rendering_wrappers", readme, StringComparison.Ordinal);
        Assert.Contains("managed_no_gl_context", readme, StringComparison.Ordinal);
        Assert.Contains("不能解除 `EVID-002`", readme, StringComparison.Ordinal);
        Assert.Contains("外部 GL driver 级 detector", readme, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 managed native leak detector 已进入 solution，并明确 GL 只提供托管覆盖边界说明。
    /// </summary>
    [Fact]
    public void ManagedNativeLeakDetectorIsSolutionTrackedAndDocumentsManagedCoverage()
    {
        // Arrange：准备输入与初始状态
        string solution = ReadRepositoryFile("PixelEngine.sln");
        string project = ReadRepositoryFile("tools", "PixelEngine.Tools.ManagedNativeLeakDetector", "PixelEngine.Tools.ManagedNativeLeakDetector.csproj");
        string program = ReadRepositoryFile("tools", "PixelEngine.Tools.ManagedNativeLeakDetector", "Program.cs");
        string report = ReadRepositoryFile("docs", "runtime-reports", "2026-07-02-demo-window-longrun.md");
        string plan = ReadRepositoryFile("plan", "18-hosting-runtime.md");

        // Assert：验证预期结果
        Assert.Contains("tools\\PixelEngine.Tools.ManagedNativeLeakDetector\\PixelEngine.Tools.ManagedNativeLeakDetector.csproj", solution, StringComparison.Ordinal);
        Assert.Contains("PixelEngine.Audio.csproj", project, StringComparison.Ordinal);
        Assert.Contains("PixelEngine.Interop.csproj", project, StringComparison.Ordinal);
        Assert.Contains("PixelEngine.Physics.csproj", project, StringComparison.Ordinal);
        Assert.Contains("PixelEngine.Rendering.csproj", project, StringComparison.Ordinal);
        Assert.Contains("PixelEngine.Scripting.csproj", project, StringComparison.Ordinal);
        Assert.Contains("managed-native-leak-detector", program, StringComparison.Ordinal);
        Assert.Contains("managedProbe", program, StringComparison.Ordinal);
        Assert.Contains("RenderWindow.Create", program, StringComparison.Ordinal);
        Assert.Contains("gl_context_rendering_wrappers", program, StringComparison.Ordinal);
        Assert.Contains("managed_no_gl_context", program, StringComparison.Ordinal);
        Assert.Contains("GlTexture", program, StringComparison.Ordinal);
        Assert.Contains("GlBuffer", program, StringComparison.Ordinal);
        Assert.Contains("Framebuffer", program, StringComparison.Ordinal);
        Assert.Contains("ShaderProgram.Create", program, StringComparison.Ordinal);
        Assert.Contains("GlResourceTracker.Snapshot().Total", program, StringComparison.Ordinal);
        Assert.Contains("still requires external driver-level GL leak evidence", program, StringComparison.Ordinal);
        Assert.Contains("glObjectsLiveAfterShutdown", program, StringComparison.Ordinal);
        Assert.Contains("openAlObjectsLiveAfterShutdown", program, StringComparison.Ordinal);
        Assert.Contains("box2DBodiesLiveAfterShutdown", program, StringComparison.Ordinal);
        Assert.Contains("alcLoadContextsAliveAfterUnload", program, StringComparison.Ordinal);
        Assert.Contains("OpenAlDevice.TryInitialize", program, StringComparison.Ordinal);
        Assert.Contains("PhysicsSystem.Initialize", program, StringComparison.Ordinal);
        Assert.Contains("ScriptHotReloadController", program, StringComparison.Ordinal);
        Assert.Contains("evidence.json", program, StringComparison.Ordinal);

        Assert.Contains("tools/PixelEngine.Tools.ManagedNativeLeakDetector", report, StringComparison.Ordinal);
        Assert.Contains("gl_context_rendering_wrappers", report, StringComparison.Ordinal);
        Assert.Contains("managed_no_gl_context", report, StringComparison.Ordinal);
        Assert.Contains("tools/PixelEngine.Tools.ManagedNativeLeakDetector", plan, StringComparison.Ordinal);
        Assert.Contains("gl_context_rendering_wrappers", plan, StringComparison.Ordinal);
        Assert.Contains("managed_no_gl_context", plan, StringComparison.Ordinal);
        Assert.Contains("GL driver 级", plan, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 managed native leak detector 真实输出四类 report/manifest，且可被 native-leak-preflight 进入待审状态。
    /// </summary>
    [Fact]
    public void ManagedNativeLeakDetectorWritesManifestAcceptedByNativeLeakPreflight()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-managed-native-leak-detector-" + Guid.NewGuid().ToString("N"));

        try
        {
            string output = Path.Combine(temp, "detector");
            ScriptResult detector = RunDotNet(
                root,
                "run",
                "--project",
                Path.Combine(root, "tools", "PixelEngine.Tools.ManagedNativeLeakDetector", "PixelEngine.Tools.ManagedNativeLeakDetector.csproj"),
                "-c",
                "Release",
                "--",
                "--output",
                output,
                "--detector-run-id",
                "managed-test",
                "--git-commit",
                "abcdef123456");

            // Assert：验证预期结果
            Assert.Equal(0, detector.ExitCode);
            string manifest = Path.Combine(output, "evidence.json");
            Assert.True(File.Exists(manifest), detector.Output);

            foreach (string scope in new[] { "gl", "openal", "box2d", "alc" })
            {
                string scopeReport = Path.Combine(output, scope + ".md");
                Assert.True(File.Exists(scopeReport), detector.Output);
                string scopeText = File.ReadAllText(scopeReport);
                Assert.Contains("| detector | managed-native-leak-detector |", scopeText, StringComparison.Ordinal);
                Assert.Contains("| detectorRunId | managed-test |", scopeText, StringComparison.Ordinal);
                Assert.Contains("| gitCommit | abcdef123456 |", scopeText, StringComparison.Ordinal);
                Assert.Contains("| conclusion | no_leaks |", scopeText, StringComparison.Ordinal);
                Assert.Contains("| managedProbe | true |", scopeText, StringComparison.Ordinal);
            }

            string manifestText = File.ReadAllText(manifest);
            Assert.Contains("\"schemaVersion\": 1", manifestText, StringComparison.Ordinal);
            Assert.Contains("\"detector\": \"managed-native-leak-detector\"", manifestText, StringComparison.Ordinal);
            Assert.Contains("\"detectorRunId\": \"managed-test\"", manifestText, StringComparison.Ordinal);
            Assert.Contains("\"gitCommit\": \"abcdef123456\"", manifestText, StringComparison.Ordinal);
            Assert.Contains("\"gl\"", manifestText, StringComparison.Ordinal);
            Assert.Contains("\"openal\"", manifestText, StringComparison.Ordinal);
            Assert.Contains("\"box2d\"", manifestText, StringComparison.Ordinal);
            Assert.Contains("\"alc\"", manifestText, StringComparison.Ordinal);
            string glReport = File.ReadAllText(Path.Combine(output, "gl.md"));
            Assert.Contains("still requires external driver-level GL leak evidence", glReport, StringComparison.Ordinal);
            Assert.True(
                glReport.Contains("gl_context_rendering_wrappers", StringComparison.Ordinal) ||
                glReport.Contains("managed_no_gl_context", StringComparison.Ordinal),
                glReport);

            string artifacts = Path.Combine(temp, "preflight");
            ScriptResult preflight = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "native-leak-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts,
                "-AllowBlocked");
            Assert.Equal(0, preflight.ExitCode);
            string preflightReport = File.ReadAllText(Path.Combine(artifacts, "native-leak-preflight.md"));
            Assert.Contains("status | detector_evidence_attached_pending_review", preflightReport, StringComparison.Ordinal);
            Assert.Contains("managed-native-leak-detector", preflightReport, StringComparison.Ordinal);
            Assert.Contains("gl", preflightReport, StringComparison.Ordinal);
            Assert.Contains("openal", preflightReport, StringComparison.Ordinal);
            Assert.Contains("box2d", preflightReport, StringComparison.Ordinal);
            Assert.Contains("alc", preflightReport, StringComparison.Ordinal);
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
    /// 验证 native leak 预检的真实脚本行为：hash 错误被拒绝为 invalid，证据齐全也保持待审非零退出。
    /// </summary>
    [Fact]
    public void NativeLeakPreflightRejectsBadHashesAndKeepsPendingReviewNonZero()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-native-leak-evidence-" + Guid.NewGuid().ToString("N"));

        try
        {
            string goodManifest = CreateNativeLeakEvidenceManifest(temp);
            string badManifest = CreateNativeLeakEvidenceManifest(temp, corruptHashScope: "gl", suffix: "bad");

            string badArtifacts = Path.Combine(temp, "bad-out");
            // Act：执行被测操作
            ScriptResult bad = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "native-leak-preflight.ps1"),
                "-EvidenceManifestPath",
                badManifest,
                "-Artifacts",
                badArtifacts);
            // Assert：验证不变式与预期结果
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
    /// 验证 native leak evidence 不能由不同 detector run 或不同提交的 scope 拼接而成。
    /// </summary>
    [Fact]
    public void NativeLeakPreflightRejectsMixedDetectorRunIds()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-native-leak-mixed-run-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateNativeLeakEvidenceManifest(temp);
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonObject gl = rootNode["scopes"]!["gl"]!.AsObject();
            gl["detectorRunId"] = "run-older-gl";
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "mixed-run-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "native-leak-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "native-leak-preflight.md"));
            Assert.Contains("blocked_invalid_native_leak_evidence", result.Output + report, StringComparison.Ordinal);
            Assert.Contains("evidence scope gl detectorRunId 必须与 manifest detectorRunId 一致", report, StringComparison.Ordinal);
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
    /// 验证单个 detector report 入口也必须有机器可读 no-leaks 覆盖证据，不能任意文本进入 pending review。
    /// </summary>
    [Fact]
    public void NativeLeakPreflightRejectsSingleDetectorReportWithoutMachineReadableCoverage()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-native-leak-single-detector-" + Guid.NewGuid().ToString("N"));

        try
        {
            string badReport = WriteTextEvidence(Path.Combine(temp, "bad-detector.md"), "no leaks, trust me");
            string badArtifacts = Path.Combine(temp, "bad-out");
            // Act：执行被测操作
            ScriptResult bad = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "native-leak-preflight.ps1"),
                "-DetectorName",
                "external-detector",
                "-DetectorReportPath",
                badReport,
                "-Artifacts",
                badArtifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(5, bad.ExitCode);
            string badPreflightReport = File.ReadAllText(Path.Combine(badArtifacts, "native-leak-preflight.md"));
            Assert.Contains("blocked_invalid_native_leak_evidence", bad.Output + badPreflightReport, StringComparison.Ordinal);
            Assert.Contains("单 detector report 缺少 detector 字段", badPreflightReport, StringComparison.Ordinal);
            Assert.DoesNotContain("status | detector_report_attached_pending_review", badPreflightReport, StringComparison.Ordinal);

            string goodReport = WriteMarkdownEvidence(
                Path.Combine(temp, "good-detector.md"),
                new Dictionary<string, string>
                {
                    ["detector"] = "external-detector",
                    ["conclusion"] = "no_leaks",
                    ["scopes"] = "GL; OpenAL; Box2D; ALC",
                    ["glObjectsLiveAfterShutdown"] = "0",
                    ["openAlObjectsLiveAfterShutdown"] = "0",
                    ["box2DBodiesLiveAfterShutdown"] = "0",
                    ["alcLoadContextsAliveAfterUnload"] = "0",
                });
            string goodArtifacts = Path.Combine(temp, "good-out");
            ScriptResult good = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "native-leak-preflight.ps1"),
                "-DetectorName",
                "external-detector",
                "-DetectorReportPath",
                goodReport,
                "-Artifacts",
                goodArtifacts);

            Assert.Equal(2, good.ExitCode);
            string goodPreflightReport = File.ReadAllText(Path.Combine(goodArtifacts, "native-leak-preflight.md"));
            Assert.Contains("detector_report_attached_pending_review", good.Output + goodPreflightReport, StringComparison.Ordinal);
            Assert.Contains("Machine-readable no-leaks coverage", goodPreflightReport, StringComparison.Ordinal);
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
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-native-leak-schema-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateNativeLeakEvidenceManifest(temp);
            string json = File.ReadAllText(manifest).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);
            File.WriteAllText(manifest, json);

            string artifacts = Path.Combine(temp, "schema-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "native-leak-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
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
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "native-leak-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
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
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "native-leak-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
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
                    ["detectorRunId"] = "run-20260704-native-001",
                    ["gitCommit"] = "abcdef123456",
                    ["conclusion"] = "leaks_detected",
                });
            gl["sha256"] = GetSha256(report);
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "bad-conclusion-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "native-leak-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
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
                    ["detectorRunId"] = "run-20260704-native-001",
                    ["gitCommit"] = "abcdef123456",
                    ["conclusion"] = "no_leaks",
                    ["glObjectsLiveAfterShutdown"] = "1",
                });
            gl["sha256"] = GetSha256(report);
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "live-count-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "native-leak-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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

    // —— GPU 粒子基准证据预检 ——
    public void GpuParticleBenchmarkPreflightRequiresTargetHardwareEvidence()
    {
        // Arrange：准备输入与初始状态
        string script = ReadRepositoryFile("tools", "gpu-particle-benchmark-preflight.ps1");
        string report = ReadRepositoryFile("docs", "runtime-reports", "2026-07-02-particle-frame-probe.md");
        string plan = ReadRepositoryFile("plan", "09-gpu-compute.md");
        string readme = ReadRepositoryFile("plan", "tasks", "70-evidence-contracts.md");

        // Assert：验证预期结果
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
        Assert.Contains("benchmarkRunId", script, StringComparison.Ordinal);
        Assert.Contains("gitCommit 必须与 targetHardwareReport 一致", script, StringComparison.Ordinal);
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
        Assert.Contains("benchmarkRunId", report, StringComparison.Ordinal);
        Assert.Contains("measured_frames", report, StringComparison.Ordinal);
        Assert.Contains("sampleSeconds", report, StringComparison.Ordinal);
        Assert.Contains("local-comparison.md", report, StringComparison.Ordinal);
        Assert.Contains("target_gpu_evidence: false", report, StringComparison.Ordinal);
        Assert.Contains("未知 scope", report, StringComparison.Ordinal);

        Assert.Contains("tools/gpu-particle-benchmark-preflight.ps1", plan, StringComparison.Ordinal);
        Assert.Contains("local_probe_only", plan, StringComparison.Ordinal);
        Assert.Contains("target_gpu_evidence_attached_pending_review", plan, StringComparison.Ordinal);

        Assert.Contains("GPU 粒子长基准", readme, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_target_gpu_evidence", readme, StringComparison.Ordinal);
        Assert.Contains("local_probe_only", readme, StringComparison.Ordinal);
        Assert.Contains("blocked_invalid_local_probe", readme, StringComparison.Ordinal);
        Assert.Contains("target_gpu_evidence_attached_pending_review", readme, StringComparison.Ordinal);
        Assert.Contains("local-comparison.md/json", readme, StringComparison.Ordinal);
        Assert.Contains("local_only: true", readme, StringComparison.Ordinal);
        Assert.Contains("target_gpu_evidence: false", readme, StringComparison.Ordinal);
        Assert.Contains("schemaVersion=1", readme, StringComparison.Ordinal);
        Assert.Contains("targetHardwareReport", readme, StringComparison.Ordinal);
        Assert.Contains("cpuProbeReport", readme, StringComparison.Ordinal);
        Assert.Contains("gpuProbeReport", readme, StringComparison.Ordinal);
        Assert.Contains("comparisonReport", readme, StringComparison.Ordinal);
        Assert.Contains("path + sha256", readme, StringComparison.Ordinal);
        Assert.Contains("targetGpuName", readme, StringComparison.Ordinal);
        Assert.Contains("targetGpuDriver", readme, StringComparison.Ordinal);
        Assert.Contains("gpuBackend", readme, StringComparison.Ordinal);
        Assert.Contains("particleCount", readme, StringComparison.Ordinal);
        Assert.Contains("benchmarkRunId", readme, StringComparison.Ordinal);
        Assert.Contains("particle_frame_probe source=PixelEngineParticleFrameProbe", readme, StringComparison.Ordinal);
        Assert.Contains("benchmark_run_id", readme, StringComparison.Ordinal);
        Assert.Contains("requested_count=active_count>=100000", readme, StringComparison.Ordinal);
        Assert.Contains("measured_frames>=300", readme, StringComparison.Ordinal);
        Assert.Contains("gpuFasterThanCpu: true", readme, StringComparison.Ordinal);
        Assert.Contains("cpuWallAvgMs>gpuWallAvgMs", readme, StringComparison.Ordinal);
        Assert.Contains("speedupRatio>1", readme, StringComparison.Ordinal);
        Assert.Contains("sampleSeconds>=10", readme, StringComparison.Ordinal);
        Assert.Contains("不能解除 `PERF-010`", readme, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 GPU 粒子目标硬件预检的真实脚本行为：缺 scope 被拒绝，证据齐全也保持待审非零退出。
    /// </summary>
    [Fact]
    public void GpuParticleBenchmarkPreflightRejectsMissingScopesAndKeepsPendingReviewNonZero()
    {
        // Arrange：搭建测试场景与依赖
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
            // Act：执行被测操作
            ScriptResult bad = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "gpu-particle-benchmark-preflight.ps1"),
                "-EvidenceManifestPath",
                badManifest,
                "-Artifacts",
                badArtifacts);
            // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-gpu-particle-local-fail-" + Guid.NewGuid().ToString("N"));

        try
        {
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "gpu-particle-benchmark-preflight.ps1"),
                "-RunProbe",
                "-Project",
                Path.Combine(temp, "missing.csproj"),
                "-Artifacts",
                temp);

            // Assert：验证不变式与预期结果
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
    /// 验证本机 GPU 粒子 probe 成功时仍只产出 local_probe_only，并显式写出非目标硬件证据的对比报告。
    /// </summary>
    [Fact]
    public void GpuParticleBenchmarkPreflightWritesLocalComparisonForSuccessfulFakeProbe()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-gpu-particle-local-success-" + Guid.NewGuid().ToString("N"));

        try
        {
            _ = Directory.CreateDirectory(temp);
            string fakeDotNet = Path.Combine(temp, "fake-dotnet.cmd");
            File.WriteAllText(
                fakeDotNet,
                """
                @echo off
                set mode=cpu
                :args
                if "%~1"=="" goto done
                if "%~1"=="--particle-render-mode" set mode=%~2
                shift
                goto args
                :done
                if /I "%mode%"=="gpu" (
                  echo particle_frame_probe source=PixelEngineParticleFrameProbe, benchmark_run_id=local-preflight, mode=gpu, gpu_available=True, requested_count=100000, active_count=100000, warmup_frames=1, measured_frames=3, sample_seconds=1.0, wall_avg_ms=1.000, wall_p50_ms=1.000, wall_p95_ms=1.000, wall_max_ms=1.000, particle_stamp_avg_ms=0.000, particle_stamp_p50_ms=0.000, particle_stamp_p95_ms=0.000, particle_stamp_max_ms=0.000, gpu_particle_avg_ms=0.400, gpu_particle_p50_ms=0.400, gpu_particle_p95_ms=0.400, gpu_particle_max_ms=0.400, gpu_upload_avg_ms=0.000, gpu_upload_p50_ms=0.000, gpu_upload_p95_ms=0.000, gpu_upload_max_ms=0.000, lighting_avg_ms=0.000, lighting_p50_ms=0.000, lighting_p95_ms=0.000, lighting_max_ms=0.000, bloom_avg_ms=0.000, bloom_p50_ms=0.000, bloom_p95_ms=0.000, bloom_max_ms=0.000, present_avg_ms=0.000, present_p50_ms=0.000, present_p95_ms=0.000, present_max_ms=0.000
                ) else (
                  echo particle_frame_probe source=PixelEngineParticleFrameProbe, benchmark_run_id=local-preflight, mode=cpu, gpu_available=False, requested_count=100000, active_count=100000, warmup_frames=1, measured_frames=3, sample_seconds=1.0, wall_avg_ms=2.000, wall_p50_ms=2.000, wall_p95_ms=2.000, wall_max_ms=2.000, particle_stamp_avg_ms=0.800, particle_stamp_p50_ms=0.800, particle_stamp_p95_ms=0.800, particle_stamp_max_ms=0.800, gpu_particle_avg_ms=0.000, gpu_particle_p50_ms=0.000, gpu_particle_p95_ms=0.000, gpu_particle_max_ms=0.000, gpu_upload_avg_ms=0.000, gpu_upload_p50_ms=0.000, gpu_upload_p95_ms=0.000, gpu_upload_max_ms=0.000, lighting_avg_ms=0.000, lighting_p50_ms=0.000, lighting_p95_ms=0.000, lighting_max_ms=0.000, bloom_avg_ms=0.000, bloom_p50_ms=0.000, bloom_p95_ms=0.000, bloom_max_ms=0.000, present_avg_ms=0.000, present_p50_ms=0.000, present_p95_ms=0.000, present_max_ms=0.000
                )
                exit /b 0
                """);

            string blockedArtifacts = Path.Combine(temp, "blocked");
            // Act：执行被测操作
            ScriptResult blocked = RunPowerShellScript(
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
                blockedArtifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(2, blocked.ExitCode);
            string blockedReport = File.ReadAllText(Path.Combine(blockedArtifacts, "gpu-particle-benchmark-preflight.md"));
            Assert.Contains("status: local_probe_only", blocked.Output + blockedReport, StringComparison.Ordinal);
            Assert.Contains("local_comparison_markdown", blockedReport, StringComparison.Ordinal);
            Assert.Contains("local_gpu_particle_draw_faster: true", blockedReport, StringComparison.Ordinal);
            Assert.Contains("GPU particle benchmark preflight failed: local_probe_only", blocked.Output, StringComparison.Ordinal);

            string comparisonMarkdown = File.ReadAllText(Path.Combine(blockedArtifacts, "local-comparison.md"));
            Assert.Contains("local_only: true", comparisonMarkdown, StringComparison.Ordinal);
            Assert.Contains("target_gpu_evidence: false", comparisonMarkdown, StringComparison.Ordinal);
            Assert.Contains("local_gpu_particle_draw_faster: true", comparisonMarkdown, StringComparison.Ordinal);
            Assert.Contains("local_gpu_wall_time_faster: true", comparisonMarkdown, StringComparison.Ordinal);
            Assert.DoesNotContain("gpuFasterThanCpu: true", comparisonMarkdown, StringComparison.Ordinal);

            JsonObject comparisonJson = JsonNode.Parse(File.ReadAllText(Path.Combine(blockedArtifacts, "local-comparison.json")))!.AsObject();
            Assert.True(comparisonJson["localOnly"]!.GetValue<bool>());
            Assert.False(comparisonJson["targetGpuEvidence"]!.GetValue<bool>());
            Assert.True(comparisonJson["local_gpu_particle_draw_faster"]!.GetValue<bool>());
            Assert.True(comparisonJson["local_gpu_wall_time_faster"]!.GetValue<bool>());
            Assert.Equal(100000, comparisonJson["cpu_active_count"]!.GetValue<int>());
            Assert.Equal(100000, comparisonJson["gpu_active_count"]!.GetValue<int>());
            Assert.Equal(3, comparisonJson["cpu_measured_frames"]!.GetValue<int>());
            Assert.Equal(3, comparisonJson["gpu_measured_frames"]!.GetValue<int>());

            string allowedArtifacts = Path.Combine(temp, "allowed");
            ScriptResult allowed = RunPowerShellScript(
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
                allowedArtifacts,
                "-AllowBlocked");

            Assert.Equal(0, allowed.ExitCode);
            string allowedReport = File.ReadAllText(Path.Combine(allowedArtifacts, "gpu-particle-benchmark-preflight.md"));
            Assert.Contains("status: local_probe_only", allowed.Output + allowedReport, StringComparison.Ordinal);
            Assert.Contains("local_only: true", File.ReadAllText(Path.Combine(allowedArtifacts, "local-comparison.md")), StringComparison.Ordinal);
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
        // Arrange：搭建测试场景与依赖
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
                echo particle_frame_probe source=PixelEngineParticleFrameProbe, benchmark_run_id=local-preflight, mode=cpu, gpu_available=False, requested_count=100000, active_count=100000, warmup_frames=1, measured_frames=3, sample_seconds=1.0, wall_avg_ms=1.000, wall_p50_ms=1.000, wall_p95_ms=1.000, wall_max_ms=1.000, particle_stamp_avg_ms=0.800, particle_stamp_p50_ms=0.800, particle_stamp_p95_ms=0.800, particle_stamp_max_ms=0.800, gpu_particle_avg_ms=0.000, gpu_particle_p50_ms=0.000, gpu_particle_p95_ms=0.000, gpu_particle_max_ms=0.000, gpu_upload_avg_ms=0.000, gpu_upload_p50_ms=0.000, gpu_upload_p95_ms=0.000, gpu_upload_max_ms=0.000, lighting_avg_ms=0.000, lighting_p50_ms=0.000, lighting_p95_ms=0.000, lighting_max_ms=0.000, bloom_avg_ms=0.000, bloom_p50_ms=0.000, bloom_p95_ms=0.000, bloom_max_ms=0.000, present_avg_ms=0.000, present_p50_ms=0.000, present_p95_ms=0.000, present_max_ms=0.000
                exit /b 0
                """);

            // Act：执行被测操作
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

            // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-gpu-particle-comparison-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateGpuParticleEvidenceManifest(temp, suffix: "bad-comparison");
            SetFlatEvidenceFileContent(
                manifest,
                "comparisonReport",
                """
                benchmarkRunId: run-20260703-target-gpu
                gitCommit: abcdef123456
                gpuFasterThanCpu: false
                cpuWallAvgMs: 6.4
                gpuWallAvgMs: 3.2
                speedupRatio: 2.0
                measuredFrames: 600
                sampleSeconds: 20.0
                """);

            string artifacts = Path.Combine(temp, "bad-comparison-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "gpu-particle-benchmark-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
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
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "gpu-particle-benchmark-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
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
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "gpu-particle-benchmark-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-gpu-particle-short-sample-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateGpuParticleEvidenceManifest(temp, suffix: "bad-sample", sampleSeconds: 5.0);

            string artifacts = Path.Combine(temp, "bad-sample-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "gpu-particle-benchmark-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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
    /// 验证 comparisonReport 的 sampleSeconds 不能只靠手写声明，必须由 CPU/GPU probe summary 的真实采样秒数支撑。
    /// </summary>
    [Fact]
    public void GpuParticleBenchmarkPreflightRejectsComparisonSampleSecondsNotBackedByProbeSummaries()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-gpu-particle-sample-backed-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateGpuParticleEvidenceManifest(
                temp,
                suffix: "bad-sample-backed",
                sampleSeconds: 20.0,
                probeSampleSeconds: 2.0);

            string artifacts = Path.Combine(temp, "bad-sample-backed-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "gpu-particle-benchmark-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "gpu-particle-benchmark-preflight.md"));
            Assert.Contains("status: blocked_invalid_target_gpu_evidence", report, StringComparison.Ordinal);
            Assert.Contains("comparisonReport sampleSeconds 不能大于 cpu/gpu probe summary 的 sample_seconds", report, StringComparison.Ordinal);
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
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-gpu-particle-hardware-fields-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateGpuParticleEvidenceManifest(temp, suffix: "bad-hardware");
            SetFlatEvidenceFileContent(manifest, "targetHardwareReport", "targetGpuName: Test GPU");

            string artifacts = Path.Combine(temp, "bad-hardware-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "gpu-particle-benchmark-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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
    /// 验证目标 GPU 硬件、CPU probe、GPU probe 与 comparison 证据必须来自同一个 benchmark run 和 git commit。
    /// </summary>
    [Fact]
    public void GpuParticleBenchmarkPreflightRejectsMismatchedTargetEvidenceIdentity()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-gpu-particle-identity-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateGpuParticleEvidenceManifest(temp, suffix: "bad-identity");
            SetFlatEvidenceFileContent(
                manifest,
                "gpuProbeReport",
                """
                gitCommit: different-commit
                particle_frame_probe source=PixelEngineParticleFrameProbe, benchmark_run_id=run-20260703-target-gpu, mode=gpu, gpu_available=True, requested_count=100000, active_count=100000, warmup_frames=60, measured_frames=600, sample_seconds=20.0, wall_avg_ms=3.200, wall_p50_ms=3.000, wall_p95_ms=4.000, wall_max_ms=5.000, particle_stamp_avg_ms=0.000, gpu_particle_avg_ms=0.900, gpu_particle_p50_ms=0.850, gpu_particle_p95_ms=1.050, gpu_particle_max_ms=1.200
                """);

            string artifacts = Path.Combine(temp, "bad-identity-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "gpu-particle-benchmark-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "gpu-particle-benchmark-preflight.md"));
            Assert.Contains("status: blocked_invalid_target_gpu_evidence", report, StringComparison.Ordinal);
            Assert.Contains("gpuProbeReport gitCommit 必须与 targetHardwareReport 一致", report, StringComparison.Ordinal);
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
        // Arrange：搭建测试场景与依赖
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
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "gpu-particle-benchmark-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-gpu-particle-probe-frames-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateGpuParticleEvidenceManifest(temp, suffix: "bad-frames", measuredFrames: 120);

            string artifacts = Path.Combine(temp, "bad-frames-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "gpu-particle-benchmark-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-gpu-particle-bad-hash-" + Guid.NewGuid().ToString("N"));

        try
        {
            string badManifest = CreateGpuParticleEvidenceManifest(temp, suffix: "bad-hash");
            string json = File.ReadAllText(badManifest);
            json = json.Replace("\"sha256\": \"", "\"sha256\": \"0000", StringComparison.Ordinal);
            File.WriteAllText(badManifest, json);

            string badArtifacts = Path.Combine(temp, "bad-hash-out");
            // Act：执行被测操作
            ScriptResult bad = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "gpu-particle-benchmark-preflight.ps1"),
                "-EvidenceManifestPath",
                badManifest,
                "-Artifacts",
                badArtifacts);

            // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
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
            // Act：执行被测操作
            ScriptResult unknown = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "gpu-particle-benchmark-preflight.ps1"),
                "-EvidenceManifestPath",
                unknownManifest,
                "-Artifacts",
                unknownArtifacts);
            // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-gpu-particle-schema-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateGpuParticleEvidenceManifest(temp, suffix: "schema");
            string json = File.ReadAllText(manifest).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);
            File.WriteAllText(manifest, json);

            string artifacts = Path.Combine(temp, "schema-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "gpu-particle-benchmark-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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

    // —— Demo 人工验收证据预检 ——
    public void DemoManualAcceptancePreflightRequiresHumanEvidence()
    {
        // Arrange：准备输入与初始状态
        string script = ReadRepositoryFile("tools", "demo-manual-acceptance-preflight.ps1");
        string report = ReadRepositoryFile("docs", "runtime-reports", "2026-07-02-demo-manual-acceptance.md");
        string plan = ReadRepositoryFile("plan", "13-demo-game.md");
        string readme = ReadRepositoryFile("plan", "tasks", "70-evidence-contracts.md");
        string hostingPlan = ReadRepositoryFile("plan", "18-hosting-runtime.md");

        // Assert：验证预期结果
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
        Assert.Contains("editor-window", script, StringComparison.Ordinal);
        Assert.Contains("EditorShellProject", script, StringComparison.Ordinal);
        Assert.Contains("Invoke-EditorShellProbe", script, StringComparison.Ordinal);
        Assert.Contains("apps/PixelEngine.Editor.Shell/PixelEngine.Editor.Shell.csproj", script, StringComparison.Ordinal);
        Assert.DoesNotContain("--editor", script, StringComparison.Ordinal);
        Assert.Contains("scenes/lava-mine.scene", script, StringComparison.Ordinal);
        Assert.Contains("scenes/lava-mine-goal-probe.scene", script, StringComparison.Ordinal);
        Assert.Contains("scenes/lava-mine-health-probe.scene", script, StringComparison.Ordinal);
        Assert.Contains("scenes/lava-mine-camera-probe.scene", script, StringComparison.Ordinal);
        Assert.Contains("scenes/lava-mine-reaction-probe.scene", script, StringComparison.Ordinal);
        Assert.Contains("scenes/lava-mine-audio-probe.scene", script, StringComparison.Ordinal);
        Assert.Contains("scenes/lava-mine-particle-light-probe.scene", script, StringComparison.Ordinal);
        Assert.Contains("RequiredSummaryMarkers", script, StringComparison.Ordinal);
        Assert.Contains("ConvertFrom-ScriptedProbeSummary", script, StringComparison.Ordinal);
        Assert.Contains("Assert-ScriptedProbeSummarySemantics", script, StringComparison.Ordinal);
        Assert.Contains("Assert-ScriptedProbeNumberAtLeast", script, StringComparison.Ordinal);
        Assert.Contains("Assert-ScriptedProbeBoolean", script, StringComparison.Ordinal);
        Assert.Contains("Assert-ScriptedProbeRangeWidthAtLeast", script, StringComparison.Ordinal);
        Assert.Contains("player_visual=present", script, StringComparison.Ordinal);
        Assert.Contains("playable_shots=", script, StringComparison.Ordinal);
        Assert.Contains("720x480", script, StringComparison.Ordinal);
        Assert.Contains("particles=", script, StringComparison.Ordinal);
        Assert.Contains("transient_bursts=", script, StringComparison.Ordinal);
        Assert.Contains("max_transient_bursts=", script, StringComparison.Ordinal);
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
        Assert.Contains("editor_enabled=True", script, StringComparison.Ordinal);
        Assert.Contains("editor_running=True", script, StringComparison.Ordinal);
        Assert.Contains("editor_bridge_frames=", script, StringComparison.Ordinal);
        Assert.Contains("editor_panels=", script, StringComparison.Ordinal);
        Assert.Contains("playable_shots", script, StringComparison.Ordinal);
        Assert.Contains("player_ground_samples", script, StringComparison.Ordinal);
        Assert.Contains("audio_probe_max_dropped", script, StringComparison.Ordinal);
        Assert.Contains("particle_light_probe_spawned", script, StringComparison.Ordinal);

        Assert.Contains("controlFeelReport", script, StringComparison.Ordinal);
        Assert.Contains("materialBrushAndReactionVideo", script, StringComparison.Ordinal);
        Assert.Contains("rigidBodyGameplayVideo", script, StringComparison.Ordinal);
        Assert.Contains("particleLightingVideo", script, StringComparison.Ordinal);
        Assert.Contains("audioListeningReport", script, StringComparison.Ordinal);
        Assert.Contains("fullRoutePlaythroughVideo", script, StringComparison.Ordinal);
        Assert.Contains("lavaCombatPlaythroughVideo", script, StringComparison.Ordinal);
        Assert.Contains("hudMenuEditorVideo", script, StringComparison.Ordinal);
        Assert.Contains("hotReloadWindowReport", script, StringComparison.Ordinal);
        Assert.Contains("minDurationSeconds", script, StringComparison.Ordinal);
        Assert.Contains("Assert-VideoEvidence", script, StringComparison.Ordinal);
        Assert.Contains("Assert-Mp4VideoEvidence", script, StringComparison.Ordinal);
        Assert.Contains("Read-Mp4MovieInfo", script, StringComparison.Ordinal);
        Assert.Contains("ftyp", script, StringComparison.Ordinal);
        Assert.Contains("moov", script, StringComparison.Ordinal);
        Assert.Contains("mdat", script, StringComparison.Ordinal);
        Assert.Contains("视频 track", script, StringComparison.Ordinal);
        Assert.Contains("EBML", script, StringComparison.Ordinal);
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
            "playerPackageStandaloneRun",
            "lavaDamageObserved",
            "grenadeLargeTerrainEdit",
            "obstacleDemolitionRoute",
            "webFirstResultRestart",
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
        Assert.Contains("reviewSessionId", script, StringComparison.Ordinal);
        Assert.Contains("gitCommit", script, StringComparison.Ordinal);
        Assert.Contains("必须为 $reviewSessionId", script, StringComparison.Ordinal);
        Assert.Contains("必须为 $gitCommit", script, StringComparison.Ordinal);
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
        Assert.Contains("语义阈值", report, StringComparison.Ordinal);
        Assert.Contains("editor-window", report, StringComparison.Ordinal);
        Assert.Contains("editor_enabled=True", report, StringComparison.Ordinal);
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

        Assert.Contains("tools/demo-manual-acceptance-preflight.ps1", readme, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_manual_evidence", readme, StringComparison.Ordinal);
        Assert.Contains("scripted_probe_only", readme, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_manual_scope_evidence", readme, StringComparison.Ordinal);
        Assert.Contains("blocked_invalid_manual_evidence", readme, StringComparison.Ordinal);
        Assert.Contains("manual_evidence_attached_pending_review", readme, StringComparison.Ordinal);
        Assert.Contains("同一 `reviewSessionId` / `gitCommit`", readme, StringComparison.Ordinal);
        Assert.Contains("reviewer/capturedAt/notes/checklist/criteria", readme, StringComparison.Ordinal);
        Assert.Contains("video scope", readme, StringComparison.Ordinal);
        Assert.Contains("durationSeconds", readme, StringComparison.Ordinal);
        Assert.Contains("视频结构", readme, StringComparison.Ordinal);
        Assert.Contains("真实 duration", readme, StringComparison.Ordinal);
        Assert.Contains("controlFeelReport", readme, StringComparison.Ordinal);
        Assert.Contains("materialBrushAndReactionVideo", readme, StringComparison.Ordinal);
        Assert.Contains("rigidBodyGameplayVideo", readme, StringComparison.Ordinal);
        Assert.Contains("particleLightingVideo", readme, StringComparison.Ordinal);
        Assert.Contains("audioListeningReport", readme, StringComparison.Ordinal);
        Assert.Contains("fullRoutePlaythroughVideo", readme, StringComparison.Ordinal);
        Assert.Contains("lavaCombatPlaythroughVideo", readme, StringComparison.Ordinal);
        Assert.Contains("hudMenuEditorVideo", readme, StringComparison.Ordinal);
        Assert.Contains("hotReloadWindowReport", readme, StringComparison.Ordinal);
        Assert.Contains("手感", readme, StringComparison.Ordinal);
        Assert.Contains("横向熔岩战斗", readme, StringComparison.Ordinal);
        Assert.Contains("HUD/菜单/EditorShell", readme, StringComparison.Ordinal);
        Assert.Contains("至少 30 秒", readme, StringComparison.Ordinal);
        Assert.Contains("其它视频至少 10 秒", readme, StringComparison.Ordinal);
        Assert.Contains("scripted probe 只可辅助生成 `scripted_probe_only`", readme, StringComparison.Ordinal);
        Assert.Contains("不能替代真实窗口人工体验结论", readme, StringComparison.Ordinal);
        Assert.Contains("capture_unique_visible_pixels", readme, StringComparison.Ordinal);
        Assert.Contains("拒绝空白/纯色画面", readme, StringComparison.Ordinal);

        Assert.Contains("tools/demo-manual-acceptance-preflight.ps1", hostingPlan, StringComparison.Ordinal);
        Assert.Contains("capture.bmp", hostingPlan, StringComparison.Ordinal);
        Assert.Contains("hudMenuEditorVideo", hostingPlan, StringComparison.Ordinal);
        Assert.Contains("editor-window", hostingPlan, StringComparison.Ordinal);
        Assert.Contains("editor_enabled", hostingPlan, StringComparison.Ordinal);
        Assert.Contains("editor_bridge_frames", hostingPlan, StringComparison.Ordinal);
        Assert.Contains("criteria", hostingPlan, StringComparison.Ordinal);
        Assert.Contains("hudReadable", hostingPlan, StringComparison.Ordinal);
        Assert.Contains("menuButtonsClicked", hostingPlan, StringComparison.Ordinal);
        Assert.Contains("editorDockspaceOpened", hostingPlan, StringComparison.Ordinal);
        Assert.Contains("manual_evidence_attached_pending_review", hostingPlan, StringComparison.Ordinal);
        Assert.Contains("- [x] 过载降级按五级顺序触发", hostingPlan, StringComparison.Ordinal);
        Assert.Contains("- [!] Editor 真实窗口观测/覆盖仍缺人工复核证据", hostingPlan, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 scripted probe 摘要不仅检查字段存在，还会拒绝关键数值不达标的机器证据。
    /// </summary>
    [Fact]
    public void DemoManualAcceptancePreflightRejectsScriptedProbeSummaryBelowSemanticThreshold()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-demo-manual-summary-semantics-" + Guid.NewGuid().ToString("N"));

        try
        {
            _ = Directory.CreateDirectory(temp);
            string source = ReadRepositoryFile("tools", "demo-manual-acceptance-preflight.ps1");
            int start = source.IndexOf("function ConvertFrom-ScriptedProbeSummary", StringComparison.Ordinal);
            int end = source.IndexOf("function Invoke-ScriptedProbe", StringComparison.Ordinal);
            // Assert：验证预期结果
            Assert.True(start >= 0);
            Assert.True(end > start);
            string harness = string.Concat(
                "$ErrorActionPreference='Stop'\n",
                source[start..end],
                "\n$good = '脚本化窗口输入摘要：frames=240, playable_shots=2, max_particles=316, transient_bursts=0, max_transient_bursts=4, frame_samples=240, camera_samples=239, player_ground_samples=105, player_air_samples=134, player_left_ground=True, player_air_control=True, camera_followed=True, render_camera_synced=True。'\n",
                "$values = ConvertFrom-ScriptedProbeSummary -Summary $good\n",
                "Assert-ScriptedProbeSummarySemantics -Name 'playable-world' -Values $values\n",
                "$goodEditor = '脚本化窗口输入摘要：frames=180, frame_samples=120, editor_enabled=True, editor_running=True, editor_panels=12, editor_bridge_frames=120, render_camera_synced=True, scripted_play_entered=True, scripted_play_exited=True, scripted_scene_saved=True, scripted_project_closed=True, scripted_project_reopened=True。'\n",
                "Assert-ScriptedProbeSummarySemantics -Name 'editor-window' -Values (ConvertFrom-ScriptedProbeSummary -Summary $goodEditor)\n",
                "$bad = '脚本化窗口输入摘要：frames=240, playable_shots=0, max_particles=316, transient_bursts=0, max_transient_bursts=4, frame_samples=240, camera_samples=239, player_ground_samples=105, player_air_samples=134, player_left_ground=True, player_air_control=True, camera_followed=True, render_camera_synced=True。'\n",
                "try {\n",
                "  Assert-ScriptedProbeSummarySemantics -Name 'playable-world' -Values (ConvertFrom-ScriptedProbeSummary -Summary $bad)\n",
                "  throw 'expected semantic failure missing'\n",
                "} catch {\n",
                "  if ($_.Exception.Message -notlike '*playable_shots*') { throw }\n",
                "  Write-Output $_.Exception.Message\n",
                "}\n",
                "$badTransient = '脚本化窗口输入摘要：frames=240, playable_shots=2, max_particles=316, transient_bursts=2, max_transient_bursts=4, frame_samples=240, camera_samples=239, player_ground_samples=105, player_air_samples=134, player_left_ground=True, player_air_control=True, camera_followed=True, render_camera_synced=True。'\n",
                "try {\n",
                "  Assert-ScriptedProbeSummarySemantics -Name 'playable-world' -Values (ConvertFrom-ScriptedProbeSummary -Summary $badTransient)\n",
                "  throw 'expected transient burst failure missing'\n",
                "} catch {\n",
                "  if ($_.Exception.Message -notlike '*transient_bursts*') { throw }\n",
                "  Write-Output $_.Exception.Message\n",
                "}\n",
                "$badEditor = '脚本化窗口输入摘要：frames=180, frame_samples=120, editor_enabled=True, editor_running=False, editor_panels=12, editor_bridge_frames=120, render_camera_synced=True, scripted_play_entered=True, scripted_play_exited=True, scripted_scene_saved=True, scripted_project_closed=True, scripted_project_reopened=True。'\n",
                "try {\n",
                "  Assert-ScriptedProbeSummarySemantics -Name 'editor-window' -Values (ConvertFrom-ScriptedProbeSummary -Summary $badEditor)\n",
                "  throw 'expected editor semantic failure missing'\n",
                "} catch {\n",
                "  if ($_.Exception.Message -notlike '*editor_running*') { throw }\n",
                "  Write-Output $_.Exception.Message\n",
                "}\n");
            string harnessPath = Path.Combine(temp, "summary-semantics-harness.ps1");
            File.WriteAllText(harnessPath, harness);

            ScriptResult result = RunPowerShellScript(root, harnessPath);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("playable_shots", result.Output, StringComparison.Ordinal);
            Assert.Contains("transient_bursts", result.Output, StringComparison.Ordinal);
            Assert.Contains("editor_running", result.Output, StringComparison.Ordinal);
            Assert.Contains("必须 >=", result.Output, StringComparison.Ordinal);
            Assert.Contains("必须 <", result.Output, StringComparison.Ordinal);
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
    /// 验证 scripted probe 截图证据会拒绝纯黑 BMP，避免黑屏截图进入机器 probe 报告。
    /// </summary>
    [Fact]
    public void DemoManualAcceptancePreflightRejectsBlankScriptedProbeCapture()
    {
        // Arrange：准备输入与初始状态
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
            // Assert：验证预期结果
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
        // Arrange：搭建测试场景与依赖
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
            "lavaCombatPlaythroughVideo",
            "hudMenuEditorVideo",
            "hotReloadWindowReport",
        ];

        try
        {
            string goodManifest = CreateFlatEvidenceManifest(temp, manualScopes, suffix: "good", includeDemoManualMetadata: true);
            string badManifest = CreateFlatEvidenceManifest(temp, manualScopes[..^1], suffix: "bad", includeDemoManualMetadata: true);

            string badArtifacts = Path.Combine(temp, "bad-out");
            // Act：执行被测操作
            ScriptResult bad = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "demo-manual-acceptance-preflight.ps1"),
                "-EvidenceManifestPath",
                badManifest,
                "-Artifacts",
                badArtifacts);
            // Assert：验证不变式与预期结果
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
    /// 验证 Demo 人工验收 evidence 不能由不同 review session 或不同提交的片段拼接而成。
    /// </summary>
    [Fact]
    public void DemoManualAcceptancePreflightRejectsMixedReviewSessionIds()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-demo-manual-mixed-session-" + Guid.NewGuid().ToString("N"));

        string[] manualScopes =
        [
            "controlFeelReport",
            "materialBrushAndReactionVideo",
            "rigidBodyGameplayVideo",
            "particleLightingVideo",
            "audioListeningReport",
            "fullRoutePlaythroughVideo",
            "lavaCombatPlaythroughVideo",
            "hudMenuEditorVideo",
            "hotReloadWindowReport",
        ];

        try
        {
            string manifest = CreateFlatEvidenceManifest(temp, manualScopes, suffix: "mixed-session", includeDemoManualMetadata: true);
            SetFlatEvidenceProperty(manifest, "hudMenuEditorVideo", "reviewSessionId", "session-old-ui");

            string artifacts = Path.Combine(temp, "mixed-session-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "demo-manual-acceptance-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "demo-manual-acceptance-preflight.md"));
            Assert.Contains("status: blocked_invalid_manual_evidence", result.Output + report, StringComparison.Ordinal);
            Assert.Contains("evidence scope hudMenuEditorVideo reviewSessionId 必须为 session-20260704-demo-001，实际为 session-old-ui", report, StringComparison.Ordinal);
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
    /// 验证 Demo 人工验收预检会拒绝无效 metadata，避免空视频或无观察说明冒充人工验收。
    /// </summary>
    [Fact]
    public void DemoManualAcceptancePreflightRejectsInvalidMetadata()
    {
        // Arrange：搭建测试场景与依赖
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
            "lavaCombatPlaythroughVideo",
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
            string badStandaloneManifest = CreateFlatEvidenceManifest(temp, manualScopes, suffix: "bad-standalone", includeDemoManualMetadata: true);
            SetFlatEvidenceProperty(
                badStandaloneManifest,
                "fullRoutePlaythroughVideo",
                "checklist",
                new JsonObject
                {
                    ["routeCompleted"] = true,
                    ["materialsReactionsBodiesShown"] = true,
                    ["audioLightingHudShown"] = true,
                    ["playerPackageStandaloneRun"] = false,
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
            string staleCommitManifest = CreateFlatEvidenceManifest(temp, manualScopes, suffix: "stale-commit", includeDemoManualMetadata: true);
            SetFlatEvidenceManifestProperty(staleCommitManifest, "gitCommit", "abcdef123456");
            string weakReportManifest = CreateFlatEvidenceManifest(temp, manualScopes, suffix: "weak-report", includeDemoManualMetadata: true);
            SetFlatEvidenceFileContent(
                weakReportManifest,
                "controlFeelReport",
                "reviewSessionId: session-20260704-demo-001" + Environment.NewLine +
                "gitCommit: " + GetCurrentGitCommit() + Environment.NewLine +
                "short report without conclusion and risk");

            string badDurationArtifacts = Path.Combine(temp, "bad-duration-out");
            // Act：执行被测操作
            ScriptResult badDuration = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "demo-manual-acceptance-preflight.ps1"),
                "-EvidenceManifestPath",
                badDurationManifest,
                "-Artifacts",
                badDurationArtifacts);
            // Assert：验证不变式与预期结果
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

            string badStandaloneArtifacts = Path.Combine(temp, "bad-standalone-out");
            ScriptResult badStandalone = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "demo-manual-acceptance-preflight.ps1"),
                "-EvidenceManifestPath",
                badStandaloneManifest,
                "-Artifacts",
                badStandaloneArtifacts);
            Assert.Equal(5, badStandalone.ExitCode);
            string badStandaloneReport = File.ReadAllText(Path.Combine(badStandaloneArtifacts, "demo-manual-acceptance-preflight.md"));
            Assert.Contains("status: blocked_invalid_manual_evidence", badStandalone.Output + badStandaloneReport, StringComparison.Ordinal);
            Assert.Contains("fullRoutePlaythroughVideo checklist.playerPackageStandaloneRun 必须为 true", badStandaloneReport, StringComparison.Ordinal);
            Assert.DoesNotContain("status: manual_evidence_attached_pending_review", badStandaloneReport, StringComparison.Ordinal);

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

            string staleCommitArtifacts = Path.Combine(temp, "stale-commit-out");
            ScriptResult staleCommit = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "demo-manual-acceptance-preflight.ps1"),
                "-EvidenceManifestPath",
                staleCommitManifest,
                "-Artifacts",
                staleCommitArtifacts);
            Assert.Equal(5, staleCommit.ExitCode);
            string staleCommitReport = File.ReadAllText(Path.Combine(staleCommitArtifacts, "demo-manual-acceptance-preflight.md"));
            Assert.Contains("status: blocked_invalid_manual_evidence", staleCommit.Output + staleCommitReport, StringComparison.Ordinal);
            Assert.Contains("evidence manifest gitCommit 必须等于当前 HEAD", staleCommitReport, StringComparison.Ordinal);
            Assert.DoesNotContain("status: manual_evidence_attached_pending_review", staleCommitReport, StringComparison.Ordinal);

            string weakReportArtifacts = Path.Combine(temp, "weak-report-out");
            ScriptResult weakReport = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "demo-manual-acceptance-preflight.ps1"),
                "-EvidenceManifestPath",
                weakReportManifest,
                "-Artifacts",
                weakReportArtifacts);
            Assert.Equal(5, weakReport.ExitCode);
            string weakReportReport = File.ReadAllText(Path.Combine(weakReportArtifacts, "demo-manual-acceptance-preflight.md"));
            Assert.Contains("status: blocked_invalid_manual_evidence", weakReport.Output + weakReportReport, StringComparison.Ordinal);
            Assert.Contains("controlFeelReport report 文件太短", weakReportReport, StringComparison.Ordinal);
            Assert.DoesNotContain("status: manual_evidence_attached_pending_review", weakReportReport, StringComparison.Ordinal);
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
    /// 验证 Demo 人工验收 video evidence 会拒绝文本文件改名成 .mp4。
    /// </summary>
    [Fact]
    public void DemoManualAcceptancePreflightRejectsRenamedTextAsVideoEvidence()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-demo-manual-text-video-" + Guid.NewGuid().ToString("N"));

        string[] manualScopes =
        [
            "controlFeelReport",
            "materialBrushAndReactionVideo",
            "rigidBodyGameplayVideo",
            "particleLightingVideo",
            "audioListeningReport",
            "fullRoutePlaythroughVideo",
            "lavaCombatPlaythroughVideo",
            "hudMenuEditorVideo",
            "hotReloadWindowReport",
        ];

        try
        {
            string manifest = CreateFlatEvidenceManifest(temp, manualScopes, suffix: "text-video", includeDemoManualMetadata: true);
            SetFlatEvidenceFileContent(
                manifest,
                "materialBrushAndReactionVideo",
                "this is plain text renamed to mp4");

            string artifacts = Path.Combine(temp, "text-video-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "demo-manual-acceptance-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "demo-manual-acceptance-preflight.md"));
            Assert.Contains("status: blocked_invalid_manual_evidence", result.Output + report, StringComparison.Ordinal);
            Assert.Contains("materialBrushAndReactionVideo video 文件太小，缺少可解析 MP4/MOV 视频结构", report, StringComparison.Ordinal);
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
    /// 验证 Demo 人工验收 video evidence 会拒绝只有 ftyp 的伪 MP4。
    /// </summary>
    [Fact]
    public void DemoManualAcceptancePreflightRejectsFtypOnlyVideoEvidence()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-demo-manual-ftyp-only-video-" + Guid.NewGuid().ToString("N"));

        string[] manualScopes =
        [
            "controlFeelReport",
            "materialBrushAndReactionVideo",
            "rigidBodyGameplayVideo",
            "particleLightingVideo",
            "audioListeningReport",
            "fullRoutePlaythroughVideo",
            "lavaCombatPlaythroughVideo",
            "hudMenuEditorVideo",
            "hotReloadWindowReport",
        ];

        try
        {
            string manifest = CreateFlatEvidenceManifest(temp, manualScopes, suffix: "ftyp-only-video", includeDemoManualMetadata: true);
            SetFlatEvidenceFileBytes(
                manifest,
                "materialBrushAndReactionVideo",
                CreateFtypOnlyMp4Bytes());

            string artifacts = Path.Combine(temp, "ftyp-only-video-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "demo-manual-acceptance-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "demo-manual-acceptance-preflight.md"));
            Assert.Contains("status: blocked_invalid_manual_evidence", result.Output + report, StringComparison.Ordinal);
            Assert.Contains("materialBrushAndReactionVideo video 文件必须包含可解析 moov 视频 track 与正 duration", report, StringComparison.Ordinal);
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
    /// 验证 Demo 人工验收预检会实际拒绝 sha256 不匹配的人工 evidence，并写出可审计报告。
    /// </summary>
    [Fact]
    public void DemoManualAcceptancePreflightRejectsMismatchedEvidenceHash()
    {
        // Arrange：搭建测试场景与依赖
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
            "lavaCombatPlaythroughVideo",
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
            // Act：执行被测操作
            ScriptResult bad = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "demo-manual-acceptance-preflight.ps1"),
                "-EvidenceManifestPath",
                badManifest,
                "-Artifacts",
                badArtifacts);

            // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
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
            "lavaCombatPlaythroughVideo",
            "hudMenuEditorVideo",
            "hotReloadWindowReport",
        ];

        try
        {
            string manifest = CreateFlatEvidenceManifest(temp, manualScopes, suffix: "schema", includeDemoManualMetadata: true);
            string json = File.ReadAllText(manifest).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);
            File.WriteAllText(manifest, json);

            string artifacts = Path.Combine(temp, "schema-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "demo-manual-acceptance-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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

    // —— CI 矩阵证据预检 ——
    public void CiMatrixEvidencePreflightRequiresWorkflowRunEvidence()
    {
        // Arrange：准备输入与初始状态
        string script = ReadRepositoryFile("tools", "ci-matrix-evidence-preflight.ps1");
        string ci = ReadRepositoryFile(".github", "workflows", "ci.yml");
        string report = ReadRepositoryFile("docs", "benchmark-reports", "2026-07-02-ci-matrix-evidence.md");
        string plan = ReadRepositoryFile("plan", "14-testing-benchmarking.md");
        string readme = ReadRepositoryFile("plan", "tasks", "70-evidence-contracts.md");

        // Assert：验证预期结果
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
        Assert.Contains("Add-ManifestStringCheck", script, StringComparison.Ordinal);
        Assert.Contains("$Scope $Field 必须为 $Expected", script, StringComparison.Ordinal);
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
        Assert.Contains("| workflow | CI |", ci, StringComparison.Ordinal);
        Assert.Contains("| event | ${{ github.event_name }} |", ci, StringComparison.Ordinal);
        Assert.Contains("| run_attempt | ${{ github.run_attempt }} |", ci, StringComparison.Ordinal);
        Assert.Contains("sha256 = Get-Sha256", ci, StringComparison.Ordinal);
        Assert.Contains("build-test-win-arm64.md", ci, StringComparison.Ordinal);
        Assert.Contains("testsRan = $false", ci, StringComparison.Ordinal);
        Assert.Contains("runner = 'ubuntu-24.04-arm'", ci, StringComparison.Ordinal);
        Assert.Contains("| runner | ${{ matrix.runner }} |", ci, StringComparison.Ordinal);
        Assert.Contains("verify-publish-osx-arm64.md", ci, StringComparison.Ordinal);

        Assert.Contains("tools/ci-matrix-evidence-preflight.ps1", report, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_ci_manifest", report, StringComparison.Ordinal);
        Assert.Contains("blocked_invalid_ci_evidence", report, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_ci_scope_evidence", report, StringComparison.Ordinal);
        Assert.Contains("ci_matrix_evidence_attached_pending_review", report, StringComparison.Ordinal);
        Assert.Contains("conclusion", report, StringComparison.Ordinal);
        Assert.Contains("channels: r2r,aot", report, StringComparison.Ordinal);
        Assert.Contains("runner", report, StringComparison.Ordinal);
        Assert.Contains("workflow", report, StringComparison.Ordinal);
        Assert.Contains("run_attempt", report, StringComparison.Ordinal);
        Assert.Contains("push/pull_request", report, StringComparison.Ordinal);
        Assert.Contains("同一个 GitHub Actions run", report, StringComparison.Ordinal);

        Assert.Contains("manifest 必须包含 `workflow_run`、`benchmark_guard`、6 RID `buildTest/<rid>` 与 4 RID `verifyPublish/<rid>` scope", readme, StringComparison.Ordinal);
        Assert.Contains("逐项提供 path + sha256", readme, StringComparison.Ordinal);
        Assert.Contains("`workflow=CI`", readme, StringComparison.Ordinal);
        Assert.Contains("`event=push` 或 `pull_request`", readme, StringComparison.Ordinal);
        Assert.Contains("`run_attempt>=1`", readme, StringComparison.Ordinal);
        Assert.Contains("`ref=refs/heads/*` 或 `refs/pull/*`", readme, StringComparison.Ordinal);
        Assert.Contains("`run_id` / `sha` 同源", readme, StringComparison.Ordinal);
        Assert.Contains("`benchmark_guard` 必须在 `windows-latest` 上 `conclusion=success`", readme, StringComparison.Ordinal);
        Assert.Contains("`win-x64` / `win-arm64` / `linux-x64` / `linux-arm64` / `osx-x64` / `osx-arm64`", readme, StringComparison.Ordinal);
        Assert.Contains("`windows-latest` / `ubuntu-latest` / `ubuntu-24.04-arm` / `macos-15-intel` / `macos-14`", readme, StringComparison.Ordinal);
        Assert.Contains("`testsRan` 必须显式存在", readme, StringComparison.Ordinal);
        Assert.Contains("`win-arm64` 当前为 `build_only=true` / `tests_ran=false`", readme, StringComparison.Ordinal);
        Assert.Contains("其余可测 RID 必须 `tests_ran=true`", readme, StringComparison.Ordinal);
        Assert.Contains("publish verify scope 覆盖 `win-x64` / `linux-x64` / `osx-x64` / `osx-arm64`", readme, StringComparison.Ordinal);
        Assert.Contains("`channels=r2r,aot` 且 `conclusion=success`", readme, StringComparison.Ordinal);
        Assert.Contains("仍需人工确认对应 GitHub Actions run 的 job 结论，不能解除 `CI-003`", readme, StringComparison.Ordinal);

        Assert.Contains("tools/ci-matrix-evidence-preflight.ps1", plan, StringComparison.Ordinal);
        Assert.Contains("blocked_invalid_ci_evidence", plan, StringComparison.Ordinal);
        Assert.Contains("ci_matrix_evidence_attached_pending_review", plan, StringComparison.Ordinal);
        Assert.Contains("CiMatrixEvidencePreflightRejectsWrongWorkflowMetadata", plan, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 CI evidence 预检的真实脚本行为：失败 conclusion 被拒绝，证据齐全也保持待审非零退出。
    /// </summary>
    [Fact]
    public void CiMatrixEvidencePreflightRejectsFailedReportsAndKeepsPendingReviewNonZero()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-ci-evidence-" + Guid.NewGuid().ToString("N"));

        try
        {
            string goodManifest = CreateCiEvidenceManifest(temp, benchmarkConclusion: "success");
            string badManifest = CreateCiEvidenceManifest(temp, benchmarkConclusion: "failure", suffix: "bad");

            string badArtifacts = Path.Combine(temp, "bad-out");
            // Act：执行被测操作
            ScriptResult bad = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "ci-matrix-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                badManifest,
                "-Artifacts",
                badArtifacts);
            // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-ci-schema-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateCiEvidenceManifest(temp, benchmarkConclusion: "success");
            string json = File.ReadAllText(manifest).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);
            File.WriteAllText(manifest, json);

            string artifacts = Path.Combine(temp, "schema-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "ci-matrix-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-ci-hash-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateCiEvidenceManifest(temp, benchmarkConclusion: "success");
            string json = File.ReadAllText(manifest).Replace("\"sha256\": \"", "\"sha256\": \"0000", StringComparison.Ordinal);
            File.WriteAllText(manifest, json);

            string artifacts = Path.Combine(temp, "hash-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "ci-matrix-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
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
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "ci-matrix-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
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
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "ci-matrix-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
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
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "ci-matrix-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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
    /// 验证 CI evidence 预检要求 RID 证据来自预期 GitHub hosted runner，避免错误 runner 报告冒充矩阵覆盖。
    /// </summary>
    [Fact]
    public void CiMatrixEvidencePreflightRejectsMismatchedRunnerIdentity()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-ci-runner-identity-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateCiEvidenceManifest(temp, benchmarkConclusion: "success");
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonObject buildTest = rootNode["buildTest"]!.AsObject();
            JsonObject linuxArm64 = buildTest["linux-arm64"]!.AsObject();
            string reportPath = (string)linuxArm64["report"]!;
            string text = File.ReadAllText(reportPath).Replace("| runner | ubuntu-24.04-arm |", "| runner | ubuntu-latest |", StringComparison.Ordinal);
            File.WriteAllText(reportPath, text);
            linuxArm64["runner"] = "ubuntu-latest";
            linuxArm64["sha256"] = GetSha256(reportPath);
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "runner-identity-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "ci-matrix-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "ci-matrix-evidence-preflight.md"));
            Assert.Contains("status: blocked_missing_ci_scope_evidence", result.Output + report, StringComparison.Ordinal);
            Assert.Contains("buildTest.linux-arm64 runner 必须为 ubuntu-24.04-arm，实际为 ubuntu-latest", report, StringComparison.Ordinal);
            Assert.Contains("build_test/linux-arm64/report 报告 runner 必须为 ubuntu-24.04-arm，实际为 ubuntu-latest", report, StringComparison.Ordinal);
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
    /// 验证 CI evidence 预检会拒绝错误 workflow/event/run_attempt/ref 元数据，避免其它 workflow 报告冒充 CI 矩阵。
    /// </summary>
    [Fact]
    public void CiMatrixEvidencePreflightRejectsWrongWorkflowMetadata()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-ci-workflow-metadata-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateCiEvidenceManifest(temp, benchmarkConclusion: "success");
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            string workflowRunReport = (string)rootNode["workflowRunReport"]!;
            string text = File.ReadAllText(workflowRunReport)
                .Replace("| workflow | CI |", "| workflow | Release |", StringComparison.Ordinal)
                .Replace("| event | push |", "| event | workflow_dispatch |", StringComparison.Ordinal)
                .Replace("| run_attempt | 1 |", "| run_attempt | 0 |", StringComparison.Ordinal)
                .Replace("| ref | refs/heads/main |", "| ref | refs/tags/v0.1.0 |", StringComparison.Ordinal);
            File.WriteAllText(workflowRunReport, text);
            rootNode["workflowRunSha256"] = GetSha256(workflowRunReport);
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "workflow-metadata-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "ci-matrix-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "ci-matrix-evidence-preflight.md"));
            Assert.Contains("status: blocked_missing_ci_scope_evidence", result.Output + report, StringComparison.Ordinal);
            Assert.Contains("workflow_run 报告 workflow 必须为 CI", report, StringComparison.Ordinal);
            Assert.Contains("workflow_run 报告 event 必须为 push 或 pull_request", report, StringComparison.Ordinal);
            Assert.Contains("workflow_run 报告 run_attempt 必须为 >= 1 的整数", report, StringComparison.Ordinal);
            Assert.Contains("workflow_run 报告 ref 必须来自分支或 PR", report, StringComparison.Ordinal);
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

    // —— 性能目标证据预检 ——
    public void PerformanceTargetEvidencePreflightRejectsUnknownScopesAndKeepsPendingReviewNonZero()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-performance-target-evidence-" + Guid.NewGuid().ToString("N"));

        try
        {
            string goodManifest = CreatePerformanceTargetEvidenceManifest(temp);
            string badManifest = CreatePerformanceTargetEvidenceManifest(temp, includeUnknownScope: true, suffix: "bad");

            string badArtifacts = Path.Combine(temp, "bad-out");
            // Act：执行被测操作
            ScriptResult bad = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "performance-target-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                badManifest,
                "-Artifacts",
                badArtifacts);
            // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-performance-target-schema-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreatePerformanceTargetEvidenceManifest(temp);
            string json = File.ReadAllText(manifest);
            json = json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);
            File.WriteAllText(manifest, json);

            string artifacts = Path.Combine(temp, "out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "performance-target-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-performance-target-hash-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreatePerformanceTargetEvidenceManifest(temp);
            string json = File.ReadAllText(manifest);
            json = json.Replace("\"sha256\": \"", "\"sha256\": \"0000", StringComparison.Ordinal);
            File.WriteAllText(manifest, json);

            string artifacts = Path.Combine(temp, "out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "performance-target-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-performance-target-missing-scope-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreatePerformanceTargetEvidenceManifest(temp);
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonArray evidence = rootNode["evidence"]!.AsArray();
            JsonNode? target = evidence.FirstOrDefault(node =>
                string.Equals((string?)node?["scope"], "hardware_counters_cache_branch", StringComparison.Ordinal));
            // Assert：验证预期结果
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
    /// 验证目标性能 evidence 不能拼接不同 benchmark run 的报告。
    /// </summary>
    [Fact]
    public void PerformanceTargetEvidencePreflightRejectsMixedBenchmarkRunIds()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-performance-target-run-id-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreatePerformanceTargetEvidenceManifest(temp);
            SetFlatEvidenceFileContent(
                manifest,
                "frame_budget_target_hardware",
                """
                benchmarkRunId: run-old-frame-budget
                gitCommit: abcdef123456
                targetHardware: representative-target
                source: PixelEngineDiagnostics
                scenario: lava_mine_typical
                demoScene: lava-mine
                sampleSeconds: 120
                frameSamples: 7200
                fixedTickNoCatchUp: true
                playerPackageRun: true
                realWindowRun: true
                degradationPolicyObserved: true
                frameTimelineCaptured: true
                caP99Ms: 7.5
                renderP99Ms: 3.5
                physicsP99Ms: 3.5
                logicAudioP99Ms: 0.8
                """);

            string artifacts = Path.Combine(temp, "run-id-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "performance-target-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "performance-target-evidence-preflight.md"));
            Assert.Contains("status: blocked_missing_target_performance_scope_evidence", report, StringComparison.Ordinal);
            Assert.Contains("frame_budget_target_hardware benchmarkRunId 必须与 manifest benchmarkRunId 一致", report, StringComparison.Ordinal);
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
        // Arrange：搭建测试场景与依赖
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
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "performance-target-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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
    /// 验证 cells/frame evidence 不能只写机器可读字段，必须附 BenchmarkDotNet 目标基准报告特征。
    /// </summary>
    [Fact]
    public void PerformanceTargetEvidencePreflightRejectsCellsFramePlainKeyValueSummary()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-performance-target-cells-plain-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreatePerformanceTargetEvidenceManifest(temp);
            SetFlatEvidenceFileContent(
                manifest,
                "cells_frame/win-x64",
                """
                rid: win-x64
                benchmarkDotNet: true
                representativeHardware: true
                activeCellsPerFrame: 2500000
                caFrameMs: 7.2
                measuredIterations: 5
                iterationCount: 5
                """);

            string artifacts = Path.Combine(temp, "cells-plain-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "performance-target-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "performance-target-evidence-preflight.md"));
            Assert.Contains("status: blocked_missing_target_performance_scope_evidence", report, StringComparison.Ordinal);
            Assert.Contains("cells_frame/win-x64 必须包含 BenchmarkDotNet v 报告头", report, StringComparison.Ordinal);
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
    /// 验证旧 262K FullActiveLiquid 报告即使机器字段达标，也不能冒充 2M full-dirty 目标基准。
    /// </summary>
    [Fact]
    public void PerformanceTargetEvidencePreflightRejectsLegacyFullActiveLiquidReport()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-performance-target-legacy-ca-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreatePerformanceTargetEvidenceManifest(temp);
            SetFlatEvidenceFileContent(
                manifest,
                "cells_frame/win-x64",
                """
                // BenchmarkDotNet v0.15.8
                // Benchmark: PixelEngine.Benchmarks.CellThroughputBenchmark.StepJobSystem
                // Scenario: FullActiveLiquid
                benchmarkRunId: run-20260704-performance-001
                gitCommit: abcdef123456
                rid: win-x64
                benchmarkDotNet: true
                representativeHardware: true
                activeCellsPerFrame: 2500000
                caFrameMs: 7.2
                measuredIterations: 5
                iterationCount: 5
                """);

            string artifacts = Path.Combine(temp, "legacy-ca-out");
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "performance-target-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "performance-target-evidence-preflight.md"));
            Assert.Contains("FullActiveCellThroughputBenchmark.StepJobSystemFullActive2MIndependentFrames", report, StringComparison.Ordinal);
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
        // Arrange：搭建测试场景与依赖
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
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "performance-target-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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

    // —— 反汇编与硬件计数器预检 ——
    public void PerformanceTargetEvidencePreflightRejectsHardwareCounterReportWithoutBranchMispredictions()
    {
        // Arrange：搭建测试场景与依赖
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
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "performance-target-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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

    // —— 性能目标证据预检 ——
    public void PerformanceTargetEvidencePreflightRejectsFrameBudgetAbovePlanThreshold()
    {
        // Arrange：搭建测试场景与依赖
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
                source: PixelEngineDiagnostics
                scenario: lava_mine_typical
                demoScene: lava-mine
                sampleSeconds: 120
                frameSamples: 7200
                fixedTickNoCatchUp: true
                playerPackageRun: true
                realWindowRun: true
                degradationPolicyObserved: true
                frameTimelineCaptured: true
                caP99Ms: 8.5
                renderP99Ms: 3.5
                physicsP99Ms: 3.5
                logicAudioP99Ms: 0.8
                """);

            string artifacts = Path.Combine(temp, "frame-budget-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "performance-target-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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
    /// 验证目标硬件帧预算 evidence 必须来自引擎诊断长跑，而不是只写 p99 数字摘要。
    /// </summary>
    [Fact]
    public void PerformanceTargetEvidencePreflightRejectsFrameBudgetWithoutDiagnosticsSource()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-performance-target-frame-budget-source-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreatePerformanceTargetEvidenceManifest(temp);
            SetFlatEvidenceFileContent(
                manifest,
                "frame_budget_target_hardware",
                """
                targetHardware: representative-target
                sampleSeconds: 120
                caP99Ms: 7.5
                renderP99Ms: 3.5
                physicsP99Ms: 3.5
                logicAudioP99Ms: 0.8
                """);

            string artifacts = Path.Combine(temp, "frame-budget-source-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "performance-target-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "performance-target-evidence-preflight.md"));
            Assert.Contains("status: blocked_missing_target_performance_scope_evidence", report, StringComparison.Ordinal);
            Assert.Contains("frame_budget_target_hardware 缺少机器可读字段 source", report, StringComparison.Ordinal);
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
    /// 验证目标硬件帧预算 evidence 必须来自玩家包真实窗口，并观察到降级策略与帧时间线。
    /// </summary>
    [Fact]
    public void PerformanceTargetEvidencePreflightRejectsFrameBudgetWithoutProductRunProof()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-performance-target-frame-budget-product-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreatePerformanceTargetEvidenceManifest(temp);
            SetFlatEvidenceFileContent(
                manifest,
                "frame_budget_target_hardware",
                """
                targetHardware: representative-target
                source: PixelEngineDiagnostics
                scenario: lava_mine_typical
                demoScene: lava-mine
                sampleSeconds: 120
                frameSamples: 7200
                fixedTickNoCatchUp: true
                playerPackageRun: false
                realWindowRun: true
                degradationPolicyObserved: true
                frameTimelineCaptured: true
                caP99Ms: 7.5
                renderP99Ms: 3.5
                physicsP99Ms: 3.5
                logicAudioP99Ms: 0.8
                """);

            string artifacts = Path.Combine(temp, "frame-budget-product-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "performance-target-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "performance-target-evidence-preflight.md"));
            Assert.Contains("status: blocked_missing_target_performance_scope_evidence", report, StringComparison.Ordinal);
            Assert.Contains("frame_budget_target_hardware playerPackageRun 必须为 true", report, StringComparison.Ordinal);
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
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-performance-target-cells-target-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreatePerformanceTargetEvidenceManifest(temp);
            SetFlatEvidenceFileContent(
                manifest,
                "cells_frame/linux-x64",
                """
                // BenchmarkDotNet v0.15.8
                // Benchmark: PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark.StepJobSystemFullActive2MIndependentFrames
                // Scenario: FullActive2M
                rid: linux-x64
                benchmarkDotNet: true
                representativeHardware: true
                activeCellsPerFrame: 1500000
                caFrameMs: 7.2
                measuredIterations: 5
                iterationCount: 5
                """);

            string artifacts = Path.Combine(temp, "cells-target-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "performance-target-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-performance-target-cells-representative-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreatePerformanceTargetEvidenceManifest(temp);
            SetFlatEvidenceFileContent(
                manifest,
                "cells_frame/win-arm64",
                """
                // BenchmarkDotNet v0.15.8
                // Benchmark: PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark.StepJobSystemFullActive2MIndependentFrames
                // Scenario: FullActive2M
                rid: win-arm64
                benchmarkDotNet: true
                representativeHardware: false
                activeCellsPerFrame: 2500000
                caFrameMs: 7.2
                measuredIterations: 5
                iterationCount: 5
                """);

            string artifacts = Path.Combine(temp, "cells-representative-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "performance-target-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-performance-target-cells-iterations-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreatePerformanceTargetEvidenceManifest(temp);
            SetFlatEvidenceFileContent(
                manifest,
                "cells_frame/osx-arm64",
                """
                // BenchmarkDotNet v0.15.8
                // Benchmark: PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark.StepJobSystemFullActive2MIndependentFrames
                // Scenario: FullActive2M
                rid: osx-arm64
                benchmarkDotNet: true
                representativeHardware: true
                activeCellsPerFrame: 2500000
                caFrameMs: 7.2
                measuredIterations: 2
                iterationCount: 5
                """);

            string artifacts = Path.Combine(temp, "cells-iterations-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "performance-target-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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
    /// 验证 UI Runtime 证据预检入口覆盖 plan/20 的真实窗口、native、IME、Ultralight 与发行证据债，且不会把 pending review 误当成完成。
    /// </summary>
    [Fact]

    // —— UI 运行时证据预检 ——
    public void UiRuntimeEvidencePreflightRequiresManifestScopesAndKeepsPlan20Blocked()
    {
        // Arrange：准备输入与初始状态
        string script = ReadRepositoryFile("tools", "ui-runtime-evidence-preflight.ps1");
        string readme = ReadRepositoryFile("plan", "tasks", "70-evidence-contracts.md");
        string plan = ReadRepositoryFile("plan", "20-interactive-html-ui.md");

        // Assert：验证预期结果
        Assert.Contains("EvidenceManifestPath", script, StringComparison.Ordinal);
        Assert.Contains("AllowBlocked", script, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_ui_runtime_evidence", script, StringComparison.Ordinal);
        Assert.Contains("blocked_invalid_ui_runtime_evidence", script, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_ui_runtime_scope_evidence", script, StringComparison.Ordinal);
        Assert.Contains("ui_runtime_evidence_attached_pending_review", script, StringComparison.Ordinal);
        Assert.Contains("transparent_ui_product_window", script, StringComparison.Ordinal);
        Assert.Contains("rmlui_angle_gles_native_profile", script, StringComparison.Ordinal);
        Assert.Contains("platform_ime_composition", script, StringComparison.Ordinal);
        Assert.Contains("ultralight_optional_profile_gate", script, StringComparison.Ordinal);
        Assert.Contains("ui_native_release_artifact", script, StringComparison.Ordinal);
        Assert.Contains("gitCommit 必须等于当前 HEAD", script, StringComparison.Ordinal);

        Assert.Contains("tools/ui-runtime-evidence-preflight.ps1", readme, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_ui_runtime_evidence", readme, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_ui_runtime_scope_evidence", readme, StringComparison.Ordinal);
        Assert.Contains("ui_runtime_evidence_attached_pending_review", readme, StringComparison.Ordinal);
        Assert.Contains("同一 `reviewSessionId` / `gitCommit`", readme, StringComparison.Ordinal);
        Assert.Contains("conclusion", readme, StringComparison.Ordinal);
        Assert.Contains("risk", readme, StringComparison.Ordinal);
        Assert.Contains("true 字段", readme, StringComparison.Ordinal);
        Assert.Contains("noSecondWindow", readme, StringComparison.Ordinal);
        Assert.Contains("noSecondProcess", readme, StringComparison.Ordinal);
        Assert.Contains("singleRenderContextVerified", readme, StringComparison.Ordinal);
        Assert.Contains("worldVisibleThroughTransparentPixels", readme, StringComparison.Ordinal);
        Assert.Contains("videoDurationSeconds>=30", readme, StringComparison.Ordinal);
        Assert.Contains("capturedFrameCount>=300", readme, StringComparison.Ordinal);
        Assert.Contains("transparentPixelSampleCount>=3", readme, StringComparison.Ordinal);
        Assert.Contains("passThroughSampleCount>=3", readme, StringComparison.Ordinal);
        Assert.Contains("smokeFrameCount>=60", readme, StringComparison.Ordinal);
        Assert.Contains("compositionSessionCount>=1", readme, StringComparison.Ordinal);
        Assert.Contains("licenseDocumentCount>=1", readme, StringComparison.Ordinal);
        Assert.Contains("inactiveBoundaryTestCount>=1", readme, StringComparison.Ordinal);
        Assert.Contains("releaseAuditRejectionCaseCount>=1", readme, StringComparison.Ordinal);
        Assert.Contains("releaseArtifactCount>=1", readme, StringComparison.Ordinal);
        Assert.Contains("sha256EntryCount>=1", readme, StringComparison.Ordinal);
        Assert.Contains("tools/ui-runtime-evidence-preflight.ps1", plan, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_ui_runtime_evidence", plan, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_ui_runtime_scope_evidence", plan, StringComparison.Ordinal);
        Assert.Contains("ui_runtime_evidence_attached_pending_review", plan, StringComparison.Ordinal);
        Assert.Contains("noSecondWindow", plan, StringComparison.Ordinal);
        Assert.Contains("worldVisibleThroughTransparentPixels", plan, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 UI Runtime 证据预检会真实拒绝缺失 scope，并保持证据齐全时仍为待人工复核的非零退出。
    /// </summary>
    [Fact]
    public void UiRuntimeEvidencePreflightRejectsMissingScopesAndKeepsPendingReviewNonZero()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-ui-runtime-evidence-" + Guid.NewGuid().ToString("N"));

        try
        {
            string goodManifest = CreateUiRuntimeEvidenceManifest(temp);
            string missingManifest = CreateUiRuntimeEvidenceManifest(temp, suffix: "missing");
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(missingManifest))!.AsObject();
            JsonArray evidence = rootNode["evidence"]!.AsArray();
            JsonNode? target = evidence.FirstOrDefault(node =>
                string.Equals((string?)node?["scope"], "platform_ime_composition", StringComparison.Ordinal));
            // Assert：验证预期结果
            Assert.NotNull(target);
            _ = evidence.Remove(target);
            File.WriteAllText(missingManifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string missingArtifacts = Path.Combine(temp, "missing-out");
            ScriptResult missing = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "ui-runtime-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                missingManifest,
                "-Artifacts",
                missingArtifacts);
            Assert.Equal(5, missing.ExitCode);
            string missingReport = File.ReadAllText(Path.Combine(missingArtifacts, "ui-runtime-evidence-preflight.md"));
            Assert.Contains("status: blocked_missing_ui_runtime_scope_evidence", missingReport, StringComparison.Ordinal);
            Assert.Contains("缺少 evidence scope：platform_ime_composition", missingReport, StringComparison.Ordinal);
            Assert.DoesNotContain("status: ui_runtime_evidence_attached_pending_review", missingReport, StringComparison.Ordinal);

            string goodArtifacts = Path.Combine(temp, "good-out");
            ScriptResult good = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "ui-runtime-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                goodManifest,
                "-Artifacts",
                goodArtifacts);
            Assert.Equal(2, good.ExitCode);
            string goodReport = File.ReadAllText(Path.Combine(goodArtifacts, "ui-runtime-evidence-preflight.md"));
            Assert.Contains("ui_runtime_evidence_attached_pending_review", good.Output + goodReport, StringComparison.Ordinal);
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
    /// 验证 UI Runtime 证据预检会把 schema 错误写入报告，而不是直接抛异常丢失审计上下文。
    /// </summary>
    [Fact]
    public void UiRuntimeEvidencePreflightRejectsInvalidSchemaWithReport()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-ui-runtime-schema-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateUiRuntimeEvidenceManifest(temp);
            string json = File.ReadAllText(manifest);
            json = json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);
            File.WriteAllText(manifest, json);

            string artifacts = Path.Combine(temp, "out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "ui-runtime-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "ui-runtime-evidence-preflight.md"));
            Assert.Contains("status: blocked_invalid_ui_runtime_evidence", report, StringComparison.Ordinal);
            Assert.Contains("schemaVersion 必须为 1", report, StringComparison.Ordinal);
            Assert.DoesNotContain("status: ui_runtime_evidence_attached_pending_review", report, StringComparison.Ordinal);
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
    /// 验证 UI Runtime 证据预检会实际拒绝 sha256 不匹配的真实平台 evidence。
    /// </summary>
    [Fact]
    public void UiRuntimeEvidencePreflightRejectsMismatchedEvidenceHash()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-ui-runtime-hash-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateUiRuntimeEvidenceManifest(temp);
            string json = File.ReadAllText(manifest);
            json = json.Replace("\"sha256\": \"", "\"sha256\": \"0000", StringComparison.Ordinal);
            File.WriteAllText(manifest, json);

            string artifacts = Path.Combine(temp, "out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "ui-runtime-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "ui-runtime-evidence-preflight.md"));
            Assert.Contains("status: blocked_missing_ui_runtime_scope_evidence", report, StringComparison.Ordinal);
            Assert.Contains("sha256 不匹配", report, StringComparison.Ordinal);
            Assert.Contains("transparent_ui_product_window", report, StringComparison.Ordinal);
            Assert.DoesNotContain("status: ui_runtime_evidence_attached_pending_review", report, StringComparison.Ordinal);
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
    /// 验证 UI Runtime 证据预检会拒绝只有布尔结论但缺少足够时长 / 帧数支撑的弱产品面报告。
    /// </summary>
    [Fact]
    public void UiRuntimeEvidencePreflightRejectsWeakTransparentWindowDuration()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-ui-runtime-weak-duration-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateUiRuntimeEvidenceManifest(temp);
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonArray evidence = rootNode["evidence"]!.AsArray();
            JsonObject transparentEntry = evidence
                .Select(static node => node!.AsObject())
                .Single(static node => string.Equals((string?)node["scope"], "transparent_ui_product_window", StringComparison.Ordinal));
            string reportPath = (string)transparentEntry["path"]!;
            string report = File.ReadAllText(reportPath)
                .Replace("videoDurationSeconds: 30", "videoDurationSeconds: 2", StringComparison.Ordinal);
            File.WriteAllText(reportPath, report);
            transparentEntry["sha256"] = GetSha256(reportPath);
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "ui-runtime-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(5, result.ExitCode);
            string outputReport = File.ReadAllText(Path.Combine(artifacts, "ui-runtime-evidence-preflight.md"));
            Assert.Contains("status: blocked_missing_ui_runtime_scope_evidence", outputReport, StringComparison.Ordinal);
            Assert.Contains("transparent_ui_product_window videoDurationSeconds 必须至少为 30", outputReport, StringComparison.Ordinal);
            Assert.DoesNotContain("status: ui_runtime_evidence_attached_pending_review", outputReport, StringComparison.Ordinal);
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
    /// 验证透明 UI 产品面证据不能只给视频和 alpha 结论，还必须证明没有第二窗口/进程且透明像素真实透出世界。
    /// </summary>
    [Fact]
    public void UiRuntimeEvidencePreflightRejectsWeakTransparentWindowTopologyAndPixelEvidence()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-ui-runtime-weak-window-topology-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateUiRuntimeEvidenceManifest(temp);
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonArray evidence = rootNode["evidence"]!.AsArray();
            JsonObject transparentEntry = evidence
                .Select(static node => node!.AsObject())
                .Single(static node => string.Equals((string?)node["scope"], "transparent_ui_product_window", StringComparison.Ordinal));
            string reportPath = (string)transparentEntry["path"]!;
            string report = File.ReadAllText(reportPath)
                .Replace("noSecondWindow: true", "noSecondWindow: false", StringComparison.Ordinal)
                .Replace("worldVisibleThroughTransparentPixels: true", "worldVisibleThroughTransparentPixels: false", StringComparison.Ordinal)
                .Replace("transparentPixelSampleCount: 3", "transparentPixelSampleCount: 0", StringComparison.Ordinal);
            File.WriteAllText(reportPath, report);
            transparentEntry["sha256"] = GetSha256(reportPath);
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "ui-runtime-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(5, result.ExitCode);
            string outputReport = File.ReadAllText(Path.Combine(artifacts, "ui-runtime-evidence-preflight.md"));
            Assert.Contains("status: blocked_missing_ui_runtime_scope_evidence", outputReport, StringComparison.Ordinal);
            Assert.Contains("transparent_ui_product_window noSecondWindow 必须为 true", outputReport, StringComparison.Ordinal);
            Assert.DoesNotContain("status: ui_runtime_evidence_attached_pending_review", outputReport, StringComparison.Ordinal);
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
    /// 验证 Ultralight optional profile 证据不能只声明许可存在，还必须证明未激活边界不执行且发行审计拒绝 native 混入。
    /// </summary>
    [Fact]
    public void UiRuntimeEvidencePreflightRejectsWeakUltralightInactiveGateEvidence()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-ui-runtime-weak-ultralight-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateUiRuntimeEvidenceManifest(temp);
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonArray evidence = rootNode["evidence"]!.AsArray();
            JsonObject ultralightEntry = evidence
                .Select(static node => node!.AsObject())
                .Single(static node => string.Equals((string?)node["scope"], "ultralight_optional_profile_gate", StringComparison.Ordinal));
            string reportPath = (string)ultralightEntry["path"]!;
            string report = File.ReadAllText(reportPath)
                .Replace("inactiveBackendCapturesNoInput: true", "inactiveBackendCapturesNoInput: false", StringComparison.Ordinal)
                .Replace("inactiveBoundaryTestCount: 1", "inactiveBoundaryTestCount: 0", StringComparison.Ordinal);
            File.WriteAllText(reportPath, report);
            ultralightEntry["sha256"] = GetSha256(reportPath);
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "ui-runtime-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(5, result.ExitCode);
            string outputReport = File.ReadAllText(Path.Combine(artifacts, "ui-runtime-evidence-preflight.md"));
            Assert.Contains("status: blocked_missing_ui_runtime_scope_evidence", outputReport, StringComparison.Ordinal);
            Assert.Contains("ultralight_optional_profile_gate inactiveBackendCapturesNoInput 必须为 true", outputReport, StringComparison.Ordinal);
            Assert.DoesNotContain("status: ui_runtime_evidence_attached_pending_review", outputReport, StringComparison.Ordinal);
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
    /// 验证 Editor UX 证据预检入口覆盖 plan/19 的真实窗口 UX 证据债，且不会把 pending review 误当成完成。
    /// </summary>
    [Fact]

    // —— 编辑器 UX 证据预检 ——
    public void EditorUxEvidencePreflightRequiresManifestScopesAndKeepsPlan19Blocked()
    {
        // Arrange：准备输入与初始状态
        string script = ReadRepositoryFile("tools", "editor-ux-evidence-preflight.ps1");
        string readme = ReadRepositoryFile("plan", "tasks", "70-evidence-contracts.md");
        string plan = ReadRepositoryFile("plan", "19-standalone-editor-app.md");

        // Assert：验证预期结果
        Assert.Contains("EvidenceManifestPath", script, StringComparison.Ordinal);
        Assert.Contains("AllowBlocked", script, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_editor_ux_evidence", script, StringComparison.Ordinal);
        Assert.Contains("blocked_invalid_editor_ux_evidence", script, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_editor_ux_scope_evidence", script, StringComparison.Ordinal);
        Assert.Contains("editor_ux_evidence_attached_pending_review", script, StringComparison.Ordinal);
        Assert.Contains("editor_full_route_window", script, StringComparison.Ordinal);
        Assert.Contains("project_window_reference_stability", script, StringComparison.Ordinal);
        Assert.Contains("script_external_editor", script, StringComparison.Ordinal);
        Assert.Contains("settings_build_ux", script, StringComparison.Ordinal);
        Assert.Contains("editor_product_usability", script, StringComparison.Ordinal);
        Assert.Contains("videoDurationSeconds", script, StringComparison.Ordinal);
        Assert.Contains("interactionChecklistItemCount", script, StringComparison.Ordinal);
        Assert.Contains("gitCommit 必须等于当前 HEAD", script, StringComparison.Ordinal);

        Assert.Contains("tools/editor-ux-evidence-preflight.ps1", readme, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_editor_ux_evidence", readme, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_editor_ux_scope_evidence", readme, StringComparison.Ordinal);
        Assert.Contains("editor_ux_evidence_attached_pending_review", readme, StringComparison.Ordinal);
        Assert.Contains("shellStarted", readme, StringComparison.Ordinal);
        Assert.Contains("editorShellExeLaunched", readme, StringComparison.Ordinal);
        Assert.Contains("singleTopLevelWindowVerified", readme, StringComparison.Ordinal);
        Assert.Contains("singleProcessInProcessHost", readme, StringComparison.Ordinal);
        Assert.Contains("noConsoleWindowObserved", readme, StringComparison.Ordinal);
        Assert.Contains("videoDurationSeconds>=60", readme, StringComparison.Ordinal);
        Assert.Contains("capturedFrameCount>=600", readme, StringComparison.Ordinal);
        Assert.Contains("routeStepCount>=8", readme, StringComparison.Ordinal);
        Assert.Contains("stableIdsChecked", readme, StringComparison.Ordinal);
        Assert.Contains("assetOperationCount>=3", readme, StringComparison.Ordinal);
        Assert.Contains("referenceDocumentCount>=2", readme, StringComparison.Ordinal);
        Assert.Contains("stableAssetKindCount>=4", readme, StringComparison.Ordinal);
        Assert.Contains("buildPackageAuditCount>=1", readme, StringComparison.Ordinal);
        Assert.Contains("scriptDoubleClickAttempted", readme, StringComparison.Ordinal);
        Assert.Contains("scriptOpenAttemptCount>=1", readme, StringComparison.Ordinal);
        Assert.Contains("projectSettingsSaved", readme, StringComparison.Ordinal);
        Assert.Contains("settingsRoundTripCount>=1", readme, StringComparison.Ordinal);
        Assert.Contains("buildRunAttemptCount>=1", readme, StringComparison.Ordinal);
        Assert.Contains("layoutUsable", readme, StringComparison.Ordinal);
        Assert.Contains("interactionChecklistItemCount>=7", readme, StringComparison.Ordinal);
        Assert.Contains("reviewerCount>=1", readme, StringComparison.Ordinal);
        Assert.Contains("tools/editor-ux-evidence-preflight.ps1", plan, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_editor_ux_evidence", plan, StringComparison.Ordinal);
        Assert.Contains("blocked_missing_editor_ux_scope_evidence", plan, StringComparison.Ordinal);
        Assert.Contains("editor_ux_evidence_attached_pending_review", plan, StringComparison.Ordinal);
        Assert.Contains("editorShellExeLaunched", plan, StringComparison.Ordinal);
        Assert.Contains("singleProcessInProcessHost", plan, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 Editor UX 证据预检会真实拒绝缺失 scope，并保持证据齐全时仍为待人工复核的非零退出。
    /// </summary>
    [Fact]
    public void EditorUxEvidencePreflightRejectsMissingScopesAndKeepsPendingReviewNonZero()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-editor-ux-evidence-" + Guid.NewGuid().ToString("N"));

        try
        {
            string goodManifest = CreateEditorUxEvidenceManifest(temp);
            string missingManifest = CreateEditorUxEvidenceManifest(temp, suffix: "missing");
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(missingManifest))!.AsObject();
            JsonArray evidence = rootNode["evidence"]!.AsArray();
            JsonNode? target = evidence.FirstOrDefault(node =>
                string.Equals((string?)node?["scope"], "project_window_reference_stability", StringComparison.Ordinal));
            // Assert：验证预期结果
            Assert.NotNull(target);
            _ = evidence.Remove(target);
            File.WriteAllText(missingManifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string missingArtifacts = Path.Combine(temp, "missing-out");
            ScriptResult missing = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "editor-ux-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                missingManifest,
                "-Artifacts",
                missingArtifacts);
            Assert.Equal(5, missing.ExitCode);
            string missingReport = File.ReadAllText(Path.Combine(missingArtifacts, "editor-ux-evidence-preflight.md"));
            Assert.Contains("status: blocked_missing_editor_ux_scope_evidence", missingReport, StringComparison.Ordinal);
            Assert.Contains("缺少 evidence scope：project_window_reference_stability", missingReport, StringComparison.Ordinal);
            Assert.DoesNotContain("status: editor_ux_evidence_attached_pending_review", missingReport, StringComparison.Ordinal);

            string goodArtifacts = Path.Combine(temp, "good-out");
            ScriptResult good = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "editor-ux-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                goodManifest,
                "-Artifacts",
                goodArtifacts);
            Assert.Equal(2, good.ExitCode);
            string goodReport = File.ReadAllText(Path.Combine(goodArtifacts, "editor-ux-evidence-preflight.md"));
            Assert.Contains("editor_ux_evidence_attached_pending_review", good.Output + goodReport, StringComparison.Ordinal);
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
    /// 验证 Editor UX 证据预检会拒绝时长/帧数不足的弱真实窗口路线证据。
    /// </summary>
    [Fact]
    public void EditorUxEvidencePreflightRejectsWeakFullRouteDuration()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-editor-ux-weak-route-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateEditorUxEvidenceManifest(temp);
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonArray evidence = rootNode["evidence"]!.AsArray();
            JsonObject fullRoute = evidence
                .Select(node => node!.AsObject())
                .Single(node => string.Equals((string?)node["scope"], "editor_full_route_window", StringComparison.Ordinal));

            string reportPath = Path.Combine(root, (string)fullRoute["path"]!);
            string report = File.ReadAllText(reportPath);
            report = report.Replace("videoDurationSeconds: 60", "videoDurationSeconds: 2", StringComparison.Ordinal);
            report = report.Replace("capturedFrameCount: 600", "capturedFrameCount: 12", StringComparison.Ordinal);
            File.WriteAllText(reportPath, report);
            fullRoute["sha256"] = GetSha256(reportPath);
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "editor-ux-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(5, result.ExitCode);
            string outputReport = File.ReadAllText(Path.Combine(artifacts, "editor-ux-evidence-preflight.md"));
            Assert.Contains("status: blocked_missing_editor_ux_scope_evidence", outputReport, StringComparison.Ordinal);
            Assert.Contains("editor_full_route_window videoDurationSeconds 必须至少为 60", outputReport, StringComparison.Ordinal);
            Assert.DoesNotContain("status: editor_ux_evidence_attached_pending_review", outputReport, StringComparison.Ordinal);
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
    /// 验证完整路线证据不能只展示 UI 流程，还必须证明正式 Shell 拓扑：单窗口、单进程 in-process host 且不弹控制台。
    /// </summary>
    [Fact]
    public void EditorUxEvidencePreflightRejectsWeakFullRouteShellTopology()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-editor-ux-weak-route-topology-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateEditorUxEvidenceManifest(temp);
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonArray evidence = rootNode["evidence"]!.AsArray();
            JsonObject fullRoute = evidence
                .Select(node => node!.AsObject())
                .Single(node => string.Equals((string?)node["scope"], "editor_full_route_window", StringComparison.Ordinal));

            string reportPath = Path.Combine(root, (string)fullRoute["path"]!);
            string report = File.ReadAllText(reportPath)
                .Replace("singleProcessInProcessHost: true", "singleProcessInProcessHost: false", StringComparison.Ordinal)
                .Replace("noConsoleWindowObserved: true", "noConsoleWindowObserved: false", StringComparison.Ordinal);
            File.WriteAllText(reportPath, report);
            fullRoute["sha256"] = GetSha256(reportPath);
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "editor-ux-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(5, result.ExitCode);
            string outputReport = File.ReadAllText(Path.Combine(artifacts, "editor-ux-evidence-preflight.md"));
            Assert.Contains("status: blocked_missing_editor_ux_scope_evidence", outputReport, StringComparison.Ordinal);
            Assert.Contains("editor_full_route_window singleProcessInProcessHost 必须为 true", outputReport, StringComparison.Ordinal);
            Assert.DoesNotContain("status: editor_ux_evidence_attached_pending_review", outputReport, StringComparison.Ordinal);
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
    /// 验证 Project Window 引用稳定性证据必须覆盖前后 stable id、零断引用和 Build 包引用审计。
    /// </summary>
    [Fact]
    public void EditorUxEvidencePreflightRejectsWeakProjectReferenceStabilityEvidence()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-editor-ux-weak-project-reference-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateEditorUxEvidenceManifest(temp);
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonArray evidence = rootNode["evidence"]!.AsArray();
            JsonObject projectReference = evidence
                .Select(static node => node!.AsObject())
                .Single(static node => string.Equals((string?)node["scope"], "project_window_reference_stability", StringComparison.Ordinal));

            string reportPath = Path.Combine(root, (string)projectReference["path"]!);
            string report = File.ReadAllText(reportPath)
                .Replace("brokenReferenceCountZero: true", "brokenReferenceCountZero: false", StringComparison.Ordinal)
                .Replace("buildPackageAuditCount: 1", "buildPackageAuditCount: 0", StringComparison.Ordinal);
            File.WriteAllText(reportPath, report);
            projectReference["sha256"] = GetSha256(reportPath);
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "editor-ux-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(5, result.ExitCode);
            string outputReport = File.ReadAllText(Path.Combine(artifacts, "editor-ux-evidence-preflight.md"));
            Assert.Contains("status: blocked_missing_editor_ux_scope_evidence", outputReport, StringComparison.Ordinal);
            Assert.Contains("project_window_reference_stability brokenReferenceCountZero 必须为 true", outputReport, StringComparison.Ordinal);
            Assert.DoesNotContain("status: editor_ux_evidence_attached_pending_review", outputReport, StringComparison.Ordinal);
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
    /// 验证 Editor UX 证据预检会把 schema 错误写入报告，而不是直接抛异常丢失审计上下文。
    /// </summary>
    [Fact]
    public void EditorUxEvidencePreflightRejectsInvalidSchemaWithReport()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-editor-ux-schema-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateEditorUxEvidenceManifest(temp);
            string json = File.ReadAllText(manifest);
            json = json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);
            File.WriteAllText(manifest, json);

            string artifacts = Path.Combine(temp, "out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "editor-ux-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "editor-ux-evidence-preflight.md"));
            Assert.Contains("status: blocked_invalid_editor_ux_evidence", report, StringComparison.Ordinal);
            Assert.Contains("schemaVersion 必须为 1", report, StringComparison.Ordinal);
            Assert.DoesNotContain("status: editor_ux_evidence_attached_pending_review", report, StringComparison.Ordinal);
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
    /// 验证 Editor UX 证据预检会实际拒绝 sha256 不匹配的真实窗口 evidence。
    /// </summary>
    [Fact]
    public void EditorUxEvidencePreflightRejectsMismatchedEvidenceHash()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-editor-ux-hash-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateEditorUxEvidenceManifest(temp);
            string json = File.ReadAllText(manifest);
            json = json.Replace("\"sha256\": \"", "\"sha256\": \"0000", StringComparison.Ordinal);
            File.WriteAllText(manifest, json);

            string artifacts = Path.Combine(temp, "out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "editor-ux-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "editor-ux-evidence-preflight.md"));
            Assert.Contains("status: blocked_missing_editor_ux_scope_evidence", report, StringComparison.Ordinal);
            Assert.Contains("sha256 不匹配", report, StringComparison.Ordinal);
            Assert.Contains("editor_full_route_window", report, StringComparison.Ordinal);
            Assert.DoesNotContain("status: editor_ux_evidence_attached_pending_review", report, StringComparison.Ordinal);
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

    // —— 发行打包与证据审计 ——
    public void ReleasePublishModesPreserveR2RLightUpAndAotIsaAudit()
    {
        // Arrange：准备输入与初始状态
        string props = ReadRepositoryFile("Directory.Build.props");
        string release = ReadRepositoryFile(".github", "workflows", "release.yml");
        string aotProbePs1 = ReadRepositoryFile("tools", "aot-simd-probe.ps1");
        string aotProbeSh = ReadRepositoryFile("tools", "aot-simd-probe.sh");

        // Assert：验证预期结果
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
        // Arrange：准备输入与初始状态
        string auditPs1 = ReadRepositoryFile("tools", "audit-release-artifacts.ps1");
        string auditSh = ReadRepositoryFile("tools", "audit-release-artifacts.sh");
        string packagePs1 = ReadRepositoryFile("tools", "package.ps1");
        string packageSh = ReadRepositoryFile("tools", "package.sh");
        string publishR2rPs1 = ReadRepositoryFile("tools", "publish-r2r.ps1");
        string publishAotPs1 = ReadRepositoryFile("tools", "publish-aot.ps1");
        string publishR2rSh = ReadRepositoryFile("tools", "publish-r2r.sh");
        string publishAotSh = ReadRepositoryFile("tools", "publish-aot.sh");
        string evidence = ReadRepositoryFile("tools", "release-evidence-preflight.ps1");
        string evidenceSh = ReadRepositoryFile("tools", "release-evidence-preflight.sh");
        string release = ReadRepositoryFile(".github", "workflows", "release.yml");
        string example = ReadRepositoryFile("docs", "release-reports", "release-evidence-manifest.example.json");
        string releaseReport = ReadRepositoryFile("docs", "release-reports", "2026-07-02-win-x64-publish.md");
        string plan = ReadRepositoryFile("plan", "15-build-packaging-distribution.md");
        string conventions = ReadRepositoryFile("plan", "00-conventions-and-techstack.md");
        string solution = ReadRepositoryFile("PixelEngine.sln");

        // Assert：验证预期结果
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
        Assert.Contains("NOTICE.txt", auditPs1, StringComparison.Ordinal);
        Assert.Contains("Test-DisallowedPlayerPackageFile", auditPs1, StringComparison.Ordinal);
        Assert.Contains("createdump.exe", auditPs1, StringComparison.Ordinal);
        Assert.Contains("package 不应包含玩家无关的调试、文档、诊断辅助或本地化卫星资源文件", auditPs1, StringComparison.Ordinal);
        Assert.Contains("展开 package 不应包含玩家无关的调试、文档、诊断辅助或本地化卫星资源文件", auditPs1, StringComparison.Ordinal);
        Assert.Contains("package 内 SHA256SUMS 未覆盖文件", auditPs1, StringComparison.Ordinal);
        Assert.Contains("content/materials.json", auditPs1, StringComparison.Ordinal);
        Assert.Contains("content/weapons.json", auditPs1, StringComparison.Ordinal);
        Assert.Contains("content/textures/17_gravel.png", auditPs1, StringComparison.Ordinal);
        Assert.Contains("content/textures/18_boundary_stone.png", auditPs1, StringComparison.Ordinal);
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
        Assert.Contains("has_notice", auditSh, StringComparison.Ordinal);
        Assert.Contains("NOTICE.txt", auditSh, StringComparison.Ordinal);
        Assert.Contains("is_disallowed_player_package_file", auditSh, StringComparison.Ordinal);
        Assert.Contains("createdump.exe", auditSh, StringComparison.Ordinal);
        Assert.Contains("package 不应包含玩家无关的调试、文档、诊断辅助或本地化卫星资源文件", auditSh, StringComparison.Ordinal);
        Assert.Contains("展开 package 不应包含玩家无关的调试、文档、诊断辅助或本地化卫星资源文件", auditSh, StringComparison.Ordinal);
        Assert.Contains("package 内 SHA256SUMS 未覆盖文件", auditSh, StringComparison.Ordinal);
        Assert.Contains("content/materials.json", auditSh, StringComparison.Ordinal);
        Assert.Contains("content/weapons.json", auditSh, StringComparison.Ordinal);
        Assert.Contains("content/textures/17_gravel.png", auditSh, StringComparison.Ordinal);
        Assert.Contains("content/textures/18_boundary_stone.png", auditSh, StringComparison.Ordinal);

        Assert.Contains("PixelEngine.Tools.DeterministicPackage", packagePs1, StringComparison.Ordinal);
        Assert.Contains("PixelEngine.Tools.DeterministicPackage", packageSh, StringComparison.Ordinal);
        Assert.Contains("tools\\PixelEngine.Tools.DeterministicPackage\\PixelEngine.Tools.DeterministicPackage.csproj", solution, StringComparison.Ordinal);
        Assert.Contains("$appDir = Join-Path $stagingDir 'app'", packagePs1, StringComparison.Ordinal);
        Assert.Contains("$ProductName = 'PixelEngine Demo'", packagePs1, StringComparison.Ordinal);
        Assert.Contains("$windowsLauncherName = \"$ProductName.exe\"", packagePs1, StringComparison.Ordinal);
        Assert.Contains("Set-AppHostRelativeAssemblyPath", packagePs1, StringComparison.Ordinal);
        Assert.Contains("$unixLauncherName = \"$ProductName.sh\"", packagePs1, StringComparison.Ordinal);
        Assert.Contains("README.txt", packagePs1, StringComparison.Ordinal);
        Assert.Contains("NOTICE.txt", packagePs1, StringComparison.Ordinal);
        Assert.Contains("RmlUi: MIT license", packagePs1, StringComparison.Ordinal);
        Assert.Contains("Remove-PlayerPackageNoise", packagePs1, StringComparison.Ordinal);
        Assert.Contains(".resources.dll", packagePs1, StringComparison.Ordinal);
        Assert.Contains("createdump.exe", packagePs1, StringComparison.Ordinal);
        Assert.Contains("Join-Path $stagingDir 'SHA256SUMS'", packagePs1, StringComparison.Ordinal);
        Assert.Contains("$packageDir = Join-Path $OutputRoot $packageName", packagePs1, StringComparison.Ordinal);
        Assert.Contains("Move-Item -LiteralPath $stagingDir -Destination $packageDir -Force", packagePs1, StringComparison.Ordinal);
        Assert.Contains("PlayerOutputDir", packagePs1, StringComparison.Ordinal);
        Assert.Contains("artifacts/PixelEngine Demo", packagePs1, StringComparison.Ordinal);
        Assert.Contains("Copy-Item -LiteralPath $packageDir -Destination $PlayerOutputDir", packagePs1, StringComparison.Ordinal);
        Assert.Contains("$samePairPattern", packagePs1, StringComparison.Ordinal);
        Assert.Contains("app_dir=\"$staging_dir/app\"", packageSh, StringComparison.Ordinal);
        Assert.Contains("launcher_base=\"${product_name:-PixelEngine Demo}\"", packageSh, StringComparison.Ordinal);
        Assert.Contains("windows_launcher=\"$launcher_base.exe\"", packageSh, StringComparison.Ordinal);
        Assert.Contains("patch_apphost_relative_assembly", packageSh, StringComparison.Ordinal);
        Assert.Contains("unix_launcher=\"$launcher_base.sh\"", packageSh, StringComparison.Ordinal);
        Assert.Contains("README.txt", packageSh, StringComparison.Ordinal);
        Assert.Contains("NOTICE.txt", packageSh, StringComparison.Ordinal);
        Assert.Contains("RmlUi: MIT license", packageSh, StringComparison.Ordinal);
        Assert.Contains("remove_player_package_noise", packageSh, StringComparison.Ordinal);
        Assert.Contains("rm -rf \"$app_dir/content\" \"$app_dir/_PUBLISH_INTERMEDIATE_README.txt\" \"$content_dir\"", packageSh, StringComparison.Ordinal);
        Assert.Contains("*.resources.dll", packageSh, StringComparison.Ordinal);
        Assert.Contains("createdump.exe", packageSh, StringComparison.Ordinal);
        Assert.Contains("package_checksum_path=\"$staging_dir/SHA256SUMS\"", packageSh, StringComparison.Ordinal);
        Assert.Contains("package_dir=\"$output_root/$package_name\"", packageSh, StringComparison.Ordinal);
        Assert.Contains("mv \"$staging_dir\" \"$package_dir\"", packageSh, StringComparison.Ordinal);
        Assert.Contains("player_output_dir", packageSh, StringComparison.Ordinal);
        Assert.Contains("artifacts/PixelEngine Demo", packageSh, StringComparison.Ordinal);
        Assert.Contains("cp -a \"$package_dir\" \"$player_output_dir\"", packageSh, StringComparison.Ordinal);
        Assert.Contains("PixelEngine-Demo-*-$rid-$channel.zip", packageSh, StringComparison.Ordinal);
        Assert.DoesNotContain("Compress-Archive", packagePs1, StringComparison.Ordinal);
        Assert.DoesNotContain("tar -czf", packagePs1, StringComparison.Ordinal);
        Assert.DoesNotContain("zip -qr", packageSh, StringComparison.Ordinal);
        Assert.DoesNotContain("tar -czf", packageSh, StringComparison.Ordinal);
        Assert.Contains("_PUBLISH_INTERMEDIATE_README.txt", publishR2rPs1, StringComparison.Ordinal);
        Assert.Contains("_PUBLISH_INTERMEDIATE_README.txt", publishAotPs1, StringComparison.Ordinal);
        Assert.Contains("_PUBLISH_INTERMEDIATE_README.txt", publishR2rSh, StringComparison.Ordinal);
        Assert.Contains("_PUBLISH_INTERMEDIATE_README.txt", publishAotSh, StringComparison.Ordinal);
        Assert.Contains("raw dotnet publish output", publishR2rPs1, StringComparison.Ordinal);
        Assert.Contains("not the player-facing package", publishAotPs1, StringComparison.Ordinal);
        Assert.Contains("_PUBLISH_INTERMEDIATE_README.txt", packagePs1, StringComparison.Ordinal);
        Assert.Contains("_PUBLISH_INTERMEDIATE_README.txt", packageSh, StringComparison.Ordinal);

        Assert.Contains("EvidenceManifestPath", evidence, StringComparison.Ordinal);
        Assert.Contains("schemaVersion", evidence, StringComparison.Ordinal);
        Assert.Contains("DeclaredSha256", evidence, StringComparison.Ordinal);
        Assert.Contains("缺少 sha256", evidence, StringComparison.Ordinal);
        Assert.Contains("sha256 不匹配", evidence, StringComparison.Ordinal);
        Assert.Contains("Read-MarkdownEvidenceTable", evidence, StringComparison.Ordinal);
        Assert.Contains("报告 $key 必须为 $expected", evidence, StringComparison.Ordinal);
        Assert.Contains("Get-ExpectedRunIdentity", evidence, StringComparison.Ordinal);
        Assert.Contains("Add-RunIdentityCheck", evidence, StringComparison.Ordinal);
        Assert.Contains("Add-WorkflowRunMetadataCheck", evidence, StringComparison.Ordinal);
        Assert.Contains("必须与 workflow_run 一致", evidence, StringComparison.Ordinal);
        Assert.Contains("workflow_run 报告 workflow 必须为 Release", evidence, StringComparison.Ordinal);
        Assert.Contains("workflow_run 报告 event 必须为 push/tag push", evidence, StringComparison.Ordinal);
        Assert.Contains("workflow_run 报告 run_attempt 必须为 >= 1 的整数", evidence, StringComparison.Ordinal);
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
        Assert.Contains("release-evidence-preflight.ps1", evidenceSh, StringComparison.Ordinal);
        Assert.Contains("--active-rids", evidenceSh, StringComparison.Ordinal);
        Assert.Contains("-ActiveRids", evidenceSh, StringComparison.Ordinal);
        Assert.Contains("--expected-package-count", evidenceSh, StringComparison.Ordinal);
        Assert.Contains("-ExpectedPackageCount", evidenceSh, StringComparison.Ordinal);
        Assert.Contains("--allow-blocked", evidenceSh, StringComparison.Ordinal);
        Assert.Contains("-AllowBlocked", evidenceSh, StringComparison.Ordinal);
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
        Assert.Contains("browser_download_url", evidence, StringComparison.Ordinal);
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
        Assert.Contains("browser_download_url", releaseReport, StringComparison.Ordinal);
        Assert.Contains("arm64_neon", releaseReport, StringComparison.Ordinal);
        Assert.Contains("pending review 误当成验收通过", releaseReport, StringComparison.Ordinal);
        Assert.Contains("release-evidence-manifest.example.json", releaseReport, StringComparison.Ordinal);
        Assert.Contains("tools/release-evidence-preflight.ps1", plan, StringComparison.Ordinal);
        Assert.Contains("tools/release-evidence-preflight.ps1|.sh", plan, StringComparison.Ordinal);

        string readme = ReadRepositoryFile("plan", "tasks", "70-evidence-contracts.md");
        Assert.Contains("active RID × `r2r/aot`", readme, StringComparison.Ordinal);
        Assert.Contains("`publish` / `verify` / `package_report` / `package` / 单一 `SHA256SUMS`", readme, StringComparison.Ordinal);
        Assert.Contains("`workflow_run`、`artifact_audit`、`github_release_upload`、`deterministic_hash`、`r2r_lightup`", readme, StringComparison.Ordinal);
        Assert.Contains("`simd_probe` scope", readme, StringComparison.Ordinal);
        Assert.Contains("`run_id` / `sha` / `workflow=Release` / `run_attempt` 同源", readme, StringComparison.Ordinal);
        Assert.Contains("`event=push` 且 `ref=refs/tags/v<semver>`", readme, StringComparison.Ordinal);
        Assert.Contains("`release_tag=true` 且 `tag` 与 ref 一致", readme, StringComparison.Ordinal);
        Assert.Contains("`uploaded_asset_count=packageCount+1`", readme, StringComparison.Ordinal);
        Assert.Contains("每个 package asset hash、唯一 `SHA256SUMS` hash 与每个上传资产的 `browser_download_url/<asset>`，下载 URL 必须绑定同一 release tag", readme, StringComparison.Ordinal);
        Assert.Contains("必须逐 active RID × channel 给出 `result=match` 明细行", readme, StringComparison.Ordinal);
        Assert.Contains("`require_all=true`", readme, StringComparison.Ordinal);
        Assert.Contains("`aot_dynamic_box2d_rejected=true`", readme, StringComparison.Ordinal);
        Assert.Contains("`package_layout_checked=true`", readme, StringComparison.Ordinal);
        Assert.Contains("`checksum_checked=true`", readme, StringComparison.Ordinal);
        Assert.Contains("`simdProbeKind`（x64=`x64_ymm_zmm`、arm64=`arm64_neon`）", readme, StringComparison.Ordinal);
        Assert.Contains("不能是 skip", readme, StringComparison.Ordinal);
        Assert.Contains("`codesign` / `notarization` success 报告", readme, StringComparison.Ordinal);
        Assert.Contains("仍需人工复核，不能解除 `REL-003`", readme, StringComparison.Ordinal);

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
        Assert.Contains("| workflow | Release |", release, StringComparison.Ordinal);
        Assert.Contains("| event | ${{ github.event_name }} |", release, StringComparison.Ordinal);
        Assert.Contains("| run_attempt | ${{ github.run_attempt }} |", release, StringComparison.Ordinal);
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
    /// 验证 Bash 发行证据预检入口复用 PowerShell 实现，并传递 active RID / package count 口径。
    /// </summary>
    [Fact]
    public void ReleaseEvidencePreflightBashEntryDelegatesActiveRidArguments()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string artifacts = "artifacts/test-release-evidence-preflight-bash-" + Guid.NewGuid().ToString("N");
        string artifactPath = Path.Combine(root, artifacts);

        try
        {
            ScriptResult result = RunBashScript(
                root,
                "tools/release-evidence-preflight.sh",
                "--evidence-manifest-path",
                "artifacts/missing-release-evidence.json",
                "--artifacts",
                artifacts,
                "--active-rids",
                "win-x64",
                "--expected-package-count",
                "2",
                "--allow-blocked");

            // Assert：验证预期结果
            Assert.Equal(0, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifactPath, "release-evidence-preflight.md"));
            Assert.Contains("| status | blocked_missing_release_manifest |", report, StringComparison.Ordinal);
            Assert.Contains("| required_rids | win-x64 |", report, StringComparison.Ordinal);
            Assert.Contains("| required_channels | r2r; aot |", report, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(artifactPath))
            {
                Directory.Delete(artifactPath, recursive: true);
            }
        }
    }

    /// <summary>
    /// 验证 package.ps1 输出玩家友好启动布局：包根有启动 exe/content，运行时依赖位于 app/。
    /// </summary>
    [Fact]
    public void PackageScriptPlacesRuntimeFilesUnderAppAndAuditRejectsRootClutter()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-friendly-package-" + Guid.NewGuid().ToString("N"));

        try
        {
            string publish = Path.Combine(temp, "publish");
            string content = Path.Combine(temp, "content");
            string packageRoot = Path.Combine(temp, "package");
            string playerOutput = Path.Combine(temp, "PixelEngine Demo");
            _ = Directory.CreateDirectory(publish);
            _ = Directory.CreateDirectory(Path.Combine(publish, "runtimes", "win-x64", "native"));
            _ = Directory.CreateDirectory(Path.Combine(publish, "zh-Hans"));
            _ = Directory.CreateDirectory(Path.Combine(content, "scenes"));
            _ = Directory.CreateDirectory(Path.Combine(content, "textures"));
            _ = Directory.CreateDirectory(Path.Combine(temp, "scripts"));
            _ = Directory.CreateDirectory(playerOutput);
            _ = Directory.CreateDirectory(Path.Combine(packageRoot, "PixelEngine-Demo-8.8.8-win-x64-r2r"));
            _ = Directory.CreateDirectory(Path.Combine(packageRoot, "PixelEngine-Demo-8.8.8-win-x64-aot"));
            _ = WriteTextEvidence(Path.Combine(playerOutput, "stale.dll"), "stale player output clutter");
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
            _ = WriteTextEvidence(Path.Combine(publish, "createdump.exe"), "dump helper");
            _ = WriteTextEvidence(Path.Combine(publish, "PixelEngine.Demo.deps.json"), "{}");
            _ = WriteTextEvidence(Path.Combine(publish, "PixelEngine.Demo.runtimeconfig.json"), "{}");
            _ = WriteTextEvidence(Path.Combine(publish, "_PUBLISH_INTERMEDIATE_README.txt"), "raw dotnet publish output");
            _ = WriteTextEvidence(Path.Combine(publish, "runtimes", "win-x64", "native", "box2d.dll"), "native");
            _ = WriteTextEvidence(Path.Combine(publish, "runtimes", "win-x64", "native", "PixelEngine.UI.Native.dll"), "ui native");
            _ = WriteTextEvidence(Path.Combine(content, "materials.json"), "{}");
            _ = WriteTextEvidence(Path.Combine(content, "reactions.json"), "{}");
            _ = WriteTextEvidence(Path.Combine(content, "weapons.json"), "{}");
            _ = WriteTextEvidence(Path.Combine(content, "textures", "17_gravel.png"), "gravel");
            _ = WriteTextEvidence(Path.Combine(content, "textures", "18_boundary_stone.png"), "boundary");
            _ = WriteTextEvidence(Path.Combine(content, "scenes", "lava-mine.scene"), "scene");
            _ = WriteTextEvidence(Path.Combine(temp, "scripts", "ExternalProjectBehaviour.cs"), "external project script");

            // Act：执行被测操作
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
                "-PlayerOutputDir",
                playerOutput,
                "-StartScene",
                "scenes/lava-mine.scene",
                "-WindowMode",
                "BorderlessFullscreen",
                "-ContentRoot",
                content);
            // Assert：验证不变式与预期结果
            Assert.Equal(0, package.ExitCode);
            Assert.Contains("PlayerOutput:", package.Output, StringComparison.Ordinal);

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
            JsonNode startup = JsonNode.Parse(
                File.ReadAllText(Path.Combine(expandedPackageDir, "content", "startup.json")))!;
            Assert.Equal("BorderlessFullscreen", startup["windowMode"]!.GetValue<string>());
            Assert.True(File.Exists(Path.Combine(expandedPackageDir, "content", "weapons.json")));
            Assert.True(File.Exists(Path.Combine(expandedPackageDir, "content", "textures", "17_gravel.png")));
            Assert.True(File.Exists(Path.Combine(expandedPackageDir, "content", "textures", "18_boundary_stone.png")));
            Assert.True(File.Exists(Path.Combine(expandedPackageDir, "content", "scripts", "ExternalProjectBehaviour.cs")));
            Assert.True(File.Exists(Path.Combine(expandedPackageDir, "NOTICE.txt")));
            Assert.True(File.Exists(Path.Combine(expandedPackageDir, "app", "runtimes", "win-x64", "native", "PixelEngine.UI.Native.dll")));
            Assert.False(File.Exists(Path.Combine(expandedPackageDir, "PixelEngine.Demo.dll")));
            Assert.False(File.Exists(Path.Combine(expandedPackageDir, "PixelEngine.Demo.pdb")));
            Assert.False(File.Exists(Path.Combine(expandedPackageDir, "PixelEngine.Demo.xml")));
            Assert.False(File.Exists(Path.Combine(expandedPackageDir, "_PUBLISH_INTERMEDIATE_README.txt")));
            Assert.False(File.Exists(Path.Combine(expandedPackageDir, "app", "_PUBLISH_INTERMEDIATE_README.txt")));
            Assert.False(File.Exists(Path.Combine(expandedPackageDir, "app", "PixelEngine.Demo.pdb")));
            Assert.False(File.Exists(Path.Combine(expandedPackageDir, "app", "PixelEngine.Demo.xml")));
            Assert.False(File.Exists(Path.Combine(expandedPackageDir, "app", "zh-Hans", "PixelEngine.Demo.resources.dll")));
            Assert.False(File.Exists(Path.Combine(expandedPackageDir, "app", "createdump.exe")));
            Assert.False(Directory.Exists(Path.Combine(expandedPackageDir, "app", "zh-Hans")));
            Assert.False(Directory.Exists(Path.Combine(expandedPackageDir, "app", "content")));
            Assert.True(File.Exists(Path.Combine(playerOutput, "PixelEngine Demo.exe")));
            Assert.True(File.Exists(Path.Combine(playerOutput, "NOTICE.txt")));
            Assert.True(File.Exists(Path.Combine(playerOutput, "app", "PixelEngine.Demo.dll")));
            Assert.True(File.Exists(Path.Combine(playerOutput, "app", "runtimes", "win-x64", "native", "PixelEngine.UI.Native.dll")));
            Assert.True(File.Exists(Path.Combine(playerOutput, "content", "materials.json")));
            Assert.True(File.Exists(Path.Combine(playerOutput, "content", "weapons.json")));
            Assert.True(File.Exists(Path.Combine(playerOutput, "content", "scripts", "ExternalProjectBehaviour.cs")));
            Assert.False(File.Exists(Path.Combine(playerOutput, "stale.dll")));
            Assert.False(File.Exists(Path.Combine(playerOutput, "PixelEngine.Demo.dll")));
            Assert.False(File.Exists(Path.Combine(playerOutput, "app", "PixelEngine.Demo.pdb")));
            Assert.False(File.Exists(Path.Combine(playerOutput, "app", "createdump.exe")));
            string extract = Path.Combine(temp, "extract");
            System.IO.Compression.ZipFile.ExtractToDirectory(archive, extract);
            string packageDir = Path.Combine(extract, "PixelEngine-Demo-9.9.9-win-x64-r2r");
            Assert.True(File.Exists(Path.Combine(packageDir, "PixelEngine Demo.exe")));
            Assert.True(File.Exists(Path.Combine(packageDir, "README.txt")));
            Assert.True(File.Exists(Path.Combine(packageDir, "NOTICE.txt")));
            string packageChecksums = Path.Combine(packageDir, "SHA256SUMS");
            Assert.True(File.Exists(packageChecksums));
            Assert.False(File.Exists(Path.Combine(packageDir, "app", "PixelEngine.Demo.exe")));
            Assert.True(File.Exists(Path.Combine(packageDir, "app", "PixelEngine.Demo.dll")));
            Assert.False(File.Exists(Path.Combine(packageDir, "app", "PixelEngine.Demo.pdb")));
            Assert.False(File.Exists(Path.Combine(packageDir, "app", "PixelEngine.Demo.xml")));
            Assert.False(File.Exists(Path.Combine(packageDir, "app", "zh-Hans", "PixelEngine.Demo.resources.dll")));
            Assert.False(File.Exists(Path.Combine(packageDir, "app", "createdump.exe")));
            Assert.False(Directory.Exists(Path.Combine(packageDir, "app", "zh-Hans")));
            Assert.True(File.Exists(Path.Combine(packageDir, "app", "PixelEngine.Demo.deps.json")));
            Assert.True(File.Exists(Path.Combine(packageDir, "app", "PixelEngine.Demo.runtimeconfig.json")));
            Assert.True(File.Exists(Path.Combine(packageDir, "app", "runtimes", "win-x64", "native", "PixelEngine.UI.Native.dll")));
            Assert.True(File.Exists(Path.Combine(packageDir, "content", "materials.json")));
            Assert.True(File.Exists(Path.Combine(packageDir, "content", "reactions.json")));
            Assert.True(File.Exists(Path.Combine(packageDir, "content", "weapons.json")));
            Assert.True(File.Exists(Path.Combine(packageDir, "content", "textures", "17_gravel.png")));
            Assert.True(File.Exists(Path.Combine(packageDir, "content", "textures", "18_boundary_stone.png")));
            Assert.True(File.Exists(Path.Combine(packageDir, "content", "scenes", "lava-mine.scene")));
            Assert.True(File.Exists(Path.Combine(packageDir, "content", "scripts", "ExternalProjectBehaviour.cs")));
            Assert.False(Directory.Exists(Path.Combine(packageDir, "app", "content")));
            Assert.False(File.Exists(Path.Combine(packageDir, "PixelEngine.Demo.dll")));
            Assert.False(File.Exists(Path.Combine(packageDir, "PixelEngine.Demo.pdb")));
            Assert.False(File.Exists(Path.Combine(packageDir, "PixelEngine.Demo.xml")));
            Assert.False(File.Exists(Path.Combine(packageDir, "PixelEngine.Demo.deps.json")));
            Assert.False(File.Exists(Path.Combine(packageDir, "PixelEngine.Demo.runtimeconfig.json")));
            Assert.False(File.Exists(Path.Combine(packageDir, "_PUBLISH_INTERMEDIATE_README.txt")));
            Assert.False(File.Exists(Path.Combine(packageDir, "app", "_PUBLISH_INTERMEDIATE_README.txt")));
            string packageChecksumsText = File.ReadAllText(packageChecksums);
            Assert.Contains("PixelEngine Demo.exe", packageChecksumsText, StringComparison.Ordinal);
            Assert.Contains("NOTICE.txt", packageChecksumsText, StringComparison.Ordinal);
            Assert.Contains("app/PixelEngine.Demo.dll", packageChecksumsText, StringComparison.Ordinal);
            Assert.Contains("app/runtimes/win-x64/native/PixelEngine.UI.Native.dll", packageChecksumsText, StringComparison.Ordinal);
            Assert.DoesNotContain("PixelEngine.Demo.pdb", packageChecksumsText, StringComparison.Ordinal);
            Assert.DoesNotContain("PixelEngine.Demo.xml", packageChecksumsText, StringComparison.Ordinal);
            Assert.DoesNotContain("PixelEngine.Demo.resources.dll", packageChecksumsText, StringComparison.Ordinal);
            Assert.DoesNotContain("createdump.exe", packageChecksumsText, StringComparison.Ordinal);
            Assert.DoesNotContain("_PUBLISH_INTERMEDIATE_README.txt", packageChecksumsText, StringComparison.Ordinal);
            Assert.Contains("content/materials.json", packageChecksumsText, StringComparison.Ordinal);
            Assert.Contains("content/weapons.json", packageChecksumsText, StringComparison.Ordinal);
            Assert.Contains("content/textures/17_gravel.png", packageChecksumsText, StringComparison.Ordinal);
            Assert.Contains("content/textures/18_boundary_stone.png", packageChecksumsText, StringComparison.Ordinal);
            Assert.Contains("content/scripts/ExternalProjectBehaviour.cs", packageChecksumsText, StringComparison.Ordinal);

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
            Assert.Contains("展开 package 不应包含玩家无关的调试、文档、诊断辅助或本地化卫星资源文件", appNoiseAudit.Output, StringComparison.Ordinal);
            File.Delete(Path.Combine(expandedPackageDir, "app", "PixelEngine.Demo.pdb"));

            _ = WriteTextEvidence(Path.Combine(expandedPackageDir, "app", "createdump.exe"), "dump helper");
            ScriptResult dumpHelperAudit = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "audit-release-artifacts.ps1"),
                "-PublishRoot",
                Path.Combine(temp, "missing-publish"),
                "-PackageRoot",
                packageRoot);
            Assert.NotEqual(0, dumpHelperAudit.ExitCode);
            Assert.Contains("展开 package 不应包含玩家无关的调试、文档、诊断辅助或本地化卫星资源文件", dumpHelperAudit.Output, StringComparison.Ordinal);
            File.Delete(Path.Combine(expandedPackageDir, "app", "createdump.exe"));

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

            string expandedUiNative = Path.Combine(expandedPackageDir, "app", "runtimes", "win-x64", "native", "PixelEngine.UI.Native.dll");
            File.Delete(expandedUiNative);
            ScriptResult missingUiNativeAudit = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "audit-release-artifacts.ps1"),
                "-PublishRoot",
                Path.Combine(temp, "missing-publish"),
                "-PackageRoot",
                packageRoot);
            Assert.NotEqual(0, missingUiNativeAudit.ExitCode);
            Assert.Contains("展开 package 缺少玩家友好布局入口、app 依赖或 content 内容", missingUiNativeAudit.Output, StringComparison.Ordinal);
            Assert.Contains("app/runtimes/win-x64/native/PixelEngine.UI.Native.dll", missingUiNativeAudit.Output, StringComparison.Ordinal);
            _ = WriteTextEvidence(expandedUiNative, "ui native");

            ScriptResult audit = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "audit-release-artifacts.ps1"),
                "-PublishRoot",
                Path.Combine(temp, "missing-publish"),
                "-PackageRoot",
                packageRoot);
            Assert.True(audit.ExitCode == 0, audit.Output);
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
    /// 验证 PowerShell 发行审计真实执行时拒绝 AOT package 携带动态 UI native。
    /// </summary>
    [Fact]
    public void PowerShellReleaseArtifactAuditRejectsUiNativeInAotPackage()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-pwsh-ui-native-aot-audit-" + Guid.NewGuid().ToString("N"));

        try
        {
            string packageRoot = Path.Combine(temp, "package");
            string packageName = "PixelEngine-Demo-9.9.9-win-x64-aot";
            string expandedPackage = Path.Combine(packageRoot, packageName);
            CreateFriendlyExpandedPackage(expandedPackage, channel: "aot", includeUiNative: false);
            string archive = Path.Combine(packageRoot, packageName + ".zip");
            CreateZipWithRoot(expandedPackage, archive, packageName);
            _ = WriteTextEvidence(Path.Combine(packageRoot, "SHA256SUMS"), $"{GetSha256(archive)}  {Path.GetFileName(archive)}{Environment.NewLine}");

            // Act：执行被测操作
            ScriptResult audit = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "audit-release-artifacts.ps1"),
                "-PublishRoot",
                Path.Combine(temp, "missing-publish"),
                "-PackageRoot",
                packageRoot,
                "-ActiveRids",
                "win-x64");

            // Assert：验证不变式与预期结果
            Assert.True(audit.ExitCode == 0, audit.Output);
            Assert.Contains("Package audit passed. Packages: 1. Expanded: 1.", audit.Output, StringComparison.Ordinal);

            _ = WriteTextEvidence(Path.Combine(expandedPackage, "app", "runtimes", "win-x64", "native", "PixelEngine.UI.Native.dll"), "ui native");
            ScriptResult forbiddenUiNative = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "audit-release-artifacts.ps1"),
                "-PublishRoot",
                Path.Combine(temp, "missing-publish"),
                "-PackageRoot",
                packageRoot,
                "-ActiveRids",
                "win-x64");

            Assert.NotEqual(0, forbiddenUiNative.ExitCode);
            Assert.Contains("app/runtimes/win-x64/native/PixelEngine.UI.Native.dll", forbiddenUiNative.Output, StringComparison.Ordinal);
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
    /// 验证 PowerShell 发行审计拒绝未激活 Ultralight optional profile 的 native 混入玩家包。
    /// </summary>
    [Fact]
    public void PowerShellReleaseArtifactAuditRejectsInactiveUltralightNativeInPackage()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-pwsh-ultralight-native-audit-" + Guid.NewGuid().ToString("N"));

        try
        {
            string packageRoot = Path.Combine(temp, "package");
            string packageName = "PixelEngine-Demo-9.9.9-win-x64-r2r";
            string expandedPackage = Path.Combine(packageRoot, packageName);
            CreateFriendlyExpandedPackage(expandedPackage, channel: "r2r", includeUiNative: true);
            string archive = Path.Combine(packageRoot, packageName + ".zip");
            CreateZipWithRoot(expandedPackage, archive, packageName);
            _ = WriteTextEvidence(Path.Combine(packageRoot, "SHA256SUMS"), $"{GetSha256(archive)}  {Path.GetFileName(archive)}{Environment.NewLine}");

            // Act：执行被测操作
            ScriptResult clean = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "audit-release-artifacts.ps1"),
                "-PublishRoot",
                Path.Combine(temp, "missing-publish"),
                "-PackageRoot",
                packageRoot,
                "-ActiveRids",
                "win-x64");
            // Assert：验证不变式与预期结果
            Assert.True(clean.ExitCode == 0, clean.Output);

            string ultralightNative = Path.Combine(expandedPackage, "app", "runtimes", "win-x64", "native", "Ultralight.dll");
            _ = WriteTextEvidence(ultralightNative, "inactive ultralight native");
            RewriteFriendlyExpandedPackageChecksum(expandedPackage);
            CreateZipWithRoot(expandedPackage, archive, packageName);
            _ = WriteTextEvidence(Path.Combine(packageRoot, "SHA256SUMS"), $"{GetSha256(archive)}  {Path.GetFileName(archive)}{Environment.NewLine}");

            ScriptResult forbidden = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "audit-release-artifacts.ps1"),
                "-PublishRoot",
                Path.Combine(temp, "missing-publish"),
                "-PackageRoot",
                packageRoot,
                "-ActiveRids",
                "win-x64");

            Assert.NotEqual(0, forbidden.ExitCode);
            Assert.Contains("Ultralight native", forbidden.Output, StringComparison.Ordinal);
            Assert.Contains("commercial redistribution license", forbidden.Output, StringComparison.Ordinal);
            Assert.Contains("app/runtimes/win-x64/native/Ultralight.dll", forbidden.Output, StringComparison.Ordinal);
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
    /// 验证 Bash 发行审计真实执行时也强制 R2R package 携带 UI native。
    /// </summary>
    [Fact]
    public void BashReleaseArtifactAuditRequiresUiNativeInR2RPackage()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-bash-ui-native-audit-" + Guid.NewGuid().ToString("N"));

        try
        {
            string packageRoot = Path.Combine(temp, "package");
            string packageName = "PixelEngine-Demo-9.9.9-win-x64-r2r";
            string expandedPackage = Path.Combine(packageRoot, packageName);
            CreateFriendlyExpandedPackage(expandedPackage, channel: "r2r", includeUiNative: true);
            string archive = Path.Combine(packageRoot, packageName + ".zip");
            CreateZipWithRoot(expandedPackage, archive, packageName);
            _ = WriteTextEvidence(Path.Combine(packageRoot, "SHA256SUMS"), $"{GetSha256(archive)}  {Path.GetFileName(archive)}{Environment.NewLine}");

            ScriptResult audit = RunBashScript(
                root,
                "tools/audit-release-artifacts.sh",
                "--publish-root",
                ToBashPath(Path.Combine(temp, "missing-publish")),
                "--package-root",
                ToBashPath(packageRoot),
                "--active-rids",
                "win-x64");

            // Assert：验证预期结果
            Assert.Equal(0, audit.ExitCode);
            Assert.Contains("Package OK: 1 expanded=1", audit.Output, StringComparison.Ordinal);

            File.Delete(Path.Combine(expandedPackage, "app", "runtimes", "win-x64", "native", "PixelEngine.UI.Native.dll"));
            ScriptResult missingUiNative = RunBashScript(
                root,
                "tools/audit-release-artifacts.sh",
                "--publish-root",
                ToBashPath(Path.Combine(temp, "missing-publish")),
                "--package-root",
                ToBashPath(packageRoot),
                "--active-rids",
                "win-x64");

            Assert.NotEqual(0, missingUiNative.ExitCode);
            Assert.Contains("app/runtimes/win-x64/native/PixelEngine.UI.Native.dll", missingUiNative.Output, StringComparison.Ordinal);
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
    /// 验证 Bash 发行审计真实执行时拒绝 AOT package 携带动态 UI native。
    /// </summary>
    [Fact]
    public void BashReleaseArtifactAuditRejectsUiNativeInAotPackage()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-bash-ui-native-aot-audit-" + Guid.NewGuid().ToString("N"));

        try
        {
            string packageRoot = Path.Combine(temp, "package");
            string packageName = "PixelEngine-Demo-9.9.9-win-x64-aot";
            string expandedPackage = Path.Combine(packageRoot, packageName);
            CreateFriendlyExpandedPackage(expandedPackage, channel: "aot", includeUiNative: false);
            string archive = Path.Combine(packageRoot, packageName + ".zip");
            CreateZipWithRoot(expandedPackage, archive, packageName);
            _ = WriteTextEvidence(Path.Combine(packageRoot, "SHA256SUMS"), $"{GetSha256(archive)}  {Path.GetFileName(archive)}{Environment.NewLine}");

            ScriptResult audit = RunBashScript(
                root,
                "tools/audit-release-artifacts.sh",
                "--publish-root",
                ToBashPath(Path.Combine(temp, "missing-publish")),
                "--package-root",
                ToBashPath(packageRoot),
                "--active-rids",
                "win-x64");

            // Assert：验证预期结果
            Assert.Equal(0, audit.ExitCode);
            Assert.Contains("Package OK: 1 expanded=1", audit.Output, StringComparison.Ordinal);

            _ = WriteTextEvidence(Path.Combine(expandedPackage, "app", "runtimes", "win-x64", "native", "PixelEngine.UI.Native.dll"), "ui native");
            ScriptResult forbiddenUiNative = RunBashScript(
                root,
                "tools/audit-release-artifacts.sh",
                "--publish-root",
                ToBashPath(Path.Combine(temp, "missing-publish")),
                "--package-root",
                ToBashPath(packageRoot),
                "--active-rids",
                "win-x64");

            Assert.NotEqual(0, forbiddenUiNative.ExitCode);
            Assert.Contains("app/runtimes/win-x64/native/PixelEngine.UI.Native.dll", forbiddenUiNative.Output, StringComparison.Ordinal);
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
    /// 验证 Bash 发行审计拒绝未激活 Ultralight optional profile 的 native 混入玩家包。
    /// </summary>
    [Fact]
    public void BashReleaseArtifactAuditRejectsInactiveUltralightNativeInPackage()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-bash-ultralight-native-audit-" + Guid.NewGuid().ToString("N"));

        try
        {
            string packageRoot = Path.Combine(temp, "package");
            string packageName = "PixelEngine-Demo-9.9.9-win-x64-r2r";
            string expandedPackage = Path.Combine(packageRoot, packageName);
            CreateFriendlyExpandedPackage(expandedPackage, channel: "r2r", includeUiNative: true);
            string archive = Path.Combine(packageRoot, packageName + ".zip");
            CreateZipWithRoot(expandedPackage, archive, packageName);
            _ = WriteTextEvidence(Path.Combine(packageRoot, "SHA256SUMS"), $"{GetSha256(archive)}  {Path.GetFileName(archive)}{Environment.NewLine}");

            ScriptResult clean = RunBashScript(
                root,
                "tools/audit-release-artifacts.sh",
                "--publish-root",
                ToBashPath(Path.Combine(temp, "missing-publish")),
                "--package-root",
                ToBashPath(packageRoot),
                "--active-rids",
                "win-x64");
            // Assert：验证预期结果
            Assert.True(clean.ExitCode == 0, clean.Output);

            _ = WriteTextEvidence(Path.Combine(expandedPackage, "app", "runtimes", "win-x64", "native", "WebCore.dll"), "inactive ultralight native");
            RewriteFriendlyExpandedPackageChecksum(expandedPackage);
            CreateZipWithRoot(expandedPackage, archive, packageName);
            _ = WriteTextEvidence(Path.Combine(packageRoot, "SHA256SUMS"), $"{GetSha256(archive)}  {Path.GetFileName(archive)}{Environment.NewLine}");

            ScriptResult forbidden = RunBashScript(
                root,
                "tools/audit-release-artifacts.sh",
                "--publish-root",
                ToBashPath(Path.Combine(temp, "missing-publish")),
                "--package-root",
                ToBashPath(packageRoot),
                "--active-rids",
                "win-x64");

            Assert.NotEqual(0, forbidden.ExitCode);
            Assert.Contains("Ultralight native", forbidden.Output, StringComparison.Ordinal);
            Assert.Contains("commercial redistribution license", forbidden.Output, StringComparison.Ordinal);
            Assert.Contains("app/runtimes/win-x64/native/WebCore.dll", forbidden.Output, StringComparison.Ordinal);
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
    /// 验证 Bash 发行审计按真实 NuGet 文件名前缀拒绝编辑器专属 ImGuizmo/ImPlot 闭包。
    /// </summary>
    [Fact]
    public void BashReleaseArtifactAuditRejectsHexaNamedEditorUiClosure()
    {
        // Arrange：准备输入与初始状态
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-bash-editor-closure-audit-" + Guid.NewGuid().ToString("N"));

        try
        {
            string packageRoot = Path.Combine(temp, "package");
            string packageName = "PixelEngine-Demo-9.9.9-win-x64-r2r";
            string expandedPackage = Path.Combine(packageRoot, packageName);
            CreateFriendlyExpandedPackage(expandedPackage, channel: "r2r", includeUiNative: true);
            string archive = Path.Combine(packageRoot, packageName + ".zip");
            CreateZipWithRoot(expandedPackage, archive, packageName);
            _ = WriteTextEvidence(Path.Combine(packageRoot, "SHA256SUMS"), $"{GetSha256(archive)}  {Path.GetFileName(archive)}{Environment.NewLine}");

            ScriptResult clean = RunBashScript(
                root,
                "tools/audit-release-artifacts.sh",
                "--publish-root",
                ToBashPath(Path.Combine(temp, "missing-publish")),
                "--package-root",
                ToBashPath(packageRoot),
                "--active-rids",
                "win-x64");

            // Assert：验证预期结果
            Assert.Equal(0, clean.ExitCode);

            _ = WriteTextEvidence(Path.Combine(expandedPackage, "app", "Hexa.NET.ImGuizmo.dll"), "guizmo");
            RewriteFriendlyExpandedPackageChecksum(expandedPackage);
            CreateZipWithRoot(expandedPackage, archive, packageName);
            _ = WriteTextEvidence(Path.Combine(packageRoot, "SHA256SUMS"), $"{GetSha256(archive)}  {Path.GetFileName(archive)}{Environment.NewLine}");

            ScriptResult guizmo = RunBashScript(
                root,
                "tools/audit-release-artifacts.sh",
                "--publish-root",
                ToBashPath(Path.Combine(temp, "missing-publish")),
                "--package-root",
                ToBashPath(packageRoot),
                "--active-rids",
                "win-x64");

            Assert.NotEqual(0, guizmo.ExitCode);
            Assert.Contains("app/Hexa.NET.ImGuizmo.dll", guizmo.Output, StringComparison.Ordinal);
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
    /// 验证 hosted Windows 原生 Python 的 CRLF ZIP listing 不会污染 archive entry identity。
    /// </summary>
    [Fact]
    public void BashReleaseArtifactAuditAcceptsCrLfPythonZipListing()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-bash-python-crlf-audit-" + Guid.NewGuid().ToString("N"));

        try
        {
            string packageRoot = Path.Combine(temp, "package");
            List<string> checksumLines = [];
            foreach (string channel in new[] { "r2r", "aot" })
            {
                string packageName = $"PixelEngine-Demo-9.9.9-win-x64-{channel}";
                string expandedPackage = Path.Combine(packageRoot, packageName);
                CreateFriendlyExpandedPackage(
                    expandedPackage,
                    channel,
                    includeUiNative: string.Equals(channel, "r2r", StringComparison.Ordinal));
                string archive = Path.Combine(packageRoot, packageName + ".zip");
                CreateZipWithRoot(expandedPackage, archive, packageName);
                checksumLines.Add($"{GetSha256(archive)}  {Path.GetFileName(archive)}");
            }

            _ = WriteTextEvidence(
                Path.Combine(packageRoot, "SHA256SUMS"),
                string.Join(Environment.NewLine, checksumLines) + Environment.NewLine);
            string shimDirectory = CreateCrLfPython3Shim(Path.Combine(temp, "shim"));
            string path = shimDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");

            ScriptResult audit = RunBashScriptWithEnvironment(
                root,
                "tools/audit-release-artifacts.sh",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["PATH"] = path },
                "--publish-root",
                ToBashPath(Path.Combine(temp, "missing-publish")),
                "--package-root",
                ToBashPath(packageRoot),
                "--active-rids",
                "win-x64");

            Assert.True(audit.ExitCode == 0, audit.Output);
            Assert.Contains("Package OK: 2 expanded=2", audit.Output, StringComparison.Ordinal);
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
        // Arrange：准备输入与初始状态
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

            // Assert：验证预期结果
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
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-release-evidence-" + Guid.NewGuid().ToString("N"));

        try
        {
            string goodManifest = CreateReleaseEvidenceManifest(temp, packageConclusion: "success");
            string badManifest = CreateReleaseEvidenceManifest(temp, packageConclusion: "failure", suffix: "bad");
            string badR2RManifest = CreateReleaseEvidenceManifest(temp, packageConclusion: "success", r2rLightupConclusion: "failure", suffix: "bad-r2r");

            string badArtifacts = Path.Combine(temp, "bad-out");
            // Act：执行被测操作
            ScriptResult bad = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "release-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                badManifest,
                "-Artifacts",
                badArtifacts);
            // Assert：验证不变式与预期结果
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
    /// 验证 release evidence 预检会拒绝错误 workflow/event/run_attempt 元数据，避免其它 workflow 报告冒充发布证据。
    /// </summary>
    [Fact]
    public void ReleaseEvidencePreflightRejectsWrongWorkflowMetadata()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-release-workflow-metadata-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateReleaseEvidenceManifest(temp, packageConclusion: "success");
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            string workflowRunReport = (string)rootNode["workflowRunReport"]!;
            string text = File.ReadAllText(workflowRunReport)
                .Replace("| workflow | Release |", "| workflow | CI |", StringComparison.Ordinal)
                .Replace("| event | push |", "| event | pull_request |", StringComparison.Ordinal)
                .Replace("| run_attempt | 1 |", "| run_attempt | 0 |", StringComparison.Ordinal);
            File.WriteAllText(workflowRunReport, text);
            rootNode["workflowRunSha256"] = GetSha256(workflowRunReport);
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "workflow-metadata-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "release-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "release-evidence-preflight.md"));
            Assert.Contains("status | blocked_missing_release_scope_evidence", result.Output + report, StringComparison.Ordinal);
            Assert.Contains("workflow_run 报告 workflow 必须为 Release", report, StringComparison.Ordinal);
            Assert.Contains("workflow_run 报告 event 必须为 push/tag push", report, StringComparison.Ordinal);
            Assert.Contains("workflow_run 报告 run_attempt 必须为 >= 1 的整数", report, StringComparison.Ordinal);
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
    /// 验证 workflow_dispatch 即使声明 tag ref / release_tag=true，也不能冒充 tag push Release 证据。
    /// </summary>
    [Fact]
    public void ReleaseEvidencePreflightRejectsWorkflowDispatchEvenWithTagRef()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-release-dispatch-tag-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateReleaseEvidenceManifest(temp, packageConclusion: "success", suffix: "dispatch-tag");
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            string workflowRunReport = (string)rootNode["workflowRunReport"]!;
            string text = File.ReadAllText(workflowRunReport)
                .Replace("| event | push |", "| event | workflow_dispatch |", StringComparison.Ordinal);
            File.WriteAllText(workflowRunReport, text);
            rootNode["workflowRunSha256"] = GetSha256(workflowRunReport);
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "dispatch-tag-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "release-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
            Assert.Equal(5, result.ExitCode);
            string report = File.ReadAllText(Path.Combine(artifacts, "release-evidence-preflight.md"));
            Assert.Contains("status | blocked_missing_release_scope_evidence", result.Output + report, StringComparison.Ordinal);
            Assert.Contains("workflow_run 报告 event 必须为 push/tag push", report, StringComparison.Ordinal);
            Assert.Contains("workflow_dispatch", report, StringComparison.Ordinal);
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
    /// 验证 release evidence 预检会把 schema/JSON 错误落成稳定报告，而不是直接抛出无报告异常。
    /// </summary>
    [Fact]
    public void ReleaseEvidencePreflightRejectsInvalidSchemaWithReport()
    {
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-release-schema-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateReleaseEvidenceManifest(temp, packageConclusion: "success");
            string json = File.ReadAllText(manifest).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);
            File.WriteAllText(manifest, json);

            string artifacts = Path.Combine(temp, "schema-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "release-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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
        // Arrange：搭建测试场景与依赖
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-release-malformed-json-" + Guid.NewGuid().ToString("N"));

        try
        {
            _ = Directory.CreateDirectory(temp);
            string manifest = Path.Combine(temp, "release-evidence.json");
            File.WriteAllText(manifest, "{ invalid");

            string artifacts = Path.Combine(temp, "json-out");
            // Act：执行被测操作
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "release-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            // Assert：验证不变式与预期结果
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
    /// 验证 release evidence 预检要求所有报告来自同一个 workflow / run / attempt / commit。
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
            string text = File.ReadAllText(publishReport)
                .Replace("| workflow | Release |", "| workflow | CI |", StringComparison.Ordinal)
                .Replace("| sha | abc |", "| sha | different-commit |", StringComparison.Ordinal)
                .Replace("| run_attempt | 1 |", "| run_attempt | 2 |", StringComparison.Ordinal);
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
            Assert.Contains("linux-x64/r2r/publish 报告 workflow 必须与 workflow_run 一致", report, StringComparison.Ordinal);
            Assert.Contains("linux-x64/r2r/publish 报告 sha 必须与 workflow_run 一致", report, StringComparison.Ordinal);
            Assert.Contains("linux-x64/r2r/publish 报告 run_attempt 必须与 workflow_run 一致", report, StringComparison.Ordinal);
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
    /// 验证 release evidence 预检拒绝缺少 workflow / run_attempt 的子报告，避免旧报告冒充同源 release evidence。
    /// </summary>
    [Fact]
    public void ReleaseEvidencePreflightRejectsReportMissingRunIdentityFields()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-release-missing-run-identity-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateReleaseEvidenceManifest(temp, packageConclusion: "success");
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonObject publishNode = rootNode["artifacts"]!["linux-x64"]!["r2r"]!.AsObject();
            string publishReport = (string)publishNode["publishReport"]!;
            string text = File.ReadAllText(publishReport)
                .Replace("| workflow | Release |" + Environment.NewLine, string.Empty, StringComparison.Ordinal)
                .Replace("| run_attempt | 1 |" + Environment.NewLine, string.Empty, StringComparison.Ordinal);
            File.WriteAllText(publishReport, text);
            publishNode["publishSha256"] = GetSha256(publishReport);
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "missing-run-identity-out");
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
            Assert.Contains("linux-x64/r2r/publish 报告缺少 workflow 字段，不能证明与 workflow_run 同源", report, StringComparison.Ordinal);
            Assert.Contains("linux-x64/r2r/publish 报告缺少 run_attempt 字段，不能证明与 workflow_run 同源", report, StringComparison.Ordinal);
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
    /// 验证 GitHub Release 上传报告必须列出 12 个 package 与 SHA256SUMS 的 asset/hash 覆盖，不能只靠 success 冒充上传完成。
    /// </summary>
    [Fact]
    public void ReleaseEvidencePreflightRejectsUploadReportWithoutAssetCoverage()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-release-upload-assets-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateReleaseEvidenceManifest(
                temp,
                packageConclusion: "success",
                suffix: "missing-upload-assets",
                includeUploadAssetCoverage: false);

            string artifacts = Path.Combine(temp, "missing-upload-assets-out");
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
            Assert.Contains("github_release_upload 报告缺少 uploaded_asset_count 字段", report, StringComparison.Ordinal);
            Assert.Contains("github_release_upload 缺少 SHA256SUMS 上传证据", report, StringComparison.Ordinal);
            Assert.Contains("github_release_upload 缺少上传 asset：PixelEngine-Demo-0.1.0-win-x64-r2r.zip", report, StringComparison.Ordinal);
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
    /// 验证 GitHub Release 上传报告必须包含每个资产的 browser_download_url，不能只列 hash。
    /// </summary>
    [Fact]
    public void ReleaseEvidencePreflightRejectsUploadReportWithoutDownloadUrls()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-release-upload-download-url-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateReleaseEvidenceManifest(temp, packageConclusion: "success");
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonObject githubRelease = rootNode["githubRelease"]!.AsObject();
            string upload = (string)githubRelease["uploadReport"]!;
            IEnumerable<string> lines = File.ReadAllLines(upload)
                .Where(static line => !line.Contains("browser_download_url/", StringComparison.Ordinal));
            File.WriteAllLines(upload, lines);
            githubRelease["uploadSha256"] = GetSha256(upload);
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "missing-download-url-out");
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
            Assert.Contains("github_release_upload 缺少 browser_download_url：PixelEngine-Demo-0.1.0-win-x64-r2r.zip", report, StringComparison.Ordinal);
            Assert.Contains("github_release_upload 缺少 browser_download_url：SHA256SUMS", report, StringComparison.Ordinal);
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
    /// 验证 GitHub Release 下载 URL 必须绑定到 workflow tag，不能指向其它 tag 的资产。
    /// </summary>
    [Fact]
    public void ReleaseEvidencePreflightRejectsDownloadUrlsFromDifferentTag()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-release-upload-download-tag-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateReleaseEvidenceManifest(temp, packageConclusion: "success");
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonObject githubRelease = rootNode["githubRelease"]!.AsObject();
            string upload = (string)githubRelease["uploadReport"]!;
            string text = File.ReadAllText(upload).Replace(
                "/releases/download/v0.1.0/",
                "/releases/download/v0.0.9/",
                StringComparison.Ordinal);
            File.WriteAllText(upload, text);
            githubRelease["uploadSha256"] = GetSha256(upload);
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "wrong-download-tag-out");
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
            Assert.Contains("github_release_upload browser_download_url 必须指向 GitHub Release 下载资产：PixelEngine-Demo-0.1.0-win-x64-r2r.zip", report, StringComparison.Ordinal);
            Assert.Contains("/releases/download/v0.0.9/", report, StringComparison.Ordinal);
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
    /// 验证 release manifest 中 package 文件名必须与所在 RID/channel 节点和 tag version 一致。
    /// </summary>
    [Fact]
    public void ReleaseEvidencePreflightRejectsPackageNameNotMatchingArtifactNode()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-release-package-name-node-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateReleaseEvidenceManifest(temp, packageConclusion: "success");
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonObject node = rootNode["artifacts"]!["win-x64"]!["r2r"]!.AsObject();
            string oldPackage = (string)node["package"]!;
            string badPackage = Path.Combine(
                Path.GetDirectoryName(oldPackage)!,
                "PixelEngine-Demo-0.1.0-linux-x64-r2r.zip");
            File.Copy(oldPackage, badPackage);
            node["package"] = badPackage;
            node["packageSha256"] = GetSha256(badPackage);
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "package-name-node-out");
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
            Assert.Contains("artifacts.win-x64.r2r.package 文件名必须匹配 PixelEngine-Demo-<version>-win-x64-r2r.zip", report, StringComparison.Ordinal);
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
    /// 验证 AOT SIMD probe markdown 报告自身必须声明与 manifest 一致的 simdProbeKind。
    /// </summary>
    [Fact]
    public void ReleaseEvidencePreflightRejectsSimdProbeReportKindMismatch()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(Path.GetTempPath(), "pixelengine-release-simd-kind-" + Guid.NewGuid().ToString("N"));

        try
        {
            string manifest = CreateReleaseEvidenceManifest(temp, packageConclusion: "success");
            JsonObject rootNode = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            JsonObject node = rootNode["artifacts"]!["win-arm64"]!["aot"]!.AsObject();
            string simdProbe = (string)node["simdProbe"]!;
            string report = File.ReadAllText(simdProbe).Replace(
                "| simdProbeKind | arm64_neon |",
                "| simdProbeKind | x64_ymm_zmm |",
                StringComparison.Ordinal);
            File.WriteAllText(simdProbe, report);
            node["simdProbeSha256"] = GetSha256(simdProbe);
            File.WriteAllText(manifest, rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            string artifacts = Path.Combine(temp, "simd-kind-out");
            ScriptResult result = RunPowerShellScript(
                root,
                Path.Combine(root, "tools", "release-evidence-preflight.ps1"),
                "-EvidenceManifestPath",
                manifest,
                "-Artifacts",
                artifacts);

            Assert.Equal(5, result.ExitCode);
            string preflightReport = File.ReadAllText(Path.Combine(artifacts, "release-evidence-preflight.md"));
            Assert.Contains("blocked_missing_release_scope_evidence", result.Output + preflightReport, StringComparison.Ordinal);
            Assert.Contains("artifacts.win-arm64.aot.simdProbe 报告 simdProbeKind 必须为 arm64_neon，实际为 x64_ymm_zmm", preflightReport, StringComparison.Ordinal);
            Assert.DoesNotContain("status | release_evidence_attached_pending_review", preflightReport, StringComparison.Ordinal);
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

    // —— 热路径与计划文档索引 ——
    public void SimulationHotNeighborAccessUsesUnsafeBaseRefs()
    {
        string chunk = ReadRepositoryFile("src", "PixelEngine.Simulation", "Chunk.cs");
        string window = ReadRepositoryFile("src", "PixelEngine.Simulation", "NeighborWindow.cs");
        string updater = ReadRepositoryFile("src", "PixelEngine.Simulation", "ChunkUpdater.cs");

        Assert.Contains("MemoryMarshal.GetArrayDataReference(_materialBuffer)", chunk, StringComparison.Ordinal);
        Assert.Contains("MemoryMarshal.GetArrayDataReference(FlagsBuffer)", chunk, StringComparison.Ordinal);
        Assert.Contains("MemoryMarshal.GetArrayDataReference(LifetimeBuffer)", chunk, StringComparison.Ordinal);

        Assert.Contains("ref struct NeighborWindow", window, StringComparison.Ordinal);
        Assert.Contains("ref ushort _matBase0", window, StringComparison.Ordinal);
        Assert.Contains("ref byte _flagsBase0", window, StringComparison.Ordinal);
        Assert.Contains("ref byte _lifeBase0", window, StringComparison.Ordinal);
        Assert.Contains("Unsafe.Add(ref SelectMaterialBase(slot), local)", window, StringComparison.Ordinal);
        Assert.Contains("Unsafe.Add(ref SelectFlagsBase(slot), local)", window, StringComparison.Ordinal);
        Assert.Contains("Unsafe.Add(ref SelectLifetimeBase(slot), local)", window, StringComparison.Ordinal);
        Assert.Contains("TryMoveCellFromCenterKnownCenterTarget", window, StringComparison.Ordinal);
        Assert.Contains("MoveCellFromCenterKnownEligibleCenterTarget", window, StringComparison.Ordinal);
        Assert.Contains("TryFindFirstOccupiedBelow", window, StringComparison.Ordinal);

        Assert.Contains("private readonly uint[] _columnOccupancy", chunk, StringComparison.Ordinal);
        Assert.Contains("FindFirstOccupiedInColumn", chunk, StringComparison.Ordinal);
        Assert.Contains("_columnOccupancyValid = false;", chunk, StringComparison.Ordinal);

        Assert.Contains("NeighborWindow window = new(chunk.Coord, in neighborhood);", updater, StringComparison.Ordinal);
        Assert.Contains("ref ushort materialBase = ref chunk.GetMaterialBase();", updater, StringComparison.Ordinal);
        Assert.Contains("ref byte flagsBase = ref chunk.GetFlagsBase();", updater, StringComparison.Ordinal);
        Assert.Contains("ref byte lifetimeBase = ref chunk.GetLifetimeBase();", updater, StringComparison.Ordinal);
        Assert.Contains("int localOffset = (ly * EngineConstants.ChunkSize) + rect.MinX;", updater, StringComparison.Ordinal);
        Assert.Contains("Unsafe.Add(ref materialBase, localOffset)", updater, StringComparison.Ordinal);
        Assert.Contains("Unsafe.Add(ref flagsBase, localOffset)", updater, StringComparison.Ordinal);
        Assert.Contains("ref byte lifetime = ref Unsafe.Add(ref lifetimeBase, localOffset);", updater, StringComparison.Ordinal);
        Assert.Contains("ProcessLifetime(ref window, chunk, lifetimeSink, wx, wy, material, parityBit, ref lifetime);", updater, StringComparison.Ordinal);
        Assert.Contains("ushort activeMaterial = material;", updater, StringComparison.Ordinal);
        Assert.Contains("materials.Hot.CellUpdatePropertiesOfUnchecked(material)", updater, StringComparison.Ordinal);
        Assert.Contains("MaterialHotTable.CellUpdateHasReaction(updateProperties)", updater, StringComparison.Ordinal);
        Assert.Contains("MaterialHotTable.CellUpdateHasCustomUpdate(updateProperties)", updater, StringComparison.Ordinal);
        Assert.DoesNotContain("materials.ReactionCountOf(material)", updater, StringComparison.Ordinal);
        Assert.DoesNotContain("materials.PropertyFlagsOf(material)", updater, StringComparison.Ordinal);
        Assert.Contains("CellType.Liquid => TryMovePowder", updater, StringComparison.Ordinal);
        Assert.Contains("dispersion != 0 && TryMoveLiquidHorizontal", updater, StringComparison.Ordinal);
        Assert.Matches(@"\[MethodImpl\(MethodImplOptions\.NoInlining\)\]\s+private static bool TryMoveLiquidHorizontal", updater);
        Assert.Contains(": TryMoveDownThroughEmptyColumn(", updater, StringComparison.Ordinal);
        Assert.Matches(@"\[MethodImpl\(MethodImplOptions\.NoInlining\)\]\s+private static bool TryMoveDownThroughEmptyColumn", updater);
        Assert.DoesNotContain("private static bool TryMoveLiquid(", updater, StringComparison.Ordinal);
        Assert.Contains("TryMoveToCenterKnownTarget", updater, StringComparison.Ordinal);
        Assert.Contains("sourceLocalIndex + EngineConstants.ChunkSize + firstDx", updater, StringComparison.Ordinal);
        Assert.DoesNotContain("ushort activeMaterial = window.GetMaterial(activeX, activeY);", updater, StringComparison.Ordinal);
        Assert.Contains("localOffset++", updater, StringComparison.Ordinal);
        Assert.DoesNotContain("chunk.MaterialBuffer[", updater, StringComparison.Ordinal);
        Assert.DoesNotContain("chunk.FlagsBuffer[", updater, StringComparison.Ordinal);
        Assert.DoesNotContain("chunk.LifetimeBuffer[", updater, StringComparison.Ordinal);
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
        Dictionary<string, string> buildRunners = new()
        {
            ["win-x64"] = "windows-latest",
            ["win-arm64"] = "windows-latest",
            ["linux-x64"] = "ubuntu-latest",
            ["linux-arm64"] = "ubuntu-24.04-arm",
            ["osx-x64"] = "macos-15-intel",
            ["osx-arm64"] = "macos-14",
        };
        Dictionary<string, string> verifyRunners = new()
        {
            ["win-x64"] = "windows-latest",
            ["linux-x64"] = "ubuntu-latest",
            ["osx-x64"] = "macos-15-intel",
            ["osx-arm64"] = "macos-14",
        };

        string workflow = WriteMarkdownEvidence(
            Path.Combine(evidenceRoot, "workflow-run.md"),
            new Dictionary<string, string>
            {
                ["run_id"] = "1",
                ["workflow"] = "CI",
                ["event"] = "push",
                ["run_attempt"] = "1",
                ["sha"] = "abc",
                ["ref"] = "refs/heads/main",
                ["aggregator_job_status"] = "success",
            });

        string benchmark = WriteMarkdownEvidence(
            Path.Combine(evidenceRoot, "benchmark-guard.md"),
            new Dictionary<string, string>
            {
                ["runner"] = "windows-latest",
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
                    ["runner"] = buildRunners[rid],
                    ["build_only"] = testsRan ? "false" : "true",
                    ["tests_ran"] = testsRan ? "true" : "false",
                    ["native_gpu_smoke_scope"] = "separate_workflow",
                    ["native_gpu_smoke_executed"] = "false",
                    ["run_id"] = "1",
                    ["sha"] = "abc",
                    ["conclusion"] = "success",
                });
            buildTest[rid] = new Dictionary<string, object>
            {
                ["report"] = report,
                ["sha256"] = GetSha256(report),
                ["runner"] = buildRunners[rid],
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
                    ["runner"] = verifyRunners[rid],
                    ["channels"] = "r2r,aot",
                    ["run_id"] = "1",
                    ["sha"] = "abc",
                    ["conclusion"] = "success",
                });
            verifyPublish[rid] = new Dictionary<string, object>
            {
                ["report"] = report,
                ["sha256"] = GetSha256(report),
                ["runner"] = verifyRunners[rid],
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
                ["runner"] = "windows-latest",
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
        string uploadTag = "v0.1.0",
        bool includeUploadAssetCoverage = true)
    {
        string evidenceRoot = Path.Combine(tempRoot, suffix, "artifacts", "release-evidence");
        string packageRoot = Path.Combine(tempRoot, suffix, "artifacts", "package");
        _ = Directory.CreateDirectory(evidenceRoot);
        _ = Directory.CreateDirectory(packageRoot);

        string workflow = WriteMarkdownEvidence(
            Path.Combine(evidenceRoot, "workflow-run.md"),
            new Dictionary<string, string>
            {
                ["run_id"] = "1",
                ["workflow"] = "Release",
                ["event"] = "push",
                ["run_attempt"] = "1",
                ["sha"] = "abc",
                ["ref"] = workflowRef,
                ["conclusion"] = "success",
            });
        string r2rLightup = WriteMarkdownEvidence(
            Path.Combine(evidenceRoot, "r2r-lightup.md"),
            new Dictionary<string, string>
            {
                ["run_id"] = "1",
                ["workflow"] = "Release",
                ["run_attempt"] = "1",
                ["sha"] = "abc",
                ["conclusion"] = r2rLightupConclusion,
            });
        string artifactAudit = WriteMarkdownEvidence(
            Path.Combine(evidenceRoot, "artifact-audit.md"),
            new Dictionary<string, string>
            {
                ["run_id"] = "1",
                ["workflow"] = "Release",
                ["run_attempt"] = "1",
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
                    string simdProbeKind = rid.EndsWith("-x64", StringComparison.Ordinal) ? "x64_ymm_zmm" : "arm64_neon";
                    string simdExtra = rid.EndsWith("-x64", StringComparison.Ordinal) ? "SIMD evidence contains ymm and zmm." : "SIMD evidence contains NEON.";
                    string simd = WriteMarkdownEvidence(
                        Path.Combine(evidenceRoot, $"{rid}-{channel}-simd.md"),
                        new Dictionary<string, string>
                        {
                            ["rid"] = rid,
                            ["channel"] = channel,
                            ["run_id"] = "1",
                            ["workflow"] = "Release",
                            ["run_attempt"] = "1",
                            ["sha"] = "abc",
                            ["conclusion"] = "success",
                            ["simdProbeKind"] = simdProbeKind,
                        },
                        simdExtra);
                    node["simdProbe"] = simd;
                    node["simdProbeSha256"] = GetSha256(simd);
                    node["simdProbeKind"] = simdProbeKind;
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

        Dictionary<string, string> uploadEvidence = new()
        {
            ["tag"] = uploadTag,
            ["run_id"] = "1",
            ["workflow"] = "Release",
            ["run_attempt"] = "1",
            ["sha"] = "abc",
            ["release_tag"] = releaseTag,
            ["conclusion"] = githubReleaseConclusion,
        };
        if (includeUploadAssetCoverage)
        {
            uploadEvidence["uploaded_asset_count"] = (packageChecksums.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            foreach ((string name, string hash) in packageChecksums.OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                uploadEvidence[$"asset/{name}"] = hash;
                uploadEvidence[$"browser_download_url/{name}"] = $"https://github.com/fruktoguo/PixelEngine/releases/download/{uploadTag}/{name}";
            }

            uploadEvidence["asset/SHA256SUMS"] = checksumHash;
            uploadEvidence["browser_download_url/SHA256SUMS"] = $"https://github.com/fruktoguo/PixelEngine/releases/download/{uploadTag}/SHA256SUMS";
        }

        string upload = WriteMarkdownEvidence(
            Path.Combine(evidenceRoot, "github-release-upload.md"),
            uploadEvidence);

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
        const string detectorRunId = "run-20260704-native-001";
        const string gitCommit = "abcdef123456";

        Dictionary<string, object> scopes = [];
        foreach (string scope in new[] { "gl", "openal", "box2d", "alc" })
        {
            string report = WriteMarkdownEvidence(
                Path.Combine(evidenceRoot, $"{scope}.md"),
                new Dictionary<string, string>
                {
                    ["scope"] = scope,
                    ["detector"] = "external-detector",
                    ["detectorRunId"] = detectorRunId,
                    ["gitCommit"] = gitCommit,
                    ["conclusion"] = "no_leaks",
                    [GetNativeLeakScopeRequiredMetric(scope)] = "0",
                });
            string hash = scope.Equals(corruptHashScope, StringComparison.Ordinal) ? new string('0', 64) : GetSha256(report);
            scopes[scope] = new Dictionary<string, object>
            {
                ["detector"] = "external-detector",
                ["detectorRunId"] = detectorRunId,
                ["gitCommit"] = gitCommit,
                ["report"] = report,
                ["sha256"] = hash,
            };
        }

        Dictionary<string, object> manifest = new()
        {
            ["schemaVersion"] = 1,
            ["detectorRunId"] = detectorRunId,
            ["gitCommit"] = gitCommit,
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
        double probeSampleSeconds = 20.0,
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
            benchmarkRunId: run-20260703-target-gpu
            """);

        string cpuProbe = WriteTextEvidence(
            Path.Combine(evidenceRoot, "cpu-probe.md"),
            "gitCommit: abcdef123456" + Environment.NewLine +
            "particle_frame_probe source=PixelEngineParticleFrameProbe, benchmark_run_id=run-20260703-target-gpu, mode=cpu, gpu_available=False, requested_count=" + particleCount.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ", active_count=" + particleCount.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ", warmup_frames=60, measured_frames=" + measuredFrames.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ", sample_seconds=" + probeSampleSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
            ", wall_avg_ms=" + cpuWallAvgMs.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
            ", wall_p50_ms=6.000, wall_p95_ms=7.000, wall_max_ms=8.000, particle_stamp_avg_ms=2.400, particle_stamp_p50_ms=2.300, particle_stamp_p95_ms=2.600, particle_stamp_max_ms=2.900, gpu_particle_avg_ms=0.000");

        string gpuProbe = WriteTextEvidence(
            Path.Combine(evidenceRoot, "gpu-probe.md"),
            "gitCommit: abcdef123456" + Environment.NewLine +
            "particle_frame_probe source=PixelEngineParticleFrameProbe, benchmark_run_id=run-20260703-target-gpu, mode=gpu, gpu_available=True, requested_count=" + particleCount.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ", active_count=" + particleCount.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ", warmup_frames=60, measured_frames=" + measuredFrames.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ", sample_seconds=" + probeSampleSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
            ", wall_avg_ms=" + gpuWallAvgMs.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
            ", wall_p50_ms=3.000, wall_p95_ms=4.000, wall_max_ms=5.000, particle_stamp_avg_ms=0.000, gpu_particle_avg_ms=0.900, gpu_particle_p50_ms=0.850, gpu_particle_p95_ms=1.050, gpu_particle_max_ms=1.200");

        string comparison = WriteTextEvidence(
            Path.Combine(evidenceRoot, "comparison.md"),
            $"""
            # Target GPU comparison

            gpuFasterThanCpu: true
            benchmarkRunId: run-20260703-target-gpu
            gitCommit: abcdef123456
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
        const string reviewSessionId = "session-20260704-demo-001";
        string gitCommit = includeDemoManualMetadata ? GetCurrentGitCommit() : "abcdef123456";

        List<Dictionary<string, object>> evidence = [];
        foreach (string scope in scopes)
        {
            string safeScope = scope.Replace('/', '-');
            bool isVideo = includeDemoManualMetadata && scope.EndsWith("Video", StringComparison.Ordinal);
            string extension = isVideo ? ".mp4" : ".md";
            string report = isVideo
                ? WriteMinimalMp4VideoEvidence(Path.Combine(evidenceRoot, $"{safeScope}{extension}"))
                : WriteTextEvidence(
                    Path.Combine(evidenceRoot, $"{safeScope}{extension}"),
                    includeDemoManualMetadata
                        ? CreateDemoManualReportEvidence(scope, reviewSessionId, gitCommit)
                        : $"{scope} evidence");
            Dictionary<string, object> entry = new()
            {
                ["scope"] = scope,
                ["path"] = report,
                ["sha256"] = GetSha256(report),
            };

            if (includeDemoManualMetadata)
            {
                entry["kind"] = isVideo ? "video" : "report";
                entry["reviewSessionId"] = reviewSessionId;
                entry["gitCommit"] = gitCommit;
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
        if (includeDemoManualMetadata)
        {
            manifest["reviewSessionId"] = reviewSessionId;
            manifest["gitCommit"] = gitCommit;
        }

        string manifestPath = Path.Combine(tempRoot, suffix, "flat-evidence.json");
        File.WriteAllText(
            manifestPath,
            System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return manifestPath;
    }

    private static string WriteMinimalMp4VideoEvidence(string path)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, CreateMinimalMp4VideoBytes(durationSeconds: 60));
        return path;
    }

    private static string CreateDemoManualReportEvidence(string scope, string reviewSessionId, string gitCommit)
    {
        return $"""
            # {scope} manual report

            reviewSessionId: {reviewSessionId}
            gitCommit: {gitCommit}
            conclusion: reviewer confirmed this scope against the live PixelEngine window and recorded the concrete observations required by the manifest checklist.
            risk: remaining risk is limited to the human review judgment and must still be checked before changing any plan M15 blocker to complete.

            结论: 该报告正文与 manifest 使用同一个 reviewSessionId 和 gitCommit，供预检确认不是把旧报告或空白说明拼接到当前证据。
            风险: 该报告仍只是人工验收待审材料，不代表 plan/13、plan/19 或 plan/20 的真实窗口体验验收已经完成。
            """;
    }

    private static byte[] CreateFtypOnlyMp4Bytes()
    {
        return Concat(Box("ftyp", Ascii("isom"), Be32(1), Ascii("isom"), Ascii("mp42")), Box("free", new byte[64]));
    }

    private static byte[] CreateMinimalMp4VideoBytes(uint durationSeconds)
    {
        const uint timescale = 1_000;
        uint duration = durationSeconds * timescale;
        byte[] mvhd = Box(
            "mvhd",
            [0, 0, 0, 0],
            Be32(0),
            Be32(0),
            Be32(timescale),
            Be32(duration),
            new byte[80]);
        byte[] mdhd = Box(
            "mdhd",
            [0, 0, 0, 0],
            Be32(0),
            Be32(0),
            Be32(timescale),
            Be32(duration),
            Be32(0));
        byte[] hdlr = Box(
            "hdlr",
            [0, 0, 0, 0],
            Be32(0),
            Ascii("vide"),
            Ascii("VideoHandler\0"));
        byte[] mdia = Box("mdia", mdhd, hdlr);
        byte[] trak = Box("trak", mdia);
        byte[] moov = Box("moov", mvhd, trak);
        byte[] ftyp = CreateFtypOnlyMp4Bytes();
        byte[] mdat = Box("mdat", [0x00]);
        return Concat(ftyp, moov, mdat);
    }

    private static byte[] Box(string type, params byte[][] payloads)
    {
        byte[] payload = Concat(payloads);
        return Concat(Be32((uint)(payload.Length + 8)), Ascii(type), payload);
    }

    private static byte[] Be32(uint value)
    {
        return
        [
            (byte)(value >> 24),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value,
        ];
    }

    private static byte[] Ascii(string value)
    {
        return System.Text.Encoding.ASCII.GetBytes(value);
    }

    private static byte[] Concat(params byte[][] arrays)
    {
        byte[] result = new byte[arrays.Sum(static array => array.Length)];
        int offset = 0;
        foreach (byte[] array in arrays)
        {
            Buffer.BlockCopy(array, 0, result, offset, array.Length);
            offset += array.Length;
        }

        return result;
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
                "playerPackageStandaloneRun",
            ],
            "lavaCombatPlaythroughVideo" =>
            [
                "lavaDamageObserved",
                "grenadeLargeTerrainEdit",
                "obstacleDemolitionRoute",
                "webFirstResultRestart",
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

    private static void SetFlatEvidenceFileBytes(string manifestPath, string scope, byte[] content)
    {
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        JsonArray evidence = manifest["evidence"]!.AsArray();
        JsonObject? entry = evidence
            .Select(node => node?.AsObject())
            .FirstOrDefault(node => string.Equals((string?)node?["scope"], scope, StringComparison.Ordinal));
        Assert.NotNull(entry);
        string path = (string?)entry["path"] ?? throw new InvalidOperationException("evidence entry 缺少 path。");
        File.WriteAllBytes(path, content);
        entry["sha256"] = GetSha256(path);
        File.WriteAllText(manifestPath, manifest.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static void SetFlatEvidenceManifestProperty(string manifestPath, string propertyName, JsonNode? value)
    {
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest[propertyName] = value;
        File.WriteAllText(manifestPath, manifest.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static string CreateUiRuntimeEvidenceManifest(string tempRoot, string suffix = "good")
    {
        const string ReviewSessionId = "ui-runtime-review-20260709-001";
        string gitCommit = GetCurrentGitCommit();
        Dictionary<string, string[]> trueFieldsByScope = new()
        {
            ["transparent_ui_product_window"] =
            [
                "sameWindowSameGl",
                "noSecondWindow",
                "noSecondProcess",
                "singleRenderContextVerified",
                "alphaBlendVerified",
                "worldVisibleThroughTransparentPixels",
                "passThroughVerified",
                "captureVerified",
                "editorOverlayVerified",
                "videoOrWalkthroughAttached",
            ],
            ["rmlui_angle_gles_native_profile"] =
            [
                "glesRendererImplemented",
                "angleContextVerified",
                "shaderProfileGles300Es",
                "sameContextFunctionTable",
                "stateRestoreSmokePassed",
            ],
            ["platform_ime_composition"] =
            [
                "windowsImeSmokePassed",
                "preeditVisible",
                "candidateWindowChecked",
                "committedTextSeparated",
                "backendConsistencyChecked",
            ],
            ["ultralight_optional_profile_gate"] =
            [
                "nativeSdkProvenanceRecorded",
                "licenseReviewed",
                "redistributionDecisionRecorded",
                "releaseAuditGatePassed",
                "fallbackPathVerified",
                "inactiveProfileExecutionBlocked",
                "inactiveBackendRejectsDocuments",
                "inactiveBackendCapturesNoInput",
                "inactiveBackendProducesNoCompositeOutput",
                "releaseAuditRejectsInactiveNative",
            ],
            ["ui_native_release_artifact"] =
            [
                "activeRidArtifactsAttached",
                "aotFallbackVerified",
                "sha256SumsAttached",
                "noticeLicenseAttached",
                "tagReleaseWorkflowEvidence",
            ],
        };

        string evidenceRoot = Path.Combine(tempRoot, suffix, "artifacts", "ui-runtime-evidence");
        _ = Directory.CreateDirectory(evidenceRoot);

        List<Dictionary<string, object>> evidence = [];
        foreach (KeyValuePair<string, string[]> scope in trueFieldsByScope)
        {
            List<string> lines =
            [
                "# UI Runtime evidence",
                "",
                $"scope: {scope.Key}",
                $"reviewSessionId: {ReviewSessionId}",
                $"gitCommit: {gitCommit}",
                "conclusion: pass",
                "risk: evidence still requires human review before plan status can change",
            ];

            foreach (string field in scope.Value)
            {
                lines.Add($"{field}: true");
            }

            foreach (KeyValuePair<string, double> field in GetUiRuntimeMinimumNumberFields(scope.Key))
            {
                lines.Add($"{field.Key}: {field.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}");
            }

            string report = WriteTextEvidence(Path.Combine(evidenceRoot, $"{scope.Key}.md"), string.Join(Environment.NewLine, lines));
            evidence.Add(new Dictionary<string, object>
            {
                ["scope"] = scope.Key,
                ["path"] = report,
                ["sha256"] = GetSha256(report),
            });
        }

        Dictionary<string, object> manifest = new()
        {
            ["schemaVersion"] = 1,
            ["reviewSessionId"] = ReviewSessionId,
            ["gitCommit"] = gitCommit,
            ["evidence"] = evidence,
        };

        string manifestPath = Path.Combine(tempRoot, suffix, "ui-runtime-evidence.json");
        File.WriteAllText(
            manifestPath,
            System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return manifestPath;
    }

    private static Dictionary<string, double> GetUiRuntimeMinimumNumberFields(string scope)
    {
        return scope switch
        {
            "transparent_ui_product_window" => new Dictionary<string, double>
            {
                ["videoDurationSeconds"] = 30,
                ["capturedFrameCount"] = 300,
                ["transparentPixelSampleCount"] = 3,
                ["passThroughSampleCount"] = 3,
            },
            "rmlui_angle_gles_native_profile" => new Dictionary<string, double>
            {
                ["smokeFrameCount"] = 60,
            },
            "platform_ime_composition" => new Dictionary<string, double>
            {
                ["compositionSessionCount"] = 1,
            },
            "ultralight_optional_profile_gate" => new Dictionary<string, double>
            {
                ["licenseDocumentCount"] = 1,
                ["inactiveBoundaryTestCount"] = 1,
                ["releaseAuditRejectionCaseCount"] = 1,
            },
            "ui_native_release_artifact" => new Dictionary<string, double>
            {
                ["releaseArtifactCount"] = 1,
                ["sha256EntryCount"] = 1,
            },
            _ => [],
        };
    }

    private static string CreateEditorUxEvidenceManifest(string tempRoot, string suffix = "good")
    {
        const string ReviewSessionId = "editor-ux-review-20260709-001";
        string gitCommit = GetCurrentGitCommit();
        Dictionary<string, string[]> trueFieldsByScope = new()
        {
            ["editor_full_route_window"] =
            [
                "shellStarted",
                "editorShellExeLaunched",
                "singleTopLevelWindowVerified",
                "singleProcessInProcessHost",
                "noConsoleWindowObserved",
                "projectOpenedOrCreated",
                "defaultLayoutVisible",
                "playExitVerified",
                "sceneSaved",
                "buildAndRunVerified",
            ],
            ["project_window_reference_stability"] =
            [
                "stableIdsChecked",
                "stableIdsBeforeAfterRecorded",
                "sceneReferencesChecked",
                "prefabReferencesChecked",
                "inspectorAssetFieldsChecked",
                "projectPlayerBuildSettingsChecked",
                "startupSettingsChecked",
                "buildRequestChecked",
                "buildPackageReferenceAuditPassed",
                "deleteConfirmationChecked",
                "brokenReferenceCountZero",
            ],
            ["script_external_editor"] =
            [
                "scriptDoubleClickAttempted",
                "osOrConfiguredEditorObserved",
                "failureDiagnosticObserved",
                "noSilentFailure",
            ],
            ["settings_build_ux"] =
            [
                "projectSettingsSaved",
                "playerSettingsSaved",
                "buildSettingsSaved",
                "restartReloadVerified",
                "invalidInputRejected",
                "buildPlayerProjectionVerified",
            ],
            ["editor_product_usability"] =
            [
                "layoutUsable",
                "shortcutsChecked",
                "dragDropChecked",
                "gizmoChecked",
                "undoRedoChecked",
                "consoleDiagnosticsChecked",
                "buildFeedbackChecked",
            ],
        };

        string evidenceRoot = Path.Combine(tempRoot, suffix, "artifacts", "editor-ux-evidence");
        _ = Directory.CreateDirectory(evidenceRoot);

        List<Dictionary<string, object>> evidence = [];
        foreach (KeyValuePair<string, string[]> scope in trueFieldsByScope)
        {
            List<string> lines =
            [
                "# Editor UX evidence",
                "",
                $"scope: {scope.Key}",
                $"reviewSessionId: {ReviewSessionId}",
                $"gitCommit: {gitCommit}",
                "conclusion: pass",
                "risk: evidence still requires human review before plan status can change",
            ];

            foreach (string field in scope.Value)
            {
                lines.Add($"{field}: true");
            }

            foreach (KeyValuePair<string, double> field in GetEditorUxMinimumNumberFields(scope.Key))
            {
                lines.Add($"{field.Key}: {field.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}");
            }

            string report = WriteTextEvidence(Path.Combine(evidenceRoot, $"{scope.Key}.md"), string.Join(Environment.NewLine, lines));
            evidence.Add(new Dictionary<string, object>
            {
                ["scope"] = scope.Key,
                ["path"] = report,
                ["sha256"] = GetSha256(report),
            });
        }

        Dictionary<string, object> manifest = new()
        {
            ["schemaVersion"] = 1,
            ["reviewSessionId"] = ReviewSessionId,
            ["gitCommit"] = gitCommit,
            ["evidence"] = evidence,
        };

        string manifestPath = Path.Combine(tempRoot, suffix, "editor-ux-evidence.json");
        File.WriteAllText(
            manifestPath,
            System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return manifestPath;
    }

    private static Dictionary<string, double> GetEditorUxMinimumNumberFields(string scope)
    {
        return scope switch
        {
            "editor_full_route_window" => new Dictionary<string, double>
            {
                ["videoDurationSeconds"] = 60,
                ["capturedFrameCount"] = 600,
                ["routeStepCount"] = 8,
            },
            "project_window_reference_stability" => new Dictionary<string, double>
            {
                ["assetOperationCount"] = 3,
                ["referenceDocumentCount"] = 2,
                ["stableAssetKindCount"] = 4,
                ["buildPackageAuditCount"] = 1,
            },
            "script_external_editor" => new Dictionary<string, double>
            {
                ["scriptOpenAttemptCount"] = 1,
            },
            "settings_build_ux" => new Dictionary<string, double>
            {
                ["settingsRoundTripCount"] = 1,
                ["buildRunAttemptCount"] = 1,
            },
            "editor_product_usability" => new Dictionary<string, double>
            {
                ["interactionChecklistItemCount"] = 7,
                ["reviewerCount"] = 1,
            },
            _ => [],
        };
    }

    private static string CreatePerformanceTargetEvidenceManifest(string tempRoot, bool includeUnknownScope = false, string suffix = "good")
    {
        string[] rids = ["win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"];
        const string BenchmarkRunId = "run-20260704-performance-001";
        const string GitCommit = "abcdef123456";
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
                    benchmarkRunId: run-20260704-performance-001
                    gitCommit: abcdef123456
                    targetCpuName: Test AVX512 CPU
                    dotnetVersion: 10.0.8
                    benchmarkDotNet: true
                    vector512HardwareAccelerated: true
                    avx512Enabled: true
                    noNetDownclockLoss: true
                    """,
                "hardware_counters_cache_branch" => """
                    benchmarkRunId: run-20260704-performance-001
                    gitCommit: abcdef123456
                    benchmarkDotNet: true
                    elevatedEtwKernelSession: true
                    cacheMissesPresent: true
                    branchMispredictionsPresent: true

                    | Method | Cache Misses | Branch Mispredictions |
                    |---|---:|---:|
                    | Reaction | 100 | 12 |
                    """,
                "frame_budget_target_hardware" => """
                    benchmarkRunId: run-20260704-performance-001
                    gitCommit: abcdef123456
                    targetHardware: representative-target
                    source: PixelEngineDiagnostics
                    scenario: lava_mine_typical
                    demoScene: lava-mine
                    sampleSeconds: 120
                    frameSamples: 7200
                    fixedTickNoCatchUp: true
                    playerPackageRun: true
                    realWindowRun: true
                    degradationPolicyObserved: true
                    frameTimelineCaptured: true
                    caP99Ms: 7.5
                    renderP99Ms: 3.5
                    physicsP99Ms: 3.5
                    logicAudioP99Ms: 0.8
                    """,
                _ when scope.StartsWith("cells_frame/", StringComparison.Ordinal) => $"""
                    // BenchmarkDotNet v0.15.8
                    // Benchmark: PixelEngine.Benchmarks.FullActiveCellThroughputBenchmark.StepJobSystemFullActive2MIndependentFrames
                    // Scenario: FullActive2M
                    benchmarkRunId: run-20260704-performance-001
                    gitCommit: abcdef123456
                    rid: {scope["cells_frame/".Length..]}
                    benchmarkDotNet: true
                    representativeHardware: true
                    activeCellsPerFrame: 2500000
                    caFrameMs: 7.2
                    measuredIterations: 5
                    iterationCount: 5
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
            ["benchmarkRunId"] = BenchmarkRunId,
            ["gitCommit"] = GitCommit,
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
            "| workflow | Release |",
            "| run_attempt | 1 |",
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
                ["workflow"] = "Release",
                ["run_attempt"] = "1",
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

    private static string GetCurrentGitCommit()
    {
        string root = FindRepositoryRoot();
        using System.Diagnostics.Process process = new();
        process.StartInfo.FileName = "git";
        process.StartInfo.WorkingDirectory = root;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.ArgumentList.Add("rev-parse");
        process.StartInfo.ArgumentList.Add("HEAD");
        _ = process.Start();
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode != 0 ? throw new InvalidOperationException("无法读取当前 git HEAD：" + error) : output.Trim();
    }

    private static ScriptResult RunPowerShellScript(string workingDirectory, string scriptPath, params string[] arguments)
    {
        string[] effectiveArguments = arguments;
        if (Path.GetFileName(scriptPath).Equals("release-evidence-preflight.ps1", StringComparison.OrdinalIgnoreCase) &&
            !arguments.Contains("-ActiveRids", StringComparer.OrdinalIgnoreCase))
        {
            effectiveArguments =
            [
                .. arguments,
                "-ActiveRids",
                "win-x64,win-arm64,linux-x64,linux-arm64,osx-x64,osx-arm64",
            ];
        }

        using System.Diagnostics.Process process = new()
        {
            StartInfo = Utf8TestProcess.CreatePowerShell(workingDirectory, scriptPath, effectiveArguments),
        };

        _ = process.Start();
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ScriptResult(process.ExitCode, output);
    }

    private static ScriptResult RunBashScript(string workingDirectory, string scriptPath, params string[] arguments)
    {
        return RunBashScriptWithEnvironment(workingDirectory, scriptPath, null, arguments);
    }

    private static ScriptResult RunBashScriptWithEnvironment(
        string workingDirectory,
        string scriptPath,
        IReadOnlyDictionary<string, string>? environment,
        params string[] arguments)
    {
        using System.Diagnostics.Process process = new()
        {
            StartInfo = Utf8TestProcess.CreateBash(workingDirectory, scriptPath, arguments),
        };
        if (environment is not null)
        {
            foreach ((string name, string value) in environment)
            {
                process.StartInfo.Environment[name] = value;
            }
        }

        _ = process.Start();
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ScriptResult(process.ExitCode, output);
    }

    private static string CreateCrLfPython3Shim(string directory)
    {
        _ = Directory.CreateDirectory(directory);
        string shim = Path.Combine(directory, "python3");
        File.WriteAllText(
            shim,
            """
            #!/usr/bin/env bash
            set -euo pipefail
            cat >/dev/null
            if [[ "$#" -eq 2 ]]; then
              while IFS= read -r line || [[ -n "$line" ]]; do
                printf '%s\r\n' "$line"
              done < <(unzip -Z1 "$2")
              exit 0
            fi
            if [[ "$#" -eq 3 ]]; then
              unzip -p "$2" "$3"
              exit $?
            fi
            exit 64
            """);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                shim,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return directory;
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
        bool injectDisableBuildServers = arguments.Length > 0 &&
            string.Equals(arguments[0], "run", StringComparison.Ordinal) &&
            !arguments.Contains("--disable-build-servers", StringComparer.Ordinal);
        for (int i = 0; i < arguments.Length; i++)
        {
            process.StartInfo.ArgumentList.Add(arguments[i]);
            if (i == 0 && injectDisableBuildServers)
            {
                // 可复用 MSBuild node 会继承重定向管道；dotnet run 主进程退出后它们仍持有 writer，
                // ReadToEnd 因而永远等不到 EOF。工具纪律测试必须让每个子进程完整收口。
                process.StartInfo.ArgumentList.Add("--disable-build-servers");
            }
        }

        _ = process.Start();
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        string output = standardOutput.GetAwaiter().GetResult() + standardError.GetAwaiter().GetResult();
        return new ScriptResult(process.ExitCode, output);
    }

    private static void CreateFriendlyExpandedPackage(string root, string channel, bool includeUiNative)
    {
        _ = WriteTextEvidence(Path.Combine(root, "README.txt"), "readme");
        _ = WriteTextEvidence(Path.Combine(root, "NOTICE.txt"), "notice");
        _ = WriteTextEvidence(Path.Combine(root, "PixelEngine Demo.exe"), "launcher");
        if (string.Equals(channel, "r2r", StringComparison.Ordinal))
        {
            _ = WriteTextEvidence(Path.Combine(root, "app", "PixelEngine.Demo.dll"), "app");
            _ = WriteTextEvidence(Path.Combine(root, "app", "runtimes", "win-x64", "native", "box2d.dll"), "box2d");
        }

        if (includeUiNative)
        {
            _ = WriteTextEvidence(Path.Combine(root, "app", "runtimes", "win-x64", "native", "PixelEngine.UI.Native.dll"), "ui native");
        }

        _ = WriteTextEvidence(Path.Combine(root, "content", "materials.json"), "{}");
        _ = WriteTextEvidence(Path.Combine(root, "content", "reactions.json"), "{}");
        _ = WriteTextEvidence(Path.Combine(root, "content", "weapons.json"), "{}");
        _ = WriteTextEvidence(Path.Combine(root, "content", "textures", "17_gravel.png"), "gravel");
        _ = WriteTextEvidence(Path.Combine(root, "content", "textures", "18_boundary_stone.png"), "boundary");
        _ = WriteTextEvidence(Path.Combine(root, "content", "scenes", "lava-mine.scene"), "scene");

        RewriteFriendlyExpandedPackageChecksum(root);
    }

    private static void RewriteFriendlyExpandedPackageChecksum(string root)
    {
        string checksumText = string.Join(
            Environment.NewLine,
            Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)
                .Where(static relative => !string.Equals(relative, "SHA256SUMS", StringComparison.Ordinal))
                .Select(relative => $"{new string('0', 64)}  {relative}")) + Environment.NewLine;
        _ = WriteTextEvidence(Path.Combine(root, "SHA256SUMS"), checksumText);
    }

    private static void CreateZipWithRoot(string sourceDirectory, string archivePath, string rootName)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        using FileStream stream = File.Create(archivePath);
        using System.IO.Compression.ZipArchive archive = new(stream, System.IO.Compression.ZipArchiveMode.Create);
        foreach (string file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            _ = System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(archive, file, rootName + "/" + relative);
        }
    }

    private static string ToBashPath(string path)
    {
        return Path.GetFullPath(path).Replace('\\', '/');
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
