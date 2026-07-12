using Silk.NET.Input;
using Silk.NET.Core.Contexts;
using Silk.NET.GLFW;
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
    private readonly INativeContext _nativeContext;
    private readonly GlDebugMessenger? _debugMessenger;
    private readonly WindowsDxgiGlPresenter? _dxgiPresenter;
    private bool _disposed;

    private RenderWindow(
        IWindow window,
        IInputContext input,
        GL gl,
        INativeContext nativeContext,
        RenderBackend backend,
        GlCapabilities capabilities,
        GlDebugMessenger? debugMessenger,
        WindowsDxgiGlPresenter? dxgiPresenter)
    {
        _window = window;
        Input = input;
        Gl = gl;
        _nativeContext = nativeContext;
        Backend = backend;
        Capabilities = capabilities;
        _debugMessenger = debugMessenger;
        _dxgiPresenter = dxgiPresenter;
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
    /// 平台窗口焦点变化。GUI 平台桥用它向 ImGui 注入 focus event，避免 Alt-Tab 后残留按键或文本焦点。
    /// </summary>
    public event Action<bool> FocusChanged
    {
        add
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _window.FocusChanged += value;
        }

        remove
        {
            if (!_disposed)
            {
                _window.FocusChanged -= value;
            }
        }
    }

    /// <summary>
    /// 用户从操作系统拖入窗口的文件或目录路径。事件只转发平台回调，不在窗口层执行磁盘 I/O。
    /// </summary>
    public event Action<string[]> FilesDropped
    {
        add
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _window.FileDrop += value;
        }

        remove
        {
            if (!_disposed)
            {
                _window.FileDrop -= value;
            }
        }
    }

    /// <summary>
    /// 当前窗口的 presentation framebuffer；普通窗口为 0，DXGI interop 路径为共享 backbuffer FBO。
    /// </summary>
    public uint PresentationFramebuffer => _dxgiPresenter?.Framebuffer ?? 0;

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

        ReadOnlySpan<RenderBackend> attemptOrder = RenderBackendSelector.GetAttemptOrder(options.BackendPreference);
        for (int i = 0; i < attemptOrder.Length; i++)
        {
            RenderBackend backend = attemptOrder[i];
            try
            {
                return CreateForBackend(options, backend, diagnostics);
            }
            catch (Exception ex) when (i + 1 < attemptOrder.Length)
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
        _dxgiPresenter?.PrepareFrame(Width, Height);
    }

    /// <summary>
    /// 交换前后缓冲。
    /// </summary>
    public void SwapBuffers()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_dxgiPresenter is not null)
        {
            _dxgiPresenter.Present(VSyncEnabled);
            return;
        }

        _window.SwapBuffers();
    }

    /// <summary>
    /// 绑定当前窗口实际 presentation framebuffer，供 present、overlay、UI 与截图共享同一输出目标。
    /// </summary>
    public void BindPresentationFramebuffer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Gl.BindFramebuffer(FramebufferTarget.Framebuffer, PresentationFramebuffer);
    }

    /// <summary>
    /// 从当前窗口 OpenGL context 查询函数入口。用于 native UI shim 复用同一 loader，不自行打开系统 GL。
    /// </summary>
    /// <param name="name">OpenGL 函数名。</param>
    /// <param name="address">函数入口地址。</param>
    /// <returns>若当前 context 可解析该函数返回 true。</returns>
    public bool TryGetProcAddress(string name, out IntPtr address)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _nativeContext.TryGetProcAddress(name, out address);
    }

    /// <summary>
    /// 尝试取得当前窗口的 Win32 HWND；非 Windows 后端或未暴露 Win32 句柄时返回 false。
    /// </summary>
    /// <param name="hwnd">Win32 窗口句柄。</param>
    /// <returns>当前窗口有有效 HWND 时返回 true。</returns>
    public bool TryGetWin32WindowHandle(out IntPtr hwnd)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        (IntPtr Hwnd, IntPtr Hdc, IntPtr HInstance)? win32 = _window.Native?.Win32;
        if (win32 is { } handles && handles.Hwnd != IntPtr.Zero)
        {
            hwnd = handles.Hwnd;
            return true;
        }

        hwnd = IntPtr.Zero;
        return false;
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
        _dxgiPresenter?.Dispose();
        Gl.Dispose();
        Input.Dispose();
        _window.Dispose();
    }

    private static RenderWindow CreateForBackend(
        RenderWindowOptions options,
        RenderBackend backend,
        Action<string>? diagnostics)
    {
        // --- 窗口创建：DPI 感知 → Silk.NET 窗口 → 输入上下文 ---
        WindowsDpiAwareness.EnsureEnabled();
        GlfwProvider.GLFW.Value.WindowHint(
            WindowHintContextApi.ContextCreationApi,
            backend == RenderBackend.GlEs30Angle
                ? ContextApi.EglContextApi
                : ContextApi.NativeContextApi);
        WindowOptions windowOptions = RenderBackendSelector.CreateWindowOptions(options, backend);
        IWindow window = Window.Create(windowOptions);
        IInputContext? input = null;
        GL? gl = null;
        GlDebugMessenger? debugMessenger = null;
        WindowsDxgiGlPresenter? dxgiPresenter = null;
        try
        {
            window.Initialize();
            input = window.CreateInput();
            // --- GL 上下文：查询能力快照并按后端校验最低版本 ---
            gl = GL.GetApi(window);
            INativeContext nativeContext = gl.Context;
            GlCapabilities capabilities = GlCapabilities.Query(gl);
            ValidateCapabilitiesForBackend(backend, capabilities);
            debugMessenger = options.EnableDebugContext && diagnostics is not null
                ? GlDebugMessenger.TryCreate(gl, capabilities, diagnostics)
                : null;
            if (options.UseDarkWindowChrome && window.Native?.Win32 is { } chromeHandles)
            {
                WindowsWindowChrome.TryApply(
                    chromeHandles.Hwnd,
                    options.TitleBarColorRgb,
                    options.TitleBarTextColorRgb,
                    options.WindowBorderColorRgb);
            }

            if (backend == RenderBackend.DesktopGl33DxgiInterop)
            {
                if (window.Native?.Win32 is not { } dxgiHandles)
                {
                    throw new InvalidOperationException("DXGI interop 后端未取得 Win32 HWND。");
                }

                dxgiPresenter = WindowsDxgiGlPresenter.Create(
                    gl,
                    nativeContext,
                    dxgiHandles.Hwnd,
                    Math.Max(1, window.FramebufferSize.X),
                    Math.Max(1, window.FramebufferSize.Y));
            }

            return new RenderWindow(window, input, gl, nativeContext, backend, capabilities, debugMessenger, dxgiPresenter);
        }
        catch
        {
            dxgiPresenter?.Dispose();
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
            case RenderBackend.DesktopGl33DxgiInterop:
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

                if (OperatingSystem.IsWindows() && !capabilities.IsAngle)
                {
                    throw new InvalidOperationException(
                        $"Windows 上请求 ANGLE，但实际 GLES provider 不是 ANGLE：{capabilities.Version} / {capabilities.Renderer}。");
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
