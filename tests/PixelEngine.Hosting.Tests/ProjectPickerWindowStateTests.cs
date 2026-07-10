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
        Assert.Equal(ProjectPickerMode.NewProject, window.Mode);
        Assert.Equal(ProjectPickerMode.NewProject, window.PendingTabSelection);

        window.Close();

        Assert.False(window.Visible);

        window.Focus(ProjectPickerMode.OpenProject);

        Assert.True(window.Visible);
        Assert.Equal(ProjectPickerMode.OpenProject, window.Mode);
        Assert.Equal(ProjectPickerMode.OpenProject, window.PendingTabSelection);

        window.Close();
        window.Focus(ProjectPickerMode.RecentProjects);

        Assert.True(window.Visible);
        Assert.Equal(ProjectPickerMode.RecentProjects, window.Mode);
        Assert.Equal(ProjectPickerMode.RecentProjects, window.PendingTabSelection);
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

    private sealed class NoOpFolderPicker : IProjectFolderPicker
    {
        public bool TryPickFolder(string initialPath, out string selectedPath, out string diagnostic)
        {
            selectedPath = string.Empty;
            diagnostic = string.Empty;
            return false;
        }
    }
}
