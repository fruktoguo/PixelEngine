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
        ? UiTextCompositionCapabilities.Supported("Windows IMM32 输入源可从 Win32 HWND 采集真实 IME composition 预编辑状态，并支持 UI caret/候选锚点回写 ImmSetCompositionWindow(CFS_POINT|CFS_RECT)/ImmSetCandidateWindow(CFS_CANDIDATEPOS|CFS_EXCLUDE)；真实窗口候选窗与产品体验仍归 M15 人工验收。")
        : UiTextCompositionCapabilities.Unsupported("当前平台不是 Windows，或未启用 Windows IMM32 composition reader；预编辑状态保持 inactive。");

    internal int CaptureTextComposition(Span<char> destination, out UiTextComposition composition)
    {
        composition = UiTextComposition.Inactive;
        if (!_enabled)
        {
            return 0;
        }

        if (!TryGetContext(out IntPtr hwnd, out IntPtr context))
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
            if (written == 0)
            {
                return 0;
            }

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
        catch (Exception)
        {
            composition = UiTextComposition.Inactive;
            return 0;
        }
        finally
        {
            ReleaseContextSafe(hwnd, context);
        }
    }

    /// <summary>
    /// 把 UI 坐标中的 caret/候选锚点写回 IMM32 composition/candidate 窗口（窗口 client 坐标）。
    /// </summary>
    /// <param name="geometry">定位几何；无有效信息时安全忽略。</param>
    internal void ApplyImeGeometry(in UiImeGeometry geometry)
    {
        if (!_enabled || !geometry.HasAny)
        {
            return;
        }

        if (!TryGetContext(out IntPtr hwnd, out IntPtr context))
        {
            return;
        }

        try
        {
            int caretLeft = 0;
            int caretTop = 0;
            bool hasCaretRect = geometry.HasCaretRect;
            bool hasExclude = geometry.TryGetExcludeRect(
                out float excludeX,
                out float excludeY,
                out float excludeWidth,
                out float excludeHeight);
            int excludeLeft = 0;
            int excludeTop = 0;
            int excludeRight = 0;
            int excludeBottom = 0;
            if (hasExclude)
            {
                excludeLeft = ToClientCoordinate(excludeX);
                excludeTop = ToClientCoordinate(excludeY);
                // 宽高至少 1 逻辑像素，避免 IMM32 忽略零面积矩形。
                int excludeW = Math.Max(1, ToClientCoordinate(excludeWidth));
                int excludeH = Math.Max(1, ToClientCoordinate(excludeHeight));
                excludeRight = excludeLeft + excludeW;
                excludeBottom = excludeTop + excludeH;
            }

            if (hasCaretRect)
            {
                caretLeft = ToClientCoordinate(geometry.CaretX);
                caretTop = ToClientCoordinate(geometry.CaretY);
                // CFS_POINT 给插入点；CFS_RECT 使用排除区（选区或 caret），便于 IME 贴近预编辑定位。
                Win32CompositionForm compositionForm = new()
                {
                    Style = Win32ImeNative.CompositionFormStylePoint | Win32ImeNative.CompositionFormStyleRect,
                    CurrentPos = new Win32Point
                    {
                        X = caretLeft,
                        Y = caretTop,
                    },
                    Area = hasExclude
                        ? new Win32Rect
                        {
                            Left = excludeLeft,
                            Top = excludeTop,
                            Right = excludeRight,
                            Bottom = excludeBottom,
                        }
                        : default,
                };
                _ = _native.SetCompositionWindow(context, in compositionForm);
            }

            if (geometry.HasCandidateAnchor)
            {
                // 有排除区时 CFS_EXCLUDE，避免候选窗盖住预编辑选区/插入点。
                int candidateStyle = Win32ImeNative.CandidateFormStyleCandidatePos;
                Win32Rect excludeArea = default;
                if (hasExclude)
                {
                    candidateStyle |= Win32ImeNative.CandidateFormStyleExclude;
                    excludeArea = new Win32Rect
                    {
                        Left = excludeLeft,
                        Top = excludeTop,
                        Right = excludeRight,
                        Bottom = excludeBottom,
                    };
                }

                Win32CandidateForm candidateForm = new()
                {
                    Index = 0,
                    Style = candidateStyle,
                    CurrentPos = new Win32Point
                    {
                        X = ToClientCoordinate(geometry.CandidateAnchorX),
                        Y = ToClientCoordinate(geometry.CandidateAnchorY),
                    },
                    Area = excludeArea,
                };
                _ = _native.SetCandidateWindow(context, in candidateForm);
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            ReleaseContextSafe(hwnd, context);
        }
    }

    private bool TryGetContext(out IntPtr hwnd, out IntPtr context)
    {
        hwnd = IntPtr.Zero;
        context = IntPtr.Zero;
        try
        {
            hwnd = _hwndProvider();
        }
        catch (Exception)
        {
            return false;
        }

        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            context = _native.GetContext(hwnd);
        }
        catch (Exception)
        {
            return false;
        }

        return context != IntPtr.Zero;
    }

    private void ReleaseContextSafe(IntPtr hwnd, IntPtr context)
    {
        try
        {
            _ = _native.ReleaseContext(hwnd, context);
        }
        catch (Exception)
        {
        }
    }

    private static int ToClientCoordinate(float value)
    {
        return float.IsFinite(value) ? (int)MathF.Round(value) : 0;
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

    bool SetCompositionWindow(IntPtr context, in Win32CompositionForm form);

    bool SetCandidateWindow(IntPtr context, in Win32CandidateForm form);
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

    bool IWindowsImeNative.SetCompositionWindow(IntPtr context, in Win32CompositionForm form)
    {
        return Win32ImeNative.SetCompositionWindow(context, in form);
    }

    bool IWindowsImeNative.SetCandidateWindow(IntPtr context, in Win32CandidateForm form)
    {
        return Win32ImeNative.SetCandidateWindow(context, in form);
    }
}
