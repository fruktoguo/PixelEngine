using PixelEngine.Scripting;

namespace PixelEngine.Hosting;

/// <summary>
/// 将窗口输入采样结果写入脚本输入 API，并按通道应用 Editor/ImGui 门控。
/// </summary>
public static class ScriptInputSnapshotBuilder
{
    /// <summary>
    /// 更新脚本输入快照。
    /// </summary>
    public static void Update(
        ScriptInputApi input,
        ReadOnlySpan<Key> downKeys,
        ReadOnlySpan<MouseButton> downButtons,
        float mouseX,
        float mouseY,
        float wheelY,
        bool allowKeyboard = true,
        bool allowMouse = true)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Update(
            allowKeyboard ? downKeys : [],
            allowMouse ? downButtons : [],
            mouseX,
            mouseY,
            allowMouse ? wheelY : 0f);
    }
}
