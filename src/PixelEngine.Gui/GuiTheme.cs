using Hexa.NET.ImGui;
using System.Numerics;

namespace PixelEngine.Gui;

/// <summary>
/// 中性 ImGui 主题选择。
/// </summary>
public enum GuiThemeKind
{
    /// <summary>
    /// Dear ImGui 默认暗色主题，用于玩家侧 fallback 与通用 GUI。
    /// </summary>
    NeutralDark,

    /// <summary>
    /// 面向 Unity 6 Editor 心智模型的深灰主题。
    /// </summary>
    Unity6Dark,
}

/// <summary>
/// 可测试的 ImGui 主题 token。
/// </summary>
public readonly record struct GuiThemeTokens(
    string Name,
    float WindowRounding,
    float ChildRounding,
    float FrameRounding,
    float PopupRounding,
    float TabRounding,
    float DockingSeparatorSize,
    Vector2 WindowPadding,
    Vector2 FramePadding,
    Vector2 ItemSpacing,
    Vector4 Text,
    Vector4 TextDisabled,
    Vector4 WindowBg,
    Vector4 PanelBg,
    Vector4 FrameBg,
    Vector4 Header,
    Vector4 HeaderHovered,
    Vector4 HeaderActive,
    Vector4 Button,
    Vector4 ButtonHovered,
    Vector4 ButtonActive,
    Vector4 Accent,
    Vector4 Border);

