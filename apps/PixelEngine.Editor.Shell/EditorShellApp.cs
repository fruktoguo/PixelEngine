using System.Diagnostics;

namespace PixelEngine.Editor.Shell;

internal sealed class EditorShellApp
{
    private readonly EditorShellOptions _options;
    private readonly ProjectPickerWindow _projectPicker = new();

    private EditorShellApp(EditorShellOptions options)
    {
        _options = options;
    }

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
        shellWindow.SetTitle(ProjectName(_options.ProjectPath), SceneName(_options.ScenePath), dirty: false);

        Stopwatch stopwatch = Stopwatch.StartNew();
        double previousSeconds = stopwatch.Elapsed.TotalSeconds;
        int executed = 0;
        int requestedTicks = _options.WindowTicks;
        while (!shellWindow.Window.IsClosing)
        {
            shellWindow.Window.DoEvents();
            double now = stopwatch.Elapsed.TotalSeconds;
            float deltaSeconds = (float)Math.Max(0.0, now - previousSeconds);
            previousSeconds = now;
            if (!shellWindow.Gui.IsRunning)
            {
                shellWindow.Gui.Initialize();
            }

            shellWindow.Gui.DrawFrame(
                deltaSeconds,
                shellWindow.Window.Width,
                shellWindow.Window.Height,
                _ => _projectPicker.Draw(_options));
            shellWindow.Window.SwapBuffers();
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
                "editor_panels=0, " +
                $"editor_bridge_frames={executed}, " +
                "render_camera_synced=False, " +
                $"window_ticks={executed}");
        }

        return 0;
    }

    private static string ProjectName(string? projectPath)
    {
        return string.IsNullOrWhiteSpace(projectPath)
            ? "No Project"
            : Path.GetFileName(Path.GetFullPath(projectPath));
    }

    private static string SceneName(string? scenePath)
    {
        return string.IsNullOrWhiteSpace(scenePath)
            ? "No Scene"
            : Path.GetFileName(scenePath);
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
