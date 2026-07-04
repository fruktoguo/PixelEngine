using Hexa.NET.ImGui;

namespace PixelEngine.Editor.Shell;

internal sealed class ProjectPickerWindow
{
    public void Draw(EditorShellOptions options)
    {
        ImGui.SetNextWindowPos(new System.Numerics.Vector2(24, 24), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(520, 260), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Project Picker"))
        {
            ImGui.End();
            return;
        }

        ImGui.TextUnformatted("PixelEngine Editor");
        ImGui.Separator();
        ImGui.TextUnformatted(string.IsNullOrWhiteSpace(options.ProjectPath)
            ? "No project selected"
            : $"Project: {options.ProjectPath}");
        ImGui.TextUnformatted(string.IsNullOrWhiteSpace(options.ScenePath)
            ? "Scene: default"
            : $"Scene: {options.ScenePath}");
        ImGui.Spacing();
        ImGui.TextUnformatted("Project creation, recent projects, and full editor panels are implemented by the next shell slices.");
        ImGui.End();
    }
}
