using System.Text.Json;
using PixelEngine.Editor.Shell;
using PixelEngine.Rendering;
using PixelEngine.Scripting;
using PixelEngine.Testing;
using PixelEngine.UI;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 独立编辑器工程模型测试。
/// 不变式：编辑器工程模型读写与 Hosting 项目描述一致。
/// </summary>
public sealed class EditorShellProjectTests
{
    /// <summary>
    /// 验证项目选择器 Browse 成功时用文件夹选择器结果回填路径，并清理旧诊断。
    /// </summary>
    [Fact]
    public void ProjectPickerBrowseSuccessUpdatesPathAndClearsDiagnostic()
    {
        FakeProjectFolderPicker picker = new(success: true, selectedPath: @"D:\Pixel Projects\Demo", diagnostic: string.Empty);
        ProjectPickerWindow window = new(EditorShellOptions.Parse([]), picker);
        string path = @"C:\Old";

        Assert.True(window.ApplyFolderPicker(ref path));

        Assert.Equal(@"C:\Old", picker.InitialPath);
        Assert.Equal(@"D:\Pixel Projects\Demo", path);
        Assert.Empty(window.FolderPickerDiagnostic);
    }

    /// <summary>
    /// 验证项目选择器 Browse 失败时保留原路径并显示失败诊断。
    /// </summary>
    [Fact]
    public void ProjectPickerBrowseFailureKeepsPathAndStoresDiagnostic()
    {
        FakeProjectFolderPicker picker = new(success: false, selectedPath: string.Empty, diagnostic: "打开文件夹选择器失败：COM 不可用");
        ProjectPickerWindow window = new(EditorShellOptions.Parse([]), picker);
        string path = @"C:\Project";

        Assert.False(window.ApplyFolderPicker(ref path));

        Assert.Equal(@"C:\Project", picker.InitialPath);
        Assert.Equal(@"C:\Project", path);
        Assert.Equal("打开文件夹选择器失败：COM 不可用", window.FolderPickerDiagnostic);
    }

    /// <summary>
    /// 验证项目选择器 Browse 取消时不会改写路径，也不会保留旧失败诊断。
    /// </summary>
    [Fact]
    public void ProjectPickerBrowseCancelClearsStaleDiagnosticAndKeepsPath()
    {
        FakeProjectFolderPicker picker = new(success: false, selectedPath: string.Empty, diagnostic: "上一次失败");
        ProjectPickerWindow window = new(EditorShellOptions.Parse([]), picker);
        string path = @"C:\Project";
        Assert.False(window.ApplyFolderPicker(ref path));
        Assert.Equal("上一次失败", window.FolderPickerDiagnostic);

        picker.Diagnostic = string.Empty;
        Assert.False(window.ApplyFolderPicker(ref path));

        Assert.Equal(@"C:\Project", path);
        Assert.Empty(window.FolderPickerDiagnostic);
    }

    /// <summary>
    /// 验证新建工程会落盘 project.pixelproj 与默认 content/scenes/main.scene 骨架。
    /// </summary>
    [Fact]
    public void CreateNewWritesProjectDocumentAndDefaultSceneSkeleton()
    {
        // Arrange：准备输入与初始状态
        using TempDirectory temp = new();
        string projectRoot = Path.Combine(temp.Path, "SampleProject");

        EditorProject project = EditorProject.CreateNew(projectRoot, " Sample ");

        // Assert：验证预期结果
        Assert.Equal("Sample", project.Name);
        Assert.Equal("content", project.ContentRoot);
        Assert.Equal("scripts", project.ScriptSourceDir);
        Assert.Equal("scenes/main.scene", project.StartScene);
        Assert.True(File.Exists(Path.Combine(projectRoot, EditorProject.ProjectFileName)));
        Assert.True(Directory.Exists(Path.Combine(projectRoot, "content", "scenes")));
        Assert.True(Directory.Exists(Path.Combine(projectRoot, "scripts")));
        string scenePath = Path.Combine(projectRoot, "content", "scenes", "main.scene");
        Assert.True(File.Exists(scenePath));
        Assert.Contains("\"formatVersion\"", File.ReadAllText(scenePath), StringComparison.Ordinal);

        EditorProject reloaded = EditorProject.Load(projectRoot);
        EditorProjectSceneEntry scene = Assert.Single(reloaded.Scenes);
        Assert.Equal("main", scene.Name);
        Assert.Equal("scenes/main.scene", scene.Path);
        Assert.Equal("main", reloaded.ToEngineProject().StartScene);
    }

