namespace PixelEngine.UI;

/// <summary>
/// ManagedFallbackBackend 使用的根窗口盒模型。
/// </summary>
/// <param name="X">窗口左上角 X；未指定时为 null。</param>
/// <param name="Y">窗口左上角 Y；未指定时为 null。</param>
/// <param name="Width">窗口宽度；未指定时为 null。</param>
/// <param name="Height">窗口高度；未指定时为 null。</param>
internal readonly record struct ManagedUiBox(float? X, float? Y, float? Width, float? Height)
{
    /// <summary>
    /// 是否包含完整的位置与尺寸。
    /// </summary>
    public bool HasPositionAndSize =>
        X.HasValue &&
        Y.HasValue &&
        Width is > 0f &&
        Height is > 0f;
}
