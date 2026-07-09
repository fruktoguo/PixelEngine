using System.Runtime.InteropServices;

namespace PixelEngine.Rendering;

/// <summary>
/// Windows 进程级 DPI 感知初始化；在创建 GLFW 窗口前调用，避免高 DPI 显示器上逻辑尺寸与物理像素错位。
/// </summary>
internal static partial class WindowsDpiAwareness
{
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);
    private static int _attempted;

    /// <summary>
    /// 确保当前进程启用 Per-Monitor V2 DPI 感知；非 Windows 平台或已尝试过则立即返回。
    /// </summary>
    public static void EnsureEnabled()
    {
        if (!OperatingSystem.IsWindows() || Interlocked.Exchange(ref _attempted, 1) != 0)
        {
            return;
        }

        _ = SetProcessDpiAwarenessContext(PerMonitorAwareV2);
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
}
