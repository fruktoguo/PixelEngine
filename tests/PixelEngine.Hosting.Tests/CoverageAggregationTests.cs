using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Coverage JSON/TRX 聚合、测试分层与阈值门禁行为测试。
/// </summary>
public sealed class CoverageAggregationTests
{
    private const string CommitSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    /// <summary>
    /// 验证重复加载同一生产程序集时按 file/line 与精确 branch identity 合并，并独立报告行为和源码纪律层。
    /// </summary>
    [Fact]
    public void MergeCoverageUnionsLinesAndBranchesAndSeparatesTestLayers()
    {
        using CoverageFixture fixture = CoverageFixture.Create(expectedCoverageReports: 2);
        fixture.WriteCoverageReport("first", firstLineHits: 1, secondLineHits: 0, firstBranchHits: 1, secondBranchHits: 0);
        fixture.WriteCoverageReport("second", firstLineHits: 0, secondLineHits: 1, firstBranchHits: 0, secondBranchHits: 1);

        ScriptResult result = fixture.RunMerge();

        Assert.Equal(0, result.ExitCode);
        JsonObject report = JsonNode.Parse(File.ReadAllText(fixture.SummaryJsonPath))!.AsObject();
        Assert.True(report["passed"]!.GetValue<bool>());
        Assert.Equal(1, report["testLayers"]!["behavior"]!["passed"]!.GetValue<int>());
        Assert.Equal(1, report["testLayers"]!["sourceDiscipline"]!["passed"]!.GetValue<int>());
        Assert.Equal(2, report["rawCoverage"]!["uniqueReportCount"]!.GetValue<int>());
        JsonObject assembly = report["assemblies"]!.AsArray().Single()!.AsObject();
        Assert.Equal(2, assembly["linesCovered"]!.GetValue<int>());
        Assert.Equal(2, assembly["linesValid"]!.GetValue<int>());
        Assert.Equal(100, assembly["linePercent"]!.GetValue<double>());
        Assert.Equal(2, assembly["branchesCovered"]!.GetValue<int>());
        Assert.Equal(2, assembly["branchesValid"]!.GetValue<int>());
        Assert.Equal(100, assembly["branchPercent"]!.GetValue<double>());
        Assert.Contains("| PixelEngine.Sample | 2/2 | 100.00 / 100.00 | 2/2 | 100.00 / 100.00 | pass |", File.ReadAllText(fixture.SummaryMarkdownPath), StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证未覆盖行或分支不能被其它报告的已覆盖计数重复相加后掩盖。
    /// </summary>
    [Fact]
    public void MergeCoverageRejectsPerAssemblyThresholdRegression()
    {
        using CoverageFixture fixture = CoverageFixture.Create(expectedCoverageReports: 2);
        fixture.WriteCoverageReport("first", firstLineHits: 1, secondLineHits: 0, firstBranchHits: 1, secondBranchHits: 0);
        fixture.WriteCoverageReport("second", firstLineHits: 0, secondLineHits: 0, firstBranchHits: 0, secondBranchHits: 0);

        ScriptResult result = fixture.RunMerge();

        Assert.NotEqual(0, result.ExitCode);
        JsonObject report = JsonNode.Parse(File.ReadAllText(fixture.SummaryJsonPath))!.AsObject();
        Assert.False(report["passed"]!.GetValue<bool>());
        string violations = string.Join('\n', report["violations"]!.AsArray().Select(static node => node!.GetValue<string>()));
        Assert.Contains("程序集行覆盖率低于门槛", violations, StringComparison.Ordinal);
        Assert.Contains("程序集分支覆盖率低于门槛", violations, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 obj/source-generated 文件不会构成生产源码 coverage，且不能让缺失程序集静默通过。
    /// </summary>
    [Fact]
    public void MergeCoverageRejectsGeneratedOnlyAssembly()
    {
        using CoverageFixture fixture = CoverageFixture.Create(expectedCoverageReports: 1);
        fixture.WriteCoverageReport(
            "generated",
            firstLineHits: 1,
            secondLineHits: 1,
            firstBranchHits: 1,
            secondBranchHits: 1,
            generated: true);

        ScriptResult result = fixture.RunMerge();

        Assert.NotEqual(0, result.ExitCode);
        JsonObject report = JsonNode.Parse(File.ReadAllText(fixture.SummaryJsonPath))!.AsObject();
        Assert.Equal(1, report["rawCoverage"]!["excludedGeneratedFileCount"]!.GetValue<int>());
        Assert.Contains(
            report["violations"]!.AsArray().Select(static node => node!.GetValue<string>()),
            static value => value.Contains("程序集没有手写源码 coverage", StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证行为 coverage 重跑若混入 *DisciplineTests 会在计算百分比前失败。
    /// </summary>
    [Fact]
    public void MergeCoverageRejectsSourceDisciplineTestInBehaviorRun()
    {
        using CoverageFixture fixture = CoverageFixture.Create(expectedCoverageReports: 1, leakDisciplineIntoBehavior: true);
        fixture.WriteCoverageReport("only", firstLineHits: 1, secondLineHits: 1, firstBranchHits: 1, secondBranchHits: 1);

        ScriptResult result = fixture.RunMerge();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("行为 coverage TRX 混入源码纪律测试", result.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.SummaryJsonPath));
    }

    private sealed class CoverageFixture : IDisposable
    {
        private readonly string _root;
        private readonly string _repoRoot;
        private readonly string _sourceFile;
        private readonly string _generatedFile;

        private CoverageFixture(string root, int expectedCoverageReports, bool leakDisciplineIntoBehavior)
        {
            _root = root;
            _repoRoot = FindRepositoryRoot();
            SourceRoot = Directory.CreateDirectory(Path.Combine(root, "src")).FullName;
            string projectRoot = Directory.CreateDirectory(Path.Combine(SourceRoot, "PixelEngine.Sample")).FullName;
            CoverageRoot = Directory.CreateDirectory(Path.Combine(root, "coverage")).FullName;
            FullResultsRoot = Directory.CreateDirectory(Path.Combine(root, "full-results")).FullName;
            BehaviorResultsRoot = Directory.CreateDirectory(Path.Combine(root, "behavior-results")).FullName;
            OutputRoot = Path.Combine(root, "output");
            PolicyPath = Path.Combine(root, "coverage-policy.json");
            _sourceFile = Path.Combine(projectRoot, "Sample.cs");
            _generatedFile = Path.Combine(projectRoot, "obj", "generated", "Sample.g.cs");

            File.WriteAllText(
                Path.Combine(projectRoot, "PixelEngine.Sample.csproj"),
                "<Project><PropertyGroup><AssemblyName>PixelEngine.Sample</AssemblyName></PropertyGroup></Project>");
            File.WriteAllText(_sourceFile, "namespace PixelEngine.Sample; public static class Sample { public static int Value => 1; }");
            _ = Directory.CreateDirectory(Path.GetDirectoryName(_generatedFile)!);
            File.WriteAllText(_generatedFile, "// generated");
            WritePolicy(expectedCoverageReports);
            WriteTrx(
                Path.Combine(FullResultsRoot, "full.trx"),
                new TestCase("PixelEngine.Sample.Tests.SampleTests", "Behavior", "Passed"),
                new TestCase("PixelEngine.Sample.Tests.SampleDisciplineTests", "Source", "Passed"));

            TestCase[] behaviorCases = leakDisciplineIntoBehavior
                ?
                [
                    new TestCase("PixelEngine.Sample.Tests.SampleTests", "Behavior", "Passed"),
                    new TestCase("PixelEngine.Sample.Tests.SampleDisciplineTests", "Source", "Passed"),
                ]
                : [new TestCase("PixelEngine.Sample.Tests.SampleTests", "Behavior", "Passed")];
            WriteTrx(Path.Combine(BehaviorResultsRoot, "behavior.trx"), behaviorCases);
        }

        public string SourceRoot { get; }

        public string CoverageRoot { get; }

        public string FullResultsRoot { get; }

        public string BehaviorResultsRoot { get; }

        public string OutputRoot { get; }

        public string PolicyPath { get; }

        public string SummaryJsonPath => Path.Combine(OutputRoot, "coverage-summary.json");

        public string SummaryMarkdownPath => Path.Combine(OutputRoot, "coverage-summary.md");

        public static CoverageFixture Create(int expectedCoverageReports, bool leakDisciplineIntoBehavior = false)
        {
            string root = Path.Combine(Path.GetTempPath(), "pixelengine-coverage-test-" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(root);
            return new CoverageFixture(root, expectedCoverageReports, leakDisciplineIntoBehavior);
        }

        public void WriteCoverageReport(
            string name,
            int firstLineHits,
            int secondLineHits,
            int firstBranchHits,
            int secondBranchHits,
            bool generated = false)
        {
            string path = generated ? _generatedFile : _sourceFile;
            Dictionary<string, object?> method = new()
            {
                ["Lines"] = new Dictionary<string, int>
                {
                    ["1"] = firstLineHits,
                    ["2"] = secondLineHits,
                },
                ["Branches"] = new object[]
                {
                    new { Line = 1, Offset = 10, EndOffset = 20, Path = 0, Ordinal = 0, Hits = firstBranchHits },
                    new { Line = 1, Offset = 10, EndOffset = 30, Path = 1, Ordinal = 1, Hits = secondBranchHits },
                },
            };
            Dictionary<string, object?> report = new()
            {
                ["PixelEngine.Sample.dll"] = new Dictionary<string, object?>
                {
                    [path] = new Dictionary<string, object?>
                    {
                        ["PixelEngine.Sample.Sample"] = new Dictionary<string, object?>
                        {
                            ["System.Int32 PixelEngine.Sample.Sample::get_Value()"] = method,
                        },
                    },
                },
            };
            string directory = Directory.CreateDirectory(Path.Combine(CoverageRoot, name)).FullName;
            File.WriteAllText(
                Path.Combine(directory, "coverage.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }

        public ScriptResult RunMerge()
        {
            ProcessStartInfo startInfo = Utf8TestProcess.CreatePowerShell(
                _repoRoot,
                Path.Combine(_repoRoot, "tools", "merge-coverage.ps1"),
                [
                    "-CoverageResultsDirectory", CoverageRoot,
                    "-FullTestResultsDirectory", FullResultsRoot,
                    "-BehaviorTestResultsDirectory", BehaviorResultsRoot,
                    "-OutputDirectory", OutputRoot,
                    "-PolicyPath", PolicyPath,
                    "-SourceProjectsRoot", SourceRoot,
                    "-RunId", "test-run",
                    "-CommitSha", CommitSha,
                ]);
            using Process process = Process.Start(startInfo)!;
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            Task.WaitAll(standardOutput, standardError);
            return new ScriptResult(process.ExitCode, standardOutput.Result + standardError.Result);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private void WritePolicy(int expectedCoverageReports)
        {
            var policy = new
            {
                schema = "pixelengine.coverage-policy/v1",
                baselineCommit = CommitSha,
                testClassification = new
                {
                    behaviorFilter = "FullyQualifiedName!~DisciplineTests",
                    sourceDisciplineClassSuffix = "DisciplineTests",
                    minimumBehaviorPassed = 1,
                    maximumBehaviorNotExecuted = 0,
                    minimumSourceDisciplinePassed = 1,
                    maximumSourceDisciplineNotExecuted = 0,
                    expectedTestProjectCount = 1,
                    expectedUniqueCoverageReportCount = expectedCoverageReports,
                },
                assemblies = new[]
                {
                    new
                    {
                        name = "PixelEngine.Sample",
                        sourceDirectory = "PixelEngine.Sample",
                        baseline = new { linesCovered = 2, linesValid = 2, branchesCovered = 2, branchesValid = 2 },
                        minimum = new { linePercent = 100, branchPercent = 100, linesValid = 2, branchesValid = 2 },
                    },
                },
            };
            File.WriteAllText(PolicyPath, JsonSerializer.Serialize(policy, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static void WriteTrx(string path, params TestCase[] tests)
        {
            XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
            XElement definitions = new(ns + "TestDefinitions");
            XElement results = new(ns + "Results");
            foreach (TestCase test in tests)
            {
                Guid testId = Guid.NewGuid();
                definitions.Add(
                    new XElement(
                        ns + "UnitTest",
                        new XAttribute("id", testId),
                        new XAttribute("name", test.Name),
                        new XElement(
                            ns + "TestMethod",
                            new XAttribute("className", test.ClassName),
                            new XAttribute("name", test.Name),
                            new XAttribute("codeBase", "PixelEngine.Sample.Tests.dll"))));
                results.Add(
                    new XElement(
                        ns + "UnitTestResult",
                        new XAttribute("executionId", Guid.NewGuid()),
                        new XAttribute("testId", testId),
                        new XAttribute("testName", test.Name),
                        new XAttribute("outcome", test.Outcome)));
            }

            int passed = tests.Count(static test => test.Outcome == "Passed");
            int notExecuted = tests.Count(static test => test.Outcome == "NotExecuted");
            int failed = tests.Length - passed - notExecuted;
            XDocument document = new(
                new XElement(
                    ns + "TestRun",
                    new XAttribute("id", Guid.NewGuid()),
                    definitions,
                    results,
                    new XElement(
                        ns + "ResultSummary",
                        new XAttribute("outcome", failed == 0 ? "Completed" : "Failed"),
                        new XElement(
                            ns + "Counters",
                            new XAttribute("total", tests.Length),
                            new XAttribute("executed", tests.Length - notExecuted),
                            new XAttribute("passed", passed),
                            new XAttribute("failed", failed),
                            new XAttribute("notExecuted", notExecuted)))));
            document.Save(path);
        }
    }

    private sealed record TestCase(string ClassName, string Name, string Outcome);

    private sealed record ScriptResult(int ExitCode, string Output);

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

        throw new DirectoryNotFoundException("无法定位 PixelEngine.sln。");
    }
}

/// <summary>
/// Coverage policy 与仓库 src/test 集合的源码纪律测试。
/// </summary>
public sealed class CoveragePolicyDisciplineTests
{
    /// <summary>
    /// 验证 policy 精确登记全部 src 程序集、历史测试分层下限和可审查 baseline。
    /// </summary>
    [Fact]
    public void CoveragePolicyIndexesEverySourceAssemblyWithAuditableThresholds()
    {
        string root = FindRepositoryRoot();
        JsonObject policy = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "tools", "coverage-policy.json")))!.AsObject();
        Assert.Equal("pixelengine.coverage-policy/v1", policy["schema"]!.GetValue<string>());
        Assert.Matches("^[0-9a-f]{40}$", policy["baselineCommit"]!.GetValue<string>());

        JsonObject classification = policy["testClassification"]!.AsObject();
        Assert.Equal("FullyQualifiedName!~DisciplineTests", classification["behaviorFilter"]!.GetValue<string>());
        Assert.Equal("DisciplineTests", classification["sourceDisciplineClassSuffix"]!.GetValue<string>());
        Assert.True(classification["minimumBehaviorPassed"]!.GetValue<int>() >= 2032);
        Assert.True(classification["minimumSourceDisciplinePassed"]!.GetValue<int>() >= 241);
        Assert.Equal(14, classification["expectedTestProjectCount"]!.GetValue<int>());
        Assert.Equal(14, classification["expectedUniqueCoverageReportCount"]!.GetValue<int>());

        string[] actualAssemblies =
        [
            .. Directory.GetDirectories(Path.Combine(root, "src"), "PixelEngine.*")
                .SelectMany(static directory => Directory.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly))
                .Select(static project => Path.GetFileNameWithoutExtension(project))
                .Order(StringComparer.Ordinal),
        ];
        JsonObject[] entries = [.. policy["assemblies"]!.AsArray().Select(static node => node!.AsObject())];
        string[] indexedAssemblies = [.. entries.Select(static entry => entry["name"]!.GetValue<string>()).Order(StringComparer.Ordinal)];
        Assert.Equal(actualAssemblies, indexedAssemblies);
        Assert.Equal(17, entries.Length);

        foreach (JsonObject entry in entries)
        {
            JsonObject baseline = entry["baseline"]!.AsObject();
            JsonObject minimum = entry["minimum"]!.AsObject();
            int linesCovered = baseline["linesCovered"]!.GetValue<int>();
            int linesValid = baseline["linesValid"]!.GetValue<int>();
            int branchesCovered = baseline["branchesCovered"]!.GetValue<int>();
            int branchesValid = baseline["branchesValid"]!.GetValue<int>();
            Assert.InRange(linesCovered, 1, linesValid);
            Assert.InRange(branchesCovered, 1, branchesValid);
            Assert.Equal(linesValid, minimum["linesValid"]!.GetValue<int>());
            Assert.Equal(branchesValid, minimum["branchesValid"]!.GetValue<int>());
            Assert.True(minimum["linePercent"]!.GetValue<double>() <= 100.0 * linesCovered / linesValid);
            Assert.True(minimum["branchPercent"]!.GetValue<double>() <= 100.0 * branchesCovered / branchesValid);
        }
    }

    /// <summary>
    /// 验证 Windows CI 在完整 TRX 后仅重跑行为层采集 coverage，并上传原始与聚合证据。
    /// </summary>
    [Fact]
    public void CoverageRunnerAndCiKeepBehaviorAndSourceDisciplineEvidenceSeparate()
    {
        string root = FindRepositoryRoot();
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        string runner = File.ReadAllText(Path.Combine(root, "tools", "run-coverage.ps1"));
        string merger = File.ReadAllText(Path.Combine(root, "tools", "merge-coverage.ps1"));
        XDocument settings = XDocument.Load(Path.Combine(root, "tools", "coverage.runsettings"), LoadOptions.None);

        Assert.Contains("name: Collect behavior coverage", workflow, StringComparison.Ordinal);
        Assert.Contains("if: matrix.rid == 'win-x64'", workflow, StringComparison.Ordinal);
        Assert.Contains("./tools/run-coverage.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("-FullTestResultsDirectory $resultsDirectory", workflow, StringComparison.Ordinal);
        Assert.Contains("name: ci-evidence-coverage-${{ matrix.rid }}", workflow, StringComparison.Ordinal);
        Assert.Contains("[string]$FullTestResultsDirectory", runner, StringComparison.Ordinal);
        Assert.Contains("[string]$policy.testClassification.behaviorFilter", runner, StringComparison.Ordinal);
        Assert.Contains("'XPlat Code Coverage'", runner, StringComparison.Ordinal);
        Assert.Contains("tools/merge-coverage.ps1", runner, StringComparison.Ordinal);
        Assert.Contains("-RequireBehaviorOnly", merger, StringComparison.Ordinal);
        Assert.Contains("sourceDisciplineClassSuffix", merger, StringComparison.Ordinal);
        Assert.Contains("excludedGeneratedFileCount", merger, StringComparison.Ordinal);
        Assert.Equal("json,cobertura", settings.Descendants("Format").Single().Value);
        Assert.Equal("**/bin/**,**/obj/**", settings.Descendants("ExcludeByFile").Single().Value);
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

        throw new DirectoryNotFoundException("无法定位 PixelEngine.sln。");
    }
}
