using Hexa.NET.ImGui;
using PixelEngine.Rendering;
using System.Numerics;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Game View ImGui 面板：显示运行时 viewport 纹理。
/// </summary>
internal sealed class GameViewPanel(Func<RenderViewportTexture> textureProvider) : IEditorPanel
{
    private readonly Func<RenderViewportTexture> _textureProvider = textureProvider ?? throw new ArgumentNullException(nameof(textureProvider));
    private bool _focusRequested;

    public string Title => EditorDockSpace.GameViewWindowTitle;

    public bool Visible
    {
        get;
        set
        {
            field = value;
            if (!value)
            {
                ClearInputState();
            }
        }
    } = true;

    /// <summary>Game View 窗口是否拥有 gameplay 键盘焦点。</summary>
    public bool KeyboardFocused { get; private set; }

    /// <summary>指针是否位于实际 runtime 图像矩形内。</summary>
    public bool PointerHovered { get; private set; }

    /// <summary>兼容旧调用方的键盘焦点别名。</summary>
    public bool InputFocused => KeyboardFocused;

    /// <summary>兼容旧调用方的图像 hover 别名。</summary>
    public bool InputHovered => PointerHovered;

    public Vector2 LastPointerPanelPoint { get; private set; }

    public Vector2 LastPanelOriginFramebuffer { get; private set; }

    public Vector2 LastFramebufferScale { get; private set; } = Vector2.One;

    public GameViewViewportSnapshot LastViewportSnapshot { get; private set; } = GameViewViewportSnapshot.Empty;

    public EditorViewportContract CaptureContract(EditorMode mode)
    {
        return EditorGameViewContract.GameView(mode);
    }

    public void RequestFocus()
    {
        _focusRequested = true;
    }

    public void Draw(in EditorContext context)
    {
        _ = context;
        if (_focusRequested)
        {
            ImGui.SetNextWindowFocus();
            _focusRequested = false;
        }

        if (!ImGui.Begin(
                Title,
                ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse |
                ImGuiWindowFlags.NoNavInputs))
        {
            ClearInputState();
            ImGui.End();
            return;
        }

        RenderViewportTexture texture = _textureProvider();
        if (!texture.IsValid)
        {
            LastViewportSnapshot = GameViewViewportSnapshot.Empty;
            ClearInputState();
            ImGui.TextUnformatted(EditorLocalization.Get("game.waiting", "Waiting for the Game View texture"));
            ImGui.End();
            return;
        }

        Vector2 available = ImGui.GetContentRegionAvail();
        Vector2 imageMinScreen = ImGui.GetCursorScreenPos();
        Vector2 panelOriginScreen = ImGui.GetWindowPos();
        Vector2 framebufferScale = ImGui.GetIO().DisplayFramebufferScale;
        framebufferScale = new Vector2(NormalizeScale(framebufferScale.X), NormalizeScale(framebufferScale.Y));
        LastFramebufferScale = framebufferScale;
        LastPanelOriginFramebuffer = panelOriginScreen * framebufferScale;
        LastViewportSnapshot = GameViewViewportSnapshot.Create(
            texture.Width,
            texture.Height,
            imageMinScreen - panelOriginScreen,
            available);

        Vector2 mousePanel = ImGui.GetIO().MousePos - panelOriginScreen;
        LastPointerPanelPoint = mousePanel;
        PointerHovered = ImGui.IsWindowHovered() && LastViewportSnapshot.ContainsPanelPoint(mousePanel);
        // 键盘焦点与 mouse hover 必须独立：Unity 风格 Game View 点击/自动聚焦后，即使鼠标暂时
        // 离开图像，WASD 仍归 gameplay；popup/modal 获取焦点时这里会自然变为 false。
        KeyboardFocused = ImGui.IsWindowFocused();

        ImGui.Image(
            ViewportPanel.CreateTextureRef(texture.Handle),
            LastViewportSnapshot.ImageRect.Size,
            new Vector2(0f, 1f),
            new Vector2(1f, 0f));
        ImGui.End();
    }

    private void ClearInputState()
    {
        KeyboardFocused = false;
        PointerHovered = false;
        LastPointerPanelPoint = default;
        LastPanelOriginFramebuffer = default;
        LastFramebufferScale = Vector2.One;
    }

    private static float NormalizeScale(float scale)
    {
        return float.IsFinite(scale) && scale > 0f ? scale : 1f;
    }
}
