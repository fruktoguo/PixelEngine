using System.Runtime.InteropServices;

namespace PixelEngine.Interop;

/// <summary>
/// Win32 IMM32 输入法接口的窄绑定；调用方负责只在 Windows + 有 HWND 时使用。
/// </summary>
public static partial class Win32ImeNative
{
    /// <summary>CFS_RECT：提供 rcArea 作为 composition 相关矩形。</summary>
    public const int CompositionFormStyleRect = 0x0001;

    /// <summary>CFS_POINT：按 ptCurrentPos 定位 composition 窗。</summary>
    public const int CompositionFormStylePoint = 0x0002;

    /// <summary>CFS_CANDIDATEPOS：按 ptCurrentPos 定位候选窗。</summary>
    public const int CandidateFormStyleCandidatePos = 0x0040;

    /// <summary>CFS_EXCLUDE：候选窗避开 rcArea 矩形（通常为 caret/composition 区）。</summary>
    public const int CandidateFormStyleExclude = 0x0080;

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

    /// <summary>
    /// 设置 composition 窗位置（client 坐标）。
    /// </summary>
    /// <param name="context">输入法上下文。</param>
    /// <param name="form">composition 窗描述。</param>
    /// <returns>设置成功时为 true。</returns>
    public static bool SetCompositionWindow(IntPtr context, in Win32CompositionForm form)
    {
        return ImmSetCompositionWindow(context, in form);
    }

    /// <summary>
    /// 设置候选窗位置（client 坐标）。
    /// </summary>
    /// <param name="context">输入法上下文。</param>
    /// <param name="form">候选窗描述。</param>
    /// <returns>设置成功时为 true。</returns>
    public static bool SetCandidateWindow(IntPtr context, in Win32CandidateForm form)
    {
        return ImmSetCandidateWindow(context, in form);
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

    [LibraryImport("imm32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ImmSetCompositionWindow(IntPtr context, in Win32CompositionForm form);

    [LibraryImport("imm32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ImmSetCandidateWindow(IntPtr context, in Win32CandidateForm form);
}

/// <summary>
/// Win32 POINT 等价结构。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Win32Point
{
    /// <summary>x 坐标。</summary>
    public int X;

    /// <summary>y 坐标。</summary>
    public int Y;
}

/// <summary>
/// Win32 RECT 等价结构。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Win32Rect
{
    /// <summary>左。</summary>
    public int Left;

    /// <summary>上。</summary>
    public int Top;

    /// <summary>右。</summary>
    public int Right;

    /// <summary>下。</summary>
    public int Bottom;
}

/// <summary>
/// IMM32 COMPOSITIONFORM 等价结构。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Win32CompositionForm
{
    /// <summary>定位样式（如 CFS_POINT）。</summary>
    public int Style;

    /// <summary>当前插入点。</summary>
    public Win32Point CurrentPos;

    /// <summary>排除矩形。</summary>
    public Win32Rect Area;
}

/// <summary>
/// IMM32 CANDIDATEFORM 等价结构。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Win32CandidateForm
{
    /// <summary>候选列表索引。</summary>
    public int Index;

    /// <summary>定位样式（如 CFS_CANDIDATEPOS）。</summary>
    public int Style;

    /// <summary>候选窗锚点。</summary>
    public Win32Point CurrentPos;

    /// <summary>排除矩形。</summary>
    public Win32Rect Area;
}
