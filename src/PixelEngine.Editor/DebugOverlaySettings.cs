namespace PixelEngine.Editor;

/// <summary>
/// 调试叠层运行时设置。
/// </summary>
public sealed class DebugOverlaySettings
{
    /// <summary>
    /// 当前启用的叠层位。
    /// </summary>
    public DebugOverlayFlags Enabled { get; set; }

    /// <summary>
    /// 判断指定叠层是否启用。
    /// </summary>
    public bool IsEnabled(DebugOverlayFlags flag)
    {
        return (Enabled & flag) != 0;
    }

    /// <summary>
    /// 设置指定叠层开关。
    /// </summary>
    public void Set(DebugOverlayFlags flag, bool enabled)
    {
        Enabled = enabled ? Enabled | flag : Enabled & ~flag;
    }
}
