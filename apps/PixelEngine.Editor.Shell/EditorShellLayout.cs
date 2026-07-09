using Hexa.NET.ImGui;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Editor Shell 默认 Dock 布局与面板可见性状态。
/// </summary>
internal sealed class EditorShellLayout
{
    private const int MinimumSavedDockSpaceWidth = 800;
    private const int MinimumSavedDockSpaceHeight = 450;
    private readonly EditorDockSpace _dockSpace = new();

    public EditorShellLayout(string layoutPath)
    {
        LayoutPath = layoutPath;
        if (SavedDockLayoutIsTooSmall(layoutPath))
        {
            File.Delete(layoutPath);
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

    public void ResetLayout()
    {
        if (File.Exists(LayoutPath))
        {
            File.Delete(LayoutPath);
        }

        _dockSpace.ResetLayoutState(buildDefaultLayout: true);
    }

    internal static bool SavedDockLayoutIsTooSmall(string layoutPath)
    {
        if (!File.Exists(layoutPath))
        {
            return false;
        }

        bool readingViewportHostWindow = false;
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

            if (width > 0 &&
                height > 0 &&
                (width < MinimumSavedDockSpaceWidth || height < MinimumSavedDockSpaceHeight))
            {
                return true;
            }
        }

        return false;
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
