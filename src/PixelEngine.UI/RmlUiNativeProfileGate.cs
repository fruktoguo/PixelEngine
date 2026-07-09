using PixelEngine.Rendering;

namespace PixelEngine.UI;

/// <summary>
/// RmlUi native renderer profile gate：在 desktop GL3 与 GLES3/ANGLE 双 profile 间选择，禁止用错误 shader 冒充另一 profile。
/// </summary>
public static class RmlUiNativeProfileGate
{
    /// <summary>
    /// Desktop GL3 profile 对应的 native 整型值（与 peui_native_set_renderer_profile 一致）。
    /// </summary>
    public const int NativeProfileDesktopGl3 = 0;

    /// <summary>
    /// GLES3/ANGLE profile 对应的 native 整型值。
    /// </summary>
    public const int NativeProfileGles3Angle = 1;

    /// <summary>
    /// 判断当前 GL 上下文是否允许加载 RmlUi desktop GL3 renderer。
    /// </summary>
    /// <param name="capabilities">当前窗口的 GL 能力快照。</param>
    /// <param name="fallbackReason">不允许时写入可见降级原因。</param>
    /// <returns>允许使用 RmlUi desktop GL3 renderer 时返回 true。</returns>
    public static bool CanUseDesktopGl3(GlCapabilities capabilities, out string? fallbackReason)
    {
        return CanUseDesktopGl3(RenderBackend.DesktopGl33, capabilities, out fallbackReason);
    }

    /// <summary>
    /// 判断指定渲染后端与 GL 上下文是否允许加载 RmlUi desktop GL3 renderer。
    /// </summary>
    /// <param name="backend">窗口创建时选中的渲染后端。</param>
    /// <param name="capabilities">当前窗口的 GL 能力快照。</param>
    /// <param name="fallbackReason">不允许时写入可见降级原因。</param>
    /// <returns>允许使用 RmlUi desktop GL3 renderer 时返回 true。</returns>
    public static bool CanUseDesktopGl3(RenderBackend backend, GlCapabilities capabilities, out string? fallbackReason)
    {
        RmlUiNativeProfileDecision decision = Evaluate(backend, capabilities);
        if (decision.RequestedProfile != RmlUiNativeRendererProfile.DesktopGl3)
        {
            fallbackReason = decision.FallbackReason ??
                "当前上下文需要 GLES3/ANGLE native profile（#version 300 es），不能使用 desktop GL3 renderer。";
            return false;
        }

        fallbackReason = decision.FallbackReason;
        return decision.CanUseNativeRenderer;
    }

    /// <summary>
    /// 判断当前上下文是否允许加载任一已实现的 RmlUi native renderer profile（desktop GL3 或 GLES3/ANGLE）。
    /// </summary>
    /// <param name="backend">窗口创建时选中的渲染后端。</param>
    /// <param name="capabilities">当前窗口的 GL 能力快照。</param>
    /// <param name="fallbackReason">不允许时写入可见降级原因。</param>
    /// <returns>允许使用 native renderer 时返回 true。</returns>
    public static bool CanUseNativeRenderer(RenderBackend backend, GlCapabilities capabilities, out string? fallbackReason)
    {
        RmlUiNativeProfileDecision decision = Evaluate(backend, capabilities);
        fallbackReason = decision.FallbackReason;
        return decision.CanUseNativeRenderer;
    }

    /// <summary>
    /// 评估当前窗口应使用的 RmlUi native renderer profile。
    /// </summary>
    /// <param name="backend">窗口创建时选中的渲染后端。</param>
    /// <param name="capabilities">当前窗口的 GL 能力快照。</param>
    /// <returns>包含 renderer、shader profile 与降级原因的决策。</returns>
    public static RmlUiNativeProfileDecision Evaluate(RenderBackend backend, GlCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        // UI 后端切换门控：GLES/ANGLE 与 desktop GL3 使用不同 shader profile，不满足则回退 ManagedFallback。
        return RequiresGlesAngleProfile(backend, capabilities)
            ? CanUseGlesAngleProfile(capabilities)
                ? CreateGlesAngleAllowedDecision()
                : CreateGlesAngleBlockedDecision(backend, capabilities)
            : IsAtLeast(capabilities, 3, 3)
                ? CreateDesktopAllowedDecision()
                : CreateDesktopBlockedDecision(backend, capabilities);
    }

    /// <summary>
    /// 将 profile 决策映射为 native peui_native_set_renderer_profile 整型。
    /// </summary>
    /// <param name="profile">托管 profile 枚举。</param>
    /// <returns>native profile 整型。</returns>
    public static int ToNativeProfileId(RmlUiNativeRendererProfile profile)
    {
        return profile == RmlUiNativeRendererProfile.Gles3Angle
            ? NativeProfileGles3Angle
            : NativeProfileDesktopGl3;
    }

