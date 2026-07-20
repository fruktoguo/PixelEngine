using System.Numerics;
using Hexa.NET.ImGui;
using PixelEngine.Editor.Shell.Build;
using PixelEngine.Editor.Shell.Settings;
using L = PixelEngine.Editor.EditorLocalization;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Editor Shell 顶部菜单栏：文件、编辑、构建与帮助。
/// </summary>
[EditorUiSurface("editor.panel.main-menu")]
internal sealed class EditorMainMenuBar
{
    private const float ToolbarHeight = 38f;
    private const float StatusBarHeight = 22f;
    private const float PlayControlButtonSize = 28f;
    private const float PlayControlGap = 4f;
    private const float LayoutButtonWidth = 72f;
    private const string ToolbarWindowName = "##PixelEngineEditorToolbar";
    private const string StatusBarWindowName = "##PixelEngineEditorStatusBar";
    private const string LayoutPopupName = "##PixelEngineEditorLayoutPopup";
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
        DrawAssetsMenu(app);
        DrawGameObjectMenu(app);
        DrawComponentMenu(app);
        DrawWindowMenu(app);
        DrawPlayMenu(app);
        DrawHelpMenu(app);
        ImGui.EndMainMenuBar();
        DrawToolbar(app);
        DrawStatusBar(app);
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
        string projectName = app.CurrentProject?.Name ?? L.Get("status.noProject", "No Project");
        string sceneName = session?.CurrentSceneDisplayName ?? L.Get("status.noScene", "No Scene");
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
            hasSession
                ? playSession.Mode switch
                {
                    Hosting.EditorMode.Edit => L.Get("mode.edit", "Edit"),
                    Hosting.EditorMode.Play => L.Get("mode.play", "Play"),
                    Hosting.EditorMode.Paused => L.Get("mode.paused", "Paused"),
                    _ => playSession.Mode.ToString(),
                }
                : L.Get("status.noProject", "No Project"));
    }

    [EditorUiCommands(
        "toolbar.play-controls",
        "toolbar.play",
        "toolbar.stop",
        "toolbar.pause",
        "toolbar.step",
        "toolbar.layout.reset")]
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

        float scaledPlayButtonSize = EditorUiScale.Scale(PlayControlButtonSize, uiScale);
        float scaledPlayGap = EditorUiScale.Scale(PlayControlGap, uiScale);
        float playControlsWidth = (scaledPlayButtonSize * 3f) + (scaledPlayGap * 2f);
        float playControlsX = Math.Max(
            ImGui.GetStyle().WindowPadding.X,
            (ImGui.GetWindowSize().X * 0.5f) - (playControlsWidth * 0.5f));
        ImGui.SetCursorPosX(playControlsX);
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

        float scaledLayoutButtonWidth = EditorUiScale.Scale(LayoutButtonWidth, uiScale);
        float layoutX = Math.Max(
            ImGui.GetCursorPosX() + scaledPlayGap,
            ImGui.GetWindowSize().X - ImGui.GetStyle().WindowPadding.X - scaledLayoutButtonWidth);
        ImGui.SameLine(layoutX);
        if (ImGui.Button(L.Get("action.layout", "Layout"), new Vector2(scaledLayoutButtonWidth, 0f)))
        {
            ImGui.OpenPopup(LayoutPopupName);
        }

        if (ImGui.BeginPopup(LayoutPopupName))
        {
            if (ImGui.MenuItem(L.Get("action.resetLayout", "Reset Layout")))
            {
                app.ResetLayout();
            }

            ImGui.EndPopup();
        }

        ImGui.End();
    }

    private static void DrawStatusBar(EditorShellApp app)
    {
        EditorMainToolbarState state = CaptureToolbarState(app);
        float uiScale = app.UiScale;
        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        if (!ImGuiP.BeginViewportSideBar(
            StatusBarWindowName,
            viewport,
            ImGuiDir.Down,
            EditorUiScale.Scale(StatusBarHeight, uiScale),
            ToolbarWindowFlags))
        {
            ImGui.End();
            return;
        }

        Vector4 modeColor = state.IsPlaySessionActive
            ? new Vector4(0x63 / 255f, 0xA6 / 255f, 0xD8 / 255f, 1f)
            : state.IsDirty
                ? new Vector4(0xE0 / 255f, 0xA5 / 255f, 0x43 / 255f, 1f)
                : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled];
        ImGui.PushStyleColor(ImGuiCol.Text, modeColor);
        ImGui.TextUnformatted(state.Mode);
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.TextUnformatted(state.StatusText);
        ImGui.End();
    }

    [EditorUiCommands("menu.component.add")]
    private static void DrawComponentMenu(EditorShellApp app)
    {
        if (!ImGui.BeginMenu(L.Get("menu.component", "Component")))
        {
            return;
        }

        bool canAdd = app.CurrentSession?.SceneModel.SelectedStableId is not null;
        string[] behaviours = app.GetBehaviourTypeNames();
        if (behaviours.Length == 0)
        {
            _ = ImGui.MenuItem(
                L.Get("component.noneAvailable", "No Components Available"),
                string.Empty,
                selected: false,
                enabled: false);
        }
        else
        {
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (ImGui.MenuItem(behaviours[i], string.Empty, selected: false, enabled: canAdd))
                {
                    app.AddComponentToSelected(behaviours[i]);
                }
            }
        }

        ImGui.EndMenu();
    }

    [EditorUiControlPrimitive]
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
            ? 0xFFC5853B
            : hovered && enabled ? 0xFF4A4A4A : 0xFF353535;
        drawList.AddRectFilled(min, min + new Vector2(size), background, EditorUiScale.Scale(2f, uiScale));
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
            _ = ImGui.BeginTooltip();
            ImGui.TextUnformatted(tooltip);
            ImGui.EndTooltip();
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

    [EditorUiCommands(
        "menu.file",
        "menu.file.project",
        "menu.file.new-project",
        "menu.file.open-project",
        "menu.file.open-recent",
        "menu.file.scene",
        "menu.file.new-scene",
        "menu.file.open-scene",
        "menu.file.save",
        "menu.file.save-as",
        "menu.file.project-settings",
        "menu.file.player-settings",
        "menu.file.build-settings",
        "menu.file.build-and-run",
        "menu.file.close-project",
        "menu.file.exit")]
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

        if (ImGui.BeginMenu(L.Get("file.openRecent", "Open Recent"), app.RecentProjects.Entries.Count != 0))
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
        if (ImGui.MenuItem(
            L.Get("file.newScene", "New Scene"),
            string.Empty,
            selected: false,
            enabled: app.HasOpenProject))
        {
            _ = app.NewScene();
        }

        if (ImGui.BeginMenu(L.Get("file.openScene", "Open Scene"), app.CurrentProject?.Scenes.Count > 0))
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
            L.Get("file.saveSceneAs", "Save Scene As..."),
            EditorShortcutCatalog.Get(EditorShortcutCommand.SaveSceneAs).DisplayText,
            selected: false,
            enabled: app.HasOpenProject))
        {
            _ = app.SaveSceneAs();
        }
        ImGui.Separator();
        if (ImGui.MenuItem(
            L.Get("window.projectSettings", "Project Settings..."),
            string.Empty,
            selected: false,
            enabled: app.HasOpenProject))
        {
            app.ShowProjectSettings();
        }

        if (ImGui.MenuItem(
            L.Get("window.playerSettings", "Player Settings..."),
            string.Empty,
            selected: false,
            enabled: app.HasOpenProject))
        {
            app.ShowPlayerSettings();
        }

        if (ImGui.MenuItem(
            L.Get("window.buildSettings", "Build Settings..."),
            EditorShortcutCatalog.Get(EditorShortcutCommand.OpenBuildSettings).DisplayText,
            selected: false,
            enabled: app.HasOpenProject))
        {
            app.ShowBuildSettings();
        }

        if (ImGui.MenuItem(
            L.Get("build.action.buildAndRun", "Build And Run"),
            EditorShortcutCatalog.Get(EditorShortcutCommand.BuildAndRun).DisplayText,
            selected: false,
            enabled: app.HasOpenProject))
        {
            _ = app.TryStartBuild(runAfterBuild: true, out _);
        }
        ImGui.Separator();
        if (ImGui.MenuItem(
            L.Get("file.closeProject", "Close Project"),
            string.Empty,
            selected: false,
            enabled: app.HasOpenProject))
        {
            app.CloseProject();
        }

        if (ImGui.MenuItem(L.Get("file.exit", "Exit")))
        {
            app.RequestExit();
        }

        ImGui.EndMenu();
    }

    [EditorUiCommands(
        "menu.edit.undo",
        "menu.edit.redo",
        "menu.edit.delete",
        "menu.edit.duplicate",
        "menu.edit.preferences")]
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
            L.Get("window.preferencesDialog", "Preferences..."),
            EditorShortcutCatalog.Get(EditorShortcutCommand.OpenPreferences).DisplayText))
        {
            app.ShowPreferences();
        }

        ImGui.EndMenu();
    }

    [EditorUiCommands(
        "menu.game-object.create",
        "menu.game-object.create-empty",
        "menu.game-object.create-child",
        "menu.game-object.create-with-component",
        "menu.game-object.rename",
        "menu.game-object.delete")]
    private static void DrawGameObjectMenu(EditorShellApp app)
    {
        if (!ImGui.BeginMenu(L.Get("menu.gameObject", "GameObject")))
        {
            return;
        }

        if (ImGui.MenuItem(
            L.Get("gameObject.createEmpty", "Create Empty"),
            string.Empty,
            selected: false,
            enabled: app.CurrentSession is not null))
        {
            app.CreateGameObject();
        }

        if (ImGui.MenuItem(
            L.Get("gameObject.createEmptyChild", "Create Empty Child"),
            string.Empty,
            selected: false,
            enabled: app.CurrentSession?.SceneModel.SelectedStableId is not null))
        {
            app.CreateChildGameObject();
        }

        if (ImGui.BeginMenu(
            L.Get("gameObject.createWithComponent", "Create with Component"),
            app.CurrentSession is not null))
        {
            string[] behaviours = app.GetBehaviourTypeNames();
            if (behaviours.Length == 0)
            {
                _ = ImGui.MenuItem(
                    L.Get("component.noBehaviour", "No Behaviour"),
                    string.Empty,
                    selected: false,
                    enabled: false);
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

    [EditorUiCommands("menu.assets.open-csharp-project")]
    private static void DrawAssetsMenu(EditorShellApp app)
    {
        if (!ImGui.BeginMenu(L.Get("menu.assets", "Assets")))
        {
            return;
        }

        if (ImGui.MenuItem(
            L.Get("action.openCSharpProject", "Open C# Project"),
            string.Empty,
            selected: false,
            enabled: app.HasOpenProject))
        {
            _ = app.OpenCSharpProject(out _);
        }

        ImGui.EndMenu();
    }

    [EditorUiCommands(
        "menu.window",
        "menu.window.project-picker",
        "menu.window.reset-layout")]
    private static void DrawWindowMenu(EditorShellApp app)
    {
        if (!ImGui.BeginMenu(L.Get("menu.window", "Window")))
        {
            return;
        }

        if (ImGui.MenuItem(L.Get("window.projectPicker", "Project Picker")))
        {
            app.FocusProjectPicker(ProjectPickerMode.OpenProject);
        }

        if (ImGui.BeginMenu(L.Get("window.group.general", "General")))
        {
            DrawPanelMenuItem(app, L.Get("window.hierarchy", "Hierarchy"), EditorDockSpace.SceneHierarchyWindowTitle);
            DrawPanelMenuItem(app, L.Get("window.sceneView", "Scene View"), EditorDockSpace.ViewportWindowTitle);
            DrawPanelMenuItem(app, L.Get("window.game", "Game View"), EditorDockSpace.GameViewWindowTitle);
            DrawPanelMenuItem(app, L.Get("window.inspector", "Inspector"), EditorDockSpace.InspectorWindowTitle);
            DrawPanelMenuItem(app, L.Get("window.project", "Project"), EditorDockSpace.AssetBrowserWindowTitle);
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu(L.Get("window.group.analysis", "Analysis")))
        {
            DrawPanelMenuItem(app, L.Get("window.console", "Console"), EditorDockSpace.ConsoleDiagnosticsWindowTitle);
            DrawPanelMenuItem(app, L.Get("window.profiler", "Profiler"), EditorDockSpace.PerformanceHudWindowTitle);
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu(L.Get("window.group.settings", "Settings")))
        {
            DrawPanelMenuItem(app, L.Get("window.uiManifest", "UI Manifest"), UiManifestPanel.PanelTitle);
            DrawPanelMenuItem(app, L.Get("window.projectSettings", "Project Settings..."), ProjectSettingsPanel.PanelTitle);
            DrawPanelMenuItem(app, L.Get("window.playerSettings", "Player Settings..."), PlayerSettingsPanel.PanelTitle);
            DrawPanelMenuItem(app, L.Get("window.buildSettings", "Build Settings..."), BuildSettingsPanel.PanelTitle);
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu(L.Get("window.group.tools", "Tools")))
        {
            DrawPanelMenuItem(app, L.Get("window.materials", "Materials"), EditorDockSpace.MaterialReactionEditorWindowTitle);
            DrawPanelMenuItem(app, L.Get("window.brush", "Brush"), EditorDockSpace.MaterialBrushWindowTitle);
            DrawPanelMenuItem(app, L.Get("window.worldInspector", "World Inspector"), EditorDockSpace.WorldInspectorWindowTitle);
            DrawPanelMenuItem(app, L.Get("window.overlays", "Overlays"), EditorDockSpace.DebugOverlayWindowTitle);
            DrawPanelMenuItem(app, L.Get("window.simulation", "Simulation"), EditorDockSpace.SimulationControlWindowTitle);
            DrawPanelMenuItem(app, L.Get("window.playMode", "Play Mode"), EditorDockSpace.EditorModeWindowTitle);
            DrawPanelMenuItem(app, L.Get("window.saveLoad", "Save / Load"), EditorDockSpace.SaveLoadWindowTitle);
            DrawPanelMenuItem(app, L.Get("window.physics", "Physics"), EditorDockSpace.PhysicsTuningWindowTitle);
            DrawPanelMenuItem(app, L.Get("window.particles", "Particles"), EditorDockSpace.ParticleTuningWindowTitle);
            DrawPanelMenuItem(app, L.Get("window.lighting", "Lighting"), EditorDockSpace.LightingTuningWindowTitle);
            ImGui.EndMenu();
        }
        ImGui.Separator();
        if (ImGui.MenuItem(L.Get("action.resetLayout", "Reset Layout")))
        {
            app.ResetLayout();
        }

        ImGui.EndMenu();
    }

    [EditorUiCommands("menu.window.panel.open")]
    private static void DrawPanelMenuItem(EditorShellApp app, string label, string panelTitle)
    {
        bool visible = false;
        bool enabled = app.HasOpenProject && app.TryGetPanelVisibility(panelTitle, out visible);
        if (ImGui.MenuItem(label, string.Empty, selected: visible, enabled: enabled))
        {
            // Unity-like Window 菜单是“打开并聚焦”入口；已停靠但被其他 tab 覆盖时，
            // 再次选择必须把目标 tab 拉到前台，关闭仍由面板自身的关闭按钮负责。
            _ = app.ShowPanel(panelTitle);
        }
    }

    [EditorUiCommands(
        "menu.play.play",
        "menu.play.stop",
        "menu.play.pause",
        "menu.play.resume",
        "menu.play.step")]
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

    [EditorUiCommands("menu.help.about", "menu.help.shortcuts")]
    private static void DrawHelpMenu(EditorShellApp app)
    {
        if (!ImGui.BeginMenu(L.Get("menu.help", "Help")))
        {
            return;
        }

        ImGui.TextUnformatted(PixelEngineProduct.Name);
        ImGui.TextUnformatted(app.HasOpenProject
            ? app.CurrentProject!.Name
            : L.Get("status.noProject", "No Project"));
        ImGui.Separator();
        if (ImGui.MenuItem(L.Get("help.about", "About")))
        {
            ImGui.OpenPopup(PixelEngineProduct.AboutPopupTitle);
        }

        if (ImGui.MenuItem(L.Get("help.shortcuts", "Shortcuts")))
        {
            app.ShowPreferences(EditorPreferencesCategory.Shortcuts);
        }

        if (ImGui.BeginPopup(PixelEngineProduct.AboutPopupTitle))
        {
            ImGui.TextUnformatted(PixelEngineProduct.Name);
            ImGui.TextUnformatted(L.Get("help.description", "Standalone editor shell"));
            ImGui.EndPopup();
        }

        ImGui.EndMenu();
    }

    [EditorUiCommands(
        "shortcut.ctrl-s",
        "shortcut.ctrl-shift-s",
        "shortcut.ctrl-z",
        "shortcut.ctrl-y",
        "shortcut.ctrl-d",
        "shortcut.ctrl-p",
        "shortcut.ctrl-shift-b",
        "shortcut.ctrl-b",
        "shortcut.ctrl-comma",
        "shortcut.delete",
        "shortcut.f2")]
    internal static void DispatchShortcuts(EditorShellApp app)
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

        if (EditorShortcutCatalog.IsPressed(EditorShortcutCommand.Delete) &&
            app.CurrentSession.SceneModel.SelectedStableId is not null)
        {
            app.DeleteSelectedGameObject();
        }

        if (EditorShortcutCatalog.IsPressed(EditorShortcutCommand.Rename) &&
            app.CurrentSession.SceneModel.SelectedStableId is not null)
        {
            app.RenameSelectedGameObject();
        }

        if (EditorShortcutCatalog.IsPressed(EditorShortcutCommand.TogglePlayMode))
        {
            EditorMainToolbarState state = CaptureToolbarState(app);
            TogglePlayMode(app, state);
        }

        if (EditorShortcutCatalog.IsPressed(EditorShortcutCommand.OpenBuildSettings))
        {
            app.ShowBuildSettings();
        }
        else if (EditorShortcutCatalog.IsPressed(EditorShortcutCommand.BuildAndRun))
        {
            _ = app.TryStartBuild(runAfterBuild: true, out _);
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
            ? L.Format(
                "status.projectSummary",
                "{0}  |  {1}{2}  |  {3} GameObjects",
                ProjectName,
                SceneName,
                IsDirty ? "*" : string.Empty,
                ObjectCount)
            : L.Get("status.openOrCreateProject", "Open or create a project");
}
