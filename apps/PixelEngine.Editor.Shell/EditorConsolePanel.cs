using Hexa.NET.ImGui;

namespace PixelEngine.Editor.Shell;

internal sealed class EditorConsolePanel(EditorShellApp app) : IEditorPanel
{
    private readonly EditorShellApp _app = app ?? throw new ArgumentNullException(nameof(app));

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

        if (!string.IsNullOrWhiteSpace(_app.LastProjectError))
        {
            ImGui.Separator();
            ImGui.TextWrapped(_app.LastProjectError);
        }

        if (!string.IsNullOrWhiteSpace(_app.LastAssetOpenDiagnostic))
        {
            ImGui.Separator();
            ImGui.TextWrapped($"Asset opener: {_app.LastAssetOpenDiagnostic}");
        }

        ImGui.End();
    }
}
