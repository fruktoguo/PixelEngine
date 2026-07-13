using System.Runtime.InteropServices;

namespace PixelEngine.Rendering;

/// <summary>
/// Windows raw monitor DPI 查询。不可用时返回 null，绝不从 framebuffer scale 伪造物理 DPI。
/// </summary>
internal static partial class WindowsMonitorDpi
{
    private const uint MonitorDefaultToNearest = 2;
    private const int MonitorDpiTypeRaw = 2;

    internal static bool TryCapture(
        IntPtr hwnd,
        out nint monitorId,
        out float? actualPhysicalDpi)
    {
        monitorId = 0;
        actualPhysicalDpi = null;
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return false;
            }

            monitorId = monitor;
            int result = GetDpiForMonitor(monitor, MonitorDpiTypeRaw, out uint dpiX, out uint dpiY);
            if (result != 0 || dpiX == 0 || dpiY == 0)
            {
                return true;
            }

            float scalar = (float)((dpiX + (double)dpiY) * 0.5);
            if (float.IsFinite(scalar) && scalar > 0f)
            {
                actualPhysicalDpi = scalar;
            }

            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
