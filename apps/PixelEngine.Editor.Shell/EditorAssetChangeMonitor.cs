namespace PixelEngine.Editor.Shell;

/// <summary>
/// Editor 资产监视器的逻辑根。
/// </summary>
internal enum EditorAssetRootKind
{
    Content,
    ScriptSource,
}

/// <summary>
/// Editor 资产增量变更类型。
/// </summary>
internal enum EditorAssetChangeKind
{
    Created,
    Changed,
    Deleted,
    Renamed,
}

/// <summary>
/// 已规范化到逻辑根下的资产路径。
/// </summary>
/// <param name="Root">逻辑根。</param>
/// <param name="RelativePath">使用正斜杠、且不越过根目录的相对路径。</param>
internal readonly record struct EditorAssetPath(EditorAssetRootKind Root, string RelativePath);

/// <summary>
/// 一条合并后的 Editor 资产变更。
/// </summary>
/// <param name="Kind">变更类型。</param>
/// <param name="Path">当前路径；Deleted 时为被删除路径。</param>
/// <param name="OldPath">Renamed 的旧路径，其余类型为 null。</param>
internal readonly record struct EditorAssetChange(
    EditorAssetChangeKind Kind,
    EditorAssetPath Path,
    EditorAssetPath? OldPath = null);

/// <summary>
/// 一次主线程 drain 得到的不可变资产失效批次。
/// </summary>
/// <param name="Changes">仍可信的增量变更。</param>
/// <param name="FullRescanRoots">watcher 或内存队列溢出后必须完整重扫的逻辑根。</param>
internal sealed record EditorAssetChangeBatch(
    EditorAssetChange[] Changes,
    EditorAssetRootKind[] FullRescanRoots)
{
    public bool RequiresFullRescan => FullRescanRoots.Length > 0;

    public bool IsEmpty => Changes.Length == 0 && FullRescanRoots.Length == 0;
}

/// <summary>
/// 线程安全的资产事件合并核心；FileSystemWatcher 回调与确定性单元测试共用。
/// </summary>
internal sealed class EditorAssetChangeAccumulator
{
    internal const int DefaultMaxPendingChangesPerRoot = 4096;

    private readonly Lock _gate = new();
    private readonly int _maxPendingChangesPerRoot;
    private readonly List<EditorAssetChange> _changes = [];
    private readonly HashSet<EditorAssetRootKind> _fullRescanRoots = [];

