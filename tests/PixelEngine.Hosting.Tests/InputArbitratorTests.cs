using PixelEngine.Gui;
using PixelEngine.UI;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Hosting 输入仲裁器契约测试。
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
    /// 游戏 GUI/ManagedFallback 捕获在 HTML UI 之前截断世界输入。
    /// </summary>
    [Fact]
    public void GuiCaptureComposesBeforeHtmlUiCapture()
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