    /// <summary>
    /// 验证登记编辑器当前场景只更新场景目录，不会把玩家启动场景漂移到最近打开的场景。
    /// </summary>
    [Fact]
    public void RegisterSceneKeepsConfiguredStartScene()
    {
        using TempDirectory temp = new();
        EditorProject project = EditorProject.CreateNew(Path.Combine(temp.Path, "Project"), "Project");
        string secondaryPath = Path.Combine(project.ContentRootPath, "scenes", "secondary.scene");
        EngineSceneDocumentLoader.SaveDocument(
            new EngineSceneDocument
            {
                FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
                Name = "secondary",
                Entities = [],
            },
            secondaryPath);

        project.RegisterScene("scenes/secondary.scene");

        Assert.Equal("scenes/main.scene", project.StartScene);
        Assert.Contains(project.Scenes, static scene => scene.Path == "scenes/secondary.scene");
        EditorProject reloaded = EditorProject.Load(project.ProjectRoot);
        Assert.Equal("scenes/main.scene", reloaded.StartScene);
        Assert.Contains(reloaded.Scenes, static scene => scene.Path == "scenes/secondary.scene");
    }

    /// <summary>
    /// 验证声明场景缺失或 JSON 损坏时给出可执行诊断，而不是伪装成一个空场景。
    /// </summary>
    [Fact]
    public void LoadSceneModelRejectsMissingAndCorruptedDeclaredScene()
    {
        using TempDirectory temp = new();
        EditorProject project = EditorProject.CreateNew(Path.Combine(temp.Path, "Project"), "Project");
        string scenePath = project.ResolveSceneFullPath(project.StartScene);
        File.Delete(scenePath);

        FileNotFoundException missing = Assert.Throws<FileNotFoundException>(() =>
            EditorProjectSession.LoadSceneModel(project, project.StartScene));
        Assert.Equal(scenePath, missing.FileName);
        Assert.Contains("场景文件不存在", missing.Message, StringComparison.Ordinal);

        File.WriteAllText(scenePath, "{ damaged scene");
        InvalidOperationException corrupted = Assert.Throws<InvalidOperationException>(() =>
            EditorProjectSession.LoadSceneModel(project, project.StartScene));
        _ = Assert.IsType<JsonException>(corrupted.InnerException);
        Assert.Contains(project.StartScene, corrupted.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证原子文本提交失败时既有 JSON 保持逐字节完整，且临时文件会被清理。
    /// </summary>
    [Fact]
    public void AtomicTextWritesKeepPreviousJsonWhenCommitFails()
    {
        using TempDirectory temp = new();
        string projectPath = Path.Combine(temp.Path, "project.pixelproj");
        string scenePath = Path.Combine(temp.Path, "main.scene");
        const string original = "{\"name\":\"intact\"}";
        File.WriteAllText(projectPath, original);
        File.WriteAllText(scenePath, original);

        IOException projectFailure = Assert.Throws<IOException>(() => EditorAtomicTextFile.WriteAllText(
            projectPath,
            "{\"name\":\"replacement\"}",
            static (_, _) => throw new IOException("simulated atomic commit failure")));
        IOException sceneFailure = Assert.Throws<IOException>(() => AtomicTextFile.WriteAllText(
            scenePath,
            "{\"name\":\"replacement\"}",
            static (_, _) => throw new IOException("simulated atomic commit failure")));

        Assert.Contains("simulated", projectFailure.Message, StringComparison.Ordinal);
        Assert.Contains("simulated", sceneFailure.Message, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllText(projectPath));
        Assert.Equal(original, File.ReadAllText(scenePath));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    /// <summary>
    /// 验证 EditorProjectSession.Open 遇到合法但不存在的脚本源目录时仍能打开工程，并把 watcher 失败写入 Console。
    /// </summary>
    [NativeSmokeFact]
    [Trait("Category", "NativeSmoke")]
    public void OpenAllowsMissingScriptSourceDirectoryAndReportsWatcherStartFailedToConsole()
    {
        // Arrange：准备输入与初始状态；NativeSmokeFact 在 discovery 阶段负责未启用环境的 skipped 状态。
        using TempDirectory temp = new();
        EditorProject project = EditorProject.CreateNew(Path.Combine(temp.Path, "MissingScriptsProject"), "Missing Scripts");
        Directory.Delete(project.ScriptSourcePath, recursive: true);
        EditorShellApp app = EditorShellApp.CreateForTests();
        using RenderWindow window = RenderWindow.Create(new RenderWindowOptions
        {
            Title = "PixelEngine missing script watcher smoke",
            Width = 64,
            Height = 64,
            BackendPreference = RenderBackendPreference.Auto,
        });

        using EditorProjectSession session = EditorProjectSession.Open(project, window, app);

        // Assert：验证预期结果
        Assert.True(session.Engine.Context.TryGetService(out ScriptHotReloadController _));
        Assert.True(session.Engine.Context.TryGetService(out IScriptContext scriptContext));
        Assert.True(session.Engine.Context.TryGetService(out GameUiHost _));
        Assert.True(session.Engine.Context.TryGetService(out IGameUiService gameUi));
        Assert.Same(gameUi, scriptContext.GameUi);
        Assert.True(session.Engine.Context.TryGetService(out GameUiBackendSelection selection));
        Assert.Equal(UiBackendKind.ManagedFallback, selection.ActiveBackend);
        Assert.Contains(app.ConsoleStore.Snapshot(), entry =>
            entry.Category == EditorConsoleCategory.Ui &&
            entry.Severity == EditorConsoleSeverity.Info &&
            entry.Source == "ui-backend" &&
            entry.Text.Contains("ManagedFallback", StringComparison.Ordinal));
        Assert.Contains(app.ConsoleStore.Snapshot(), entry =>
            entry.Category == EditorConsoleCategory.Script &&
            entry.Severity == EditorConsoleSeverity.Error &&
            entry.Source == "script-hot-reload" &&
            entry.Text.Contains("WatcherStartFailed", StringComparison.Ordinal) &&
            entry.Text.Contains("无 watcher", StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证默认工作台 scripted probe 参数可被解析，供 M14 自动化路线使用。
    /// </summary>
    [Fact]
    public void ParseRecognizesDefaultWorkbenchProbeFlag()
    {
        EditorShellOptions options = EditorShellOptions.Parse(["--scripted-default-workbench-probe", "--build-output", "artifacts/workbench"]);

        Assert.True(options.ScriptedDefaultWorkbenchProbe);
        Assert.Equal("artifacts/workbench", options.BuildOutputPath);
    }

    /// <summary>
    /// 验证最近工程文件损坏时不会阻断 EditorShell 启动。
    /// </summary>
    [Fact]
    public void RecentProjectsLoadReturnsEmptyStoreWhenJsonIsCorrupted()
    {
        // Arrange：准备输入与初始状态
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "recent-projects.json");
        File.WriteAllText(path, "{ not json");

        RecentProjectsStore store = RecentProjectsStore.Load(path);

        // Assert：验证预期结果
        Assert.Empty(store.Entries);
    }

    /// <summary>
    /// 验证最近工程列表经原子写入后可以完整重载，且不会遗留临时文件。
    /// </summary>
    [Fact]
    public void RecentProjectsSaveRoundTripsWithoutTemporaryFiles()
    {
        using TempDirectory temp = new();
        EditorProject project = EditorProject.CreateNew(Path.Combine(temp.Path, "Project"), "Project");
        string path = Path.Combine(temp.Path, "recent-projects.json");
        RecentProjectsStore store = RecentProjectsStore.Load(path);
        store.AddOrUpdate(project);
        Assert.True(store.SetFavorite(project.ProjectRoot, favorite: true));

        store.Save();

        RecentProjectEntry entry = Assert.Single(RecentProjectsStore.Load(path).Entries);
        Assert.Equal(project.ProjectRoot, entry.ProjectPath);
        Assert.Equal(project.Name, entry.Name);
        Assert.True(entry.Favorite);
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    /// <summary>
    /// 验证 Recent 收藏可切换，移除后持久化列表不再包含该工程。
    /// </summary>
    [Fact]
    public void RecentProjectsFavoriteAndRemoveMutationsAreStable()
    {
        using TempDirectory temp = new();
        EditorProject first = EditorProject.CreateNew(Path.Combine(temp.Path, "First"), "First");
        EditorProject second = EditorProject.CreateNew(Path.Combine(temp.Path, "Second"), "Second");
        string path = Path.Combine(temp.Path, "recent-projects.json");
        RecentProjectsStore store = RecentProjectsStore.Load(path);
        store.AddOrUpdate(first);
        store.AddOrUpdate(second);

        Assert.True(store.SetFavorite(first.ProjectRoot, favorite: true));
        Assert.False(store.SetFavorite(first.ProjectRoot, favorite: true));
        Assert.True(store.Remove(second.ProjectRoot));
        Assert.False(store.Remove(second.ProjectRoot));
        store.Save();

        RecentProjectEntry entry = Assert.Single(RecentProjectsStore.Load(path).Entries);
        Assert.Equal(first.ProjectRoot, entry.ProjectPath);
        Assert.True(entry.Favorite);
    }

    /// <summary>
    /// 验证最近工程加载会跳过无效项、按最近打开排序、去重并裁剪上限。
    /// </summary>
    [Fact]
    public void RecentProjectsLoadNormalizesEntriesByPathOrderAndLimit()
    {
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "recent-projects.json");
        string duplicateProject = Path.Combine(temp.Path, "Project-duplicate");
        DateTimeOffset baseTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        List<RecentProjectEntry> entries =
        [
            new RecentProjectEntry
            {
                Name = "Old Duplicate",
                ProjectPath = duplicateProject,
                LastOpenedUtc = baseTime.AddDays(-1),
            },
            new RecentProjectEntry
            {
                Name = "Invalid",
                ProjectPath = "\0bad",
                LastOpenedUtc = baseTime.AddYears(1),
            },
            new RecentProjectEntry
            {
                Name = " ",
                ProjectPath = Path.Combine(temp.Path, "FallbackName"),
                LastOpenedUtc = baseTime.AddDays(30),
            },
            new RecentProjectEntry
            {
                Name = "New Duplicate",
                ProjectPath = duplicateProject,
                LastOpenedUtc = baseTime.AddDays(31),
                Favorite = true,
            },
        ];
        for (int i = 0; i < 25; i++)
        {
            entries.Add(new RecentProjectEntry
            {
                Name = $"Project {i:D2}",
                ProjectPath = Path.Combine(temp.Path, $"Project-{i:D2}"),
                LastOpenedUtc = baseTime.AddDays(i),
            });
        }

        RecentProjectsDocument document = new()
        {
            Entries = [.. entries],
        };
        File.WriteAllText(path, JsonSerializer.Serialize(document, EditorShellJsonContext.Default.RecentProjectsDocument));

        RecentProjectsStore store = RecentProjectsStore.Load(path);

        Assert.Equal(RecentProjectsStore.MaxEntries, store.Entries.Count);
        Assert.DoesNotContain(store.Entries, static entry => entry.Name == "Invalid");
        RecentProjectEntry duplicate = Assert.Single(store.Entries, entry =>
            string.Equals(entry.ProjectPath, Path.GetFullPath(duplicateProject), StringComparison.OrdinalIgnoreCase));
        Assert.Equal("New Duplicate", duplicate.Name);
        Assert.True(duplicate.Favorite);
        Assert.Contains(store.Entries, static entry => entry.Name == "FallbackName");
        Assert.All(store.Entries, static entry => Assert.Equal(Path.GetFullPath(entry.ProjectPath), entry.ProjectPath));
        RecentProjectEntry previous = store.Entries[0];
        foreach (RecentProjectEntry current in store.Entries.Skip(1))
        {
            Assert.True(previous.LastOpenedUtc >= current.LastOpenedUtc);
            previous = current;
        }
    }

    /// <summary>
    /// 验证工程文件中的目录与场景路径不能逃逸工程/content 根。
    /// </summary>
    [Fact]
    public void LoadRejectsProjectDocumentPathsThatEscapeProjectOrContentRoot()
    {
        using TempDirectory temp = new();

        AssertInvalid(temp.Path, new EditorProjectDocument
        {
            FormatVersion = EditorProject.CurrentFormatVersion,
            Name = "BadContent",
            ContentRoot = "../outside",
            ScriptSourceDir = "scripts",
            StartScene = "scenes/main.scene",
        }, "ContentRoot");

        AssertInvalid(temp.Path, new EditorProjectDocument
        {
            FormatVersion = EditorProject.CurrentFormatVersion,
            Name = "BadScripts",
            ContentRoot = "content",
            ScriptSourceDir = "../scripts",
            StartScene = "scenes/main.scene",
        }, "ScriptSourceDir");

        AssertInvalid(temp.Path, new EditorProjectDocument
        {
            FormatVersion = EditorProject.CurrentFormatVersion,
            Name = "BadStart",
            ContentRoot = "content",
            ScriptSourceDir = "scripts",
            StartScene = "../outside.scene",
        }, "StartScene");

        AssertInvalid(temp.Path, new EditorProjectDocument
        {
            FormatVersion = EditorProject.CurrentFormatVersion,
            Name = "BadScene",
            ContentRoot = "content",
            ScriptSourceDir = "scripts",
            StartScene = "scenes/main.scene",
            Scenes =
            [
                new EditorProjectSceneEntry
                {
                    Name = "escape",
                    Path = "scenes/../../outside.scene",
                },
            ],
        }, "Scenes[0].Path");
    }

    /// <summary>
    /// 验证 Save/Open 场景入口可接受 content 内绝对路径，但拒绝 content 外绝对路径。
    /// </summary>
    [Fact]
    public void ResolveSceneRelativePathRejectsAbsolutePathsOutsideContentRoot()
    {
        using TempDirectory temp = new();
        EditorProject project = EditorProject.CreateNew(Path.Combine(temp.Path, "Project"), "Project");
        string contentScene = Path.Combine(project.ContentRootPath, "scenes", "main.scene");
        string outsideScene = Path.Combine(temp.Path, "outside.scene");

        Assert.Equal("scenes/main.scene", project.ResolveSceneRelativePath(contentScene));
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => project.ResolveSceneRelativePath(outsideScene));
        Assert.Contains("content 根目录", ex.Message, StringComparison.Ordinal);
    }

    private static void AssertInvalid(string root, EditorProjectDocument document, string messageFragment)
    {
        string projectRoot = Path.Combine(root, Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(projectRoot);
        File.WriteAllText(
            Path.Combine(projectRoot, EditorProject.ProjectFileName),
            JsonSerializer.Serialize(document, EditorShellJsonContext.Default.EditorProjectDocument));

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => EditorProject.Load(projectRoot));
        Assert.Contains(messageFragment, ex.Message, StringComparison.Ordinal);
    }

    private sealed class FakeProjectFolderPicker(bool success, string selectedPath, string diagnostic) : IProjectFolderPicker
    {
        public bool Success { get; set; } = success;

        public string SelectedPath { get; set; } = selectedPath;

        public string Diagnostic { get; set; } = diagnostic;

        public string InitialPath { get; private set; } = string.Empty;

        public bool TryPickFolder(string initialPath, out string selectedPath, out string diagnostic)
        {
            InitialPath = initialPath;
            selectedPath = SelectedPath;
            diagnostic = Diagnostic;
            return Success;
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pixelengine-editor-project-" + Guid.NewGuid().ToString("N"));
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
