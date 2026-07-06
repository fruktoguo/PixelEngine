using PixelEngine.Gui;
using PixelEngine.UI;

namespace PixelEngine.Hosting;

/// <summary>
/// Hosting 侧输入仲裁器，按 Editor ImGui、游戏 GUI/HTML UI、世界脚本的优先级合并输入捕获结果。
/// </summary>
public static class InputArbitrator
{
    /// <summary>
    /// 应用 Editor 壳输入捕获；Play 模式下编辑器工具让位时应传入 <see cref="EditorHostInputCapture.None" />。
    /// </summary>
    public static InputArbitrationState ApplyEditor(
        in InputArbitrationState state,
        in EditorHostInputCapture capture)
    {
        return state.Apply(capture.AllowGameKeyboard, capture.AllowGameMouse);
    }

    /// <summary>
    /// 应用游戏 GUI/ManagedFallback 输入捕获。
    /// </summary>
    public static InputArbitrationState ApplyGui(
        in InputArbitrationState state,
        in GuiInputSnapshot capture)
    {
        return state.Apply(capture.AllowWorldKeyboard, capture.AllowWorldMouse);
    }

    /// <summary>
    /// 应用 HTML 游戏 UI 输入捕获。
    /// </summary>
    public static InputArbitrationState ApplyGameUi(
        in InputArbitrationState state,
        in UiInputCapture capture)
    {
        return state.Apply(capture.AllowWorldKeyboard, capture.AllowWorldMouse);
    }
}

/// <summary>
/// 当前帧输入仲裁状态；为脚本输入通道保留键盘和鼠标是否允许进入世界层的单一真相。
/// </summary>
/// <param name="AllowWorldKeyboard">世界/脚本是否可消费键盘输入。</param>
/// <param name="AllowWorldMouse">世界/脚本是否可消费鼠标输入。</param>
public readonly record struct InputArbitrationState(bool AllowWorldKeyboard, bool AllowWorldMouse)
{
    /// <summary>
    /// 初始状态：键盘和鼠标均允许进入世界层。
    /// </summary>
    public static InputArbitrationState Allowed { get; } = new(true, true);

    /// <summary>
    /// 合并一层输入捕获后的世界输入许可。
    /// </summary>
    public InputArbitrationState Apply(bool allowKeyboard, bool allowMouse)
    {
        return new(
            AllowWorldKeyboard && allowKeyboard,
            AllowWorldMouse && allowMouse);
    }

    /// <summary>
    /// 转换为 Silk 输入相位可消费的脚本通道路由。
    /// </summary>
    public ScriptInputRoute ToScriptInputRoute()
    {
        return new(AllowWorldKeyboard, AllowWorldMouse);
    }
}
