using Xunit;

namespace PixelEngine.UI.Tests;

/// <summary>
/// UI 脏矩形合并测试：扩张、合并与裁剪不变式。
/// </summary>
public sealed class UiDirtyRectMergeTests
{
    /// <summary>
    /// 验证Collector合并Overlapping Rects Into Minimum Bounds行为符合预期。
    /// </summary>
    [Fact]
    public void CollectorMergesOverlappingRectsIntoMinimumBounds()
    {
        UiDirtyRectCollector collector = new();

        _ = collector.Add(new UiDirtyRect(10, 12, 20, 8));
        _ = collector.Add(new UiDirtyRect(24, 16, 12, 10));

        UiDirtyRect[] output = new UiDirtyRect[4];
        int count = collector.CopyTo(output);

        Assert.Equal(1, count);
        Assert.Equal(new UiDirtyRect(10, 12, 26, 14), output[0]);
    }

    /// <summary>
    /// 验证Collector合并Edge Adjacent Rects行为符合预期。
    /// </summary>
    [Fact]
    public void CollectorMergesEdgeAdjacentRects()
    {
        UiDirtyRectCollector collector = new();

        _ = collector.Add(new UiDirtyRect(0, 0, 8, 8));
        _ = collector.Add(new UiDirtyRect(8, 0, 4, 8));

        UiDirtyRect[] output = new UiDirtyRect[2];
        int count = collector.CopyTo(output);

        Assert.Equal(1, count);
        Assert.Equal(new UiDirtyRect(0, 0, 12, 8), output[0]);
    }

    /// <summary>
    /// 验证Collector保持Separated Rects Independent行为符合预期。
    /// </summary>
    [Fact]
    public void CollectorKeepsSeparatedRectsIndependent()
    {
        UiDirtyRectCollector collector = new();

        _ = collector.Add(new UiDirtyRect(0, 0, 8, 8));
        _ = collector.Add(new UiDirtyRect(12, 0, 4, 8));

        UiDirtyRect[] output = new UiDirtyRect[2];
        int count = collector.CopyTo(output);

        Assert.Equal(2, count);
        Assert.Contains(new UiDirtyRect(0, 0, 8, 8), output);
        Assert.Contains(new UiDirtyRect(12, 0, 4, 8), output);
    }

    /// <summary>
    /// 验证Collector不会Merge Corner Touching Rects行为符合预期。
    /// </summary>
    [Fact]
    public void CollectorDoesNotMergeCornerTouchingRects()
    {
        UiDirtyRectCollector collector = new();

        _ = collector.Add(new UiDirtyRect(0, 0, 8, 8));
        _ = collector.Add(new UiDirtyRect(8, 8, 4, 4));

        UiDirtyRect[] output = new UiDirtyRect[2];
        int count = collector.CopyTo(output);

        Assert.Equal(2, count);
        Assert.Contains(new UiDirtyRect(0, 0, 8, 8), output);
        Assert.Contains(new UiDirtyRect(8, 8, 4, 4), output);
    }

    /// <summary>
    /// 验证Collector忽略Empty Rects And Can Clear行为符合预期。
    /// </summary>
    [Fact]
    public void CollectorIgnoresEmptyRectsAndCanClear()
    {
        UiDirtyRectCollector collector = new();

        _ = collector.Add(new UiDirtyRect(0, 0, 0, 8));
        _ = collector.Add(new UiDirtyRect(1, 2, 3, 4));
        collector.Clear();

        UiDirtyRect[] output = new UiDirtyRect[1];
        Assert.Equal(0, collector.CopyTo(output));
    }
}
