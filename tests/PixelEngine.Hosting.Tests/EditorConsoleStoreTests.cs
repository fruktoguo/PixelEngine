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
    /// 验证 Collapse 仅聚合展示投影，保留原始日志并给出稳定重复次数与严重度计数。
    /// </summary>
    [Fact]
    public void ConsoleCollapseProjectionPreservesRawEntriesAndCountsSeverities()
    {
        EditorConsoleStore store = new();
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        store.Add(new EditorConsoleEntry(timestamp, EditorConsoleCategory.Runtime, EditorConsoleSeverity.Info, "runtime", "ready"));
        store.Add(new EditorConsoleEntry(timestamp.AddMilliseconds(1), EditorConsoleCategory.Runtime, EditorConsoleSeverity.Warning, "runtime", "hot"));
        store.Add(new EditorConsoleEntry(timestamp.AddMilliseconds(2), EditorConsoleCategory.Runtime, EditorConsoleSeverity.Warning, "runtime", "hot"));
        store.Add(new EditorConsoleEntry(timestamp.AddMilliseconds(3), EditorConsoleCategory.Runtime, EditorConsoleSeverity.Error, "runtime", "boom", "stack"));

        EditorConsoleRow[] rows = store.SnapshotRows(collapse: true);
        EditorConsoleRow warning = Assert.Single(rows, static row => row.Entry.Text == "hot");
        Assert.Equal(2, warning.RepeatCount);
        Assert.Equal(4, store.Snapshot().Length);
        Assert.Equal(new EditorConsoleCounts(1, 2, 1), store.CaptureCounts());
        Assert.True(store.LastRuntimeErrorSequence >= 0);
    }

    /// <summary>
    /// 验证非连续重复项按 CollapseKey 选择，不把 first/last sequence 区间内的其他消息误高亮。
    /// </summary>
    [Fact]
    public void ConsoleCollapsedSelectionDoesNotTreatSequenceRangeAsGroupMembership()
    {
        EditorConsoleStore store = new();
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        store.Add(new EditorConsoleEntry(timestamp, EditorConsoleCategory.Runtime, EditorConsoleSeverity.Info, "runtime", "A"));
        store.Add(new EditorConsoleEntry(timestamp.AddMilliseconds(1), EditorConsoleCategory.Runtime, EditorConsoleSeverity.Info, "runtime", "B"));
        store.Add(new EditorConsoleEntry(timestamp.AddMilliseconds(2), EditorConsoleCategory.Runtime, EditorConsoleSeverity.Info, "runtime", "A"));
        EditorConsoleRow[] raw = store.SnapshotRows(collapse: false);
        EditorConsoleRow selectedB = raw.Single(row => row.Entry.Text == "B");
        EditorConsoleRow[] collapsed = store.SnapshotRows(collapse: true);

        EditorConsoleRow a = collapsed.Single(row => row.Entry.Text == "A");
        EditorConsoleRow b = collapsed.Single(row => row.Entry.Text == "B");
        Assert.False(EditorConsolePanel.RowMatchesSelection(a, collapse: true, selectedB.Sequence, selectedB.Entry.CollapseKey));
        Assert.True(EditorConsolePanel.RowMatchesSelection(b, collapse: true, selectedB.Sequence, selectedB.Entry.CollapseKey));
    }

    /// <summary>
    /// 验证 Console 搜索覆盖消息、来源、详情与文件位置。
    /// </summary>
    [Fact]
    public void ConsoleUnifiedSearchIncludesDetailsAndSourceLocation()
    {
        EditorConsoleStore store = new();
        store.Add(new EditorConsoleEntry(
            DateTimeOffset.UtcNow,
            EditorConsoleCategory.Script,
            EditorConsoleSeverity.Error,
            "runtime-script",
            "callback failed",
            "NullReferenceException stack",
            "ScriptSource/Player.cs",
            42));

        _ = Assert.Single(store.SnapshotRows(new EditorConsoleFilter { SearchContains = "NullReference" }));
        _ = Assert.Single(store.SnapshotRows(new EditorConsoleFilter { SearchContains = "runtime-script" }));
        _ = Assert.Single(store.SnapshotRows(new EditorConsoleFilter { SearchContains = "Player.cs" }));
        Assert.Empty(store.SnapshotRows(new EditorConsoleFilter { SearchContains = "not-found" }));
    }

    /// <summary>
    /// 验证 Clear on Play 只在新 Play session 触发，Error Pause 只按新运行时错误边沿触发。
    /// </summary>
    [Fact]
    public void ConsolePlayStateTriggersClearAndErrorPauseOnlyOnEdges()
    {
        EditorConsolePlayState state = new(initialRuntimeErrorSequence: 3);
        Assert.False(state.ObserveMode(EditorMode.Edit, clearOnPlay: true));
        Assert.True(state.ObserveMode(EditorMode.Play, clearOnPlay: true));
        Assert.False(state.ObserveMode(EditorMode.Paused, clearOnPlay: true));
        Assert.False(state.ObserveMode(EditorMode.Play, clearOnPlay: true));
        Assert.False(state.ObserveRuntimeError(3, errorPause: true, EditorMode.Play));
        Assert.True(state.ObserveRuntimeError(4, errorPause: true, EditorMode.Play));
        Assert.False(state.ObserveRuntimeError(4, errorPause: true, EditorMode.Play));
        Assert.False(state.ObserveRuntimeError(5, errorPause: true, EditorMode.Edit));
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
    /// 验证 Roslyn 标准诊断文本保留文件与一基行列，供 Console 双击精确跳转。
    /// </summary>
    [Fact]
    public void ScriptDiagnosticsPreserveRoslynSourceLocationAndColumn()
    {
        EditorConsoleStore store = new();
        const string diagnostic = "Gameplay/Player (Copy).cs(27,14): error CS1002: ; expected";

        store.AddScriptDiagnostics(
            "script-hot-reload",
            "脚本编译失败",
            [diagnostic],
            success: false);

        EditorConsoleEntry detail = Assert.Single(store.Snapshot(), entry => entry.Text == diagnostic);
        Assert.Equal("Gameplay/Player (Copy).cs", detail.FilePath);
        Assert.Equal(27, detail.Line);
        Assert.Equal(14, detail.Column);
        Assert.Contains("Gameplay/Player (Copy).cs", detail.CollapseKey, StringComparison.Ordinal);
        Assert.EndsWith("\u001f27\u001f14", detail.CollapseKey, StringComparison.Ordinal);
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

    /// <summary>
    /// 验证 Build And Run 只影响当前一次请求，下一次普通 Build 不会继承启动语义。
    /// </summary>
    [Fact]
    public void BuildSettingsCommandsKeepRunAfterBuildScopedToEachInvocation()
    {
        // Arrange：准备可立即完成的预检和失败构建，避免测试真正启动玩家进程。
        using TempDir temp = new();
        EditorProject project = EditorProject.CreateNew(Path.Combine(temp.Path, "BuildCommandProject"), "Build Command Project");
        ImmediateBuildService service = new()
        {
            Preflight = new BuildPreflight { Ok = true, Diagnostic = "preflight ok" },
            Result = new BuildResult { Ok = false, Error = "expected test result", ExitCode = 5 },
        };
        BuildSettingsPanel panel = new(project, service);
        _ = panel.ApplyScriptedBuildSettingsProbe(Path.Combine(temp.Path, "out"));

        // Act：先执行 Build And Run，再执行普通 Build。
        Assert.True(panel.TryStartBuild(runAfterBuild: true, out string runDiagnostic), runDiagnostic);
        Assert.True(SpinWait.SpinUntil(
            () =>
            {
                _ = panel.CaptureScriptedBuildProbe();
                return service.Requests.Count == 1;
            },
            TimeSpan.FromSeconds(2)));
        Assert.True(panel.TryStartBuild(runAfterBuild: false, out string buildDiagnostic), buildDiagnostic);
        Assert.True(SpinWait.SpinUntil(
            () =>
            {
                _ = panel.CaptureScriptedBuildProbe();
                return service.Requests.Count == 2;
            },
            TimeSpan.FromSeconds(2)));

        // Assert：两次请求各自携带精确命令语义。
        Assert.True(service.Requests[0].RunAfterBuild);
        Assert.False(service.Requests[1].RunAfterBuild);
    }

    /// <summary>
    /// 验证场景校验或自动保存失败时，Build/Build And Run 不会打包旧的磁盘场景。
    /// </summary>
    [Fact]
    public void BuildSettingsRejectsFailedScenePreparationBeforeStartingService()
    {
        using TempDir temp = new();
        EditorProject project = EditorProject.CreateNew(Path.Combine(temp.Path, "BuildDirtySceneProject"), "Build Dirty Scene Project");
        ImmediateBuildService service = new()
        {
            Preflight = new BuildPreflight { Ok = true, Diagnostic = "preflight ok" },
        };
        int preparationCalls = 0;
        BuildSettingsPanel panel = new(
            project,
            service,
            prepareScene: () =>
            {
                preparationCalls++;
                return new BuildScenePreparationResult(false, "当前场景保存失败，构建未启动。");
            });
        _ = panel.ApplyScriptedBuildSettingsProbe(Path.Combine(temp.Path, "out"));

        Assert.False(panel.TryStartBuild(runAfterBuild: true, out string diagnostic));

        Assert.Equal(1, preparationCalls);
        Assert.Equal("当前场景保存失败，构建未启动。", diagnostic);
        Assert.Empty(service.Requests);
    }

    /// <summary>
    /// 验证窄停靠区里的 Build Settings 仍为 label/value 两列保留可读宽度。
    /// </summary>
    [Fact]
    public void BuildSettingsLabelColumnRemainsReadableAndBounded()
    {
        Assert.Equal(72f, BuildSettingsPanel.ResolveSettingsLabelWidth(120f));
        Assert.Equal(132f, BuildSettingsPanel.ResolveSettingsLabelWidth(300f), precision: 3);
        Assert.Equal(144f, BuildSettingsPanel.ResolveSettingsLabelWidth(600f));
        Assert.Equal(72f, BuildSettingsPanel.ResolveSettingsLabelWidth(float.NaN));
    }

    private sealed class ImmediateBuildService : IPlayerBuildService
    {
        public BuildPreflight Preflight { get; init; } = new() { Ok = true, Diagnostic = "ok" };

        public BuildProgressEvent[] Events { get; init; } = [];

        public BuildResult Result { get; init; } = new() { Ok = true, ExitCode = 0 };

        public List<BuildRequest> Requests { get; } = [];

        public Task<BuildPreflight> PreflightAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(Preflight);
        }

        public Task<BuildResult> RunAsync(BuildRequest request, IProgress<BuildProgressEvent> progress, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            Requests.Add(request);
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
