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
                InputFocused = false;
            }
        }
    }

    public bool InputFocused { get; private set; }

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
            InputFocused = false;
            ImGui.End();
            return;
        }

        Visible = visible;
        InputFocused = ImGui.IsWindowHovered() || ImGui.IsWindowFocused();
        RenderViewportTexture texture = _textureProvider();
        if (!texture.IsValid)
        {
            ImGui.TextUnformatted("等待游戏视图纹理");
            ImGui.End();
            return;
        }

        Vector2 available = ImGui.GetContentRegionAvail();
        Vector2 size = ViewportPanel.FitTexture(texture.Width, texture.Height, available);
        ImGui.Image(ViewportPanel.CreateTextureRef(texture.Handle), size, new Vector2(0f, 1f), new Vector2(1f, 0f));
        ImGui.End();
    }
}
