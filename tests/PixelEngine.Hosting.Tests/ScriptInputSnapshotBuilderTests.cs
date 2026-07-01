using PixelEngine.Scripting;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Hosting 输入快照构建器测试。
/// </summary>
public sealed class ScriptInputSnapshotBuilderTests
{
    /// <summary>
    /// 验证键盘/鼠标通道可独立门控，避免 Editor/ImGui capture 时脚本误收输入。
    /// </summary>
    [Fact]
    public void BuilderAppliesKeyboardAndMouseRoutesIndependently()
    {
        ScriptInputApi input = new();
        ScriptInputSnapshotBuilder.Update(
            input,
            [Key.A],
            [MouseButton.Left],
            mouseX: 10,
            mouseY: 20,
            wheelY: 1);

        Assert.True(input.IsDown(Key.A));
        Assert.True(input.WasPressed(Key.A));
        Assert.True(input.IsMouseDown(MouseButton.Left));
        Assert.True(input.WasMousePressed(MouseButton.Left));
        Assert.Equal(1f, input.MouseWheelY);

        ScriptInputSnapshotBuilder.Update(
            input,
            [Key.A],
            [MouseButton.Left],
            mouseX: 11,
            mouseY: 21,
            wheelY: 2,
            allowKeyboard: false,
            allowMouse: true);

        Assert.False(input.IsDown(Key.A));
        Assert.True(input.WasReleased(Key.A));
        Assert.True(input.IsMouseDown(MouseButton.Left));
        Assert.False(input.WasMousePressed(MouseButton.Left));
        Assert.Equal(2f, input.MouseWheelY);

        ScriptInputSnapshotBuilder.Update(
            input,
            [Key.D],
            [MouseButton.Left],
            mouseX: 12,
            mouseY: 22,
            wheelY: 3,
            allowKeyboard: true,
            allowMouse: false);

        Assert.True(input.IsDown(Key.D));
        Assert.True(input.WasPressed(Key.D));
        Assert.False(input.IsMouseDown(MouseButton.Left));
        Assert.True(input.WasMouseReleased(MouseButton.Left));
        Assert.Equal(0f, input.MouseWheelY);
    }
}
