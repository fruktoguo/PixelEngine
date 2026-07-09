using PixelEngine.UI;

namespace PixelEngine.Hosting;

/// <summary>
/// 游戏大 UI 后端选择结果，记录请求后端、实际后端、显式降级原因与 native profile 诊断。
/// </summary>
/// <param name="RequestedBackend">用户或构建配置请求的后端。</param>
/// <param name="ActiveBackend">实际启用的后端。</param>
/// <param name="FallbackReason">发生降级时的原因；未降级时为 null。</param>
/// <param name="ActiveNativeProfile">实际启用的 native renderer profile 诊断（如 RmlUi desktop/GLES）；无 native 时为 null。</param>
public readonly record struct GameUiBackendSelection(
    UiBackendKind RequestedBackend,
    UiBackendKind ActiveBackend,
    string? FallbackReason,
    string? ActiveNativeProfile = null)
{
    /// <summary>
    /// 是否发生了后端降级。
    /// </summary>
    public bool UsedFallback => RequestedBackend != ActiveBackend;
}
