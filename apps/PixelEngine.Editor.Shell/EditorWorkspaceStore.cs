using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 用户级 Editor workspace 文档；不得写入工程目录或参与玩家包构建。
/// </summary>
internal sealed record EditorWorkspaceDocument
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    public bool LastCleanShutdown { get; init; } = true;

    public string? LastSuccessfulProjectPath { get; init; }

    public EditorWorkspaceWindowState? Window { get; init; } = new();

    public EditorProjectWorkspaceState[]? Projects { get; init; } = [];
}

/// <summary>
/// Editor 顶层窗口尺寸。窗口位置与显示器拓扑由后续窗口接线切片扩展。
/// </summary>
internal sealed record EditorWorkspaceWindowState
{
    public const int DefaultWidth = 1280;
    public const int DefaultHeight = 720;

    public int Width { get; init; } = DefaultWidth;

    public int Height { get; init; } = DefaultHeight;
}

/// <summary>
/// 单个工程的用户级编辑会话状态。
/// </summary>
internal sealed record EditorProjectWorkspaceState
{
    public string ProjectPath { get; init; } = string.Empty;

    public string LastScenePath { get; init; } = string.Empty;

    public DateTimeOffset LastOpenedUtc { get; init; }
}

/// <summary>
/// 版本化、损坏容错并原子写入的 Editor workspace 存储。
/// </summary>
internal sealed class EditorWorkspaceStore
{
    public const int MaxProjectWorkspaces = 50;
    private const int MaximumWindowDimension = 32768;

    private EditorWorkspaceStore(
        string? storagePath,
        EditorWorkspaceDocument current,
        bool loadedFromDisk,
        string diagnostic)
    {
        StoragePath = storagePath;
        Current = current;
        LoadedFromDisk = loadedFromDisk;
        LastDiagnostic = diagnostic;
    }

    public EditorWorkspaceDocument Current { get; private set; }

    public string? StoragePath { get; }

    public bool LoadedFromDisk { get; private set; }

    public string LastDiagnostic { get; private set; }

    public static EditorWorkspaceStore LoadDefault(EditorUserDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Load(paths.WorkspacePath);
    }

    public static EditorWorkspaceStore CreateInMemory(EditorWorkspaceDocument? initial = null)
    {
        EditorWorkspaceDocument candidate = initial ?? new EditorWorkspaceDocument();
        return TryNormalize(candidate, out EditorWorkspaceDocument normalized, out string diagnostic)
            ? new EditorWorkspaceStore(null, normalized, loadedFromDisk: false, diagnostic)
            : new EditorWorkspaceStore(null, new EditorWorkspaceDocument(), loadedFromDisk: false, diagnostic);
    }

