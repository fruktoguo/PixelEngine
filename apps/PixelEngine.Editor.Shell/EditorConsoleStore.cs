using PixelEngine.Editor.Shell.Build;
using PixelEngine.Hosting;
using System.Globalization;
using System.Text.RegularExpressions;
using HostingEditorMode = PixelEngine.Hosting.EditorMode;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Console 日志分类。
/// </summary>
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

/// <summary>
/// Console 日志严重级别。
/// </summary>
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
    string Text,
    string Details = "",
    string? FilePath = null,
    int Line = 0,
    long FrameIndex = -1,
    int Column = 0)
{
    public string CollapseKey => string.Join(
        '\u001f',
        Category,
        Severity,
        Source,
        Text,
        Details,
        FilePath ?? string.Empty,
        Line,
        Column);
}

/// <summary>
/// Unity-like Console 的可选择投影行。
/// </summary>
internal readonly record struct EditorConsoleRow(
    long Sequence,
    long LastSequence,
    EditorConsoleEntry Entry,
    int RepeatCount);

/// <summary>
/// Console 三种可见严重度的实时数量。
/// </summary>
internal readonly record struct EditorConsoleCounts(int Logs, int Warnings, int Errors);

/// <summary>
/// Console 的 Play transition 边沿状态；保证 Clear on Play 与 Error Pause 不被逐帧重复触发。
/// </summary>
internal sealed class EditorConsolePlayState(long initialRuntimeErrorSequence = -1)
{
    private bool _modeInitialized;
    private HostingEditorMode _lastMode = HostingEditorMode.Edit;
    private long _observedRuntimeErrorSequence = initialRuntimeErrorSequence;

    public bool ObserveMode(HostingEditorMode mode, bool clearOnPlay)
    {
        bool shouldClear = _modeInitialized &&
            clearOnPlay &&
            _lastMode == HostingEditorMode.Edit &&
            mode == HostingEditorMode.Play;
        _lastMode = mode;
        _modeInitialized = true;
        return shouldClear;
    }

    public bool ObserveRuntimeError(long sequence, bool errorPause, HostingEditorMode mode)
    {
        if (sequence <= _observedRuntimeErrorSequence)
        {
            return false;
        }

        _observedRuntimeErrorSequence = sequence;
        return errorPause && mode == HostingEditorMode.Play;
    }
}

/// <summary>
/// EditorConsoleFilter。
/// </summary>
internal sealed record EditorConsoleFilter
{
    public EditorConsoleCategory? Category { get; init; }

    public EditorConsoleSeverity? Severity { get; init; }

    public EditorConsoleSeverity? MinimumSeverity { get; init; }

    public string? SourceContains { get; init; }

    public string? TextContains { get; init; }

