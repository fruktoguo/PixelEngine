using System.Text;
using PixelEngine.UI;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Windows IME composition reader 的边界测试。
/// </summary>
public sealed class WindowsImeCompositionReaderTests
{
    /// <summary>
    /// 验证 reader 会从 IMM32 抽取预编辑文本和光标位置，而不是混用 committed text 通道。
    /// </summary>
    [Fact]
    public void CaptureTextCompositionReadsPreeditTextAndCursorFromImm()
    {
        FakeImeNative native = new()
        {
            Context = new IntPtr(0x456),
            Text = "拼音",
            Cursor = 1,
        };
        WindowsImeCompositionReader reader = new(() => new IntPtr(0x123), native, enableWindowsComposition: true);

        Span<char> buffer = stackalloc char[8];
        int count = reader.CaptureTextComposition(buffer, out UiTextComposition composition);

        Assert.True(reader.Capabilities.SupportsPlatformComposition);
        Assert.Equal(2, count);
        Assert.Equal("拼音", new string(buffer[..count]));
        Assert.True(composition.IsActive);
        Assert.Equal(1, composition.CursorIndex);
        Assert.Equal(1, native.GetContextCalls);
        Assert.Equal(1, native.ReleaseContextCalls);
        Assert.Equal(2, native.GetCompositionStringCalls);
    }

    /// <summary>
    /// 验证无 HWND、无 HIMC 或非 Windows gate 时都会显式返回 inactive。
    /// </summary>
    [Fact]
    public void CaptureTextCompositionReturnsInactiveWhenPlatformOrContextUnavailable()
    {
        FakeImeNative native = new() { Context = new IntPtr(0x456), Text = "拼音", Cursor = 1 };
        WindowsImeCompositionReader disabled = new(() => new IntPtr(0x123), native, enableWindowsComposition: false);
        WindowsImeCompositionReader noHwnd = new(() => IntPtr.Zero, native, enableWindowsComposition: true);
        WindowsImeCompositionReader noContext = new(() => new IntPtr(0x123), new FakeImeNative(), enableWindowsComposition: true);

        Span<char> buffer = stackalloc char[8];

        Assert.False(disabled.Capabilities.SupportsPlatformComposition);
        Assert.Equal(0, disabled.CaptureTextComposition(buffer, out UiTextComposition disabledComposition));
        Assert.False(disabledComposition.IsActive);
        Assert.Equal(0, noHwnd.CaptureTextComposition(buffer, out UiTextComposition noHwndComposition));
        Assert.False(noHwndComposition.IsActive);
        Assert.Equal(0, noContext.CaptureTextComposition(buffer, out UiTextComposition noContextComposition));
        Assert.False(noContextComposition.IsActive);
    }

    /// <summary>
    /// 验证光标位置按实际写入缓冲长度夹取，避免后端看到越界 composition 范围。
    /// </summary>
    [Fact]
    public void CaptureTextCompositionClampsCursorToWrittenTextLength()
    {
        FakeImeNative native = new()
        {
            Context = new IntPtr(0x456),
            Text = "composition",
            Cursor = 99,
        };
        WindowsImeCompositionReader reader = new(() => new IntPtr(0x123), native, enableWindowsComposition: true);

        Span<char> buffer = stackalloc char[4];
        int count = reader.CaptureTextComposition(buffer, out UiTextComposition composition);

        Assert.Equal(4, count);
        Assert.Equal("comp", new string(buffer));
        Assert.True(composition.IsActive);
        Assert.Equal(4, composition.CursorIndex);
    }

    /// <summary>
    /// 验证 IMM32 target converted / target not-converted 属性会映射为预编辑选区。
    /// </summary>
    [Fact]
    public void CaptureTextCompositionMapsImmTargetAttributesToSelection()
    {
        FakeImeNative native = new()
        {
            Context = new IntPtr(0x456),
            Text = "かな候補",
            Cursor = 4,
            Attributes = [0, 1, 1, 3],
        };
        WindowsImeCompositionReader reader = new(() => new IntPtr(0x123), native, enableWindowsComposition: true);

        Span<char> buffer = stackalloc char[8];
        int count = reader.CaptureTextComposition(buffer, out UiTextComposition composition);

        Assert.Equal(4, count);
        Assert.True(composition.IsActive);
        Assert.Equal(4, composition.CursorIndex);
        Assert.Equal(1, composition.SelectionStart);
        Assert.Equal(3, composition.SelectionLength);
    }

    /// <summary>
    /// 验证属性长度超过写入文本长度时按真实写入字符数裁剪。
    /// </summary>
    [Fact]
    public void CaptureTextCompositionClampsSelectionToWrittenTextLength()
    {
        FakeImeNative native = new()
        {
            Context = new IntPtr(0x456),
            Text = "abcdef",
            Cursor = 6,
            Attributes = [0, 0, 1, 1, 1, 1],
        };
        WindowsImeCompositionReader reader = new(() => new IntPtr(0x123), native, enableWindowsComposition: true);

        Span<char> buffer = stackalloc char[4];
        int count = reader.CaptureTextComposition(buffer, out UiTextComposition composition);

        Assert.Equal(4, count);
        Assert.Equal("abcd", new string(buffer));
        Assert.True(composition.IsActive);
        Assert.Equal(4, composition.CursorIndex);
        Assert.Equal(2, composition.SelectionStart);
        Assert.Equal(2, composition.SelectionLength);
    }

    private sealed class FakeImeNative : IWindowsImeNative
    {
        private const int CompositionAttribute = 0x0010;
        private const int CompositionString = 0x0008;

        public IntPtr Context { get; init; }

        public string Text { get; init; } = string.Empty;

        public byte[] Attributes { get; init; } = [];

        public int Cursor { get; init; }

        public int GetContextCalls { get; private set; }

        public int ReleaseContextCalls { get; private set; }

        public int GetCompositionStringCalls { get; private set; }

        public IntPtr GetContext(IntPtr hwnd)
        {
            Assert.NotEqual(IntPtr.Zero, hwnd);
            GetContextCalls++;
            return Context;
        }

        public bool ReleaseContext(IntPtr hwnd, IntPtr context)
        {
            Assert.NotEqual(IntPtr.Zero, hwnd);
            Assert.Equal(Context, context);
            ReleaseContextCalls++;
            return true;
        }

        public int GetCompositionString(IntPtr context, int index, byte[] destination)
        {
            Assert.Equal(Context, context);
            GetCompositionStringCalls++;
            if (index == CompositionAttribute)
            {
                Array.Copy(Attributes, destination, Math.Min(Attributes.Length, destination.Length));
                return Attributes.Length;
            }

            Assert.Equal(CompositionString, index);
            byte[] bytes = Encoding.Unicode.GetBytes(Text);
            Array.Copy(bytes, destination, Math.Min(bytes.Length, destination.Length));
            return bytes.Length;
        }

        public int GetCompositionCursorPosition(IntPtr context)
        {
            Assert.Equal(Context, context);
            return Cursor;
        }
    }
}
