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
}
