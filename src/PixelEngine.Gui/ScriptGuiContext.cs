using Hexa.NET.ImGui;
using PixelEngine.Scripting;
using System.Numerics;
using System.Text;

namespace PixelEngine.Gui;

/// <summary>
/// 将脚本公开的 <see cref="IGuiContext" /> 适配到当前 ImGui frame。
/// </summary>
public sealed class ScriptGuiContext : IGuiContext, IGuiDrawContext
{
    private const int InitialUtf8Capacity = 4096;
    private byte[] _utf8Buffer = GC.AllocateUninitializedArray<byte>(InitialUtf8Capacity);

    /// <summary>
    /// 创建脚本 GUI 上下文。
    /// </summary>
    /// <param name="width">framebuffer 宽度。</param>
    /// <param name="height">framebuffer 高度。</param>
    /// <param name="deltaTime">GUI 帧间隔。</param>
    /// <param name="capture">输入捕获快照。</param>
    public ScriptGuiContext(int width, int height, float deltaTime, GuiInputSnapshot capture)
    {
        ResetFrame(width, height, deltaTime, capture);
    }

    /// <inheritdoc />
    public int Width { get; private set; }

    /// <inheritdoc />
    public int Height { get; private set; }

    /// <inheritdoc />
    public float DeltaTime { get; private set; }

    /// <inheritdoc />
    public bool WantsMouse { get; private set; }

    /// <inheritdoc />
    public bool WantsKeyboard { get; private set; }

