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

        string json = File.ReadAllText(fullPath);
        RecentProjectsDocument? document = JsonSerializer.Deserialize(
            json,
            EditorShellJsonContext.Default.RecentProjectsDocument);
        List<RecentProjectEntry> entries = document?.Entries is null
            ? []
            : [.. document.Entries.Where(static entry => !string.IsNullOrWhiteSpace(entry.ProjectPath))];
        entries.Sort(static (left, right) => right.LastOpenedUtc.CompareTo(left.LastOpenedUtc));
        if (entries.Count > MaxEntries)
        {
            entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
        }

        return new RecentProjectsStore(fullPath, entries);
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
