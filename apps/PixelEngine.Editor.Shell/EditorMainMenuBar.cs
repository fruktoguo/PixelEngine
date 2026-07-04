using Hexa.NET.ImGui;

namespace PixelEngine.Editor.Shell;

internal sealed class EditorMainMenuBar
{
    public void Draw(EditorShellApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!ImGui.BeginMainMenuBar())
        {
            return;
        }

        DrawFileMenu(app);
        DrawEditMenu(app);
        DrawGameObjectMenu(app);
        DrawWindowMenu(app);
        DrawPlayMenu(app);
        DrawHelpMenu(app);
        ImGui.EndMainMenuBar();
    }

    private static void DrawFileMenu(EditorShellApp app)
    {
        if (!ImGui.BeginMenu("File"))
        {
            return;
        }

        if (ImGui.MenuItem("New Project..."))
        {
            app.FocusProjectPicker(ProjectPickerMode.NewProject);
        }

        if (ImGui.MenuItem("Open Project..."))
        {
            app.FocusProjectPicker(ProjectPickerMode.OpenProject);
        }

        _ = ImGui.MenuItem("Save Scene", "Ctrl+S", selected: false, enabled: app.HasOpenProject);
        _ = ImGui.MenuItem("Save Scene As...", "Ctrl+Shift+S", selected: false, enabled: app.HasOpenProject);
        ImGui.Separator();
        _ = ImGui.MenuItem("Build Settings...", string.Empty, selected: false, enabled: app.HasOpenProject);
        ImGui.Separator();
        if (ImGui.MenuItem("Close Project", string.Empty, selected: false, enabled: app.HasOpenProject))
        {
            app.CloseProject();
        }

        ImGui.EndMenu();
    }

    private static void DrawEditMenu(EditorShellApp app)
    {
        if (!ImGui.BeginMenu("Edit"))
        {
            return;
        }

        if (ImGui.MenuItem("Undo", "Ctrl+Z", selected: false, enabled: app.CurrentSession?.UndoStack.CanUndo == true))
        {
            _ = app.Undo();
        }

        if (ImGui.MenuItem("Redo", "Ctrl+Y", selected: false, enabled: app.CurrentSession?.UndoStack.CanRedo == true))
        {
            _ = app.Redo();
        }
        ImGui.Separator();
        if (ImGui.MenuItem("Reset Layout"))
        {
            app.ResetLayout();
        }

        ImGui.EndMenu();
    }

    private static void DrawGameObjectMenu(EditorShellApp app)
    {
        if (!ImGui.BeginMenu("GameObject"))
        {
            return;
        }

        if (ImGui.MenuItem("Create Empty", string.Empty, selected: false, enabled: app.CurrentSession is not null))
        {
            app.CreateGameObject();
        }

        _ = ImGui.MenuItem("Create Script Object", string.Empty, selected: false, enabled: false);
        ImGui.EndMenu();
    }

    private static void DrawWindowMenu(EditorShellApp app)
    {
        if (!ImGui.BeginMenu("Window"))
        {
            return;
        }

        if (ImGui.MenuItem("Project Picker"))
        {
            app.FocusProjectPicker(ProjectPickerMode.OpenProject);
        }

        _ = ImGui.MenuItem("Hierarchy", string.Empty, selected: false, enabled: app.HasOpenProject);
        _ = ImGui.MenuItem("Inspector", string.Empty, selected: false, enabled: app.HasOpenProject);
        _ = ImGui.MenuItem("Project", string.Empty, selected: false, enabled: app.HasOpenProject);
        _ = ImGui.MenuItem("Console", string.Empty, selected: false, enabled: app.HasOpenProject);
        _ = ImGui.MenuItem("Performance HUD", string.Empty, selected: false, enabled: app.HasOpenProject);
        ImGui.EndMenu();
    }

    private static void DrawPlayMenu(EditorShellApp app)
    {
        if (!ImGui.BeginMenu("Play"))
        {
            return;
        }

        if (ImGui.MenuItem("Play", "Ctrl+P", selected: false, enabled: app.CurrentSession is not null))
        {
            app.EnterPlayMode();
        }

        if (ImGui.MenuItem("Pause", string.Empty, selected: false, enabled: app.CurrentSession is not null))
        {
            app.EnterEditMode();
        }

        if (ImGui.MenuItem("Step", string.Empty, selected: false, enabled: app.CurrentSession is not null))
        {
            app.StepOnce();
        }

        ImGui.EndMenu();
    }

    private static void DrawHelpMenu(EditorShellApp app)
    {
        if (!ImGui.BeginMenu("Help"))
        {
            return;
        }

        ImGui.TextUnformatted("PixelEngine Editor");
        ImGui.TextUnformatted(app.HasOpenProject ? app.CurrentProject!.Name : "No Project");
        ImGui.EndMenu();
    }
}
