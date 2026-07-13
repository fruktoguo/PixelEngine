using System.Numerics;

namespace PixelEngine.Editor.Shell.Settings;

/// <summary>
/// Project/Player Settings 浮动窗口的响应式布局结果。
/// </summary>
internal readonly record struct EditorSettingsWindowPlacement(
    Vector2 Position,
    Vector2 Size,
    Vector2 MinimumSize,
    Vector2 MaximumSize);

/// <summary>
/// 让项目级设置在 720p 与更窄窗口中仍保持可编辑，而不是被压进底部 dock 的窄标签页。
/// </summary>
internal static class EditorSettingsWindowLayout
{
    private static readonly Vector2 PreferredSize = new(780f, 540f);
    private static readonly Vector2 MinimumUsableSize = new(460f, 320f);

    public static EditorSettingsWindowPlacement Resolve(
        Vector2 workPosition,
        Vector2 workSize,
        float uiScale)
    {
        float scale = EditorUiScale.Normalize(uiScale);
        float edgeMargin = EditorUiScale.Scale(16f, scale);
        Vector2 maximumSize = new(
            MathF.Max(1f, workSize.X - edgeMargin),
            MathF.Max(1f, workSize.Y - edgeMargin));
        Vector2 minimumSize = new(
            MathF.Min(EditorUiScale.Scale(MinimumUsableSize.X, scale), maximumSize.X),
            MathF.Min(EditorUiScale.Scale(MinimumUsableSize.Y, scale), maximumSize.Y));
        Vector2 size = EditorUiScale.FitWindow(PreferredSize, scale, workSize);
        size = Vector2.Clamp(size, minimumSize, maximumSize);
        Vector2 position = workPosition + ((workSize - size) * 0.5f);
        return new EditorSettingsWindowPlacement(position, size, minimumSize, maximumSize);
    }
}
