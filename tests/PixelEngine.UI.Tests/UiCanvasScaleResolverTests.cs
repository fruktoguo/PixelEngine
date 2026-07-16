using PixelEngine.Gui;
using Xunit;

namespace PixelEngine.UI.Tests;

/// <summary>
/// CanvasScaler 三模式、坐标变换与后端统一度量测试。
/// </summary>
public sealed class UiCanvasScaleResolverTests
{
    /// <summary>
    /// 默认值严格对齐设计中的 Unity-compatible 基线。
    /// </summary>
    [Fact]
    public void DefaultSettingsUseUnityCompatibleValues()
    {
        UiCanvasScalerSettings settings = UiCanvasScalerSettings.Default;

        Assert.Equal(UiScaleMode.ConstantPixelSize, settings.ScaleMode);
        Assert.Equal(1f, settings.ScaleFactor);
        Assert.Equal(800f, settings.ReferenceWidth);
        Assert.Equal(600f, settings.ReferenceHeight);
        Assert.Equal(UiScreenMatchMode.MatchWidthOrHeight, settings.ScreenMatchMode);
        Assert.Equal(0f, settings.MatchWidthOrHeight);
        Assert.Equal(UiPhysicalUnit.Points, settings.PhysicalUnit);
        Assert.Equal(96f, settings.FallbackScreenDpi);
        Assert.Equal(96f, settings.DefaultSpriteDpi);
        Assert.Equal(100f, settings.ReferencePixelsPerUnit);
    }

    /// <summary>
    /// Constant Pixel Size 使用固定 scale，并提供严格双向坐标与 IME 变换。
    /// </summary>
    [Fact]
    public void ConstantPixelSizeResolvesLogicalCanvasAndBidirectionalMapping()
    {
        UiCanvasScalerSettings settings = UiCanvasScalerSettings.Default with { ScaleFactor = 2f };
        UiDisplayMetrics display = Display(1920, 1080, actualPhysicalDpi: 144f, revision: 7);

        UiCanvasMetrics metrics = UiCanvasScaleResolver.Resolve(in settings, in display);

        Assert.Equal(2f, metrics.ScaleFactor);
        Assert.Equal(960f, metrics.LogicalWidth);
        Assert.Equal(540f, metrics.LogicalHeight);
        Assert.Equal(100f, metrics.ResolvedReferencePixelsPerUnit);
        Assert.Equal(7, metrics.DisplayMetricsRevision);
        Assert.True(metrics.TryMapPresentationToLogical(400f, 300f, out float logicalX, out float logicalY));
        Assert.Equal(200f, logicalX);
        Assert.Equal(150f, logicalY);
        Assert.Equal((400f, 300f), metrics.MapLogicalToPresentation(logicalX, logicalY));
        Assert.False(metrics.TryMapPresentationToLogical(1920f, 20f, out _, out _));

        UiImeGeometry ime = metrics.MapLogicalImeGeometryToPresentation(
            UiImeGeometry.FromCaretRect(10f, 20f, 2f, 8f));
        Assert.Equal((20f, 40f, 4f, 16f), (ime.CaretX, ime.CaretY, ime.CaretWidth, ime.CaretHeight));
    }

    /// <summary>
    /// Match Width Or Height 使用对数插值；Expand/Shrink 分别选择 min/max。
    /// </summary>
    [Fact]
    public void ScaleWithScreenSizeImplementsAllScreenMatchModes()
    {
        UiDisplayMetrics display = Display(1600, 900);
        UiCanvasScalerSettings baseSettings = UiCanvasScalerSettings.Default with
        {
            ScaleMode = UiScaleMode.ScaleWithScreenSize,
            ReferenceWidth = 800f,
            ReferenceHeight = 600f,
        };

        UiCanvasScalerSettings match = baseSettings with { MatchWidthOrHeight = 0.5f };
        UiCanvasMetrics matchMetrics = UiCanvasScaleResolver.Resolve(in match, in display);
        UiCanvasScalerSettings expand = baseSettings with { ScreenMatchMode = UiScreenMatchMode.Expand };
        UiCanvasMetrics expandMetrics = UiCanvasScaleResolver.Resolve(in expand, in display);
        UiCanvasScalerSettings shrink = baseSettings with { ScreenMatchMode = UiScreenMatchMode.Shrink };
        UiCanvasMetrics shrinkMetrics = UiCanvasScaleResolver.Resolve(in shrink, in display);

        Assert.Equal(MathF.Sqrt(3f), matchMetrics.ScaleFactor, 5);
        Assert.Equal(1.5f, expandMetrics.ScaleFactor);
        Assert.Equal(2f, shrinkMetrics.ScaleFactor);
    }

