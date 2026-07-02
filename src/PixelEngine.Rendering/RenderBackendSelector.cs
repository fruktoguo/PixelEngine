using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace PixelEngine.Rendering;

/// <summary>
/// 将引擎渲染后端选项转换为 Silk.NET 窗口参数。
/// </summary>
public static class RenderBackendSelector
{
    /// <summary>
    /// 生成给定后端的 Silk.NET 窗口参数。
    /// </summary>
    /// <param name="options">引擎窗口参数。</param>
    /// <param name="backend">目标渲染后端。</param>
    /// <returns>Silk.NET 窗口参数。</returns>
    public static WindowOptions CreateWindowOptions(RenderWindowOptions options, RenderBackend backend)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Width <= 0 || options.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "窗口尺寸必须为正数。");
        }

        ValidateRate(options.FramesPerSecond, nameof(options.FramesPerSecond));
        ValidateRate(options.UpdatesPerSecond, nameof(options.UpdatesPerSecond));

        WindowOptions windowOptions = WindowOptions.Default;
        windowOptions.Title = options.Title;
        windowOptions.Size = new Vector2D<int>(options.Width, options.Height);
        windowOptions.API = CreateGraphicsApi(backend, options.EnableDebugContext);
        windowOptions.VSync = options.VSync;
        windowOptions.FramesPerSecond = options.FramesPerSecond;
        windowOptions.UpdatesPerSecond = options.UpdatesPerSecond;
        windowOptions.ShouldSwapAutomatically = false;
        return windowOptions;
    }

    /// <summary>
    /// 按偏好顺序列出需要尝试的后端。
    /// </summary>
    /// <param name="preference">后端选择偏好。</param>
    /// <returns>按尝试顺序排列的后端。</returns>
    public static ReadOnlySpan<RenderBackend> GetAttemptOrder(RenderBackendPreference preference)
    {
        return preference switch
        {
            RenderBackendPreference.Auto => [RenderBackend.DesktopGl33, RenderBackend.GlEs30Angle],
            RenderBackendPreference.DesktopGl33 => [RenderBackend.DesktopGl33],
            RenderBackendPreference.GlEs30Angle => [RenderBackend.GlEs30Angle],
            _ => throw new ArgumentOutOfRangeException(nameof(preference)),
        };
    }

    private static GraphicsAPI CreateGraphicsApi(RenderBackend backend, bool debug)
    {
        ContextFlags flags = debug ? ContextFlags.Debug : ContextFlags.Default;
        return backend switch
        {
            RenderBackend.DesktopGl33 => new GraphicsAPI(
                ContextAPI.OpenGL,
                ContextProfile.Core,
                flags,
                new APIVersion(3, 3)),
            RenderBackend.GlEs30Angle => new GraphicsAPI(
                ContextAPI.OpenGLES,
                ContextProfile.Core,
                flags,
                new APIVersion(3, 0)),
            _ => throw new ArgumentOutOfRangeException(nameof(backend)),
        };
    }

    private static void ValidateRate(double rate, string parameterName)
    {
        if (!double.IsFinite(rate) || rate < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, rate, "窗口频率必须是非负有限数；0 表示不节流。");
        }
    }
}
