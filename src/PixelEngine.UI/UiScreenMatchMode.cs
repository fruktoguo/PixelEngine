namespace PixelEngine.UI;

/// <summary>
/// <see cref="UiScaleMode.ScaleWithScreenSize" /> 下宽高比例的合并方式。
/// </summary>
public enum UiScreenMatchMode : byte
{
    /// <summary>
    /// 在宽、高缩放的对数空间按 Match 值插值。
    /// </summary>
    MatchWidthOrHeight = 0,

    /// <summary>
    /// 选择较小缩放，使逻辑 Canvas 至少覆盖参考分辨率。
    /// </summary>
    Expand = 1,

    /// <summary>
    /// 选择较大缩放，使逻辑 Canvas 不超出参考分辨率。
    /// </summary>
    Shrink = 2,
}