    /// <summary>
    /// Constant Physical Size 优先 raw DPI，缺失时只使用入盘 fallback，并按物理单位解析 PPU。
    /// </summary>
    [Theory]
    [InlineData(UiPhysicalUnit.Inches, 1.0, 96.0, 1.0416666)]
    [InlineData(UiPhysicalUnit.Centimeters, 2.54, 37.795275, 2.6458333)]
    [InlineData(UiPhysicalUnit.Millimeters, 25.4, 3.7795275, 26.458334)]
    [InlineData(UiPhysicalUnit.Points, 72.0, 1.3333333, 75.0)]
    [InlineData(UiPhysicalUnit.Picas, 6.0, 16.0, 6.25)]
    public void ConstantPhysicalSizeSupportsEveryPhysicalUnit(
        UiPhysicalUnit unit,
        double unitsPerInch,
        double expectedScale,
        double expectedPixelsPerUnit)
    {
        UiCanvasScalerSettings settings = UiCanvasScalerSettings.Default with
        {
            ScaleMode = UiScaleMode.ConstantPhysicalSize,
            PhysicalUnit = unit,
        };
        UiDisplayMetrics display = Display(1280, 720, actualPhysicalDpi: 96f);

        UiCanvasMetrics actual = UiCanvasScaleResolver.Resolve(in settings, in display);

        Assert.Equal((float)expectedScale, actual.ScaleFactor, 5);
        Assert.Equal((float)expectedPixelsPerUnit, actual.ResolvedReferencePixelsPerUnit, 5);
        Assert.Equal(96f / (float)unitsPerInch, actual.ScaleFactor, 5);
        Assert.False(actual.UsedFallbackPhysicalDpi);
        Assert.Equal(96f, actual.EffectivePhysicalDpi);
    }

    /// <summary>
    /// raw physical DPI 未知时使用明确 fallback，而不是 framebuffer scale 推导值。
    /// </summary>
    [Fact]
    public void ConstantPhysicalSizeUsesExplicitFallbackWhenRawDpiIsUnknown()
    {
        UiCanvasScalerSettings settings = UiCanvasScalerSettings.Default with
        {
            ScaleMode = UiScaleMode.ConstantPhysicalSize,
            PhysicalUnit = UiPhysicalUnit.Points,
            FallbackScreenDpi = 120f,
        };
        UiDisplayMetrics display = new(800, 600, 2f, 2f, null, 9, 4);

        UiCanvasMetrics actual = UiCanvasScaleResolver.Resolve(in settings, in display);

        Assert.Equal(120f / 72f, actual.ScaleFactor, 5);
        Assert.True(actual.UsedFallbackPhysicalDpi);
        Assert.Equal(120f, actual.EffectivePhysicalDpi);
    }

