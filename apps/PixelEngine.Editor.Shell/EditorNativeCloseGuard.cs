namespace PixelEngine.Editor.Shell;

/// <summary>
/// 把操作系统标题栏关闭请求接入 Editor 的统一 dirty transition，而不依赖窗口或 ImGui 实现。
/// </summary>
internal static class EditorNativeCloseGuard
{
    /// <summary>
    /// 判断主循环是否应直接退出；dirty 时撤销原生关闭并请求受保护的 Exit transition。
    /// </summary>
    internal static bool ShouldExit(
        bool nativeCloseRequested,
        bool sceneDirty,
        Action cancelNativeClose,
        Action requestGuardedExit)
    {
        ArgumentNullException.ThrowIfNull(cancelNativeClose);
        ArgumentNullException.ThrowIfNull(requestGuardedExit);
        if (!nativeCloseRequested)
        {
            return false;
        }

        if (!sceneDirty)
        {
            return true;
        }

        cancelNativeClose();
        requestGuardedExit();
        return false;
    }
}
