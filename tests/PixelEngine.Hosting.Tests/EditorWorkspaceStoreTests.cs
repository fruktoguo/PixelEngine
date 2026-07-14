using PixelEngine.Editor.Shell;
using PixelEngine.Editor.Shell.Settings;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Editor 用户级 workspace 原子持久化与归一化契约。
/// </summary>
public sealed class EditorWorkspaceStoreTests
{
    /// <summary>验证 v1 workspace 无损迁移到当前版本，并持久化 Game View preset/scale/pan。</summary>
    [Fact]
    public void WorkspaceV1MigratesAndGameViewStateRoundTripsInCurrentVersion()
    {
        using TempDirectory temp = new();
        string storagePath = Path.Combine(temp.Path, "editor-workspace.json");
        string projectRoot = Path.Combine(temp.Path, "Project");
        File.WriteAllText(storagePath, $$"""
        {
          "formatVersion": 1,
          "lastCleanShutdown": true,
          "projects": [
            {
              "projectPath": {{System.Text.Json.JsonSerializer.Serialize(projectRoot)}},
              "lastScenePath": "scenes/main.scene",
              "lastOpenedUtc": "2026-07-13T00:00:00+00:00"
            }
          ]
        }
        """);
        EditorWorkspaceStore store = EditorWorkspaceStore.Load(storagePath);

        Assert.Equal(EditorWorkspaceDocument.CurrentFormatVersion, store.Current.FormatVersion);
        Assert.True(store.TryGetGameViewState(projectRoot, out EditorGameViewWorkspaceState migrated));
        Assert.Equal(EditorGameViewWorkspaceState.DefaultPresetId, migrated.PresetId);
        EditorGameViewWorkspaceState next = new()
        {
            PresetId = "custom-wide",
            ScalePercent = 100f,
            PanX = 12.5f,
            PanY = -8f,
            MaximizeOnPlay = true,
            CustomPresets =
            [
                new EditorGameViewCustomPreset
                {
                    Id = "custom-wide",
                    Name = "Wide QA",
                    Width = 1600,
                    Height = 700,
                },
            ],
        };

        Assert.True(store.TrySetGameViewState(projectRoot, next, out string diagnostic), diagnostic);
        EditorWorkspaceStore reloaded = EditorWorkspaceStore.Load(storagePath);
        Assert.True(reloaded.TryGetGameViewState(projectRoot, out EditorGameViewWorkspaceState actual));
        Assert.Equal("custom-wide", actual.PresetId);
        Assert.Equal(100f, actual.ScalePercent);
        Assert.Equal(12.5f, actual.PanX);
        Assert.Equal(-8f, actual.PanY);
        Assert.True(actual.MaximizeOnPlay);
        EditorGameViewCustomPreset custom = Assert.Single(actual.CustomPresets);
        Assert.Equal((1600, 700), (custom.Width, custom.Height));
    }

    /// <summary>
    /// 验证 workspace 往返会规范路径、合并重复工程并保存窗口尺寸。
    /// </summary>
    [Fact]
    public void WorkspaceRoundTripNormalizesPathsDeduplicatesProjectsAndPersistsWindowSize()
    {
        using TempDirectory temp = new();
        string storagePath = Path.Combine(temp.Path, "editor-workspace.json");
        string projectRoot = Path.Combine(temp.Path, "Project");
        DateTimeOffset older = new(2026, 7, 10, 1, 0, 0, TimeSpan.Zero);
        DateTimeOffset newer = older.AddHours(1);
        EditorWorkspaceStore store = EditorWorkspaceStore.Load(storagePath);
        EditorWorkspaceDocument document = new()
        {
            LastCleanShutdown = false,
            LastSuccessfulProjectPath = Path.Combine(projectRoot, "."),
            Window = new EditorWorkspaceWindowState
            {
                Width = 1600,
                Height = 900,
                X = -1200,
                Y = 75,
                State = EditorWorkspaceWindowStateKind.Maximized,
            },
            Projects =
            [
                new EditorProjectWorkspaceState
                {
                    ProjectPath = projectRoot,
                    LastScenePath = "scenes\\old.scene",
                    LastOpenedUtc = older,
                },
                new EditorProjectWorkspaceState
                {
                    ProjectPath = Path.Combine(projectRoot, "."),
                    LastScenePath = "./scenes/latest.scene",
                    LastOpenedUtc = newer,
                },
                new EditorProjectWorkspaceState
                {
                    ProjectPath = "\0invalid",
                    LastScenePath = "scenes/ignored.scene",
                    LastOpenedUtc = newer.AddHours(1),
                },
            ],
        };

        bool saved = store.TryUpdate(document, out string diagnostic);
        EditorWorkspaceStore reloaded = EditorWorkspaceStore.Load(storagePath);

        Assert.True(saved, diagnostic);
        Assert.Contains("已忽略 1 条无效工程 workspace", diagnostic, StringComparison.Ordinal);
        Assert.True(reloaded.LoadedFromDisk);
        Assert.False(reloaded.Current.LastCleanShutdown);
        Assert.Equal(Path.GetFullPath(projectRoot), reloaded.Current.LastSuccessfulProjectPath);
        Assert.Equal(1600, reloaded.Current.Window!.Width);
        Assert.Equal(900, reloaded.Current.Window.Height);
        Assert.Equal(-1200, reloaded.Current.Window.X);
        Assert.Equal(75, reloaded.Current.Window.Y);
        Assert.Equal(EditorWorkspaceWindowStateKind.Maximized, reloaded.Current.Window.State);
        EditorProjectWorkspaceState project = Assert.Single(reloaded.Current.Projects!);
        Assert.Equal(Path.GetFullPath(projectRoot), project.ProjectPath);
        Assert.Equal("scenes/latest.scene", project.LastScenePath);
        Assert.Equal(newer, project.LastOpenedUtc);
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp", SearchOption.AllDirectories));
    }

