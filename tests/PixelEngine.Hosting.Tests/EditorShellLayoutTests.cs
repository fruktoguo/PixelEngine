using PixelEngine.Editor.Shell;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 验证独立编辑器壳的 ImGui dock layout 自愈逻辑。
/// </summary>
public sealed class EditorShellLayoutTests
{
    /// <summary>
    /// 验证旧版 1/4 主视口布局会被识别为陈旧布局。
    /// </summary>
    [Fact]
    public void SavedDockLayoutIsTooSmallDetectsStaleQuarterViewportLayout()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "editor-shell-imgui.ini");
            File.WriteAllText(path, """
[Window][Debug##Default]
Pos=60,60
Size=400,400
Collapsed=0

[Window][WindowOverViewport_11111111]
Pos=0,24
Size=640,336
Collapsed=0

[Docking][Data]
DockSpace       ID=0x08BD597D Window=0x1BBC0F80 Pos=0,24 Size=640,336 Split=X
""");

            Assert.True(EditorShellLayout.SavedDockLayoutIsTooSmall(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 验证普通浮窗较小不会触发主 dockspace 重置。
    /// </summary>
    [Fact]
    public void SavedDockLayoutIsTooSmallIgnoresSmallFloatingWindows()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "editor-shell-imgui.ini");
            File.WriteAllText(path, """
[Window][Debug##Default]
Pos=60,60
Size=400,400
Collapsed=0

[Window][WindowOverViewport_11111111]
Pos=0,24
Size=1280,696
Collapsed=0

[Window][Project Picker]
Pos=24,48
Size=680,500
Collapsed=0

[Docking][Data]
DockSpace       ID=0x08BD597D Window=0x1BBC0F80 Pos=0,24 Size=1280,696 Split=X
""");

            Assert.False(EditorShellLayout.SavedDockLayoutIsTooSmall(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 验证构造布局控制器时会删除陈旧主 dockspace 布局。
    /// </summary>
    [Fact]
    public void ConstructorDeletesStaleSavedDockLayout()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "editor-shell-imgui.ini");
            File.WriteAllText(path, """
[Window][WindowOverViewport_11111111]
Pos=0,24
Size=640,336
Collapsed=0
""");

            EditorShellLayout layout = new(path);

            Assert.Equal(path, layout.LayoutPath);
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "PixelEngine.EditorShellLayoutTests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(path);
        return path;
    }
}
