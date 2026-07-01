namespace PixelEngine.Editor;

/// <summary>
/// Editor 与游戏输入的仲裁结果。
/// </summary>
/// <param name="AllowEditorMouse">编辑工具是否可消费鼠标。</param>
/// <param name="AllowEditorKeyboard">编辑工具是否可消费键盘。</param>
/// <param name="AllowGameMouse">游戏/脚本是否可消费鼠标。</param>
/// <param name="AllowGameKeyboard">游戏/脚本是否可消费键盘。</param>
public readonly record struct EditorInputRoute(
    bool AllowEditorMouse,
    bool AllowEditorKeyboard,
    bool AllowGameMouse,
    bool AllowGameKeyboard);

/// <summary>
/// 按 EditorMode 与 ImGui capture 状态仲裁输入去向。
/// </summary>
public sealed class EditorInputGate
{
    /// <summary>
    /// 计算当前帧输入路由。
    /// </summary>
    /// <param name="session">Editor Play session 状态。</param>
    /// <param name="input">ImGui capture 状态。</param>
    /// <returns>输入路由。</returns>
    public EditorInputRoute Route(EditorPlaySessionSnapshot session, EditorInputSnapshot input)
    {
        bool freeMouse = !input.WantCaptureMouse;
        bool freeKeyboard = !input.WantCaptureKeyboard;
        return session.Mode == EditorMode.Edit
            ? new EditorInputRoute(freeMouse, freeKeyboard, AllowGameMouse: false, AllowGameKeyboard: false)
            : new EditorInputRoute(AllowEditorMouse: false, AllowEditorKeyboard: false, freeMouse, freeKeyboard);
    }
}
