using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Tests;

/// <summary>Build/player 公共 wire DTO 与发布 Schema 回归。</summary>
public sealed class AutomationBuildProtocolTests
{
    /// <summary>完整 build/player DTO 严格往返且拒绝未知成员。</summary>
    [Fact]
    public void BuildAndPlayerDtosRoundTripStrictly()
    {
        AutomationBuildSnapshot build = new()
        {
            BuildId = new string('a', 32),
            State = AutomationBuildState.Succeeded,
            Phase = AutomationBuildPhase.Done,
            Percent = 1,
            StartedAtUtc = DateTimeOffset.Parse("2026-07-16T01:02:03Z"),
            CompletedAtUtc = DateTimeOffset.Parse("2026-07-16T01:03:04Z"),
            LaunchOnSuccess = true,
            CancellationRequested = false,
            PlayerProcessId = new string('b', 32),
            PlayerLaunchError = null,
            Result = new AutomationBuildResult
            {
                Ok = true,
                Rid = "win-x64",
                Channel = "R2R",
                ReleaseChannel = "Development",
                WindowMode = "Windowed",
                Configuration = "Release",
                Version = "1.2.3",
                InformationalVersion = "1.2.3+test",
                PackageArchivePath = "D:\\out\\player.zip",
                PackageDirectory = "D:\\out\\package",
                PlayerDirectory = "D:\\out\\player",
                LauncherPath = "D:\\out\\player\\Game.exe",
                Sha256 = new string('c', 64),
                SizeBytes = 42,
                PhaseTimings =
                [
                    new AutomationBuildPhaseTiming
                    {
                        Phase = AutomationBuildPhase.Publish,
                        Milliseconds = 12.5,
                    },
                ],
                Warnings = ["warning"],
                ExitCode = 0,
            },
        };
        JsonElement buildJson = JsonSerializer.SerializeToElement(
            build,
            AutomationJsonContext.Default.AutomationBuildSnapshot);
        AutomationBuildSnapshot buildRoundTrip = buildJson.Deserialize(
            AutomationJsonContext.Default.AutomationBuildSnapshot)
            ?? throw new InvalidOperationException("build snapshot round-trip 返回 null。");

        Assert.Equal(build.BuildId, buildRoundTrip.BuildId);
        Assert.Equal(build.State, buildRoundTrip.State);
        Assert.Equal(build.Phase, buildRoundTrip.Phase);
        Assert.Equal(build.CancellationRequested, buildRoundTrip.CancellationRequested);
        Assert.Null(buildRoundTrip.PlayerLaunchError);
        Assert.Equal(build.Result?.Sha256, buildRoundTrip.Result?.Sha256);
        Assert.Equal(build.Result?.Warnings, buildRoundTrip.Result?.Warnings);
        Assert.Equal(
            build.Result?.PhaseTimings.Select(static timing => timing.Phase),
            buildRoundTrip.Result?.PhaseTimings.Select(static timing => timing.Phase));
        Assert.True(buildJson.GetProperty("cancellationRequested").ValueKind == JsonValueKind.False);

        AutomationBuildStartRequest start = new() { LaunchOnSuccess = true };
        JsonElement startJson = JsonSerializer.SerializeToElement(
            start,
            AutomationJsonContext.Default.AutomationBuildStartRequest);
        Assert.False(startJson.TryGetProperty("cancellationRequested", out _));
        _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"schemaVersion\":1,\"launchOnSuccess\":true,\"extra\":1}",
            AutomationJsonContext.Default.AutomationBuildStartRequest));

        AutomationPlayerProcessSnapshot player = new()
        {
            PlayerProcessId = new string('b', 32),
            BuildId = build.BuildId,
            ProcessId = 123,
            ProcessStartUtc = build.StartedAtUtc,
            StartedAtUtc = build.StartedAtUtc,
            State = AutomationPlayerProcessState.Exited,
            TerminationRequested = true,
            ExitedAtUtc = build.CompletedAtUtc,
            ExitCode = 0,
        };
        Assert.Equal(
            player,
            JsonSerializer.SerializeToElement(
                    player,
                    AutomationJsonContext.Default.AutomationPlayerProcessSnapshot)
                .Deserialize(AutomationJsonContext.Default.AutomationPlayerProcessSnapshot));
    }

    /// <summary>发布 Schema 完整声明 build/player 请求、响应与日志制品项。</summary>
    [Fact]
    public void PublishedSchemaDefinesEveryBuildAndPlayerContract()
    {
        string schemaPath = FindRepositoryFile("schema/editor-automation-protocol.v1.schema.json");
        using JsonDocument schema = JsonDocument.Parse(File.ReadAllBytes(schemaPath));
        JsonElement definitions = schema.RootElement.GetProperty("$defs");
        string[] expected =
        [
            "buildPreflightResult",
            "buildStartRequest",
            "buildRequest",
            "buildPhaseTiming",
            "buildResult",
            "buildSnapshot",
            "buildListResponse",
            "buildLogEntry",
            "playerLaunchRequest",
            "playerProcessRequest",
            "playerTerminateRequest",
            "playerProcessSnapshot",
            "playerProcessListResponse",
        ];

        Assert.All(expected, name => Assert.True(definitions.TryGetProperty(name, out _), name));
        Assert.DoesNotContain(
            "cancellationRequested",
            definitions.GetProperty("buildStartRequest").GetProperty("properties")
                .EnumerateObject().Select(static property => property.Name));
        Assert.Contains(
            "terminationRequested",
            definitions.GetProperty("playerProcessSnapshot").GetProperty("required")
                .EnumerateArray().Select(static item => item.GetString()));
        Assert.Contains(
            "cancellationRequested",
            definitions.GetProperty("buildSnapshot").GetProperty("required")
                .EnumerateArray().Select(static item => item.GetString()));
        Assert.True(definitions.GetProperty("buildSnapshot").GetProperty("properties")
            .TryGetProperty("playerLaunchError", out JsonElement launchError));
        Assert.Equal(8192, launchError.GetProperty("maxLength").GetInt32());
    }

    private static string FindRepositoryFile(string relativePath)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
