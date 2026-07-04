using System.Diagnostics;
using PixelEngine.Editor.Shell.Build;
using Silk.NET.OpenGL;

namespace PixelEngine.Editor.Shell;

internal sealed class EditorShellApp
{
    private static readonly TimeSpan ScriptedBuildProbeTimeout = TimeSpan.FromMinutes(10);
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
        bool scriptedProjectClosed = false;
        bool scriptedProjectReopened = false;
        bool scriptedBuildStarted = false;
        bool scriptedBuildCompleted = false;
        bool scriptedBuildTimedOut = false;
        string scriptedBuildDiagnostic = string.Empty;
        ScriptedBuildProbeSnapshot scriptedBuildSnapshot = new();
        ScriptedPlayerRunProbeResult scriptedPlayerRun = new();
        while (!shellWindow.Window.IsClosing)
        {
            double now = stopwatch.Elapsed.TotalSeconds;
            float deltaSeconds = (float)Math.Max(0.0, now - previousSeconds);
            previousSeconds = now;
            if (CurrentSession is null)
            {
                if (_options.ScriptedProbe &&
                    scriptedProjectClosed &&
                    !scriptedProjectReopened &&
                    !string.IsNullOrWhiteSpace(_options.ProjectPath))
                {
                    OpenProjectPath(_options.ProjectPath);
                    scriptedProjectReopened = true;
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
                        ref scriptedSceneSaved,
                        ref scriptedProjectClosed);
                }

                ApplyDeferredClose();
                if (_options.ScriptedBuildProbe)
                {
                    RunScriptedBuildProbeActions(
                        ref scriptedBuildStarted,
                        ref scriptedBuildCompleted,
                        ref scriptedBuildDiagnostic,
                        ref scriptedBuildSnapshot);
                }
            }

            UpdateTitle(shellWindow);
            executed++;
            if (_options.ScriptedBuildProbe && scriptedBuildCompleted)
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

        if (_options.ScriptedBuildProbe)
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
                scriptedBuildSnapshot);
            if (_options.ScriptedBuildRunProbe)
            {
                WriteScriptedPlayerRunProbeSummary(scriptedPlayerRun);
            }
        }

        return 0;
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
        ScriptedBuildProbeSnapshot snapshot)
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
