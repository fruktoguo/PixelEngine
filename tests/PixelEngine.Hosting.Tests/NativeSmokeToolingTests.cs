using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// native-smoke 执行器的 fail-closed TRX 审计测试。
/// </summary>
public sealed class NativeSmokeToolingTests
{
    private static readonly string[] CounterNames =
    [
        "total", "executed", "passed", "failed", "error", "timeout", "aborted", "inconclusive",
        "passedButRunAborted", "notRunnable", "notExecuted", "disconnected", "warning",
        "completed", "inProgress", "pending", "skipped",
    ];

    /// <summary>
    /// 验证每个项目全量通过时生成 v2 summary，并把本地缺失 CI identity 明确写成 unavailable/null。
    /// </summary>
    [Fact]
    public void PassingTrxIsAcceptedAndRecordsExplicitLocalIdentity()
    {
        using NativeSmokeFixture fixture = new();
        string trx = fixture.WriteTrx(["Passed", "Passed"]);

        NativeSmokeRunResult result = fixture.Run(trx, runnerExitCode: 0);

        Assert.Equal(0, result.ExitCode);
        JsonObject summary = Assert.IsType<JsonObject>(result.Summary);
        Assert.Equal("pixelengine.native-smoke/v2", summary["schema"]!.GetValue<string>());
        Assert.Equal(2, summary["schemaVersion"]!.GetValue<int>());
        Assert.Equal("native-smoke-test-seam", summary["evidenceKind"]!.GetValue<string>());
        Assert.True(summary["testSeamActive"]!.GetValue<bool>());
        Assert.True(summary["success"]!.GetValue<bool>());
        Assert.Equal(1, summary["projectCount"]!.GetValue<int>());
        Assert.Equal(1, summary["successfulProjectCount"]!.GetValue<int>());
        Assert.Equal(2, summary["totalTests"]!.GetValue<int>());
        Assert.Equal(2, summary["passedTests"]!.GetValue<int>());

        JsonObject identity = summary["runIdentity"]!.AsObject();
        Assert.Equal("local", identity["source"]!.GetValue<string>());
        Assert.False(identity["githubRun"]!["available"]!.GetValue<bool>());
        Assert.Null(identity["githubRun"]!["id"]);
        Assert.Null(identity["dispatchSha"]);
        Assert.Null(identity["candidateSha"]);
        Assert.Null(identity["checkedOutSha"]);
        Assert.False(identity["runner"]!["available"]!.GetValue<bool>());

        JsonObject graphicsContext = summary["graphicsContext"]!.AsObject();
        Assert.Equal("Fixture Desktop Vendor", graphicsContext["glVendor"]!.GetValue<string>());
        Assert.Equal("Fixture Desktop Renderer", graphicsContext["glRenderer"]!.GetValue<string>());
        Assert.Equal("4.6 Fixture", graphicsContext["glVersion"]!.GetValue<string>());
        Assert.Equal("ANGLE (Fixture D3D11)", graphicsContext["angleBackend"]!.GetValue<string>());
        Assert.Equal("desktop-gl", graphicsContext["desktopGl"]!["backend"]!.GetValue<string>());
        Assert.Equal("angle-gles", graphicsContext["angleGles"]!["backend"]!.GetValue<string>());

        JsonObject project = Assert.Single(summary["projects"]!.AsArray())!.AsObject();
        Assert.True(project["projectExists"]!.GetValue<bool>());
        Assert.True(project["trxExists"]!.GetValue<bool>());
        Assert.True(project["counterConsistent"]!.GetValue<bool>());
        Assert.True(project["success"]!.GetValue<bool>());
        Assert.Equal(0, project["exitCode"]!.GetValue<int>());
        Assert.Equal(2, project["counters"]!["total"]!.GetValue<int>());
        Assert.Equal(2, project["resultCounts"]!["total"]!.GetValue<int>());
    }

