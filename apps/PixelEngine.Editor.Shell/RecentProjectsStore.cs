using System.Text.Json;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 最近打开项目列表的持久化存储。
/// </summary>
internal sealed class RecentProjectsStore
{
    public const int MaxEntries = 20;

    private readonly List<RecentProjectEntry> _entries;

    private RecentProjectsStore(string? path, List<RecentProjectEntry> entries)
    {
        StoragePath = path;
        _entries = entries;
    }

    public IReadOnlyList<RecentProjectEntry> Entries => _entries;

    internal string? StoragePath { get; }

    internal event Action? Changed;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PixelEngine",
        "recent-projects.json");

    public static RecentProjectsStore LoadDefault()
    {
        return Load(DefaultPath);
    }

    public static RecentProjectsStore CreateInMemory()
    {
        return new RecentProjectsStore(null, []);
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
                Favorite = entry.Favorite,
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
        RestoreAutomationSnapshot(CreateAddOrUpdateSnapshot(
            CaptureAutomationSnapshot(),
            project,
            DateTimeOffset.UtcNow));
    }

    public void AddOrUpdateAndSave(EditorProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        PersistAndPublish(CreateAddOrUpdateSnapshot(
            CaptureAutomationSnapshot(),
            project,
            DateTimeOffset.UtcNow));
    }

    private static RecentProjectsAutomationSnapshot CreateAddOrUpdateSnapshot(
        RecentProjectsAutomationSnapshot source,
        EditorProject project,
        DateTimeOffset openedAt)
    {
        string projectPath = Path.GetFullPath(project.ProjectRoot);
        bool favorite = false;
        List<RecentProjectEntry> entries = [.. source.Entries];
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            if (string.Equals(
                Path.GetFullPath(entries[i].ProjectPath),
                projectPath,
                StringComparison.OrdinalIgnoreCase))
            {
                favorite |= entries[i].Favorite;
                entries.RemoveAt(i);
            }
        }

        entries.Insert(
            0,
            new RecentProjectEntry
            {
                Name = project.Name,
                ProjectPath = projectPath,
                LastOpenedUtc = openedAt,
                Favorite = favorite,
            });
        if (entries.Count > MaxEntries)
        {
            entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
        }

        return new RecentProjectsAutomationSnapshot([.. entries]);
    }

    public bool SetFavorite(string projectPath, bool favorite)
    {
        RecentProjectsAutomationSnapshot before = CaptureAutomationSnapshot();
        if (!TryCreateFavoriteSnapshot(before, projectPath, favorite, out RecentProjectsAutomationSnapshot after))
        {
            return false;
        }

        RestoreAutomationSnapshot(after);
        return true;
    }

    public bool SetFavoriteAndSave(string projectPath, bool favorite)
    {
        RecentProjectsAutomationSnapshot before = CaptureAutomationSnapshot();
        if (!TryCreateFavoriteSnapshot(before, projectPath, favorite, out RecentProjectsAutomationSnapshot after))
        {
            return false;
        }

        PersistAndPublish(after);
        return true;
    }

    public bool Remove(string projectPath)
    {
        RecentProjectsAutomationSnapshot before = CaptureAutomationSnapshot();
        if (!TryCreateRemoveSnapshot(before, projectPath, out RecentProjectsAutomationSnapshot after))
        {
            return false;
        }

        RestoreAutomationSnapshot(after);
        return true;
    }

    public bool RemoveAndSave(string projectPath)
    {
        RecentProjectsAutomationSnapshot before = CaptureAutomationSnapshot();
        if (!TryCreateRemoveSnapshot(before, projectPath, out RecentProjectsAutomationSnapshot after))
        {
            return false;
        }

        PersistAndPublish(after);
        return true;
    }

    public void Save()
    {
        SaveSnapshot(CaptureAutomationSnapshot());
    }

    internal RecentProjectsAutomationSnapshot CaptureAutomationSnapshot()
    {
        return new RecentProjectsAutomationSnapshot([.. _entries]);
    }

    internal void RestoreAutomationSnapshot(RecentProjectsAutomationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _entries.Clear();
        _entries.AddRange(snapshot.Entries);
    }

    internal static bool SnapshotsEqual(
        RecentProjectsAutomationSnapshot left,
        RecentProjectsAutomationSnapshot right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.Entries.AsSpan().SequenceEqual(right.Entries);
    }

    internal static bool TryCreateFavoriteSnapshot(
        RecentProjectsAutomationSnapshot source,
        string projectPath,
        bool favorite,
        out RecentProjectsAutomationSnapshot result)
    {
        ArgumentNullException.ThrowIfNull(source);
        result = source;
        if (!TryGetFullPath(projectPath, out string? fullPath) || fullPath is null)
        {
            return false;
        }

        RecentProjectEntry[] entries = [.. source.Entries];
        for (int i = 0; i < entries.Length; i++)
        {
            RecentProjectEntry entry = entries[i];
            if (!string.Equals(entry.ProjectPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entry.Favorite == favorite)
            {
                return false;
            }

            entries[i] = entry with { Favorite = favorite };
            result = new RecentProjectsAutomationSnapshot(entries);
            return true;
        }

        return false;
    }

    internal static bool TryCreateRemoveSnapshot(
        RecentProjectsAutomationSnapshot source,
        string projectPath,
        out RecentProjectsAutomationSnapshot result)
    {
        ArgumentNullException.ThrowIfNull(source);
        result = source;
        if (!TryGetFullPath(projectPath, out string? fullPath) || fullPath is null)
        {
            return false;
        }

        List<RecentProjectEntry> entries = [.. source.Entries];
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(entries[i].ProjectPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entries.RemoveAt(i);
            result = new RecentProjectsAutomationSnapshot([.. entries]);
            return true;
        }

        return false;
    }

    internal static byte[] SerializeCanonical(RecentProjectsAutomationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.SerializeToUtf8Bytes(
            new RecentProjectsDocument { Entries = [.. snapshot.Entries] },
            EditorShellJsonContext.Default.RecentProjectsDocument);
    }

    private void PersistAndPublish(RecentProjectsAutomationSnapshot snapshot)
    {
        SaveSnapshot(snapshot);
        RestoreAutomationSnapshot(snapshot);
        Changed?.Invoke();
    }

    private void SaveSnapshot(RecentProjectsAutomationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (StoragePath is null)
        {
            return;
        }

        string? directory = Path.GetDirectoryName(StoragePath);
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        EditorAtomicTextFile.WriteAllBytes(StoragePath, SerializeCanonical(snapshot));
    }
}

internal sealed record RecentProjectsAutomationSnapshot(RecentProjectEntry[] Entries);

/// <summary>
/// RecentProjectsDocument JSON 文档模型。
/// </summary>
internal sealed class RecentProjectsDocument
{
    public RecentProjectEntry[]? Entries { get; init; }
}

/// <summary>
/// RecentProjectEntry。
/// </summary>
internal sealed record RecentProjectEntry
{
    public string Name { get; init; } = string.Empty;

    public string ProjectPath { get; init; } = string.Empty;

    public DateTimeOffset LastOpenedUtc { get; init; }

    public bool Favorite { get; init; }
}
