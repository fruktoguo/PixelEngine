using PixelEngine.Editor.Shell;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 验证独立编辑器壳的 ImGui dock layout 自愈逻辑。
/// 不变式：dock layout 损坏时可自愈、默认面板组合可恢复。
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

    /// <summary>
    /// 4K 工作台布局恢复到 720p 窗口时会重建，避免所有面板落在可视区外。
    /// </summary>
    [Fact]
    public void SavedDockLayoutRejectsViewportFarLargerThanRestoredWindow()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "editor-shell-imgui.ini");
            File.WriteAllText(path, """
[Window][WindowOverViewport_11111111]
Pos=0,24
Size=3840,1965
Collapsed=0
""");

            Assert.True(EditorShellLayout.SavedDockLayoutIsIncompatible(path, 1280, 720));
            _ = new EditorShellLayout(path, 1280, 720);
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 默认工作台结构升级时会丢弃旧 ini，并用 sidecar 记录当前结构版本。
    /// </summary>
    [Fact]
    public void ConstructorMigratesLegacyLayoutAndWritesCurrentVersion()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "editor-shell-imgui.ini");
            File.WriteAllText(path, "[Window][Legacy]\nPos=0,0\nSize=1280,720\n");
            File.WriteAllText($"{path}.version", "1");

            _ = new EditorShellLayout(path, 1280, 720, migrateToCurrentLayout: true);

            Assert.False(File.Exists(path));
            Assert.Equal(
                EditorShellLayout.CurrentLayoutVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                File.ReadAllText($"{path}.version").Trim());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 验证 Reset Layout 删除持久化 ini 并报告成功，使下一帧可重建默认 dock tree。
    /// </summary>
    [Fact]
    public void ResetLayoutDeletesSavedIniWithoutThrowing()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "editor-shell-imgui.ini");
            File.WriteAllText(path, "[Window][WindowOverViewport_1]\nSize=1280,720\n");
            EditorShellLayout layout = new(path, 1280, 720);

            bool reset = layout.TryResetLayout(out string diagnostic);

            Assert.True(reset, diagnostic);
            Assert.Empty(diagnostic);
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Windows 上布局文件被其它进程占用时，Reset Layout 仍保留内存重建路径并返回可见诊断，
    /// 不让 Preferences 按钮把整个 Editor 弄崩。
    /// </summary>
    [Fact]
    public void ResetLayoutReportsLockedIniInsteadOfThrowing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "editor-shell-imgui.ini");
            File.WriteAllText(path, "[Window][WindowOverViewport_1]\nSize=1280,720\n");
            EditorShellLayout layout = new(path, 1280, 720);
            using FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            bool reset = layout.TryResetLayout(out string diagnostic);

            Assert.False(reset);
            Assert.Contains("无法删除旧布局文件", diagnostic, StringComparison.Ordinal);
            Assert.True(File.Exists(path));
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
