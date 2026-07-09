namespace PixelEngine.UI;

/// <summary>
/// ManagedFallbackBackend 支持的受控 XHTML/CSS 子集样式。
/// </summary>
internal readonly record struct ManagedUiStyle(
    float? X,
    float? Y,
    float? Width,
    float? Height,
    float? MarginTop)
{
    /// <summary>
    /// 空样式。
    /// </summary>
    public static ManagedUiStyle Empty { get; } = new(null, null, null, null, null);

    /// <summary>
    /// 是否指定了控件局部位置。
    /// </summary>
    public bool HasPosition => X.HasValue && Y.HasValue;

    /// <summary>
    /// 是否指定了控件尺寸。
    /// </summary>
    public bool HasSize => Width is > 0f && Height is > 0f;

    /// <summary>
    /// 用覆盖样式合并当前样式。
    /// </summary>
    /// <param name="overrides">覆盖样式。</param>
    /// <returns>合并后的样式。</returns>
    public ManagedUiStyle Merge(in ManagedUiStyle overrides)
    {
        return new ManagedUiStyle(
            overrides.X ?? X,
            overrides.Y ?? Y,
            overrides.Width ?? Width,
            overrides.Height ?? Height,
            overrides.MarginTop ?? MarginTop);
    }
}
