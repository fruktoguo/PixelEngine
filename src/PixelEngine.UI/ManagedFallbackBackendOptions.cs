namespace PixelEngine.UI;

/// <summary>
/// ManagedFallbackBackend 容量配置。
/// </summary>
/// <param name="MaxDocuments">可载入文档数。</param>
/// <param name="MaxControlsPerDocument">单文档最大控件数。</param>
/// <param name="MaxVisibleScreens">可见屏栈最大深度。</param>
/// <param name="EventCapacity">事件环形缓冲容量。</param>
public readonly record struct ManagedFallbackBackendOptions(
    int MaxDocuments,
    int MaxControlsPerDocument,
    int MaxVisibleScreens,
    int EventCapacity)
{
    /// <summary>
    /// 默认配置。
    /// </summary>
    public static readonly ManagedFallbackBackendOptions Default = new(64, 256, 32, 128);

    /// <summary>
    /// 规范化并校验配置。
    /// </summary>
    /// <returns>规范化后的配置。</returns>
    public ManagedFallbackBackendOptions Normalize()
    {
        ManagedFallbackBackendOptions value = this == default ? Default : this;
        return value.MaxDocuments <= 0
            ? throw new ArgumentOutOfRangeException(nameof(MaxDocuments))
            : value.MaxControlsPerDocument <= 0
                ? throw new ArgumentOutOfRangeException(nameof(MaxControlsPerDocument))
                : value.MaxVisibleScreens <= 0
                    ? throw new ArgumentOutOfRangeException(nameof(MaxVisibleScreens))
                    : value.EventCapacity <= 0
                        ? throw new ArgumentOutOfRangeException(nameof(EventCapacity))
                        : value;
    }
}
