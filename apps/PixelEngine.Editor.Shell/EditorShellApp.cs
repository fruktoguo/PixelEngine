using System.Diagnostics;
using System.Text.Json;
using Hexa.NET.ImGui;
using PixelEngine.Editor.Shell.Build;
using PixelEngine.Editor.Shell.Settings;
using PixelEngine.Hosting;
using PixelEngine.Rendering;
using PixelEngine.Scripting;
using Silk.NET.OpenGL;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Editor Shell 主应用：项目会话、Engine 生命周期与 ImGui 工作台编排。
/// </summary>
internal sealed class EditorShellApp
{
    private const string DefaultWorkbenchBehaviourTypeName = "DefaultWorkbenchBehaviour";
    private static readonly TimeSpan ScriptedBuildProbeTimeout = TimeSpan.FromMinutes(10);
    private readonly EditorShellOptions _options;
    private readonly EditorUserDataPaths _userDataPaths;
    private readonly EditorTransitionCoordinator _transitions;
    private readonly EditorDeferredFrameActions _deferredFrameActions = new();
    private EditorProject? _pendingProject;
    private bool _pendingSceneOverrideFromWorkspace;
    private string? _commandLineSceneOverride;
    private bool _closeProjectRequested;
    private bool _exitRequested;
    private bool _allowDirtyShutdown;

    private EditorShellApp(
        EditorShellOptions options,
        EditorUserDataPaths userDataPaths,
        EditorPreferencesStore preferences,
        RecentProjectsStore recentProjects,
        EditorWorkspaceStore workspace)
    {
        _options = options;
        _userDataPaths = userDataPaths ?? throw new ArgumentNullException(nameof(userDataPaths));
        LayoutPath = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PIXELENGINE_EDITOR_LAYOUT_PATH"))
            ? userDataPaths.LayoutPath
            : EditorShellWindow.DefaultLayoutPath;
        Preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        EditorLocalization.Configure(
            [Path.Combine(AppContext.BaseDirectory, "Localization"), userDataPaths.LocalizationDirectory],
            Preferences.Current.Language);
        RecentProjects = recentProjects ?? throw new ArgumentNullException(nameof(recentProjects));
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _commandLineSceneOverride = options.ScenePath;
        ProjectPicker = new ProjectPickerWindow(options);
        EditorWorkspaceWindowState windowState = Workspace.Current.Window ?? new EditorWorkspaceWindowState();
        Layout = new EditorShellLayout(
            LayoutPath,
            windowState.Width,
            windowState.Height,
            migrateToCurrentLayout: true);
        PreferencesWindow = new EditorPreferencesWindow(Preferences, ResetLayout, SetLanguage);
        _transitions = new EditorTransitionCoordinator(
            IsCurrentSceneDirtyAfterFlushing,
            TrySaveSceneForTransition);
    }

    internal static EditorShellApp CreateForTests(EditorPreferencesStore? preferences = null)
    {
        EditorShellOptions options = new(
            ProjectPath: null,
            ScenePath: null,
            WindowTicks: 0,
            ScriptedProbe: false,
            ScriptedBuildProbe: false,
            ScriptedBuildRunProbe: false,
            ScriptedBuildCancelProbe: false,
            ScriptedBuildSettingsProbe: false,
            ScriptedMenuLayoutProbe: false,
            ScriptedHierarchyProbe: false,
            ScriptedDefaultWorkbenchProbe: false,
            ScriptedPreferencesProbe: false,
            BuildOutputPath: null,
            CaptureFramePath: null,
            LogDirectory: null)
        {
            EphemeralUserState = true,
        };
        EditorUserDataPaths userDataPaths = EditorUserDataPaths.Resolve(
            options,
            environmentUserDataDirectory: null,
            ephemeralDirectory: Path.Combine(Path.GetTempPath(), "PixelEngine", "EditorTests", "in-memory"));
        return new EditorShellApp(
            options,
            userDataPaths,
            preferences ?? EditorPreferencesStore.CreateInMemory(),
            RecentProjectsStore.CreateInMemory(),
            EditorWorkspaceStore.CreateInMemory());
    }

    public EditorConsoleStore ConsoleStore { get; } = new();

    public EditorPreferencesStore Preferences { get; }

    public EditorPreferencesWindow PreferencesWindow { get; }

    public float UiScale => Preferences.Current.UiScale;

    private void SetLanguage(string locale)
    {
        _ = EditorLocalization.TrySetLocale(locale);
    }

    public EditorProject? CurrentProject { get; private set; }

    public bool HasOpenProject => CurrentProject is not null;

    public string? SceneOverridePath { get; private set; }

    public RecentProjectsStore RecentProjects { get; }

    internal EditorWorkspaceStore Workspace { get; }

    internal string LayoutPath { get; }

    public string? LastProjectError { get; private set; }

    public string? LastAssetOpenDiagnostic { get; private set; }

    internal EditorTransitionPrompt? PendingTransition => _transitions.Pending;

    public EditorProjectSession? CurrentSession { get; private set; }

    private ProjectPickerWindow ProjectPicker { get; }

    private EditorShellLayout Layout { get; }

    public static int Execute(string[] args)
    {
        EditorShellOptions? options = null;
        try
        {
            options = EditorShellOptions.Parse(args);
            EditorUserDataPaths userDataPaths = EditorUserDataPaths.Resolve(options);
            string? preferencesOverride = Environment.GetEnvironmentVariable("PIXELENGINE_EDITOR_PREFERENCES_PATH");
            return new EditorShellApp(
                options,
                userDataPaths,
                EditorPreferencesStore.Load(string.IsNullOrWhiteSpace(preferencesOverride)
                    ? userDataPaths.PreferencesPath
                    : preferencesOverride),
                RecentProjectsStore.Load(userDataPaths.RecentProjectsPath),
                EditorWorkspaceStore.Load(userDataPaths.WorkspacePath)).Run();
        }
        catch (Exception exception)
        {
            string path = WriteCrashLog(exception, options?.LogDirectory);
            Console.Error.WriteLine($"Editor Shell 启动失败，异常已写入：{path}");
            return 1;
        }
    }

