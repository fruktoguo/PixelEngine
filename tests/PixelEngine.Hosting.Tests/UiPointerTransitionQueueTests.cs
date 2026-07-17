using PixelEngine.UI;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 验证窗口事件泵与 UI 帧采样之间的物理按钮边沿不会丢失或留下卡键状态。
/// </summary>
public sealed class UiPointerTransitionQueueTests
{
    /// <summary>
    /// 同一帧事件泵内完成的按下和释放必须按顺序跨采样保留。
    /// </summary>
    [Fact]
    public void FastPressAndReleaseRemainDistinctSnapshots()
    {
        UiPointerTransitionQueue queue = new(capacity: 4);

        queue.Record(UiPointerButton.Left, isDown: true, 10f, 20f);
        queue.Record(UiPointerButton.Left, isDown: false, 11f, 21f);

        Assert.Equal(2, queue.PendingCount);
        Assert.Equal(1, queue.LeftPressEdges);
        Assert.Equal(1, queue.LeftReleaseEdges);
        Assert.True(queue.TryDequeue(out UiPointerState pressed));
        Assert.True(pressed.LeftDown);
        Assert.Equal((10f, 20f), (pressed.X, pressed.Y));
        Assert.True(queue.TryDequeue(out UiPointerState released));
        Assert.False(released.LeftDown);
        Assert.Equal((11f, 21f), (released.X, released.Y));
        Assert.False(queue.TryDequeue(out _));
    }

    /// <summary>
    /// 输入洪泛超过固定容量时保留最早边沿与最终权威状态，而不是让按钮永久按下。
    /// </summary>
    [Fact]
    public void FullQueueCoalescesTailToFinalAuthoritativeState()
    {
        UiPointerTransitionQueue queue = new(capacity: 2);

        queue.Record(UiPointerButton.Left, isDown: true, 1f, 1f);
        queue.Record(UiPointerButton.Left, isDown: false, 2f, 2f);
        queue.Record(UiPointerButton.Left, isDown: true, 3f, 3f);
        queue.Record(UiPointerButton.Left, isDown: false, 4f, 4f);

        Assert.Equal(2, queue.PendingCount);
        Assert.Equal(2, queue.CoalescedTransitionCount);
        Assert.True(queue.TryDequeue(out UiPointerState pressed));
        Assert.True(pressed.LeftDown);
        Assert.True(queue.TryDequeue(out UiPointerState released));
        Assert.False(released.LeftDown);
        Assert.Equal((4f, 4f), (released.X, released.Y));
    }

    /// <summary>
    /// 窗口失焦必须为所有已观察按键排入释放，防止重新聚焦后卡住拖拽。
    /// </summary>
    [Fact]
    public void ReleaseAllQueuesReleasesForObservedButtons()
    {
        UiPointerTransitionQueue queue = new(capacity: 8);
        queue.Record(UiPointerButton.Left, isDown: true, 1f, 2f);
        queue.Record(UiPointerButton.Right, isDown: true, 1f, 2f);

        queue.ReleaseAll(7f, 8f);

        Assert.Equal(4, queue.PendingCount);
        Assert.True(queue.TryDequeue(out UiPointerState leftDown));
        Assert.True(leftDown.LeftDown);
        Assert.True(queue.TryDequeue(out UiPointerState bothDown));
        Assert.True(bothDown.LeftDown);
        Assert.True(bothDown.RightDown);
        Assert.True(queue.TryDequeue(out UiPointerState rightOnly));
        Assert.False(rightOnly.LeftDown);
        Assert.True(rightOnly.RightDown);
        Assert.True(queue.TryDequeue(out UiPointerState released));
        Assert.False(released.LeftDown);
        Assert.False(released.RightDown);
    }
}
