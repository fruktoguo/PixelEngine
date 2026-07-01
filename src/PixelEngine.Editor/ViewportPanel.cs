using Hexa.NET.ImGui;
using PixelEngine.Rendering;
using System.Numerics;

namespace PixelEngine.Editor;

/// <summary>
/// 显示 Rendering 最终画面纹理的世界视口面板。
/// </summary>
#pragma warning disable IDE0290
public sealed class ViewportPanel : IEditorPanel
#pragma warning restore IDE0290
{
    private readonly Func<RenderViewportTexture> _textureProvider;

    /// <summary>
    /// 创建世界视口面板。
    /// </summary>
    /// <param name="textureProvider">最终画面纹理提供器。</param>
    public ViewportPanel(Func<RenderViewportTexture> textureProvider)
    {
        _textureProvider = textureProvider ?? throw new ArgumentNullException(nameof(textureProvider));
    }

    /// <inheritdoc />
    public string Title => EditorDockSpace.ViewportWindowTitle;

    /// <inheritdoc />
    public bool Visible { get; set; } = true;

    /// <summary>
    /// 最近一次读取到的纹理快照。
    /// </summary>
    public RenderViewportTexture LastTexture { get; private set; }

    /// <inheritdoc />
    public void Draw(in EditorContext context)
    {
        LastTexture = _textureProvider();
        bool visible = Visible;
        if (!ImGui.Begin(Title, ref visible, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            Visible = visible;
            ImGui.End();
            return;
        }

        Visible = visible;
        if (!LastTexture.IsValid)
        {
            ImGui.TextUnformatted("等待渲染纹理");
            ImGui.End();
            return;
        }

        Vector2 available = ImGui.GetContentRegionAvail();
        Vector2 size = FitTexture(LastTexture.Width, LastTexture.Height, available);
        ImGui.Image(CreateTextureRef(LastTexture.Handle), size, new Vector2(0f, 1f), new Vector2(1f, 0f));
        ImGui.End();
    }

    /// <summary>
    /// 计算纹理在面板内等比适配后的显示尺寸。
    /// </summary>
    /// <param name="textureWidth">纹理宽度。</param>
    /// <param name="textureHeight">纹理高度。</param>
    /// <param name="available">可用区域。</param>
    /// <returns>显示尺寸。</returns>
    public static Vector2 FitTexture(int textureWidth, int textureHeight, Vector2 available)
    {
        if (textureWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(textureWidth), "纹理宽度必须为正数。");
        }

        if (textureHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(textureHeight), "纹理高度必须为正数。");
        }

        float width = MathF.Max(1f, available.X);
        float height = MathF.Max(1f, available.Y);
        float scale = MathF.Min(width / textureWidth, height / textureHeight);
        scale = MathF.Max(scale, 1f / MathF.Max(textureWidth, textureHeight));
        return new Vector2(MathF.Max(1f, textureWidth * scale), MathF.Max(1f, textureHeight * scale));
    }

    private static unsafe ImTextureRef CreateTextureRef(uint handle)
    {
        return new ImTextureRef(null, (ImTextureID)(ulong)handle);
    }
}
