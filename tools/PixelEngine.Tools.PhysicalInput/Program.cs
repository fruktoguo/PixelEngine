using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixelEngine.Tools.PhysicalInput;

/// <summary>
/// Windows 物理输入发行探针：激活指定 HWND，并通过 Win32 SendInput 提交真实鼠标按下与释放。
/// </summary>
internal static partial class Program
{
    private const uint InputMouse = 0;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const int ShowRestore = 9;
    private static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new(-4);

    /// <summary>解析 click 参数、执行物理点击并输出 JSON 结果。</summary>
    public static int Main(string[] args)
    {
        Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        try
        {
            _ = OperatingSystem.IsWindows()
                ? true
                : throw new PlatformNotSupportedException("物理输入 probe 仅支持交互式 Windows 桌面。");
            Options options = Options.Parse(args);
            if (SetProcessDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2) == 0)
            {
                throw CreateWin32Exception("无法启用 Per-Monitor V2 DPI awareness。");
            }

            ClickResult result = ActivateAndClick(options.WindowHandle, options.NormalizedX, options.NormalizedY);
            Console.WriteLine(JsonSerializer.Serialize(result, PhysicalInputJsonContext.Default.ClickResult));
            return 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static ClickResult ActivateAndClick(IntPtr windowHandle, double normalizedX, double normalizedY)
    {
        _ = ShowWindow(windowHandle, ShowRestore);
        _ = SetForegroundWindow(windowHandle);
        if (GetWindowRect(windowHandle, out NativeRect window) == 0)
        {
            throw CreateWin32Exception("无法读取目标窗口矩形。");
        }

        uint sent = ClickScreen((window.Left + window.Right) / 2, window.Top + 10);
        Thread.Sleep(500);
        _ = SetForegroundWindow(windowHandle);
        bool foreground = GetForegroundWindow() == windowHandle;
        if (GetClientRect(windowHandle, out NativeRect client) == 0)
        {
            throw CreateWin32Exception("无法读取目标窗口客户区。");
        }

        int width = client.Right - client.Left;
        int height = client.Bottom - client.Top;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException($"目标窗口客户区无效：{width}x{height}。");
        }

        NativePoint point = new()
        {
            X = (int)Math.Round(width * normalizedX, MidpointRounding.AwayFromZero),
            Y = (int)Math.Round(height * normalizedY, MidpointRounding.AwayFromZero),
        };
        int clientX = point.X;
        int clientY = point.Y;
        if (ClientToScreen(windowHandle, ref point) == 0)
        {
            throw CreateWin32Exception("无法把客户区点击位置映射到屏幕。");
        }

        sent += ClickScreen(point.X, point.Y);
        return new ClickResult(
            width,
            height,
            clientX,
            clientY,
            point.X,
            point.Y,
            sent,
            foreground);
    }

    private static uint ClickScreen(int x, int y)
    {
        if (SetCursorPos(x, y) == 0)
        {
            throw CreateWin32Exception("无法移动系统指针。");
        }

        Thread.Sleep(120);
        NativeInput down = CreateMouseInput(MouseEventLeftDown);
        NativeInput up = CreateMouseInput(MouseEventLeftUp);
        uint sent = SendInput(1, in down, Marshal.SizeOf<NativeInput>());
        Thread.Sleep(220);
        sent += SendInput(1, in up, Marshal.SizeOf<NativeInput>());
        return sent == 2
            ? sent
            : throw CreateWin32Exception("SendInput 未完整提交按下/释放。");
    }

    private static NativeInput CreateMouseInput(uint flags)
    {
        return new NativeInput
        {
            Type = InputMouse,
            Union = new NativeInputUnion
            {
                Mouse = new NativeMouseInput { Flags = flags },
            },
        };
    }

    private static Win32Exception CreateWin32Exception(string message)
    {
        return new Win32Exception(Marshal.GetLastPInvokeError(), message);
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int SetProcessDpiAwarenessContext(IntPtr value);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int GetClientRect(IntPtr windowHandle, out NativeRect rect);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int GetWindowRect(IntPtr windowHandle, out NativeRect rect);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int ClientToScreen(IntPtr windowHandle, ref NativePoint point);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    private static partial int SetForegroundWindow(IntPtr windowHandle);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial int ShowWindow(IntPtr windowHandle, int command);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint inputCount, in NativeInput input, int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        internal int X;
        internal int Y;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativeInputUnion
    {
        [FieldOffset(0)]
        internal NativeMouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        internal uint Type;
        internal NativeInputUnion Union;
    }

    private sealed record Options(IntPtr WindowHandle, double NormalizedX, double NormalizedY)
    {
        internal static Options Parse(string[] args)
        {
            ArgumentNullException.ThrowIfNull(args);
            if (args.Length != 7 || !string.Equals(args[0], "click", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "用法：pixelengine-physical-input click --hwnd <int64> --normalized-x <0..1> --normalized-y <0..1>。",
                    nameof(args));
            }

            long rawHandle = ParseInt64(args, 1, "--hwnd");
            double normalizedX = ParseDouble(args, 3, "--normalized-x");
            double normalizedY = ParseDouble(args, 5, "--normalized-y");
            if (rawHandle == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(args), "--hwnd 不能为 0。");
            }

            _ = normalizedX is <= 0d or >= 1d || normalizedY is <= 0d or >= 1d
                ? throw new ArgumentOutOfRangeException(nameof(args), "normalized 坐标必须位于开区间 (0, 1)。")
                : true;

            return new Options(new IntPtr(rawHandle), normalizedX, normalizedY);
        }

        private static long ParseInt64(string[] args, int optionIndex, string option)
        {
            RequireOption(args, optionIndex, option);
            return long.TryParse(args[optionIndex + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
                ? value
                : throw new ArgumentException($"{option} 必须是 Int64。", nameof(args));
        }

        private static double ParseDouble(string[] args, int optionIndex, string option)
        {
            RequireOption(args, optionIndex, option);
            return double.TryParse(args[optionIndex + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) &&
                double.IsFinite(value)
                ? value
                : throw new ArgumentException($"{option} 必须是有限数值。", nameof(args));
        }

        private static void RequireOption(string[] args, int index, string expected)
        {
            if (!string.Equals(args[index], expected, StringComparison.Ordinal))
            {
                throw new ArgumentException($"缺少参数 {expected}。", nameof(args));
            }
        }
    }
}

/// <summary>物理点击的结构化结果。</summary>
/// <param name="ClientWidth">目标客户区宽度。</param>
/// <param name="ClientHeight">目标客户区高度。</param>
/// <param name="ClientX">点击客户区 X。</param>
/// <param name="ClientY">点击客户区 Y。</param>
/// <param name="ScreenX">点击物理屏幕 X。</param>
/// <param name="ScreenY">点击物理屏幕 Y。</param>
/// <param name="SentInputs">提交的鼠标按下/释放 INPUT 数量。</param>
/// <param name="Foreground">目标点击前是否已成为前台窗口。</param>
internal sealed record ClickResult(
    int ClientWidth,
    int ClientHeight,
    int ClientX,
    int ClientY,
    int ScreenX,
    int ScreenY,
    uint SentInputs,
    bool Foreground);

[JsonSerializable(typeof(ClickResult))]
internal sealed partial class PhysicalInputJsonContext : JsonSerializerContext;