    private int Run()
    {
        bool previousShutdownWasClean = Workspace.Current.LastCleanShutdown;
        if (!Workspace.TrySetShutdownState(cleanShutdown: false, out string startupStateDiagnostic))
        {
            ConsoleStore.AddProjectError("workspace", startupStateDiagnostic);
        }

        EditorWorkspaceWindowState windowState = Workspace.Current.Window ?? new EditorWorkspaceWindowState();
        using EditorShellWindow shellWindow = EditorShellWindow.Create(
            Preferences.Current.UiScale,
            LayoutPath,
            windowState.Width,
            windowState.Height);
        void HandleNativeWindowClosing()
        {
            bool isDirty = IsCurrentSceneDirtyAfterFlushing();
            if (EditorNativeCloseGuard.ShouldExit(
                true,
                isDirty,
                () => _ = shellWindow.Window.TryCancelCloseRequest(),
                RequestExit))
            {
                _exitRequested = true;
            }
        }

        shellWindow.Window.Closing += HandleNativeWindowClosing;
        if (_options.ScriptedPreferencesProbe)
        {
            ShowPreferences(EditorPreferencesCategory.Appearance);
        }

        string? startupProjectPath = ResolveStartupProjectPath(
            _options,
            Preferences.Current,
            Workspace.Current,
            previousShutdownWasClean);

        if (!string.IsNullOrWhiteSpace(startupProjectPath))
        {
            OpenProjectPath(startupProjectPath);
            ApplyPendingProject(shellWindow);
        }
        else if (!previousShutdownWasClean && string.IsNullOrWhiteSpace(_options.ProjectPath))
        {
            LastProjectError = "上一次 Editor 未正常关闭，已停止自动打开工程；请从 Recent 手动恢复。";
            ConsoleStore.AddProjectError("workspace", LastProjectError);
        }

        UpdateTitle(shellWindow);

        Stopwatch stopwatch = Stopwatch.StartNew();
        double previousSeconds = stopwatch.Elapsed.TotalSeconds;
        int executed = 0;
        int requestedTicks = _options.WindowTicks;
        bool configuredImGui = false;
        bool scriptedPlayEntered = false;
        bool scriptedPlayPaused = false;
        bool scriptedPlayStepped = false;
        bool scriptedPlayResumed = false;
        bool scriptedPlayExited = false;
        bool scriptedSceneSaved = false;
        bool scriptedProjectClosed = false;
        bool scriptedProjectReopened = false;
        bool scriptedBuildStarted = false;
        bool scriptedBuildCompleted = false;
        bool scriptedBuildTimedOut = false;
        string scriptedBuildDiagnostic = string.Empty;
        ScriptedBuildProbeSnapshot scriptedBuildSnapshot = new();
        ScriptedBuildFrameStats scriptedBuildFrameStats = new();
        ScriptedBuildCancelProbeState scriptedBuildCancel = new();
        ScriptedBuildSettingsProbeState scriptedBuildSettings = new();
        ScriptedMenuLayoutProbeState scriptedMenuLayout = new();
        ScriptedHierarchyProbeState scriptedHierarchy = new();
        ScriptedDefaultWorkbenchProbeState scriptedDefaultWorkbench = new();
        ScriptedGameViewProbeState scriptedGameView = new();
        ScriptedPlayerRunProbeResult scriptedPlayerRun = new();
        // 主循环：无项目时显示 ProjectPicker；有项目时由 Session 驱动 Engine tick
        while (!_exitRequested)
        {
            bool nativeCloseRequested = shellWindow.Window.IsClosing;
            bool isDirty = nativeCloseRequested && IsCurrentSceneDirtyAfterFlushing();
            if (EditorNativeCloseGuard.ShouldExit(
                nativeCloseRequested,
                isDirty,
                () => _ = shellWindow.Window.TryCancelCloseRequest(),
                RequestExit))
            {
                break;
            }

            double now = stopwatch.Elapsed.TotalSeconds;
            float deltaSeconds = (float)Math.Max(0.0, now - previousSeconds);
            previousSeconds = now;
            if (CurrentSession is null)
            {
                shellWindow.EnsureProjectPickerGui();
                if (_options.ScriptedDefaultWorkbenchProbe &&
                    !scriptedDefaultWorkbench.ProjectCreated &&
                    string.IsNullOrWhiteSpace(_options.ProjectPath))
                {
                    string projectRoot = ResolveScriptedDefaultWorkbenchProjectRoot();
                    CreateProject(projectRoot, "Default Workbench Probe");
                    scriptedDefaultWorkbench.ProjectCreated = true;
                    scriptedDefaultWorkbench.ProjectRoot = projectRoot;
                }

                if (_options.ScriptedProbe &&
                    scriptedProjectClosed &&
                    !scriptedProjectReopened &&
                    !string.IsNullOrWhiteSpace(_options.ProjectPath))
                {
                    OpenProjectPath(_options.ProjectPath);
                    scriptedProjectReopened = true;
                }

                if (_options.ScriptedBuildSettingsProbe &&
                    scriptedBuildSettings.CloseRequested &&
                    !scriptedBuildSettings.Reopened &&
                    !string.IsNullOrWhiteSpace(_options.ProjectPath))
                {
                    OpenProjectPath(_options.ProjectPath);
                    scriptedBuildSettings.Reopened = true;
                }

                shellWindow.Window.DoEvents();
                if (!shellWindow.Gui.IsRunning)
                {
                    shellWindow.Gui.Initialize();
                }

                if (!configuredImGui)
                {
                    Layout.ConfigureImGui();
                    configuredImGui = true;
                }

                shellWindow.Gui.SetUiScale(Preferences.Current.UiScale);
                shellWindow.Gui.SetLayoutPersistence(Preferences.Current.SaveLayoutOnExit);
                shellWindow.Gui.DrawFrame(
                    deltaSeconds,
                    shellWindow.Window.LogicalWidth,
                    shellWindow.Window.LogicalHeight,
                    _ =>
                    {
                        EditorMainMenuBar.DispatchShortcuts(this);
                        ProjectPicker.Draw(this);
                        PreferencesWindow.Draw();
                    },
                    shellWindow.Window.FramebufferScaleX,
                    shellWindow.Window.FramebufferScaleY);
                shellWindow.Window.SwapBuffers();
                ApplyPendingProject(shellWindow);
            }
            else
            {
                CurrentSession.RunOneTick(deltaSeconds);
                ApplyDeferredFrameActions();
                if (_options.ScriptedGameViewProbe)
                {
                    RunScriptedGameViewProbeActions(executed, scriptedGameView);
                }

                if (_options.ScriptedProbe)
                {
                    RunScriptedProbeActions(
                        executed,
                        ref scriptedPlayEntered,
                        ref scriptedPlayPaused,
                        ref scriptedPlayStepped,
                        ref scriptedPlayResumed,
                        ref scriptedPlayExited,
                        ref scriptedSceneSaved,
                        ref scriptedProjectClosed);
                }

                ApplyDeferredClose();
                ApplyPendingProject(shellWindow);
                if (_options.ScriptedBuildCancelProbe)
                {
                    RunScriptedBuildCancelProbeActions(scriptedBuildCancel);
                    scriptedBuildSnapshot = scriptedBuildCancel.RerunSnapshot.Result is not null
                        ? scriptedBuildCancel.RerunSnapshot
                        : scriptedBuildCancel.FirstSnapshot;
                    scriptedBuildCompleted = scriptedBuildCancel.Completed;
                    scriptedBuildDiagnostic = scriptedBuildCancel.Diagnostic;
                }
                else if (_options.ScriptedBuildSettingsProbe)
                {
                    RunScriptedBuildSettingsProbeActions(scriptedBuildSettings);
                }
                else if (_options.ScriptedBuildProbe)
                {
                    RunScriptedBuildProbeActions(
                        ref scriptedBuildStarted,
                        ref scriptedBuildCompleted,
                        ref scriptedBuildDiagnostic,
                        ref scriptedBuildSnapshot);
                }
                else if (_options.ScriptedMenuLayoutProbe)
                {
                    RunScriptedMenuLayoutProbeActions(scriptedMenuLayout);
                }
                else if (_options.ScriptedHierarchyProbe)
                {
                    RunScriptedHierarchyProbeActions(scriptedHierarchy);
                }
                else if (_options.ScriptedDefaultWorkbenchProbe)
                {
                    RunScriptedDefaultWorkbenchProbeActions(scriptedDefaultWorkbench);
                }
            }

            UpdateTitle(shellWindow);
            if (_options.ScriptedBuildProbe && scriptedBuildStarted && !scriptedBuildCompleted)
            {
                scriptedBuildFrameStats.Record(deltaSeconds);
            }

            executed++;
            if (_options.ScriptedBuildCancelProbe && scriptedBuildCancel.Completed)
            {
                break;
            }

            if (_options.ScriptedBuildSettingsProbe && scriptedBuildSettings.Completed)
            {
                break;
            }

            if (_options.ScriptedMenuLayoutProbe && scriptedMenuLayout.Completed)
            {
                break;
            }

            if (_options.ScriptedHierarchyProbe && scriptedHierarchy.Completed)
            {
                break;
            }

            if (_options.ScriptedDefaultWorkbenchProbe && scriptedDefaultWorkbench.Completed)
            {
                break;
            }

            if (_options.ScriptedGameViewProbe && scriptedGameView.Finished)
            {
                break;
            }

            if (!_options.ScriptedBuildCancelProbe && _options.ScriptedBuildProbe && scriptedBuildCompleted)
            {
                break;
            }

            if (_options.ScriptedBuildProbe &&
                scriptedBuildStarted &&
                !scriptedBuildCompleted &&
                stopwatch.Elapsed >= ScriptedBuildProbeTimeout)
            {
                scriptedBuildTimedOut = true;
                CurrentSession?.CancelScriptedBuildProbe();
                scriptedBuildSnapshot = CurrentSession?.CaptureScriptedBuildProbe() ?? scriptedBuildSnapshot;
                break;
            }

            if (!_options.ScriptedBuildProbe && requestedTicks > 0 && executed >= requestedTicks)
            {
                break;
            }
        }

        shellWindow.Window.Closing -= HandleNativeWindowClosing;

        CaptureFrameIfRequested(shellWindow);
        if (requestedTicks > 0 || _options.ScriptedProbe)
        {
            bool projectOpen = HasOpenProject;
            Console.WriteLine(
                $"frame_samples={executed}, " +
                "editor_enabled=True, " +
                $"editor_running={projectOpen}, " +
                $"editor_panels={CurrentSession?.PanelCount ?? 0}, " +
                $"editor_bridge_frames={CurrentSession?.EditorBridgeFrameCount ?? executed}, " +
                $"render_camera_synced={projectOpen}, " +
                $"scripted_play_entered={scriptedPlayEntered}, " +
                $"scripted_play_paused={scriptedPlayPaused}, " +
                $"scripted_play_stepped={scriptedPlayStepped}, " +
                $"scripted_play_resumed={scriptedPlayResumed}, " +
                $"scripted_play_exited={scriptedPlayExited}, " +
                $"scripted_scene_saved={scriptedSceneSaved}, " +
                $"scripted_project_closed={scriptedProjectClosed}, " +
                $"scripted_project_reopened={scriptedProjectReopened}, " +
                $"project_open={projectOpen}, " +
                $"window_ticks={executed}");
        }

        if (_options.ScriptedPreferencesProbe)
        {
            Console.WriteLine(
                $"editor_preferences_probe=True, preferences_visible={PreferencesWindow.Visible}, " +
                $"ui_scale_percent={EditorUiScale.ToPercent(Preferences.Current.UiScale)}, " +
                $"save_layout_on_exit={Preferences.Current.SaveLayoutOnExit}, " +
                $"reopen_last_project={Preferences.Current.ReopenLastProject}, " +
                $"restore_last_scene={Preferences.Current.RestoreLastScene}, " +
                $"ephemeral_user_state={_userDataPaths.IsEphemeral}, " +
                $"user_data_root={_userDataPaths.RootDirectory}, " +
                $"preferences_path={Preferences.StoragePath}, " +
                $"window_pos={PreferencesWindow.LastWindowPosition.X:F0},{PreferencesWindow.LastWindowPosition.Y:F0}, " +
                $"window_size={PreferencesWindow.LastWindowSize.X:F0}x{PreferencesWindow.LastWindowSize.Y:F0}, " +
                $"navigation_visible={PreferencesWindow.LastNavigationVisible}");
        }

        if (_options.ScriptedGameViewProbe)
        {
            WriteScriptedGameViewProbeSummary(scriptedGameView);
        }

        if (_options.ScriptedBuildSettingsProbe)
        {
            WriteScriptedBuildSettingsProbeSummary(scriptedBuildSettings);
        }
        else if (_options.ScriptedMenuLayoutProbe)
        {
            WriteScriptedMenuLayoutProbeSummary(scriptedMenuLayout);
        }
        else if (_options.ScriptedHierarchyProbe)
        {
            WriteScriptedHierarchyProbeSummary(scriptedHierarchy);
        }
        else if (_options.ScriptedDefaultWorkbenchProbe)
        {
            WriteScriptedDefaultWorkbenchProbeSummary(scriptedDefaultWorkbench);
        }
        else if (_options.ScriptedBuildCancelProbe)
        {
            WriteScriptedBuildCancelProbeSummary(scriptedBuildCancel);
        }
        else if (_options.ScriptedBuildProbe)
        {
            if (_options.ScriptedBuildRunProbe)
            {
                scriptedPlayerRun = RunScriptedPlayerProbe(scriptedBuildSnapshot.Result);
            }

            WriteScriptedBuildProbeSummary(
                scriptedBuildStarted,
                scriptedBuildCompleted,
                scriptedBuildTimedOut,
                scriptedBuildDiagnostic,
                scriptedBuildSnapshot,
                scriptedBuildFrameStats);
            if (_options.ScriptedBuildRunProbe)
            {
                WriteScriptedPlayerRunProbeSummary(scriptedPlayerRun);
            }
        }

        if (!Workspace.TrySetWindowSize(
            shellWindow.Window.LogicalWidth,
            shellWindow.Window.LogicalHeight,
            out string windowStateDiagnostic))
        {
            ConsoleStore.AddProjectError("workspace", windowStateDiagnostic);
        }

        bool cleanShutdown = CurrentSession?.SceneModel.IsDirty != true || _allowDirtyShutdown;
        CurrentSession?.Dispose();
        CurrentSession = null;
        if (cleanShutdown && !Workspace.TrySetShutdownState(cleanShutdown: true, out string shutdownStateDiagnostic))
        {
            ConsoleStore.AddProjectError("workspace", shutdownStateDiagnostic);
        }

        return 0;
    }