    /// <summary>
    /// 验证 Failed、Skipped、NotExecuted 任一逐条结果都会阻断，即使 runner exit code 为零且 counters 自洽。
    /// </summary>
    [Theory]
    [InlineData("Failed", "failed")]
    [InlineData("Skipped", "skipped")]
    [InlineData("NotExecuted", "notExecuted")]
    public void NonPassingUnitTestOutcomeFailsClosed(string outcome, string expectedDiagnostic)
    {
        using NativeSmokeFixture fixture = new();
        string trx = fixture.WriteTrx(["Passed", outcome]);

        NativeSmokeRunResult result = fixture.Run(trx, runnerExitCode: 0);

        Assert.NotEqual(0, result.ExitCode);
        JsonObject summary = Assert.IsType<JsonObject>(result.Summary);
        Assert.False(summary["success"]!.GetValue<bool>());
        Assert.Equal(0, summary["successfulProjectCount"]!.GetValue<int>());
        Assert.Contains(expectedDiagnostic, result.Output, StringComparison.OrdinalIgnoreCase);
        JsonObject project = Assert.Single(summary["projects"]!.AsArray())!.AsObject();
        Assert.False(project["success"]!.GetValue<bool>());
        Assert.True(project["counterConsistent"]!.GetValue<bool>());
    }

    /// <summary>
    /// 验证单个项目 total=0 会失败，不能被其它项目或 exit=0 掩盖。
    /// </summary>
    [Fact]
    public void ZeroExecutedTestsFailsPerProject()
    {
        using NativeSmokeFixture fixture = new();
        string trx = fixture.WriteTrx([]);

        NativeSmokeRunResult result = fixture.Run(trx, runnerExitCode: 0);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("total=0", result.Output, StringComparison.Ordinal);
        JsonObject project = Assert.Single(result.Summary!["projects"]!.AsArray())!.AsObject();
        Assert.Equal(0, project["total"]!.GetValue<int>());
        Assert.False(project["success"]!.GetValue<bool>());
    }

    /// <summary>
    /// 验证 Counters 即使只伪报一个字段，也会与逐条 UnitTestResult 对账失败。
    /// </summary>
    [Fact]
    public void CounterMismatchFailsEvenWhenRowsAllPass()
    {
        using NativeSmokeFixture fixture = new();
        string trx = fixture.WriteTrx(["Passed"], new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["passed"] = 0,
        });

