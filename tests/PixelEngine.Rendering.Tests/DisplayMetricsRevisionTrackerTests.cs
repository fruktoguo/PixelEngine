using Xunit;

namespace PixelEngine.Rendering.Tests;

/// <summary>
/// 显示器度量的帧边界 revision 与 raw DPI 边界测试。
/// </summary>
public sealed class DisplayMetricsRevisionTrackerTests
{
    /// <summary>
    /// monitor、framebuffer scale 或 raw physical DPI 变化才递增 revision。
    /// </summary>
    [Fact]
    public void RevisionChangesOnlyWhenCommittedDisplayMetricsChange()
    {
        DisplayMetricsRevisionTracker tracker = new();

        DisplayMetricsSnapshot first = tracker.Commit(11, 1f, 1f, null);
        DisplayMetricsSnapshot unchanged = tracker.Commit(11, 1f, 1f, null);
        DisplayMetricsSnapshot monitorChanged = tracker.Commit(12, 1f, 1f, null);
        DisplayMetricsSnapshot dpiChanged = tracker.Commit(12, 1.5f, 1.5f, 142f);

        Assert.Equal(1, first.Revision);
        Assert.Equal(first, unchanged);
        Assert.Equal(2, monitorChanged.Revision);
        Assert.Equal(3, dpiChanged.Revision);
        Assert.Equal(142f, dpiChanged.ActualPhysicalDpi);
    }

    /// <summary>
    /// 非法采样不会污染最近一次已提交快照。
    /// </summary>
    [Fact]
    public void InvalidSampleIsRejectedWithoutChangingCurrentRevision()
    {
        DisplayMetricsRevisionTracker tracker = new();
        DisplayMetricsSnapshot initial = tracker.Commit(1, 1f, 1f, 96f);

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => tracker.Commit(1, float.NaN, 1f, 96f));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => tracker.Commit(1, 1f, 1f, 0f));

        Assert.Equal(initial, tracker.Current);
    }

    /// <summary>
    /// Windows 查询使用 MDT_RAW_DPI，并且源码中不存在 96×framebuffer scale 的物理 DPI 伪造。
    /// </summary>
    [Fact]
    public void WindowsSourceUsesRawMonitorDpiWithoutSynthesizingPhysicalDpi()
    {
        string root = FindRepositoryRoot();
        string rawDpi = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Rendering", "WindowsMonitorDpi.cs"));
        string source = File.ReadAllText(Path.Combine(root, "src", "PixelEngine.Rendering", "RenderWindowDisplayMetricsSource.cs"));

        Assert.Contains("MonitorDpiTypeRaw = 2", rawDpi, StringComparison.Ordinal);
        Assert.Contains("GetDpiForMonitor", rawDpi, StringComparison.Ordinal);
        string combined = rawDpi + source;
        Assert.Contains("actualPhysicalDpi", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("96 *", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("96f *", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("DllImport", rawDpi, StringComparison.Ordinal);
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

            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }
}
