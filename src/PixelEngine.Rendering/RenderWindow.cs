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
    public bool IsClosing => _window.IsClosing;

    /// <summary>
    /// 当前窗口 framebuffer 宽度。
    /// </summary>
    public int Width => _window.Size.X;

    /// <summary>
    /// 当前窗口 framebuffer 高度。
    /// </summary>
    public int Height => _window.Size.Y;

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

        _debugMessenger?.Dispose();
        Gl.Dispose();
        Input.Dispose();
        _window.Dispose();
        _disposed = true;
    }

    private static RenderWindow CreateForBackend(
        RenderWindowOptions options,
        RenderBackend backend,
        Action<string>? diagnostics)
    {
        WindowOptions windowOptions = RenderBackendSelector.CreateWindowOptions(options, backend);
        IWindow window = Window.Create(windowOptions);
        try
        {
            window.Initialize();
            IInputContext input = window.CreateInput();
            GL gl = GL.GetApi(window);
            GlCapabilities capabilities = GlCapabilities.Query(gl);
            GlDebugMessenger? debugMessenger = options.EnableDebugContext && diagnostics is not null
                ? GlDebugMessenger.TryCreate(gl, capabilities, diagnostics)
                : null;
            return new RenderWindow(window, input, gl, backend, capabilities, debugMessenger);
        }
        catch
        {
            window.Dispose();
            throw;
        }
    }
}
