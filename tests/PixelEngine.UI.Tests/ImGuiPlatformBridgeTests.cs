using Hexa.NET.ImGui;
using PixelEngine.Gui;
using PixelEngine.Interop;
using Silk.NET.Input;
using System.Numerics;
using Xunit;

namespace PixelEngine.UI.Tests;

/// <summary>
/// Dear ImGui 平台桥的纯托管契约测试。
/// </summary>
public sealed class ImGuiPlatformBridgeTests
{
    /// <summary>
    /// 验证全部公开 ImGui 光标语义都映射到对应系统光标。
    /// </summary>
    [Theory]
    [InlineData(ImGuiMouseCursor.None, StandardCursor.Arrow)]
    [InlineData(ImGuiMouseCursor.Arrow, StandardCursor.Arrow)]
    [InlineData(ImGuiMouseCursor.TextInput, StandardCursor.IBeam)]
    [InlineData(ImGuiMouseCursor.ResizeAll, StandardCursor.ResizeAll)]
    [InlineData(ImGuiMouseCursor.ResizeNs, StandardCursor.VResize)]
    [InlineData(ImGuiMouseCursor.ResizeEw, StandardCursor.HResize)]
    [InlineData(ImGuiMouseCursor.ResizeNesw, StandardCursor.NeswResize)]
    [InlineData(ImGuiMouseCursor.ResizeNwse, StandardCursor.NwseResize)]
    [InlineData(ImGuiMouseCursor.Hand, StandardCursor.Hand)]
    [InlineData(ImGuiMouseCursor.Wait, StandardCursor.Wait)]
    [InlineData(ImGuiMouseCursor.Progress, StandardCursor.WaitArrow)]
    [InlineData(ImGuiMouseCursor.NotAllowed, StandardCursor.NotAllowed)]
    public void MapCursorPreservesPlatformMeaning(ImGuiMouseCursor input, StandardCursor expected)
    {
        Assert.Equal(expected, ImGuiPlatformBridge.MapCursor(input));
    }

    /// <summary>
    /// 验证 Windows IME composition 与候选窗以 ImGui caret 的 client 坐标为锚点。
    /// </summary>
    [Fact]
    public void TryCreateImeFormsAnchorsCandidateBelowCaret()
    {
        bool created = ImGuiPlatformBridge.TryCreateImeForms(
            new Vector2(120.4f, 80.6f),
            18.2f,
            out Win32CompositionForm composition,
            out Win32CandidateForm candidate);

        Assert.True(created);
        Assert.Equal(Win32ImeNative.CompositionFormStyleForcePosition, composition.Style);
        Assert.Equal(120, composition.CurrentPos.X);
        Assert.Equal(81, composition.CurrentPos.Y);
        Assert.Equal(
            Win32ImeNative.CandidateFormStyleCandidatePos | Win32ImeNative.CandidateFormStyleExclude,
            candidate.Style);
        Assert.Equal(120, candidate.CurrentPos.X);
        Assert.Equal(99, candidate.CurrentPos.Y);
        Assert.Equal(120, candidate.Area.Left);
        Assert.Equal(81, candidate.Area.Top);
        Assert.Equal(121, candidate.Area.Right);
        Assert.Equal(99, candidate.Area.Bottom);
    }

    /// <summary>
    /// 验证非有限 caret 坐标不会流入 Win32 IMM32。
    /// </summary>
    [Fact]
    public void TryCreateImeFormsRejectsNonFiniteCaret()
    {
        bool created = ImGuiPlatformBridge.TryCreateImeForms(
            new Vector2(float.NaN, 10f),
            18f,
            out _,
            out _);

        Assert.False(created);
    }