    /// <summary>
    /// 验证损坏 workspace 不阻止启动并保留诊断。
    /// </summary>
    [Fact]
    public void CorruptWorkspaceFallsBackWithoutBlockingStartup()
    {
        using TempDirectory temp = new();
        string storagePath = Path.Combine(temp.Path, "editor-workspace.json");
        File.WriteAllText(storagePath, "{ invalid json");

        EditorWorkspaceStore store = EditorWorkspaceStore.Load(storagePath);

        Assert.False(store.LoadedFromDisk);
        Assert.True(store.Current.LastCleanShutdown);
        Assert.Null(store.Current.LastSuccessfulProjectPath);
        Assert.Empty(store.Current.Projects!);
        Assert.Contains("读取 Editor workspace 失败", store.LastDiagnostic, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证记录工程会保留其它条目，并可按规范化路径读取最后场景。
    /// </summary>
    [Fact]
    public void RecordProjectOpenedUpdatesLookupAndPreservesOtherProjects()
    {
        string firstProject = Path.Combine(Path.GetTempPath(), "PixelEngine", "Workspace", "First");
        string secondProject = Path.Combine(Path.GetTempPath(), "PixelEngine", "Workspace", "Second");
        DateTimeOffset firstOpen = new(2026, 7, 10, 1, 0, 0, TimeSpan.Zero);
        DateTimeOffset secondOpen = firstOpen.AddMinutes(10);
        EditorWorkspaceStore store = EditorWorkspaceStore.CreateInMemory();

        Assert.True(store.TryRecordProjectOpened(firstProject, "scenes\\first.scene", firstOpen, out string firstDiagnostic), firstDiagnostic);
        Assert.True(store.TryRecordProjectOpened(secondProject, "scenes/second.scene", secondOpen, out string secondDiagnostic), secondDiagnostic);
        Assert.True(store.TryRecordProjectOpened(firstProject, "scenes/latest.scene", secondOpen.AddMinutes(10), out string latestDiagnostic), latestDiagnostic);

        Assert.Equal(Path.GetFullPath(firstProject), store.Current.LastSuccessfulProjectPath);
        Assert.Equal(2, store.Current.Projects!.Length);
        Assert.True(store.TryGetProject(firstProject, out EditorProjectWorkspaceState? first));
        Assert.Equal("scenes/latest.scene", first.LastScenePath);
        Assert.Equal("scenes/latest.scene", store.ResolveLastScene(firstProject));
        Assert.Equal("scenes/second.scene", store.ResolveLastScene(secondProject));
        Assert.False(store.TryGetProject("\0invalid", out _));
        Assert.Null(store.ResolveLastScene(Path.Combine(Path.GetTempPath(), "missing-project")));
    }

    /// <summary>
    /// 验证非法窗口尺寸回退到受支持默认值。
    /// </summary>
    [Fact]
    public void InvalidWindowSizeFallsBackToSupportedDefaults()
    {
        EditorWorkspaceStore store = EditorWorkspaceStore.CreateInMemory(new EditorWorkspaceDocument
        {
            Window = new EditorWorkspaceWindowState
            {
                X = -500,
                Y = 20,
                State = EditorWorkspaceWindowStateKind.Fullscreen,
            },
        });

        Assert.True(store.TrySetWindowSize(-1, int.MaxValue, out string diagnostic), diagnostic);

        Assert.Equal(EditorWorkspaceWindowState.DefaultWidth, store.Current.Window!.Width);
        Assert.Equal(EditorWorkspaceWindowState.DefaultHeight, store.Current.Window.Height);
        Assert.Equal(-500, store.Current.Window.X);
        Assert.Equal(20, store.Current.Window.Y);
        Assert.Equal(EditorWorkspaceWindowStateKind.Fullscreen, store.Current.Window.State);
    }

    /// <summary>验证 v2 的缺省 placement 迁移为平台默认位置与 Normal 状态。</summary>
    [Fact]
    public void WorkspaceV2MigratesWindowPlacementDefaultsToCurrentVersion()
    {
        using TempDirectory temp = new();
        string storagePath = Path.Combine(temp.Path, "editor-workspace.json");
        File.WriteAllText(storagePath, """
        {
          "formatVersion": 2,
          "lastCleanShutdown": true,
          "window": { "width": 1440, "height": 810 },
          "projects": []
        }
        """);

        EditorWorkspaceStore store = EditorWorkspaceStore.Load(storagePath);

        Assert.True(store.LoadedFromDisk);
        Assert.Equal(EditorWorkspaceDocument.CurrentFormatVersion, store.Current.FormatVersion);
        Assert.Equal(1440, store.Current.Window!.Width);
        Assert.Equal(810, store.Current.Window.Height);
        Assert.Null(store.Current.Window.X);
        Assert.Null(store.Current.Window.Y);
        Assert.Equal(EditorWorkspaceWindowStateKind.Normal, store.Current.Window.State);
    }

    /// <summary>不完整或越界位置必须整体丢弃，不能把窗口恢复到半个旧坐标。</summary>
    [Fact]
    public void InvalidWindowPositionIsDiscardedAsAnAtomicPair()
    {
        EditorWorkspaceStore store = EditorWorkspaceStore.CreateInMemory(new EditorWorkspaceDocument
        {
            Window = new EditorWorkspaceWindowState
            {
                X = 120,
                Y = int.MaxValue,
            },
        });

        Assert.Null(store.Current.Window!.X);
        Assert.Null(store.Current.Window.Y);
        Assert.Contains("窗口位置", store.LastDiagnostic, StringComparison.Ordinal);
    }

    /// <summary>
    /// 显式工程始终优先于 workspace，正常退出且偏好允许时才自动恢复。
    /// </summary>
    [Fact]
    public void StartupProjectResolutionUsesCliThenCleanWorkspaceAndHonorsOptOuts()
    {
        string explicitProject = Path.Combine(Path.GetTempPath(), "PixelEngine", "Explicit");
        string workspaceProject = Path.Combine(Path.GetTempPath(), "PixelEngine", "Workspace");
        EditorWorkspaceDocument workspace = new() { LastSuccessfulProjectPath = workspaceProject };
        EditorPreferencesDocument preferences = new() { ReopenLastProject = true };

        Assert.Equal(
            explicitProject,
            EditorShellApp.ResolveStartupProjectPath(
                EditorShellOptions.Parse(["--project", explicitProject]),
                preferences,
                workspace,
                previousShutdownWasClean: false));
        Assert.Equal(
            workspaceProject,
            EditorShellApp.ResolveStartupProjectPath(
                EditorShellOptions.Parse([]),
                preferences,
                workspace,
                previousShutdownWasClean: true));
        Assert.Null(EditorShellApp.ResolveStartupProjectPath(
            EditorShellOptions.Parse([]),
            preferences,
            workspace,
            previousShutdownWasClean: false));
        Assert.Null(EditorShellApp.ResolveStartupProjectPath(
            EditorShellOptions.Parse(["--no-reopen-last-project"]),
            preferences,
            workspace,
            previousShutdownWasClean: true));
        Assert.Null(EditorShellApp.ResolveStartupProjectPath(
            EditorShellOptions.Parse([]),
            preferences with { ReopenLastProject = false },
            workspace,
            previousShutdownWasClean: true));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "pixelengine-editor-workspace-" + Guid.NewGuid().ToString("N"));
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
