using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 锁定专用 native GPU Actions workflow、目标硬件矩阵和 runner preflight 的 fail-closed 合同。
/// </summary>
public sealed class NativeGpuCiContractTests
{
    private const string CandidateSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    /// <summary>
    /// 验证真实 GPU workflow 只能手工调度受信任 SHA，并路由到同时具备全部 capability label 的 runner。
    /// </summary>
    [Fact]
    public void DedicatedWorkflowRequiresExactTrustedShaAndGpuRunnerLabels()
    {
        string workflow = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "native-gpu-smoke.yml"));

        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.Contains("candidate_sha:", workflow, StringComparison.Ordinal);
        Assert.Contains("required: true", workflow, StringComparison.Ordinal);
        Assert.Contains("type: string", workflow, StringComparison.Ordinal);
        Assert.Contains("contents: read", workflow, StringComparison.Ordinal);
        Assert.Contains("runs-on: [self-hosted, Windows, X64, pixelengine-wgl-angle, pixelengine-native-smoke]", workflow, StringComparison.Ordinal);
        Assert.Contains("^[0-9a-fA-F]{40}$", workflow, StringComparison.Ordinal);
        Assert.Contains("ref: ${{ inputs.candidate_sha }}", workflow, StringComparison.Ordinal);
        Assert.Contains("persist-credentials: false", workflow, StringComparison.Ordinal);
        Assert.Contains("git rev-parse HEAD", workflow, StringComparison.Ordinal);
        Assert.Contains("OrdinalIgnoreCase", workflow, StringComparison.Ordinal);
        Assert.Contains("native-gpu-runner-preflight.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("run-native-smoke.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("GITHUB_RUN_ID", workflow, StringComparison.Ordinal);
        Assert.Contains("GITHUB_RUN_ATTEMPT", workflow, StringComparison.Ordinal);
        Assert.Contains("RUNNER_NAME", workflow, StringComparison.Ordinal);
        Assert.Contains("if: ${{ always() }}", workflow, StringComparison.Ordinal);
        Assert.Contains("if-no-files-found: error", workflow, StringComparison.Ordinal);
        Assert.Contains("pixelengine.native-smoke/v2", workflow, StringComparison.Ordinal);
        Assert.Contains("graphicsContext.glVendor", workflow, StringComparison.Ordinal);
        Assert.Contains("graphicsContext.glRenderer", workflow, StringComparison.Ordinal);
        Assert.Contains("graphicsContext.glVersion", workflow, StringComparison.Ordinal);
        Assert.Contains("graphicsContext.angleBackend", workflow, StringComparison.Ordinal);
        Assert.Contains("graphicsContext.desktopGl.isGles 必须为 false", workflow, StringComparison.Ordinal);
        Assert.Contains("graphicsContext.angleGles.isGles 必须为 true", workflow, StringComparison.Ordinal);
        Assert.Contains("graphicsContext.angleGles.isAngle 必须为 true", workflow, StringComparison.Ordinal);
        Assert.Contains("graphicsContext.desktopGl.isAngle 必须为 false", workflow, StringComparison.Ordinal);
        Assert.Contains("ANGLE/GLES renderer identity 必须明确包含 ANGLE", workflow, StringComparison.Ordinal);
        Assert.Contains("pixelengine.native-gpu-runner-preflight/v1", workflow, StringComparison.Ordinal);
        Assert.Contains("fixtureUsed 必须为 false", workflow, StringComparison.Ordinal);
        Assert.Contains("evidenceKind')) -cne 'executed-native-smoke'", workflow, StringComparison.Ordinal);
        Assert.Contains("testSeamActive') -ne $false", workflow, StringComparison.Ordinal);
        Assert.Contains("projectSetSource')) -cne 'built-in-required-projects'", workflow, StringComparison.Ordinal);
        Assert.Contains("$projectCount -ne 4 -or $successfulProjectCount -ne 4", workflow, StringComparison.Ordinal);
        Assert.Contains("$totalTests -le 0 -or $passedTests -ne $totalTests", workflow, StringComparison.Ordinal);
        Assert.Contains("([string](Get-PropertyValue $runIdentity 'source')) -cne 'github-actions'", workflow, StringComparison.Ordinal);
        Assert.Contains("Get-PropertyValue $runIdentity 'candidateSha'", workflow, StringComparison.Ordinal);
        Assert.Contains("Get-PropertyValue $runIdentity 'checkedOutSha'", workflow, StringComparison.Ordinal);
        Assert.Contains("Get-PropertyValue $summaryRunner 'name'", workflow, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS", workflow, StringComparison.Ordinal);
        Assert.Contains("[Array]::Sort($sortedPaths, [StringComparer]::Ordinal)", workflow, StringComparison.Ordinal);
        Assert.Contains(".Hash.ToLowerInvariant()", workflow, StringComparison.Ordinal);

        int syntaxIndex = workflow.IndexOf("- name: Validate candidate SHA syntax", StringComparison.Ordinal);
        int checkoutIndex = workflow.IndexOf("- name: Checkout exact candidate", StringComparison.Ordinal);
        int verifyIndex = workflow.IndexOf("- name: Verify checked-out commit identity", StringComparison.Ordinal);
        int initializeIndex = workflow.IndexOf("- name: Initialize immutable run evidence", StringComparison.Ordinal);
        Assert.True(syntaxIndex < checkoutIndex && checkoutIndex < verifyIndex && verifyIndex < initializeIndex);

        Assert.DoesNotContain("pull_request", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("pull_request_target", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowBlocked", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("FixturePath", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowFixture", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("clean: false", workflow, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 matrix v2 不再让 standard hosted build runner 冒充真实 native GPU runner。
    /// </summary>
    [Fact]
    public void TargetHardwareMatrixSeparatesHostedBuildAndExternalGpuRunners()
    {
        JsonObject root = JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "target-hardware-matrix.json")))!.AsObject();
        Assert.Equal("pixelengine.target-hardware-matrix/v2", root["schema"]!.GetValue<string>());

        JsonObject[] entries = [.. root["entries"]!.AsArray().Select(node => node!.AsObject())];
        Assert.All(entries, entry =>
        {
            Assert.True(entry.ContainsKey("buildTestRunner"));
            Assert.False(entry.ContainsKey("runner"));
        });

        JsonObject winX64 = entries.Single(entry => entry["rid"]!.GetValue<string>() == "win-x64");
        JsonObject buildRunner = winX64["buildTestRunner"]!.AsObject();
        Assert.Equal("github-hosted", buildRunner["provider"]!.GetValue<string>());
        Assert.Equal("windows-latest", buildRunner["label"]!.GetValue<string>());
        Assert.False(buildRunner["graphicsNativeSmokeEligible"]!.GetValue<bool>());

        JsonObject nativeRunner = winX64["nativeGpuSmokeRunner"]!.AsObject();
        Assert.Equal("external_required", nativeRunner["provider"]!.GetValue<string>());
        Assert.Equal("missing", nativeRunner["registrationStatus"]!.GetValue<string>());
        Assert.Equal("workflow_dispatch", nativeRunner["trigger"]!.GetValue<string>());
        Assert.True(nativeRunner["candidateShaRequired"]!.GetValue<bool>());
        Assert.True(nativeRunner["interactiveDesktopRequired"]!.GetValue<bool>());
        Assert.False(nativeRunner["fixtureAllowedInProduction"]!.GetValue<bool>());
        Assert.Contains("GL_VENDOR", nativeRunner["identityCapture"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.Contains("GL_RENDERER", nativeRunner["identityCapture"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.Contains("GL_VERSION", nativeRunner["identityCapture"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.Contains("ANGLE renderer", nativeRunner["identityCapture"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.Equal(
            ["self-hosted", "Windows", "X64", "pixelengine-wgl-angle", "pixelengine-native-smoke"],
            nativeRunner["labels"]!.AsArray().Select(node => node!.GetValue<string>()));
    }

    /// <summary>
    /// 验证跨平台 fixture seam 能证明一个完整、合格的 Windows x64 GPU runner identity。
    /// </summary>
    [Fact]
    public void PreflightAcceptsExplicitValidFixtureAndWritesEvidence()
    {
        using TempDirectory temp = new();
        string fixture = Path.Combine(temp.Path, "valid.json");
        string output = Path.Combine(temp.Path, "out");
        File.WriteAllText(fixture, CreateFixture().ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        ProcessResult result = RunPreflight(output, fixture, allowFixture: true);

        Assert.Equal(0, result.ExitCode);
        JsonObject evidence = ReadEvidence(output);
        Assert.Equal("success", evidence["status"]!.GetValue<string>());
        Assert.True(evidence["fixtureUsed"]!.GetValue<bool>());
        Assert.Empty(evidence["validationErrors"]!.AsArray());
        _ = Assert.Single(evidence["eligibleGpuAdapters"]!.AsArray());
        Assert.Equal(CandidateSha, evidence["runnerIdentity"]!["checkedOutSha"]!.GetValue<string>());
        Assert.True(File.Exists(Path.Combine(output, "native-gpu-runner-preflight.md")));
    }

    /// <summary>
    /// 验证 Session 0、非 x64 与 Basic display fixture 会失败，但失败前仍落盘可审计证据。
    /// </summary>
    [Fact]
    public void PreflightRejectsInvalidRunnerAndStillWritesEvidence()
    {
        using TempDirectory temp = new();
        string fixture = Path.Combine(temp.Path, "invalid.json");
        string output = Path.Combine(temp.Path, "out");
        JsonObject invalid = CreateFixture(
            osArchitecture: "Arm64",
            processArchitecture: "Arm64",
            sessionId: 0,
            userInteractive: false,
            gpuName: "Microsoft Basic Display Adapter");
        File.WriteAllText(fixture, invalid.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        ProcessResult result = RunPreflight(output, fixture, allowFixture: true);

        Assert.NotEqual(0, result.ExitCode);
        JsonObject evidence = ReadEvidence(output);
        Assert.Equal("failed", evidence["status"]!.GetValue<string>());
        string errors = string.Join('\n', evidence["validationErrors"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.Contains("X64", errors, StringComparison.Ordinal);
        Assert.Contains("Session 0", errors, StringComparison.Ordinal);
        Assert.Contains("Basic/Remote/Virtual", errors, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(output, "native-gpu-runner-preflight.md")));
    }

    /// <summary>
    /// 验证 fixture 必须显式 opt-in，避免生产 workflow 意外消费合成硬件身份。
    /// </summary>
    [Fact]
    public void PreflightRejectsFixtureWithoutExplicitOptIn()
    {
        using TempDirectory temp = new();
        string fixture = Path.Combine(temp.Path, "fixture.json");
        string output = Path.Combine(temp.Path, "out");
        File.WriteAllText(fixture, CreateFixture().ToJsonString());

        ProcessResult result = RunPreflight(output, fixture, allowFixture: false);

        Assert.NotEqual(0, result.ExitCode);
        JsonObject evidence = ReadEvidence(output);
        Assert.Equal("failed", evidence["status"]!.GetValue<string>());
        Assert.Contains(
            evidence["validationErrors"]!.AsArray(),
            node => node!.GetValue<string>().Contains("-AllowFixture", StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证外部 identity 无法通过控制字符或 Markdown 分隔符伪造 preflight 表格行。
    /// </summary>
    [Fact]
    public void PreflightEscapesMarkdownAndRejectsIdentityControlCharacters()
    {
        using TempDirectory temp = new();
        string fixture = Path.Combine(temp.Path, "markdown-injection.json");
        string output = Path.Combine(temp.Path, "out");
        JsonObject injected = CreateFixture(
            gpuName: "AMD|Radeon",
            runnerName: "runner|name\r\nforged-row");
        File.WriteAllText(fixture, injected.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        ProcessResult result = RunPreflight(output, fixture, allowFixture: true);

        Assert.NotEqual(0, result.ExitCode);
        JsonObject evidence = ReadEvidence(output);
        Assert.Contains(
            evidence["validationErrors"]!.AsArray(),
            node => node!.GetValue<string>().Contains("控制字符", StringComparison.Ordinal));
        string markdown = File.ReadAllText(Path.Combine(output, "native-gpu-runner-preflight.md"));
        Assert.Contains("runner&#124;name&#13;&#10;forged-row", markdown, StringComparison.Ordinal);
        Assert.Contains("AMD&#124;Radeon", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("runner|name\r\nforged-row", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证关键 identity 的长度受限，避免不受限 WMI/runner 文本污染证据。
    /// </summary>
    [Fact]
    public void PreflightRejectsOversizedCriticalIdentity()
    {
        using TempDirectory temp = new();
        string fixture = Path.Combine(temp.Path, "oversized.json");
        string output = Path.Combine(temp.Path, "out");
        File.WriteAllText(fixture, CreateFixture(runnerName: new string('r', 257)).ToJsonString());

        ProcessResult result = RunPreflight(output, fixture, allowFixture: true);

        Assert.NotEqual(0, result.ExitCode);
        JsonObject evidence = ReadEvidence(output);
        Assert.Contains(
            evidence["validationErrors"]!.AsArray(),
            node => node!.GetValue<string>().Contains("runnerIdentity.name 长度超过上限 256", StringComparison.Ordinal));
    }

    private static JsonObject CreateFixture(
        string osArchitecture = "X64",
        string processArchitecture = "X64",
        int sessionId = 1,
        bool userInteractive = true,
        string gpuName = "AMD Radeon RX 7900 XT",
        string runnerName = "fixture-native-gpu-runner")
    {
        return new JsonObject
        {
            ["schema"] = "pixelengine.native-gpu-runner-fixture/v1",
            ["platform"] = new JsonObject
            {
                ["isWindows"] = true,
                ["osArchitecture"] = osArchitecture,
                ["processArchitecture"] = processArchitecture,
            },
            ["session"] = new JsonObject
            {
                ["id"] = sessionId,
                ["userInteractive"] = userInteractive,
                ["name"] = "Console",
                ["userName"] = "fixture-user",
            },
            ["cpu"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "AMD Ryzen 7 5800X",
                    ["manufacturer"] = "AuthenticAMD",
                    ["cores"] = 8,
                    ["logicalProcessors"] = 16,
                },
            },
            ["gpuAdapters"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = gpuName,
                    ["driverVersion"] = "32.0.31021.5001",
                    ["adapterCompatibility"] = "Advanced Micro Devices, Inc.",
                    ["videoProcessor"] = "AMD Radeon Graphics Processor",
                    ["pnpDeviceId"] = "PCI\\VEN_1002&DEV_744C",
                    ["status"] = "OK",
                },
            },
            ["os"] = new JsonObject
            {
                ["caption"] = "Microsoft Windows 11 Pro",
                ["version"] = "10.0.26100",
                ["buildNumber"] = "26100",
                ["architecture"] = "64-bit",
            },
            ["dotnet"] = new JsonObject
            {
                ["version"] = "10.0.108",
                ["info"] = ".NET SDK 10.0.108 fixture",
                ["exitCode"] = 0,
            },
            ["runnerIdentity"] = new JsonObject
            {
                ["name"] = runnerName,
                ["os"] = "Windows",
                ["arch"] = "X64",
                ["labels"] = StringArray("self-hosted", "Windows", "X64", "pixelengine-wgl-angle", "pixelengine-native-smoke"),
                ["workflow"] = "Native GPU Smoke",
                ["event"] = "workflow_dispatch",
                ["repository"] = "fruktoguo/PixelEngine",
                ["ref"] = "refs/heads/main",
                ["dispatchSha"] = CandidateSha,
                ["candidateSha"] = CandidateSha,
                ["checkedOutSha"] = CandidateSha,
                ["runId"] = "29149667017",
                ["runAttempt"] = "1",
            },
        };
    }

    private static JsonArray StringArray(params string[] values)
    {
        return new JsonArray([.. values.Select(value => (JsonNode?)JsonValue.Create(value))]);
    }

    private static JsonObject ReadEvidence(string output)
    {
        return JsonNode.Parse(File.ReadAllText(Path.Combine(output, "native-gpu-runner-preflight.json")))!.AsObject();
    }

    private static ProcessResult RunPreflight(string output, string fixture, bool allowFixture)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "pwsh",
            WorkingDirectory = RepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine(RepositoryRoot(), "tools", "native-gpu-runner-preflight.ps1"));
        startInfo.ArgumentList.Add("-OutputDirectory");
        startInfo.ArgumentList.Add(output);
        startInfo.ArgumentList.Add("-FixturePath");
        startInfo.ArgumentList.Add(fixture);
        if (allowFixture)
        {
            startInfo.ArgumentList.Add("-AllowFixture");
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 native GPU runner preflight。");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(60_000), "native GPU runner preflight 超时。");
        Task.WaitAll(stdout, stderr);
        return new ProcessResult(process.ExitCode, stdout.Result, stderr.Result);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PixelEngine.sln")) && File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("无法定位 PixelEngine 仓库根目录。");
    }

    private readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pixelengine-native-gpu-contract-" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