    internal static string? ResolveStartupProjectPath(
        EditorShellOptions options,
        EditorPreferencesDocument preferences,
        EditorWorkspaceDocument workspace,
        bool previousShutdownWasClean)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(workspace);
        return !string.IsNullOrWhiteSpace(options.ProjectPath)
            ? options.ProjectPath
            : options.ReopenLastProject && preferences.ReopenLastProject && previousShutdownWasClean
                ? workspace.LastSuccessfulProjectPath
                : null;
    }

    private void RunScriptedMenuLayoutProbeActions(ScriptedMenuLayoutProbeState state)
    {
        if (CurrentSession is null || state.Completed)
        {
            return;
        }

        try
        {
            EditorSceneModel scene = CurrentSession.SceneModel;
            state.StartScene = CurrentSession.CurrentSceneRelativePath;
            state.InitialCount = scene.Count;
            string[] panelTitles =
            [
                EditorDockSpace.SceneHierarchyWindowTitle,
                EditorDockSpace.ViewportWindowTitle,
                EditorDockSpace.GameViewWindowTitle,
                EditorDockSpace.InspectorWindowTitle,
                EditorDockSpace.AssetBrowserWindowTitle,
                EditorDockSpace.ConsoleDiagnosticsWindowTitle,
                EditorDockSpace.PerformanceHudWindowTitle,
                ProjectSettingsPanel.PanelTitle,
                PlayerSettingsPanel.PanelTitle,
                BuildSettingsPanel.PanelTitle,
            ];
            int shown = 0;
            for (int i = 0; i < panelTitles.Length; i++)
            {
                if (ShowPanel(panelTitles[i]))
                {
                    shown++;
                }
            }

            ResetLayout();
            CreateGameObject();
            int? createdId = scene.SelectedStableId;
            state.CreatedObject = createdId.HasValue &&
                scene.TryGet(createdId.Value, out _) &&
                scene.Count == state.InitialCount + 1;
            int countAfterCreate = scene.Count;
            DuplicateSelectedGameObject();
            int? duplicateId = scene.SelectedStableId;
            state.DuplicatedObject = createdId.HasValue &&
                duplicateId.HasValue &&
                duplicateId.Value != createdId.Value &&
                scene.TryGet(duplicateId.Value, out _) &&
                scene.Count == countAfterCreate + 1;
            RenameSelectedGameObject();
            state.RenamedObject = scene.SelectedStableId is { } renamedId &&
                scene.Get(renamedId).Name.EndsWith(" Renamed", StringComparison.Ordinal);
            int countBeforeDelete = scene.Count;
            int? deletedId = scene.SelectedStableId;
            DeleteSelectedGameObject();
            state.DeletedObject = deletedId.HasValue &&
                !scene.TryGet(deletedId.Value, out _) &&
                scene.Count == countBeforeDelete - 1;
            state.FinalCount = scene.Count;
            string newScene = CurrentSession.NewSceneAuto();
            state.NewSceneCreated = File.Exists(CurrentSession.Project.ResolveSceneFullPath(newScene));
            CurrentSession.OpenScene(state.StartScene);
            state.OpenedOriginalScene = string.Equals(CurrentSession.CurrentSceneRelativePath, state.StartScene, StringComparison.OrdinalIgnoreCase);
            state.RequiredPanelsShown = shown == panelTitles.Length;
            state.PanelCount = CurrentSession.PanelCount;
            state.ResetRequested = true;
            state.Completed = true;
            state.Diagnostic = state.Succeeded
                ? "菜单与布局探针完成。"
                : $"探针未满足验收：panels={state.RequiredPanelsShown}, reset={state.ResetRequested}, create={state.CreatedObject}, duplicate={state.DuplicatedObject}, rename={state.RenamedObject}, delete={state.DeletedObject}, new_scene={state.NewSceneCreated}, reopen={state.OpenedOriginalScene}。";
        }
        catch (Exception ex) when (!OperatingSystem.IsBrowser())
        {
            state.Completed = true;
            state.Diagnostic = ex.Message;
        }
    }

    private void RunScriptedHierarchyProbeActions(ScriptedHierarchyProbeState state)
    {
        if (CurrentSession is null || state.Completed)
        {
            return;
        }

        try
        {
            EditorSceneModel scene = CurrentSession.SceneModel;
            EditorUndoStack undo = CurrentSession.UndoStack;
            state.InitialCount = scene.Count;
            undo.Execute(scene, new CreateGameObjectCommand("Hierarchy Parent"));
            int parent = scene.SelectedStableId ?? throw new InvalidOperationException("创建父节点后没有选择对象。");
            undo.Execute(scene, new CreateGameObjectCommand("Hierarchy Child", parent));
            int child = scene.SelectedStableId ?? throw new InvalidOperationException("创建子节点后没有选择对象。");
            state.Created = scene.TryGet(parent, out _) && scene.TryGet(child, out _);
            state.ChildParented = scene.Get(child).ParentId == parent;

            try
            {
                undo.Execute(scene, new ReparentGameObjectCommand(parent, child));
            }
            catch (InvalidOperationException)
            {
                state.CycleRejected = true;
            }

            state.CyclePrevented = scene.Get(parent).ParentId is null && scene.Get(child).ParentId == parent;
            undo.Execute(scene, new DuplicateGameObjectCommand(child));
            int duplicate = scene.SelectedStableId ?? throw new InvalidOperationException("复制后没有选择对象。");
            state.Duplicated = duplicate != child && scene.TryGet(duplicate, out _);
            undo.Execute(scene, new RenameGameObjectCommand(duplicate, "Hierarchy Duplicate Renamed"));
            state.Renamed = scene.Get(duplicate).Name == "Hierarchy Duplicate Renamed";
            undo.Execute(scene, new ReparentGameObjectCommand(duplicate, null));
            state.ReparentedToRoot = scene.Get(duplicate).ParentId is null;
            scene.Select(duplicate);
            state.SelectionLinked = scene.SelectedStableId == duplicate;
            undo.Execute(scene, new DeleteGameObjectCommand(duplicate));
            state.Deleted = !scene.TryGet(duplicate, out _);
            state.FinalCount = scene.Count;
            state.Completed = true;
            state.Diagnostic = "层级面板命令探针完成。";
        }
        catch (Exception ex) when (!OperatingSystem.IsBrowser())
        {
            state.Completed = true;
            state.Diagnostic = ex.Message;
        }
    }

    private void RunScriptedDefaultWorkbenchProbeActions(ScriptedDefaultWorkbenchProbeState state)
    {
        if (CurrentSession is null || state.Completed)
        {
            return;
        }

        try
        {
            if (!state.RequiredPanelsShown)
            {
                string[] panelTitles =
                [
                    EditorDockSpace.SceneHierarchyWindowTitle,
                    EditorDockSpace.ViewportWindowTitle,
                    EditorDockSpace.GameViewWindowTitle,
                    EditorDockSpace.InspectorWindowTitle,
                    EditorDockSpace.AssetBrowserWindowTitle,
                    EditorDockSpace.ConsoleDiagnosticsWindowTitle,
                    ProjectSettingsPanel.PanelTitle,
                    PlayerSettingsPanel.PanelTitle,
                    BuildSettingsPanel.PanelTitle,
                ];
                int shown = 0;
                for (int i = 0; i < panelTitles.Length; i++)
                {
                    if (ShowPanel(panelTitles[i]))
                    {
                        shown++;
                    }
                }

                ResetLayout();
                state.RequiredPanelsShown = shown == panelTitles.Length;
                state.PanelCount = CurrentSession.PanelCount;
                return;
            }

            if (!state.GameObjectCreated)
            {
                CreateGameObject();
                state.GameObjectCreated = CurrentSession.SceneModel.SelectedStableId.HasValue;
                return;
            }

            if (!state.ScriptSourceCreated)
            {
                state.ScriptSourcePath = CreateDefaultWorkbenchScriptSource(CurrentSession.Project);
                state.ScriptSourceCreated = File.Exists(state.ScriptSourcePath);
                return;
            }

            if (!state.ScriptHotReloadRequested)
            {
                Scripting.ScriptHotReloadController controller = CurrentSession.Engine.Context.GetService<Scripting.ScriptHotReloadController>();
                controller.RequestReloadFromDirectory($"{CurrentSession.Project.Name}.EditorScripts", CurrentSession.Project.ScriptSourcePath);
                state.ScriptHotReloadRequested = controller.HasPendingReload;
                if (state.ScriptHotReloadRequested)
                {
                    Scripting.ScriptHotReloadApplyResult result = CurrentSession.Engine.ApplyPendingScriptHotReload();
                    state.ScriptHotReloadApplied = result.Status == Scripting.ScriptHotReloadStatus.Reloaded;
                }

                return;
            }

            if (!state.BehaviourRegistered)
            {
                string? behaviourTypeName = CurrentSession.GetBehaviourTypeNames()
                    .FirstOrDefault(static name => IsDefaultWorkbenchBehaviourTypeName(name));
                state.ScriptHotReloadApplied = state.ScriptHotReloadApplied || behaviourTypeName is not null;
                state.BehaviourRegistered = behaviourTypeName is not null;
                state.BehaviourTypeName = behaviourTypeName ?? string.Empty;
                return;
            }

            if (!state.BehaviourAttached)
            {
                CurrentSession.AddComponentToSelected(state.BehaviourTypeName);
                state.BehaviourAttached = CurrentSession.SceneModel.SelectedStableId is { } selectedStableId &&
                    CurrentSession.SceneModel.Get(selectedStableId).Components.Any(component =>
                        string.Equals(component.TypeName, state.BehaviourTypeName, StringComparison.Ordinal));
                return;
            }

            if (!state.SceneSaved)
            {
                _ = SaveScene();
                state.SceneSaved = File.Exists(CurrentSession.SceneFilePath) && !CurrentSession.SceneModel.IsDirty;
                return;
            }

            if (!state.PlayEntered)
            {
                Hosting.EditorPlaySessionResult result = CurrentSession.EnterPlayTemporary();
                state.PlayEntered = result.Succeeded;
                state.PlayStatus = result.Message;
                return;
            }

            if (!state.PlayExited)
            {
                Hosting.EditorPlaySessionResult result = CurrentSession.ExitEditorPlay();
                state.PlayExited = result.Succeeded;
                state.PlayStatus = result.Message;
                return;
            }

            if (!state.BuildSettingsShown)
            {
                state.BuildSettingsShown = ShowPanel(BuildSettingsPanel.PanelTitle);
                state.BuildOutputPath = ResolveScriptedBuildOutputDirectory();
                _ = Directory.CreateDirectory(state.BuildOutputPath);
                state.BuildOutputReady = Directory.Exists(state.BuildOutputPath);
                return;
            }

            if (!state.BuildStarted)
            {
                state.BuildStarted = CurrentSession.TryStartScriptedBuildProbe(
                    state.BuildOutputPath,
                    runAfterBuild: false,
                    out state.Diagnostic);
                state.BuildSnapshot = CurrentSession.CaptureScriptedBuildProbe();
                if (!state.BuildStarted)
                {
                    state.Completed = true;
                }

                return;
            }

            if (!state.BuildCompleted)
            {
                state.BuildSnapshot = CurrentSession.CaptureScriptedBuildProbe();
                BuildResult? result = state.BuildSnapshot.Result;
                if (result is null)
                {
                    return;
                }

                state.BuildCompleted = true;
                state.BuildOk = result.Ok;
                state.BuildExitCode = result.ExitCode;
                state.BuildPackageArchive = result.PackageArchive ?? string.Empty;
                state.BuildError = result.Error ?? string.Empty;
            }

            state.Succeeded = state.RequiredPanelsShown &&
                state.GameObjectCreated &&
                state.ScriptSourceCreated &&
                state.ScriptHotReloadRequested &&
                state.ScriptHotReloadApplied &&
                state.BehaviourRegistered &&
                state.BehaviourAttached &&
                state.SceneSaved &&
                state.PlayEntered &&
                state.PlayExited &&
                state.BuildSettingsShown &&
                state.BuildOutputReady &&
                state.BuildStarted &&
                state.BuildCompleted &&
                state.BuildOk;
            state.Completed = true;
            state.Diagnostic = state.Succeeded
                ? "默认工作台脚本热重载、Behaviour 注册、挂载与玩家包构建探针完成。"
                : "默认工作台自动化路线探针未满足全部条件。";
        }
        catch (Exception ex) when (!OperatingSystem.IsBrowser())
        {
            state.Completed = true;
            state.Diagnostic = ex.Message;
        }
    }

    private string ResolveScriptedDefaultWorkbenchProjectRoot()
    {
        string root = string.IsNullOrWhiteSpace(_options.BuildOutputPath)
            ? string.IsNullOrWhiteSpace(_options.LogDirectory)
                ? Path.Combine(Environment.CurrentDirectory, "artifacts", "editor-default-workbench-probe")
                : Path.Combine(_options.LogDirectory, "editor-default-workbench-probe")
            : Path.Combine(_options.BuildOutputPath, "editor-default-workbench-probe");
        return Path.GetFullPath(Path.Combine(root, "project-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static string CreateDefaultWorkbenchScriptSource(EditorProject project)
    {
        _ = Directory.CreateDirectory(project.ScriptSourcePath);
        string path = Path.Combine(project.ScriptSourcePath, "DefaultWorkbenchBehaviour.cs");
        if (!File.Exists(path))
        {
            File.WriteAllText(
                path,
                "using PixelEngine.Scripting;" + Environment.NewLine +
                Environment.NewLine +
                "public sealed class DefaultWorkbenchBehaviour : Behaviour" + Environment.NewLine +
                "{" + Environment.NewLine +
                "}" + Environment.NewLine);
        }

        return path;
    }

    private static bool IsDefaultWorkbenchBehaviourTypeName(string typeName)
    {
        return string.Equals(typeName, DefaultWorkbenchBehaviourTypeName, StringComparison.Ordinal) ||
            typeName.EndsWith("." + DefaultWorkbenchBehaviourTypeName, StringComparison.Ordinal);
    }

    private void RunScriptedBuildSettingsProbeActions(ScriptedBuildSettingsProbeState state)
    {
        if (CurrentSession is null || state.Completed)
        {
            return;
        }

        if (!state.Applied)
        {
            try
            {
                state.Before = CurrentSession.ApplyScriptedBuildSettingsProbe(ResolveScriptedBuildOutputDirectory());
                state.Applied = true;
                CloseProject();
                state.CloseRequested = true;
                state.Diagnostic = "构建设置探针已保存并请求关闭工程。";
            }
            catch (Exception ex) when (!OperatingSystem.IsBrowser())
            {
                state.Diagnostic = ex.Message;
                state.Completed = true;
            }

            return;
        }

        if (state.Reopened && !state.Captured)
        {
            try
            {
                state.After = CurrentSession.CaptureScriptedBuildSettingsProbe();
                state.Captured = true;
                state.Completed = true;
                state.Diagnostic = "构建设置探针重启恢复完成。";
            }
            catch (Exception ex) when (!OperatingSystem.IsBrowser())
            {
                state.Diagnostic = ex.Message;
                state.Completed = true;
            }
        }
    }

    private void RunScriptedBuildCancelProbeActions(ScriptedBuildCancelProbeState state)
    {
        if (CurrentSession is null || state.Completed)
        {
            return;
        }

        string outputDirectory = ResolveScriptedBuildOutputDirectory();
        if (!state.FirstStarted)
        {
            state.FirstStarted = CurrentSession.TryStartScriptedBuildProbe(outputDirectory, runAfterBuild: false, out state.Diagnostic);
            state.FirstSnapshot = CurrentSession.CaptureScriptedBuildProbe();
            return;
        }

        if (!state.CancelRequested)
        {
            state.FirstSnapshot = CurrentSession.CaptureScriptedBuildProbe();
            state.ChildPid = TryReadCancelChildPid(outputDirectory);
            if (state.FirstSnapshot.IsRunning && state.ChildPid.HasValue)
            {
                CurrentSession.CancelScriptedBuildProbe();
                state.CancelRequested = true;
            }

            return;
        }

        if (!state.FirstCompleted)
        {
            state.FirstSnapshot = CurrentSession.CaptureScriptedBuildProbe();
            if (state.FirstSnapshot.Result is null)
            {
                return;
            }

            state.FirstCompleted = true;
            state.FirstCompletedAt = DateTimeOffset.UtcNow;
            state.FirstExitCode = state.FirstSnapshot.Result.ExitCode;
            state.ChildPid ??= TryReadCancelChildPid(outputDirectory);
        }

        if (!state.ChildObserved)
        {
            state.ChildAliveAfterCancel = state.ChildPid.HasValue && IsProcessAlive(state.ChildPid.Value);
            if (state.ChildAliveAfterCancel &&
                DateTimeOffset.UtcNow - state.FirstCompletedAt < TimeSpan.FromSeconds(2))
            {
                return;
            }

            state.ChildObserved = true;
        }

        if (!state.RerunStarted)
        {
            state.RerunStarted = CurrentSession.TryStartScriptedBuildProbe(outputDirectory, runAfterBuild: false, out state.RerunDiagnostic);
            state.RerunSnapshot = CurrentSession.CaptureScriptedBuildProbe();
            if (!state.RerunStarted)
            {
                state.Completed = true;
            }

            return;
        }

        state.RerunSnapshot = CurrentSession.CaptureScriptedBuildProbe();
        if (state.RerunSnapshot.Result is not null)
        {
            state.RerunCompleted = true;
            state.Completed = true;
        }
    }

    private void RunScriptedBuildProbeActions(
        ref bool started,
        ref bool completed,
        ref string diagnostic,
        ref ScriptedBuildProbeSnapshot snapshot)
    {
        if (CurrentSession is null || completed)
        {
            return;
        }

        if (!started)
        {
            string outputDirectory = ResolveScriptedBuildOutputDirectory();
            started = CurrentSession.TryStartScriptedBuildProbe(outputDirectory, runAfterBuild: false, out diagnostic);
            snapshot = CurrentSession.CaptureScriptedBuildProbe();
            return;
        }

        snapshot = CurrentSession.CaptureScriptedBuildProbe();
        completed = snapshot.Result is not null;
    }

    private string ResolveScriptedBuildOutputDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_options.BuildOutputPath))
        {
            return Path.GetFullPath(_options.BuildOutputPath);
        }

        string root = string.IsNullOrWhiteSpace(_options.LogDirectory)
            ? Path.Combine(Environment.CurrentDirectory, "artifacts", "editor-build-probe")
            : Path.Combine(_options.LogDirectory, "editor-build-probe");
        return Path.GetFullPath(root);
    }

    private static void WriteScriptedBuildProbeSummary(
        bool started,
        bool completed,
        bool timedOut,
        string diagnostic,
        ScriptedBuildProbeSnapshot snapshot,
        ScriptedBuildFrameStats frameStats)
    {
        BuildResult? result = snapshot.Result;
        string phaseTimings = result is null || result.PhaseTimingsMs.Count == 0
            ? "none"
            : string.Join(
                "|",
                result.PhaseTimingsMs.OrderBy(static item => item.Key)
                    .Select(static item => $"{item.Key}:{item.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}"));
        Console.WriteLine(
            "editor_build_probe " +
            "schema=pixelengine.editor-build-probe/v1, " +
            $"started={started}, " +
            $"completed={completed}, " +
            $"timed_out={timedOut}, " +
            $"running={snapshot.IsRunning}, " +
            $"phase={snapshot.Phase}, " +
            $"percent={snapshot.Percent.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"ok={result?.Ok.ToString() ?? "<missing>"}, " +
            $"exit_code={result?.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<missing>"}, " +
            $"rid={result?.Rid ?? "<missing>"}, " +
            $"channel={result?.Channel ?? "<missing>"}, " +
            $"configuration={result?.Configuration ?? "<missing>"}, " +
            $"package_archive={result?.PackageArchive ?? "<missing>"}, " +
            $"size_bytes={result?.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<missing>"}, " +
            $"sha256={result?.Sha256 ?? "<missing>"}, " +
            $"error_present={!string.IsNullOrWhiteSpace(result?.Error)}, " +
            $"error={SanitizeSummaryValue(result?.Error ?? "<missing>")}, " +
            $"phase_timing_count={result?.PhaseTimingsMs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0"}, " +
            $"phase_timings={phaseTimings}, " +
            $"ui_frame_count={frameStats.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"ui_avg_delta_ms={frameStats.AverageMilliseconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"ui_max_delta_ms={frameStats.MaxMilliseconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"log_count={snapshot.LogCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"diagnostic={SanitizeSummaryValue(diagnostic)}");
    }

    private ScriptedPlayerRunProbeResult RunScriptedPlayerProbe(BuildResult? result)
    {
        if (result is null || !result.Ok)
        {
            return new ScriptedPlayerRunProbeResult(
                Started: false,
                Completed: false,
                ExitCode: -1,
                CaptureExists: false,
                WindowCompleted: false,
                ContentLoaded: false,
                StdoutPath: string.Empty,
                StderrPath: string.Empty,
                CapturePath: string.Empty,
                Diagnostic: "构建未成功，未启动 player。");
        }

        if (string.IsNullOrWhiteSpace(result.LauncherExe) || !File.Exists(result.LauncherExe))
        {
            return new ScriptedPlayerRunProbeResult(
                Started: false,
                Completed: false,
                ExitCode: -1,
                CaptureExists: false,
                WindowCompleted: false,
                ContentLoaded: false,
                StdoutPath: string.Empty,
                StderrPath: string.Empty,
                CapturePath: string.Empty,
                Diagnostic: "构建结果缺少可启动 LauncherExe。");
        }

        string root = string.IsNullOrWhiteSpace(_options.BuildOutputPath)
            ? Path.Combine(Environment.CurrentDirectory, "artifacts", "editor-build-run-probe")
            : Path.Combine(Path.GetFullPath(_options.BuildOutputPath), "run-probe");
        _ = Directory.CreateDirectory(root);
        string stdoutPath = Path.Combine(root, "player-stdout.txt");
        string stderrPath = Path.Combine(root, "player-stderr.txt");
        string capturePath = Path.Combine(root, "player-capture.bmp");
        string workingDirectory = string.IsNullOrWhiteSpace(result.PlayerDir)
            ? Path.GetDirectoryName(result.LauncherExe) ?? Environment.CurrentDirectory
            : result.PlayerDir;
        ProcessStartInfo startInfo = new()
        {
            FileName = result.LauncherExe,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--window-ticks");
        startInfo.ArgumentList.Add("80");
        startInfo.ArgumentList.Add("--no-hot-reload");
        startInfo.ArgumentList.Add("--capture-frame");
        startInfo.ArgumentList.Add(capturePath);
        try
        {
            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 player 进程。");
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            File.WriteAllText(stdoutPath, stdout);
            File.WriteAllText(stderrPath, stderr);
            return new ScriptedPlayerRunProbeResult(
                Started: true,
                Completed: process.ExitCode == 0,
                ExitCode: process.ExitCode,
                CaptureExists: File.Exists(capturePath),
                WindowCompleted: stdout.Contains("window_frame_probe", StringComparison.Ordinal),
                ContentLoaded: stdout.Contains("PixelEngine.Demo", StringComparison.Ordinal) &&
                    stdout.Contains("RID:", StringComparison.Ordinal),
                StdoutPath: stdoutPath,
                StderrPath: stderrPath,
                CapturePath: capturePath,
                Diagnostic: process.ExitCode == 0 ? "player 短跑完成。" : $"player 退出码 {process.ExitCode}。");
        }
        catch (Exception ex) when (!OperatingSystem.IsBrowser())
        {
            return new ScriptedPlayerRunProbeResult(
                Started: true,
                Completed: false,
                ExitCode: -1,
                CaptureExists: File.Exists(capturePath),
                WindowCompleted: false,
                ContentLoaded: false,
                StdoutPath: stdoutPath,
                StderrPath: stderrPath,
                CapturePath: capturePath,
                Diagnostic: ex.Message);
        }
    }

    private static void WriteScriptedPlayerRunProbeSummary(ScriptedPlayerRunProbeResult result)
    {
        Console.WriteLine(
            "editor_build_run_probe " +
            "schema=pixelengine.editor-build-run-probe/v1, " +
            $"started={result.Started}, " +
            $"completed={result.Completed}, " +
            $"exit_code={result.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"capture_exists={result.CaptureExists}, " +
            $"window_completed={result.WindowCompleted}, " +
            $"content_loaded={result.ContentLoaded}, " +
            $"stdout={result.StdoutPath}, " +
            $"stderr={result.StderrPath}, " +
            $"capture={result.CapturePath}, " +
            $"diagnostic={SanitizeSummaryValue(result.Diagnostic)}");
    }

    private static string SanitizeSummaryValue(string value)
    {
        return value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace(',', ';');
    }

    private static int? TryReadCancelChildPid(string outputDirectory)
    {
        string path = Path.Combine(outputDirectory, "cancel-child.pid");
        if (!File.Exists(path))
        {
            return null;
        }

        string text = File.ReadAllText(path).Trim();
        return int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int pid)
            ? pid
            : null;
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void WriteScriptedBuildCancelProbeSummary(ScriptedBuildCancelProbeState state)
    {
        BuildResult? first = state.FirstSnapshot.Result;
        BuildResult? rerun = state.RerunSnapshot.Result;
        Console.WriteLine(
            "editor_build_cancel_probe " +
            "schema=pixelengine.editor-build-cancel-probe/v1, " +
            $"first_started={state.FirstStarted}, " +
            $"cancel_requested={state.CancelRequested}, " +
            $"first_completed={state.FirstCompleted}, " +
            $"first_exit_code={first?.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<missing>"}, " +
            $"child_pid={state.ChildPid?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<missing>"}, " +
            $"child_alive_after_cancel={state.ChildAliveAfterCancel}, " +
            $"rerun_started={state.RerunStarted}, " +
            $"rerun_completed={state.RerunCompleted}, " +
            $"rerun_ok={rerun?.Ok.ToString() ?? "<missing>"}, " +
            $"rerun_exit_code={rerun?.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<missing>"}, " +
            $"package_archive={rerun?.PackageArchive ?? "<missing>"}, " +
            $"diagnostic={SanitizeSummaryValue(state.Diagnostic)}, " +
            $"rerun_diagnostic={SanitizeSummaryValue(state.RerunDiagnostic)}");
    }

    private static void WriteScriptedBuildSettingsProbeSummary(ScriptedBuildSettingsProbeState state)
    {
        bool matches = state.Before == state.After;
        Console.WriteLine(
            "editor_build_settings_probe " +
            "schema=pixelengine.editor-build-settings-probe/v1, " +
            $"applied={state.Applied}, " +
            $"close_requested={state.CloseRequested}, " +
            $"reopened={state.Reopened}, " +
            $"captured={state.Captured}, " +
            $"matches={matches}, " +
            $"product={SanitizeSummaryValue(state.After.ProductName)}, " +
            $"version={SanitizeSummaryValue(state.After.Version)}, " +
            $"configuration={SanitizeSummaryValue(state.After.Configuration)}, " +
            $"include_symbols={state.After.IncludeSymbols}, " +
            $"package_whole_content={state.After.PackageWholeContent}, " +
            $"run_after_build={state.After.RunAfterBuild}, " +
            $"included_scene_count={state.After.IncludedSceneCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"startup_scene={SanitizeSummaryValue(state.After.StartupScene)}, " +
            $"diagnostic={SanitizeSummaryValue(state.Diagnostic)}");
    }

    private static void WriteScriptedMenuLayoutProbeSummary(ScriptedMenuLayoutProbeState state)
    {
        Console.WriteLine(
            "editor_menu_layout_probe " +
            "schema=pixelengine.editor-menu-layout-probe/v1, " +
            $"completed={state.Completed}, " +
            $"succeeded={state.Succeeded}, " +
            $"required_panels_shown={state.RequiredPanelsShown}, " +
            $"reset_requested={state.ResetRequested}, " +
            $"created_object={state.CreatedObject}, " +
            $"duplicated_object={state.DuplicatedObject}, " +
            $"renamed_object={state.RenamedObject}, " +
            $"deleted_object={state.DeletedObject}, " +
            $"new_scene_created={state.NewSceneCreated}, " +
            $"opened_original_scene={state.OpenedOriginalScene}, " +
            $"panel_count={state.PanelCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"initial_count={state.InitialCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"final_count={state.FinalCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"start_scene={SanitizeSummaryValue(state.StartScene)}, " +
            $"diagnostic={SanitizeSummaryValue(state.Diagnostic)}");
    }

    private static void WriteScriptedHierarchyProbeSummary(ScriptedHierarchyProbeState state)
    {
        Console.WriteLine(
            "editor_hierarchy_probe " +
            "schema=pixelengine.editor-hierarchy-probe/v1, " +
            $"completed={state.Completed}, " +
            $"created={state.Created}, " +
            $"child_parented={state.ChildParented}, " +
            $"cycle_rejected={state.CycleRejected}, " +
            $"cycle_prevented={state.CyclePrevented}, " +
            $"duplicated={state.Duplicated}, " +
            $"renamed={state.Renamed}, " +
            $"reparented_to_root={state.ReparentedToRoot}, " +
            $"selection_linked={state.SelectionLinked}, " +
            $"deleted={state.Deleted}, " +
            $"initial_count={state.InitialCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"final_count={state.FinalCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"diagnostic={SanitizeSummaryValue(state.Diagnostic)}");
    }

    private static void WriteScriptedDefaultWorkbenchProbeSummary(ScriptedDefaultWorkbenchProbeState state)
    {
        Console.WriteLine(
            "editor_default_workbench_probe " +
            "schema=pixelengine.editor-default-workbench-probe/v1, " +
            $"completed={state.Completed}, " +
            $"succeeded={state.Succeeded}, " +
            $"project_created={state.ProjectCreated}, " +
            $"project_root={SanitizeSummaryValue(state.ProjectRoot)}, " +
            $"required_panels_shown={state.RequiredPanelsShown}, " +
            $"panel_count={state.PanelCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"game_object_created={state.GameObjectCreated}, " +
            $"script_source_created={state.ScriptSourceCreated}, " +
            $"script_source={SanitizeSummaryValue(state.ScriptSourcePath)}, " +
            $"script_hot_reload_requested={state.ScriptHotReloadRequested}, " +
            $"script_hot_reload_applied={state.ScriptHotReloadApplied}, " +
            $"behaviour_registered={state.BehaviourRegistered}, " +
            $"behaviour_type={SanitizeSummaryValue(state.BehaviourTypeName)}, " +
            $"behaviour_attached={state.BehaviourAttached}, " +
            $"scene_saved={state.SceneSaved}, " +
            $"play_entered={state.PlayEntered}, " +
            $"play_exited={state.PlayExited}, " +
            $"play_status={SanitizeSummaryValue(state.PlayStatus)}, " +
            $"build_settings_shown={state.BuildSettingsShown}, " +
            $"build_output_ready={state.BuildOutputReady}, " +
            $"build_output={SanitizeSummaryValue(state.BuildOutputPath)}, " +
            $"build_started={state.BuildStarted}, " +
            $"build_completed={state.BuildCompleted}, " +
            $"build_ok={state.BuildOk}, " +
            $"build_exit_code={state.BuildExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"build_package_archive={SanitizeSummaryValue(state.BuildPackageArchive)}, " +
            $"build_error_present={!string.IsNullOrWhiteSpace(state.BuildError)}, " +
            $"build_log_count={state.BuildSnapshot.LogCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"diagnostic={SanitizeSummaryValue(state.Diagnostic)}");
    }

    private void RunScriptedGameViewProbeActions(int executedTicks, ScriptedGameViewProbeState state)
    {
        if (CurrentSession is null || state.Finished)
        {
            return;
        }

        Engine engine = CurrentSession.Engine;
        if (!state.InputRegistered && executedTicks >= 4)
        {
            ScriptInputApi input = engine.Context.GetService<ScriptInputApi>();
            engine.Phases.Register(EnginePhase.InputAndTime, _ =>
            {
                if (state.InjectMovement && engine.Mode == EngineExecutionMode.Play)
                {
                    input.Update(state.MovementKeys, [], 360f, 240f, 0f);
                }
            });
            state.InputRegistered = true;
            return;
        }

        if (!state.PlayEntered && executedTicks >= 8)
        {
            CurrentSession.EnterPlayMode();
            state.PlayEntered = engine.Mode == EngineExecutionMode.Play;
            return;
        }

        if (!state.StartCaptured && executedTicks >= 18 && TryCapturePlayerProbe(engine, out float startX, out int startVisualCommands))
        {
            state.StartX = startX;
            state.StartVisualCommands = startVisualCommands;
            state.StartCaptured = true;
            state.InjectMovement = true;
            return;
        }

        if (state.StartCaptured && state.InjectMovement && executedTicks >= 58)
        {
            state.InjectMovement = false;
            if (TryCapturePlayerProbe(engine, out float endX, out int endVisualCommands))
            {
                state.EndX = endX;
                state.EndVisualCommands = endVisualCommands;
                state.PlayerMoved = endX > state.StartX + 0.5f;
                state.RenderOverlayCommands = CaptureRenderOverlayCommandCount(engine);
                state.RemainedInPlay = engine.Mode == EngineExecutionMode.Play;
                state.FirstPlayVerified = state.PlayerMoved &&
                    state.EndVisualCommands > 0 &&
                    state.RenderOverlayCommands > 0 &&
                    state.RemainedInPlay;
            }
            else
            {
                state.Diagnostic = "Play 场景中未找到 PlayerController/PlayerVisual。";
                state.Finished = true;
            }

            return;
        }

        if (state.FirstPlayVerified && !state.FirstPlayExited && executedTicks >= 60)
        {
            Hosting.EditorPlaySessionResult exit = CurrentSession.ExitEditorPlay();
            state.FirstPlayExited = exit.Succeeded && engine.Mode == EngineExecutionMode.Edit;
            state.ExitUiStackDepth = CaptureGameUiStackDepth(engine);
            if (!state.FirstPlayExited)
            {
                state.Diagnostic = $"首次 Play 退出失败：{exit.Message}";
                state.Finished = true;
            }

            return;
        }

        if (state.FirstPlayExited && !state.SecondPlayEntered && executedTicks >= 64)
        {
            Hosting.EditorPlaySessionResult enter = CurrentSession.EnterPlayTemporary();
            state.SecondPlayEntered = enter.Succeeded && engine.Mode == EngineExecutionMode.Play;
            if (!state.SecondPlayEntered)
            {
                state.Diagnostic = $"第二次 Play 进入失败：{enter.Message}";
                state.Finished = true;
            }

            return;
        }

        if (state.SecondPlayEntered && executedTicks >= 74)
        {
            state.Presentation = CurrentSession.CaptureScriptedGameViewPresentation();
            state.SecondUiStackDepth = CaptureGameUiStackDepth(engine);
            state.SecondControllerFound = TryCaptureGameUiControllerProbe(
                engine,
                out state.SecondControllerEnabled,
                out state.SecondControllerFaulted,
                out state.SecondControllerException);
            bool secondVisualReady = TryCapturePlayerProbe(engine, out _, out int secondVisualCommands) &&
                secondVisualCommands > 0;
            state.SecondVisualCommands = secondVisualCommands;
            state.SecondPlayUiRestored = state.SecondUiStackDepth >= 2 &&
                state.SecondControllerFound &&
                state.SecondControllerEnabled &&
                !state.SecondControllerFaulted;
            state.Completed = state.FirstPlayVerified &&
                state.FirstPlayExited &&
                state.ExitUiStackDepth == 0 &&
                state.SecondPlayEntered &&
                state.SecondPlayUiRestored &&
                secondVisualReady &&
                state.Presentation.IsSynchronized &&
                engine.Mode == EngineExecutionMode.Play;
            state.Diagnostic = state.Completed
                ? "Game View 玩家移动与 Play→Stop→Play UI 生命周期探针完成。"
                : $"探针未满足验收：first={state.FirstPlayVerified}, exit={state.FirstPlayExited}, exit_stack={state.ExitUiStackDepth}, second={state.SecondPlayEntered}, second_stack={state.SecondUiStackDepth}, controller={state.SecondControllerFound}/{state.SecondControllerEnabled}/{state.SecondControllerFaulted}, second_visual={state.SecondVisualCommands}, presentation={state.Presentation.IsSynchronized}, exception={state.SecondControllerException}。";
            state.Finished = true;
        }
    }

    private static int CaptureGameUiStackDepth(Engine engine)
    {
        return engine.Context.TryGetService(out PixelEngine.UI.GameUiHost host)
            ? host.Documents.StackCount
            : -1;
    }

    private static bool TryCaptureGameUiControllerProbe(
        Engine engine,
        out bool enabled,
        out bool faulted,
        out string exception)
    {
        enabled = false;
        faulted = false;
        exception = string.Empty;
        PixelEngine.Scripting.Scene? scriptScene = engine.CurrentScene?.ScriptScene;
        if (scriptScene is null)
        {
            return false;
        }

        ScriptEntityInspection[] entities = scriptScene.CaptureInspectionSnapshot();
        for (int i = 0; i < entities.Length; i++)
        {
            ScriptComponentInspection[] components = entities[i].Components;
            for (int j = 0; j < components.Length; j++)
            {
                ScriptComponentInspection component = components[j];
                if (!component.TypeName.EndsWith(".GameUiDemoController", StringComparison.Ordinal))
                {
                    continue;
                }

                enabled = component.Enabled;
                faulted = component.Faulted;
                exception = component.Behaviour.LastException?.ToString() ?? string.Empty;
                return true;
            }
        }

        return false;
    }

    private static bool TryCapturePlayerProbe(Engine engine, out float centerX, out int visualCommands)
    {
        centerX = 0f;
        visualCommands = 0;
        PixelEngine.Scripting.Scene? scriptScene = engine.CurrentScene?.ScriptScene;
        if (scriptScene is null)
        {
            return false;
        }

        ScriptEntityInspection[] entities = scriptScene.CaptureInspectionSnapshot();
        bool foundPlayer = false;
        for (int i = 0; i < entities.Length; i++)
        {
            ScriptComponentInspection[] components = entities[i].Components;
            for (int j = 0; j < components.Length; j++)
            {
                ScriptComponentInspection component = components[j];
                if (component.TypeName.EndsWith(".PlayerController", StringComparison.Ordinal))
                {
                    object? value = component.Behaviour.GetType().GetProperty("CenterX")?.GetValue(component.Behaviour);
                    if (value is float x && float.IsFinite(x))
                    {
                        centerX = x;
                        foundPlayer = true;
                    }
                }
                else if (component.TypeName.EndsWith(".PlayerVisual", StringComparison.Ordinal))
                {
                    object? value = component.Behaviour.GetType().GetProperty("LastOverlayCommandsSubmitted")?.GetValue(component.Behaviour);
                    if (value is int count)
                    {
                        visualCommands = count;
                    }
                }
            }
        }

        return foundPlayer;
    }

    private static int CaptureRenderOverlayCommandCount(Engine engine)
    {
        return engine.Context.TryGetService(out RenderPipeline pipeline)
            ? pipeline.CurrentViewportOverlayCount
            : 0;
    }

    private static void WriteScriptedGameViewProbeSummary(ScriptedGameViewProbeState state)
    {
        ScriptedGameViewPresentationSnapshot presentation = state.Presentation;
        Console.WriteLine(
            "editor_gameview_probe " +
            "schema=pixelengine.editor-gameview-probe/v2, " +
            $"completed={state.Completed}, " +
            $"input_registered={state.InputRegistered}, " +
            $"play_entered={state.PlayEntered}, " +
            $"start_x={state.StartX.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"end_x={state.EndX.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"player_moved={state.PlayerMoved}, " +
            $"visual_commands={state.EndVisualCommands}, " +
            $"render_overlay_commands={state.RenderOverlayCommands}, " +
            $"remained_in_play={state.RemainedInPlay}, " +
            $"first_play_verified={state.FirstPlayVerified}, " +
            $"first_play_exited={state.FirstPlayExited}, " +
            $"exit_ui_stack_depth={state.ExitUiStackDepth}, " +
            $"second_play_entered={state.SecondPlayEntered}, " +
            $"second_ui_stack_depth={state.SecondUiStackDepth}, " +
            $"second_controller_found={state.SecondControllerFound}, " +
            $"second_controller_enabled={state.SecondControllerEnabled}, " +
            $"second_controller_faulted={state.SecondControllerFaulted}, " +
            $"second_visual_commands={state.SecondVisualCommands}, " +
            $"second_play_ui_restored={state.SecondPlayUiRestored}, " +
            $"presentation_synchronized={presentation.IsSynchronized}, " +
            $"preset_id={SanitizeSummaryValue(presentation.PresetId)}, " +
            $"scale_percent={presentation.ScalePercent.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"maximize_on_play={presentation.MaximizeOnPlay}, " +
            $"maximized={presentation.IsMaximized}, " +
            $"presentation_source={presentation.Source}, " +
            $"presentation={presentation.PresentationWidth}x{presentation.PresentationHeight}, " +
            $"presentation_revision={presentation.PresentationRevision}, " +
            $"world_content={FormatPresentationViewport(presentation.WorldContentRect)}, " +
            $"display_area={FormatGameViewRect(presentation.DisplayAreaRect)}, " +
            $"image_rect={FormatGameViewRect(presentation.ImageRect)}, " +
            $"visible_viewport={FormatGameViewRect(presentation.VisibleViewportRect)}, " +
            $"framebuffer_scale={presentation.FramebufferScale.X.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}x{presentation.FramebufferScale.Y.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"toolbar_density={presentation.ToolbarDensity}, " +
            $"toolbar_fits={presentation.ToolbarFits}, " +
            $"toolbar_available={presentation.ToolbarAvailableWidth.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"toolbar_occupied={presentation.ToolbarOccupiedWidth.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"toolbar_overflow_visible={presentation.ToolbarOverflowVisible}, " +
            $"diagnostic={SanitizeSummaryValue(state.Diagnostic)}");
    }

    private static string FormatGameViewRect(in GameViewRect rect)
    {
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{rect.X:F3}:{rect.Y:F3}:{rect.Width:F3}x{rect.Height:F3}");
    }

    private static string FormatPresentationViewport(in PresentationViewport viewport)
    {
        return $"{viewport.X}:{viewport.Y}:{viewport.Width}x{viewport.Height}:{viewport.SourceWidth}x{viewport.SourceHeight}:{viewport.TargetWidth}x{viewport.TargetHeight}";
    }

    private void RunScriptedProbeActions(
        int executedTicks,
        ref bool playEntered,
        ref bool playPaused,
        ref bool playStepped,
        ref bool playResumed,
        ref bool playExited,
        ref bool sceneSaved,
        ref bool projectClosed)
    {
        if (CurrentSession is null)
        {
            return;
        }

        if (!playEntered && executedTicks >= 10)
        {
            CurrentSession.EnterPlayMode();
            playEntered = true;
            return;
        }

        if (playEntered && !playPaused && executedTicks >= 14)
        {
            CurrentSession.TogglePauseMode();
            playPaused = CurrentSession.Engine.Mode == EngineExecutionMode.Paused;
            return;
        }

        if (playPaused && !playStepped && executedTicks >= 15)
        {
            long before = CurrentSession.Engine.Context.Clock.SimTickIndex;
            CurrentSession.StepOnce();
            playStepped = CurrentSession.Engine.Mode == EngineExecutionMode.Paused &&
                CurrentSession.Engine.Context.Clock.SimTickIndex == before + 1;
            return;
        }

        if (playStepped && !playResumed && executedTicks >= 16)
        {
            CurrentSession.EnterPlayMode();
            playResumed = CurrentSession.Engine.Mode == EngineExecutionMode.Play;
            return;
        }

        if (playEntered && !playExited && executedTicks >= 20)
        {
            CurrentSession.EnterEditMode();
            playExited = true;
            return;
        }

        if (playExited && !sceneSaved && executedTicks >= 30)
        {
            CurrentSession.SaveScene();
            sceneSaved = true;
            return;
        }

        if (sceneSaved && !projectClosed && executedTicks >= 40)
        {
            CloseProject();
            projectClosed = true;
        }
    }

    public void CreateProject(string projectRoot, string name)
    {
        HandleTransitionResult(_transitions.Request(
            EditorTransitionKind.CreateProject,
            () => TryCreateAndQueueProject(projectRoot, name),
            projectRoot));
    }

    public void OpenProjectPath(string projectRootOrFile)
    {
        try
        {
            OpenProject(EditorProject.Load(projectRootOrFile));
        }
        catch (Exception exception)
        {
            LastProjectError = exception.Message;
            ConsoleStore.AddProjectError("project", exception.Message);
        }
    }

    /// <summary>
    /// 排队打开项目；实际 Session 在帧末由 <see cref="ApplyPendingProject"/> 创建。
    /// </summary>
    public void OpenProject(EditorProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        HandleTransitionResult(_transitions.Request(
            EditorTransitionKind.OpenProject,
            () => QueueProject(project),
            project.ProjectRoot));
    }

    public void CloseProject()
    {
        HandleTransitionResult(_transitions.Request(
            EditorTransitionKind.CloseProject,
            QueueCloseProject,
            CurrentProject?.ProjectRoot));
    }

    public void FocusProjectPicker(ProjectPickerMode mode)
    {
        ProjectPicker.Focus(mode);
    }

    public void SetRecentProjectFavorite(string projectPath, bool favorite)
    {
        PersistRecentProjectsChange(
            RecentProjects.SetFavorite(projectPath, favorite),
            "更新工程收藏状态失败");
    }

    public void RemoveRecentProject(string projectPath)
    {
        PersistRecentProjectsChange(
            RecentProjects.Remove(projectPath),
            "移除最近工程失败");
    }

    public void ResetLayout()
    {
        if (!Layout.TryResetLayout(out string diagnostic))
        {
            LastProjectError = diagnostic;
            ConsoleStore.AddProjectError("layout", diagnostic);
        }

        CurrentSession?.ResetLayout();
    }

    private void PersistRecentProjectsChange(bool changed, string failurePrefix)
    {
        if (!changed)
        {
            return;
        }

        try
        {
            RecentProjects.Save();
            LastProjectError = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LastProjectError = $"{failurePrefix}：{exception.Message}";
            ConsoleStore.AddProjectError("recent-projects", LastProjectError);
        }
    }

    public void EnterPlayMode()
    {
        CurrentSession?.EnterPlayMode();
    }

    public void EnterEditMode()
    {
        CurrentSession?.EnterEditMode();
    }

    public void TogglePauseMode()
    {
        CurrentSession?.TogglePauseMode();
    }

    public void StepOnce()
    {
        if (CurrentSession is { } session)
        {
            // 顶部工具栏在 Engine 正在执行的 ImGui draw callback 内触发；此处若同步再进
            // Engine.StepOnce，会嵌套第二个 ImGui frame 并破坏 native UI 栈。延迟到当前 tick
            // 完整返回后执行，语义仍是一次单步，同时消除 render/UI reentrancy。
            _deferredFrameActions.RequestStepOnce(session);
        }
    }

    private void ApplyDeferredFrameActions()
    {
        EditorProjectSession? session = CurrentSession;
        if (_deferredFrameActions.TryConsumeStepOnce(session))
        {
            session!.StepOnce();
        }
    }

    public void CreateGameObject()
    {
        CurrentSession?.CreateRootGameObject();
    }

    public void CreateChildGameObject()
    {
        CurrentSession?.CreateChildGameObject();
    }

    public void DeleteSelectedGameObject()
    {
        CurrentSession?.DeleteSelectedGameObject();
    }

    public void DuplicateSelectedGameObject()
    {
        CurrentSession?.DuplicateSelectedGameObject();
    }

    public void RenameSelectedGameObject()
    {
        CurrentSession?.RenameSelectedGameObject();
    }

    public void AddComponentToSelected(string typeName)
    {
        CurrentSession?.AddComponentToSelected(typeName);
    }

    public string[] GetBehaviourTypeNames()
    {
        return CurrentSession?.GetBehaviourTypeNames() ?? [];
    }

    public void CreatePrefabFromSelection()
    {
        CurrentSession?.CreatePrefabFromSelection();
    }

    public void InstantiatePrefab(string assetPath)
    {
        _ = InstantiatePrefab(assetPath, out _);
    }

    public bool InstantiatePrefab(string assetPath, out string diagnostic)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            diagnostic = "Prefab 资产路径不能为空。";
            return RecordPrefabInstantiationResult(success: false, diagnostic);
        }

        if (CurrentSession is null)
        {
            diagnostic = "当前没有打开的工程，无法实例化 Prefab。";
            return RecordPrefabInstantiationResult(success: false, diagnostic);
        }

        try
        {
            CurrentSession.InstantiatePrefab(assetPath);
            diagnostic = $"已实例化 Prefab：{assetPath}";
            return RecordPrefabInstantiationResult(success: true, diagnostic);
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            JsonException or
            NotSupportedException or
            FormatException or
            ArgumentException)
        {
            diagnostic = $"Prefab 实例化失败：{assetPath}。{exception.Message}";
            return RecordPrefabInstantiationResult(success: false, diagnostic);
        }
    }

    private bool RecordPrefabInstantiationResult(bool success, string diagnostic)
    {
        LastAssetOpenDiagnostic = diagnostic;
        ConsoleStore.Add(new EditorConsoleEntry(
            DateTimeOffset.UtcNow,
            EditorConsoleCategory.Asset,
            success ? EditorConsoleSeverity.Info : EditorConsoleSeverity.Error,
            "prefab-instantiator",
            diagnostic));
        return success;
    }

    public bool OpenScriptAsset(string assetPath, out string diagnostic)
    {
        return OpenScriptAsset(assetPath, line: 1, column: 1, out diagnostic);
    }

    public bool OpenScriptAsset(string assetPath, int line, int column, out string diagnostic)
    {
        if (CurrentSession is null)
        {
            diagnostic = "当前没有打开的工程，无法打开脚本资产。";
            LastAssetOpenDiagnostic = diagnostic;
            return false;
        }

        EditorScriptAssetOpenResult result = CurrentSession.OpenScriptAsset(assetPath, line, column);
        diagnostic = result.Diagnostic;
        LastAssetOpenDiagnostic = diagnostic;
        ConsoleStore.AddAssetOpenResult(result);
        return result.Success;
    }

    public bool OpenCSharpProject(out string diagnostic)
    {
        if (CurrentSession is null)
        {
            diagnostic = "当前没有打开的工程，无法打开 C# 工程。";
            LastAssetOpenDiagnostic = diagnostic;
            return false;
        }

        EditorCodeWorkspaceOpenResult result = CurrentSession.OpenCodeProject();
        diagnostic = result.Diagnostic;
        LastAssetOpenDiagnostic = diagnostic;
        ConsoleStore.AddCodeWorkspaceOpenResult(result);
        return result.Success;
    }

    public void ShowProjectSettings()
    {
        _ = CurrentSession?.ShowProjectSettings();
    }

    public void ShowPlayerSettings()
    {
        _ = CurrentSession?.ShowPlayerSettings();
    }

    public void ShowBuildSettings()
    {
        _ = CurrentSession?.ShowBuildSettings();
    }

    public bool TryStartBuild(bool runAfterBuild, out string diagnostic)
    {
        if (CurrentSession is null)
        {
            diagnostic = "当前没有打开的工程。";
            return false;
        }

        return CurrentSession.TryStartBuild(runAfterBuild, out diagnostic);
    }

    public void ShowPreferences(EditorPreferencesCategory category = EditorPreferencesCategory.Appearance)
    {
        PreferencesWindow.Show(category);
    }

    public void ApplyCurrentUiPreferences(EditorUiScaleContextState state, float fontAtlasScale)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Apply(Preferences.Current.UiScale, fontAtlasScale);
    }

    public bool ShowPanel(string title)
    {
        return CurrentSession?.ShowPanel(title) == true;
    }

    public bool TryGetPanelVisibility(string title, out bool visible)
    {
        if (CurrentSession is not null)
        {
            return CurrentSession.TryGetPanelVisibility(title, out visible);
        }

        visible = false;
        return false;
    }

    public bool TrySetPanelVisibility(string title, bool visible)
    {
        return CurrentSession?.TrySetPanelVisibility(title, visible) == true;
    }

    public bool Undo()
    {
        return CurrentSession?.Undo() == true;
    }

    public bool Redo()
    {
        return CurrentSession?.Redo() == true;
    }

    public bool SaveScene()
    {
        if (CurrentSession is null)
        {
            return false;
        }

        try
        {
            CurrentSession.SaveScene();
            LastProjectError = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LastProjectError = $"保存场景失败：{exception.Message}";
            ConsoleStore.AddProjectError("scene-save", LastProjectError);
            return false;
        }
    }

    public bool SaveSceneAs()
    {
        if (CurrentSession is null)
        {
            return false;
        }

        try
        {
            _ = CurrentSession.SaveSceneAsAuto();
            RecordCurrentWorkspace();
            LastProjectError = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LastProjectError = $"另存场景失败：{exception.Message}";
            ConsoleStore.AddProjectError("scene-save-as", LastProjectError);
            return false;
        }
    }

    public bool NewScene()
    {
        if (CurrentSession is null)
        {
            return false;
        }

        EditorTransitionResult result = _transitions.Request(
            EditorTransitionKind.NewScene,
            CreateNewSceneAndRecord);
        HandleTransitionResult(result);
        return result.Status is EditorTransitionStatus.Executed or EditorTransitionStatus.ConfirmationRequired;
    }

    public bool OpenScene(string sceneRelativePath)
    {
        if (CurrentSession is null)
        {
            return false;
        }

        EditorTransitionResult result = _transitions.Request(
            EditorTransitionKind.OpenScene,
            () => OpenSceneAndRecord(sceneRelativePath),
            sceneRelativePath);
        HandleTransitionResult(result);
        return result.Status is EditorTransitionStatus.Executed or EditorTransitionStatus.ConfirmationRequired;
    }

    /// <summary>
    /// 从 Project Window rooted asset path 打开场景；复用统一 dirty guard 且不修改 Project StartScene。
    /// </summary>
    /// <param name="assetPath">Content/... rooted 场景路径。</param>
    /// <param name="diagnostic">打开、待确认或失败诊断。</param>
    /// <returns>场景已打开或 dirty-guard 转场已受理时返回 true。</returns>
    public bool OpenSceneAsset(string assetPath, out string diagnostic)
    {
        if (!EditorRootedBrowserPath.TryParse(assetPath, out EditorAssetPath path, out diagnostic) ||
            path.Root != EditorAssetRootKind.Content)
        {
            diagnostic = string.IsNullOrWhiteSpace(diagnostic)
                ? $"Scene 资产必须位于 Content logical root：{assetPath}"
                : diagnostic;
            return false;
        }

        string extension = Path.GetExtension(path.RelativePath);
        if (!string.Equals(extension, ".scene", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".world", StringComparison.OrdinalIgnoreCase))
        {
            diagnostic = $"Project Window 只能打开 .scene 或 .world：{assetPath}";
            return false;
        }

        LastProjectError = null;
        bool accepted = OpenScene(path.RelativePath);
        if (!accepted || !string.IsNullOrWhiteSpace(LastProjectError))
        {
            diagnostic = LastProjectError ?? $"场景打开请求未执行：{assetPath}";
            return false;
        }

        if (PendingTransition is { Kind: EditorTransitionKind.OpenScene } pending &&
            string.Equals(pending.Target, path.RelativePath, StringComparison.OrdinalIgnoreCase))
        {
            diagnostic = $"场景 {assetPath} 等待 Save / Discard / Cancel 确认。";
            return true;
        }

        bool opened = CurrentSession is not null &&
            string.Equals(CurrentSession.CurrentSceneRelativePath, path.RelativePath, StringComparison.OrdinalIgnoreCase);
        diagnostic = opened
            ? $"已打开场景 {assetPath}；Project StartScene 未改变。"
            : $"场景打开请求未完成：{assetPath}";
        return opened;
    }

    public void RequestExit()
    {
        HandleTransitionResult(_transitions.Request(
            EditorTransitionKind.Exit,
            () => _exitRequested = true));
    }

    private bool IsCurrentSceneDirtyAfterFlushing()
    {
        EditorProjectSession? session = CurrentSession;
        return session is not null &&
            FlushPendingAuthoringEditsAndCheckDirty(
                session.FlushPendingAuthoringEdits,
                () => session.SceneModel.IsDirty);
    }

    internal static bool FlushPendingAuthoringEditsAndCheckDirty(
        Action flushPendingEdits,
        Func<bool> isDirty)
    {
        ArgumentNullException.ThrowIfNull(flushPendingEdits);
        ArgumentNullException.ThrowIfNull(isDirty);
        flushPendingEdits();
        return isDirty();
    }

    internal void DrawTransitionPrompt()
    {
        if (_transitions.Pending is not { } pending)
        {
            return;
        }

        const string PopupTitle = "Unsaved Scene Changes";
        ImGui.OpenPopup(PopupTitle);
        bool open = true;
        if (!ImGui.BeginPopupModal(PopupTitle, ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            return;
        }

        ImGui.TextWrapped("当前场景有未保存修改。继续操作前请选择保存、放弃修改或取消。");
        ImGui.TextUnformatted($"操作：{pending.Kind}");
        if (!string.IsNullOrWhiteSpace(pending.Target))
        {
            ImGui.TextWrapped($"目标：{pending.Target}");
        }

        if (ImGui.Button("Save"))
        {
            ResolveTransitionFromPopup(EditorTransitionDecision.Save);
        }

        ImGui.SameLine();
        if (ImGui.Button("Don't Save"))
        {
            ResolveTransitionFromPopup(EditorTransitionDecision.Discard);
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel") || !open)
        {
            ResolveTransitionFromPopup(EditorTransitionDecision.Cancel);
        }

        ImGui.EndPopup();
    }

    internal void DrawTransientWindows()
    {
        ProjectPicker.Draw(this);
        DrawTransitionPrompt();
    }

    internal EditorTransitionResult ResolveTransition(EditorTransitionDecision decision)
    {
        EditorTransitionPrompt? pending = _transitions.Pending;
        EditorTransitionResult result = _transitions.Resolve(decision);
        if (result.Executed &&
            decision == EditorTransitionDecision.Discard &&
            pending?.Kind == EditorTransitionKind.Exit)
        {
            _allowDirtyShutdown = true;
        }

        HandleTransitionResult(result);
        return result;
    }

    private void ResolveTransitionFromPopup(EditorTransitionDecision decision)
    {
        EditorTransitionResult result = ResolveTransition(decision);
        if (result.Status is not EditorTransitionStatus.SaveFailed)
        {
            ImGui.CloseCurrentPopup();
        }
    }

    private void QueueProject(EditorProject project)
    {
        LastProjectError = null;
        bool hasCommandLineScene = !string.IsNullOrWhiteSpace(_commandLineSceneOverride);
        SceneOverridePath = hasCommandLineScene
            ? _commandLineSceneOverride
            : Preferences.Current.RestoreLastScene ? ResolveWorkspaceScene(project) : null;
        _pendingSceneOverrideFromWorkspace = !hasCommandLineScene && !string.IsNullOrWhiteSpace(SceneOverridePath);
        _commandLineSceneOverride = null;
        _pendingProject = project;
    }

    private string? ResolveWorkspaceScene(EditorProject project)
    {
        string? lastScene = Workspace.ResolveLastScene(project.ProjectRoot);
        if (string.IsNullOrWhiteSpace(lastScene))
        {
            return null;
        }

        try
        {
            if (File.Exists(project.ResolveSceneFullPath(lastScene)))
            {
                return lastScene;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            ConsoleStore.AddProjectError("workspace", $"忽略无效的上次场景 {lastScene}：{exception.Message}");
            return null;
        }

        ConsoleStore.AddProjectError("workspace", $"上次场景已不存在，改用工程 Start Scene：{lastScene}");
        return null;
    }

    private void CreateNewSceneAndRecord()
    {
        if (CurrentSession is null)
        {
            return;
        }

        try
        {
            _ = CurrentSession.NewSceneAuto();
            RecordCurrentWorkspace();
            LastProjectError = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LastProjectError = $"新建场景失败：{exception.Message}";
            ConsoleStore.AddProjectError("scene-new", LastProjectError);
        }
    }

    private void OpenSceneAndRecord(string sceneRelativePath)
    {
        if (CurrentSession is null)
        {
            return;
        }

        try
        {
            CurrentSession.OpenScene(sceneRelativePath);
            RecordCurrentWorkspace();
            LastProjectError = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or NotSupportedException)
        {
            LastProjectError = $"打开场景失败：{exception.Message}";
            ConsoleStore.AddProjectError("scene-open", LastProjectError);
        }
    }

    private void RecordCurrentWorkspace()
    {
        if (CurrentProject is null || CurrentSession is null)
        {
            return;
        }

        if (!Workspace.TryRecordProjectOpened(
            CurrentProject.ProjectRoot,
            CurrentSession.CurrentSceneRelativePath,
            DateTimeOffset.UtcNow,
            out string diagnostic))
        {
            ConsoleStore.AddProjectError("workspace", diagnostic);
        }
    }

    private void TryCreateAndQueueProject(string projectRoot, string name)
    {
        try
        {
            QueueProject(EditorProject.CreateNew(projectRoot, name));
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LastProjectError = exception.Message;
            ConsoleStore.AddProjectError("project", exception.Message);
        }
    }

    private void QueueCloseProject()
    {
        if (CurrentSession is null)
        {
            CurrentProject = null;
            _pendingProject = null;
            return;
        }

        _closeProjectRequested = true;
    }

    private EditorTransitionSaveResult TrySaveSceneForTransition()
    {
        if (CurrentSession is null)
        {
            return EditorTransitionSaveResult.Success();
        }

        try
        {
            CurrentSession.SaveScene();
            return EditorTransitionSaveResult.Success();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LastProjectError = $"保存场景失败：{exception.Message}";
            ConsoleStore.AddProjectError("scene-save", LastProjectError);
            return EditorTransitionSaveResult.Failure(LastProjectError);
        }
    }

    private void HandleTransitionResult(EditorTransitionResult result)
    {
        if (result.Status is EditorTransitionStatus.SaveFailed or EditorTransitionStatus.PendingTransitionExists)
        {
            LastProjectError = result.Diagnostic;
            ConsoleStore.AddProjectError("editor-transition", result.Diagnostic);
        }
    }

    // 帧末创建 EditorProjectSession，接管 Engine tick 与 ImGui 面板
    private void ApplyPendingProject(EditorShellWindow shellWindow)
    {
        if (_pendingProject is null)
        {
            return;
        }

        EditorProject project = _pendingProject;
        _pendingProject = null;
        CurrentSession?.Dispose();
        shellWindow.ShutdownProjectPickerGui();
        CurrentSession = null;
        CurrentProject = null;
        try
        {
            ProjectSettingsDto legacySettings = new ProjectSettingsStore(project).LoadRecoverable(
                out string projectSettingsDiagnostic);
            if (!string.IsNullOrWhiteSpace(projectSettingsDiagnostic))
            {
                ConsoleStore.AddProjectError("project-settings", projectSettingsDiagnostic);
            }

            if (!Preferences.TryMigrateLegacy(legacySettings.EditorPreferences, out string migrationDiagnostic))
            {
                ConsoleStore.AddProjectError("preferences", migrationDiagnostic);
            }

            try
            {
                CurrentSession = EditorProjectSession.Open(project, shellWindow.Window, this);
            }
            catch (Exception exception) when (
                _pendingSceneOverrideFromWorkspace &&
                exception is IOException or InvalidOperationException or JsonException or NotSupportedException)
            {
                ConsoleStore.AddProjectError(
                    "workspace",
                    $"恢复上次场景失败，已回退工程 Start Scene：{exception.Message}");
                SceneOverridePath = null;
                _pendingSceneOverrideFromWorkspace = false;
                CurrentSession = EditorProjectSession.Open(project, shellWindow.Window, this);
            }

            CurrentProject = project;
            ProjectPicker.Close();
            RecordCurrentWorkspace();
            SceneOverridePath = null;
            _pendingSceneOverrideFromWorkspace = false;
            RecentProjects.AddOrUpdate(project);
            try
            {
                RecentProjects.Save();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                ConsoleStore.AddProjectError("recent-projects", $"工程已打开，但最近工程列表保存失败：{exception.Message}");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or NotSupportedException)
        {
            LastProjectError = $"打开工程失败：{exception.Message}";
            ConsoleStore.AddProjectError("project-open", LastProjectError);
            CurrentSession?.Dispose();
            CurrentSession = null;
            CurrentProject = null;
            SceneOverridePath = null;
            _pendingSceneOverrideFromWorkspace = false;
            FocusProjectPicker(ProjectPickerMode.OpenProject);
        }
    }

    private void ApplyDeferredClose()
    {
        if (!_closeProjectRequested)
        {
            return;
        }

        CurrentSession?.Dispose();
        CurrentSession = null;
        CurrentProject = null;
        ProjectPicker.Focus(ProjectPickerMode.RecentProjects);
        _closeProjectRequested = false;
    }

    private void UpdateTitle(EditorShellWindow shellWindow)
    {
        shellWindow.SetTitle(
            CurrentProject?.Name,
            CurrentSession?.CurrentSceneDisplayName ?? CurrentProject?.ResolveDisplaySceneName(null),
            dirty: CurrentSession?.SceneModel.IsDirty == true);
    }

    private static string WriteCrashLog(Exception exception, string? logDirectory)
    {
        string directory = string.IsNullOrWhiteSpace(logDirectory)
            ? Path.Combine(Path.GetTempPath(), "PixelEngine", "EditorShellCrash")
            : logDirectory;
        _ = Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"editor-shell-crash-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.log");
        File.WriteAllText(path, exception.ToString());
        return path;
    }

    private void CaptureFrameIfRequested(EditorShellWindow shellWindow)
    {
        if (string.IsNullOrWhiteSpace(_options.CaptureFramePath))
        {
            return;
        }

        string path = Path.GetFullPath(_options.CaptureFramePath);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        int width = 0;
        int height = 0;
        byte[]? bgra = null;
        void CaptureCompletedFrame()
        {
            width = shellWindow.Window.Width;
            height = shellWindow.Window.Height;
            bgra = new byte[checked(width * height * 4)];
            shellWindow.Window.BindPresentationFramebuffer();
            shellWindow.Window.Gl.ReadPixels(
                0,
                0,
                (uint)width,
                (uint)height,
                PixelFormat.Bgra,
                PixelType.UnsignedByte,
                bgra);
        }

        if (CurrentSession is { } session)
        {
            // DXGI/WGL presenter 会在 SwapBuffers 后立即以 WRITE_DISCARD 锁定下一帧共享纹理；
            // 此时再读 framebuffer 得到的是未定义内容。与 Player/Demo 证据路径一致，
            // 只在完整 Editor UI 已绘制、交换缓冲前执行一次 readback。
            using IDisposable registration = session.Engine.Probe.RegisterBeforeSwapBuffers(CaptureCompletedFrame);
            session.RunOneTick(0);
        }
        else
        {
            DrawProjectPickerCaptureFrame(shellWindow, CaptureCompletedFrame);
        }

        if (bgra is null || width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("Editor framebuffer 未在交换缓冲前完成捕获。");
        }

        WriteBgraBottomUpBmp(path, width, height, bgra);
        Console.WriteLine($"EditorShell framebuffer 截图已写入：{path}");
    }

    private void DrawProjectPickerCaptureFrame(EditorShellWindow shellWindow, Action captureCompletedFrame)
    {
        ArgumentNullException.ThrowIfNull(captureCompletedFrame);
        shellWindow.Window.DoEvents();
        if (!shellWindow.Gui.IsRunning)
        {
            shellWindow.Gui.Initialize();
        }

        Layout.ConfigureImGui();
        shellWindow.Window.BindPresentationFramebuffer();
        shellWindow.Window.Gl.Viewport(
            0,
            0,
            (uint)shellWindow.Window.Width,
            (uint)shellWindow.Window.Height);
        shellWindow.Window.Gl.Disable(EnableCap.ScissorTest);
        shellWindow.Window.Gl.ClearColor(0.125f, 0.133f, 0.149f, 1f);
        shellWindow.Window.Gl.Clear(ClearBufferMask.ColorBufferBit);
        shellWindow.Gui.SetUiScale(Preferences.Current.UiScale);
        shellWindow.Gui.SetLayoutPersistence(Preferences.Current.SaveLayoutOnExit);
        shellWindow.Gui.DrawFrame(
            0f,
            shellWindow.Window.LogicalWidth,
            shellWindow.Window.LogicalHeight,
            _ =>
            {
                EditorMainMenuBar.DispatchShortcuts(this);
                ProjectPicker.Draw(this);
                PreferencesWindow.Draw();
            },
            shellWindow.Window.FramebufferScaleX,
            shellWindow.Window.FramebufferScaleY);
        captureCompletedFrame();
        shellWindow.Window.SwapBuffers();
    }

    private static void WriteBgraBottomUpBmp(string path, int width, int height, ReadOnlySpan<byte> bgra)
    {
        int pixelBytes = checked(width * height * 4);
        if (bgra.Length != pixelBytes)
        {
            throw new ArgumentException("BMP 像素数据尺寸与宽高不一致。", nameof(bgra));
        }

        const int fileHeaderBytes = 14;
        const int infoHeaderBytes = 40;
        int pixelOffset = fileHeaderBytes + infoHeaderBytes;
        int fileSize = checked(pixelOffset + pixelBytes);
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream);
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(fileSize);
        writer.Write(0);
        writer.Write(pixelOffset);
        writer.Write(infoHeaderBytes);
        writer.Write(width);
        writer.Write(height);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(pixelBytes);
        writer.Write(2_835);
        writer.Write(2_835);
        writer.Write(0);
        writer.Write(0);
        writer.Write(bgra);
    }
}

/// <summary>
/// 将 UI draw callback 中发出的会重入 Engine frame 的命令延迟到当前 frame 返回后执行。
/// 同一帧的重复 Step 请求合并为一次，并绑定到发出请求时的 session，避免工程切换后误作用于新 session。
/// </summary>
internal sealed class EditorDeferredFrameActions
{
    private object? _stepOnceOwner;

    public void RequestStepOnce(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _stepOnceOwner = owner;
    }

    public bool TryConsumeStepOnce(object? currentOwner)
    {
        object? requestedOwner = _stepOnceOwner;
        _stepOnceOwner = null;
        return requestedOwner is not null && ReferenceEquals(requestedOwner, currentOwner);
    }
}

/// <summary>
/// 脚本化验收探针：ScriptedPlayerRunProbeResult。
/// </summary>
internal sealed record ScriptedPlayerRunProbeResult(
    bool Started = false,
    bool Completed = false,
    int ExitCode = 0,
    bool CaptureExists = false,
    bool WindowCompleted = false,
    bool ContentLoaded = false,
    string StdoutPath = "",
    string StderrPath = "",
    string CapturePath = "",
    string Diagnostic = "");

/// <summary>
/// 脚本化验收探针：ScriptedBuildCancelProbeState。
/// </summary>
internal sealed class ScriptedBuildCancelProbeState
{
    public bool FirstStarted;
    public bool CancelRequested;
    public bool FirstCompleted;
    public DateTimeOffset FirstCompletedAt;
    public int FirstExitCode;
    public int? ChildPid;
    public bool ChildObserved;
    public bool ChildAliveAfterCancel;
    public bool RerunStarted;
    public bool RerunCompleted;
    public bool Completed;

    public string Diagnostic = string.Empty;
    public string RerunDiagnostic = string.Empty;
    public ScriptedBuildProbeSnapshot FirstSnapshot = new();
    public ScriptedBuildProbeSnapshot RerunSnapshot = new();
}

/// <summary>
/// 脚本化验收探针：ScriptedBuildSettingsProbeState。
/// </summary>
internal sealed class ScriptedBuildSettingsProbeState
{
    public bool Applied;
    public bool CloseRequested;
    public bool Reopened;
    public bool Captured;
    public bool Completed;
    public string Diagnostic = string.Empty;
    public ScriptedBuildSettingsProbeSnapshot Before = new();
    public ScriptedBuildSettingsProbeSnapshot After = new();
}

/// <summary>
/// 脚本化验收探针：ScriptedMenuLayoutProbeState。
/// </summary>
internal sealed class ScriptedMenuLayoutProbeState
{
    public bool Succeeded => Completed &&
        RequiredPanelsShown &&
        ResetRequested &&
        CreatedObject &&
        DuplicatedObject &&
        RenamedObject &&
        DeletedObject &&
        NewSceneCreated &&
        OpenedOriginalScene;

    public bool Completed;
    public bool RequiredPanelsShown;
    public bool ResetRequested;
    public bool CreatedObject;
    public bool DuplicatedObject;
    public bool RenamedObject;
    public bool DeletedObject;
    public bool NewSceneCreated;
    public bool OpenedOriginalScene;
    public int PanelCount;
    public int InitialCount;
    public int FinalCount;
    public string StartScene = string.Empty;
    public string Diagnostic = string.Empty;
}

/// <summary>
/// 脚本化验收探针：ScriptedHierarchyProbeState。
/// </summary>
internal sealed class ScriptedHierarchyProbeState
{
    public bool Completed;
    public bool Created;
    public bool ChildParented;
    public bool CycleRejected;
    public bool CyclePrevented;
    public bool Duplicated;
    public bool Renamed;
    public bool ReparentedToRoot;
    public bool SelectionLinked;
    public bool Deleted;
    public int InitialCount;
    public int FinalCount;
    public string Diagnostic = string.Empty;
}

/// <summary>
/// 脚本化验收探针：ScriptedDefaultWorkbenchProbeState。
/// </summary>
internal sealed class ScriptedDefaultWorkbenchProbeState
{
    public bool Completed;
    public bool Succeeded;
    public bool ProjectCreated;
    public string ProjectRoot = string.Empty;
    public bool RequiredPanelsShown;
    public int PanelCount;
    public bool GameObjectCreated;
    public bool ScriptSourceCreated;
    public string ScriptSourcePath = string.Empty;
    public bool ScriptHotReloadRequested;
    public bool ScriptHotReloadApplied;
    public bool BehaviourRegistered;
    public string BehaviourTypeName = string.Empty;
    public bool BehaviourAttached;
    public bool SceneSaved;
    public bool PlayEntered;
    public bool PlayExited;
    public string PlayStatus = string.Empty;
    public bool BuildSettingsShown;
    public bool BuildOutputReady;
    public string BuildOutputPath = string.Empty;
    public bool BuildStarted;
    public bool BuildCompleted;
    public bool BuildOk;
    public int BuildExitCode;
    public string BuildPackageArchive = string.Empty;
    public string BuildError = string.Empty;
    public ScriptedBuildProbeSnapshot BuildSnapshot = new();
    public string Diagnostic = string.Empty;
}

/// <summary>
/// 保持 Play 的 Game View 玩家视觉与移动验收状态。
/// </summary>
internal sealed class ScriptedGameViewProbeState
{
    public readonly Key[] MovementKeys = [Key.D];

    public bool InputRegistered;

    public bool PlayEntered;

    public bool StartCaptured;

    public bool InjectMovement;

    public bool PlayerMoved;

    public bool RemainedInPlay;

    public bool FirstPlayVerified;

    public bool FirstPlayExited;

    public bool SecondPlayEntered;

    public bool SecondPlayUiRestored;

    public bool SecondControllerFound;

    public bool SecondControllerEnabled;

    public bool SecondControllerFaulted;

    public bool Completed;

    public bool Finished;

    public float StartX;

    public float EndX;

    public int StartVisualCommands;

    public int EndVisualCommands;

    public int RenderOverlayCommands;

    public int ExitUiStackDepth;

    public int SecondUiStackDepth;

    public int SecondVisualCommands;

    public ScriptedGameViewPresentationSnapshot Presentation = ScriptedGameViewPresentationSnapshot.Missing;

    public string SecondControllerException = string.Empty;

    public string Diagnostic = string.Empty;
}

/// <summary>
/// ScriptedBuildFrameStats。
/// </summary>
internal sealed class ScriptedBuildFrameStats
{
    private double _totalSeconds;

    public int Count { get; private set; }

    public double MaxMilliseconds { get; private set; }

    public double AverageMilliseconds => Count == 0 ? 0 : _totalSeconds * 1000 / Count;

    public void Record(float deltaSeconds)
    {
        Count++;
        _totalSeconds += deltaSeconds;
        MaxMilliseconds = Math.Max(MaxMilliseconds, deltaSeconds * 1000);
    }
}
