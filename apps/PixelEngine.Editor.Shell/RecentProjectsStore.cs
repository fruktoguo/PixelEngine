using System.Text.Json;

namespace PixelEngine.Editor.Shell;

internal sealed class RecentProjectsStore
{
    public const int MaxEntries = 20;

    private readonly string _path;
    private readonly List<RecentProjectEntry> _entries;

    private RecentProjectsStore(string path, List<RecentProjectEntry> entries)
    {
        _path = path;
        _entries = entries;
    }

    public IReadOnlyList<RecentProjectEntry> Entries => _entries;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PixelEngine",
        "recent-projects.json");

    public static RecentProjectsStore LoadDefault()
    {
        return Load(DefaultPath);
    }

    public static RecentProjectsStore Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return new RecentProjectsStore(fullPath, []);
        }

        RecentProjectsDocument? document;
        try
        {
            string json = File.ReadAllText(fullPath);
            document = JsonSerializer.Deserialize(
                json,
                EditorShellJsonContext.Default.RecentProjectsDocument);
        }
        catch (JsonException)
        {
            return new RecentProjectsStore(fullPath, []);
        }
        catch (IOException)
        {
            return new RecentProjectsStore(fullPath, []);
        }
        catch (UnauthorizedAccessException)
        {
            return new RecentProjectsStore(fullPath, []);
        }

        return new RecentProjectsStore(fullPath, NormalizeEntries(document?.Entries));
    }

    private static List<RecentProjectEntry> NormalizeEntries(IEnumerable<RecentProjectEntry?>? source)
    {
        if (source is null)
        {
            return [];
        }

        List<RecentProjectEntry> sorted = [];
        foreach (RecentProjectEntry? entry in source)
        {
            if (entry is not null)
            {
                sorted.Add(entry);
            }
        }

        sorted.Sort(static (left, right) => right.LastOpenedUtc.CompareTo(left.LastOpenedUtc));

        HashSet<string> seenPaths = new(StringComparer.OrdinalIgnoreCase);
        List<RecentProjectEntry> entries = [];
        foreach (RecentProjectEntry entry in sorted)
        {
            if (!TryGetFullPath(entry.ProjectPath, out string? projectPath) || projectPath is null || !seenPaths.Add(projectPath))
            {
                continue;
            }

            string name = string.IsNullOrWhiteSpace(entry.Name)
                ? Path.GetFileName(projectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : entry.Name.Trim();
            entries.Add(new RecentProjectEntry
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Project" : name,
                ProjectPath = projectPath,
                LastOpenedUtc = entry.LastOpenedUtc,
            });
            if (entries.Count == MaxEntries)
            {
                break;
            }
        }

        return entries;
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
            fullPath = Path.GetFullPath(path);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }
    }

    public void AddOrUpdate(EditorProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        string projectPath = Path.GetFullPath(project.ProjectRoot);
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (string.Equals(Path.GetFullPath(_entries[i].ProjectPath), projectPath, StringComparison.OrdinalIgnoreCase))
            {
                _entries.RemoveAt(i);
            }
        }

        _entries.Insert(
            0,
            new RecentProjectEntry
            {
                Name = project.Name,
                ProjectPath = projectPath,
                LastOpenedUtc = DateTimeOffset.UtcNow,
            });
        if (_entries.Count > MaxEntries)
        {
            _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
        }
    }

    public void Save()
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        RecentProjectsDocument document = new()
        {
            Entries = [.. _entries],
        };
        string json = JsonSerializer.Serialize(
            document,
            EditorShellJsonContext.Default.RecentProjectsDocument);
        File.WriteAllText(_path, json);
    }
}

internal sealed class RecentProjectsDocument
{
    public RecentProjectEntry[]? Entries { get; init; }
}

internal sealed class RecentProjectEntry
{
    public string Name { get; init; } = string.Empty;

    public string ProjectPath { get; init; } = string.Empty;

    public DateTimeOffset LastOpenedUtc { get; init; }
}
