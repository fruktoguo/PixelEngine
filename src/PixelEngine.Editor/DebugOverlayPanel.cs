using Hexa.NET.ImGui;

namespace PixelEngine.Editor;

/// <summary>
/// 调试叠层开关面板。
/// </summary>
public sealed class DebugOverlayPanel(DebugOverlaySettings settings) : IEditorPanel
{
    private readonly DebugOverlaySettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    /// <inheritdoc />
    public string Title => EditorDockSpace.DebugOverlayWindowTitle;

    /// <inheritdoc />
    public bool Visible { get; set; } = true;

    /// <inheritdoc />
    public void Draw(in EditorContext context)
    {
        bool visible = Visible;
        if (!ImGui.Begin(Title, ref visible))
        {
            Visible = visible;
            ImGui.End();
            return;
        }

        Visible = visible;
        DrawToggle("Dirty rect", DebugOverlayFlags.DirtyRects);
        DrawToggle("CA iteration", DebugOverlayFlags.CaIterationRects);
        DrawToggle("Chunk parity", DebugOverlayFlags.ChunkGridParity);
        DrawToggle("KeepAlive", DebugOverlayFlags.KeepAliveHotspots);
        DrawToggle("Cell parity", DebugOverlayFlags.CellParity);
        DrawToggle("Temperature", DebugOverlayFlags.TemperatureHeatmap);
        DrawToggle("Owned body", DebugOverlayFlags.OwnedByBody);
        DrawToggle("Particles", DebugOverlayFlags.ParticleTrails);
        DrawToggle("CCL", DebugOverlayFlags.ConnectedComponents);
        ImGui.End();
    }

    private void DrawToggle(string label, DebugOverlayFlags flag)
    {
        bool enabled = _settings.IsEnabled(flag);
        if (ImGui.Checkbox(label, ref enabled))
        {
            _settings.Set(flag, enabled);
        }
    }
}
