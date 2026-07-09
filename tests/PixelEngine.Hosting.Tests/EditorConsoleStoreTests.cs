using PixelEngine.Editor.Shell;
using PixelEngine.Editor.Shell.Build;
using PixelEngine.Scripting;
using PixelEngine.UI;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// plan/19 Console 产品面自动化切片测试。
/// 不变式：控制台条目按级别归档、环形缓冲不丢关键错误行。
/// </summary>
public sealed class EditorConsoleStoreTests
{
    /// <summary>
    /// 验证 Console snapshot 可按类别、严重度、来源、文本与时间窗过滤。
    /// </summary>
    [Fact]
    public void SnapshotFiltersByCategorySeveritySourceTextAndTimestamp()
    {
        // Arrange：准备输入与初始状态
        DateTimeOffset start = new(2026, 7, 6, 0, 0, 0, TimeSpan.Zero);
        EditorConsoleStore store = new();
        store.Add(new EditorConsoleEntry(start, EditorConsoleCategory.Build, EditorConsoleSeverity.Info, "build-player", "native ready"));
        store.Add(new EditorConsoleEntry(start.AddMinutes(1), EditorConsoleCategory.Asset, EditorConsoleSeverity.Error, "asset-opener", "missing script asset"));
        store.Add(new EditorConsoleEntry(start.AddMinutes(2), EditorConsoleCategory.Script, EditorConsoleSeverity.Warning, "script-hot-reload", "warning CS0168"));

        EditorConsoleEntry[] filtered = store.Snapshot(new EditorConsoleFilter
        {
            Category = EditorConsoleCategory.Asset,
            MinimumSeverity = EditorConsoleSeverity.Warning,
            SourceContains = "opener",
            TextContains = "missing",
            From = start.AddSeconds(30),
            To = start.AddMinutes(90),
        });

        // Assert：验证预期结果
        EditorConsoleEntry entry = Assert.Single(filtered);
        Assert.Equal(EditorConsoleCategory.Asset, entry.Category);
        Assert.Equal(EditorConsoleSeverity.Error, entry.Severity);
        Assert.Equal("asset-opener", entry.Source);
        Assert.Contains("missing script", entry.Text, StringComparison.Ordinal);
        Assert.Empty(store.Snapshot(new EditorConsoleFilter { Severity = EditorConsoleSeverity.Info, TextContains = "missing" }));
    }

    /// <summary>
    /// 验证 Console store 容量淘汰与同 timestamp 稳定排序。
    /// </summary>
    [Fact]
    public void SnapshotAppliesCapacityEvictionAndStableTimestampOrdering()
    {
        // Arrange：准备输入与初始状态
        DateTimeOffset start = new(2026, 7, 6, 0, 0, 0, TimeSpan.Zero);
        EditorConsoleStore capacityStore = new(capacity: 8);
        for (int i = 0; i < 10; i++)
        {
            capacityStore.Add(new EditorConsoleEntry(start.AddSeconds(i), EditorConsoleCategory.General, EditorConsoleSeverity.Info, "capacity", $"event-{i}"));
        }

        EditorConsoleEntry[] retained = capacityStore.Snapshot();
        // Assert：验证预期结果
        Assert.Equal(8, retained.Length);
        Assert.Equal("event-2", retained[0].Text);
        Assert.Equal("event-9", retained[^1].Text);

        EditorConsoleStore orderStore = new();
        orderStore.Add(new EditorConsoleEntry(start, EditorConsoleCategory.General, EditorConsoleSeverity.Info, "order", "first"));
        orderStore.Add(new EditorConsoleEntry(start, EditorConsoleCategory.General, EditorConsoleSeverity.Info, "order", "second"));
        orderStore.Add(new EditorConsoleEntry(start, EditorConsoleCategory.General, EditorConsoleSeverity.Info, "order", "third"));

        Assert.Equal(["first", "second", "third"], orderStore.Snapshot().Select(static entry => entry.Text));
        Assert.Equal(["third", "second", "first"], orderStore.Snapshot(newestFirst: true).Select(static entry => entry.Text));
    }

