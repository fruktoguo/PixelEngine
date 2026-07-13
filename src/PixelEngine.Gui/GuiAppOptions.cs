namespace PixelEngine.Gui;

/// <summary>
/// 中性 ImGui 宿主初始化选项，供玩家 HUD、脚本 GUI 与编辑器壳共享。
/// </summary>
public sealed record GuiAppOptions
{
    /// <summary>
    /// 是否启用 GUI 宿主。关闭时不创建 ImGui context。
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// 传给 ImGui OpenGL3 后端的 GLSL 版本字符串。
    /// </summary>
    public string GlslVersion { get; init; } = "#version 330 core";

    /// <summary>
    /// ImGui ini 布局文件路径。
    /// </summary>
    public string LayoutPath { get; init; } = "imgui.ini";

    /// <summary>
    /// 拉丁字母与数字使用的主字体路径；为空时由后端使用默认主字体。
    /// </summary>
    public string? PrimaryFontPath { get; init; }

    /// <summary>
    /// 合并到主字体 atlas 的 CJK fallback 字体路径；为空时从系统字体中探测。
    /// </summary>
    public string? CjkFallbackFontPath { get; init; }

    /// <summary>
    /// 兼容旧调用方的 CJK 字体路径；新代码应使用 <see cref="CjkFallbackFontPath"/>。
    /// </summary>
    public string? PreferredFontPath { get; init; }

    /// <summary>
    /// 基准字体大小，DPI 缩放会乘到此值上。
    /// </summary>
    public float FontSizePixels { get; init; } = 18f;

    /// <summary>
    /// UI DPI 缩放。
    /// </summary>
    public float DpiScale { get; init; } = 1f;

    /// <summary>
    /// 是否允许 ImGui 多视口。默认关闭以保持单窗口、单 GL context。
    /// </summary>
    public bool EnableMultiViewport { get; init; }

    /// <summary>
    /// 初始化 ImGui context 时应用的主题。
    /// </summary>
    public GuiThemeKind Theme { get; init; } = GuiThemeKind.NeutralDark;

    /// <summary>
    /// 校验并返回规范化后的选项。
    /// </summary>
    public GuiAppOptions Normalize()
    {
        if (string.IsNullOrWhiteSpace(GlslVersion))
        {
            throw new ArgumentException("GLSL 版本不能为空。", nameof(GlslVersion));
        }

        if (string.IsNullOrWhiteSpace(LayoutPath))
        {
            throw new ArgumentException("GUI 布局路径不能为空。", nameof(LayoutPath));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(FontSizePixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(DpiScale);

        return this with
        {
            GlslVersion = GlslVersion.Trim(),
            LayoutPath = LayoutPath.Trim(),
            PrimaryFontPath = NormalizeOptionalPath(PrimaryFontPath),
            CjkFallbackFontPath = NormalizeOptionalPath(CjkFallbackFontPath ?? PreferredFontPath),
            PreferredFontPath = NormalizeOptionalPath(CjkFallbackFontPath ?? PreferredFontPath),
        };
    }

    private static string? NormalizeOptionalPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? null : path.Trim();
    }
}
