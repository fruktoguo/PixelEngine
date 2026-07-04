using Hexa.NET.ImGui;
using PixelEngine.Scripting;
using System.Numerics;

namespace PixelEngine.Gui;

/// <summary>
/// 将脚本公开的 <see cref="IGuiContext" /> 适配到当前 ImGui frame。
/// </summary>
public sealed class ScriptGuiContext(int width, int height, float deltaTime, GuiInputSnapshot capture) : IGuiContext, IGuiDrawContext
{
    /// <inheritdoc />
    public int Width { get; } = Math.Max(1, width);

    /// <inheritdoc />
    public int Height { get; } = Math.Max(1, height);

    /// <inheritdoc />
    public float DeltaTime { get; } = float.IsFinite(deltaTime) && deltaTime > 0f ? deltaTime : 1f / 60f;

    /// <inheritdoc />
    public bool WantsMouse { get; } = capture.WantCaptureMouse;

    /// <inheritdoc />
    public bool WantsKeyboard { get; } = capture.WantCaptureKeyboard;

    /// <inheritdoc />
    public void SetNextWindow(float x, float y, float width, float height, GuiCondition condition = GuiCondition.Always)
    {
        SetNextWindowCore(x, y, width, height, MapCondition(condition));
    }

    /// <inheritdoc />
    public void SetNextWindow(float x, float y, float width, float height, GuiDrawCondition condition = GuiDrawCondition.Always)
    {
        SetNextWindowCore(x, y, width, height, MapCondition(condition));
    }