        NativeSmokeRunResult result = fixture.Run(trx, runnerExitCode: 0);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("counter mismatch 'passed'", result.Output, StringComparison.Ordinal);
        JsonObject project = Assert.Single(result.Summary!["projects"]!.AsArray())!.AsObject();
        Assert.False(project["counterConsistent"]!.GetValue<bool>());
        Assert.Equal(1, project["resultCounts"]!["passed"]!.GetValue<int>());
        Assert.Equal(0, project["counters"]!["passed"]!.GetValue<int>());
    }

    /// <summary>
    /// 验证缺少同源 Desktop GL 与 ANGLE/GLES capability marker 时失败闭合。
    /// </summary>
    [Fact]
    public void MissingGraphicsCapabilityMarkersFailsClosed()
    {
        using NativeSmokeFixture fixture = new();
        string trx = fixture.WriteTrx(["Passed"], graphicsMarkers: []);

        NativeSmokeRunResult result = fixture.Run(trx, runnerExitCode: 0);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("同一份 TRX", result.Output, StringComparison.Ordinal);
        JsonObject context = result.Summary!["graphicsContext"]!.AsObject();
        Assert.Null(context["desktopGl"]);
        Assert.Null(context["angleGles"]);
    }

    /// <summary>
    /// 验证伪造为低版本 Desktop GL 的 marker 不会成为有效图形证据。
    /// </summary>
    [Fact]
    public void InvalidDesktopGraphicsCapabilityMarkerFailsClosed()
    {
        using NativeSmokeFixture fixture = new();
        string trx = fixture.WriteTrx(
            ["Passed"],
            graphicsMarkers:
            [
                NativeSmokeFixture.DesktopMarker with { Major = 3, Minor = 2 },
                NativeSmokeFixture.AngleMarker,
            ]);

        NativeSmokeRunResult result = fixture.Run(trx, runnerExitCode: 0);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Desktop GL 3.3+", result.Output, StringComparison.Ordinal);
        Assert.False(result.Summary!["success"]!.GetValue<bool>());
    }

    /// <summary>
    /// 验证 runner 非零退出码不能被一份全绿 TRX 覆盖。
    /// </summary>
    [Fact]
    public void NonZeroRunnerExitFailsEvenWithPassingTrx()
    {
        using NativeSmokeFixture fixture = new();
        string trx = fixture.WriteTrx(["Passed"]);

        NativeSmokeRunResult result = fixture.Run(trx, runnerExitCode: 7);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("exit=7", result.Output, StringComparison.Ordinal);
        JsonObject project = Assert.Single(result.Summary!["projects"]!.AsArray())!.AsObject();
        Assert.Equal(7, project["exitCode"]!.GetValue<int>());
        Assert.False(project["success"]!.GetValue<bool>());
    }

    /// <summary>
    /// 验证 runner 未生成 TRX 时明确失败并在 summary 标记 trxExists=false。
    /// </summary>
    [Fact]
    public void MissingTrxFailsWithoutInventingTestCounts()
    {
        using NativeSmokeFixture fixture = new();

        NativeSmokeRunResult result = fixture.Run(trxPath: null, runnerExitCode: 0);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("未生成 TRX", result.Output, StringComparison.Ordinal);
        JsonObject project = Assert.Single(result.Summary!["projects"]!.AsArray())!.AsObject();
        Assert.False(project["trxExists"]!.GetValue<bool>());
        Assert.Equal(0, project["total"]!.GetValue<int>());
    }

    /// <summary>
    /// 验证 project 文件缺失时不启动 runner，并生成明确失败报告。
    /// </summary>
    [Fact]
    public void MissingProjectFailsBeforeRunnerInvocation()
    {
        using NativeSmokeFixture fixture = new();
        string missingProject = Path.Combine(fixture.Root, "missing", "Missing.Tests.csproj");

        NativeSmokeRunResult result = fixture.Run(trxPath: null, runnerExitCode: 0, projectPath: missingProject);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(fixture.RunnerCallsPath));
        JsonObject summary = Assert.IsType<JsonObject>(result.Summary);
        Assert.Contains(
            summary["failures"]!.AsArray(),
            failure => failure!.GetValue<string>().Contains("project 不存在", StringComparison.Ordinal));
        JsonObject project = Assert.Single(summary["projects"]!.AsArray())!.AsObject();
        Assert.False(project["projectExists"]!.GetValue<bool>());
        Assert.Equal(-1, project["exitCode"]!.GetValue<int>());
    }

    /// <summary>
    /// 验证有效 GitHub run/candidate/runner 环境会原样、规范化写入 summary。
    /// </summary>
    [Fact]
    public void GitHubActionsRejectsTestSeamBeforeRunnerAndRecordsIdentity()
    {
        using NativeSmokeFixture fixture = new();
        string trx = fixture.WriteTrx(["Passed"]);
        Dictionary<string, string> identity = NativeSmokeFixture.ValidGitHubIdentity();

        NativeSmokeRunResult result = fixture.Run(
            trx,
            runnerExitCode: 0,
            environment: identity,
            createProjectManifest: false);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(fixture.RunnerCallsPath));
        Assert.Contains("禁止 TestRunner/ProjectManifestPath", result.Output, StringComparison.Ordinal);
        Assert.Equal("native-smoke-test-seam", result.Summary!["evidenceKind"]!.GetValue<string>());
        Assert.Equal("built-in-rejection-report", result.Summary!["projectSetSource"]!.GetValue<string>());
        Assert.Equal(4, result.Summary!["projectCount"]!.GetValue<int>());
        Assert.False(result.Summary!["success"]!.GetValue<bool>());
        JsonObject runIdentity = result.Summary!["runIdentity"]!.AsObject();
        Assert.Equal("github-actions", runIdentity["source"]!.GetValue<string>());
        Assert.True(runIdentity["githubRun"]!["available"]!.GetValue<bool>());
        Assert.Equal("29140906626", runIdentity["githubRun"]!["id"]!.GetValue<string>());
        Assert.Equal(3, runIdentity["githubRun"]!["attempt"]!.GetValue<int>());
        Assert.Equal(new string('d', 40), runIdentity["dispatchSha"]!.GetValue<string>());
        Assert.Equal(new string('a', 40), runIdentity["candidateSha"]!.GetValue<string>());
        Assert.Equal(new string('a', 40), runIdentity["checkedOutSha"]!.GetValue<string>());
        Assert.True(runIdentity["runner"]!["available"]!.GetValue<bool>());
        Assert.Equal("Windows", runIdentity["runner"]!["os"]!.GetValue<string>());
        Assert.Equal("X64", runIdentity["runner"]!["arch"]!.GetValue<string>());
    }

    /// <summary>
    /// 验证 GitHub identity 的数值、SHA 与 runner 枚举格式异常会在执行前失败。
    /// </summary>
    [Theory]
    [InlineData("GITHUB_RUN_ID", "abc", "GITHUB_RUN_ID")]
    [InlineData("GITHUB_RUN_ATTEMPT", "0", "GITHUB_RUN_ATTEMPT")]
    [InlineData("GITHUB_SHA", "not-a-dispatch-sha", "GITHUB_SHA")]
    [InlineData("PIXELENGINE_CANDIDATE_SHA", "not-a-candidate-sha", "PIXELENGINE_CANDIDATE_SHA")]
    [InlineData("PIXELENGINE_CHECKED_OUT_SHA", "not-a-checkout-sha", "PIXELENGINE_CHECKED_OUT_SHA")]
    [InlineData("PIXELENGINE_CHECKED_OUT_SHA", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "不一致")]
    [InlineData("RUNNER_OS", "Solaris", "RUNNER_OS")]
    [InlineData("RUNNER_ARCH", "MIPS", "RUNNER_ARCH")]
    public void InvalidGitHubIdentityFailsBeforeExecution(string variable, string value, string diagnostic)
    {
        using NativeSmokeFixture fixture = new();
        Dictionary<string, string> identity = NativeSmokeFixture.ValidGitHubIdentity();
        identity[variable] = value;

        NativeSmokeRunResult result = fixture.Run(trxPath: null, runnerExitCode: 0, environment: identity);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(diagnostic, result.Output, StringComparison.Ordinal);
        Assert.Null(result.Summary);
        Assert.False(File.Exists(fixture.RunnerCallsPath));
    }

    /// <summary>
    /// 验证 PowerShell test process 在 hosted Windows 上对 stdout、stderr 与 terminating error
    /// 都使用无 ANSI 的 UTF-8 文本，避免中文诊断被 console code page 或渲染样式破坏。
    /// </summary>
    [Fact]
    public void Utf8PowerShellProcessPreservesChineseStdoutStderrAndException()
    {
        string root = Path.Combine(Path.GetTempPath(), "pixelengine-utf8-powershell-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(root);
        string script = Path.Combine(root, "utf8-sentinel.ps1");
        File.WriteAllText(
            script,
            """
            [Console]::Out.WriteLine('stdout-中文-✓')
            [Console]::Error.WriteLine('stderr-中文-✓')
            throw 'exception-中文-✓'
            """);

        try
        {
            using Process process = new()
            {
                StartInfo = Utf8TestProcess.CreatePowerShell(root, script, []),
            };

            Assert.True(process.Start());
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(30_000), "UTF-8 PowerShell sentinel timed out.");

            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains("stdout-中文-✓", output, StringComparison.Ordinal);
            Assert.Contains("stderr-中文-✓", output, StringComparison.Ordinal);
            Assert.Contains("exception-中文-✓", output, StringComparison.Ordinal);
            Assert.DoesNotContain("\u001b[", output, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class NativeSmokeFixture : IDisposable
    {
        private static readonly string[] IdentityEnvironmentNames =
        [
            "GITHUB_ACTIONS", "GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT", "GITHUB_SHA",
            "PIXELENGINE_CANDIDATE_SHA", "PIXELENGINE_CHECKED_OUT_SHA",
            "RUNNER_NAME", "RUNNER_OS", "RUNNER_ARCH", "ImageOS", "ImageVersion",
        ];

        public NativeSmokeFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "pixelengine-native-smoke-tooling-" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(Root);
            RunnerPath = Path.Combine(Root, "fake-native-smoke-runner.ps1");
            RunnerCallsPath = Path.Combine(Root, "runner-calls.txt");
            File.WriteAllText(
                RunnerPath,
                """
                $argumentsList = @($args)
                Add-Content -LiteralPath $env:PIXELENGINE_NATIVE_SMOKE_RUNNER_CALLS -Value ($argumentsList -join ' ')
                $resultsIndex = [Array]::IndexOf($argumentsList, '--results-directory')
                $loggerIndex = [Array]::IndexOf($argumentsList, '--logger')
                if ($resultsIndex -lt 0 -or $loggerIndex -lt 0) {
                    throw 'fake runner 缺少 results/logger 参数'
                }
                $resultsDirectory = $argumentsList[$resultsIndex + 1]
                $logger = $argumentsList[$loggerIndex + 1]
                $trxName = $logger.Substring($logger.IndexOf('LogFileName=', [StringComparison]::Ordinal) + 12)
                if (-not [string]::IsNullOrWhiteSpace($env:PIXELENGINE_NATIVE_SMOKE_FIXTURE_TRX)) {
                    New-Item -ItemType Directory -Force -Path $resultsDirectory | Out-Null
                    Copy-Item -LiteralPath $env:PIXELENGINE_NATIVE_SMOKE_FIXTURE_TRX -Destination (Join-Path $resultsDirectory $trxName) -Force
                }
                Write-Output 'synthetic native smoke runner'
                $global:LASTEXITCODE = [int]$env:PIXELENGINE_NATIVE_SMOKE_RUNNER_EXIT
                """);
        }

        public string Root { get; }

        public string RunnerPath { get; }

        public string RunnerCallsPath { get; }

        public static Dictionary<string, string> ValidGitHubIdentity()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["GITHUB_ACTIONS"] = "true",
                ["GITHUB_RUN_ID"] = "29140906626",
                ["GITHUB_RUN_ATTEMPT"] = "3",
                ["GITHUB_SHA"] = new string('D', 40),
                ["PIXELENGINE_CANDIDATE_SHA"] = new string('A', 40),
                ["PIXELENGINE_CHECKED_OUT_SHA"] = new string('A', 40),
                ["RUNNER_NAME"] = "GitHub Actions 1000000123",
                ["RUNNER_OS"] = "Windows",
                ["RUNNER_ARCH"] = "X64",
                ["ImageOS"] = "win22",
                ["ImageVersion"] = "20260707.1.0",
            };
        }

        public static GraphicsMarker DesktopMarker { get; } = new(
            "desktop-gl", "Fixture Desktop Vendor", "Fixture Desktop Renderer", "4.6 Fixture", 4, 6, false, false);

        public static GraphicsMarker AngleMarker { get; } = new(
            "angle-gles", "Google Inc.", "ANGLE (Fixture D3D11)", "OpenGL ES 3.0 (ANGLE 2.1)", 3, 0, true, true);

        public string WriteTrx(
            IReadOnlyList<string> outcomes,
            IReadOnlyDictionary<string, int>? counterOverrides = null,
            IReadOnlyList<GraphicsMarker>? graphicsMarkers = null)
        {
            Dictionary<string, int> counters = CounterNames.ToDictionary(static name => name, static _ => 0, StringComparer.Ordinal);
            counters["total"] = outcomes.Count;
            counters["executed"] = outcomes.Count(static outcome => outcome is not "NotExecuted" and not "Skipped" and not "NotRunnable" and not "InProgress" and not "Pending");
            for (int i = 0; i < outcomes.Count; i++)
            {
                string counter = outcomes[i] switch
                {
                    "Passed" => "passed",
                    "Failed" => "failed",
                    "Error" => "error",
                    "Timeout" => "timeout",
                    "Aborted" => "aborted",
                    "Inconclusive" => "inconclusive",
                    "PassedButRunAborted" => "passedButRunAborted",
                    "NotRunnable" => "notRunnable",
                    "NotExecuted" => "notExecuted",
                    "Disconnected" => "disconnected",
                    "Warning" => "warning",
                    "Completed" => "completed",
                    "InProgress" => "inProgress",
                    "Pending" => "pending",
                    "Skipped" => "skipped",
                    _ => throw new ArgumentOutOfRangeException(nameof(outcomes), outcomes[i], "未知测试 outcome。"),
                };
                counters[counter]++;
            }

            if (counterOverrides is not null)
            {
                foreach ((string key, int value) in counterOverrides)
                {
                    counters[key] = value;
                }
            }

            XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
            XElement results = new(ns + "Results");
            for (int i = 0; i < outcomes.Count; i++)
            {
                results.Add(new XElement(
                    ns + "UnitTestResult",
                    new XAttribute("executionId", Guid.NewGuid()),
                    new XAttribute("testId", Guid.NewGuid()),
                    new XAttribute("testName", "Fixture.Tests.Case" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new XAttribute("outcome", outcomes[i])));
            }

            XElement countersElement = new(ns + "Counters");
            foreach (string name in CounterNames)
            {
                if (name == "skipped" && counters[name] == 0)
                {
                    continue;
                }

                countersElement.SetAttributeValue(name, counters[name]);
            }

            IReadOnlyList<GraphicsMarker> markers = graphicsMarkers ?? [DesktopMarker, AngleMarker];
            string markerOutput = string.Join(
                Environment.NewLine,
                markers.Select(static marker =>
                    "PIXELENGINE_GRAPHICS_CAPABILITY " + JsonSerializer.Serialize(new
                    {
                        schema = "pixelengine.graphics-capability/v1",
                        backend = marker.Backend,
                        vendor = marker.Vendor,
                        renderer = marker.Renderer,
                        version = marker.Version,
                        major = marker.Major,
                        minor = marker.Minor,
                        isGles = marker.IsGles,
                        isAngle = marker.IsAngle,
                    })));
            XDocument document = new(
                new XElement(
                    ns + "TestRun",
                    results,
                    new XElement(
                        ns + "ResultSummary",
                        new XAttribute("outcome", "Completed"),
                        countersElement,
                        new XElement(ns + "Output", new XElement(ns + "StdOut", markerOutput)))));
            string path = Path.Combine(Root, "fixture-" + Guid.NewGuid().ToString("N") + ".trx");
            document.Save(path);
            return path;
        }

        public NativeSmokeRunResult Run(
            string? trxPath,
            int runnerExitCode,
            string? projectPath = null,
            IReadOnlyDictionary<string, string>? environment = null,
            bool createProjectManifest = true)
        {
            string repositoryRoot = FindRepositoryRoot();
            string manifestPath = Path.Combine(Root, "projects-" + Guid.NewGuid().ToString("N") + ".json");
            JsonObject manifest = new()
            {
                ["projects"] = new JsonArray(
                    new JsonObject
                    {
                        ["name"] = "fixture",
                        ["path"] = projectPath ?? Path.Combine(repositoryRoot, "tests", "PixelEngine.Hosting.Tests", "PixelEngine.Hosting.Tests.csproj"),
                    }),
            };
            if (createProjectManifest)
            {
                File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }

            string resultsDirectory = Path.Combine(Root, "results-" + Guid.NewGuid().ToString("N"));
            string scriptPath = Path.Combine(repositoryRoot, "tools", "run-native-smoke.ps1");
            string[] arguments =
            [
                "-Configuration", "Release",
                "-ResultsDirectory", resultsDirectory,
                "-TestRunner", RunnerPath,
                "-ProjectManifestPath", manifestPath,
            ];
            using Process process = new()
            {
                StartInfo = Utf8TestProcess.CreatePowerShell(repositoryRoot, scriptPath, arguments),
            };
            process.StartInfo.Environment["PIXELENGINE_RENDERING_GL_SMOKE"] = "1";
            process.StartInfo.Environment["PIXELENGINE_RENDERING_ANGLE_SMOKE"] = "1";
            process.StartInfo.Environment["PIXELENGINE_NATIVE_SMOKE_FIXTURE_TRX"] = trxPath ?? string.Empty;
            process.StartInfo.Environment["PIXELENGINE_NATIVE_SMOKE_RUNNER_EXIT"] = runnerExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
            process.StartInfo.Environment["PIXELENGINE_NATIVE_SMOKE_RUNNER_CALLS"] = RunnerCallsPath;
            foreach (string name in IdentityEnvironmentNames)
            {
                process.StartInfo.Environment[name] = string.Empty;
            }
            process.StartInfo.Environment["GITHUB_ACTIONS"] = "false";
            if (environment is not null)
            {
                foreach ((string key, string value) in environment)
                {
                    process.StartInfo.Environment[key] = value;
                }
            }

            Assert.True(process.Start());
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(30_000), "run-native-smoke.ps1 timed out");
            string[] summaries = Directory.Exists(resultsDirectory)
                ? Directory.GetFiles(resultsDirectory, "summary.json", SearchOption.AllDirectories)
                : [];
            JsonNode? summary = summaries.Length == 0
                ? null
                : JsonNode.Parse(File.ReadAllText(Assert.Single(summaries)));
            return new NativeSmokeRunResult(process.ExitCode, output, summary);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private readonly record struct GraphicsMarker(
        string Backend,
        string Vendor,
        string Renderer,
        string Version,
        int Major,
        int Minor,
        bool IsGles,
        bool IsAngle);

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

        throw new InvalidOperationException("无法定位 PixelEngine.sln。 ");
    }

    private readonly record struct NativeSmokeRunResult(int ExitCode, string Output, JsonNode? Summary);
}
