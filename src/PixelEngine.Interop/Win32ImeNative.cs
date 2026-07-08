using System.Runtime.InteropServices;

namespace PixelEngine.Interop;

/// <summary>
/// Win32 IMM32 输入法接口的窄绑定；调用方负责只在 Windows + 有 HWND 时使用。
/// </summary>
public static partial class Win32ImeNative
{
    /// <summary>
    /// 取得窗口当前输入法上下文。
    /// </summary>
    /// <param name="hwnd">Win32 HWND。</param>
    /// <returns>输入法上下文；失败时为 <see cref="IntPtr.Zero" />。</returns>
    public static IntPtr GetContext(IntPtr hwnd)
    {
        return ImmGetContext(hwnd);
    }

    /// <summary>
    /// 释放输入法上下文。
    /// </summary>
    /// <param name="hwnd">Win32 HWND。</param>
    /// <param name="context">输入法上下文。</param>
    /// <returns>释放成功时为 true。</returns>
    public static bool ReleaseContext(IntPtr hwnd, IntPtr context)
    {
        return ImmReleaseContext(hwnd, context);
    }

    /// <summary>
    /// 读取指定 composition 字段的 UTF-16 字节。
    /// </summary>
    /// <param name="context">输入法上下文。</param>
    /// <param name="index">IMM32 composition 字段索引。</param>
    /// <param name="destination">目标字节缓冲。</param>
    /// <returns>实际字段字节数；无字段时通常为负数或 0。</returns>
    public static int GetCompositionString(IntPtr context, int index, byte[] destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return ImmGetCompositionStringW(context, index, destination, destination.Length);
    }

    /// <summary>
    /// 读取 composition 光标位置。
    /// </summary>
    /// <param name="context">输入法上下文。</param>
    /// <param name="index">IMM32 光标字段索引。</param>
    /// <returns>composition 内光标字符索引。</returns>
    public static int GetCompositionInteger(IntPtr context, int index)
    {
        return ImmGetCompositionStringW(context, index, IntPtr.Zero, 0);
    }

    [LibraryImport("imm32.dll", SetLastError = false)]
    private static partial IntPtr ImmGetContext(IntPtr hwnd);

    [LibraryImport("imm32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ImmReleaseContext(IntPtr hwnd, IntPtr context);

    [LibraryImport("imm32.dll", EntryPoint = "ImmGetCompositionStringW", SetLastError = false)]
    private static partial int ImmGetCompositionStringW(
        IntPtr context,
        int index,
        [Out] byte[] destination,
        int destinationBytes);

    [LibraryImport("imm32.dll", EntryPoint = "ImmGetCompositionStringW", SetLastError = false)]
    private static partial int ImmGetCompositionStringW(
        IntPtr context,
        int index,
        IntPtr destination,
        int destinationBytes);
}
