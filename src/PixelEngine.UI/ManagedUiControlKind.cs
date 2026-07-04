namespace PixelEngine.UI;

/// <summary>
/// ManagedFallbackBackend 支持的托管控件类型。
/// </summary>
public enum ManagedUiControlKind : byte
{
    /// <summary>
    /// 静态文本。
    /// </summary>
    Text = 0,

    /// <summary>
    /// 按钮。
    /// </summary>
    Button = 1,

    /// <summary>
    /// 复选框。
    /// </summary>
    Checkbox = 2,

    /// <summary>
    /// 进度条。
    /// </summary>
    Progress = 3,
}
