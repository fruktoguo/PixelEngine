using System.Numerics;

namespace PixelEngine.Editor.Shell.Settings;

/// <summary>
/// Editor 辅助浮动窗口的响应式布局结果。
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
    private static readonly Vector2 SettingsPreferredSize = new(780f, 540f);
    private static readonly Vector2 SettingsMinimumUsableSize = new(460f, 320f);
    private static readonly Vector2 BuildPreferredSize = new(980f, 680f);
    private static readonly Vector2 BuildMinimumUsableSize = new(620f, 420f);

    public static EditorSettingsWindowPlacement Resolve(
        Vector2 workPosition,
        Vector2 workSize,
        float uiScale)
    {
        return Resolve(
            workPosition,
            workSize,
            uiScale,
            SettingsPreferredSize,
            SettingsMinimumUsableSize);
    }

    /// <summary>解析 Build Settings 首次出现时的独立浮动窗口位置与尺寸。</summary>
    public static EditorSettingsWindowPlacement ResolveBuildSettings(
        Vector2 workPosition,
        Vector2 workSize,
        float uiScale)
    {
        return Resolve(
            workPosition,
            workSize,
            uiScale,
            BuildPreferredSize,
            BuildMinimumUsableSize);
    }

    private static EditorSettingsWindowPlacement Resolve(
        Vector2 workPosition,
        Vector2 workSize,
        float uiScale,
        Vector2 preferredSize,
        Vector2 minimumUsableSize)
    {
        float scale = EditorUiScale.Normalize(uiScale);
        float edgeMargin = EditorUiScale.Scale(16f, scale);
        Vector2 maximumSize = new(
            MathF.Max(1f, workSize.X - edgeMargin),
            MathF.Max(1f, workSize.Y - edgeMargin));
        Vector2 minimumSize = new(
            MathF.Min(EditorUiScale.Scale(minimumUsableSize.X, scale), maximumSize.X),
            MathF.Min(EditorUiScale.Scale(minimumUsableSize.Y, scale), maximumSize.Y));
        Vector2 size = EditorUiScale.FitWindow(preferredSize, scale, workSize);
        size = Vector2.Clamp(size, minimumSize, maximumSize);
        Vector2 position = workPosition + ((workSize - size) * 0.5f);
        return new EditorSettingsWindowPlacement(position, size, minimumSize, maximumSize);
    }

    /// <summary>
    /// 为 Project/Player Settings 的 label/value 表格分配 label 列。
    /// 高缩放或窄窗口优先保留可编辑的 value 宽度，避免固定 210px label 把输入框挤没。
    /// </summary>
    public static float ResolveLabelWidth(float availableWidth, float uiScale)
    {
        float available = float.IsFinite(availableWidth) ? MathF.Max(1f, availableWidth) : 1f;
        float scale = EditorUiScale.Normalize(uiScale);
        float minimum = EditorUiScale.Scale(120f, scale);
        float maximum = EditorUiScale.Scale(220f, scale);
        float minimumValueWidth = EditorUiScale.Scale(180f, scale);
        float preferred = Math.Clamp(available * 0.36f, minimum, maximum);
        return MathF.Min(preferred, MathF.Max(1f, available - minimumValueWidth));
    }
}
