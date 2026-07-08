using Xunit;

namespace PixelEngine.Rendering.Tests;

public sealed class RenderWindowDpiAwarenessDisciplineTests
{
    [Fact]
    public void RenderWindowEnablesWindowsDpiAwarenessBeforeSilkWindowCreation()
    {
        string root = FindRepositoryRoot();
        string renderWindow = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Rendering", "RenderWindow.cs"));
        string dpiAwareness = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Rendering", "WindowsDpiAwareness.cs"));

        Assert.Contains("WindowsDpiAwareness.EnsureEnabled();", renderWindow, StringComparison.Ordinal);
        Assert.True(
            renderWindow.IndexOf("WindowsDpiAwareness.EnsureEnabled();", StringComparison.Ordinal) <
            renderWindow.IndexOf("Window.Create(windowOptions)", StringComparison.Ordinal),
            "DPI awareness 必须在 Silk Window.Create 前设置。");
        Assert.Contains("OperatingSystem.IsWindows()", dpiAwareness, StringComparison.Ordinal);
        Assert.Contains("SetProcessDpiAwarenessContext", dpiAwareness, StringComparison.Ordinal);
        Assert.Contains("new(-4)", dpiAwareness, StringComparison.Ordinal);
        Assert.Contains("LibraryImport(\"user32.dll\"", dpiAwareness, StringComparison.Ordinal);
        Assert.DoesNotContain("DllImport", dpiAwareness, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        string directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "PixelEngine.sln")))
            {
                return directory;
            }

            string? parent = Directory.GetParent(directory)?.FullName;
            if (string.Equals(parent, directory, StringComparison.Ordinal))
            {
                break;
            }

            directory = parent ?? string.Empty;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }
}