    private static void SetNextWindowCore(float x, float y, float width, float height, ImGuiCond condition)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(width) || !float.IsFinite(height))
        {
            throw new ArgumentOutOfRangeException(nameof(width), "GUI 窗口坐标与尺寸必须是有限数值。");
        }

        ImGui.SetNextWindowPos(new Vector2(x, y), condition);
        ImGui.SetNextWindowSize(new Vector2(Math.Max(1f, width), Math.Max(1f, height)), condition);
    }

    /// <inheritdoc />
    public bool BeginWindow(string id, string title, GuiWindowFlags flags = GuiWindowFlags.None)
    {
        return BeginWindowCore(id, title, MapWindowFlags(flags));
    }

    /// <inheritdoc />
    public bool BeginWindow(string id, string title, GuiDrawWindowFlags flags = GuiDrawWindowFlags.None)
    {
        return BeginWindowCore(id, title, MapWindowFlags(flags));
    }

    private static bool BeginWindowCore(string id, string title, ImGuiWindowFlags flags)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(title);
        bool visible = true;
        return ImGui.Begin($"{title}##{id}", ref visible, flags);
    }

    /// <inheritdoc />
    public void EndWindow()
    {
        ImGui.End();
    }

    /// <inheritdoc />
    public void Text(string text)
    {
        ImGui.TextUnformatted(text ?? string.Empty);
    }

    /// <inheritdoc />
    public void TextColored(string text, uint colorBgra)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, BgraToVector4(colorBgra));
        ImGui.TextUnformatted(text ?? string.Empty);
        ImGui.PopStyleColor();
    }

    /// <inheritdoc />
    public void SameLine()
    {
        ImGui.SameLine();
    }

    /// <inheritdoc />
    public void Separator()
    {
        ImGui.Separator();
    }

    /// <inheritdoc />
    public bool Button(string label)
    {
        return ImGui.Button(label ?? string.Empty);
    }

    /// <inheritdoc />
    public bool Checkbox(string label, ref bool value)
    {
        return ImGui.Checkbox(label ?? string.Empty, ref value);
    }

    /// <inheritdoc />
    public void ProgressBar(float value01, string? label = null)
    {
        float clamped = float.IsFinite(value01) ? Math.Clamp(value01, 0f, 1f) : 0f;
        ImGui.ProgressBar(clamped, new Vector2(-1f, 0f), label ?? string.Empty);
    }

    /// <inheritdoc />
    public void ColorSwatch(string id, uint colorBgra, float size = 16f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        float side = float.IsFinite(size) ? Math.Max(1f, size) : 16f;
        _ = ImGui.ColorButton($"##{id}", BgraToVector4(colorBgra), ImGuiColorEditFlags.NoTooltip, new Vector2(side, side));
    }

    private static ImGuiCond MapCondition(GuiCondition condition)
    {
        return condition switch
        {
            GuiCondition.Always => ImGuiCond.Always,
            GuiCondition.FirstUseEver => ImGuiCond.FirstUseEver,
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "未知 GUI 条件。"),
        };
    }

    private static ImGuiCond MapCondition(GuiDrawCondition condition)
    {
        return condition switch
        {
            GuiDrawCondition.Always => ImGuiCond.Always,
            GuiDrawCondition.FirstUseEver => ImGuiCond.FirstUseEver,
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "未知 GUI 条件。"),
        };
    }

    private static ImGuiWindowFlags MapWindowFlags(GuiWindowFlags flags)
    {
        ImGuiWindowFlags result = ImGuiWindowFlags.None;
        if ((flags & GuiWindowFlags.NoTitleBar) != 0)
        {
            result |= ImGuiWindowFlags.NoTitleBar;
        }

        if ((flags & GuiWindowFlags.NoResize) != 0)
        {
            result |= ImGuiWindowFlags.NoResize;
        }

        if ((flags & GuiWindowFlags.NoMove) != 0)
        {
            result |= ImGuiWindowFlags.NoMove;
        }

        if ((flags & GuiWindowFlags.AlwaysAutoResize) != 0)
        {
            result |= ImGuiWindowFlags.AlwaysAutoResize;
        }

        if ((flags & GuiWindowFlags.NoSavedSettings) != 0)
        {
            result |= ImGuiWindowFlags.NoSavedSettings;
        }

        if ((flags & GuiWindowFlags.NoBackground) != 0)
        {
            result |= ImGuiWindowFlags.NoBackground;
        }

        if ((flags & GuiWindowFlags.NoScrollbar) != 0)
        {
            result |= ImGuiWindowFlags.NoScrollbar;
        }

        return result;
    }

    private static ImGuiWindowFlags MapWindowFlags(GuiDrawWindowFlags flags)
    {
        ImGuiWindowFlags result = ImGuiWindowFlags.None;
        if ((flags & GuiDrawWindowFlags.NoTitleBar) != 0)
        {
            result |= ImGuiWindowFlags.NoTitleBar;
        }

        if ((flags & GuiDrawWindowFlags.NoResize) != 0)
        {
            result |= ImGuiWindowFlags.NoResize;
        }

        if ((flags & GuiDrawWindowFlags.NoMove) != 0)
        {
            result |= ImGuiWindowFlags.NoMove;
        }

        if ((flags & GuiDrawWindowFlags.AlwaysAutoResize) != 0)
        {
            result |= ImGuiWindowFlags.AlwaysAutoResize;
        }

        if ((flags & GuiDrawWindowFlags.NoSavedSettings) != 0)
        {
            result |= ImGuiWindowFlags.NoSavedSettings;
        }

        if ((flags & GuiDrawWindowFlags.NoBackground) != 0)
        {
            result |= ImGuiWindowFlags.NoBackground;
        }

        if ((flags & GuiDrawWindowFlags.NoScrollbar) != 0)
        {
            result |= ImGuiWindowFlags.NoScrollbar;
        }

        return result;
    }

    private static Vector4 BgraToVector4(uint colorBgra)
    {
        float b = (colorBgra & 0xFF) / 255f;
        float g = ((colorBgra >> 8) & 0xFF) / 255f;
        float r = ((colorBgra >> 16) & 0xFF) / 255f;
        float a = ((colorBgra >> 24) & 0xFF) / 255f;
        return new Vector4(r, g, b, a);
    }
}
