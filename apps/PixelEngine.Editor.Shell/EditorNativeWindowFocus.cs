using System.Runtime.InteropServices;
using PixelEngine.Rendering;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 查询平台真实前台窗口；Windows 不能把“曾收到 focus event”当成当前焦点。
/// </summary>
internal static partial class EditorNativeWindowFocus
{
    public static bool IsFocused(RenderWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return OperatingSystem.IsWindows() &&
            window.TryGetWin32WindowHandle(out IntPtr hwnd) &&
            hwnd != IntPtr.Zero
                ? GetForegroundWindow() == hwnd
                : window.IsFocused;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();
}
