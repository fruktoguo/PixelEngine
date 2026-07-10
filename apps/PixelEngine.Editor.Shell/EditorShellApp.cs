using System.Diagnostics;
using PixelEngine.Editor.Shell.Build;
using PixelEngine.Editor.Shell.Settings;
using PixelEngine.Hosting;
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
    private readonly EditorUiScaleContextState _projectPickerUiScaleState = new();
    private EditorProject? _pendingProject;
    private bool _closeProjectRequested;
    private bool _exitRequested;

    private EditorShellApp(EditorShellOptions options, EditorPreferencesStore preferences)
    {
        _options = options;
        Preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        ProjectPicker = new ProjectPickerWindow(options);
        MainMenu = new EditorMainMenuBar();
        Layout = new EditorShellLayout(EditorShellWindow.DefaultLayoutPath);
        PreferencesWindow = new EditorPreferencesWindow(Preferences, ResetLayout);
        RecentProjects = RecentProjectsStore.LoadDefault();
    }

    internal static EditorShellApp CreateForTests(EditorPreferencesStore? preferences = null)
    {
        return new EditorShellApp(new EditorShellOptions(
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
            LogDirectory: null),
            preferences ?? EditorPreferencesStore.CreateInMemory());
    }

    public EditorConsoleStore ConsoleStore { get; } = new();

    public EditorPreferencesStore Preferences { get; }

    public EditorPreferencesWindow PreferencesWindow { get; }

    public float UiScale => Preferences.Current.UiScale;

    public EditorProject? CurrentProject { get; private set; }

    public bool HasOpenProject => CurrentProject is not null;

    public string? SceneOverridePath => _options.ScenePath;

    public RecentProjectsStore RecentProjects { get; }

    public string? LastProjectError { get; private set; }

    public string? LastAssetOpenDiagnostic { get; private set; }

    public EditorProjectSession? CurrentSession { get; private set; }

    private ProjectPickerWindow ProjectPicker { get; }

    private EditorMainMenuBar MainMenu { get; }

    private EditorShellLayout Layout { get; }

    public static int Execute(string[] args)
    {
        EditorShellOptions? options = null;
        try
        {
            options = EditorShellOptions.Parse(args);
            return new EditorShellApp(options, EditorPreferencesStore.LoadDefault()).Run();
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
        using EditorShellWindow shellWindow = EditorShellWindow.Create(Preferences.Current.UiScale);
        if (_options.ScriptedPreferencesProbe)
        {
            ShowPreferences(EditorPreferencesCategory.Appearance);
        }

        if (!string.IsNullOrWhiteSpace(_options.ProjectPath))
        {
            OpenProjectPath(_options.ProjectPath);
            ApplyPendingProject(shellWindow);
        }

        UpdateTitle(shellWindow);

        Stopwatch stopwatch = Stopwatch.StartNew();
        double previousSeconds = stopwatch.Elapsed.TotalSeconds;
        int executed = 0;
        int requestedTicks = _options.WindowTicks;
        bool configuredImGui = false;
        bool scriptedPlayEntered = false;
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
        ScriptedPlayerRunProbeResult scriptedPlayerRun = new();
        // 主循环：无项目时显示 ProjectPicker；有项目时由 Session 驱动 Engine tick
        while (!shellWindow.Window.IsClosing && !_exitRequested)
        {
            double now = stopwatch.Elapsed.TotalSeconds;
            float deltaSeconds = (float)Math.Max(0.0, now - previousSeconds);
            previousSeconds = now;
            if (CurrentSession is null)
            {
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
                    _projectPickerUiScaleState.Reset();
                    shellWindow.Gui.Initialize();
                }

                if (!configuredImGui)
                {
                    Layout.ConfigureImGui();
                    configuredImGui = true;
                }

                shellWindow.Gui.DrawFrame(
                    deltaSeconds,
                    shellWindow.Window.LogicalWidth,
                    shellWindow.Window.LogicalHeight,
                    _ =>
                    {
                        ApplyCurrentUiPreferences(_projectPickerUiScaleState, shellWindow.Gui.Options.DpiScale);
                        shellWindow.Gui.SetLayoutPersistence(Preferences.Current.SaveLayoutOnExit);
                        MainMenu.Draw(this);
                        Layout.DrawDockSpace();
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
                if (_options.ScriptedProbe)
                {
                    RunScriptedProbeActions(
                        executed,
                        ref scriptedPlayEntered,
                        ref scriptedPlayExited,
                        ref scriptedSceneSaved,
                        ref scriptedProjectClosed);
                }

                ApplyDeferredClose();
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
                $"preferences_path={Preferences.StoragePath}, " +
                $"window_pos={PreferencesWindow.LastWindowPosition.X:F0},{PreferencesWindow.LastWindowPosition.Y:F0}, " +
                $"window_size={PreferencesWindow.LastWindowSize.X:F0}x{PreferencesWindow.LastWindowSize.Y:F0}, " +
                $"navigation_visible={PreferencesWindow.LastNavigationVisible}");
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

        CurrentSession?.Dispose();
        CurrentSession = null;
        return 0;
    }

    private void RunScriptedMenuLayoutProbeActions(ScriptedMenuLayoutProbeState state)
    {
        if (CurrentSession is null || state.Completed)
        {
            return;
        }

        try
        {
            state.StartScene = CurrentSession.CurrentSceneRelativePath;
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
            state.CreatedObject = CurrentSession.SceneModel.SelectedStableId.HasValue;
            DuplicateSelectedGameObject();
            state.DuplicatedObject = CurrentSession.SceneModel.Count >= 2;
            RenameSelectedGameObject();
            state.RenamedObject = CurrentSession.SceneModel.SelectedStableId is { } renamedId &&
                CurrentSession.SceneModel.Get(renamedId).Name.EndsWith(" Renamed", StringComparison.Ordinal);
            DeleteSelectedGameObject();
            state.DeletedObject = CurrentSession.SceneModel.Count == 1;
            string newScene = CurrentSession.NewSceneAuto();
            state.NewSceneCreated = File.Exists(CurrentSession.Project.ResolveSceneFullPath(newScene));
            CurrentSession.OpenScene(state.StartScene);
            state.OpenedOriginalScene = string.Equals(CurrentSession.CurrentSceneRelativePath, state.StartScene, StringComparison.OrdinalIgnoreCase);
            state.RequiredPanelsShown = shown == panelTitles.Length;
            state.PanelCount = CurrentSession.PanelCount;
            state.ResetRequested = true;
            state.Completed = true;
            state.Diagnostic = "菜单与布局探针完成。";
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
            $"required_panels_shown={state.RequiredPanelsShown}, " +
            $"reset_requested={state.ResetRequested}, " +
            $"created_object={state.CreatedObject}, " +
            $"duplicated_object={state.DuplicatedObject}, " +
            $"renamed_object={state.RenamedObject}, " +
            $"deleted_object={state.DeletedObject}, " +
            $"new_scene_created={state.NewSceneCreated}, " +
            $"opened_original_scene={state.OpenedOriginalScene}, " +
            $"panel_count={state.PanelCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
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

    private void RunScriptedProbeActions(
        int executedTicks,
        ref bool playEntered,
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
        try
        {
            OpenProject(EditorProject.CreateNew(projectRoot, name));
        }
        catch (Exception exception)
        {
            LastProjectError = exception.Message;
            ConsoleStore.AddProjectError("project", exception.Message);
        }
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
        LastProjectError = null;
        RecentProjects.AddOrUpdate(project);
        RecentProjects.Save();
        _pendingProject = project;
    }

    public void CloseProject()
    {
        if (CurrentSession is null)
        {
            CurrentProject = null;
            _pendingProject = null;
            return;
        }

        _closeProjectRequested = true;
    }

    public void FocusProjectPicker(ProjectPickerMode mode)
    {
        ProjectPicker.Focus(mode);
    }

    public void ResetLayout()
    {
        Layout.ResetLayout();
        CurrentSession?.ResetLayout();
    }

    public void EnterPlayMode()
    {
        CurrentSession?.EnterPlayMode();
    }

    public void EnterEditMode()
    {
        CurrentSession?.EnterEditMode();
    }

    public void StepOnce()
    {
        CurrentSession?.StepOnce();
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
        CurrentSession?.InstantiatePrefab(assetPath);
    }

    public bool OpenScriptAsset(string assetPath, out string diagnostic)
    {
        if (CurrentSession is null)
        {
            diagnostic = "当前没有打开的工程，无法打开脚本资产。";
            LastAssetOpenDiagnostic = diagnostic;
            return false;
        }

        EditorScriptAssetOpenResult result = CurrentSession.OpenScriptAsset(assetPath);
        diagnostic = result.Diagnostic;
        LastAssetOpenDiagnostic = diagnostic;
        ConsoleStore.AddAssetOpenResult(result);
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

        CurrentSession.SaveScene();
        return true;
    }

    public bool SaveSceneAs()
    {
        if (CurrentSession is null)
        {
            return false;
        }

        _ = CurrentSession.SaveSceneAsAuto();
        return true;
    }

    public bool NewScene()
    {
        if (CurrentSession is null)
        {
            return false;
        }

        _ = CurrentSession.NewSceneAuto();
        return true;
    }

    public bool OpenScene(string sceneRelativePath)
    {
        if (CurrentSession is null)
        {
            return false;
        }

        CurrentSession.OpenScene(sceneRelativePath);
        return true;
    }

    public void RequestExit()
    {
        _exitRequested = true;
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
        ProjectSettingsDto legacySettings = EngineProjectSettingsStore.LoadProjectSettings(project.ProjectRoot);
        if (!Preferences.TryMigrateLegacy(legacySettings.EditorPreferences, out string migrationDiagnostic))
        {
            ConsoleStore.AddProjectError("preferences", migrationDiagnostic);
        }

        CurrentSession?.Dispose();
        shellWindow.ShutdownProjectPickerGui();
        CurrentSession = EditorProjectSession.Open(project, shellWindow.Window, this);
        CurrentProject = project;
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
        _closeProjectRequested = false;
    }

    private void UpdateTitle(EditorShellWindow shellWindow)
    {
        shellWindow.SetTitle(
            CurrentProject?.Name,
            CurrentSession?.CurrentSceneDisplayName ?? CurrentProject?.ResolveDisplaySceneName(_options.ScenePath),
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

        int width = shellWindow.Window.Width;
        int height = shellWindow.Window.Height;
        byte[] bgra = new byte[checked(width * height * 4)];
        shellWindow.Window.Gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        shellWindow.Window.Gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Bgra, PixelType.UnsignedByte, bgra);
        WriteBgraBottomUpBmp(path, width, height, bgra);
        Console.WriteLine($"EditorShell framebuffer 截图已写入：{path}");
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
