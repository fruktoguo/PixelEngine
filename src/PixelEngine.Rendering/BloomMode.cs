namespace PixelEngine.Rendering;

/// <summary>
/// Bloom 模糊模式。
/// </summary>
public enum BloomMode
{
    /// <summary>
    /// 默认 dual-Kawase bloom，少 tap、多 mip，质量与性能平衡较好。
    /// </summary>
    DualKawase,

    /// <summary>
    /// separable Gaussian blur 回退路径。
    /// </summary>
    Gaussian,
}