    /// <summary>
    /// 验证文本输入请求只在聚合状态转换点解除/恢复原 HIMC，caret 移动只更新候选窗位置。
    /// </summary>
    [Fact]
    public void ImeContextLifecycleRestoresExactContextOnlyOnTextRequestTransitions()
    {
        IntPtr hwnd = new(0x123);
        IntPtr originalContext = new(0x456);
        FakeWindowsImeContextNative native = new(originalContext);
        WindowsImeContextRegistry registry = new(native, enabled: true);
        WindowsImeContextController controller = new(registry);
        Win32CompositionForm composition = new()
        {
            Style = Win32ImeNative.CompositionFormStyleForcePosition,
            CurrentPos = new Win32Point { X = 40, Y = 60 },
        };
        Win32CandidateForm candidate = new()
        {
            Style = Win32ImeNative.CandidateFormStyleCandidatePos,
            CurrentPos = new Win32Point { X = 40, Y = 78 },
        };

        controller.Attach(hwnd);

        Assert.Equal(IntPtr.Zero, native.AssociatedContext);
        Assert.Equal(new[] { "get", "cancel", "close", "release", "associate:0" }, native.Events);

        controller.UpdateRequest(
            wantsTextInput: true,
            visible: true,
            hasForms: true,
            in composition,
            in candidate);

        Assert.Equal(originalContext, native.AssociatedContext);
        Assert.Equal(2, native.AssociateContextCalls);
        Assert.Equal(1, native.SetCompositionWindowCalls);
        Assert.Equal(1, native.SetCandidateWindowCalls);

        composition.CurrentPos = new Win32Point { X = 80, Y = 100 };
        controller.UpdateRequest(
            wantsTextInput: true,
            visible: true,
            hasForms: true,
            in composition,
            in candidate);

        Assert.Equal(2, native.AssociateContextCalls);
        Assert.Equal(2, native.SetCompositionWindowCalls);
        Assert.Equal(80, native.LastCompositionForm.CurrentPos.X);

        Win32CompositionForm noComposition = default;
        Win32CandidateForm noCandidate = default;
        controller.UpdateRequest(
            wantsTextInput: false,
            visible: false,
            hasForms: false,
            in noComposition,
            in noCandidate);

        Assert.Equal(IntPtr.Zero, native.AssociatedContext);
        Assert.Equal(3, native.AssociateContextCalls);
        Assert.Equal(2, native.CancelCompositionCalls);
        Assert.Equal(2, native.CloseCandidateCalls);

        controller.Detach();

        Assert.Equal(originalContext, native.AssociatedContext);
        Assert.Equal(4, native.AssociateContextCalls);
    }

    /// <summary>
    /// 验证窗口失焦立即暂挂 IME，重新聚焦只在文本项仍请求输入时恢复并重放 caret 几何。
    /// </summary>
    [Fact]
    public void ImeContextLifecycleFollowsWindowFocusWithoutChangingRequestedTextState()
    {
        IntPtr originalContext = new(0x456);
        FakeWindowsImeContextNative native = new(originalContext);
        WindowsImeContextRegistry registry = new(native, enabled: true);
        WindowsImeContextController controller = new(registry);
        Win32CompositionForm composition = new()
        {
            Style = Win32ImeNative.CompositionFormStyleForcePosition,
            CurrentPos = new Win32Point { X = 10, Y = 20 },
        };
        Win32CandidateForm candidate = new()
        {
            Style = Win32ImeNative.CandidateFormStyleCandidatePos,
            CurrentPos = new Win32Point { X = 10, Y = 38 },
        };

        controller.Attach(new IntPtr(0x123));
        controller.UpdateRequest(
            wantsTextInput: true,
            visible: true,
            hasForms: true,
            in composition,
            in candidate);
        controller.SetFocused(focused: false);

        Assert.Equal(IntPtr.Zero, native.AssociatedContext);
        Assert.Equal(3, native.AssociateContextCalls);

        controller.SetFocused(focused: true);

        Assert.Equal(originalContext, native.AssociatedContext);
        Assert.Equal(4, native.AssociateContextCalls);
        Assert.Equal(2, native.SetCompositionWindowCalls);
        Assert.Equal(2, native.SetCandidateWindowCalls);
    }

