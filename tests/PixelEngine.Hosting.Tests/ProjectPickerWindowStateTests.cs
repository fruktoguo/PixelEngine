using PixelEngine.Editor.Shell;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Project Picker 可见性与程序化 tab 选择状态测试。
/// </summary>
public sealed class ProjectPickerWindowStateTests
{
    /// <summary>
    /// 验证 Close 隐藏窗口，Focus 重新显示并请求目标 tab。
    /// </summary>
    [Fact]
    public void CloseAndFocusControlVisibilityAndRequestedTab()
    {
        ProjectPickerWindow window = new(EditorShellOptions.Parse([]), new NoOpFolderPicker());

        Assert.True(window.Visible);
        Assert.Equal(ProjectPickerMode.RecentProjects, window.Mode);
        Assert.Equal(ProjectPickerMode.RecentProjects, window.PendingTabSelection);

        window.Close();

        Assert.False(window.Visible);

        window.Focus(ProjectPickerMode.NewProject);

        Assert.True(window.Visible);
        Assert.Equal(ProjectPickerMode.NewProject, window.Mode);
        Assert.Equal(ProjectPickerMode.NewProject, window.PendingTabSelection);

        window.Close();
        window.Focus(ProjectPickerMode.OpenProject);

        Assert.True(window.Visible);
        Assert.Equal(ProjectPickerMode.OpenProject, window.Mode);
        Assert.Equal(ProjectPickerMode.OpenProject, window.PendingTabSelection);
    }

    /// <summary>
    /// 验证命令行工程路径让首次绘制选择 Open Project tab。
    /// </summary>
    [Fact]
    public void ProjectOptionRequestsOpenProjectTabOnFirstDraw()
    {
        EditorShellOptions options = EditorShellOptions.Parse(["--project", @"C:\Projects\Demo"]);

        ProjectPickerWindow window = new(options, new NoOpFolderPicker());

        Assert.Equal(ProjectPickerMode.OpenProject, window.Mode);
        Assert.Equal(ProjectPickerMode.OpenProject, window.PendingTabSelection);
    }

    /// <summary>
    /// 验证 Recent 搜索同时匹配工程名和路径，并忽略大小写。
    /// </summary>
    [Fact]
    public void RecentProjectSearchMatchesNameAndPath()
    {
        RecentProjectEntry entry = new()
        {
            Name = "Lava Mine",
            ProjectPath = @"D:\Projects\PixelEngineDemo",
        };

        Assert.True(ProjectPickerWindow.RecentProjectMatchesSearch(entry, string.Empty));
        Assert.True(ProjectPickerWindow.RecentProjectMatchesSearch(entry, "lava"));
        Assert.True(ProjectPickerWindow.RecentProjectMatchesSearch(entry, "PIXELENGINE"));
        Assert.False(ProjectPickerWindow.RecentProjectMatchesSearch(entry, "ball world"));
    }

    /// <summary>
    /// 验证新工程 location/name 会解析为安全子目录，并拒绝路径逃逸及非空目标。
    /// </summary>
    [Fact]
    public void NewProjectPathValidationRejectsUnsafeOrOccupiedDestinations()
    {
        using TempDirectory temp = new();

        Assert.True(ProjectPickerWindow.TryResolveNewProjectPath(
            temp.Path,
            " Demo ",
            out string projectPath,
            out string diagnostic));
        Assert.Equal(Path.Combine(temp.Path, "Demo"), projectPath);
        Assert.Empty(diagnostic);

        Assert.False(ProjectPickerWindow.TryResolveNewProjectPath(
            temp.Path,
            "../escape",
            out _,
            out diagnostic));
        Assert.Contains("characters", diagnostic, StringComparison.OrdinalIgnoreCase);

        string occupied = Path.Combine(temp.Path, "Occupied");
        _ = Directory.CreateDirectory(occupied);
        File.WriteAllText(Path.Combine(occupied, "keep.txt"), "keep");
        Assert.False(ProjectPickerWindow.TryResolveNewProjectPath(
            temp.Path,
            "Occupied",
            out _,
            out diagnostic));
        Assert.Contains("not empty", diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 验证最近打开时间按 Unity Hub 式相对时间显示，较旧记录回退到日期。
    /// </summary>
    [Fact]
    public void LastOpenedFormattingUsesRelativeTimeAndStableDateFallback()
    {
        DateTimeOffset now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal("Just now", ProjectPickerWindow.FormatLastOpened(now.AddSeconds(-20), now));
        Assert.Equal("15 min ago", ProjectPickerWindow.FormatLastOpened(now.AddMinutes(-15), now));
        Assert.Equal("3 hr ago", ProjectPickerWindow.FormatLastOpened(now.AddHours(-3), now));
        Assert.Equal("5 days ago", ProjectPickerWindow.FormatLastOpened(now.AddDays(-5), now));
        Assert.Equal("2026-05-01", ProjectPickerWindow.FormatLastOpened(
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            now));
    }

    private sealed class NoOpFolderPicker : IProjectFolderPicker
    {
        public bool TryPickFolder(string initialPath, out string selectedPath, out string diagnostic)
        {
            selectedPath = string.Empty;
            diagnostic = string.Empty;
            return false;
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pixelengine-project-picker-{Guid.NewGuid():N}");
            _ = Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
