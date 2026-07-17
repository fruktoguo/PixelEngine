using Xunit;

namespace PixelEngine.Demo.Tests;

/// <summary>
/// 真实窗口物理 UI 输入探针的完成时机测试。
/// </summary>
public sealed class DemoPhysicalUiInputProbeTests
{
    /// <summary>验证 action 前不提前退出，action 后保留 30 帧供 UI 状态与截图稳定。</summary>
    [Theory]
    [InlineData(500, -1, false)]
    [InlineData(99, 100, false)]
    [InlineData(100, 100, false)]
    [InlineData(129, 100, false)]
    [InlineData(130, 100, true)]
    [InlineData(131, 100, true)]
    public void CompletionRequiresThirtyFramesAfterObservedAction(
        long framesObserved,
        long actionObservedFrame,
        bool expected)
    {
        Assert.Equal(
            expected,
            DemoPhysicalUiInputProbe.ShouldStopAfterAction(framesObserved, actionObservedFrame));
    }
}
