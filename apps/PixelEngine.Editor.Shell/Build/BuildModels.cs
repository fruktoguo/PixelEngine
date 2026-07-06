using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using PixelEngine.Hosting;
using PixelEngine.UI;

namespace PixelEngine.Editor.Shell.Build;

internal enum BuildLogLevel
{
    Info,
    Warning,
    Error,
}

internal enum BuildEventKind
{
    Log,
    Progress,
    Result,
}

internal enum BuildPhase
{
    Unknown,
    Native,
    Publish,
    Verify,
    Package,
    Audit,
    Done,
}

internal static class BuildHostRid
{
    public static string Current
    {
        get
        {
            string os = OperatingSystem.IsWindows() ? "win" :
                OperatingSystem.IsLinux() ? "linux" :
                OperatingSystem.IsMacOS() ? "osx" :
                string.Empty;
            Architecture processArchitecture = RuntimeInformation.ProcessArchitecture;
            string architecture = processArchitecture == Architecture.X64 ? "x64" :
                processArchitecture == Architecture.Arm64 ? "arm64" :
                processArchitecture == Architecture.X86 ? "x86" :
                processArchitecture == Architecture.Arm ? "arm" :
                string.Empty;
            return string.IsNullOrEmpty(os) || string.IsNullOrEmpty(architecture)
                ? RuntimeInformation.RuntimeIdentifier
                : $"{os}-{architecture}";
        }
    }

