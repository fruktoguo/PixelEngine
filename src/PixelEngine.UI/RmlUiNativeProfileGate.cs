using PixelEngine.Rendering;

namespace PixelEngine.UI;

/// <summary>
/// RmlUi native renderer profile gate：当前只允许桌面 OpenGL GL3 renderer，不把 GLES/ANGLE 冒充为 GL3。
/// </summary>
public static class RmlUiNativeProfileGate
{
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
        fallbackReason = decision.FallbackReason;
        return decision.CanUseNativeRenderer &&
            decision.RequestedProfile == RmlUiNativeRendererProfile.DesktopGl3;
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
        return RequiresGlesAngleProfile(backend, capabilities)
            ? CreateGlesAngleBlockedDecision(backend, capabilities)
            : !IsAtLeast(capabilities, 3, 3)
                ? CreateDesktopBlockedDecision(backend, capabilities)
                : CreateDesktopAllowedDecision();
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

    private static RmlUiNativeProfileDecision CreateGlesAngleBlockedDecision(RenderBackend backend, GlCapabilities capabilities)
    {
        return new RmlUiNativeProfileDecision(
            RmlUiNativeRendererProfile.Gles3Angle,
            CanUseNativeRenderer: false,
            NativeRendererSymbol: "RmlUi_Renderer_GLES3_ANGLE",
            ShaderVersionDirective: "#version 300 es",
            RequiresSameContextFunctionResolver: true,
            FallbackReason:
                $"RmlUi 当前 native shim 只包含 desktop GL3 renderer（RmlUi_Renderer_GL3），请求/上下文为 {DescribeContext(backend, capabilities)}；" +
                "GLES3/ANGLE renderer profile 需要独立 RmlUi_Renderer_GLES3_ANGLE、loader、#version 300 es shader、同 context 函数表验证和状态恢复 smoke，回退 ManagedFallback，避免误用 GL3 renderer。");
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
    /// OpenGL ES 3.0 / ANGLE renderer；当前仓库尚未实现该 native profile。
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