    private static string DescribeContext(RenderBackend backend, GlCapabilities capabilities)
    {
        string profile = backend == RenderBackend.GlEs30Angle
            ? "GLES/ANGLE request"
            : capabilities.IsGles && capabilities.IsAngle
                ? "GLES/ANGLE"
                : capabilities.IsGles
                    ? "GLES"
                    : capabilities.IsAngle
                        ? "ANGLE"
                        : "desktop GL";
        return $"{profile} {capabilities.Version} / {capabilities.Renderer} / {capabilities.Vendor}";
    }

    private static bool IsAtLeast(GlCapabilities capabilities, int major, int minor)
    {
        return capabilities.MajorVersion > major ||
            (capabilities.MajorVersion == major && capabilities.MinorVersion >= minor);
    }

    private static bool RequiresGlesAngleProfile(RenderBackend backend, GlCapabilities capabilities)
    {
        return backend == RenderBackend.GlEs30Angle || capabilities.IsGles || capabilities.IsAngle;
    }

    private static bool CanUseGlesAngleProfile(GlCapabilities capabilities)
    {
        // GLES/ANGLE path requires ES 3.0+ (or ANGLE reporting compatible 3.0+).
        // Desktop-looking ANGLE strings (e.g. 4.1.0 ANGLE) still route here and typically expose ES 3.
        return IsAtLeast(capabilities, 3, 0);
    }

    private static RmlUiNativeProfileDecision CreateGlesAngleAllowedDecision()
    {
        return new RmlUiNativeProfileDecision(
            RmlUiNativeRendererProfile.Gles3Angle,
            CanUseNativeRenderer: true,
            NativeRendererSymbol: "RmlUi_Renderer_GLES3_ANGLE",
            ShaderVersionDirective: "#version 300 es",
            RequiresSameContextFunctionResolver: true,
            FallbackReason: null);
    }

    private static RmlUiNativeProfileDecision CreateGlesAngleBlockedDecision(RenderBackend backend, GlCapabilities capabilities)
    {
        return new RmlUiNativeProfileDecision(
            RmlUiNativeRendererProfile.Gles3Angle,
            CanUseNativeRenderer: false,
            NativeRendererSymbol: "RmlUi_Renderer_GLES3_ANGLE",
            ShaderVersionDirective: "#version 300 es",
            RequiresSameContextFunctionResolver: true,
            FallbackReason:
                $"RmlUi GLES3/ANGLE renderer profile 需要 OpenGL ES 3.0+ 与同 context 函数表，当前上下文为 {DescribeContext(backend, capabilities)}；" +
                "回退 ManagedFallback，避免误用 desktop GL3 #version 330 shader。");
    }

    private static RmlUiNativeProfileDecision CreateDesktopBlockedDecision(RenderBackend backend, GlCapabilities capabilities)
    {
        return new RmlUiNativeProfileDecision(
            RmlUiNativeRendererProfile.DesktopGl3,
            CanUseNativeRenderer: false,
            NativeRendererSymbol: "RmlUi_Renderer_GL3",
            ShaderVersionDirective: "#version 330 core",
            RequiresSameContextFunctionResolver: true,
            FallbackReason:
                $"RmlUi desktop GL3 renderer 需要桌面 OpenGL 3.3+，当前上下文为 {DescribeContext(backend, capabilities)}；回退 ManagedFallback。");
    }

    private static RmlUiNativeProfileDecision CreateDesktopAllowedDecision()
    {
        return new RmlUiNativeProfileDecision(
            RmlUiNativeRendererProfile.DesktopGl3,
            CanUseNativeRenderer: true,
            NativeRendererSymbol: "RmlUi_Renderer_GL3",
            ShaderVersionDirective: "#version 330 core",
            RequiresSameContextFunctionResolver: true,
            FallbackReason: null);
    }
}

/// <summary>
/// RmlUi native renderer profile 类型。
/// </summary>
public enum RmlUiNativeRendererProfile
{
    /// <summary>
    /// 桌面 OpenGL 3.3 GL3 renderer。
    /// </summary>
    DesktopGl3,

    /// <summary>
    /// OpenGL ES 3.0 / ANGLE renderer（#version 300 es shader profile）。
    /// </summary>
    Gles3Angle,
}

/// <summary>
/// RmlUi native renderer profile 选择结果。
/// </summary>
/// <param name="RequestedProfile">当前上下文需要的 renderer profile。</param>
/// <param name="CanUseNativeRenderer">是否可以使用 native renderer。</param>
/// <param name="NativeRendererSymbol">该 profile 需要的 native renderer symbol。</param>
/// <param name="ShaderVersionDirective">该 profile 需要的 GLSL shader version。</param>
/// <param name="RequiresSameContextFunctionResolver">是否要求从同一 GL context 解析函数表。</param>
/// <param name="FallbackReason">不能使用 native renderer 时的可见降级原因。</param>
public readonly record struct RmlUiNativeProfileDecision(
    RmlUiNativeRendererProfile RequestedProfile,
    bool CanUseNativeRenderer,
    string NativeRendererSymbol,
    string ShaderVersionDirective,
    bool RequiresSameContextFunctionResolver,
    string? FallbackReason);