    public static bool SupportsAot(BuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Channel != BuildProfileChannel.Aot ||
            string.Equals(request.Rid, Current, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record BuildToolLocatorResult
{
    public string RepositoryRoot { get; init; } = string.Empty;

    public string BuildPlayerPath { get; init; } = string.Empty;

    public bool BuildPlayerExists { get; init; }

    public string DotnetPath { get; init; } = "dotnet";

    public string ShellPath { get; init; } = string.Empty;

    public bool UsesPowerShell { get; init; }
}

internal sealed record BuildPreflight
{
    public bool Ok { get; init; }

    public BuildToolLocatorResult Tools { get; init; } = new();

    public string DotnetVersion { get; init; } = string.Empty;

    public string ShellVersion { get; init; } = string.Empty;

    public string Diagnostic { get; init; } = string.Empty;
}

internal static class BuildProfileEditorAdapter
{
    public static BuildProfileDto CreateDefault(EditorProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        BuildProfileDto settings = BuildProfileDto.CreateDefault(project.ResolveSceneRelativePath(null));
        settings.RefreshScenes(project);
        return settings;
    }

    public static void RefreshScenes(this BuildProfileDto settings, EditorProject project)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(project);
        Dictionary<string, BuildProfileSceneDto> merged = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < settings.Scenes.Count; i++)
        {
            BuildProfileSceneDto entry = settings.Scenes[i];
            if (!string.IsNullOrWhiteSpace(entry.Source))
            {
                merged[entry.Source] = entry;
            }
        }

        foreach (EditorProjectSceneEntry scene in project.Scenes)
        {
            AddOrUpdateScene(merged, scene.Name, scene.Path, SceneSourceKind.SceneFile);
        }

        string sceneRoot = Path.Combine(project.ContentRootPath, "scenes");
        if (Directory.Exists(sceneRoot))
        {
            foreach (string file in Directory.EnumerateFiles(sceneRoot, "*.scene", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
            {
                string relative = Path.GetRelativePath(project.ContentRootPath, file).Replace('\\', '/');
                AddOrUpdateScene(merged, Path.GetFileNameWithoutExtension(relative) ?? relative, relative, SceneSourceKind.SceneFile);
            }
        }

        settings.Scenes = [.. merged.Values.OrderBy(static entry => entry.Source, StringComparer.OrdinalIgnoreCase)];
        EnsureSingleStartup(settings, project.ResolveSceneRelativePath(null));
    }

    public static BuildRequest ToRequest(this BuildProfileDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _ = settings.Normalize();
        BuildProfileSceneDto startup = settings.Scenes.Single(static scene => scene.IsStartup);
        return new BuildRequest
        {
            Rid = settings.Rid,
            Channel = settings.Channel,
            Configuration = settings.Configuration,
            OutputDirectory = settings.OutputDirectory,
            Version = settings.Version,
            InformationalVersion = settings.InformationalVersion,
            ProductName = settings.ProductName,
            IconPath = settings.IconPath,
            IncludeSymbols = settings.IncludeSymbols,
            StartScene = startup.Source ?? startup.SceneName,
            IncludedScenes = settings.PackageWholeContent ? [] : [.. settings.Scenes.Where(static scene => scene.Included).Select(static scene => scene.Source ?? scene.SceneName)],
            RunAfterBuild = settings.RunAfterBuild,
        };
    }

    private static void EnsureSingleStartup(BuildProfileDto settings, string preferredSource)
    {
        bool hasStartup = false;
        for (int i = 0; i < settings.Scenes.Count; i++)
        {
            if (settings.Scenes[i].IsStartup)
            {
                if (!hasStartup)
                {
                    settings.Scenes[i].Included = true;
                    hasStartup = true;
                }
                else
                {
                    settings.Scenes[i].IsStartup = false;
                }
            }
        }

        if (hasStartup || settings.Scenes.Count == 0)
        {
            return;
        }

        BuildProfileSceneDto? preferred = settings.Scenes.FirstOrDefault(scene => string.Equals(scene.Source, preferredSource, StringComparison.OrdinalIgnoreCase));
        (preferred ?? settings.Scenes[0]).IsStartup = true;
        (preferred ?? settings.Scenes[0]).Included = true;
    }

    private static void AddOrUpdateScene(Dictionary<string, BuildProfileSceneDto> merged, string name, string path, SceneSourceKind sourceKind)
    {
        if (merged.TryGetValue(path, out BuildProfileSceneDto? existing))
        {
            existing.SceneName = string.IsNullOrWhiteSpace(existing.SceneName) ? name : existing.SceneName;
            existing.SourceKind = sourceKind;
            existing.Source = path;
            return;
        }

        merged[path] = new BuildProfileSceneDto
        {
            SceneName = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(path) ?? path : name,
            Included = true,
            SourceKind = sourceKind,
            Source = path,
        };
    }
}

internal sealed record BuildRequest
{
    public string Rid { get; init; } = string.Empty;

    public BuildProfileChannel Channel { get; init; }

    public string Configuration { get; init; } = string.Empty;

    public string OutputDirectory { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string InformationalVersion { get; init; } = string.Empty;

    public string ProductName { get; init; } = string.Empty;

    public string? IconPath { get; init; }

    public bool IncludeSymbols { get; init; }

    public string StartScene { get; init; } = string.Empty;

    public string[] IncludedScenes { get; init; } = [];

    public bool RunAfterBuild { get; init; }

    public int PlayerWindowWidth { get; init; } = EngineOptions.DefaultWindowWidth;

    public int PlayerWindowHeight { get; init; } = EngineOptions.DefaultWindowHeight;

    public bool PlayerVSync { get; init; } = true;

    public UiBackendKind RuntimeUiBackend { get; init; } = UiBackendKind.ManagedFallback;

    public PlayerReleaseChannel ReleaseChannel { get; init; } = PlayerReleaseChannel.Development;
}

internal sealed record BuildProgressEvent(
    BuildEventKind Kind,
    BuildPhase Phase,
    float Percent,
    BuildLogLevel Level,
    string Message,
    DateTimeOffset Timestamp);

internal sealed record BuildResult
{
    public bool Ok { get; init; }

    public string Rid { get; init; } = string.Empty;

    public string Channel { get; init; } = string.Empty;

    public string ReleaseChannel { get; init; } = string.Empty;

    public string Configuration { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string InformationalVersion { get; init; } = string.Empty;

    public string? PackageArchive { get; init; }

    public string? PackageDir { get; init; }

    public string? PlayerDir { get; init; }

    public string? LauncherExe { get; init; }

    public string? Sha256 { get; init; }

    public long SizeBytes { get; init; }

    public IReadOnlyDictionary<BuildPhase, double> PhaseTimingsMs { get; init; } = new Dictionary<BuildPhase, double>();

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public string? Error { get; init; }

    public int ExitCode { get; init; }
}

internal sealed record BuildRunView
{
    public bool IsRunning { get; init; }

    public BuildPhase Phase { get; init; } = BuildPhase.Unknown;

    public float Percent { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public BuildResult? Result { get; init; }

    public BuildPreflight? Preflight { get; init; }
}

internal sealed record ScriptedBuildProbeSnapshot
{
    public bool Started { get; init; }

    public bool IsRunning { get; init; }

    public BuildPhase Phase { get; init; } = BuildPhase.Unknown;

    public float Percent { get; init; }

    public BuildResult? Result { get; init; }

    public int LogCount { get; init; }
}

internal sealed record ScriptedBuildSettingsProbeSnapshot
{
    public string Rid { get; init; } = string.Empty;

    public BuildProfileChannel Channel { get; init; }

    public string Configuration { get; init; } = string.Empty;

    public string OutputDirectory { get; init; } = string.Empty;

    public string ProductName { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string InformationalVersion { get; init; } = string.Empty;

    public bool IncludeSymbols { get; init; }

    public bool PackageWholeContent { get; init; }

    public bool RunAfterBuild { get; init; }

    public int IncludedSceneCount { get; init; }

    public string StartupScene { get; init; } = string.Empty;
}

internal sealed class BuildLog(int capacity = 512)
{
    private readonly BuildProgressEvent[] _events = new BuildProgressEvent[Math.Max(8, capacity)];
    private int _next;
    public int Count { get; private set; }

    public void Add(BuildProgressEvent item)
    {
        _events[_next] = item;
        _next = (_next + 1) % _events.Length;
        Count = Math.Min(Count + 1, _events.Length);
    }

    public BuildProgressEvent GetFromOldest(int index)
    {
        if ((uint)index >= (uint)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        int start = (_next - Count + _events.Length) % _events.Length;
        return _events[(start + index) % _events.Length];
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true)]
[JsonSerializable(typeof(BuildResult))]
[JsonSerializable(typeof(Dictionary<BuildPhase, double>))]
internal sealed partial class PixelEngineEditorShellBuildJsonContext : JsonSerializerContext
{
}
