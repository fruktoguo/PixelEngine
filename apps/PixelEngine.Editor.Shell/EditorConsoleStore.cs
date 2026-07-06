using PixelEngine.Editor.Shell.Build;
using PixelEngine.Hosting;

namespace PixelEngine.Editor.Shell;

internal enum EditorConsoleCategory
{
    General,
    Build,
    Script,
    Ui,
    Asset,
    Project,
    Runtime,
}

internal enum EditorConsoleSeverity
{
    Trace,
    Info,
    Warning,
    Error,
}

internal readonly record struct EditorConsoleEntry(
    DateTimeOffset Timestamp,
    EditorConsoleCategory Category,
    EditorConsoleSeverity Severity,
    string Source,
    string Text);

internal sealed record EditorConsoleFilter
{
    public EditorConsoleCategory? Category { get; init; }

    public EditorConsoleSeverity? Severity { get; init; }

    public EditorConsoleSeverity? MinimumSeverity { get; init; }

    public string? SourceContains { get; init; }

    public string? TextContains { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public bool Matches(in EditorConsoleEntry entry)
    {
        if (Category is { } category && entry.Category != category)
        {
            return false;
        }

        if (Severity is { } severity && entry.Severity != severity)
        {
            return false;
        }

        if (MinimumSeverity is { } minimum && entry.Severity < minimum)
        {
            return false;
        }

        if (From is { } from && entry.Timestamp < from)
        {
            return false;
        }

        if (To is { } to && entry.Timestamp > to)
        {
            return false;
        }

        if (!Contains(entry.Source, SourceContains))
        {
            return false;
        }

        return Contains(entry.Text, TextContains);
    }

    private static bool Contains(string value, string? filter)
    {
        return string.IsNullOrWhiteSpace(filter) ||
            value.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}

internal interface IEditorConsoleSink
{
    void Add(EditorConsoleEntry entry);
}

internal static class EditorConsoleSinkExtensions
{
    public static void AddBuildEvent(this IEditorConsoleSink sink, BuildProgressEvent item, string source = "build-player")
    {
        ArgumentNullException.ThrowIfNull(sink);
        sink.Add(new EditorConsoleEntry(
            item.Timestamp,
            EditorConsoleCategory.Build,
            FromBuildLogLevel(item.Level),
            source,
            $"[{item.Phase}] {item.Message}"));
    }

    public static void AddBuildPreflight(this IEditorConsoleSink sink, BuildPreflight preflight)
    {
        ArgumentNullException.ThrowIfNull(sink);
        sink.Add(new EditorConsoleEntry(
            DateTimeOffset.UtcNow,
            EditorConsoleCategory.Build,
            preflight.Ok ? EditorConsoleSeverity.Info : EditorConsoleSeverity.Error,
            "build-preflight",
            preflight.Diagnostic));
    }

    public static void AddBuildResult(this IEditorConsoleSink sink, BuildResult result)
    {
        ArgumentNullException.ThrowIfNull(sink);
        string message = result.Ok
            ? $"构建完成：{result.PackageArchive ?? result.PackageDir ?? result.Rid}"
            : result.Error ?? $"构建失败，exit code={result.ExitCode}。";
        sink.Add(new EditorConsoleEntry(
            DateTimeOffset.UtcNow,
            EditorConsoleCategory.Build,
            result.Ok ? EditorConsoleSeverity.Info : EditorConsoleSeverity.Error,
            "build-result",
            message));
        for (int i = 0; i < result.Warnings.Count; i++)
        {
            sink.Add(new EditorConsoleEntry(
                DateTimeOffset.UtcNow,
                EditorConsoleCategory.Build,
                EditorConsoleSeverity.Warning,
                "build-result",
                result.Warnings[i]));
        }
    }

    public static void AddAssetOpenResult(this IEditorConsoleSink sink, EditorScriptAssetOpenResult result)
    {
        ArgumentNullException.ThrowIfNull(sink);
        sink.Add(new EditorConsoleEntry(
            DateTimeOffset.UtcNow,
            EditorConsoleCategory.Asset,
            result.Success ? EditorConsoleSeverity.Info : EditorConsoleSeverity.Error,
            "asset-opener",
            result.Diagnostic));
    }

    public static void AddUiBackendSelection(this IEditorConsoleSink sink, GameUiBackendSelection selection)
    {
        ArgumentNullException.ThrowIfNull(sink);
        string message = selection.UsedFallback
            ? selection.FallbackReason ?? $"UI backend 从 {selection.RequestedBackend} 回退到 {selection.ActiveBackend}。"
            : $"UI backend 使用 {selection.ActiveBackend}。";
        sink.Add(new EditorConsoleEntry(
            DateTimeOffset.UtcNow,
            EditorConsoleCategory.Ui,
            selection.UsedFallback ? EditorConsoleSeverity.Warning : EditorConsoleSeverity.Info,
            "ui-backend",
            message));
    }

    public static void AddScriptDiagnostics(this IEditorConsoleSink sink, string source, string message, IReadOnlyList<string> diagnostics, bool success)
    {
        ArgumentNullException.ThrowIfNull(sink);
        sink.Add(new EditorConsoleEntry(
            DateTimeOffset.UtcNow,
            EditorConsoleCategory.Script,
            success ? EditorConsoleSeverity.Info : EditorConsoleSeverity.Error,
            source,
            message));
        for (int i = 0; i < diagnostics.Count; i++)
        {
            sink.Add(new EditorConsoleEntry(
                DateTimeOffset.UtcNow,
                EditorConsoleCategory.Script,
                ClassifyScriptDiagnostic(diagnostics[i], success),
                source,
                diagnostics[i]));
        }
    }

    public static void AddProjectError(this IEditorConsoleSink sink, string source, string message)
    {
        ArgumentNullException.ThrowIfNull(sink);
        sink.Add(new EditorConsoleEntry(
            DateTimeOffset.UtcNow,
            EditorConsoleCategory.Project,
            EditorConsoleSeverity.Error,
            source,
            message));
    }

    private static EditorConsoleSeverity FromBuildLogLevel(BuildLogLevel level)
    {
        return level switch
        {
            BuildLogLevel.Info => EditorConsoleSeverity.Info,
            BuildLogLevel.Warning => EditorConsoleSeverity.Warning,
            BuildLogLevel.Error => EditorConsoleSeverity.Error,
            _ => EditorConsoleSeverity.Info,
        };
    }

    private static EditorConsoleSeverity ClassifyScriptDiagnostic(string diagnostic, bool success)
    {
        if (!success || diagnostic.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            return EditorConsoleSeverity.Error;
        }

        return diagnostic.Contains("warning", StringComparison.OrdinalIgnoreCase)
            ? EditorConsoleSeverity.Warning
            : EditorConsoleSeverity.Info;
    }
}

internal sealed class EditorConsoleStore : IEditorConsoleSink
{
    public const int DefaultCapacity = 2048;

    private readonly int _capacity;
    private readonly Queue<ConsoleStoreItem> _items;
    private long _nextSequence;

    public EditorConsoleStore(int capacity = DefaultCapacity)
    {
        _capacity = Math.Max(8, capacity);
        _items = new Queue<ConsoleStoreItem>(_capacity);
    }

    public int Count
    {
        get
        {
            lock (_items)
            {
                return _items.Count;
            }
        }
    }

    public void Add(EditorConsoleEntry entry)
    {
        string source = string.IsNullOrWhiteSpace(entry.Source) ? "unknown" : entry.Source.Trim();
        string text = string.IsNullOrWhiteSpace(entry.Text) ? "<empty>" : entry.Text.Trim();
        DateTimeOffset timestamp = entry.Timestamp == default ? DateTimeOffset.UtcNow : entry.Timestamp;
        EditorConsoleEntry normalized = entry with
        {
            Source = source,
            Text = text,
            Timestamp = timestamp,
        };

        lock (_items)
        {
            while (_items.Count >= _capacity)
            {
                _ = _items.Dequeue();
            }

            _items.Enqueue(new ConsoleStoreItem(_nextSequence++, normalized));
        }
    }

    public void Clear()
    {
        lock (_items)
        {
            _items.Clear();
        }
    }

    public EditorConsoleEntry[] Snapshot(EditorConsoleFilter? filter = null, bool newestFirst = false)
    {
        ConsoleStoreItem[] snapshot;
        lock (_items)
        {
            snapshot = [.. _items];
        }

        IEnumerable<ConsoleStoreItem> query = snapshot;
        if (filter is not null)
        {
            query = query.Where(item => filter.Matches(item.Entry));
        }

        query = newestFirst
            ? query.OrderByDescending(static item => item.Entry.Timestamp).ThenByDescending(static item => item.Sequence)
            : query.OrderBy(static item => item.Entry.Timestamp).ThenBy(static item => item.Sequence);
        return [.. query.Select(static item => item.Entry)];
    }


    private readonly record struct ConsoleStoreItem(long Sequence, EditorConsoleEntry Entry);
}
