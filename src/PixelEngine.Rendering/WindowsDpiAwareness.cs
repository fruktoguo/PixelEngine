using System.Runtime.InteropServices;

namespace PixelEngine.Rendering;

internal static partial class WindowsDpiAwareness
{
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);
    private static int _attempted;

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
