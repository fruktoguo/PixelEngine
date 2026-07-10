using PixelEngine.Scripting;

namespace PixelEngine.Editor;

/// <summary>
/// 将脚本公开的 <see cref="IGuiContext" /> 适配到 Editor 当前 ImGui frame，
/// 并复用 <see cref="PixelEngine.Gui.ScriptGuiContext" /> 的无分配 UTF-8 编码路径。
/// </summary>
/// <param name="width">framebuffer 宽度。</param>
/// <param name="height">framebuffer 高度。</param>
/// <param name="deltaTime">GUI 帧间隔。</param>
/// <param name="capture">Editor 输入捕获快照。</param>
public sealed class ScriptGuiContext(
    int width,
    int height,
    float deltaTime,
    EditorInputSnapshot capture) : IGuiContext
{
    private readonly PixelEngine.Gui.ScriptGuiContext _inner = new(width, height, deltaTime, MapCapture(capture));

    /// <inheritdoc />
    public int Width => _inner.Width;

    /// <inheritdoc />
    public int Height => _inner.Height;

    /// <inheritdoc />
    public float DeltaTime => _inner.DeltaTime;

    /// <inheritdoc />
    public bool WantsMouse => _inner.WantsMouse;

    /// <inheritdoc />
    public bool WantsKeyboard => _inner.WantsKeyboard;

    internal void ResetFrame(int width, int height, float deltaTime, EditorInputSnapshot capture)
    {
        _inner.ResetFrame(width, height, deltaTime, MapCapture(capture));
    }

    /// <inheritdoc />
    public void SetNextWindow(float x, float y, float width, float height, GuiCondition condition = GuiCondition.Always)
    {
        _inner.SetNextWindow(x, y, width, height, condition);
    }

    /// <inheritdoc />
    public bool BeginWindow(string id, string title, GuiWindowFlags flags = GuiWindowFlags.None)
    {
        return _inner.BeginWindow(id, title, flags);
    }

    /// <inheritdoc />
    public void EndWindow()
    {
        _inner.EndWindow();
    }

    /// <inheritdoc />
    public void Text(string text)
    {
        _inner.Text(text);
    }

    /// <inheritdoc />
    public void Text(ReadOnlySpan<char> text)
    {
        _inner.Text(text);
    }

    /// <inheritdoc />
    public void TextColored(string text, uint colorBgra)
    {
        _inner.TextColored(text, colorBgra);
    }

    /// <inheritdoc />
    public void TextColored(ReadOnlySpan<char> text, uint colorBgra)
    {
        _inner.TextColored(text, colorBgra);
    }

    /// <inheritdoc />
    public void SameLine()
    {
        _inner.SameLine();
    }

    /// <inheritdoc />
    public void Separator()
    {
        _inner.Separator();
    }

    /// <inheritdoc />
    public bool Button(string label)
    {
        return _inner.Button(label);
    }

    /// <inheritdoc />
    public bool Checkbox(string label, ref bool value)
    {
        return _inner.Checkbox(label, ref value);
    }

    /// <inheritdoc />
    public void ProgressBar(float value01, string? label = null)
    {
        _inner.ProgressBar(value01, label);
    }

    /// <inheritdoc />
    public void ProgressBar(float value01, ReadOnlySpan<char> label)
    {
        _inner.ProgressBar(value01, label);
    }

    /// <inheritdoc />
    public void ColorSwatch(string id, uint colorBgra, float size = 16f)
    {
        _inner.ColorSwatch(id, colorBgra, size);
    }

    /// <inheritdoc />
    public void ColorSwatch(ReadOnlySpan<char> id, uint colorBgra, float size = 16f)
    {
        _inner.ColorSwatch(id, colorBgra, size);
    }

    private static PixelEngine.Gui.GuiInputSnapshot MapCapture(EditorInputSnapshot capture)
    {
        return new PixelEngine.Gui.GuiInputSnapshot(capture.WantCaptureMouse, capture.WantCaptureKeyboard);
    }
}