    public EditorAssetChangeAccumulator(int maxPendingChangesPerRoot = DefaultMaxPendingChangesPerRoot)
    {
        if (maxPendingChangesPerRoot <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxPendingChangesPerRoot),
                maxPendingChangesPerRoot,
                "每个资产根的待处理变更上限必须为正数。");
        }

        _maxPendingChangesPerRoot = maxPendingChangesPerRoot;
    }

    public void RecordCreated(EditorAssetRootKind root, string relativePath)
    {
        RecordSimple(EditorAssetChangeKind.Created, CreatePath(root, relativePath));
    }

    public void RecordChanged(EditorAssetRootKind root, string relativePath)
    {
        RecordSimple(EditorAssetChangeKind.Changed, CreatePath(root, relativePath));
    }

    public void RecordDeleted(EditorAssetRootKind root, string relativePath)
    {
        RecordSimple(EditorAssetChangeKind.Deleted, CreatePath(root, relativePath));
    }

    public void RecordRenamed(EditorAssetRootKind root, string oldRelativePath, string newRelativePath)
    {
        EditorAssetPath oldPath = CreatePath(root, oldRelativePath);
        EditorAssetPath newPath = CreatePath(root, newRelativePath);
        lock (_gate)
        {
            if (_fullRescanRoots.Contains(root))
            {
                return;
            }

            if (PathEquals(oldPath, newPath))
            {
                RecordSimpleCore(EditorAssetChangeKind.Changed, newPath);
                return;
            }

            int index = FindLatestCurrentPath(oldPath);
            if (index < 0)
            {
                _changes.Add(new EditorAssetChange(EditorAssetChangeKind.Renamed, newPath, oldPath));
            }
            else
            {
                EditorAssetChange previous = _changes[index];
                _changes[index] = previous.Kind switch
                {
                    // Editor 自己可能已同步登记刚创建的资产；若在 drain 前又发生 rename，
                    // 丢掉 old path 会让缓存同时保留旧记录与新记录。保留 rename 语义后，
                    // 无论旧记录是否已入 manifest，消费端都能原子替换或安全退化为发现新文件。
                    EditorAssetChangeKind.Created => new EditorAssetChange(EditorAssetChangeKind.Renamed, newPath, oldPath),
                    EditorAssetChangeKind.Changed => new EditorAssetChange(EditorAssetChangeKind.Renamed, newPath, oldPath),
                    EditorAssetChangeKind.Renamed => new EditorAssetChange(
                        EditorAssetChangeKind.Renamed,
                        newPath,
                        previous.OldPath ?? oldPath),
                    EditorAssetChangeKind.Deleted => new EditorAssetChange(EditorAssetChangeKind.Renamed, newPath, oldPath),
                    _ => new EditorAssetChange(EditorAssetChangeKind.Renamed, newPath, oldPath),
                };
            }

            InvalidateIfQueueExceeded(root);
        }
    }

    public void InvalidateRoot(EditorAssetRootKind root)
    {
        ValidateRoot(root);
        lock (_gate)
        {
            InvalidateRootCore(root);
        }
    }

    public EditorAssetChangeBatch Drain()
    {
        lock (_gate)
        {
            EditorAssetChangeBatch batch = new(
                [.. _changes],
                [.. _fullRescanRoots.Order()]);
            _changes.Clear();
            _fullRescanRoots.Clear();
            return batch;
        }
    }

    private void RecordSimple(EditorAssetChangeKind kind, EditorAssetPath path)
    {
        lock (_gate)
        {
            if (_fullRescanRoots.Contains(path.Root))
            {
                return;
            }

            RecordSimpleCore(kind, path);
        }
    }

    private void RecordSimpleCore(EditorAssetChangeKind kind, EditorAssetPath path)
    {
        int index = FindLatestCurrentPath(path);
        if (index < 0)
        {
            _changes.Add(new EditorAssetChange(kind, path));
            InvalidateIfQueueExceeded(path.Root);
            return;
        }

        EditorAssetChange previous = _changes[index];
        switch (kind)
        {
            case EditorAssetChangeKind.Created:
                _changes[index] = previous.Kind switch
                {
                    EditorAssetChangeKind.Deleted => new EditorAssetChange(EditorAssetChangeKind.Changed, path),
                    EditorAssetChangeKind.Changed => new EditorAssetChange(EditorAssetChangeKind.Created, path),
                    EditorAssetChangeKind.Created => previous,
                    EditorAssetChangeKind.Renamed => previous,
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(previous),
                        previous.Kind,
                        "未知待合并资产变更类型。"),
                };
                break;

            case EditorAssetChangeKind.Changed:
                if (previous.Kind == EditorAssetChangeKind.Deleted)
                {
                    return;
                }

                // Created 与 Renamed 已经包含新路径内容失效；重复 Changed 无需继续排队。
                break;

            case EditorAssetChangeKind.Deleted:
                switch (previous.Kind)
                {
                    case EditorAssetChangeKind.Created:
                        _changes.RemoveAt(index);
                        break;
                    case EditorAssetChangeKind.Renamed:
                        _changes[index] = new EditorAssetChange(
                            EditorAssetChangeKind.Deleted,
                            previous.OldPath ?? path);
                        break;
                    case EditorAssetChangeKind.Changed:
                    case EditorAssetChangeKind.Deleted:
                        _changes[index] = new EditorAssetChange(EditorAssetChangeKind.Deleted, path);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(previous),
                            previous.Kind,
                            "未知待合并资产变更类型。");
                }

                break;

            case EditorAssetChangeKind.Renamed:
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Renamed 必须通过 RecordRenamed 记录。");
        }
    }

    private void InvalidateIfQueueExceeded(EditorAssetRootKind root)
    {
        int count = 0;
        for (int i = 0; i < _changes.Count; i++)
        {
            if (_changes[i].Path.Root == root && ++count > _maxPendingChangesPerRoot)
            {
                InvalidateRootCore(root);
                return;
            }
        }
    }

    private void InvalidateRootCore(EditorAssetRootKind root)
    {
        _ = _fullRescanRoots.Add(root);
        for (int i = _changes.Count - 1; i >= 0; i--)
        {
            if (_changes[i].Path.Root == root)
            {
                _changes.RemoveAt(i);
            }
        }
    }

    private int FindLatestCurrentPath(EditorAssetPath path)
    {
        for (int i = _changes.Count - 1; i >= 0; i--)
        {
            if (PathEquals(_changes[i].Path, path))
            {
                return i;
            }
        }

        return -1;
    }

    private static EditorAssetPath CreatePath(EditorAssetRootKind root, string relativePath)
    {
        ValidateRoot(root);
        return new EditorAssetPath(root, EditorAssetChangeMonitor.NormalizeRelativePath(relativePath));
    }

    private static void ValidateRoot(EditorAssetRootKind root)
    {
        if (!Enum.IsDefined(root))
        {
            throw new ArgumentOutOfRangeException(nameof(root), root, "未知 Editor 资产逻辑根。");
        }
    }

    private static bool PathEquals(EditorAssetPath left, EditorAssetPath right)
    {
        return left.Root == right.Root &&
            string.Equals(left.RelativePath, right.RelativePath, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// 同时监视 content 与 script source 两个物理根，并发布规范化增量失效批次。
/// </summary>
internal sealed class EditorAssetChangeMonitor : IDisposable
{
    private const int WatcherBufferBytes = 64 * 1024;
    private readonly EditorAssetChangeAccumulator _accumulator;
    private readonly FileSystemWatcher _contentWatcher;
    private readonly FileSystemWatcher _scriptWatcher;
    private int _disposeState;

    public EditorAssetChangeMonitor(
        string contentRoot,
        string scriptSourceRoot,
        int maxPendingChangesPerRoot = EditorAssetChangeAccumulator.DefaultMaxPendingChangesPerRoot)
    {
        ContentRoot = NormalizeExistingRoot(contentRoot, nameof(contentRoot));
        ScriptSourceRoot = NormalizeExistingRoot(scriptSourceRoot, nameof(scriptSourceRoot));
        _accumulator = new EditorAssetChangeAccumulator(maxPendingChangesPerRoot);
        _contentWatcher = CreateWatcher(ContentRoot, EditorAssetRootKind.Content);
        try
        {
            _scriptWatcher = CreateWatcher(ScriptSourceRoot, EditorAssetRootKind.ScriptSource);
        }
        catch
        {
            _contentWatcher.Dispose();
            throw;
        }
    }

    public string ContentRoot { get; }

    public string ScriptSourceRoot { get; }

    public EditorAssetChangeBatch Drain()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        return _accumulator.Drain();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _contentWatcher.EnableRaisingEvents = false;
        _scriptWatcher.EnableRaisingEvents = false;
        _contentWatcher.Dispose();
        _scriptWatcher.Dispose();
    }

    internal static bool TryNormalizeEventPath(string root, string fullPath, out string relativePath)
    {
        relativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        try
        {
            string normalizedRoot = Path.GetFullPath(root);
            string normalizedFullPath = Path.GetFullPath(fullPath);
            string rootWithSeparator = Path.EndsInDirectorySeparator(normalizedRoot)
                ? normalizedRoot
                : normalizedRoot + Path.DirectorySeparatorChar;
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!normalizedFullPath.StartsWith(rootWithSeparator, comparison))
            {
                return false;
            }

            relativePath = NormalizeRelativePath(Path.GetRelativePath(normalizedRoot, normalizedFullPath));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            relativePath = string.Empty;
            return false;
        }
    }

    internal static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidOperationException("资产相对路径不能为空。");
        }

        string candidate = relativePath.Replace('\\', '/');
        if (Path.IsPathRooted(candidate) || candidate.StartsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"资产路径必须位于逻辑根内：{relativePath}");
        }

        string[] parts = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        List<string> normalized = new(parts.Length);
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == ".")
            {
                continue;
            }

            if (parts[i] == "..")
            {
                throw new InvalidOperationException($"资产路径不能越过逻辑根：{relativePath}");
            }

            normalized.Add(parts[i]);
        }

        return normalized.Count == 0
            ? throw new InvalidOperationException("资产相对路径不能为空。")
            : string.Join('/', normalized);
    }

    internal static bool IsInternalMetadataPath(string relativePath)
    {
        return string.Equals(relativePath, ".pixelengine", StringComparison.OrdinalIgnoreCase) ||
            relativePath.StartsWith(".pixelengine/", StringComparison.OrdinalIgnoreCase);
    }

    private FileSystemWatcher CreateWatcher(string root, EditorAssetRootKind rootKind)
    {
        FileSystemWatcher watcher = new(root)
        {
            Filter = "*",
            IncludeSubdirectories = true,
            InternalBufferSize = WatcherBufferBytes,
            NotifyFilter = NotifyFilters.FileName |
                NotifyFilters.DirectoryName |
                NotifyFilters.LastWrite |
                NotifyFilters.Size |
                NotifyFilters.CreationTime,
        };
        watcher.Created += (_, args) => RecordPath(rootKind, root, EditorAssetChangeKind.Created, args.FullPath);
        watcher.Changed += (_, args) => RecordPath(rootKind, root, EditorAssetChangeKind.Changed, args.FullPath);
        watcher.Deleted += (_, args) => RecordPath(rootKind, root, EditorAssetChangeKind.Deleted, args.FullPath);
        watcher.Renamed += (_, args) => RecordRename(rootKind, root, args.OldFullPath, args.FullPath);
        watcher.Error += (_, _) => InvalidateRoot(rootKind);
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void RecordPath(
        EditorAssetRootKind rootKind,
        string physicalRoot,
        EditorAssetChangeKind kind,
        string fullPath)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        if (!TryNormalizeEventPath(physicalRoot, fullPath, out string relativePath))
        {
            InvalidateRoot(rootKind);
            return;
        }

        if (IsInternalMetadataPath(relativePath))
        {
            return;
        }

        switch (kind)
        {
            case EditorAssetChangeKind.Created:
                _accumulator.RecordCreated(rootKind, relativePath);
                break;
            case EditorAssetChangeKind.Changed:
                _accumulator.RecordChanged(rootKind, relativePath);
                break;
            case EditorAssetChangeKind.Deleted:
                _accumulator.RecordDeleted(rootKind, relativePath);
                break;
            case EditorAssetChangeKind.Renamed:
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Renamed 必须携带 old/new path。");
        }
    }

    private void RecordRename(
        EditorAssetRootKind rootKind,
        string physicalRoot,
        string oldFullPath,
        string newFullPath)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        if (!TryNormalizeEventPath(physicalRoot, oldFullPath, out string oldRelativePath) ||
            !TryNormalizeEventPath(physicalRoot, newFullPath, out string newRelativePath))
        {
            InvalidateRoot(rootKind);
            return;
        }

        bool oldInternal = IsInternalMetadataPath(oldRelativePath);
        bool newInternal = IsInternalMetadataPath(newRelativePath);
        if (oldInternal && newInternal)
        {
            return;
        }

        if (oldInternal)
        {
            _accumulator.RecordCreated(rootKind, newRelativePath);
            return;
        }

        if (newInternal)
        {
            _accumulator.RecordDeleted(rootKind, oldRelativePath);
            return;
        }

        _accumulator.RecordRenamed(rootKind, oldRelativePath, newRelativePath);
    }

    private void InvalidateRoot(EditorAssetRootKind rootKind)
    {
        if (Volatile.Read(ref _disposeState) == 0)
        {
            _accumulator.InvalidateRoot(rootKind);
        }
    }

    private static string NormalizeExistingRoot(string root, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root, parameterName);
        string normalized = Path.GetFullPath(root);
        return Directory.Exists(normalized)
            ? normalized
            : throw new DirectoryNotFoundException($"Editor 资产监视根不存在：{normalized}");
    }
}
