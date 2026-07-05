using PixelEngine.UI;

namespace PixelEngine.Hosting;

/// <summary>
/// 游戏大 UI 后端选择结果，记录请求后端、实际后端与显式降级原因。
/// </summary>
/// <param name="RequestedBackend">用户或构建配置请求的后端。</param>
/// <param name="ActiveBackend">实际启用的后端。</param>
/// <param name="FallbackReason">发生降级时的原因；未降级时为 null。</param>
public readonly record struct GameUiBackendSelection(
    UiBackendKind RequestedBackend,
    UiBackendKind ActiveBackend,
    string? FallbackReason)
{
    /// <summary>
    /// 是否发生了后端降级。
    /// </summary>
    public bool UsedFallback => RequestedBackend != ActiveBackend;
}
