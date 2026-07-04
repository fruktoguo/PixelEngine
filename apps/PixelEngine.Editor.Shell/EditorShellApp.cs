using System.Diagnostics;

namespace PixelEngine.Editor.Shell;

internal sealed class EditorShellApp
{
    private readonly EditorShellOptions _options;
    private EditorProject? _pendingProject;
    private bool _closeProjectRequested;

    private EditorShellApp(EditorShellOptions options)
    {
        _options = options;
        ProjectPicker = new ProjectPickerWindow(options);
        MainMenu = new EditorMainMenuBar();
        Layout = new EditorShellLayout(EditorShellWindow.DefaultLayoutPath);
        RecentProjects = RecentProjectsStore.LoadDefault();
    }

    public EditorProject? CurrentProject { get; private set; }

    public bool HasOpenProject => CurrentProject is not null;

    public RecentProjectsStore RecentProjects { get; }

    public string? LastProjectError { get; private set; }

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
            return new EditorShellApp(options).Run();
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
        using EditorShellWindow shellWindow = EditorShellWindow.Create();
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
        while (!shellWindow.Window.IsClosing)
        {
            double now = stopwatch.Elapsed.TotalSeconds;
            float deltaSeconds = (float)Math.Max(0.0, now - previousSeconds);
            previousSeconds = now;
            if (CurrentSession is null)
            {
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

                shellWindow.Gui.DrawFrame(
                    deltaSeconds,
                    shellWindow.Window.Width,
                    shellWindow.Window.Height,
                    _ =>
                    {
                        MainMenu.Draw(this);
                        Layout.DrawDockSpace();
                        ProjectPicker.Draw(this);
                    });
                shellWindow.Window.SwapBuffers();
                ApplyPendingProject(shellWindow);
            }
            else
            {
                CurrentSession.RunOneTick(deltaSeconds);
                ApplyDeferredClose();
            }

            UpdateTitle(shellWindow);
            executed++;
            if (requestedTicks > 0 && executed >= requestedTicks)
            {
                break;
            }
        }

        if (requestedTicks > 0 || _options.ScriptedProbe)
        {
            Console.WriteLine(
                "editor_enabled=True, " +
                "editor_running=True, " +
                $"editor_panels={CurrentSession?.PanelCount ?? 0}, " +
                $"editor_bridge_frames={CurrentSession?.EditorBridgeFrameCount ?? executed}, " +
                "render_camera_synced=False, " +
                $"project_open={HasOpenProject}, " +
                $"window_ticks={executed}");
        }

        return 0;
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
        }
    }

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
        CurrentSession?.CreateGameObject();
    }

    public bool Undo()
    {
        return CurrentSession?.Undo() == true;
    }

    public bool Redo()
    {
        return CurrentSession?.Redo() == true;
    }

    private void ApplyPendingProject(EditorShellWindow shellWindow)
    {
        if (_pendingProject is null)
        {
            return;
        }

        EditorProject project = _pendingProject;
        _pendingProject = null;
        CurrentSession?.Dispose();
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
            CurrentProject?.ResolveDisplaySceneName(_options.ScenePath),
            dirty: false);
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
}
