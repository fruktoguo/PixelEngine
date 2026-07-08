using System.Text;
using PixelEngine.Interop;
using PixelEngine.UI;

namespace PixelEngine.Hosting;

internal sealed class WindowsImeCompositionReader
{
    private const int CompositionString = 0x0008;
    private const int CompositionAttribute = 0x0010;
    private const int CompositionBufferBytes = 512;
    private const byte TargetConvertedAttribute = 0x01;
    private const byte TargetNotConvertedAttribute = 0x03;

    private readonly Func<IntPtr> _hwndProvider;
    private readonly IWindowsImeNative _native;
    private readonly bool _enabled;
    private readonly byte[] _compositionBytes = new byte[CompositionBufferBytes];
    private readonly byte[] _attributeBytes = new byte[CompositionBufferBytes / sizeof(char)];

    internal WindowsImeCompositionReader(Func<IntPtr> hwndProvider)
        : this(hwndProvider, WindowsImeNativeMethods.Instance, OperatingSystem.IsWindows())
    {
    }

    internal WindowsImeCompositionReader(Func<IntPtr> hwndProvider, IWindowsImeNative native, bool enableWindowsComposition)
    {
        _hwndProvider = hwndProvider ?? throw new ArgumentNullException(nameof(hwndProvider));
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _enabled = enableWindowsComposition;
    }

    internal UiTextCompositionCapabilities Capabilities => _enabled
        ? UiTextCompositionCapabilities.Supported("Windows IMM32 输入源可从 Win32 HWND 采集真实 IME composition 预编辑状态；候选窗与预编辑可视化仍归 M15 真实窗口验收。")
        : UiTextCompositionCapabilities.Unsupported("当前平台不是 Windows，或未启用 Windows IMM32 composition reader；预编辑状态保持 inactive。");

    internal int CaptureTextComposition(Span<char> destination, out UiTextComposition composition)
    {
        composition = UiTextComposition.Inactive;
        if (!_enabled)
        {
            return 0;
        }

        IntPtr hwnd = _hwndProvider();
        if (hwnd == IntPtr.Zero)
        {
            return 0;
        }

        IntPtr context = _native.GetContext(hwnd);
        if (context == IntPtr.Zero)
        {
            return 0;
        }

        try
        {
            int byteCount = _native.GetCompositionString(context, CompositionString, _compositionBytes);
            if (byteCount <= 0)
            {
                return 0;
            }

            int readableBytes = Math.Min(byteCount & ~1, Math.Min(_compositionBytes.Length, destination.Length * sizeof(char)));
            int written = readableBytes == 0
                ? 0
                : Encoding.Unicode.GetChars(_compositionBytes.AsSpan(0, readableBytes), destination);
            int cursor = Math.Max(0, _native.GetCompositionCursorPosition(context));
            int attributeBytes = _native.GetCompositionString(context, CompositionAttribute, _attributeBytes);
            FindTargetAttributeRange(
                _attributeBytes.AsSpan(0, Math.Clamp(attributeBytes, 0, _attributeBytes.Length)),
                written,
                out int selectionStart,
                out int selectionLength);
            composition = new UiTextComposition(
                isActive: true,
                cursorIndex: cursor,
                selectionStart,
                selectionLength).ClampToTextLength(written);
            return written;
        }
        finally
        {
            _ = _native.ReleaseContext(hwnd, context);
        }
    }

    internal static void FindTargetAttributeRange(
        ReadOnlySpan<byte> attributes,
        int textLength,
        out int selectionStart,
        out int selectionLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(textLength);
        int count = Math.Min(attributes.Length, textLength);
        selectionStart = 0;
        selectionLength = 0;
        for (int i = 0; i < count; i++)
        {
            if (!IsTargetAttribute(attributes[i]))
            {
                continue;
            }

            selectionStart = i;
            int end = i + 1;
            while (end < count && IsTargetAttribute(attributes[end]))
            {
                end++;
            }

            selectionLength = end - selectionStart;
            return;
        }
    }

    private static bool IsTargetAttribute(byte attribute)
    {
        return attribute is TargetConvertedAttribute or TargetNotConvertedAttribute;
    }
}

internal interface IWindowsImeNative
{
    IntPtr GetContext(IntPtr hwnd);

    bool ReleaseContext(IntPtr hwnd, IntPtr context);

    int GetCompositionString(IntPtr context, int index, byte[] destination);

    int GetCompositionCursorPosition(IntPtr context);
}

internal sealed class WindowsImeNativeMethods : IWindowsImeNative
{
    private const int CompositionCursorPosition = 0x0080;

    internal static WindowsImeNativeMethods Instance { get; } = new();

    private WindowsImeNativeMethods()
    {
    }

    IntPtr IWindowsImeNative.GetContext(IntPtr hwnd)
    {
        return Win32ImeNative.GetContext(hwnd);
    }

    bool IWindowsImeNative.ReleaseContext(IntPtr hwnd, IntPtr context)
    {
        return Win32ImeNative.ReleaseContext(hwnd, context);
    }

    int IWindowsImeNative.GetCompositionString(IntPtr context, int index, byte[] destination)
    {
        return Win32ImeNative.GetCompositionString(context, index, destination);
    }

    int IWindowsImeNative.GetCompositionCursorPosition(IntPtr context)
    {
        return Win32ImeNative.GetCompositionInteger(context, CompositionCursorPosition);
    }
}
