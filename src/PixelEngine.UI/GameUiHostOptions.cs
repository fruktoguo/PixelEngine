namespace PixelEngine.UI;

/// <summary>
/// 游戏 UI 宿主容量与运行期开关。
/// </summary>
/// <param name="Enabled">是否启用游戏 UI。</param>
/// <param name="MaxDocuments">可载入文档上限。</param>
/// <param name="MaxStackDepth">可见屏栈深度上限。</param>
public readonly record struct GameUiHostOptions(bool Enabled, int MaxDocuments, int MaxStackDepth)
{
    /// <summary>
    /// 默认宿主配置。
    /// </summary>
    public static readonly GameUiHostOptions Default = new(Enabled: true, MaxDocuments: 64, MaxStackDepth: 32);

    /// <summary>
    /// 规范化配置并校验容量。
    /// </summary>
    /// <returns>规范化后的配置。</returns>
    public GameUiHostOptions Normalize()
    {
        GameUiHostOptions value = this == default ? Default : this;
        return value.MaxDocuments <= 0
            ? throw new ArgumentOutOfRangeException(nameof(MaxDocuments), "UI 文档容量必须大于 0。")
            : value.MaxStackDepth <= 0
                ? throw new ArgumentOutOfRangeException(nameof(MaxStackDepth), "UI 屏栈容量必须大于 0。")
                : value;
    }
}