    /// <summary>
    /// 所有分辨率、DPI、scale 与 PPU 非法值都被明确拒绝。
    /// </summary>
    [Fact]
    public void ResolverRejectsInvalidAndNonFiniteSettingsAndDisplayMetrics()
    {
        UiDisplayMetrics display = Display(800, 600);
        UiCanvasScalerSettings defaults = UiCanvasScalerSettings.Default;
        UiCanvasScalerSettings[] invalidSettings =
        [
            default,
            defaults with { ScaleFactor = 0f },
            defaults with { ScaleFactor = float.NaN },
            defaults with { ReferenceWidth = float.PositiveInfinity },
            defaults with { ReferenceHeight = -1f },
            defaults with { MatchWidthOrHeight = 1.1f },
            defaults with { FallbackScreenDpi = 0f },
            defaults with { DefaultSpriteDpi = float.NegativeInfinity },
            defaults with { ReferencePixelsPerUnit = float.NaN },
        ];
        foreach (UiCanvasScalerSettings invalid in invalidSettings)
        {
            UiCanvasScalerSettings captured = invalid;
            _ = Assert.Throws<ArgumentOutOfRangeException>(
                () => UiCanvasScaleResolver.Resolve(in captured, in display));
        }

        UiDisplayMetrics[] invalidDisplays =
        [
            display with { PresentationWidth = 0 },
            display with { PresentationHeight = -1 },
            display with { FramebufferScaleX = float.NaN },
            display with { FramebufferScaleY = 0f },
            display with { ActualPhysicalDpi = float.PositiveInfinity },
            display with { MetricsRevision = -1 },
        ];
        foreach (UiDisplayMetrics invalid in invalidDisplays)
        {
            UiDisplayMetrics captured = invalid;
            _ = Assert.Throws<ArgumentOutOfRangeException>(
                () => UiCanvasScaleResolver.Resolve(in defaults, in captured));
        }
    }

    /// <summary>
    /// ManagedFallback 保存 Resolver 产出的 logical/render 分层度量，而不是退回物理 viewport layout。
    /// </summary>
    [Fact]
    public void ManagedFallbackConsumesResolvedCanvasMetrics()
    {
        UiCanvasScalerSettings settings = UiCanvasScalerSettings.Default with { ScaleFactor = 2f };
        UiDisplayMetrics display = Display(1280, 720);
        UiBackendInitializeInfo info = new(display, settings, UiBackendKind.ManagedFallback);
        using ManagedFallbackBackend backend = new(new NoOpGuiHost());

        backend.Initialize(in info);

        Assert.Equal(1280, backend.DebugCanvasMetrics.PresentationWidth);
        Assert.Equal(720, backend.DebugCanvasMetrics.PresentationHeight);
        Assert.Equal(640f, backend.DebugCanvasMetrics.LogicalWidth);
        Assert.Equal(360f, backend.DebugCanvasMetrics.LogicalHeight);
        Assert.Equal(2f, backend.DebugCanvasMetrics.ScaleFactor);
    }

    private static UiDisplayMetrics Display(
        int width,
        int height,
        float? actualPhysicalDpi = null,
        long revision = 1)
    {
        return new UiDisplayMetrics(width, height, 1f, 1f, actualPhysicalDpi, 1, revision);
    }

    private sealed class NoOpGuiHost : IManagedFallbackGuiHost
    {
        public bool IsRunning => true;

        public void Initialize()
        {
        }

        public void DrawFrame(float deltaSeconds, int width, int height, Action<IGuiDrawContext> drawGui)
        {
            _ = deltaSeconds;
            _ = width;
            _ = height;
            _ = drawGui;
        }

        public ManagedFallbackImage LoadImage(string path)
        {
            _ = path;
            return new ManagedFallbackImage(1, 1, 1);
        }

        public void FeedPointerMove(float x, float y)
        {
            _ = x;
            _ = y;
        }

        public void FeedPointerButton(UiPointerButton button, bool isDown)
        {
            _ = button;
            _ = isDown;
        }

        public void FeedScroll(float deltaX, float deltaY)
        {
            _ = deltaX;
            _ = deltaY;
        }

        public void FeedKey(UiKey key, bool isDown, UiKeyModifiers modifiers)
        {
            _ = key;
            _ = isDown;
            _ = modifiers;
        }

        public void FeedText(ReadOnlySpan<char> text)
        {
            _ = text;
        }
    }
}
