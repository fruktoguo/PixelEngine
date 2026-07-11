using System.Numerics;
using Hexa.NET.ImGui;
using PixelEngine.Editor.Shell.Build;
using PixelEngine.Editor.Shell.Settings;
using L = PixelEngine.Editor.EditorLocalization;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Editor Shell 顶部菜单栏：文件、编辑、构建与帮助。
/// </summary>
internal sealed class EditorMainMenuBar
{
    private const float ToolbarHeight = 36f;
    private const float ToolbarButtonWidth = 92f;
    private const float PlayControlButtonSize = 28f;
    private const float PlayControlGap = 4f;
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
        DispatchShortcuts(app);
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
        bool isPaused = hasSession && playSession.Mode == Hosting.EditorMode.Paused;
        string projectName = app.CurrentProject?.Name ?? "No Project";
        string sceneName = session?.CurrentSceneDisplayName ?? "No Scene";
        int objectCount = session?.SceneModel.Count ?? 0;

        return new EditorMainToolbarState(
            app.HasOpenProject,
            hasSession,
            isPlaying,
            isPaused,
            session?.SceneModel.IsDirty == true,
            projectName,
            sceneName,
            objectCount,
            hasSession ? playSession.Mode.ToString() : "No Project");
    }

    private static void DrawToolbar(EditorShellApp app)
    {
        EditorMainToolbarState state = CaptureToolbarState(app);
        float uiScale = app.UiScale;
        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        if (!ImGuiP.BeginViewportSideBar(
            ToolbarWindowName,
            viewport,
            ImGuiDir.Up,
            EditorUiScale.Scale(ToolbarHeight, uiScale),
            ToolbarWindowFlags))
        {
            ImGui.End();
            return;
        }

        if (ToolbarButton(L.Get("action.newProject", "New Project"), enabled: true, uiScale: uiScale))
        {
            app.FocusProjectPicker(ProjectPickerMode.NewProject);
        }

        ImGui.SameLine();
        if (ToolbarButton(L.Get("action.openProject", "Open Project"), enabled: true, uiScale: uiScale))
        {
            app.FocusProjectPicker(ProjectPickerMode.OpenProject);
        }

        ImGui.SameLine();
        if (ToolbarButton(L.Get("action.saveScene", "Save Scene"), state.HasSession, uiScale))
        {
            _ = app.SaveScene();
        }

        ImGui.SameLine();
        if (ToolbarButton(L.Get("action.build", "Build"), state.HasOpenProject, uiScale))
        {
            app.ShowBuildSettings();
        }

        float scaledPlayButtonSize = EditorUiScale.Scale(PlayControlButtonSize, uiScale);
        float scaledPlayGap = EditorUiScale.Scale(PlayControlGap, uiScale);
        float playControlsWidth = (scaledPlayButtonSize * 3f) + (scaledPlayGap * 2f);
        float playControlsX = Math.Max(
            EditorUiScale.Scale(390f, uiScale),
            (viewport.Size.X * 0.5f) - (playControlsWidth * 0.5f));
        ImGui.SameLine(playControlsX);
        if (PlayControlButton(
            EditorToolbarPlayIcon.Play,
            state.IsPlaySessionActive,
            state.HasSession,
            state.IsPlaySessionActive ? L.Get("action.stop", "Stop") : L.Get("action.play", "Play"),
            uiScale))
        {
            TogglePlayMode(app, state);
        }

        ImGui.SameLine(0f, scaledPlayGap);
        if (PlayControlButton(
            EditorToolbarPlayIcon.Pause,
            state.IsPaused,
            state.CanPause,
            state.IsPaused ? L.Get("action.resume", "Resume") : L.Get("action.pause", "Pause"),
            uiScale))
        {
            app.TogglePauseMode();
        }

        ImGui.SameLine(0f, scaledPlayGap);
        if (PlayControlButton(
            EditorToolbarPlayIcon.Step,
            selected: false,
            state.CanStep,
            L.Get("action.step", "Step"),
            uiScale))
        {
            app.StepOnce();
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(state.StatusText);
        ImGui.End();
    }

    private static bool ToolbarButton(string label, bool enabled, float uiScale)
    {
        if (!enabled)
        {
            ImGui.BeginDisabled();
        }

        bool clicked = ImGui.Button(label, new Vector2(EditorUiScale.Scale(ToolbarButtonWidth, uiScale), 0f));
        if (!enabled)
        {
            ImGui.EndDisabled();
        }

        return clicked && enabled;
    }

    private static bool PlayControlButton(
        EditorToolbarPlayIcon icon,
        bool selected,
        bool enabled,
        string tooltip,
        float uiScale)
    {
        float size = EditorUiScale.Scale(PlayControlButtonSize, uiScale);
        Vector2 min = ImGui.GetCursorScreenPos();
        if (!enabled)
        {
            ImGui.BeginDisabled();
        }

        bool clicked = ImGui.InvisibleButton($"toolbar-play-{icon}", new Vector2(size));
        bool hovered = ImGui.IsItemHovered();
        if (!enabled)
        {
            ImGui.EndDisabled();
        }

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        uint background = selected
            ? 0xFFDA874C
            : hovered && enabled ? 0xFF45474D : 0xFF2C2E33;
        drawList.AddRectFilled(min, min + new Vector2(size), background, EditorUiScale.Scale(4f, uiScale));
        uint foreground = enabled ? 0xFFF1F3F6 : 0xFF70737A;
        float unit = size / 14f;
        Vector2 center = min + new Vector2(size * 0.5f);
        switch (icon)
        {
            case EditorToolbarPlayIcon.Play:
                drawList.AddTriangleFilled(
                    center + new Vector2(-unit * 2.5f, -unit * 3.5f),
                    center + new Vector2(-unit * 2.5f, unit * 3.5f),
                    center + new Vector2(unit * 3.5f, 0f),
                    foreground);
                break;
            case EditorToolbarPlayIcon.Pause:
                drawList.AddRectFilled(
                    center + new Vector2(-unit * 3f, -unit * 3.5f),
                    center + new Vector2(-unit, unit * 3.5f),
                    foreground,
                    unit * 0.5f);
                drawList.AddRectFilled(
                    center + new Vector2(unit, -unit * 3.5f),
                    center + new Vector2(unit * 3f, unit * 3.5f),
                    foreground,
                    unit * 0.5f);
                break;
            case EditorToolbarPlayIcon.Step:
                drawList.AddTriangleFilled(
                    center + new Vector2(-unit * 3.5f, -unit * 3.5f),
                    center + new Vector2(-unit * 3.5f, unit * 3.5f),
                    center + new Vector2(unit * 1.75f, 0f),
                    foreground);
                drawList.AddRectFilled(
                    center + new Vector2(unit * 2.25f, -unit * 3.5f),
                    center + new Vector2(unit * 3.5f, unit * 3.5f),
                    foreground,
                    unit * 0.4f);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(icon), icon, "未知 Editor 播放控制图标。");
        }

        if (hovered)
        {
            ImGui.SetTooltip(tooltip);
        }

        return clicked && enabled;
    }

    internal static void TogglePlayMode(EditorShellApp app, in EditorMainToolbarState state)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!state.HasSession)
        {
            return;
        }

        if (state.ShouldExitPlayOnToggle)
        {
            app.EnterEditMode();
        }
        else
        {
            app.EnterPlayMode();
        }
    }

    private static void DrawFileMenu(EditorShellApp app)
    {
        if (!ImGui.BeginMenu(L.Get("menu.file", "File")))
        {
            return;
        }

        if (ImGui.MenuItem(L.Get("action.newProject", "New Project") + "..."))
        {
            app.FocusProjectPicker(ProjectPickerMode.NewProject);
        }

        if (ImGui.MenuItem(L.Get("action.openProject", "Open Project") + "..."))
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

        if (ImGui.MenuItem(
            L.Get("action.saveScene", "Save Scene"),
            EditorShortcutCatalog.Get(EditorShortcutCommand.SaveScene).DisplayText,
            selected: false,
            enabled: app.HasOpenProject))
        {
            _ = app.SaveScene();
        }

        if (ImGui.MenuItem(
            "Save Scene As...",
            EditorShortcutCatalog.Get(EditorShortcutCommand.SaveSceneAs).DisplayText,
            selected: false,
            enabled: app.HasOpenProject))
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
        if (!ImGui.BeginMenu(L.Get("menu.edit", "Edit")))
        {
            return;
        }

        if (ImGui.MenuItem(
            L.Get("action.undo", "Undo"),
            EditorShortcutCatalog.Get(EditorShortcutCommand.Undo).DisplayText,
            selected: false,
            enabled: app.CurrentSession?.UndoStack.CanUndo == true))
        {
            _ = app.Undo();
        }

        if (ImGui.MenuItem(
            L.Get("action.redo", "Redo"),
            EditorShortcutCatalog.Get(EditorShortcutCommand.Redo).DisplayText,
            selected: false,
            enabled: app.CurrentSession?.UndoStack.CanRedo == true))
        {
            _ = app.Redo();
        }
        ImGui.Separator();
        if (ImGui.MenuItem(L.Get("action.delete", "Delete"), "Del", selected: false, enabled: app.CurrentSession?.SceneModel.SelectedStableId is not null))
        {
            app.DeleteSelectedGameObject();
        }

        if (ImGui.MenuItem(
            L.Get("action.duplicate", "Duplicate"),
            EditorShortcutCatalog.Get(EditorShortcutCommand.Duplicate).DisplayText,
            selected: false,
            enabled: app.CurrentSession?.SceneModel.SelectedStableId is not null))
        {
            app.DuplicateSelectedGameObject();
        }

        ImGui.Separator();
        if (ImGui.MenuItem(
            "Preferences...",
            EditorShortcutCatalog.Get(EditorShortcutCommand.OpenPreferences).DisplayText))
        {
            app.ShowPreferences();
        }

        ImGui.EndMenu();
    }

    private static void DrawGameObjectMenu(EditorShellApp app)
    {
        if (!ImGui.BeginMenu(L.Get("menu.gameObject", "GameObject")))
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
        if (ImGui.MenuItem(L.Get("action.rename", "Rename"), string.Empty, selected: false, enabled: app.CurrentSession?.SceneModel.SelectedStableId is not null))
        {
            app.RenameSelectedGameObject();
        }

        if (ImGui.MenuItem(L.Get("action.delete", "Delete"), string.Empty, selected: false, enabled: app.CurrentSession?.SceneModel.SelectedStableId is not null))
        {
            app.DeleteSelectedGameObject();
        }

        ImGui.EndMenu();
    }

    private static void DrawWindowMenu(EditorShellApp app)
    {
        if (!ImGui.BeginMenu(L.Get("menu.window", "Window")))
        {
            return;
        }

        if (ImGui.MenuItem("Project Picker"))
        {
            app.FocusProjectPicker(ProjectPickerMode.OpenProject);
        }

        if (ImGui.BeginMenu("General"))
        {
            DrawPanelMenuItem(app, "Hierarchy", EditorDockSpace.SceneHierarchyWindowTitle);
            DrawPanelMenuItem(app, "Scene View", EditorDockSpace.ViewportWindowTitle);
            DrawPanelMenuItem(app, "Game View", EditorDockSpace.GameViewWindowTitle);
            DrawPanelMenuItem(app, "Inspector", EditorDockSpace.InspectorWindowTitle);
            DrawPanelMenuItem(app, "Project", EditorDockSpace.AssetBrowserWindowTitle);
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Analysis"))
        {
            DrawPanelMenuItem(app, "Console", EditorDockSpace.ConsoleDiagnosticsWindowTitle);
            DrawPanelMenuItem(app, "Profiler", EditorDockSpace.PerformanceHudWindowTitle);
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Settings"))
        {
            DrawPanelMenuItem(app, "UI Manifest", UiManifestPanel.PanelTitle);
            DrawPanelMenuItem(app, "Project Settings...", ProjectSettingsPanel.PanelTitle);
            DrawPanelMenuItem(app, "Player Settings...", PlayerSettingsPanel.PanelTitle);
            DrawPanelMenuItem(app, "Build Settings...", BuildSettingsPanel.PanelTitle);
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Tools"))
        {
            DrawPanelMenuItem(app, "Materials", EditorDockSpace.MaterialReactionEditorWindowTitle);
            DrawPanelMenuItem(app, "Brush", EditorDockSpace.MaterialBrushWindowTitle);
            DrawPanelMenuItem(app, "World Inspector", EditorDockSpace.WorldInspectorWindowTitle);
            DrawPanelMenuItem(app, "Overlays", EditorDockSpace.DebugOverlayWindowTitle);
            DrawPanelMenuItem(app, "Simulation", EditorDockSpace.SimulationControlWindowTitle);
            DrawPanelMenuItem(app, "Play Mode", EditorDockSpace.EditorModeWindowTitle);
            DrawPanelMenuItem(app, "Save / Load", EditorDockSpace.SaveLoadWindowTitle);
            DrawPanelMenuItem(app, "Physics", EditorDockSpace.PhysicsTuningWindowTitle);
            DrawPanelMenuItem(app, "Particles", EditorDockSpace.ParticleTuningWindowTitle);
            DrawPanelMenuItem(app, "Lighting", EditorDockSpace.LightingTuningWindowTitle);
            ImGui.EndMenu();
        }
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
        if (!ImGui.BeginMenu(L.Get("menu.play", "Play")))
        {
            return;
        }

        EditorMainToolbarState state = CaptureToolbarState(app);
        if (ImGui.MenuItem(
            state.IsPlaySessionActive ? L.Get("action.stop", "Stop") : L.Get("action.play", "Play"),
            EditorShortcutCatalog.Get(EditorShortcutCommand.TogglePlayMode).DisplayText,
            selected: false,
            enabled: state.HasSession))
        {
            TogglePlayMode(app, state);
        }

        if (ImGui.MenuItem(
            state.IsPaused ? L.Get("action.resume", "Resume") : L.Get("action.pause", "Pause"),
            string.Empty,
            selected: false,
            enabled: state.CanPause))
        {
            app.TogglePauseMode();
        }

        if (ImGui.MenuItem(L.Get("action.step", "Step"), string.Empty, selected: false, enabled: state.CanStep))
        {
            app.StepOnce();
        }

        ImGui.EndMenu();
    }

    private static void DrawHelpMenu(EditorShellApp app)
    {
        if (!ImGui.BeginMenu(L.Get("menu.help", "Help")))
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
            app.ShowPreferences(EditorPreferencesCategory.Shortcuts);
        }

        if (ImGui.BeginPopup("About PixelEngine Editor"))
        {
            ImGui.TextUnformatted("PixelEngine Editor");
            ImGui.TextUnformatted("Standalone editor shell");
            ImGui.EndPopup();
        }

        ImGui.EndMenu();
    }

    private static void DispatchShortcuts(EditorShellApp app)
    {
        if (EditorShortcutCatalog.IsPressed(EditorShortcutCommand.OpenPreferences))
        {
            app.ShowPreferences();
        }

        if (app.CurrentSession is null)
        {
            return;
        }

        if (EditorShortcutCatalog.IsPressed(EditorShortcutCommand.SaveSceneAs))
        {
            _ = app.SaveSceneAs();
        }
        else if (EditorShortcutCatalog.IsPressed(EditorShortcutCommand.SaveScene))
        {
            _ = app.SaveScene();
        }

        if (EditorShortcutCatalog.IsPressed(EditorShortcutCommand.Undo))
        {
            _ = app.Undo();
        }

        if (EditorShortcutCatalog.IsPressed(EditorShortcutCommand.Redo))
        {
            _ = app.Redo();
        }

        if (EditorShortcutCatalog.IsPressed(EditorShortcutCommand.Duplicate) &&
            app.CurrentSession.SceneModel.SelectedStableId is not null)
        {
            app.DuplicateSelectedGameObject();
        }

        if (EditorShortcutCatalog.IsPressed(EditorShortcutCommand.TogglePlayMode))
        {
            EditorMainToolbarState state = CaptureToolbarState(app);
            TogglePlayMode(app, state);
        }
    }
}

internal enum EditorToolbarPlayIcon
{
    Play,
    Pause,
    Step,
}

internal readonly record struct EditorMainToolbarState(
    bool HasOpenProject,
    bool HasSession,
    bool IsPlaying,
    bool IsPaused,
    bool IsDirty,
    string ProjectName,
    string SceneName,
    int ObjectCount,
    string Mode)
{
    public bool CanEnterPlay => HasSession && !IsPlaySessionActive;

    public bool CanEnterEdit => HasSession && (IsPlaying || IsPaused);

    public bool CanPause => HasSession && (IsPlaying || IsPaused);

    public bool CanStep => HasSession && IsPlaySessionActive;

    public bool IsPlaySessionActive => IsPlaying || IsPaused;

    public bool ShouldExitPlayOnToggle => IsPlaySessionActive;

    public string StatusText =>
        HasOpenProject
            ? $"{ProjectName} / {SceneName}{(IsDirty ? "*" : string.Empty)} / {Mode} / {ObjectCount} objects"
            : "No Project";
}
