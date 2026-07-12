using PixelEngine.Editor.Shell;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// 系统标题栏关闭请求与 dirty transition 的分支测试。
/// </summary>
public sealed class EditorNativeCloseGuardTests
{
    /// <summary>
    /// 验证无关闭请求不触发动作，clean close 直接退出，dirty close 则撤销并请求受保护退出。
    /// </summary>
    [Theory]
    [InlineData(false, false, false, 0, 0)]
    [InlineData(false, true, false, 0, 0)]
    [InlineData(true, false, true, 0, 0)]
    [InlineData(true, true, false, 1, 1)]
    public void NativeCloseUsesDirtyGuard(
        bool closeRequested,
        bool dirty,
        bool expectedExit,
        int expectedCancelCalls,
        int expectedExitRequests)
    {
        int cancelCalls = 0;
        int exitRequests = 0;

        bool shouldExit = EditorNativeCloseGuard.ShouldExit(
            closeRequested,
            dirty,
            () => cancelCalls++,
            () => exitRequests++);

        Assert.Equal(expectedExit, shouldExit);
        Assert.Equal(expectedCancelCalls, cancelCalls);
        Assert.Equal(expectedExitRequests, exitRequests);
    }
}
