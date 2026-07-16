using PixelEngine.Gui;
using PixelEngine.UI;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Hosting 输入仲裁器契约测试。
/// 不变式：编辑器与游戏输入互斥、焦点切换时快照一致。
/// </summary>
public sealed class InputArbitratorTests
{
    /// <summary>
    /// Editor 捕获优先于游戏 UI 和世界输入。
    /// </summary>
    [Fact]
    public void EditorCaptureBlocksWorldBeforeGameUi()
    {
        InputArbitrationState state = InputArbitrator.ApplyEditor(
            InputArbitrationState.Allowed,
            new EditorHostInputCapture(WantCaptureMouse: true, WantCaptureKeyboard: false));

        Assert.False(state.AllowWorldMouse);
        Assert.True(state.AllowWorldKeyboard);

        state = InputArbitrator.ApplyGameUi(state, UiInputCapture.None);

        Assert.False(state.AllowWorldMouse);
        Assert.True(state.AllowWorldKeyboard);
        Assert.Equal(new ScriptInputRoute(AllowKeyboard: true, AllowMouse: false), state.ToScriptInputRoute());
    }

    /// <summary>
    /// shared/runtime Gui 与 Game UI 捕获按任意应用顺序都只能收缩世界输入许可。
    /// </summary>
    [Fact]
    public void GuiAndGameUiCaptureComposeMonotonically()
    {
        InputArbitrationState state = InputArbitrator.ApplyGui(
            InputArbitrationState.Allowed,
            new GuiInputSnapshot(WantCaptureMouse: false, WantCaptureKeyboard: true));

        state = InputArbitrator.ApplyGameUi(
            state,
            new UiInputCapture(HitsUi: true, Opaque: true, WantCaptureMouse: true, WantCaptureKeyboard: false));

        Assert.False(state.AllowWorldKeyboard);
        Assert.False(state.AllowWorldMouse);
        Assert.Equal(new ScriptInputRoute(AllowKeyboard: false, AllowMouse: false), state.ToScriptInputRoute());
    }

    /// <summary>
    /// Play 模式下编辑器工具让位时，HTML UI 仍可独立捕获输入。
    /// </summary>
    [Fact]
    public void EditorNoneLetsGameUiCaptureInput()
    {
        InputArbitrationState state = InputArbitrator.ApplyEditor(
            InputArbitrationState.Allowed,
            EditorHostInputCapture.None);

        Assert.True(state.AllowWorldKeyboard);
        Assert.True(state.AllowWorldMouse);

        state = InputArbitrator.ApplyGameUi(
            state,
            new UiInputCapture(HitsUi: true, Opaque: true, WantCaptureMouse: true, WantCaptureKeyboard: true));

        Assert.False(state.AllowWorldKeyboard);
        Assert.False(state.AllowWorldMouse);
    }
}
