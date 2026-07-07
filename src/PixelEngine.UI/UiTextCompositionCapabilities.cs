namespace PixelEngine.UI;

/// <summary>
/// 描述 UI 输入源是否提供真实平台 IME composition 事件，以及不可用时的诊断文本。
/// </summary>
/// <param name="SupportsPlatformComposition">输入源是否能提供真实平台 composition start/update/cancel 事件。</param>
/// <param name="Diagnostic">面向运行时、Editor Console 与测试的能力诊断。</param>
public readonly record struct UiTextCompositionCapabilities(bool SupportsPlatformComposition, string Diagnostic)
{
    /// <summary>
    /// 创建支持真实平台 IME composition 的能力描述。
    /// </summary>
    /// <param name="diagnostic">能力诊断文本。</param>
    /// <returns>支持 composition 的能力描述。</returns>
    public static UiTextCompositionCapabilities Supported(string diagnostic = "平台输入源提供真实 IME composition 事件。")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnostic);
        return new UiTextCompositionCapabilities(true, diagnostic);
    }

    /// <summary>
    /// 创建不支持真实平台 IME composition 的能力描述。
    /// </summary>
    /// <param name="diagnostic">不可用原因诊断。</param>
    /// <returns>不支持 composition 的能力描述。</returns>
    public static UiTextCompositionCapabilities Unsupported(string diagnostic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnostic);
        return new UiTextCompositionCapabilities(false, diagnostic);
    }

    /// <summary>
    /// 校验诊断文本非空，避免静默丢失 IME 阻塞原因。
    /// </summary>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Diagnostic);
    }
}