/// <summary>
/// 统一管理 GUI / Editor ImGui 主题。
/// </summary>
public static class GuiTheme
{
    /// <summary>
    /// 返回指定主题的 token。
    /// </summary>
    public static GuiThemeTokens GetTokens(GuiThemeKind theme)
    {
        return theme switch
        {
            GuiThemeKind.NeutralDark => new GuiThemeTokens(
                "Neutral Dark",
                WindowRounding: 0f,
                ChildRounding: 0f,
                FrameRounding: 2f,
                PopupRounding: 2f,
                TabRounding: 2f,
                DockingSeparatorSize: 2f,
                WindowPadding: new Vector2(8f, 8f),
                FramePadding: new Vector2(6f, 4f),
                ItemSpacing: new Vector2(8f, 5f),
                Text: Rgb(0xE6, 0xE6, 0xE6),
                TextDisabled: Rgb(0x86, 0x86, 0x86),
                WindowBg: Rgb(0x22, 0x22, 0x22),
                PanelBg: Rgb(0x2A, 0x2A, 0x2A),
                FrameBg: Rgb(0x35, 0x35, 0x35),
                Header: Rgb(0x3A, 0x3A, 0x3A),
                HeaderHovered: Rgb(0x48, 0x48, 0x48),
                HeaderActive: Rgb(0x54, 0x54, 0x54),
                Button: Rgb(0x3C, 0x3C, 0x3C),
                ButtonHovered: Rgb(0x4A, 0x4A, 0x4A),
                ButtonActive: Rgb(0x56, 0x56, 0x56),
                Accent: Rgb(0x4D, 0x8E, 0xC8),
                Border: Rgb(0x12, 0x12, 0x12)),
            GuiThemeKind.Unity6Dark => new GuiThemeTokens(
                "PixelEngine Modern Dark",
                WindowRounding: 4f,
                ChildRounding: 4f,
                FrameRounding: 4f,
                PopupRounding: 6f,
                TabRounding: 4f,
                DockingSeparatorSize: 3f,
                WindowPadding: new Vector2(10f, 9f),
                FramePadding: new Vector2(8f, 5f),
                ItemSpacing: new Vector2(8f, 6f),
                Text: Rgb(0xE7, 0xE9, 0xED),
                TextDisabled: Rgb(0x8D, 0x92, 0x9B),
                WindowBg: Rgb(0x1E, 0x1F, 0x22),
                PanelBg: Rgb(0x25, 0x26, 0x2A),
                FrameBg: Rgb(0x30, 0x32, 0x38),
                Header: Rgb(0x35, 0x38, 0x40),
                HeaderHovered: Rgb(0x41, 0x46, 0x52),
                HeaderActive: Rgb(0x4A, 0x51, 0x60),
                Button: Rgb(0x32, 0x35, 0x3B),
                ButtonHovered: Rgb(0x3E, 0x43, 0x4D),
                ButtonActive: Rgb(0x49, 0x50, 0x5C),
                Accent: Rgb(0x4C, 0x8D, 0xFF),
                Border: Rgb(0x14, 0x15, 0x18)),
            _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, "未知 GUI 主题。"),
        };
    }

    /// <summary>
    /// 将主题应用到当前 ImGui context。
    /// </summary>
    public static void ApplyCurrent(GuiThemeKind theme)
    {
        ImGui.StyleColorsDark();
        Apply(ImGui.GetStyle(), GetTokens(theme));
    }

    private static void Apply(ImGuiStylePtr style, GuiThemeTokens tokens)
    {
        style.WindowRounding = tokens.WindowRounding;
        style.ChildRounding = tokens.ChildRounding;
        style.FrameRounding = tokens.FrameRounding;
        style.PopupRounding = tokens.PopupRounding;
        style.TabRounding = tokens.TabRounding;
        style.DockingSeparatorSize = tokens.DockingSeparatorSize;
        style.WindowPadding = tokens.WindowPadding;
        style.FramePadding = tokens.FramePadding;
        style.ItemSpacing = tokens.ItemSpacing;
        style.WindowBorderSize = 1f;
        style.ChildBorderSize = 1f;
        style.FrameBorderSize = 0f;
        style.TabBorderSize = 0f;
        style.PopupBorderSize = 1f;
        style.ScrollbarRounding = 0f;
        style.GrabRounding = 1f;

        Span<Vector4> colors = style.Colors;
        colors[(int)ImGuiCol.Text] = tokens.Text;
        colors[(int)ImGuiCol.TextDisabled] = tokens.TextDisabled;
        colors[(int)ImGuiCol.WindowBg] = tokens.WindowBg;
        colors[(int)ImGuiCol.ChildBg] = tokens.PanelBg;
        colors[(int)ImGuiCol.PopupBg] = tokens.PanelBg;
        colors[(int)ImGuiCol.Border] = tokens.Border;
        colors[(int)ImGuiCol.FrameBg] = tokens.FrameBg;
        colors[(int)ImGuiCol.FrameBgHovered] = tokens.HeaderHovered;
        colors[(int)ImGuiCol.FrameBgActive] = tokens.HeaderActive;
        colors[(int)ImGuiCol.TitleBg] = tokens.PanelBg;
        colors[(int)ImGuiCol.TitleBgActive] = tokens.Header;
        colors[(int)ImGuiCol.MenuBarBg] = tokens.PanelBg;
        colors[(int)ImGuiCol.Button] = tokens.Button;
        colors[(int)ImGuiCol.ButtonHovered] = tokens.ButtonHovered;
        colors[(int)ImGuiCol.ButtonActive] = tokens.ButtonActive;
        colors[(int)ImGuiCol.Header] = tokens.Header;
        colors[(int)ImGuiCol.HeaderHovered] = tokens.HeaderHovered;
        colors[(int)ImGuiCol.HeaderActive] = tokens.HeaderActive;
        colors[(int)ImGuiCol.Separator] = tokens.Border;
        colors[(int)ImGuiCol.SeparatorHovered] = tokens.Accent;
        colors[(int)ImGuiCol.SeparatorActive] = tokens.Accent;
        colors[(int)ImGuiCol.Tab] = tokens.PanelBg;
        colors[(int)ImGuiCol.TabHovered] = tokens.HeaderHovered;
        colors[(int)ImGuiCol.TabSelected] = tokens.Header;
        colors[(int)ImGuiCol.TabDimmed] = tokens.PanelBg;
        colors[(int)ImGuiCol.TabDimmedSelected] = tokens.Header;
        colors[(int)ImGuiCol.DockingPreview] = tokens.Accent;
        colors[(int)ImGuiCol.DockingEmptyBg] = tokens.WindowBg;
        colors[(int)ImGuiCol.CheckMark] = tokens.Accent;
        colors[(int)ImGuiCol.SliderGrab] = tokens.Accent;
        colors[(int)ImGuiCol.SliderGrabActive] = tokens.Accent;
        colors[(int)ImGuiCol.TextSelectedBg] = WithAlpha(tokens.Accent, 0.45f);
        colors[(int)ImGuiCol.NavCursor] = tokens.Accent;
    }

    private static Vector4 Rgb(byte r, byte g, byte b)
    {
        return new Vector4(r / 255f, g / 255f, b / 255f, 1f);
    }

    private static Vector4 WithAlpha(Vector4 color, float alpha)
    {
        return color with { W = alpha };
    }
}
