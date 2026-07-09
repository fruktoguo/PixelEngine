using PixelEngine.Gui;

namespace PixelEngine.Editor;

/// <summary>
/// Editor 初始化选项，集中描述 ImGui 后端、字体、布局与禁用开关。
/// </summary>
public sealed record EditorAppOptions
{
    /// <summary>
    /// 是否启用 Editor。关闭时 <see cref="EditorApp"/> 不创建 ImGui context，也不挂接 UI 绘制。
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// 传给 ImGui OpenGL3 后端的 GLSL 版本字符串。
    /// </summary>
    public string GlslVersion { get; init; } = "#version 330 core";

    /// <summary>
    /// ImGui 布局 ini 文件路径。
    /// </summary>
    public string LayoutPath { get; init; } = "imgui.ini";

    /// <summary>
    /// 首选中文字体路径；为空时由 <see cref="Gui.GuiFontManager"/> 从系统字体中探测。
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
    /// 是否允许 ImGui 多视口。默认关闭以保证单窗口、单 GL context。
    /// </summary>
    public bool EnableMultiViewport { get; init; }

    /// <summary>
    /// 是否绘制 Editor dockspace 与注册面板；游戏 HUD 模式可关闭以只保留 ImGui frame lifecycle。
    /// </summary>
    public bool EnableDockSpace { get; init; } = true;

    /// <summary>
    /// 初始化 ImGui context 时应用的主题。
    /// </summary>
    public GuiThemeKind Theme { get; init; } = GuiThemeKind.Unity6Dark;

    /// <summary>
    /// 校验并返回规范化后的选项。
    /// </summary>
    /// <returns>规范化选项。</returns>
    public EditorAppOptions Normalize()
    {
        if (string.IsNullOrWhiteSpace(GlslVersion))
        {
            throw new ArgumentException("GLSL 版本不能为空。", nameof(GlslVersion));
        }

        if (string.IsNullOrWhiteSpace(LayoutPath))
        {
            throw new ArgumentException("Editor 布局路径不能为空。", nameof(LayoutPath));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(FontSizePixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(DpiScale);

        return this with
        {
            GlslVersion = GlslVersion.Trim(),
            LayoutPath = LayoutPath.Trim(),
            PreferredFontPath = string.IsNullOrWhiteSpace(PreferredFontPath) ? null : PreferredFontPath.Trim(),
        };
    }
}
