using Hexa.NET.ImGui;
using PixelEngine.Editor.Shell.Build;
using PixelEngine.Editor.Shell.Settings;

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

        if (ImGui.BeginMenu("Open Recent", app.RecentProjects.Entries.Count != 0))
        {
            foreach (RecentProjectEntry entry in app.RecentProjects.Entries)
            {
                bool exists = File.Exists(Path.Combine(entry.ProjectPath, EditorProject.ProjectFileName));
                if (ImGui.MenuItem(entry.Name, entry.ProjectPath, selected: false, enabled: exists))
                {
                    app.OpenProjectPath(entry.ProjectPath);
                }
            }

            ImGui.EndMenu();
        }

        ImGui.Separator();
        if (ImGui.MenuItem("New Scene", string.Empty, selected: false, enabled: app.HasOpenProject))
        {
            _ = app.NewScene();
        }

        if (ImGui.BeginMenu("Open Scene", app.CurrentProject?.Scenes.Count > 0))
        {
            foreach (EditorProjectSceneEntry scene in app.CurrentProject!.Scenes)
            {
                if (ImGui.MenuItem(scene.Name, scene.Path, selected: false, enabled: app.HasOpenProject))
                {
                    _ = app.OpenScene(scene.Path);
                }
            }

            ImGui.EndMenu();
        }

        if (ImGui.MenuItem("Save Scene", "Ctrl+S", selected: false, enabled: app.HasOpenProject))
        {
            _ = app.SaveScene();
        }

        if (ImGui.MenuItem("Save Scene As...", "Ctrl+Shift+S", selected: false, enabled: app.HasOpenProject))
        {
            _ = app.SaveSceneAs();
        }
        ImGui.Separator();
        if (ImGui.MenuItem("Project Settings...", string.Empty, selected: false, enabled: app.HasOpenProject))
        {
            app.ShowProjectSettings();
        }

        if (ImGui.MenuItem("Player Settings...", string.Empty, selected: false, enabled: app.HasOpenProject))
        {
            app.ShowPlayerSettings();
        }

        if (ImGui.MenuItem("Build Settings...", string.Empty, selected: false, enabled: app.HasOpenProject))
        {
            app.ShowBuildSettings();
        }
        ImGui.Separator();
        if (ImGui.MenuItem("Close Project", string.Empty, selected: false, enabled: app.HasOpenProject))
        {
            app.CloseProject();
        }

        if (ImGui.MenuItem("Exit"))
        {
            app.RequestExit();
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
        if (ImGui.MenuItem("Delete", "Del", selected: false, enabled: app.CurrentSession?.SceneModel.SelectedStableId is not null))
        {
            app.DeleteSelectedGameObject();
        }

        if (ImGui.MenuItem("Duplicate", "Ctrl+D", selected: false, enabled: app.CurrentSession?.SceneModel.SelectedStableId is not null))
        {
            app.DuplicateSelectedGameObject();
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

        if (ImGui.MenuItem("Create Empty Child", string.Empty, selected: false, enabled: app.CurrentSession?.SceneModel.SelectedStableId is not null))
        {
            app.CreateChildGameObject();
        }

        if (ImGui.BeginMenu("Create with Component", app.CurrentSession is not null))
        {
            string[] behaviours = app.GetBehaviourTypeNames();
            if (behaviours.Length == 0)
            {
                _ = ImGui.MenuItem("No Behaviour", string.Empty, selected: false, enabled: false);
            }
            else
            {
                for (int i = 0; i < behaviours.Length; i++)
                {
                    if (ImGui.MenuItem(behaviours[i]))
                    {
                        app.CreateGameObject();
                        app.AddComponentToSelected(behaviours[i]);
                    }
                }
            }

            ImGui.EndMenu();
        }

        ImGui.Separator();
        if (ImGui.MenuItem("Rename", string.Empty, selected: false, enabled: app.CurrentSession?.SceneModel.SelectedStableId is not null))
        {
            app.RenameSelectedGameObject();
        }

        if (ImGui.MenuItem("Delete", string.Empty, selected: false, enabled: app.CurrentSession?.SceneModel.SelectedStableId is not null))
        {
            app.DeleteSelectedGameObject();
        }

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

        DrawPanelMenuItem(app, "Hierarchy", EditorDockSpace.SceneHierarchyWindowTitle);
        DrawPanelMenuItem(app, "Scene View", EditorDockSpace.ViewportWindowTitle);
        DrawPanelMenuItem(app, "Game View", EditorDockSpace.GameViewWindowTitle);
        DrawPanelMenuItem(app, "Inspector", EditorDockSpace.InspectorWindowTitle);
        DrawPanelMenuItem(app, "Project", EditorDockSpace.AssetBrowserWindowTitle);
        DrawPanelMenuItem(app, "Console", EditorDockSpace.ConsoleDiagnosticsWindowTitle);
        DrawPanelMenuItem(app, "Performance HUD", EditorDockSpace.PerformanceHudWindowTitle);
        DrawPanelMenuItem(app, "Project Settings...", ProjectSettingsPanel.PanelTitle);
        DrawPanelMenuItem(app, "Player Settings...", PlayerSettingsPanel.PanelTitle);
        DrawPanelMenuItem(app, "Build Settings...", BuildSettingsPanel.PanelTitle);
        ImGui.Separator();
        if (ImGui.MenuItem("Reset Layout"))
        {
            app.ResetLayout();
        }

        ImGui.EndMenu();
    }

    private static void DrawPanelMenuItem(EditorShellApp app, string label, string panelTitle)
    {
        if (ImGui.MenuItem(label, string.Empty, selected: false, enabled: app.HasOpenProject))
        {
            _ = app.ShowPanel(panelTitle);
        }
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
        ImGui.Separator();
        if (ImGui.MenuItem("About"))
        {
            ImGui.OpenPopup("About PixelEngine Editor");
        }

        if (ImGui.MenuItem("Shortcuts"))
        {
            ImGui.OpenPopup("PixelEngine Editor Shortcuts");
        }

        if (ImGui.BeginPopup("About PixelEngine Editor"))
        {
            ImGui.TextUnformatted("PixelEngine Editor");
            ImGui.TextUnformatted("Standalone editor shell");
            ImGui.EndPopup();
        }

        if (ImGui.BeginPopup("PixelEngine Editor Shortcuts"))
        {
            ImGui.TextUnformatted("Ctrl+S Save Scene");
            ImGui.TextUnformatted("Ctrl+Z Undo");
            ImGui.TextUnformatted("Ctrl+Y Redo");
            ImGui.TextUnformatted("Ctrl+P Play");
            ImGui.EndPopup();
        }

        ImGui.EndMenu();
    }
}
