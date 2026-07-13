namespace PixelEngine.UI;

/// <summary>
/// 将 CanvasScaler 入盘设置与当前显示度量解析为唯一 <see cref="UiCanvasMetrics" /> 的纯函数。
/// </summary>
public static class UiCanvasScaleResolver
{
    /// <summary>
    /// 解析 CanvasScaler。非法、非有限或非正的配置会被明确拒绝，不做设备猜测。
    /// </summary>
    /// <param name="settings">CanvasScaler 入盘设置。</param>
    /// <param name="displayMetrics">当前 presentation 与显示器度量。</param>
    /// <returns>layout、raster 与 input 共用的解析结果。</returns>
    public static UiCanvasMetrics Resolve(
        in UiCanvasScalerSettings settings,
        in UiDisplayMetrics displayMetrics)
    {
        displayMetrics.Validate();
        ValidateSettings(in settings);

        double scale;
        double resolvedReferencePixelsPerUnit = settings.ReferencePixelsPerUnit;
        float effectivePhysicalDpi = 0f;
        bool usedFallbackPhysicalDpi = false;
        switch (settings.ScaleMode)
        {
            case UiScaleMode.ConstantPixelSize:
                scale = settings.ScaleFactor;
                break;
            case UiScaleMode.ScaleWithScreenSize:
                scale = ResolveScreenScale(in settings, in displayMetrics);
                break;
            case UiScaleMode.ConstantPhysicalSize:
                double unitsPerInch = GetUnitsPerInch(settings.PhysicalUnit);
                effectivePhysicalDpi = displayMetrics.ActualPhysicalDpi ?? settings.FallbackScreenDpi;
                usedFallbackPhysicalDpi = displayMetrics.ActualPhysicalDpi is null;
                scale = effectivePhysicalDpi / unitsPerInch;
                resolvedReferencePixelsPerUnit =
                    settings.ReferencePixelsPerUnit * unitsPerInch / settings.DefaultSpriteDpi;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(settings), settings.ScaleMode, "未知 UI scale mode。");
        }

        float resolvedScale = ToFinitePositiveFloat(scale, nameof(settings.ScaleFactor));
        float logicalWidth = ToFinitePositiveFloat(
            displayMetrics.PresentationWidth / scale,
            nameof(displayMetrics.PresentationWidth));
        float logicalHeight = ToFinitePositiveFloat(
            displayMetrics.PresentationHeight / scale,
            nameof(displayMetrics.PresentationHeight));
        float resolvedPixelsPerUnit = ToFinitePositiveFloat(
            resolvedReferencePixelsPerUnit,
            nameof(settings.ReferencePixelsPerUnit));
        return new UiCanvasMetrics(
            displayMetrics.PresentationWidth,
            displayMetrics.PresentationHeight,
            logicalWidth,
            logicalHeight,
            resolvedScale,
            resolvedPixelsPerUnit,
            displayMetrics.FramebufferScaleX,
            displayMetrics.FramebufferScaleY,
            effectivePhysicalDpi,
            usedFallbackPhysicalDpi,
            displayMetrics.MetricsRevision);
    }

    private static double ResolveScreenScale(
        in UiCanvasScalerSettings settings,
        in UiDisplayMetrics displayMetrics)
    {
        double widthScale = displayMetrics.PresentationWidth / (double)settings.ReferenceWidth;
        double heightScale = displayMetrics.PresentationHeight / (double)settings.ReferenceHeight;
        return settings.ScreenMatchMode switch
        {
            UiScreenMatchMode.MatchWidthOrHeight => Math.Pow(
                2.0,
                Lerp(Math.Log2(widthScale), Math.Log2(heightScale), settings.MatchWidthOrHeight)),
            UiScreenMatchMode.Expand => Math.Min(widthScale, heightScale),
            UiScreenMatchMode.Shrink => Math.Max(widthScale, heightScale),
            _ => throw new ArgumentOutOfRangeException(
                nameof(settings),
                settings.ScreenMatchMode,
                "未知 screen match mode。"),
        };
    }

    private static void ValidateSettings(in UiCanvasScalerSettings settings)
    {
        if (!Enum.IsDefined(settings.ScaleMode))
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.ScaleMode, "未知 UI scale mode。");
        }

        if (!Enum.IsDefined(settings.ScreenMatchMode))
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.ScreenMatchMode, "未知 screen match mode。");
        }

        if (!Enum.IsDefined(settings.PhysicalUnit))
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.PhysicalUnit, "未知 physical unit。");
        }

        ValidateFinitePositive(settings.ScaleFactor, nameof(settings.ScaleFactor));
        ValidateFinitePositive(settings.ReferenceWidth, nameof(settings.ReferenceWidth));
        ValidateFinitePositive(settings.ReferenceHeight, nameof(settings.ReferenceHeight));
        ValidateFinitePositive(settings.FallbackScreenDpi, nameof(settings.FallbackScreenDpi));
        ValidateFinitePositive(settings.DefaultSpriteDpi, nameof(settings.DefaultSpriteDpi));
        ValidateFinitePositive(settings.ReferencePixelsPerUnit, nameof(settings.ReferencePixelsPerUnit));
        if (!float.IsFinite(settings.MatchWidthOrHeight) ||
            settings.MatchWidthOrHeight < 0f ||
            settings.MatchWidthOrHeight > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings.MatchWidthOrHeight),
                "Match Width Or Height 必须是 0..1 的有限数值。");
        }
    }

    private static double GetUnitsPerInch(UiPhysicalUnit unit)
    {
        return unit switch
        {
            UiPhysicalUnit.Inches => 1.0,
            UiPhysicalUnit.Centimeters => 2.54,
            UiPhysicalUnit.Millimeters => 25.4,
            UiPhysicalUnit.Points => 72.0,
            UiPhysicalUnit.Picas => 6.0,
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "未知 physical unit。"),
        };
    }

    private static double Lerp(double from, double to, double amount)
    {
        return from + ((to - from) * amount);
    }

    private static void ValidateFinitePositive(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName, "CanvasScaler 数值必须为有限正数。");
        }
    }

    private static float ToFinitePositiveFloat(double value, string parameterName)
    {
        float result = (float)value;
        return double.IsFinite(value) && value > 0.0 && float.IsFinite(result) && result > 0f
            ? result
            : throw new ArgumentOutOfRangeException(parameterName, "CanvasScaler 解析结果必须为有限正数。");
    }
}
