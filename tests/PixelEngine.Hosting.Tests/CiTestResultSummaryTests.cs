using System.Diagnostics;
using System.Xml.Linq;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// CI 普通测试 TRX 持久化与 fail-closed 汇总工具测试。
/// </summary>
public sealed class CiTestResultSummaryTests
{
    private const string CommitSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    /// <summary>
    /// 验证汇总工具逐程序集聚合 TRX，并保留 workflow run identity 与跳过计数。
    /// </summary>
    [Fact]
    public void SummaryAggregatesEveryExpectedTestAssemblyAndRunIdentity()
    {
        string root = FindRepositoryRoot();
        string temp = CreateTempDirectory();
        try
        {
            string projects = CreateExpectedProjects(temp, "Alpha.Tests", "Beta.Tests");
            string results = Directory.CreateDirectory(Path.Combine(temp, "results")).FullName;
            _ = WriteTrx(Path.Combine(results, "alpha.trx"), "Alpha.Tests", "Passed", "NotExecuted");
            _ = WriteTrx(Path.Combine(results, "beta.trx"), "Beta.Tests", "Passed");
            string report = Path.Combine(temp, "summary.md");

            ScriptResult result = RunSummary(root, results, report, projects, buildOnly: false, minimumTotal: 3);

            Assert.Equal(0, result.ExitCode);
            string markdown = File.ReadAllText(report);
            Assert.Contains("| rid | win-x64 |", markdown, StringComparison.Ordinal);
            Assert.Contains("| runner | windows-latest |", markdown, StringComparison.Ordinal);
            Assert.Contains("| run_id | 123456 |", markdown, StringComparison.Ordinal);
            Assert.Contains($"| sha | {CommitSha} |", markdown, StringComparison.Ordinal);
            Assert.Contains("| tests_ran | true |", markdown, StringComparison.Ordinal);
            Assert.Contains("| native_gpu_smoke_scope | separate_workflow |", markdown, StringComparison.Ordinal);
            Assert.Contains("| native_gpu_smoke_executed | false |", markdown, StringComparison.Ordinal);
            Assert.Contains("| expected_trx_count | 2 |", markdown, StringComparison.Ordinal);
            Assert.Contains("| trx_count | 2 |", markdown, StringComparison.Ordinal);
            Assert.Contains("| test_total | 3 |", markdown, StringComparison.Ordinal);
            Assert.Contains("| test_executed | 2 |", markdown, StringComparison.Ordinal);
            Assert.Contains("| test_passed | 2 |", markdown, StringComparison.Ordinal);
            Assert.Contains("| test_not_executed | 1 |", markdown, StringComparison.Ordinal);
            Assert.Contains("| Alpha.Tests | alpha.trx | 2 | 1 | 1 | 0 | 1 |", markdown, StringComparison.Ordinal);
            Assert.Contains("| Beta.Tests | beta.trx | 1 | 1 | 1 | 0 | 0 |", markdown, StringComparison.Ordinal);
            Assert.Contains("| conclusion | success |", markdown, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    /// <summary>
    /// 验证缺少任一测试项目的 TRX 时，即使现有 TRX 全绿也必须失败。
    /// </summary>
    [Fact]
    public void SummaryRejectsMissingExpectedTestAssemblyTrx()
    {
        string root = FindRepositoryRoot();
        string temp = CreateTempDirectory();
        try
        {
            string projects = CreateExpectedProjects(temp, "Alpha.Tests", "Beta.Tests");
            string results = Directory.CreateDirectory(Path.Combine(temp, "results")).FullName;
            _ = WriteTrx(Path.Combine(results, "alpha.trx"), "Alpha.Tests", "Passed");
            string report = Path.Combine(temp, "summary.md");

            ScriptResult result = RunSummary(root, results, report, projects, buildOnly: false, minimumTotal: 1);

            Assert.NotEqual(0, result.ExitCode);
            string markdown = File.ReadAllText(report);
            Assert.Contains("| expected_trx_count | 2 |", markdown, StringComparison.Ordinal);
            Assert.Contains("| trx_count | 1 |", markdown, StringComparison.Ordinal);
            Assert.Contains("缺少测试程序集 TRX：Beta.Tests", markdown, StringComparison.Ordinal);
            Assert.Contains("| conclusion | failure |", markdown, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    /// <summary>
    /// 验证 Counters 与逐条 UnitTestResult 不一致时不会被聚合数字掩盖。
    /// </summary>
    [Fact]
    public void SummaryRejectsTrxCounterAndResultMismatch()
    {
        string root = FindRepositoryRoot();
        string temp = CreateTempDirectory();
        try
        {
            string projects = CreateExpectedProjects(temp, "Alpha.Tests");
            string results = Directory.CreateDirectory(Path.Combine(temp, "results")).FullName;
            _ = WriteTrx(Path.Combine(results, "alpha.trx"), "Alpha.Tests", declaredTotal: 2, "Passed");
            string report = Path.Combine(temp, "summary.md");

            ScriptResult result = RunSummary(root, results, report, projects, buildOnly: false, minimumTotal: 1);

            Assert.NotEqual(0, result.ExitCode);
            string markdown = File.ReadAllText(report);
            Assert.Contains("total 与 UnitTestResult 数量不一致", markdown, StringComparison.Ordinal);
            Assert.Contains("| test_total | 0 |", markdown, StringComparison.Ordinal);
            Assert.Contains("| conclusion | failure |", markdown, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    /// <summary>
    /// 验证非 build-only 的零用例 TRX 不能被视为测试执行成功。
    /// </summary>
    [Fact]
    public void SummaryRejectsZeroTestRun()
    {
        string root = FindRepositoryRoot();
        string temp = CreateTempDirectory();
        try
        {
            string projects = CreateExpectedProjects(temp, "Alpha.Tests");
            string results = Directory.CreateDirectory(Path.Combine(temp, "results")).FullName;
            _ = WriteTrx(Path.Combine(results, "alpha.trx"), "Alpha.Tests");
            string report = Path.Combine(temp, "summary.md");

            ScriptResult result = RunSummary(root, results, report, projects, buildOnly: false, minimumTotal: 1);

            Assert.NotEqual(0, result.ExitCode);
            string markdown = File.ReadAllText(report);
            Assert.Contains("| tests_ran | true |", markdown, StringComparison.Ordinal);
            Assert.Contains("| test_total | 0 |", markdown, StringComparison.Ordinal);
            Assert.Contains("| conclusion | failure |", markdown, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    /// <summary>
    /// 验证除 Passed 与显式 NotExecuted 外的所有终态都会使汇总门禁失败。
    /// </summary>
    [Fact]
    public void SummaryRejectsEveryUnsuccessfulTrxOutcome()
    {
        string root = FindRepositoryRoot();
        string temp = CreateTempDirectory();
        try
        {
            string projects = CreateExpectedProjects(temp, "Alpha.Tests");
            string results = Directory.CreateDirectory(Path.Combine(temp, "results")).FullName;
            _ = WriteTrx(
                Path.Combine(results, "alpha.trx"),
                "Alpha.Tests",
                "Failed",
                "Error",
                "Timeout",
                "Aborted",
                "Inconclusive",
                "PassedButRunAborted",
                "NotRunnable",
                "Disconnected",
                "Warning",
                "Completed",
                "InProgress",
                "Pending");
            string report = Path.Combine(temp, "summary.md");

            ScriptResult result = RunSummary(root, results, report, projects, buildOnly: false, minimumTotal: 12);

            Assert.NotEqual(0, result.ExitCode);
            string markdown = File.ReadAllText(report);
            Assert.Contains("TRX 存在未成功测试状态：count=12", markdown, StringComparison.Ordinal);
            Assert.Contains("| test_total | 12 |", markdown, StringComparison.Ordinal);
            Assert.Contains("| test_failed | 1 |", markdown, StringComparison.Ordinal);
            Assert.Contains("| conclusion | failure |", markdown, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    /// <summary>
    /// 验证 build-only 报告明确写 tests_ran=false、零 TRX 与零测试计数。
    /// </summary>
    [Fact]
    public void BuildOnlySummaryRequiresSkippedTestStepAndNoTrx()
    {
        string root = FindRepositoryRoot();
        string temp = CreateTempDirectory();
        try
        {
            string projects = CreateExpectedProjects(temp, "Alpha.Tests");
            string results = Path.Combine(temp, "missing-results");
            string report = Path.Combine(temp, "summary.md");

            ScriptResult result = RunSummary(root, results, report, projects, buildOnly: true, minimumTotal: 1492);

            Assert.Equal(0, result.ExitCode);
            string markdown = File.ReadAllText(report);
            Assert.Contains("| build_only | true |", markdown, StringComparison.Ordinal);
            Assert.Contains("| tests_ran | false |", markdown, StringComparison.Ordinal);
            Assert.Contains("| test_step_outcome | skipped |", markdown, StringComparison.Ordinal);
            Assert.Contains("| trx_count | 0 |", markdown, StringComparison.Ordinal);
            Assert.Contains("| test_total | 0 |", markdown, StringComparison.Ordinal);
            Assert.Contains("| conclusion | success |", markdown, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    /// <summary>
    /// 验证 build-only 若携带旧 TRX 会失败，避免把残留测试文件混入当前矩阵证据。
    /// </summary>
    [Fact]
    public void BuildOnlySummaryRejectsUnexpectedTrx()
    {
        string root = FindRepositoryRoot();
        string temp = CreateTempDirectory();
        try
        {
            string projects = CreateExpectedProjects(temp, "Alpha.Tests");
            string results = Directory.CreateDirectory(Path.Combine(temp, "results")).FullName;
            _ = WriteTrx(Path.Combine(results, "stale.trx"), "Alpha.Tests", "Passed");
            string report = Path.Combine(temp, "summary.md");

            ScriptResult result = RunSummary(root, results, report, projects, buildOnly: true, minimumTotal: 1492);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("build-only 不得携带 TRX", File.ReadAllText(report), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    /// <summary>
    /// 验证 CI 普通矩阵按 run identity 隔离结果目录、生成 TRX、调用 fail-closed 汇总并上传原始结果。
    /// </summary>
    [Fact]
    public void WorkflowPersistsSummarizesAndUploadsSolutionTrx()
    {
        string root = FindRepositoryRoot();
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));

        Assert.Contains("id: solution_tests", workflow, StringComparison.Ordinal);
        Assert.Contains("--logger \"trx\"", workflow, StringComparison.Ordinal);
        Assert.Contains("--results-directory $resultsDirectory", workflow, StringComparison.Ordinal);
        Assert.Contains("./tools/summarize-ci-test-results.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("-TestStepOutcome '${{ steps.solution_tests.outcome }}'", workflow, StringComparison.Ordinal);
        Assert.Contains("-MinimumTotal 1492", workflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/test-results/${{ matrix.rid }}-${{ github.run_id }}-${{ github.run_attempt }}/**/*.trx", workflow, StringComparison.Ordinal);
        Assert.Contains("if-no-files-found: error", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("PIXELENGINE_RENDERING_GL_SMOKE", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("PIXELENGINE_RENDERING_ANGLE_SMOKE", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("run-native-smoke.ps1", workflow, StringComparison.Ordinal);
        int workflowRunStart = workflow.IndexOf("# CI workflow run evidence", StringComparison.Ordinal);
        int workflowRunEnd = workflow.IndexOf("workflow-run.md", workflowRunStart, StringComparison.Ordinal);
        Assert.True(workflowRunStart >= 0 && workflowRunEnd > workflowRunStart);
        string workflowRunBlock = workflow[workflowRunStart..workflowRunEnd];
        Assert.Contains("| aggregator_job_status | ${{ job.status }} |", workflowRunBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("| conclusion | ${{ job.status }} |", workflowRunBlock, StringComparison.Ordinal);
    }

    private static ScriptResult RunSummary(
        string root,
        string results,
        string report,
        string projects,
        bool buildOnly,
        int minimumTotal)
    {
        string[] arguments =
        [
            "-ResultsDirectory", results,
            "-OutputPath", report,
            "-Rid", "win-x64",
            "-Runner", "windows-latest",
            "-RunId", "123456",
            "-CommitSha", CommitSha,
            "-BuildOnly", buildOnly ? "true" : "false",
            "-TestStepOutcome", buildOnly ? "skipped" : "success",
            "-JobStatus", "success",
            "-MinimumTotal", minimumTotal.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-ExpectedTestProjectsRoot", projects,
        ];
        using Process process = new()
        {
            StartInfo = Utf8TestProcess.CreatePowerShell(
                root,
                Path.Combine(root, "tools", "summarize-ci-test-results.ps1"),
                arguments),
        };

        _ = process.Start();
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ScriptResult(process.ExitCode, output);
    }

    private static string CreateExpectedProjects(string temp, params string[] assemblyNames)
    {
        string root = Directory.CreateDirectory(Path.Combine(temp, "projects")).FullName;
        foreach (string assemblyName in assemblyNames)
        {
            string projectDirectory = Directory.CreateDirectory(Path.Combine(root, assemblyName)).FullName;
            File.WriteAllText(
                Path.Combine(projectDirectory, $"{assemblyName}.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        }

        return root;
    }

    private static string WriteTrx(
        string path,
        string assemblyName,
        params string[] outcomes)
    {
        return WriteTrx(path, assemblyName, declaredTotal: null, outcomes);
    }

    private static string WriteTrx(
        string path,
        string assemblyName,
        int? declaredTotal,
        params string[] outcomes)
    {
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        List<(Guid ExecutionId, Guid TestId, string Outcome)> cases =
        [
            .. outcomes.Select(static outcome => (Guid.NewGuid(), Guid.NewGuid(), outcome)),
        ];
        Dictionary<string, int> counters = new(StringComparer.Ordinal)
        {
            ["passed"] = 0,
            ["failed"] = 0,
            ["error"] = 0,
            ["timeout"] = 0,
            ["aborted"] = 0,
            ["inconclusive"] = 0,
            ["passedButRunAborted"] = 0,
            ["notRunnable"] = 0,
            ["notExecuted"] = 0,
            ["disconnected"] = 0,
            ["warning"] = 0,
            ["completed"] = 0,
            ["inProgress"] = 0,
            ["pending"] = 0,
        };
        foreach ((_, _, string outcome) in cases)
        {
            string key = outcome == "Skipped" ? "notExecuted" : char.ToLowerInvariant(outcome[0]) + outcome[1..];
            counters[key]++;
        }

        // xUnit VSTest adapter 3.x 的真实 TRX 对 skipped 使用 UnitTestResult.NotExecuted，
        // 但 Counters.notExecuted 保持 0；fixture 复现该格式，汇总器必须以逐条结果为准。
        counters["notExecuted"] = 0;

        int executed = cases.Count(entry => entry.Outcome is not ("NotExecuted" or "Skipped" or "NotRunnable" or "InProgress" or "Pending"));
        XElement counter = new(
            ns + "Counters",
            new XAttribute("total", declaredTotal ?? cases.Count),
            new XAttribute("executed", executed),
            counters.Select(entry => new XAttribute(entry.Key, entry.Value)));
        XDocument document = new(
            new XElement(
                ns + "TestRun",
                new XAttribute("id", Guid.NewGuid()),
                new XAttribute("name", assemblyName),
                new XElement(
                    ns + "Results",
                    cases.Select((entry, index) =>
                        new XElement(
                            ns + "UnitTestResult",
                            new XAttribute("executionId", entry.ExecutionId),
                            new XAttribute("testId", entry.TestId),
                            new XAttribute("testName", $"Test{index}"),
                            new XAttribute("outcome", entry.Outcome)))),
                new XElement(
                    ns + "TestDefinitions",
                    cases.Select((entry, index) =>
                        new XElement(
                            ns + "UnitTest",
                            new XAttribute("id", entry.TestId),
                            new XAttribute("name", $"Test{index}"),
                            new XElement(
                                ns + "TestMethod",
                                new XAttribute("codeBase", Path.Combine("bin", $"{assemblyName}.dll")),
                                new XAttribute("className", $"{assemblyName}.Fixture"),
                                new XAttribute("name", $"Test{index}"))))),
                new XElement(ns + "ResultSummary", new XAttribute("outcome", "Completed"), counter)));

        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        document.Save(path);
        return path;
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "pixelengine-ci-test-summary-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(path);
        return path;
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
