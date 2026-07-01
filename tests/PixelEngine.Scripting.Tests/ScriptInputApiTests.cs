using Xunit;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// 脚本输入快照 API 测试。
/// </summary>
public sealed class ScriptInputApiTests
{
    /// <summary>
    /// 验证键盘、鼠标、滚轮与边沿状态来自逐帧输入快照。
    /// </summary>
    [Fact]
    public void UpdateComputesKeyMouseEdgesAxesAndWheel()
    {
        ScriptInputApi input = new();

        input.Update([Key.D, Key.Space], [MouseButton.Left], mouseX: 12, mouseY: 34, wheelY: 2);

        Assert.True(input.IsDown(Key.D));
        Assert.True(input.WasPressed(Key.Space));
        Assert.False(input.WasReleased(Key.Space));
        Assert.Equal(1f, input.Axis(Axis.Horizontal));
        Assert.Equal((12f, 34f), input.MousePixel);
        Assert.Equal(2f, input.MouseWheelY);
        Assert.True(input.IsMouseDown(MouseButton.Left));
        Assert.True(input.WasMousePressed(MouseButton.Left));

        input.Update([Key.A], [MouseButton.Right], mouseX: 20, mouseY: 40, wheelY: -1);

        Assert.False(input.IsDown(Key.D));
        Assert.True(input.WasReleased(Key.D));
        Assert.True(input.WasReleased(Key.Space));
        Assert.True(input.WasMouseReleased(MouseButton.Left));
        Assert.True(input.WasMousePressed(MouseButton.Right));
        Assert.Equal(-1f, input.Axis(Axis.Horizontal));
        Assert.Equal(-1f, input.MouseWheelY);
    }
}
