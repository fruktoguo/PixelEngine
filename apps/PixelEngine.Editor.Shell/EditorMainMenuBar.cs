using System.Numerics;
using Hexa.NET.ImGui;
using PixelEngine.Editor.Shell.Build;
using PixelEngine.Editor.Shell.Settings;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Editor Shell 顶部菜单栏：文件、编辑、构建与帮助。
/// </summary>
internal sealed class EditorMainMenuBar
{
    private const float ToolbarHeight = 36f;
    private const float ToolbarButtonWidth = 92f;
    private const string ToolbarWindowName = "##PixelEngineEditorToolbar";
    private const ImGuiWindowFlags ToolbarWindowFlags =
        ImGuiWindowFlags.NoTitleBar |
        ImGuiWindowFlags.NoResize |
        ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoScrollWithMouse |
        ImGuiWindowFlags.NoSavedSettings |
        ImGuiWindowFlags.NoDocking;

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
        DrawToolbar(app);
    }

    internal static EditorMainToolbarState CaptureToolbarState(EditorShellApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        EditorProjectSession? session = app.CurrentSession;
        bool hasSession = session is not null;
        Hosting.EditorPlaySessionSnapshot playSession = hasSession
            ? session!.CaptureEditorPlaySession()
            : default;
        bool isPlaying = hasSession && playSession.Mode == Hosting.EditorMode.Play;
        string projectName = app.CurrentProject?.Name ?? "No Project";
        string sceneName = session?.CurrentSceneDisplayName ?? "No Scene";
        int objectCount = session?.SceneModel.Count ?? 0;

        return new EditorMainToolbarState(
            app.HasOpenProject,
            hasSession,
            isPlaying,
            session?.SceneModel.IsDirty == true,
            projectName,
            sceneName,
            objectCount,
            hasSession ? playSession.Mode.ToString() : "No Project");
    }

    private static void DrawToolbar(EditorShellApp app)
    {
        EditorMainToolbarState state = CaptureToolbarState(app);
        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        if (!ImGuiP.BeginViewportSideBar(ToolbarWindowName, viewport, ImGuiDir.Up, ToolbarHeight, ToolbarWindowFlags))
        {
            ImGui.End();
            return;
        }

        if (ToolbarButton("New Project", enabled: true))
        {
            app.FocusProjectPicker(ProjectPickerMode.NewProject);
        }

        ImGui.SameLine();
        if (ToolbarButton("Open Project", enabled: true))
        {
            app.FocusProjectPicker(ProjectPickerMode.OpenProject);
        }

        ImGui.SameLine();
        if (ToolbarButton("Save Scene", state.HasSession))
        {
            _ = app.SaveScene();
        }

        ImGui.SameLine();
        if (ToolbarButton("Build", state.HasOpenProject))
        {
            app.ShowBuildSettings();
        }

        float playControlsX = Math.Max(390f, (viewport.Size.X * 0.5f) - (((ToolbarButtonWidth * 3f) + 16f) * 0.5f));
        ImGui.SameLine(playControlsX);
        if (ToolbarButton("Play", state.CanEnterPlay))
        {
            app.EnterPlayMode();
        }

        ImGui.SameLine();
        if (ToolbarButton("Pause", state.CanEnterEdit))
        {
            app.EnterEditMode();
        }

        ImGui.SameLine();
        if (ToolbarButton("Step", state.HasSession))
        {
            app.StepOnce();
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(state.StatusText);
        ImGui.End();
    }

    private static bool ToolbarButton(string label, bool enabled)
    {
        if (!enabled)
        {
            ImGui.BeginDisabled();
        }

        bool clicked = ImGui.Button(label, new Vector2(ToolbarButtonWidth, 0f));
        if (!enabled)
        {
            ImGui.EndDisabled();
        }

        return clicked && enabled;
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
        DrawPanelMenuItem(app, "Profiler", EditorDockSpace.PerformanceHudWindowTitle);
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

internal readonly record struct EditorMainToolbarState(
    bool HasOpenProject,
    bool HasSession,
    bool IsPlaying,
    bool IsDirty,
    string ProjectName,
    string SceneName,
    int ObjectCount,
    string Mode)
{
    public bool CanEnterPlay => HasSession && !IsPlaying;

    public bool CanEnterEdit => HasSession && IsPlaying;

    public string StatusText =>
        HasOpenProject
            ? $"{ProjectName} / {SceneName}{(IsDirty ? "*" : string.Empty)} / {Mode} / {ObjectCount} objects"
            : "No Project";
}
