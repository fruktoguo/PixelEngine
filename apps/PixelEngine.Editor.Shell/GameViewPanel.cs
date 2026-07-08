using Hexa.NET.ImGui;
using PixelEngine.Rendering;
using System.Numerics;

namespace PixelEngine.Editor.Shell;

internal sealed class GameViewPanel(Func<RenderViewportTexture> textureProvider) : IEditorPanel
{
    private readonly Func<RenderViewportTexture> _textureProvider = textureProvider ?? throw new ArgumentNullException(nameof(textureProvider));
    private bool _visible = true;

    public string Title => EditorDockSpace.GameViewWindowTitle;

    public bool Visible
    {
        get => _visible;
        set
        {
            _visible = value;
            if (!value)
            {
                ClearInputState();
            }
        }
    }

    public bool InputFocused { get; private set; }

    public bool InputHovered { get; private set; }

    public Vector2 LastPointerPanelPoint { get; private set; }

    public Vector2 LastPanelOriginFramebuffer { get; private set; }

    public Vector2 LastFramebufferScale { get; private set; } = Vector2.One;

    public GameViewViewportSnapshot LastViewportSnapshot { get; private set; } = GameViewViewportSnapshot.Empty;

    public EditorViewportContract CaptureContract(PixelEngine.Editor.EditorMode mode)
    {
        return EditorGameViewContract.GameView(mode);
    }

    public void Draw(in EditorContext context)
    {
        _ = context;
        bool visible = Visible;
        if (!ImGui.Begin(Title, ref visible, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            Visible = visible;
            ClearInputState();
            ImGui.End();
            return;
        }

        Visible = visible;
        RenderViewportTexture texture = _textureProvider();
        if (!texture.IsValid)
        {
            LastViewportSnapshot = GameViewViewportSnapshot.Empty;
            ClearInputState();
            ImGui.TextUnformatted("等待游戏视图纹理");
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
        InputHovered = ImGui.IsWindowHovered() && LastViewportSnapshot.ContainsPanelPoint(mousePanel);
        InputFocused = InputHovered && (ImGui.IsWindowFocused() || ImGui.IsWindowHovered());

        ImGui.Image(
            ViewportPanel.CreateTextureRef(texture.Handle),
            LastViewportSnapshot.ImageRect.Size,
            new Vector2(0f, 1f),
            new Vector2(1f, 0f));
        ImGui.End();
    }

    private void ClearInputState()
    {
        InputFocused = false;
        InputHovered = false;
        LastPointerPanelPoint = default;
        LastPanelOriginFramebuffer = default;
        LastFramebufferScale = Vector2.One;
    }

    private static float NormalizeScale(float scale)
    {
        return float.IsFinite(scale) && scale > 0f ? scale : 1f;
    }
}
