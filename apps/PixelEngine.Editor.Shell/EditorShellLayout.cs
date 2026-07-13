using Hexa.NET.ImGui;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Editor Shell 默认 Dock 布局与面板可见性状态。
/// </summary>
internal sealed class EditorShellLayout
{
    internal const int CurrentLayoutVersion = 2;
    private const int MinimumSavedDockSpaceWidth = 800;
    private const int MinimumSavedDockSpaceHeight = 450;
    private readonly EditorDockSpace _dockSpace = new();

    public EditorShellLayout(
        string layoutPath,
        int targetWindowWidth = EditorWorkspaceWindowState.DefaultWidth,
        int targetWindowHeight = EditorWorkspaceWindowState.DefaultHeight,
        bool migrateToCurrentLayout = false)
    {
        LayoutPath = layoutPath;
        if (migrateToCurrentLayout && !HasCurrentLayoutVersion(layoutPath))
        {
            TryDeleteLayout(layoutPath);
            TryWriteCurrentLayoutVersion(layoutPath);
        }

        if (SavedDockLayoutIsIncompatible(layoutPath, targetWindowWidth, targetWindowHeight))
        {
            TryDeleteLayout(layoutPath);
        }

        _dockSpace.ResetLayoutState(buildDefaultLayout: !File.Exists(layoutPath));
    }

    public string LayoutPath { get; }

    public void ConfigureImGui()
    {
        ImGui.GetIO().ConfigFlags |= EditorDockSpace.BuildConfigFlags(enableMultiViewport: false);
    }

    public void DrawDockSpace()
    {
        _dockSpace.Draw();
    }

    public bool TryResetLayout(out string diagnostic)
    {
        diagnostic = string.Empty;
        bool deleted = true;
        try
        {
            File.Delete(LayoutPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            deleted = false;
            diagnostic = $"无法删除旧布局文件；当前会话已恢复默认布局，但退出后可能再次加载旧布局：{exception.Message}";
        }

        _dockSpace.ResetLayoutState(buildDefaultLayout: true);
        TryWriteCurrentLayoutVersion(LayoutPath);
        return deleted;
    }

    internal static bool SavedDockLayoutIsTooSmall(string layoutPath)
    {
        return SavedDockLayoutIsIncompatible(
            layoutPath,
            int.MaxValue,
            int.MaxValue);
    }

    internal static bool SavedDockLayoutIsIncompatible(
        string layoutPath,
        int targetWindowWidth,
        int targetWindowHeight)
    {
        if (!File.Exists(layoutPath))
        {
            return false;
        }

        bool readingViewportHostWindow = false;
        try
        {
            foreach (string line in File.ReadLines(layoutPath))
            {
                if (line.StartsWith("[Window][", StringComparison.Ordinal))
                {
                    readingViewportHostWindow = line.Contains("[WindowOverViewport_", StringComparison.Ordinal);
                    continue;
                }

                if (!TryReadSavedSize(line, readingViewportHostWindow, out int width, out int height))
                {
                    continue;
                }

                bool tooSmall = width > 0 && height > 0 &&
                    (width < MinimumSavedDockSpaceWidth || height < MinimumSavedDockSpaceHeight);
                bool tooLargeForWindow = targetWindowWidth > 0 &&
                    targetWindowHeight > 0 &&
                    (width > targetWindowWidth * 1.5 || height > targetWindowHeight * 1.5);
                if (tooSmall || tooLargeForWindow)
                {
                    return true;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }

        return false;
    }

    private static void TryDeleteLayout(string layoutPath)
    {
        try
        {
            File.Delete(layoutPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 只影响布局自愈；ImGui 仍可尝试读取原文件并给用户 Reset Layout 入口。
        }
    }

    private static bool HasCurrentLayoutVersion(string layoutPath)
    {
        string versionPath = GetVersionPath(layoutPath);
        try
        {
            return File.Exists(versionPath) &&
                int.TryParse(File.ReadAllText(versionPath).Trim(), out int version) &&
                version == CurrentLayoutVersion;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryWriteCurrentLayoutVersion(string layoutPath)
    {
        string versionPath = GetVersionPath(layoutPath);
        string temporaryPath = $"{versionPath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            string? directory = Path.GetDirectoryName(versionPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                _ = Directory.CreateDirectory(directory);
            }

            File.WriteAllText(temporaryPath, CurrentLayoutVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            File.Move(temporaryPath, versionPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 版本 sidecar 失败只会让下次启动再次回到安全默认布局，不能阻止 Editor 启动。
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // 临时 sidecar 清理失败不覆盖主结果。
                }
            }
        }
    }

    private static string GetVersionPath(string layoutPath)
    {
        return $"{layoutPath}.version";
    }

    private static bool TryReadSavedSize(string line, bool readingViewportHostWindow, out int width, out int height)
    {
        const string windowSizePrefix = "Size=";
        const string dockSpaceSizeMarker = " Size=";
        width = 0;
        height = 0;

        string? sizeText = null;
        if (readingViewportHostWindow && line.StartsWith(windowSizePrefix, StringComparison.Ordinal))
        {
            sizeText = line[windowSizePrefix.Length..];
        }
        else if (line.StartsWith("DockSpace", StringComparison.Ordinal))
        {
            int sizeIndex = line.IndexOf(dockSpaceSizeMarker, StringComparison.Ordinal);
            if (sizeIndex >= 0)
            {
                sizeText = line[(sizeIndex + dockSpaceSizeMarker.Length)..];
            }
        }

        if (string.IsNullOrWhiteSpace(sizeText))
        {
            return false;
        }

        int end = sizeText.IndexOf(' ');
        if (end >= 0)
        {
            sizeText = sizeText[..end];
        }

        string[] parts = sizeText.Split(',', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 &&
            int.TryParse(parts[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out width) &&
            int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out height);
    }
}
