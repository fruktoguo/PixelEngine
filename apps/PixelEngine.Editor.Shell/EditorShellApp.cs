using System.Diagnostics;
using Silk.NET.OpenGL;

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

    public string? SceneOverridePath => _options.ScenePath;

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
        bool scriptedPlayEntered = false;
        bool scriptedPlayExited = false;
        bool scriptedSceneSaved = false;
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
                if (_options.ScriptedProbe)
                {
                    RunScriptedProbeActions(
                        executed,
                        ref scriptedPlayEntered,
                        ref scriptedPlayExited,
                        ref scriptedSceneSaved);
                }

                ApplyDeferredClose();
            }

            UpdateTitle(shellWindow);
            executed++;
            if (requestedTicks > 0 && executed >= requestedTicks)
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
                $"project_open={projectOpen}, " +
                $"window_ticks={executed}");
        }

        return 0;
    }

    private void RunScriptedProbeActions(
        int executedTicks,
        ref bool playEntered,
        ref bool playExited,
        ref bool sceneSaved)
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

    public void CreatePrefabFromSelection()
    {
        CurrentSession?.CreatePrefabFromSelection();
    }

    public void InstantiatePrefab(string assetPath)
    {
        CurrentSession?.InstantiatePrefab(assetPath);
    }

    public void ShowBuildSettings()
    {
        _ = CurrentSession?.ShowBuildSettings();
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
        shellWindow.Window.Gl.ReadPixels<byte>(0, 0, (uint)width, (uint)height, PixelFormat.Bgra, PixelType.UnsignedByte, bgra);
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