    public string? SearchContains { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public bool Matches(in EditorConsoleEntry entry)
    {
        return !(Category is { } category && entry.Category != category) &&
            !(Severity is { } severity && entry.Severity != severity) &&
            !(MinimumSeverity is { } minimum && entry.Severity < minimum) &&
            !(From is { } from && entry.Timestamp < from) &&
            !(To is { } to && entry.Timestamp > to) &&
            Contains(entry.Source, SourceContains) &&
            Contains(entry.Text, TextContains) &&
            MatchesSearch(entry, SearchContains);
    }

    private static bool Contains(string value, string? filter)
    {
        return string.IsNullOrWhiteSpace(filter) ||
            value.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesSearch(in EditorConsoleEntry entry, string? filter)
    {
        return string.IsNullOrWhiteSpace(filter) ||
            entry.Text.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            entry.Source.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            entry.Details.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(entry.FilePath) && entry.FilePath.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// 编辑器控制台日志接收端接口。
/// </summary>
internal interface IEditorConsoleSink
{
    void Add(EditorConsoleEntry entry);
}

/// <summary>
/// 向 Console 写入 Build 等分类日志的扩展方法。
/// </summary>
internal static class EditorConsoleSinkExtensions
{
    private static readonly Regex ScriptDiagnosticLocationPattern = new(
        @"^(?<path>.+)\((?<line>[0-9]+),(?<column>[0-9]+)(?:,[0-9]+,[0-9]+)?\):",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

    public static void AddCodeWorkspaceOpenResult(this IEditorConsoleSink sink, EditorCodeWorkspaceOpenResult result)
    {
        ArgumentNullException.ThrowIfNull(sink);
        sink.Add(new EditorConsoleEntry(
            DateTimeOffset.UtcNow,
            EditorConsoleCategory.Asset,
            result.Success ? EditorConsoleSeverity.Info : EditorConsoleSeverity.Error,
            "code-project-opener",
            result.Diagnostic));
    }

    public static void AddUiBackendSelection(this IEditorConsoleSink sink, GameUiBackendSelection selection)
    {
        ArgumentNullException.ThrowIfNull(sink);
        string message = selection.UsedFallback
            ? selection.FallbackReason ?? $"UI backend 从 {selection.RequestedBackend} 回退到 {selection.ActiveBackend}。"
            : $"UI backend 使用 {selection.ActiveBackend}。";
        if (!string.IsNullOrWhiteSpace(selection.ActiveNativeProfile))
        {
            message = $"{message} nativeProfile={selection.ActiveNativeProfile}";
        }

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
            sink.AddScriptDiagnostic(source, diagnostics[i], success);
        }
    }

    /// <summary>
    /// 写入一条脚本诊断，并从 Roslyn 标准文本中保留源码文件与一基行列。
    /// </summary>
    public static void AddScriptDiagnostic(
        this IEditorConsoleSink sink,
        string source,
        string diagnostic,
        bool success)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _ = TryParseScriptDiagnosticLocation(
            diagnostic,
            out string? filePath,
            out int line,
            out int column);
        sink.Add(new EditorConsoleEntry(
            DateTimeOffset.UtcNow,
            EditorConsoleCategory.Script,
            ClassifyScriptDiagnostic(diagnostic, success),
            source,
            diagnostic,
            FilePath: filePath,
            Line: line,
            Column: column));
    }

    internal static bool TryParseScriptDiagnosticLocation(
        string diagnostic,
        out string? filePath,
        out int line,
        out int column)
    {
        filePath = null;
        line = 0;
        column = 0;
        if (string.IsNullOrWhiteSpace(diagnostic))
        {
            return false;
        }

        Match match = ScriptDiagnosticLocationPattern.Match(diagnostic);
        if (!match.Success ||
            !int.TryParse(match.Groups["line"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out line) ||
            !int.TryParse(match.Groups["column"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out column) ||
            line <= 0 ||
            column <= 0)
        {
            line = 0;
            column = 0;
            return false;
        }

        string path = match.Groups["path"].Value.Trim();
        if (path.Length == 0)
        {
            line = 0;
            column = 0;
            return false;
        }

        filePath = path;
        return true;
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
        return !success || diagnostic.Contains("error", StringComparison.OrdinalIgnoreCase)
            ? EditorConsoleSeverity.Error
            : diagnostic.Contains("warning", StringComparison.OrdinalIgnoreCase)
            ? EditorConsoleSeverity.Warning
            : EditorConsoleSeverity.Info;
    }
}

/// <summary>
/// 编辑器控制台日志环形缓冲与过滤查询。
/// </summary>
internal sealed class EditorConsoleStore : IEditorConsoleSink
{
    public const int DefaultCapacity = 2048;

    private readonly int _capacity;
    private readonly Queue<ConsoleStoreItem> _items;
    private long _nextSequence;
    private long _lastRuntimeErrorSequence = -1;

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

    /// <summary>
    /// 最近加入的原始日志序号；无日志时为 -1。
    /// </summary>
    public long LastSequence
    {
        get
        {
            lock (_items)
            {
                return _nextSequence - 1;
            }
        }
    }

    /// <summary>
    /// 最近一条运行时错误序号；供 Error Pause 边沿触发，不把 Build/Asset 错误误当运行时错误。
    /// </summary>
    public long LastRuntimeErrorSequence => Volatile.Read(ref _lastRuntimeErrorSequence);

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

            long sequence = _nextSequence++;
            _items.Enqueue(new ConsoleStoreItem(sequence, normalized));
            if (normalized.Category == EditorConsoleCategory.Runtime && normalized.Severity == EditorConsoleSeverity.Error)
            {
                Volatile.Write(ref _lastRuntimeErrorSequence, sequence);
            }
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

    /// <summary>
    /// 捕获 Unity-like Console 行；Collapse 只聚合投影，不破坏原始环形缓冲。
    /// </summary>
    public EditorConsoleRow[] SnapshotRows(
        EditorConsoleFilter? filter = null,
        bool newestFirst = false,
        bool collapse = false)
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

        if (!collapse)
        {
            IEnumerable<ConsoleStoreItem> ordered = newestFirst
                ? query.OrderByDescending(static item => item.Sequence)
                : query.OrderBy(static item => item.Sequence);
            return
            [
                .. ordered.Select(static item => new EditorConsoleRow(
                    item.Sequence,
                    item.Sequence,
                    item.Entry,
                    RepeatCount: 1)),
            ];
        }

        Dictionary<string, CollapsedConsoleItem> collapsed = new(StringComparer.Ordinal);
        foreach (ConsoleStoreItem item in query.OrderBy(static item => item.Sequence))
        {
            string key = item.Entry.CollapseKey;
            if (collapsed.TryGetValue(key, out CollapsedConsoleItem existing))
            {
                collapsed[key] = existing with
                {
                    LastSequence = item.Sequence,
                    RepeatCount = existing.RepeatCount + 1,
                };
            }
            else
            {
                collapsed.Add(key, new CollapsedConsoleItem(item.Sequence, item.Sequence, item.Entry, 1));
            }
        }

        IEnumerable<CollapsedConsoleItem> collapsedQuery = newestFirst
            ? collapsed.Values.OrderByDescending(static item => item.LastSequence)
            : collapsed.Values.OrderBy(static item => item.Sequence);
        return
        [
            .. collapsedQuery.Select(static item => new EditorConsoleRow(
                item.Sequence,
                item.LastSequence,
                item.Entry,
                item.RepeatCount)),
        ];
    }

    /// <summary>
    /// 捕获原始日志的 Log/Warning/Error 数量；Collapse 与文本搜索不改变总数。
    /// </summary>
    public EditorConsoleCounts CaptureCounts()
    {
        lock (_items)
        {
            int logs = 0;
            int warnings = 0;
            int errors = 0;
            foreach (ConsoleStoreItem item in _items)
            {
                switch (item.Entry.Severity)
                {
                    case EditorConsoleSeverity.Trace:
                    case EditorConsoleSeverity.Info:
                        logs++;
                        break;
                    case EditorConsoleSeverity.Warning:
                        warnings++;
                        break;
                    case EditorConsoleSeverity.Error:
                        errors++;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(item), item.Entry.Severity, "未知 Console 严重度。");
                }
            }

            return new EditorConsoleCounts(logs, warnings, errors);
        }
    }


    private readonly record struct ConsoleStoreItem(long Sequence, EditorConsoleEntry Entry);

    private readonly record struct CollapsedConsoleItem(
        long Sequence,
        long LastSequence,
        EditorConsoleEntry Entry,
        int RepeatCount);
}
