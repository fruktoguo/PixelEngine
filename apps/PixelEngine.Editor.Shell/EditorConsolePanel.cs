using Hexa.NET.ImGui;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Console 面板 ImGui 绘制与过滤器 UI。
/// </summary>
internal sealed class EditorConsolePanel(EditorShellApp app) : IEditorPanel
{
    private static readonly string[] CategoryOptions = ["All", .. Enum.GetNames<EditorConsoleCategory>()];
    private static readonly string[] SeverityOptions = ["All", .. Enum.GetNames<EditorConsoleSeverity>()];
    private readonly EditorShellApp _app = app ?? throw new ArgumentNullException(nameof(app));
    private int _categoryIndex;
    private int _minimumSeverityIndex;
    private string _sourceFilter = string.Empty;
    private string _textFilter = string.Empty;
    private bool _newestFirst = true;
    private bool _autoScroll = true;

    public string Title => EditorDockSpace.ConsoleDiagnosticsWindowTitle;

    public bool Visible { get; set; } = true;

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
        DrawProjectHeader();
        ImGui.SeparatorText("过滤");
        DrawFilters();
        ImGui.SeparatorText("事件");
        DrawEntries();
        ImGui.End();
    }

    private void DrawProjectHeader()
    {
        if (_app.CurrentProject is { } project)
        {
            ImGui.TextUnformatted($"Project: {project.Name}");
            ImGui.TextUnformatted(project.ProjectRoot);
            ImGui.TextUnformatted($"Scene: {_app.CurrentSession?.CurrentSceneDisplayName ?? project.ResolveDisplaySceneName(_app.SceneOverridePath)}");
            ImGui.TextUnformatted($"Panels: {_app.CurrentSession?.PanelCount ?? 0}");
            ImGui.TextUnformatted($"Bridge frames: {_app.CurrentSession?.EditorBridgeFrameCount ?? 0}");
        }
        else
        {
            ImGui.TextUnformatted("No project");
        }

        ImGui.TextUnformatted($"Console entries: {_app.ConsoleStore.Count}");
    }

    private void DrawFilters()
    {
        _ = ImGui.Combo("Category", ref _categoryIndex, CategoryOptions, CategoryOptions.Length);
        _ = ImGui.Combo("Min Severity", ref _minimumSeverityIndex, SeverityOptions, SeverityOptions.Length);
        _ = ImGui.InputText("Source", ref _sourceFilter, 128);
        _ = ImGui.InputText("Text", ref _textFilter, 256);
        _ = ImGui.Checkbox("Newest first", ref _newestFirst);
        ImGui.SameLine();
        _ = ImGui.Checkbox("Auto scroll", ref _autoScroll);
        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            _app.ConsoleStore.Clear();
        }
    }

    private void DrawEntries()
    {
        EditorConsoleEntry[] entries = _app.ConsoleStore.Snapshot(BuildFilter(), _newestFirst);
        if (entries.Length == 0)
        {
            ImGui.TextUnformatted("No console entries");
            return;
        }

        if (ImGui.BeginTable("editor_console_entries", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY))
        {
            ImGui.TableSetupColumn("Time");
            ImGui.TableSetupColumn("Category");
            ImGui.TableSetupColumn("Severity");
            ImGui.TableSetupColumn("Source");
            ImGui.TableSetupColumn("Text");
            ImGui.TableHeadersRow();
            for (int i = 0; i < entries.Length; i++)
            {
                EditorConsoleEntry entry = entries[i];
                ImGui.TableNextRow();
                _ = ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture));
                _ = ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.Category.ToString());
                _ = ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.Severity.ToString());
                _ = ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.Source);
                _ = ImGui.TableNextColumn();
                ImGui.TextWrapped(entry.Text);
            }

            if (_autoScroll)
            {
                ImGui.SetScrollHereY(_newestFirst ? 0 : 1);
            }

            ImGui.EndTable();
        }
    }

    private EditorConsoleFilter BuildFilter()
    {
        return new EditorConsoleFilter
        {
            Category = _categoryIndex <= 0 ? null : (EditorConsoleCategory)(_categoryIndex - 1),
            MinimumSeverity = _minimumSeverityIndex <= 0 ? null : (EditorConsoleSeverity)(_minimumSeverityIndex - 1),
            SourceContains = string.IsNullOrWhiteSpace(_sourceFilter) ? null : _sourceFilter,
            TextContains = string.IsNullOrWhiteSpace(_textFilter) ? null : _textFilter,
        };
    }
}
