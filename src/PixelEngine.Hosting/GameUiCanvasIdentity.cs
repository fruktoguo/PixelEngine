using ScriptUi = PixelEngine.Scripting;

namespace PixelEngine.Hosting;

/// <summary>
/// 将 scene GameObject StableId 映射为脚本可见 opaque Canvas id 的唯一规则。
/// </summary>
public static class GameUiCanvasIdentity
{
    private const ulong ExplicitNamespace = 0x5045_5843_0000_0000UL;

    /// <summary>
    /// 旧场景未声明 WebCanvas 时使用的不落盘 implicit primary id。
    /// </summary>
    public static ScriptUi.UiCanvasId LegacyImplicit { get; } = new(ExplicitNamespace | uint.MaxValue);

    /// <summary>
    /// 由正 GameObject StableId 确定性派生 Canvas id；映射在 Int32 正数域内无碰撞。
    /// </summary>
    /// <param name="stableId">owning GameObject StableId。</param>
    /// <returns>opaque Canvas id。</returns>
    public static ScriptUi.UiCanvasId FromStableId(int stableId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stableId);
        return new ScriptUi.UiCanvasId(ExplicitNamespace | (uint)stableId);
    }
}
