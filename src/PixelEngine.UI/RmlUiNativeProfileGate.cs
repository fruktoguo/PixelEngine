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
        ArgumentNullException.ThrowIfNull(capabilities);
        if (backend == RenderBackend.GlEs30Angle || capabilities.IsGles || capabilities.IsAngle)
        {
            fallbackReason =
                $"RmlUi 当前 native shim 只包含 desktop GL3 renderer（RmlUi_Renderer_GL3），请求/上下文为 {DescribeContext(backend, capabilities)}；" +
                "仓库尚无真实 GLES3/ANGLE renderer、loader 与同 context 函数表验证，回退 ManagedFallback，避免误用 GL3 renderer。";
            return false;
        }

        if (!IsAtLeast(capabilities, 3, 3))
        {
            fallbackReason =
                $"RmlUi desktop GL3 renderer 需要桌面 OpenGL 3.3+，当前上下文为 {DescribeContext(backend, capabilities)}；回退 ManagedFallback。";
            return false;
        }

        fallbackReason = null;
        return true;
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
}
