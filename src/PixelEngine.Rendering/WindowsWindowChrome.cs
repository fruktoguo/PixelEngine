using System.Runtime.InteropServices;

namespace PixelEngine.Rendering;

/// <summary>
/// Windows DWM 标题栏着色；保留系统拖拽、缩放、Snap Layout 与无障碍行为。
/// </summary>
internal static partial class WindowsWindowChrome
{
    private const int UseImmersiveDarkMode = 20;
    private const int UseImmersiveDarkModeLegacy = 19;
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int TextColor = 36;

    public static void TryApply(IntPtr hwnd, uint captionRgb, uint textRgb, uint borderRgb)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            int enabled = 1;
            if (DwmSetWindowAttributeInt(hwnd, UseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
            {
                _ = DwmSetWindowAttributeInt(hwnd, UseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
            }

            uint caption = ToColorRef(captionRgb);
            uint text = ToColorRef(textRgb);
            uint border = ToColorRef(borderRgb);
            _ = DwmSetWindowAttributeColor(hwnd, CaptionColor, ref caption, sizeof(uint));
            _ = DwmSetWindowAttributeColor(hwnd, TextColor, ref text, sizeof(uint));
            _ = DwmSetWindowAttributeColor(hwnd, BorderColor, ref border, sizeof(uint));
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // 旧 Windows 或无 DWM 环境保留系统标题栏，不影响窗口创建。
        }
    }

    private static uint ToColorRef(uint rgb)
    {
        return ((rgb & 0x0000FFu) << 16) |
            (rgb & 0x00FF00u) |
            ((rgb & 0xFF0000u) >> 16);
    }

    [LibraryImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static partial int DwmSetWindowAttributeInt(IntPtr hwnd, int attribute, ref int value, int valueSize);

    [LibraryImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static partial int DwmSetWindowAttributeColor(IntPtr hwnd, int attribute, ref uint value, int valueSize);
}
