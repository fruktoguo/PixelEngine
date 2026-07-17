using Hexa.NET.ImGui;
using PixelEngine.Gui;
using System.Numerics;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Editor UI 缩放规则与当前 ImGui context 应用入口。
/// </summary>
internal static class EditorUiScale
{
    public const float Minimum = 0.75f;
    public const float Maximum = 2f;
    public const float Default = 1f;
    public const float Step = 0.05f;

    public static float Normalize(float scale)
    {
        if (!float.IsFinite(scale))
        {
            return Default;
        }

        float clamped = Math.Clamp(scale, Minimum, Maximum);
        return MathF.Round(clamped / Step, MidpointRounding.AwayFromZero) * Step;
    }

    public static float Scale(float pixels, float scale)
    {
        return pixels * Normalize(scale);
    }

    public static int ToPercent(float scale)
    {
        return (int)MathF.Round(Normalize(scale) * 100f, MidpointRounding.AwayFromZero);
    }

    public static Vector2 FitWindow(Vector2 baseSize, float scale, Vector2 availableSize, float marginPixels = 32f)
    {
        float margin = Scale(marginPixels, scale);
        return new Vector2(
            Math.Min(Scale(baseSize.X, scale), Math.Max(1f, availableSize.X - margin)),
            Math.Min(Scale(baseSize.Y, scale), Math.Max(1f, availableSize.Y - margin)));
    }

    public static float GetScaleRatio(float targetScale, float appliedScale)
    {
        return Normalize(targetScale) / Normalize(appliedScale);
    }
}

/// <summary>
/// 单个 ImGui context 的缩放状态，避免每帧重复调用 ScaleAllSizes 造成指数累乘。
/// </summary>
internal sealed class EditorUiScaleContextState
{
    private readonly ImGuiStyleScaleState _styleScale = new();
    private bool _initialized;

    public float AppliedScale => _styleScale.AppliedScale;

    public void Reset()
    {
        _styleScale.Reset();
        _initialized = false;
    }

    public void Apply(float targetScale, float fontAtlasScale)
    {
        float normalizedTarget = EditorUiScale.Normalize(targetScale);
        float normalizedAtlas = EditorUiScale.Normalize(fontAtlasScale);
        ImGuiStylePtr style = ImGui.GetStyle();
        if (!_initialized)
        {
            GuiTheme.ApplyCurrent(GuiThemeKind.Unity6Dark);
            _styleScale.CaptureCurrent();
            _initialized = true;
        }

        _styleScale.Apply(normalizedTarget);
        style.FontScaleMain = normalizedTarget / normalizedAtlas;
    }
}
