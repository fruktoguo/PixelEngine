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

    /// <summary>外部 automation 要求持久化与内存布局原子成功，文件占用时必须失败闭合。</summary>
    [Fact]
    public void AutomationResetLayoutFailsClosedWhenIniIsLocked()
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

            bool reset = layout.TryResetLayoutForAutomation(out string diagnostic);

            Assert.False(reset);
            Assert.Contains("无法原子重置布局", diagnostic, StringComparison.Ordinal);
            Assert.Contains("当前会话保持不变", diagnostic, StringComparison.Ordinal);
            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Reset 的版本 sidecar 提交失败时必须恢复 layout，且不得切换当前会话。</summary>
    [Fact]
    public void AutomationResetLayoutRestoresLayoutWhenVersionCommitFails()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "editor-shell-imgui.ini");
            const string before = "[Window][Scene]\nPos=1,2\n[Docking][Data]\nDockSpace ID=0x1\n";
            File.WriteAllText(path, before);
            File.WriteAllText($"{path}.version", "2");
            EditorShellLayout layout = new(path, 1280, 720);
            using FileStream lockedVersion = new(
                $"{path}.version",
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            bool reset = layout.TryResetLayoutForAutomation(out string diagnostic);

            Assert.False(reset);
            Assert.Contains("无法原子重置布局", diagnostic, StringComparison.Ordinal);
            Assert.Contains("当前会话保持不变", diagnostic, StringComparison.Ordinal);
            Assert.Equal(before, File.ReadAllText(path));
            Assert.Equal("2", File.ReadAllText($"{path}.version"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>automation layout 只接受有界 Window/Table/Docking sections，并规范换行。</summary>
    [Fact]
    public void AutomationLayoutValidationRejectsNativeParserGarbage()
    {
        Assert.True(EditorDockLayoutValidator.TryValidate(
            "[Window][Scene]\r\nPos=0,0\r\n[Docking][Data]\r\nDockSpace ID=0x1\r\n",
            out string normalized,
            out string validDiagnostic),
            validDiagnostic);
        Assert.DoesNotContain('\r', normalized);
        Assert.EndsWith("\n", normalized, StringComparison.Ordinal);

        Assert.False(EditorDockLayoutValidator.TryValidate(
            "[Malicious][Native]\nvalue=1\n[Docking][Data]\n",
            out _,
            out string unknownDiagnostic));
        Assert.Contains("未知", unknownDiagnostic, StringComparison.Ordinal);
        Assert.False(EditorDockLayoutValidator.TryValidate(
            "[Window][Scene]\nPos=0,0\n",
            out _,
            out string missingDiagnostic));
        Assert.Contains("[Docking][Data]", missingDiagnostic, StringComparison.Ordinal);

        string oversized = "[Window][Scene]\n" +
            string.Join('\n', Enumerable.Repeat(new string('x', 64), 5_000)) +
            "\n[Docking][Data]\n";
        Assert.False(EditorDockLayoutValidator.TryValidate(
            oversized,
            out _,
            out string oversizedDiagnostic));
        Assert.Contains("UTF-8", oversizedDiagnostic, StringComparison.Ordinal);
    }

    /// <summary>layout 与版本 sidecar 都必须落盘，且返回规范化文本。</summary>
    [Fact]
    public void AutomationLayoutPersistenceWritesLayoutAndVersionAtomically()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "editor-shell-imgui.ini");
            EditorShellLayout layout = new(path, 1280, 720, migrateToCurrentLayout: true);
            const string input = "[Window][Scene]\r\nPos=0,0\r\n[Docking][Data]\r\nDockSpace ID=0x1\r\n";

            bool saved = layout.TryPersistAutomationLayout(input, out string normalized, out string diagnostic);

            Assert.True(saved, diagnostic);
            Assert.Equal(normalized, File.ReadAllText(path));
            Assert.Equal(
                EditorShellLayout.CurrentLayoutVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                File.ReadAllText($"{path}.version"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>version sidecar 被占用时，已经写入的新 layout 必须回滚为完整 before image。</summary>
    [Fact]
    public void AutomationLayoutPersistenceRollsBackLayoutWhenVersionCommitFails()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "editor-shell-imgui.ini");
            const string before = "[Window][Scene]\nPos=1,2\n[Docking][Data]\nDockSpace ID=0x1\n";
            const string after = "[Window][Scene]\nPos=9,9\n[Docking][Data]\nDockSpace ID=0x2\n";
            File.WriteAllText(path, before);
            File.WriteAllText($"{path}.version", EditorShellLayout.CurrentLayoutVersion.ToString());
            EditorShellLayout layout = new(path, 1280, 720);
            using FileStream lockedVersion = new(
                $"{path}.version",
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            bool saved = layout.TryPersistAutomationLayout(after, out _, out string diagnostic);

            Assert.False(saved);
            Assert.Contains("无法原子持久化", diagnostic, StringComparison.Ordinal);
            Assert.Equal(before, File.ReadAllText(path));
            Assert.Equal(
                EditorShellLayout.CurrentLayoutVersion.ToString(),
                File.ReadAllText($"{path}.version"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Reset 后的上层失败必须按原始字节与存在性恢复 ini 和版本 sidecar。</summary>
    [Fact]
    public void AutomationPersistenceSnapshotRestoresExactTwoFileBeforeImage()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "editor-shell-imgui.ini");
            byte[] layoutBefore = [0xef, 0xbb, 0xbf, .. System.Text.Encoding.UTF8.GetBytes(
                "[Window][Scene]\r\nPos=1,2\r\n[Docking][Data]\r\nDockSpace ID=0x1\r\n")];
            byte[] versionBefore = System.Text.Encoding.UTF8.GetBytes("2\r\n");
            File.WriteAllBytes(path, layoutBefore);
            File.WriteAllBytes($"{path}.version", versionBefore);
            EditorShellLayout layout = new(path, 1280, 720);
            Assert.True(
                layout.TryCaptureAutomationPersistence(
                    out EditorLayoutPersistenceSnapshot before,
                    out string captureDiagnostic),
                captureDiagnostic);

            File.WriteAllText(path, "changed");
            File.Delete($"{path}.version");

            Assert.True(
                layout.TryRestoreAutomationPersistence(before, out string restoreDiagnostic),
                restoreDiagnostic);
            Assert.Equal(layoutBefore, File.ReadAllBytes(path));
            Assert.Equal(versionBefore, File.ReadAllBytes($"{path}.version"));

            File.Delete($"{path}.version");
            Assert.True(
                layout.TryCaptureAutomationPersistence(
                    out EditorLayoutPersistenceSnapshot withoutVersion,
                    out captureDiagnostic),
                captureDiagnostic);
            File.WriteAllText($"{path}.version", "3");
            Assert.True(
                layout.TryRestoreAutomationPersistence(withoutVersion, out restoreDiagnostic),
                restoreDiagnostic);
            Assert.False(File.Exists($"{path}.version"));
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
