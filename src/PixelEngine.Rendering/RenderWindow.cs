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
        WindowsDxgiGlPresenter? dxgiPresenter,
        int initialWidth,
        int initialHeight,
        PlayerWindowMode initialWindowMode)
    {
        _window = window;
        Input = input;
        Gl = gl;
        _nativeContext = nativeContext;
        Backend = backend;
        Capabilities = capabilities;
        _debugMessenger = debugMessenger;
        _dxgiPresenter = dxgiPresenter;
        InitialWidth = initialWidth;
        InitialHeight = initialHeight;
        InitialWindowMode = initialWindowMode;
        _window.FocusChanged += HandleFocusChanged;
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
    /// 创建窗口时在首帧之前应用的平台模式。该值不随用户后续手工 resize/maximize 改写。
    /// </summary>
    public PlayerWindowMode InitialWindowMode { get; }

    /// <summary>
    /// 创建窗口时请求的 Windowed 客户区/Presentation 宽度；不随 maximize 或 fullscreen 改写。
    /// </summary>
    public int InitialWidth { get; }

    /// <summary>
    /// 创建窗口时请求的 Windowed 客户区/Presentation 高度；不随 maximize 或 fullscreen 改写。
    /// </summary>
    public int InitialHeight { get; }

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
    /// 平台窗口即将关闭。宿主可在回调内调用 <see cref="TryCancelCloseRequest" /> 延迟关闭并显示确认 UI。
    /// </summary>
    public event Action Closing
    {
        add
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _window.Closing += value;
        }

        remove
        {
            if (!_disposed)
            {
                _window.Closing -= value;
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
    /// 当前平台窗口左上角 X 坐标。
    /// </summary>
    public int LogicalX => _window.Position.X;

    /// <summary>
    /// 当前平台窗口左上角 Y 坐标。
    /// </summary>
    public int LogicalY => _window.Position.Y;

    /// <summary>
    /// 当前平台窗口状态。
    /// </summary>
    public RenderWindowState State => FromSilkWindowState(_window.WindowState);

    /// <summary>
    /// 最近一次平台 focus event 报告的焦点状态。
    /// </summary>
    public bool IsFocused { get; private set; }

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
    /// 移动平台顶层窗口。
    /// </summary>
    /// <param name="x">窗口左上角 X 坐标。</param>
    /// <param name="y">窗口左上角 Y 坐标。</param>
    public void Move(int x, int y)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _window.Position = new Vector2D<int>(x, y);
    }

    /// <summary>
    /// 切换平台顶层窗口状态。
    /// </summary>
    /// <param name="state">目标状态。</param>
    public void SetState(RenderWindowState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _window.WindowState = ToSilkWindowState(state);
    }

    /// <summary>
    /// 请求操作系统把输入焦点交给当前窗口。
    /// </summary>
    public void Focus()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _window.Focus();
    }

    /// <summary>
    /// 请求关闭窗口。
    /// </summary>
    public void Close()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _window.Close();
    }

    /// <summary>
    /// 尝试撤销平台已经发出的关闭请求，供带未保存修改确认的宿主继续绘制确认窗口。
    /// </summary>
    /// <returns>存在关闭请求且已经撤销时返回 true。</returns>
    public bool TryCancelCloseRequest()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_window.IsClosing)
        {
            return false;
        }

        _window.IsClosing = false;
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.FocusChanged -= HandleFocusChanged;
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
            ApplyInitialBorderlessFullscreen(window, options.WindowMode);
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

            return new RenderWindow(
                window,
                input,
                gl,
                nativeContext,
                backend,
                capabilities,
                debugMessenger,
                dxgiPresenter,
                options.Width,
                options.Height,
                options.WindowMode);
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

    private static void ApplyInitialBorderlessFullscreen(IWindow window, PlayerWindowMode mode)
    {
        if (mode != PlayerWindowMode.BorderlessFullscreen)
        {
            return;
        }

        IMonitor monitor = window.Monitor ??
            throw new InvalidOperationException("BorderlessFullscreen 初始化失败：窗口平台未提供目标显示器。");

        // 此时窗口以 IsVisible=false + Normal + Hidden 创建，monitor 仍保持 desktop video mode。
        // 在任何引擎帧或 SwapBuffers 之前把无框客户区铺满 monitor，再一次性显示，避免小窗闪烁与独占模式切屏。
        Rectangle<int> bounds = monitor.Bounds;
        Vector2D<int> position = bounds.Origin;
        Vector2D<int> size = monitor.VideoMode.Resolution ?? bounds.Size;
        if (size.X <= 0 || size.Y <= 0)
        {
            size = bounds.Size;
        }
        if (window.Native?.Win32 is { } handles &&
            PlayerWindowModeProbe.TryCaptureWindowsMonitorRect(handles.Hwnd, out PlatformPixelRect windowsMonitorRect))
        {
            // GLFW 的 IMonitor.Bounds 在 Windows 返回 work area；borderless fullscreen 必须使用 rcMonitor 而非 rcWork。
            position = new Vector2D<int>(windowsMonitorRect.Left, windowsMonitorRect.Top);
            size = new Vector2D<int>(windowsMonitorRect.Width, windowsMonitorRect.Height);
        }

        if (size.X <= 0 || size.Y <= 0)
        {
            throw new InvalidOperationException("BorderlessFullscreen 初始化失败：目标显示器 bounds 非法。");
        }

        window.Position = position;
        window.Size = size;
        window.IsVisible = true;
        window.DoEvents();
    }

    private static bool IsAtLeast(GlCapabilities capabilities, int major, int minor)
    {
        return capabilities.MajorVersion > major ||
            (capabilities.MajorVersion == major && capabilities.MinorVersion >= minor);
    }

    private void HandleFocusChanged(bool focused)
    {
        IsFocused = focused;
    }

    private static RenderWindowState FromSilkWindowState(WindowState state)
    {
        return state switch
        {
            WindowState.Normal => RenderWindowState.Normal,
            WindowState.Minimized => RenderWindowState.Minimized,
            WindowState.Maximized => RenderWindowState.Maximized,
            WindowState.Fullscreen => RenderWindowState.Fullscreen,
            _ => throw new InvalidOperationException($"Silk.NET 返回未知窗口状态：{state}。"),
        };
    }

    private static WindowState ToSilkWindowState(RenderWindowState state)
    {
        return state switch
        {
            RenderWindowState.Normal => WindowState.Normal,
            RenderWindowState.Minimized => WindowState.Minimized,
            RenderWindowState.Maximized => WindowState.Maximized,
            RenderWindowState.Fullscreen => WindowState.Fullscreen,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "未知窗口状态。"),
        };
    }
}