    public static EditorWorkspaceStore Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return new EditorWorkspaceStore(fullPath, new EditorWorkspaceDocument(), loadedFromDisk: false, string.Empty);
        }

        try
        {
            string json = File.ReadAllText(fullPath);
            EditorWorkspaceDocument? document = JsonSerializer.Deserialize(
                json,
                EditorWorkspaceJsonContext.Default.EditorWorkspaceDocument);
            return document is null
                ? new EditorWorkspaceStore(
                    fullPath,
                    new EditorWorkspaceDocument(),
                    loadedFromDisk: false,
                    "Editor workspace 文件为空。")
                : TryNormalize(document, out EditorWorkspaceDocument normalized, out string diagnostic)
                    ? new EditorWorkspaceStore(fullPath, normalized, loadedFromDisk: true, diagnostic)
                    : new EditorWorkspaceStore(fullPath, new EditorWorkspaceDocument(), loadedFromDisk: false, diagnostic);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new EditorWorkspaceStore(
                fullPath,
                new EditorWorkspaceDocument(),
                loadedFromDisk: false,
                $"读取 Editor workspace 失败：{exception.Message}");
        }
    }

    public bool TryUpdate(EditorWorkspaceDocument next, out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (!TryNormalize(next, out EditorWorkspaceDocument normalized, out diagnostic))
        {
            LastDiagnostic = diagnostic;
            return false;
        }

        string normalizationDiagnostic = diagnostic;
        if (!TryWrite(normalized, out string writeDiagnostic))
        {
            diagnostic = writeDiagnostic;
            LastDiagnostic = diagnostic;
            return false;
        }

        Current = normalized;
        LoadedFromDisk = StoragePath is not null;
        diagnostic = normalizationDiagnostic;
        LastDiagnostic = normalizationDiagnostic;
        return true;
    }

    public bool TryGetProject(
        string projectPath,
        [NotNullWhen(true)] out EditorProjectWorkspaceState? project)
    {
        project = null;
        if (!TryGetFullPath(projectPath, out string? normalizedPath) || normalizedPath is null)
        {
            return false;
        }

        EditorProjectWorkspaceState[] projects = Current.Projects ?? [];
        for (int i = 0; i < projects.Length; i++)
        {
            if (string.Equals(projects[i].ProjectPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                project = projects[i];
                return true;
            }
        }

        return false;
    }

    public string? ResolveLastScene(string projectPath)
    {
        return TryGetProject(projectPath, out EditorProjectWorkspaceState? project) &&
            !string.IsNullOrWhiteSpace(project.LastScenePath)
                ? project.LastScenePath
                : null;
    }

    public bool TryRecordProjectOpened(
        string projectPath,
        string? lastScenePath,
        DateTimeOffset openedUtc,
        out string diagnostic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        EditorProjectWorkspaceState entry = new()
        {
            ProjectPath = projectPath,
            LastScenePath = lastScenePath ?? string.Empty,
            LastOpenedUtc = openedUtc,
        };
        return TryUpdate(
            Current with
            {
                LastSuccessfulProjectPath = projectPath,
                Projects = [entry, .. Current.Projects ?? []],
            },
            out diagnostic);
    }

    public bool TrySetShutdownState(bool cleanShutdown, out string diagnostic)
    {
        return TryUpdate(Current with { LastCleanShutdown = cleanShutdown }, out diagnostic);
    }

    public bool TrySetWindowSize(int width, int height, out string diagnostic)
    {
        return TryUpdate(
            Current with
            {
                Window = new EditorWorkspaceWindowState
                {
                    Width = width,
                    Height = height,
                },
            },
            out diagnostic);
    }

    private static bool TryNormalize(
        EditorWorkspaceDocument document,
        out EditorWorkspaceDocument normalized,
        out string diagnostic)
    {
        if (document.FormatVersion != EditorWorkspaceDocument.CurrentFormatVersion)
        {
            normalized = new EditorWorkspaceDocument();
            diagnostic = $"不支持的 Editor workspace 版本：{document.FormatVersion}。";
            return false;
        }

        List<string> warnings = [];
        string? lastSuccessfulProjectPath = null;
        if (!string.IsNullOrWhiteSpace(document.LastSuccessfulProjectPath))
        {
            if (!TryGetFullPath(document.LastSuccessfulProjectPath, out lastSuccessfulProjectPath))
            {
                warnings.Add("已忽略无效的 lastSuccessfulProjectPath。");
            }
        }

        Dictionary<string, EditorProjectWorkspaceState> projectsByPath = new(StringComparer.OrdinalIgnoreCase);
        EditorProjectWorkspaceState[] sourceProjects = document.Projects ?? [];
        int invalidProjectCount = 0;
        int invalidSceneCount = 0;
        for (int i = 0; i < sourceProjects.Length; i++)
        {
            EditorProjectWorkspaceState? source = sourceProjects[i];
            if (source is null || !TryGetFullPath(source.ProjectPath, out string? projectPath) || projectPath is null)
            {
                invalidProjectCount++;
                continue;
            }

            string scenePath = string.Empty;
            if (!string.IsNullOrWhiteSpace(source.LastScenePath) &&
                !TryNormalizeScenePath(source.LastScenePath, out scenePath))
            {
                invalidSceneCount++;
                scenePath = string.Empty;
            }

            EditorProjectWorkspaceState candidate = new()
            {
                ProjectPath = projectPath,
                LastScenePath = scenePath,
                LastOpenedUtc = source.LastOpenedUtc,
            };
            if (!projectsByPath.TryGetValue(projectPath, out EditorProjectWorkspaceState? existing) ||
                candidate.LastOpenedUtc > existing.LastOpenedUtc)
            {
                projectsByPath[projectPath] = candidate;
            }
        }

        if (invalidProjectCount != 0)
        {
            warnings.Add($"已忽略 {invalidProjectCount} 条无效工程 workspace。");
        }

        if (invalidSceneCount != 0)
        {
            warnings.Add($"已清空 {invalidSceneCount} 条无效 lastScenePath。");
        }

        List<EditorProjectWorkspaceState> projects = [.. projectsByPath.Values];
        projects.Sort(static (left, right) => right.LastOpenedUtc.CompareTo(left.LastOpenedUtc));
        if (projects.Count > MaxProjectWorkspaces)
        {
            projects.RemoveRange(MaxProjectWorkspaces, projects.Count - MaxProjectWorkspaces);
        }

        EditorWorkspaceWindowState window = document.Window ?? new EditorWorkspaceWindowState();
        normalized = new EditorWorkspaceDocument
        {
            FormatVersion = EditorWorkspaceDocument.CurrentFormatVersion,
            LastCleanShutdown = document.LastCleanShutdown,
            LastSuccessfulProjectPath = lastSuccessfulProjectPath,
            Window = new EditorWorkspaceWindowState
            {
                Width = NormalizeWindowDimension(window.Width, EditorWorkspaceWindowState.DefaultWidth),
                Height = NormalizeWindowDimension(window.Height, EditorWorkspaceWindowState.DefaultHeight),
            },
            Projects = [.. projects],
        };
        diagnostic = string.Join(' ', warnings);
        return true;
    }

    private static int NormalizeWindowDimension(int value, int fallback)
    {
        return value is > 0 and <= MaximumWindowDimension ? value : fallback;
    }

    private static bool TryGetFullPath(string? path, out string? fullPath)
    {
        fullPath = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(path.Trim());
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool TryNormalizeScenePath(string path, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        try
        {
            string candidate = path.Trim().Replace('\\', '/');
            if (Path.IsPathRooted(candidate) || candidate.StartsWith('/'))
            {
                return false;
            }

            string[] parts = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
            List<string> normalizedParts = new(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0 || part == ".")
                {
                    continue;
                }

                if (part == "..")
                {
                    return false;
                }

                normalizedParts.Add(part);
            }

            normalized = string.Join('/', normalizedParts);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private bool TryWrite(EditorWorkspaceDocument document, out string diagnostic)
    {
        if (StoragePath is null)
        {
            diagnostic = string.Empty;
            return true;
        }

        try
        {
            string json = JsonSerializer.Serialize(
                document,
                EditorWorkspaceJsonContext.Default.EditorWorkspaceDocument);
            EditorAtomicTextFile.WriteAllText(StoragePath, json);
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostic = $"保存 Editor workspace 失败：{exception.Message}";
            return false;
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true)]
[JsonSerializable(typeof(EditorWorkspaceDocument))]
internal sealed partial class EditorWorkspaceJsonContext : JsonSerializerContext
{
}
