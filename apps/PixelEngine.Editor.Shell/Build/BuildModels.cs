using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;
using PixelEngine.Hosting;

namespace PixelEngine.Editor.Shell.Build;

internal enum BuildChannel
{
    R2R,
    Aot,
}

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
        return request.Channel != BuildChannel.Aot ||
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

internal sealed record SceneBuildEntry
{
    public string SceneName { get; set; } = string.Empty;

    public bool Included { get; set; } = true;

    public bool IsStartup { get; set; }

    public SceneSourceKind SourceKind { get; set; } = SceneSourceKind.SceneFile;

    public string? Source { get; set; }
}

internal sealed record BuildTargetSettings
{
    public string Rid { get; set; } = "win-x64";

    public BuildChannel Channel { get; set; } = BuildChannel.R2R;

    public string Configuration { get; set; } = "Release";

    public string OutputDirectory { get; set; } = "artifacts/player";

    public string ProductName { get; set; } = "PixelEngine Demo";

    public string Version { get; set; } = "0.1.0";

    public string InformationalVersion { get; set; } = string.Empty;

    public string? IconPath { get; set; }

    public bool IncludeSymbols { get; set; }

    public bool PackageWholeContent { get; set; } = true;

    public bool RunAfterBuild { get; set; }

    public List<SceneBuildEntry> Scenes { get; set; } = [];

    public static BuildTargetSettings CreateDefault(EditorProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        BuildTargetSettings settings = new();
        settings.RefreshScenes(project);
        return settings;
    }

    public void RefreshScenes(EditorProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        Dictionary<string, SceneBuildEntry> merged = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Scenes.Count; i++)
        {
            SceneBuildEntry entry = Scenes[i];
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

        Scenes = [.. merged.Values.OrderBy(static entry => entry.Source, StringComparer.OrdinalIgnoreCase)];
        EnsureSingleStartup(project.ResolveSceneRelativePath(null));
    }

    public bool TryNormalize(out string error)
    {
        if (string.IsNullOrWhiteSpace(Rid))
        {
            error = "RID 不能为空。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Configuration))
        {
            error = "Configuration 不能为空。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            error = "输出目录不能为空。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(ProductName))
        {
            error = "产物名不能为空。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Version))
        {
            error = "版本号不能为空。";
            return false;
        }

        int included = 0;
        int startup = 0;
        for (int i = 0; i < Scenes.Count; i++)
        {
            if (Scenes[i].Included)
            {
                included++;
            }

            if (Scenes[i].IsStartup)
            {
                startup++;
                if (!Scenes[i].Included)
                {
                    error = "启动场景必须入包。";
                    return false;
                }
            }
        }

        if (included == 0)
        {
            error = "至少需要一个入包场景。";
            return false;
        }

        if (startup != 1)
        {
            error = "必须且只能选择一个启动场景。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public BuildRequest ToRequest()
    {
        _ = TryNormalize(out string error) ? true : throw new InvalidOperationException(error);
        SceneBuildEntry startup = Scenes.Single(static scene => scene.IsStartup);
        return new BuildRequest
        {
            Rid = Rid,
            Channel = Channel,
            Configuration = Configuration,
            OutputDirectory = OutputDirectory,
            Version = Version,
            InformationalVersion = InformationalVersion,
            ProductName = ProductName,
            IconPath = IconPath,
            IncludeSymbols = IncludeSymbols,
            StartScene = startup.Source ?? startup.SceneName,
            IncludedScenes = PackageWholeContent ? [] : [.. Scenes.Where(static scene => scene.Included).Select(static scene => scene.Source ?? scene.SceneName)],
            RunAfterBuild = RunAfterBuild,
        };
    }

    private void EnsureSingleStartup(string preferredSource)
    {
        bool hasStartup = false;
        for (int i = 0; i < Scenes.Count; i++)
        {
            if (Scenes[i].IsStartup)
            {
                if (!hasStartup)
                {
                    Scenes[i].Included = true;
                    hasStartup = true;
                }
                else
                {
                    Scenes[i].IsStartup = false;
                }
            }
        }

        if (hasStartup || Scenes.Count == 0)
        {
            return;
        }

        SceneBuildEntry? preferred = Scenes.FirstOrDefault(scene => string.Equals(scene.Source, preferredSource, StringComparison.OrdinalIgnoreCase));
        (preferred ?? Scenes[0]).IsStartup = true;
        (preferred ?? Scenes[0]).Included = true;
    }

    private static void AddOrUpdateScene(Dictionary<string, SceneBuildEntry> merged, string name, string path, SceneSourceKind sourceKind)
    {
        if (merged.TryGetValue(path, out SceneBuildEntry? existing))
        {
            existing.SceneName = string.IsNullOrWhiteSpace(existing.SceneName) ? name : existing.SceneName;
            existing.SourceKind = sourceKind;
            existing.Source = path;
            return;
        }

        merged[path] = new SceneBuildEntry
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

    public BuildChannel Channel { get; init; }

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
[JsonSerializable(typeof(BuildTargetSettings))]
[JsonSerializable(typeof(BuildResult))]
[JsonSerializable(typeof(Dictionary<BuildPhase, double>))]
internal sealed partial class PixelEngineEditorShellBuildJsonContext : JsonSerializerContext
{
}
