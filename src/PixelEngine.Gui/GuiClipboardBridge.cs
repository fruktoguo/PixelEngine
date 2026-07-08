using Hexa.NET.ImGui;
using System.Runtime.InteropServices;
using System.Text;

namespace PixelEngine.Gui;

/// <summary>
/// ImGui 平台剪贴板桥接，优先使用系统剪贴板，失败时回退到进程内文本。
/// </summary>
public sealed unsafe partial class GuiClipboardBridge : IDisposable
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;
    private readonly PlatformGetClipboardTextFn _getClipboardText;
    private readonly PlatformSetClipboardTextFn _setClipboardText;
    private string _fallbackText = string.Empty;
    private IntPtr _clipboardTextUtf8;
    private bool _disposed;

    /// <summary>
    /// 创建剪贴板桥接实例，并固定 ImGui 平台回调委托。
    /// </summary>
    public GuiClipboardBridge()
    {
        _getClipboardText = GetClipboardText;
        _setClipboardText = SetClipboardText;
    }

    /// <summary>
    /// 将回调注册到当前 ImGui platform IO。
    /// </summary>
    public void Attach()
    {
        ImGuiPlatformIOPtr platform = ImGui.GetPlatformIO();
        platform.PlatformGetClipboardTextFn = (void*)Marshal.GetFunctionPointerForDelegate(_getClipboardText);
        platform.PlatformSetClipboardTextFn = (void*)Marshal.GetFunctionPointerForDelegate(_setClipboardText);
    }

    /// <summary>
    /// 从当前 ImGui platform IO 移除回调。
    /// </summary>
    public void Detach()
    {
        ImGuiPlatformIOPtr platform = ImGui.GetPlatformIO();
        platform.PlatformGetClipboardTextFn = null;
        platform.PlatformSetClipboardTextFn = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        FreeClipboardText();
        _disposed = true;
    }

    private byte* GetClipboardText(ImGuiContext* context)
    {
        _ = context;
        string text = OperatingSystem.IsWindows() && TryGetWindowsClipboardText(out string? systemText)
            ? systemText ?? string.Empty
            : _fallbackText;
        FreeClipboardText();
        _clipboardTextUtf8 = Marshal.StringToCoTaskMemUTF8(text);
        return (byte*)_clipboardTextUtf8;
    }

    private void SetClipboardText(ImGuiContext* context, byte* text)
    {
        _ = context;
        string value = text is null ? string.Empty : Marshal.PtrToStringUTF8((IntPtr)text) ?? string.Empty;
        _fallbackText = value;
        if (OperatingSystem.IsWindows())
        {
            _ = TrySetWindowsClipboardText(value);
        }
    }

    private void FreeClipboardText()
    {
        if (_clipboardTextUtf8 == IntPtr.Zero)
        {
            return;
        }

        Marshal.FreeCoTaskMem(_clipboardTextUtf8);
        _clipboardTextUtf8 = IntPtr.Zero;
    }

    private static bool TryGetWindowsClipboardText(out string? text)
    {
        text = null;
        if (!IsClipboardFormatAvailable(CfUnicodeText) || !OpenClipboard(IntPtr.Zero))
        {
            return false;
        }

        try
        {
            IntPtr handle = GetClipboardData(CfUnicodeText);
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            IntPtr locked = GlobalLock(handle);
            if (locked == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                text = Marshal.PtrToStringUni(locked) ?? string.Empty;
                return true;
            }
            finally
            {
                _ = GlobalUnlock(handle);
            }
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    private static bool TrySetWindowsClipboardText(string text)
    {
        byte[] bytes = Encoding.Unicode.GetBytes(text + '\0');
        IntPtr memory = GlobalAlloc(GmemMoveable, (UIntPtr)bytes.Length);
        if (memory == IntPtr.Zero)
        {
            return false;
        }

        IntPtr locked = GlobalLock(memory);
        if (locked == IntPtr.Zero)
        {
            _ = GlobalFree(memory);
            return false;
        }

        try
        {
            Marshal.Copy(bytes, 0, locked, bytes.Length);
        }
        finally
        {
            _ = GlobalUnlock(memory);
        }

        if (!OpenClipboard(IntPtr.Zero))
        {
            _ = GlobalFree(memory);
            return false;
        }

        try
        {
            if (!EmptyClipboard())
            {
                _ = GlobalFree(memory);
                return false;
            }

            if (SetClipboardData(CfUnicodeText, memory) == IntPtr.Zero)
            {
                _ = GlobalFree(memory);
                return false;
            }

            memory = IntPtr.Zero;
            return true;
        }
        finally
        {
            _ = CloseClipboard();
            if (memory != IntPtr.Zero)
            {
                _ = GlobalFree(memory);
            }
        }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(IntPtr hWndNewOwner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr GetClipboardData(uint format);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr SetClipboardData(uint format, IntPtr memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalLock(IntPtr memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(IntPtr memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalFree(IntPtr memory);
}
