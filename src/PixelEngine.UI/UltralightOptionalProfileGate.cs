namespace PixelEngine.UI;

/// <summary>
/// Ultralight 可选高保真 UI profile 的激活门禁。
/// </summary>
public static class UltralightOptionalProfileGate
{
    /// <summary>
    /// 当前仓库没有真实 Ultralight native SDK、许可与发行证据，因此 profile 默认未激活。
    /// </summary>
    public const bool IsActive = false;

    /// <summary>
    /// 未激活 profile 的安全回退后端。
    /// </summary>
    public const UiBackendKind FallbackBackend = UiBackendKind.ManagedFallback;

    /// <summary>
    /// 给 Editor / Runtime 设置界面展示的非激活 profile 标签。
    /// </summary>
    public const string InactiveDisplayLabel = "Ultralight (inactive optional profile → ManagedFallback)";

    /// <summary>
    /// 运行时、Editor 与发行审计共享的可见诊断。
    /// </summary>
    public const string InactiveReason = "Ultralight optional profile inactive：缺少 native SDK/provenance、commercial redistribution license、runtime surface/JS bridge、RID native binaries、SHA256/NOTICE、codesign/notarize 与 release artifact evidence；显式请求时必须回退 ManagedFallback。";

    /// <summary>
    /// 返回 UI 后端在产品设置界面中的显示标签。
    /// </summary>
    public static string GetDisplayLabel(UiBackendKind backend)
    {
        return backend == UiBackendKind.Ultralight ? InactiveDisplayLabel : backend.ToString();
    }

    /// <summary>
    /// 返回未激活可选 profile 的回退诊断；已激活或非可选 profile 返回空字符串。
    /// </summary>
    public static string GetInactiveReason(UiBackendKind backend)
    {
        return backend == UiBackendKind.Ultralight ? InactiveReason : string.Empty;
    }
}
