using System.Runtime.InteropServices;

namespace PixelEngine.Rendering;

/// <summary>
/// 平台窗口矩形的物理像素坐标；Right/Bottom 保持 Win32 exclusive 语义。
/// </summary>
public readonly record struct PlatformPixelRect
{
    /// <summary>
    /// 创建物理像素矩形。
    /// </summary>
    /// <param name="left">左边界。</param>
    /// <param name="top">上边界。</param>
    /// <param name="right">exclusive 右边界。</param>
    /// <param name="bottom">exclusive 下边界。</param>
    public PlatformPixelRect(int left, int top, int right, int bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    /// <summary>左边界。</summary>
    public int Left { get; }

    /// <summary>上边界。</summary>
    public int Top { get; }

    /// <summary>exclusive 右边界。</summary>
    public int Right { get; }

    /// <summary>exclusive 下边界。</summary>
    public int Bottom { get; }

    /// <summary>非负宽度。</summary>
    public int Width => Math.Max(0, Right - Left);

    /// <summary>非负高度。</summary>
    public int Height => Math.Max(0, Bottom - Top);

    /// <summary>
    /// 生成不含空格的稳定探针值：left:top:widthxheight。
    /// </summary>
    /// <returns>可由发布脚本解析的矩形。</returns>
    public string ToProbeValue()
    {
        return $"{Left}:{Top}:{Width}x{Height}";
    }

    internal bool Covers(in PlatformPixelRect other, int tolerance)
    {
        return Left <= other.Left + tolerance &&
            Top <= other.Top + tolerance &&
            Right >= other.Right - tolerance &&
            Bottom >= other.Bottom - tolerance;
    }

    internal bool ApproximatelyEquals(in PlatformPixelRect other, int tolerance)
    {
        return Math.Abs(Left - other.Left) <= tolerance &&
            Math.Abs(Top - other.Top) <= tolerance &&
            Math.Abs(Right - other.Right) <= tolerance &&
            Math.Abs(Bottom - other.Bottom) <= tolerance;
    }
}

/// <summary>
/// Player 窗口模式实际应用结果。
/// </summary>
public readonly record struct PlayerWindowModeProbeEvaluation
{
    internal PlayerWindowModeProbeEvaluation(bool applied, string reason)
    {
        Applied = applied;
        Reason = reason;
    }

    /// <summary>请求模式是否与真实平台窗口状态一致。</summary>
    public bool Applied { get; }

    /// <summary>失败 token；成功固定为 none。</summary>
    public string Reason { get; }
}

/// <summary>
/// 独立 Player 的只读平台窗口快照。Windows 提供真实 HWND/style/monitor 数据；其他平台明确返回不可用。
/// </summary>
public readonly record struct PlayerWindowModeProbeSnapshot
{
    private const uint WindowStylePopup = 0x80000000;
    private const uint WindowStyleCaption = 0x00C00000;
    private const uint WindowStyleThickFrame = 0x00040000;

    internal PlayerWindowModeProbeSnapshot(
        bool available,
        string captureFailureReason,
        PlayerWindowMode requestedMode,
        int requestedWidth,
        int requestedHeight,
        nint windowHandle,
        nint monitorHandle,
        uint style,
        uint extendedStyle,
        bool isVisible,
        bool isZoomed,
        PlatformPixelRect windowRect,
        PlatformPixelRect clientRect,
        PlatformPixelRect monitorRect,
        PlatformPixelRect workRect,
        uint dpi)
    {
        Available = available;
        CaptureFailureReason = captureFailureReason;
        RequestedMode = requestedMode;
        RequestedWidth = requestedWidth;
        RequestedHeight = requestedHeight;
        WindowHandle = windowHandle;
        MonitorHandle = monitorHandle;
        Style = style;
        ExtendedStyle = extendedStyle;
        IsVisible = isVisible;
        IsZoomed = isZoomed;
        WindowRect = windowRect;
        ClientRect = clientRect;
        MonitorRect = monitorRect;
        WorkRect = workRect;
        Dpi = dpi;
    }

    /// <summary>当前平台是否提供了完整快照。</summary>
    public bool Available { get; }

    /// <summary>采集失败 token；成功固定为 none。</summary>
    public string CaptureFailureReason { get; }

    /// <summary>窗口初始化前请求的 Player 模式。</summary>
    public PlayerWindowMode RequestedMode { get; }

    /// <summary>配置的 Windowed 客户区/Presentation 宽度。</summary>
    public int RequestedWidth { get; }

    /// <summary>配置的 Windowed 客户区/Presentation 高度。</summary>
    public int RequestedHeight { get; }

    /// <summary>平台窗口句柄；不可用时为零。</summary>
    public nint WindowHandle { get; }

    /// <summary>窗口当前所在显示器句柄；不可用时为零。</summary>
    public nint MonitorHandle { get; }

    /// <summary>Win32 GWL_STYLE 的低 32 位。</summary>
    public uint Style { get; }

    /// <summary>Win32 GWL_EXSTYLE 的低 32 位。</summary>
    public uint ExtendedStyle { get; }

    /// <summary>窗口是否可见。</summary>
    public bool IsVisible { get; }

    /// <summary>窗口是否处于 Win32 maximized/zoomed 状态。</summary>
    public bool IsZoomed { get; }

    /// <summary>屏幕坐标下的窗口外框。</summary>
    public PlatformPixelRect WindowRect { get; }

    /// <summary>客户区尺寸；原点通常为 0,0。</summary>
    public PlatformPixelRect ClientRect { get; }

    /// <summary>当前显示器的完整物理矩形。</summary>
    public PlatformPixelRect MonitorRect { get; }

    /// <summary>当前显示器扣除任务栏后的工作区矩形。</summary>
    public PlatformPixelRect WorkRect { get; }

    /// <summary>GetDpiForWindow 返回值；旧系统不可用时为零。</summary>
    public uint Dpi { get; }

    /// <summary>窗口是否带 WS_POPUP。</summary>
    public bool IsPopup => (Style & WindowStylePopup) != 0;

    /// <summary>窗口是否带完整 WS_CAPTION。</summary>
    public bool HasCaption => (Style & WindowStyleCaption) == WindowStyleCaption;

    /// <summary>窗口是否带 WS_THICKFRAME。</summary>
    public bool HasThickFrame => (Style & WindowStyleThickFrame) != 0;

    /// <summary>当前客户区是否等于配置的 Windowed Presentation 尺寸。</summary>
    public bool ClientMatchesRequestedPresentation =>
        Math.Abs(ClientRect.Width - RequestedWidth) <= 2 &&
        Math.Abs(ClientRect.Height - RequestedHeight) <= 2;

    /// <summary>
    /// 配置的客户区加上当前 non-client frame 后是否能完整放进 monitor work area。
    /// false 表示操作系统可能合法夹取小屏上的初始普通窗口。
    /// </summary>
    public bool RequestedWindowFitsWorkArea
    {
        get
        {
            int nonClientWidth = Math.Max(0, WindowRect.Width - ClientRect.Width);
            int nonClientHeight = Math.Max(0, WindowRect.Height - ClientRect.Height);
            return RequestedWidth + nonClientWidth <= WorkRect.Width + 2 &&
                RequestedHeight + nonClientHeight <= WorkRect.Height + 2;
        }
    }
}

/// <summary>
/// Player 窗口模式平台探针。它只读取 HWND，不改变窗口状态，也不把请求枚举回显成已生效。
/// </summary>
public static partial class PlayerWindowModeProbe
{
    private const int WindowLongStyle = -16;
    private const int WindowLongExtendedStyle = -20;
    private const uint MonitorDefaultToNearest = 2;
    private const int GeometryTolerancePixels = 2;

    /// <summary>
    /// 捕获当前窗口的真实平台状态。请求模式和 Windowed 尺寸来自窗口创建时的不可变参数。
    /// </summary>
    /// <param name="window">已初始化的渲染窗口。</param>
    /// <returns>平台快照；不支持的平台通过 <see cref="PlayerWindowModeProbeSnapshot.Available"/> 明示。</returns>
    public static PlayerWindowModeProbeSnapshot Capture(RenderWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        PlayerWindowMode requestedMode = window.InitialWindowMode;
        int requestedWidth = window.InitialWidth;
        int requestedHeight = window.InitialHeight;

        if (!OperatingSystem.IsWindows())
        {
            return Unavailable(requestedMode, requestedWidth, requestedHeight, "not_windows");
        }

        if (!window.TryGetWin32WindowHandle(out IntPtr hwnd) || hwnd == IntPtr.Zero)
        {
            return Unavailable(requestedMode, requestedWidth, requestedHeight, "hwnd_unavailable");
        }

        try
        {
            IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return Unavailable(requestedMode, requestedWidth, requestedHeight, "monitor_unavailable", hwnd);
            }

            NativeMonitorInfo monitorInfo = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMonitorInfo>(),
            };
            if (!GetWindowRect(hwnd, out NativeRect windowRect) ||
                !GetClientRect(hwnd, out NativeRect clientRect) ||
                !GetMonitorInfoW(monitor, ref monitorInfo))
            {
                return Unavailable(requestedMode, requestedWidth, requestedHeight, "win32_geometry_failed", hwnd, monitor);
            }

            uint dpi = 0;
            try
            {
                dpi = GetDpiForWindow(hwnd);
            }
            catch (EntryPointNotFoundException)
            {
                // Windows 8.1 等旧系统仍可验证窗口模式，只缺失 DPI 数值。
            }

            return new PlayerWindowModeProbeSnapshot(
                available: true,
                captureFailureReason: "none",
                requestedMode,
                requestedWidth,
                requestedHeight,
                windowHandle: hwnd,
                monitorHandle: monitor,
                style: unchecked((uint)GetWindowLongPtrW(hwnd, WindowLongStyle).ToInt64()),
                extendedStyle: unchecked((uint)GetWindowLongPtrW(hwnd, WindowLongExtendedStyle).ToInt64()),
                isVisible: IsWindowVisible(hwnd),
                isZoomed: IsZoomed(hwnd),
                windowRect: ToManaged(windowRect),
                clientRect: ToManaged(clientRect),
                monitorRect: ToManaged(monitorInfo.Monitor),
                workRect: ToManaged(monitorInfo.Work),
                dpi);
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException)
        {
            return Unavailable(requestedMode, requestedWidth, requestedHeight, "win32_api_unavailable", hwnd);
        }
    }

    internal static bool TryCaptureWindowsMonitorRect(IntPtr hwnd, out PlatformPixelRect monitorRect)
    {
        monitorRect = default;
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            NativeMonitorInfo monitorInfo = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMonitorInfo>(),
            };
            if (monitor == IntPtr.Zero || !GetMonitorInfoW(monitor, ref monitorInfo))
            {
                return false;
            }

            monitorRect = ToManaged(monitorInfo.Monitor);
            return monitorRect.Width > 0 && monitorRect.Height > 0;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// 依据真实 style、zoom 状态与 monitor geometry 判定请求模式是否已经生效。
    /// </summary>
    /// <param name="snapshot">由 <see cref="Capture"/> 取得的平台快照。</param>
    /// <returns>fail-closed 判定与稳定失败 token。</returns>
    public static PlayerWindowModeProbeEvaluation Evaluate(in PlayerWindowModeProbeSnapshot snapshot)
    {
        return !snapshot.Available
            ? new PlayerWindowModeProbeEvaluation(false, snapshot.CaptureFailureReason)
            : !snapshot.IsVisible
                ? new PlayerWindowModeProbeEvaluation(false, "window_not_visible")
                : snapshot.RequestedMode switch
                {
                    PlayerWindowMode.Windowed => EvaluateWindowed(in snapshot),
                    PlayerWindowMode.MaximizedWindow => EvaluateMaximized(in snapshot),
                    PlayerWindowMode.BorderlessFullscreen => EvaluateBorderless(in snapshot),
                    _ => new PlayerWindowModeProbeEvaluation(false, "unknown_requested_mode"),
                };
    }

    private static PlayerWindowModeProbeEvaluation EvaluateWindowed(in PlayerWindowModeProbeSnapshot snapshot)
    {
        return snapshot.IsZoomed
            ? new PlayerWindowModeProbeEvaluation(false, "windowed_is_zoomed")
            : snapshot.IsPopup || !snapshot.HasCaption || !snapshot.HasThickFrame
                ? new PlayerWindowModeProbeEvaluation(false, "windowed_style_mismatch")
                : !snapshot.ClientMatchesRequestedPresentation && snapshot.RequestedWindowFitsWorkArea
                    ? new PlayerWindowModeProbeEvaluation(false, "windowed_client_size_mismatch")
                    : new PlayerWindowModeProbeEvaluation(true, "none");
    }

    private static PlayerWindowModeProbeEvaluation EvaluateMaximized(in PlayerWindowModeProbeSnapshot snapshot)
    {
        return !snapshot.IsZoomed
            ? new PlayerWindowModeProbeEvaluation(false, "maximized_not_zoomed")
            : snapshot.IsPopup || !snapshot.HasCaption || !snapshot.HasThickFrame
                ? new PlayerWindowModeProbeEvaluation(false, "maximized_style_mismatch")
                : !snapshot.WindowRect.Covers(snapshot.WorkRect, GeometryTolerancePixels)
                    ? new PlayerWindowModeProbeEvaluation(false, "maximized_work_area_mismatch")
                    : new PlayerWindowModeProbeEvaluation(true, "none");
    }

    private static PlayerWindowModeProbeEvaluation EvaluateBorderless(in PlayerWindowModeProbeSnapshot snapshot)
    {
        return !snapshot.IsPopup || snapshot.HasCaption || snapshot.HasThickFrame
            ? new PlayerWindowModeProbeEvaluation(false, "borderless_style_mismatch")
            : !snapshot.WindowRect.ApproximatelyEquals(snapshot.MonitorRect, GeometryTolerancePixels)
                ? new PlayerWindowModeProbeEvaluation(false, "borderless_monitor_rect_mismatch")
                : Math.Abs(snapshot.ClientRect.Width - snapshot.MonitorRect.Width) > GeometryTolerancePixels ||
                    Math.Abs(snapshot.ClientRect.Height - snapshot.MonitorRect.Height) > GeometryTolerancePixels
                    ? new PlayerWindowModeProbeEvaluation(false, "borderless_client_size_mismatch")
                    : new PlayerWindowModeProbeEvaluation(true, "none");
    }

    private static PlayerWindowModeProbeSnapshot Unavailable(
        PlayerWindowMode requestedMode,
        int requestedWidth,
        int requestedHeight,
        string reason,
        nint hwnd = 0,
        nint monitor = 0)
    {
        return new PlayerWindowModeProbeSnapshot(
            available: false,
            captureFailureReason: reason,
            requestedMode,
            requestedWidth,
            requestedHeight,
            windowHandle: hwnd,
            monitorHandle: monitor,
            style: 0,
            extendedStyle: 0,
            isVisible: false,
            isZoomed: false,
            windowRect: default,
            clientRect: default,
            monitorRect: default,
            workRect: default,
            dpi: 0);
    }

    private static PlatformPixelRect ToManaged(in NativeRect rect)
    {
        return new PlatformPixelRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(IntPtr hwnd, out NativeRect rect);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(IntPtr monitor, ref NativeMonitorInfo monitorInfo);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsZoomed(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr hwnd);
}