    /// <summary>
    /// 验证同一 HWND 的空闲 ImGui context 不能解除另一个活跃 context 已恢复的 HIMC。
    /// </summary>
    [Fact]
    public void ImeContextRegistryArbitratesMultipleImGuiContextsPerWindow()
    {
        IntPtr hwnd = new(0x123);
        IntPtr originalContext = new(0x456);
        FakeWindowsImeContextNative native = new(originalContext);
        WindowsImeContextRegistry registry = new(native, enabled: true);
        WindowsImeContextController editor = new(registry);
        WindowsImeContextController gameUi = new(registry);
        Win32CompositionForm editorComposition = new()
        {
            Style = Win32ImeNative.CompositionFormStyleForcePosition,
            CurrentPos = new Win32Point { X = 10, Y = 20 },
        };
        Win32CandidateForm editorCandidate = new()
        {
            Style = Win32ImeNative.CandidateFormStyleCandidatePos,
            CurrentPos = new Win32Point { X = 10, Y = 38 },
        };
        Win32CompositionForm gameComposition = new()
        {
            Style = Win32ImeNative.CompositionFormStyleForcePosition,
            CurrentPos = new Win32Point { X = 200, Y = 220 },
        };
        Win32CandidateForm gameCandidate = new()
        {
            Style = Win32ImeNative.CandidateFormStyleCandidatePos,
            CurrentPos = new Win32Point { X = 200, Y = 238 },
        };

        editor.Attach(hwnd);
        gameUi.Attach(hwnd);

        Assert.Equal(1, native.AssociateContextCalls);
        Assert.Equal(IntPtr.Zero, native.AssociatedContext);

        editor.UpdateRequest(
            wantsTextInput: true,
            visible: true,
            hasForms: true,
            in editorComposition,
            in editorCandidate);
        gameUi.UpdateRequest(
            wantsTextInput: false,
            visible: false,
            hasForms: false,
            in gameComposition,
            in gameCandidate);

        Assert.Equal(originalContext, native.AssociatedContext);
        Assert.Equal(2, native.AssociateContextCalls);
        Assert.Equal(10, native.LastCompositionForm.CurrentPos.X);

        gameUi.UpdateRequest(
            wantsTextInput: true,
            visible: true,
            hasForms: true,
            in gameComposition,
            in gameCandidate);

        Assert.Equal(originalContext, native.AssociatedContext);
        Assert.Equal(2, native.AssociateContextCalls);
        Assert.Equal(200, native.LastCompositionForm.CurrentPos.X);

        gameUi.UpdateRequest(
            wantsTextInput: false,
            visible: false,
            hasForms: false,
            in gameComposition,
            in gameCandidate);

        Assert.Equal(originalContext, native.AssociatedContext);
        Assert.Equal(2, native.AssociateContextCalls);
        Assert.Equal(10, native.LastCompositionForm.CurrentPos.X);

        editor.UpdateRequest(
            wantsTextInput: false,
            visible: false,
            hasForms: false,
            in editorComposition,
            in editorCandidate);

        Assert.Equal(IntPtr.Zero, native.AssociatedContext);
        Assert.Equal(3, native.AssociateContextCalls);

        editor.Detach();
        gameUi.Detach();

        Assert.Equal(originalContext, native.AssociatedContext);
        Assert.Equal(4, native.AssociateContextCalls);
    }

    private sealed class FakeWindowsImeContextNative(IntPtr initialContext) : IWindowsImeContextNative
    {
        public List<string> Events { get; } = [];

        public IntPtr AssociatedContext { get; private set; } = initialContext;

        public int AssociateContextCalls { get; private set; }

        public int CancelCompositionCalls { get; private set; }

        public int CloseCandidateCalls { get; private set; }

        public int SetCompositionWindowCalls { get; private set; }

        public int SetCandidateWindowCalls { get; private set; }

        public Win32CompositionForm LastCompositionForm { get; private set; }

        public IntPtr GetContext(IntPtr hwnd)
        {
            Assert.NotEqual(IntPtr.Zero, hwnd);
            Events.Add("get");
            return AssociatedContext;
        }

        public bool ReleaseContext(IntPtr hwnd, IntPtr context)
        {
            Assert.NotEqual(IntPtr.Zero, hwnd);
            Assert.Equal(AssociatedContext, context);
            Events.Add("release");
            return true;
        }

        public IntPtr AssociateContext(IntPtr hwnd, IntPtr context)
        {
            Assert.NotEqual(IntPtr.Zero, hwnd);
            IntPtr previous = AssociatedContext;
            AssociatedContext = context;
            AssociateContextCalls++;
            Events.Add($"associate:{context.ToInt64()}");
            return previous;
        }

        public bool CancelComposition(IntPtr context)
        {
            Assert.Equal(AssociatedContext, context);
            CancelCompositionCalls++;
            Events.Add("cancel");
            return true;
        }

        public bool CloseCandidate(IntPtr context)
        {
            Assert.Equal(AssociatedContext, context);
            CloseCandidateCalls++;
            Events.Add("close");
            return true;
        }

        public bool SetCompositionWindow(IntPtr context, in Win32CompositionForm form)
        {
            Assert.Equal(AssociatedContext, context);
            SetCompositionWindowCalls++;
            LastCompositionForm = form;
            return true;
        }

        public bool SetCandidateWindow(IntPtr context, in Win32CandidateForm form)
        {
            Assert.Equal(AssociatedContext, context);
            SetCandidateWindowCalls++;
            return true;
        }
    }
}