    /// <summary>
    /// 验证 Console sink helper 会归类 build、asset、UI 与 script 诊断。
    /// </summary>
    [Fact]
    public void ConsoleSinkHelpersClassifyBuildAssetUiAndScriptDiagnostics()
    {
        // Arrange：准备输入与初始状态
        DateTimeOffset timestamp = new(2026, 7, 6, 0, 0, 0, TimeSpan.Zero);
        EditorConsoleStore store = new();
        store.AddBuildEvent(new BuildProgressEvent(BuildEventKind.Log, BuildPhase.Audit, 0.9f, BuildLogLevel.Error, "stderr audit line", timestamp));
        store.AddBuildResult(new BuildResult
        {
            Ok = false,
            Error = "build summary failed",
            ExitCode = 5,
            Warnings = ["result warning"],
        });
        store.AddAssetOpenResult(new EditorScriptAssetOpenResult(false, string.Empty, "scripts/Missing.cs", null, null, false, "脚本资产不存在"));
        store.AddUiBackendSelection(new GameUiBackendSelection(UiBackendKind.Ultralight, UiBackendKind.ManagedFallback, "Ultralight unavailable; ManagedFallback active"));
        store.AddUiBackendSelection(new GameUiBackendSelection(
            UiBackendKind.RmlUi,
            UiBackendKind.ManagedFallback,
            "RmlUi GLES3/ANGLE renderer profile 需要 OpenGL ES 3.0+ 与同 context 函数表；回退 ManagedFallback，避免误用 desktop GL3 #version 330 shader。"));
        store.AddUiBackendSelection(new GameUiBackendSelection(
            UiBackendKind.RmlUi,
            UiBackendKind.RmlUi,
            FallbackReason: null,
            ActiveNativeProfile: "RmlUi_Renderer_GL3; #version 330 core; profileId=0"));
        new EditorConsoleScriptHotReloadDiagnosticSink(store).Report(new ScriptHotReloadDiagnostic(
            DateTimeOffset.UtcNow,
            ScriptHotReloadDiagnosticKind.ReloadResult,
            ScriptHotReloadStatus.CompileFailed,
            "脚本编译失败",
            ["warning CS0168", "error CS1002"]));

        EditorConsoleEntry[] entries = store.Snapshot();
        // Assert：验证预期结果
        Assert.Contains(entries, entry => entry.Category == EditorConsoleCategory.Build && entry.Severity == EditorConsoleSeverity.Error && entry.Text.Contains("stderr audit line", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Category == EditorConsoleCategory.Build && entry.Source == "build-result" && entry.Severity == EditorConsoleSeverity.Error && entry.Text.Contains("build summary failed", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Category == EditorConsoleCategory.Build && entry.Severity == EditorConsoleSeverity.Warning && entry.Text == "result warning");
        Assert.Contains(entries, entry => entry.Category == EditorConsoleCategory.Asset && entry.Severity == EditorConsoleSeverity.Error && entry.Source == "asset-opener");
        Assert.Contains(entries, entry => entry.Category == EditorConsoleCategory.Ui && entry.Severity == EditorConsoleSeverity.Warning && entry.Text.Contains("ManagedFallback", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Category == EditorConsoleCategory.Ui && entry.Severity == EditorConsoleSeverity.Warning && entry.Text.Contains("GLES3/ANGLE", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Category == EditorConsoleCategory.Ui && entry.Severity == EditorConsoleSeverity.Warning && entry.Text.Contains("#version 330", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Category == EditorConsoleCategory.Ui && entry.Severity == EditorConsoleSeverity.Info && entry.Text.Contains("nativeProfile=RmlUi_Renderer_GL3", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Category == EditorConsoleCategory.Ui && entry.Severity == EditorConsoleSeverity.Info && entry.Text.Contains("profileId=0", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Category == EditorConsoleCategory.Script && entry.Severity == EditorConsoleSeverity.Error && entry.Text.Contains("脚本编译失败", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Category == EditorConsoleCategory.Script && entry.Severity == EditorConsoleSeverity.Error && entry.Text.Contains("error CS1002", StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证 Build Settings 面板保留局部日志同时汇入统一 Console。
    /// </summary>
    [Fact]
    public void BuildSettingsPanelSinksPreflightProgressAndResultIntoConsole()
    {
        // Arrange：准备输入与初始状态
        using TempDir temp = new();
        EditorProject project = EditorProject.CreateNew(Path.Combine(temp.Path, "ConsoleProject"), "Console Project");
        EditorConsoleStore console = new();
        ImmediateBuildService service = new()
        {
            Preflight = new BuildPreflight { Ok = true, Diagnostic = "preflight ok" },
            Events =
            [
                new BuildProgressEvent(BuildEventKind.Progress, BuildPhase.Native, 0.1f, BuildLogLevel.Info, "native ready", DateTimeOffset.UtcNow),
                new BuildProgressEvent(BuildEventKind.Log, BuildPhase.Audit, 0.9f, BuildLogLevel.Error, "stderr audit line", DateTimeOffset.UtcNow),
            ],
            Result = new BuildResult
            {
                Ok = false,
                Error = "build summary failed",
                ExitCode = 7,
                Warnings = ["result warning"],
            },
        };
        BuildSettingsPanel panel = new(project, service, console);

        // Assert：验证预期结果
        Assert.True(panel.TryStartScriptedBuildProbe(Path.Combine(temp.Path, "out"), runAfterBuild: false, out string diagnostic), diagnostic);
        Assert.True(SpinWait.SpinUntil(
            () =>
            {
                _ = panel.CaptureScriptedBuildProbe();
                EditorConsoleEntry[] snapshot = console.Snapshot();
                return snapshot.Any(entry => entry.Text.Contains("stderr audit line", StringComparison.Ordinal)) &&
                    snapshot.Any(entry => entry.Text.Contains("build summary failed", StringComparison.Ordinal));
            },
            TimeSpan.FromSeconds(2)),
            string.Join(Environment.NewLine, console.Snapshot().Select(static entry => entry.Text)));

        EditorConsoleEntry[] entries = console.Snapshot();
        Assert.Contains(entries, entry => entry.Source == "build-preflight" && entry.Severity == EditorConsoleSeverity.Info && entry.Text == "preflight ok");
        Assert.Contains(entries, entry => entry.Source == "build-player" && entry.Severity == EditorConsoleSeverity.Error && entry.Text.Contains("stderr audit line", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Source == "build-result" && entry.Severity == EditorConsoleSeverity.Error && entry.Text.Contains("build summary failed", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Source == "build-result" && entry.Severity == EditorConsoleSeverity.Warning && entry.Text == "result warning");
    }

    private sealed class ImmediateBuildService : IPlayerBuildService
    {
        public BuildPreflight Preflight { get; init; } = new() { Ok = true, Diagnostic = "ok" };

        public BuildProgressEvent[] Events { get; init; } = [];

        public BuildResult Result { get; init; } = new() { Ok = true, ExitCode = 0 };

        public Task<BuildPreflight> PreflightAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(Preflight);
        }

        public Task<BuildResult> RunAsync(BuildRequest request, IProgress<BuildProgressEvent> progress, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            for (int i = 0; i < Events.Length; i++)
            {
                progress.Report(Events[i]);
            }

            return Task.FromResult(Result);
        }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pixelengine-console-tests", Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
