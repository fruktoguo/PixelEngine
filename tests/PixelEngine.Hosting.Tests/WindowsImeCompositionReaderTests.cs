using System.Text;
using PixelEngine.Interop;
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
    /// 验证窗口句柄提供器异常会退化为 inactive，避免窗口层异常冒泡到 UI 输入相位。
    /// </summary>
    [Fact]
    public void CaptureTextCompositionReturnsInactiveWhenHwndProviderThrows()
    {
        WindowsImeCompositionReader reader = new(
            () => throw new InvalidOperationException("window handle unavailable"),
            new FakeImeNative(),
            enableWindowsComposition: true);

        Span<char> buffer = stackalloc char[8];
        int count = reader.CaptureTextComposition(buffer, out UiTextComposition composition);

        Assert.Equal(0, count);
        Assert.False(composition.IsActive);
    }

    /// <summary>
    /// 验证 IMM32 读取异常会退化为 inactive，且已取得的 HIMC 仍会释放。
    /// </summary>
    [Fact]
    public void CaptureTextCompositionReturnsInactiveWhenImmReadThrowsAndReleasesContext()
    {
        FakeImeNative native = new()
        {
            Context = new IntPtr(0x456),
            Text = "拼音",
            ThrowOnGetCompositionString = true,
        };
        WindowsImeCompositionReader reader = new(() => new IntPtr(0x123), native, enableWindowsComposition: true);

        Span<char> buffer = stackalloc char[8];
        int count = reader.CaptureTextComposition(buffer, out UiTextComposition composition);

        Assert.Equal(0, count);
        Assert.False(composition.IsActive);
        Assert.Equal(1, native.GetContextCalls);
        Assert.Equal(1, native.ReleaseContextCalls);
    }

    /// <summary>
    /// 验证 ReleaseContext 异常不会污染已经成功读取的 composition 快照。
    /// </summary>
    [Fact]
    public void CaptureTextCompositionIgnoresReleaseContextFailureAfterSuccessfulRead()
    {
        FakeImeNative native = new()
        {
            Context = new IntPtr(0x456),
            Text = "拼音",
            Cursor = 1,
            ThrowOnReleaseContext = true,
        };
        WindowsImeCompositionReader reader = new(() => new IntPtr(0x123), native, enableWindowsComposition: true);

        Span<char> buffer = stackalloc char[8];
        int count = reader.CaptureTextComposition(buffer, out UiTextComposition composition);

        Assert.Equal(2, count);
        Assert.Equal("拼音", new string(buffer[..count]));
        Assert.True(composition.IsActive);
        Assert.Equal(1, composition.CursorIndex);
        Assert.Equal(1, native.ReleaseContextCalls);
    }

    /// <summary>
    /// 验证目标缓冲没有容量时不会把空文本冒充为 active composition。
    /// </summary>
    [Fact]
    public void CaptureTextCompositionReturnsInactiveWhenDestinationHasNoCapacity()
    {
        FakeImeNative native = new()
        {
            Context = new IntPtr(0x456),
            Text = "拼音",
            Cursor = 1,
        };
        WindowsImeCompositionReader reader = new(() => new IntPtr(0x123), native, enableWindowsComposition: true);

        int count = reader.CaptureTextComposition([], out UiTextComposition composition);

        Assert.Equal(0, count);
        Assert.False(composition.IsActive);
        Assert.Equal(1, native.ReleaseContextCalls);
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

    /// <summary>
    /// 验证 UI caret/候选锚点会回写到 IMM32 composition 与 candidate 窗口，而不是依赖 KeyChar。
    /// </summary>
    [Fact]
    public void ApplyImeGeometryWritesCompositionAndCandidateWindows()
    {
        FakeImeNative native = new() { Context = new IntPtr(0x456) };
        WindowsImeCompositionReader reader = new(() => new IntPtr(0x123), native, enableWindowsComposition: true);
        UiImeGeometry geometry = UiImeGeometry.FromCaretRect(120.4f, 340.6f, 2f, 18f);

        reader.ApplyImeGeometry(in geometry);

        Assert.Equal(1, native.GetContextCalls);
        Assert.Equal(1, native.ReleaseContextCalls);
        Assert.Equal(1, native.SetCompositionWindowCalls);
        Assert.Equal(1, native.SetCandidateWindowCalls);
        Assert.Equal(Win32ImeNative.CompositionFormStylePoint, native.LastCompositionForm.Style);
        Assert.Equal(120, native.LastCompositionForm.CurrentPos.X);
        Assert.Equal(341, native.LastCompositionForm.CurrentPos.Y);
        Assert.Equal(Win32ImeNative.CandidateFormStyleCandidatePos, native.LastCandidateForm.Style);
        Assert.Equal(120, native.LastCandidateForm.CurrentPos.X);
        Assert.Equal(359, native.LastCandidateForm.CurrentPos.Y);
    }

    /// <summary>
    /// 验证无几何信息、无 HWND 或禁用 Windows 时不调用 IMM32 定位 API。
    /// </summary>
    [Fact]
    public void ApplyImeGeometryIsNoOpWhenGeometryOrPlatformUnavailable()
    {
        FakeImeNative native = new() { Context = new IntPtr(0x456) };
        WindowsImeCompositionReader disabled = new(() => new IntPtr(0x123), native, enableWindowsComposition: false);
        WindowsImeCompositionReader noHwnd = new(() => IntPtr.Zero, native, enableWindowsComposition: true);
        WindowsImeCompositionReader active = new(() => new IntPtr(0x123), native, enableWindowsComposition: true);

        disabled.ApplyImeGeometry(UiImeGeometry.FromCaretRect(10, 20, 2, 18));
        noHwnd.ApplyImeGeometry(UiImeGeometry.FromCaretRect(10, 20, 2, 18));
        active.ApplyImeGeometry(UiImeGeometry.None);

        Assert.Equal(0, native.SetCompositionWindowCalls);
        Assert.Equal(0, native.SetCandidateWindowCalls);
    }

    /// <summary>
    /// 验证窗口输入源在写 IMM32 前把 framebuffer 坐标反变换为逻辑 client 坐标。
    /// </summary>
    [Fact]
    public void RenderWindowUiInputSourceConvertsFramebufferImeGeometryToLogicalClient()
    {
        UiImeGeometry framebuffer = UiImeGeometry.FromCaretRect(220f, 160f, 4f, 18f);

        UiImeGeometry logical = RenderWindowUiInputSource.ToLogicalClientGeometry(in framebuffer, 2f, 2f);

        Assert.True(logical.HasCaretRect);
        Assert.Equal(110f, logical.CaretX, precision: 3);
        Assert.Equal(80f, logical.CaretY, precision: 3);
        Assert.Equal(2f, logical.CaretWidth, precision: 3);
        Assert.Equal(9f, logical.CaretHeight, precision: 3);
        Assert.Equal(110f, logical.CandidateAnchorX, precision: 3);
        Assert.Equal(89f, logical.CandidateAnchorY, precision: 3);

        UiImeGeometry identity = RenderWindowUiInputSource.ToLogicalClientGeometry(in framebuffer, 1f, 1f);
        Assert.Equal(220f, identity.CaretX, precision: 3);
        Assert.Equal(160f, identity.CaretY, precision: 3);

        Assert.False(RenderWindowUiInputSource.ToLogicalClientGeometry(UiImeGeometry.None, 2f, 2f).HasAny);
    }

    private sealed class FakeImeNative : IWindowsImeNative
    {
        private const int CompositionAttribute = 0x0010;
        private const int CompositionString = 0x0008;

        public IntPtr Context { get; init; }

        public string Text { get; init; } = string.Empty;

        public byte[] Attributes { get; init; } = [];

        public int Cursor { get; init; }

        public bool ThrowOnGetCompositionString { get; init; }

        public bool ThrowOnReleaseContext { get; init; }

        public int GetContextCalls { get; private set; }

        public int ReleaseContextCalls { get; private set; }

        public int GetCompositionStringCalls { get; private set; }

        public int SetCompositionWindowCalls { get; private set; }

        public int SetCandidateWindowCalls { get; private set; }

        public Win32CompositionForm LastCompositionForm { get; private set; }

        public Win32CandidateForm LastCandidateForm { get; private set; }

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
            _ = ThrowOnReleaseContext ? throw new InvalidOperationException("release failed") : false;

            return true;
        }

        public int GetCompositionString(IntPtr context, int index, byte[] destination)
        {
            Assert.Equal(Context, context);
            GetCompositionStringCalls++;
            _ = ThrowOnGetCompositionString ? throw new InvalidOperationException("composition read failed") : false;

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

        public bool SetCompositionWindow(IntPtr context, in Win32CompositionForm form)
        {
            Assert.Equal(Context, context);
            SetCompositionWindowCalls++;
            LastCompositionForm = form;
            return true;
        }

        public bool SetCandidateWindow(IntPtr context, in Win32CandidateForm form)
        {
            Assert.Equal(Context, context);
            SetCandidateWindowCalls++;
            LastCandidateForm = form;
            return true;
        }
    }
}
