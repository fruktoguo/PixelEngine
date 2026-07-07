using System.Text.Json;
using PixelEngine.Editor.Shell;
using PixelEngine.Rendering;
using PixelEngine.Scripting;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 独立编辑器工程模型测试。
/// </summary>
public sealed class EditorShellProjectTests
{
    /// <summary>
    /// 验证新建工程会落盘 project.pixelproj 与默认 content/scenes/main.scene 骨架。
    /// </summary>
    [Fact]
    public void CreateNewWritesProjectDocumentAndDefaultSceneSkeleton()
    {
        using TempDirectory temp = new();
        string projectRoot = Path.Combine(temp.Path, "SampleProject");

        EditorProject project = EditorProject.CreateNew(projectRoot, " Sample ");

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
    /// 验证 EditorProjectSession.Open 遇到合法但不存在的脚本源目录时仍能打开工程，并把 watcher 失败写入 Console。
    /// </summary>
    [Fact]
    public void OpenAllowsMissingScriptSourceDirectoryAndReportsWatcherStartFailedToConsole()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PIXELENGINE_RENDERING_GL_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

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

        Assert.True(session.Engine.Context.TryGetService(out ScriptHotReloadController _));
        Assert.True(session.Engine.Context.TryGetService(out IScriptContext _));
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
