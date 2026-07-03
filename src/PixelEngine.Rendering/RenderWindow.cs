using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace PixelEngine.Rendering;

/// <summary>
/// Silk.NET 窗口、输入上下文与 OpenGL 入口的生命周期封装。
/// </summary>
public sealed class RenderWindow : IDisposable
{
    private readonly IWindow _window;
    private readonly GlDebugMessenger? _debugMessenger;
    private bool _disposed;

    private RenderWindow(
        IWindow window,
        IInputContext input,
        GL gl,
        RenderBackend backend,
        GlCapabilities capabilities,
        GlDebugMessenger? debugMessenger)
    {
        _window = window;
        Input = input;
        Gl = gl;
        Backend = backend;
        Capabilities = capabilities;
        _debugMessenger = debugMessenger;
    }

    /// <summary>
    /// 当前 OpenGL 入口。
    /// </summary>
    public GL Gl { get; }

    /// <summary>
    /// 实际创建成功的渲染后端。
    /// </summary>
    public RenderBackend Backend { get; }

    /// <summary>
    /// OpenGL 能力快照。
    /// </summary>
    public GlCapabilities Capabilities { get; }

    /// <summary>
    /// 输入上下文。
    /// </summary>
    public IInputContext Input { get; }

    /// <summary>
    /// 窗口是否正在关闭。
    /// </summary>
    public bool IsClosing => _disposed || _window.IsClosing;

    /// <summary>
    /// 当前 OpenGL 默认 framebuffer 宽度。渲染 viewport、FBO 链与脚本相机必须使用该尺寸。
    /// </summary>
    public int Width => Math.Max(1, _window.FramebufferSize.X);

    /// <summary>
    /// 当前 OpenGL 默认 framebuffer 高度。渲染 viewport、FBO 链与脚本相机必须使用该尺寸。
    /// </summary>
    public int Height => Math.Max(1, _window.FramebufferSize.Y);

    /// <summary>
    /// 当前平台窗口逻辑宽度。Silk.NET 鼠标坐标使用该坐标系。
    /// </summary>
    public int LogicalWidth => Math.Max(1, _window.Size.X);

    /// <summary>
    /// 当前平台窗口逻辑高度。Silk.NET 鼠标坐标使用该坐标系。
    /// </summary>
    public int LogicalHeight => Math.Max(1, _window.Size.Y);

    /// <summary>
    /// 逻辑窗口坐标到 framebuffer 坐标的 X 轴缩放。
    /// </summary>
    public float FramebufferScaleX => Width / (float)LogicalWidth;

    /// <summary>
    /// 逻辑窗口坐标到 framebuffer 坐标的 Y 轴缩放。
    /// </summary>
    public float FramebufferScaleY => Height / (float)LogicalHeight;

    /// <summary>
    /// 当前 VSync 状态；开启时 <see cref="SwapBuffers" /> 可能阻塞到显示刷新。
    /// </summary>
    public bool VSyncEnabled
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _window.VSync;
        }

        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _window.VSync = value;
        }
    }

    /// <summary>
    /// 更新窗口标题，用于 Demo/Editor 暴露实时运行状态。
    /// </summary>
    /// <param name="title">新的窗口标题。</param>
    public void SetTitle(string title)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _window.Title = string.IsNullOrWhiteSpace(title)
            ? "PixelEngine"
            : title;
    }

    /// <summary>
    /// 创建并初始化渲染窗口。Auto 模式下桌面 GL 失败会继续尝试 ES3/ANGLE。
    /// </summary>
    /// <param name="options">窗口参数。</param>
    /// <param name="diagnostics">诊断回调，接收后端失败原因。</param>
    /// <returns>已初始化窗口。</returns>
    public static RenderWindow Create(RenderWindowOptions options, Action<string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        Exception? lastError = null;

        foreach (RenderBackend backend in RenderBackendSelector.GetAttemptOrder(options.BackendPreference))
        {
            try
            {
                return CreateForBackend(options, backend, diagnostics);
            }
            catch (Exception ex) when (options.BackendPreference == RenderBackendPreference.Auto)
            {
                diagnostics?.Invoke($"{backend} 创建失败: {ex.Message}");
                lastError = ex;
            }
        }

        throw new InvalidOperationException("无法创建任何可用渲染后端。", lastError);
    }

    /// <summary>
    /// 手动 pump 一次窗口事件。
    /// </summary>
    public void DoEvents()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _window.DoEvents();
    }

    /// <summary>
    /// 交换前后缓冲。
    /// </summary>
    public void SwapBuffers()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _window.SwapBuffers();
    }

    /// <summary>
    /// 调整窗口尺寸。调用者应随后调用 <see cref="RenderPipeline.Resize"/> 重建渲染目标链。
    /// </summary>
    /// <param name="width">新宽度。</param>
    /// <param name="height">新高度。</param>
    public void Resize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "窗口尺寸必须为正数。");
        }

        _window.Size = new Vector2D<int>(width, height);
    }

    /// <summary>
    /// 请求关闭窗口。
    /// </summary>
    public void Close()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _window.Close();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _debugMessenger?.Dispose();
        Gl.Dispose();
        Input.Dispose();
        _window.Dispose();
    }

    private static RenderWindow CreateForBackend(
        RenderWindowOptions options,
        RenderBackend backend,
        Action<string>? diagnostics)
    {
        WindowOptions windowOptions = RenderBackendSelector.CreateWindowOptions(options, backend);
        IWindow window = Window.Create(windowOptions);
        IInputContext? input = null;
        GL? gl = null;
        GlDebugMessenger? debugMessenger = null;
        try
        {
            window.Initialize();
            input = window.CreateInput();
            gl = GL.GetApi(window);
            GlCapabilities capabilities = GlCapabilities.Query(gl);
            ValidateCapabilitiesForBackend(backend, capabilities);
            debugMessenger = options.EnableDebugContext && diagnostics is not null
                ? GlDebugMessenger.TryCreate(gl, capabilities, diagnostics)
                : null;
            return new RenderWindow(window, input, gl, backend, capabilities, debugMessenger);
        }
        catch
        {
            debugMessenger?.Dispose();
            gl?.Dispose();
            input?.Dispose();
            window.Dispose();
            throw;
        }
    }

    private static void ValidateCapabilitiesForBackend(RenderBackend backend, GlCapabilities capabilities)
    {
        switch (backend)
        {
            case RenderBackend.DesktopGl33:
                if (capabilities.IsGles || !IsAtLeast(capabilities, 3, 3))
                {
                    throw new InvalidOperationException(
                        $"请求桌面 OpenGL 3.3，但实际上下文为 {capabilities.Version} / {capabilities.Renderer}。");
                }

                break;
            case RenderBackend.GlEs30Angle:
                if (!capabilities.IsGles || !IsAtLeast(capabilities, 3, 0))
                {
                    throw new InvalidOperationException(
                        $"请求 OpenGL ES 3.0/ANGLE，但实际上下文为 {capabilities.Version} / {capabilities.Renderer}。");
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(backend), backend, "未知渲染后端。");
        }
    }

    private static bool IsAtLeast(GlCapabilities capabilities, int major, int minor)
    {
        return capabilities.MajorVersion > major ||
            (capabilities.MajorVersion == major && capabilities.MinorVersion >= minor);
    }
}
