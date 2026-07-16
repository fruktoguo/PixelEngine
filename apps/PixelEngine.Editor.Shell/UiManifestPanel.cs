using Hexa.NET.ImGui;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// content/ui/ui-manifest.json 的可视化登记与 preload 管理面板。
/// </summary>
[EditorUiSurface("editor.panel.ui-manifest")]
internal sealed class UiManifestPanel(EditorAssetManifestStore assets) : IEditorPanel
{
    public const string PanelTitle = "UI Manifest";
    private readonly EditorAssetManifestStore _assets = assets ?? throw new ArgumentNullException(nameof(assets));
    private IReadOnlyList<EditorUiManifestScreenEntry> _screens = [];
    private bool _loaded;

    public string Title => PanelTitle;

    public bool Visible { get; set; } = true;

    internal string Status { get; private set; } = "就绪";

    internal event Action? Changed;

    internal IReadOnlyList<EditorUiManifestScreenEntry> CaptureScreens()
    {
        if (!_loaded)
        {
            _screens = _assets.ListUiManifestScreens();
            _loaded = true;
        }

        return _screens;
    }

    internal IReadOnlyList<EditorUiManifestScreenEntry> RefreshScreens()
    {
        _screens = _assets.ListUiManifestScreens();
        _loaded = true;
        return _screens;
    }

    internal EditorUiManifestSyncResult SyncScreens()
    {
        EditorUiManifestSyncResult result = _assets.SyncUiManifestScreens();
        Status = result.Diagnostic;
        _ = RefreshScreens();
        if (result.RegisteredScreens > 0)
        {
            Changed?.Invoke();
        }

        return result;
    }

    internal bool TrySetPreload(string screenId, bool preload)
    {
        EditorUiManifestScreenEntry? before = CaptureScreens().FirstOrDefault(screen =>
            string.Equals(screen.Id, screenId, StringComparison.OrdinalIgnoreCase));
        bool updated = _assets.TrySetUiManifestScreenPreload(screenId, preload, out string diagnostic);
        Status = diagnostic;
        if (updated)
        {
            _ = RefreshScreens();
            if (before is { } previous && previous.Preload != preload)
            {
                Changed?.Invoke();
            }
        }

        return updated;
    }

    [EditorUiCommands(
        "panel.ui-manifest",
        "panel.ui-manifest.sync",
        "panel.ui-manifest.refresh")]
    public void Draw(in EditorContext context)
    {
        _ = context;
        bool visible = Visible;
        if (!ImGui.Begin(Title, ref visible))
        {
            Visible = visible;
            ImGui.End();
            return;
        }

        Visible = visible;
        if (ImGui.Button("同步 UI Screens"))
        {
            _ = SyncScreens();
        }

        ImGui.SameLine();
        if (ImGui.Button("刷新"))
        {
            _ = RefreshScreens();
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(Status);
        ImGui.SeparatorText("Screens");
        IReadOnlyList<EditorUiManifestScreenEntry> screens = CaptureScreens();
        if (screens.Count == 0)
        {
            ImGui.TextUnformatted("ui-manifest.json 未登记 screen。");
            ImGui.End();
            return;
        }

        if (ImGui.BeginTable("ui-manifest-screens", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Id");
            ImGui.TableSetupColumn("Path");
            ImGui.TableSetupColumn("Preload");
            ImGui.TableSetupColumn("Asset");
            ImGui.TableSetupColumn("File");
            ImGui.TableHeadersRow();
            for (int i = 0; i < screens.Count; i++)
            {
                DrawScreenRow(screens[i]);
            }

            ImGui.EndTable();
        }

        ImGui.End();
    }

    [EditorUiCommands("panel.ui-manifest.preload")]
    private void DrawScreenRow(EditorUiManifestScreenEntry screen)
    {
        ImGui.TableNextRow();
        _ = ImGui.TableNextColumn();
        ImGui.TextUnformatted(screen.Id);
        _ = ImGui.TableNextColumn();
        ImGui.TextUnformatted(screen.Path);
        _ = ImGui.TableNextColumn();
        bool preload = screen.Preload;
        if (ImGui.Checkbox($"##preload-{screen.Id}", ref preload))
        {
            _ = TrySetPreload(screen.Id, preload);
        }

        _ = ImGui.TableNextColumn();
        ImGui.TextUnformatted(screen.AssetId ?? "missing asset");
        _ = ImGui.TableNextColumn();
        ImGui.TextUnformatted(screen.FileExists ? "ok" : "missing file");
    }
}
