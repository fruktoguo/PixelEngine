namespace PixelEngine.Editor.Shell;

/// <summary>
/// Scene 工具键与 Editor 全局命令之间的输入仲裁策略。
/// </summary>
internal static class SceneToolShortcutPolicy
{
    /// <summary>
    /// 仅允许非文本输入状态下的无 modifier 裸工具键，避免 Ctrl+B 等命令同时切换 Scene 工具。
    /// </summary>
    internal static bool IsAllowed(
        bool wantTextInput,
        bool keyCtrl,
        bool keyShift,
        bool keyAlt,
        bool keySuper)
    {
        return !wantTextInput && !keyCtrl && !keyShift && !keyAlt && !keySuper;
    }
}