    /// <summary>
    /// 更新当前帧尺寸、时间与输入捕获快照，同时保留 UTF-8 scratch 供后续帧复用。
    /// </summary>
    /// <param name="width">framebuffer 宽度。</param>
    /// <param name="height">framebuffer 高度。</param>
    /// <param name="deltaTime">GUI 帧间隔。</param>
    /// <param name="capture">输入捕获快照。</param>
    public void ResetFrame(int width, int height, float deltaTime, GuiInputSnapshot capture)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        DeltaTime = float.IsFinite(deltaTime) && deltaTime > 0f ? deltaTime : 1f / 60f;
        WantsMouse = capture.WantCaptureMouse;
        WantsKeyboard = capture.WantCaptureKeyboard;
    }

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

    private bool BeginWindowCore(string id, string title, ImGuiWindowFlags flags)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(title);
        bool visible = true;
        return ImGui.Begin(EncodeCompositeNullTerminated(title, "##", id), ref visible, flags);
    }

    /// <inheritdoc />
    public void EndWindow()
    {
        ImGui.End();
    }

    /// <inheritdoc />
    public void Text(string text)
    {
        Text((text ?? string.Empty).AsSpan());
    }

    /// <inheritdoc />
    public void Text(ReadOnlySpan<char> text)
    {
        ImGui.TextUnformatted(EncodeNullTerminated(text));
    }

    /// <inheritdoc />
    public void TextColored(string text, uint colorBgra)
    {
        TextColored((text ?? string.Empty).AsSpan(), colorBgra);
    }

    /// <inheritdoc />
    public void TextColored(ReadOnlySpan<char> text, uint colorBgra)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, BgraToVector4(colorBgra));
        ImGui.TextUnformatted(EncodeNullTerminated(text));
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
    public void SetCursor(float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "GUI 控件局部坐标必须是有限数值。");
        }

        ImGui.SetCursorPos(new Vector2(Math.Max(0f, x), Math.Max(0f, y)));
    }

    /// <inheritdoc />
    public void AddVerticalSpacing(float height)
    {
        if (!float.IsFinite(height) || height <= 0f)
        {
            return;
        }

        ImGui.Dummy(new Vector2(1f, height));
    }

    /// <inheritdoc />
    public bool Button(string label)
    {
        return ImGui.Button(label ?? string.Empty);
    }

    /// <inheritdoc />
    public bool Button(string label, float width, float height)
    {
        Vector2 size = new(
            float.IsFinite(width) && width > 0f ? width : 0f,
            float.IsFinite(height) && height > 0f ? height : 0f);
        return ImGui.Button(label ?? string.Empty, size);
    }

    /// <inheritdoc />
    public bool Checkbox(string label, ref bool value)
    {
        return ImGui.Checkbox(label ?? string.Empty, ref value);
    }

    /// <inheritdoc />
    public void ProgressBar(float value01, string? label = null)
    {
        ProgressBar(value01, (label ?? string.Empty).AsSpan());
    }

    /// <inheritdoc />
    public void ProgressBar(float value01, ReadOnlySpan<char> label)
    {
        float clamped = float.IsFinite(value01) ? Math.Clamp(value01, 0f, 1f) : 0f;
        ImGui.ProgressBar(clamped, new Vector2(-1f, 0f), EncodeNullTerminated(label));
    }

    /// <inheritdoc />
    public void ProgressBar(float value01, string? label, float width, float height)
    {
        float clamped = float.IsFinite(value01) ? Math.Clamp(value01, 0f, 1f) : 0f;
        Vector2 size = new(
            float.IsFinite(width) && width > 0f ? width : -1f,
            float.IsFinite(height) && height > 0f ? height : 0f);
        ImGui.ProgressBar(clamped, size, label ?? string.Empty);
    }

    /// <inheritdoc />
    public void ColorSwatch(string id, uint colorBgra, float size = 16f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ColorSwatch(id.AsSpan(), colorBgra, size);
    }

    /// <inheritdoc />
    public void ColorSwatch(ReadOnlySpan<char> id, uint colorBgra, float size = 16f)
    {
        if (id.IsEmpty || id.Trim().IsEmpty)
        {
            throw new ArgumentException("GUI 控件 id 不能为空或空白。", nameof(id));
        }

        float side = float.IsFinite(size) ? Math.Max(1f, size) : 16f;
        _ = ImGui.ColorButton(
            EncodeCompositeNullTerminated("##", id),
            BgraToVector4(colorBgra),
            ImGuiColorEditFlags.NoTooltip,
            new Vector2(side, side));
    }

    /// <inheritdoc />
    public unsafe void Image(string id, uint textureHandle, int textureWidth, int textureHeight, float width, float height, uint tintBgra = 0xFF_FF_FF_FF)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (textureHandle == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(textureHandle), "GUI 图片纹理句柄必须非 0。");
        }

        if (textureWidth <= 0 || textureHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(textureWidth), "GUI 图片纹理尺寸必须为正数。");
        }

        float drawWidth = float.IsFinite(width) && width > 0f ? width : textureWidth;
        float drawHeight = float.IsFinite(height) && height > 0f ? height : textureHeight;
        ImGui.Image(new ImTextureRef(null, (ImTextureID)(ulong)textureHandle), new Vector2(drawWidth, drawHeight));
        _ = id;
        _ = tintBgra;
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

    private ReadOnlySpan<byte> EncodeNullTerminated(ReadOnlySpan<char> text)
    {
        int byteCount = Encoding.UTF8.GetByteCount(text);
        EnsureUtf8Capacity(checked(byteCount + 1));
        int written = Encoding.UTF8.GetBytes(text, _utf8Buffer);
        _utf8Buffer[written] = 0;
        return _utf8Buffer.AsSpan(0, written + 1);
    }

    private ReadOnlySpan<byte> EncodeCompositeNullTerminated(
        ReadOnlySpan<char> first,
        ReadOnlySpan<char> second)
    {
        int firstByteCount = Encoding.UTF8.GetByteCount(first);
        int secondByteCount = Encoding.UTF8.GetByteCount(second);
        int required = checked(firstByteCount + secondByteCount + 1);
        EnsureUtf8Capacity(required);

        int written = Encoding.UTF8.GetBytes(first, _utf8Buffer);
        written += Encoding.UTF8.GetBytes(second, _utf8Buffer.AsSpan(written));
        _utf8Buffer[written] = 0;
        return _utf8Buffer.AsSpan(0, written + 1);
    }

    private ReadOnlySpan<byte> EncodeCompositeNullTerminated(
        ReadOnlySpan<char> first,
        ReadOnlySpan<char> second,
        ReadOnlySpan<char> third)
    {
        int firstByteCount = Encoding.UTF8.GetByteCount(first);
        int secondByteCount = Encoding.UTF8.GetByteCount(second);
        int thirdByteCount = Encoding.UTF8.GetByteCount(third);
        int required = checked(firstByteCount + secondByteCount + thirdByteCount + 1);
        EnsureUtf8Capacity(required);

        int written = Encoding.UTF8.GetBytes(first, _utf8Buffer);
        written += Encoding.UTF8.GetBytes(second, _utf8Buffer.AsSpan(written));
        written += Encoding.UTF8.GetBytes(third, _utf8Buffer.AsSpan(written));
        _utf8Buffer[written] = 0;
        return _utf8Buffer.AsSpan(0, written + 1);
    }

    private void EnsureUtf8Capacity(int requiredCapacity)
    {
        if (requiredCapacity <= _utf8Buffer.Length)
        {
            return;
        }

        int doubledCapacity = _utf8Buffer.Length <= Array.MaxLength / 2
            ? _utf8Buffer.Length * 2
            : Array.MaxLength;
        Array.Resize(ref _utf8Buffer, Math.Max(requiredCapacity, doubledCapacity));
    }
}
