using System.Globalization;
using System.Text.Json;
using PixelEngine.Hosting;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 资产浏览器面板的数据源，对接 manifest 与拖放。
/// </summary>
internal sealed class EditorAssetBrowserDataSource :
    IAssetBrowserDataSource,
    IAssetBrowserRefreshableDataSource,
    IAssetBrowserDiagnosticDataSource,
    IAssetBrowserFolderDataSource,
    IAssetBrowserContextDataSource,
    IDisposable
{
    private const string ScriptManifestRelativePath = ".pixelengine/script-assets.json";
    private const int MaxIncrementalChangesPerPump = 16;
    private readonly EditorAssetManifestStore _assets;
    private readonly EditorAssetManifestStore? _scriptAssets;
    private readonly EditorAssetChangeMonitor? _changeMonitor;
    private readonly ITextureThumbnailProvider? _thumbnailProvider;
    private readonly EditorProjectSceneAssetMoveService? _sceneAssetMoveService;
    private readonly EditorSceneModel? _activeScene;
    private readonly EditorProject? _project;
    private readonly Func<string?>? _currentScenePath;
    private readonly bool _rootedBrowserPaths;
    private readonly Queue<EditorAssetChange> _pendingChanges = [];
    private readonly HashSet<EditorAssetRootKind> _pendingFullRescanRoots = [];
    private AssetBrowserItem[] _assetSnapshot = [];
    private AssetBrowserFolderItem[] _folderSnapshot = [];
    private string _runtimeDiagnostic = string.Empty;
    private bool _disposed;

    public EditorAssetBrowserDataSource(
        EditorProject project,
        ITextureThumbnailProvider? thumbnailProvider = null,
        EditorSceneModel? activeScene = null,
        Func<string?>? currentScenePath = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        _assets = new EditorAssetManifestStore(project);
        _ = Directory.CreateDirectory(project.ContentRootPath);
        _ = Directory.CreateDirectory(project.ScriptSourcePath);
        _scriptAssets = new EditorAssetManifestStore(
            project.ProjectRoot,
            project.ScriptSourcePath,
            ScriptManifestRelativePath,
            project.ContentRootPath);
        try
        {
            _changeMonitor = new EditorAssetChangeMonitor(project.ContentRootPath, project.ScriptSourcePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _changeMonitor = null;
            WatcherDiagnostic = $"资产增量监视器启动失败，将保留手动刷新：{exception.Message}";
        }

        _thumbnailProvider = thumbnailProvider;
        _sceneAssetMoveService = new EditorProjectSceneAssetMoveService(project, _assets);
        _activeScene = activeScene;
        _project = project;
        _currentScenePath = currentScenePath;
        _rootedBrowserPaths = true;
        RefreshAssets();
    }

    public string LastDiagnostic { get; private set; } = string.Empty;

    public string AssetDatabaseDiagnostic => LastDiagnostic;

    private string WatcherDiagnostic { get; } = string.Empty;

    public EditorAssetBrowserDataSource(
        EditorAssetManifestStore assets,
        ITextureThumbnailProvider? thumbnailProvider = null,
        EditorProjectSceneAssetMoveService? sceneAssetMoveService = null)
    {
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _thumbnailProvider = thumbnailProvider;
        _sceneAssetMoveService = sceneAssetMoveService;
        _activeScene = null;
        _project = null;
        _currentScenePath = null;
        RefreshAssets();
    }

    /// <summary>
    /// 返回当前资产缓存；只读查询不扫描磁盘、不解析预览，也不写 manifest。
    /// </summary>
    public IReadOnlyList<AssetBrowserItem> ListAssets()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _assetSnapshot;
    }

    public IReadOnlyList<AssetBrowserFolderItem> ListFolders()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _folderSnapshot;
    }

    /// <inheritdoc />
    public AssetBrowserBadge GetContextBadges(string assetPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_project is null ||
            !EditorRootedBrowserPath.TryParse(assetPath, out EditorAssetPath path, out _) ||
            path.Root != EditorAssetRootKind.Content)
        {
            return AssetBrowserBadge.None;
        }

        AssetBrowserBadge badges = AssetBrowserBadge.None;
        if (string.Equals(path.RelativePath, _project.StartScene, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path.RelativePath, "startup.json", StringComparison.OrdinalIgnoreCase))
        {
            badges |= AssetBrowserBadge.Startup;
        }

        string? currentScene = _currentScenePath?.Invoke();
        if (!string.IsNullOrWhiteSpace(currentScene) &&
            string.Equals(path.RelativePath, NormalizeLogicalPathForComparison(currentScene), StringComparison.OrdinalIgnoreCase))
        {
            badges |= AssetBrowserBadge.Current;
        }

        return badges;
    }

    /// <inheritdoc />
    public void RefreshAssets()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_changeMonitor is not null)
        {
            EnqueueChangeBatch(_changeMonitor.Drain());
        }

        try
        {
            _runtimeDiagnostic = string.Empty;
            _ = ApplyQueuedChanges(int.MaxValue);
            RebuildFullSnapshot();
        }
        catch (Exception exception) when (IsAssetDatabaseFailure(exception))
        {
            _runtimeDiagnostic = $"资产数据库完整刷新失败，已保留上一份缓存：{exception.Message}";
        }

        UpdateDiagnostic();
    }

    /// <inheritdoc />
    public bool ApplyPendingChanges()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_changeMonitor is null)
        {
            return false;
        }

        EnqueueChangeBatch(_changeMonitor.Drain());
        bool changed = false;
        if (_pendingChanges.Count > 0 || _pendingFullRescanRoots.Count > 0)
        {
            try
            {
                _runtimeDiagnostic = string.Empty;
                changed = ApplyQueuedChanges(MaxIncrementalChangesPerPump);
            }
            catch (Exception exception) when (IsAssetDatabaseFailure(exception))
            {
                _runtimeDiagnostic = $"资产数据库增量刷新失败，已保留可用缓存：{exception.Message}";
            }
        }

        UpdateDiagnostic();
        return changed;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _changeMonitor?.Dispose();
    }

    private void RebuildFullSnapshot()
    {
        List<AssetBrowserItem> items = [];
        IReadOnlyList<EditorAssetRecord> contentRecords = _assets.Refresh();
        ReconcileInferredRefreshMoves(EditorAssetRootKind.Content, _assets);
        AppendRecords(items, EditorAssetRootKind.Content, contentRecords);
        if (_scriptAssets is not null)
        {
            IReadOnlyList<EditorAssetRecord> scriptRecords = _scriptAssets.Refresh();
            ReconcileInferredRefreshMoves(EditorAssetRootKind.ScriptSource, _scriptAssets);
            AppendRecords(items, EditorAssetRootKind.ScriptSource, scriptRecords);
        }

        _assetSnapshot =
        [
            .. items.OrderBy(static item => item.Path, StringComparer.OrdinalIgnoreCase),
        ];
        _folderSnapshot = BuildFolderSnapshot();
    }

    private void UpdateDiagnostic()
    {
        LastDiagnostic = string.Join(
            Environment.NewLine,
            new[] { WatcherDiagnostic, _runtimeDiagnostic, _assets.LastDiagnostic, _scriptAssets?.LastDiagnostic ?? string.Empty }
                .Where(static diagnostic => !string.IsNullOrWhiteSpace(diagnostic)));
    }

    private void EnqueueChangeBatch(EditorAssetChangeBatch batch)
    {
        for (int i = 0; i < batch.Changes.Length; i++)
        {
            _pendingChanges.Enqueue(batch.Changes[i]);
        }

        for (int i = 0; i < batch.FullRescanRoots.Length; i++)
        {
            _ = _pendingFullRescanRoots.Add(batch.FullRescanRoots[i]);
        }
    }

    private bool ApplyQueuedChanges(int maxChanges)
    {
        bool changed = false;
        int processed = 0;
        while (_pendingChanges.Count > 0 && processed < maxChanges)
        {
            if (TryBuildSimpleFileBatch(maxChanges - processed, out EditorAssetChange[] simpleBatch))
            {
                changed |= ApplySimpleFileBatch(simpleBatch);
                for (int i = 0; i < simpleBatch.Length; i++)
                {
                    _ = _pendingChanges.Dequeue();
                }

                processed += simpleBatch.Length;
                continue;
            }

            EditorAssetChange change = _pendingChanges.Peek();
            changed |= ApplyIncrementalChange(change);
            _ = _pendingChanges.Dequeue();
            processed++;
        }

        if (_pendingFullRescanRoots.Count > 0)
        {
            EditorAssetRootKind[] roots = [.. _pendingFullRescanRoots.Order()];
            for (int i = 0; i < roots.Length; i++)
            {
                if (HasQueuedChangeForRoot(roots[i]))
                {
                    continue;
                }

                EditorAssetManifestStore store = GetStore(roots[i]);
                IReadOnlyList<EditorAssetRecord> records = store.Refresh();
                ReconcileInferredRefreshMoves(roots[i], store);
                changed |= ReplaceRootSnapshot(roots[i], records);
                _ = _pendingFullRescanRoots.Remove(roots[i]);
                processed++;
            }
        }

        if (processed > 0)
        {
            AssetBrowserFolderItem[] folders = BuildFolderSnapshot();
            if (!folders.SequenceEqual(_folderSnapshot))
            {
                _folderSnapshot = folders;
                changed = true;
            }
        }

        return changed;
    }

    private void ReconcileInferredRefreshMoves(EditorAssetRootKind root, EditorAssetManifestStore store)
    {
        EditorAssetPathRewrite[] rewrites = [.. store.LastRefreshPathRewrites];
        bool hadAmbiguousIdentityMatches = store.LastRefreshHadAmbiguousIdentityMatches;
        if (rewrites.Length == 0)
        {
            if (hadAmbiguousIdentityMatches)
            {
                _runtimeDiagnostic = $"资产监视器丢失事件后发现 {root} 中存在身份歧义；已重新索引，但无法安全重写旧路径引用。";
            }

            return;
        }

        List<EditorScenePathRewrite> sceneRewrites = [];
        for (int i = 0; i < rewrites.Length; i++)
        {
            EditorAssetPathRewrite rewrite = rewrites[i];
            if (!store.TryReconcileExternalAssetMove(
                rewrite.OldPath,
                rewrite.NewPath,
                _activeScene,
                out _))
            {
                _runtimeDiagnostic = $"资产索引已推断出移动 {rewrite.OldPath} -> {rewrite.NewPath}，但引用同步未完成。";
                continue;
            }

            if (root == EditorAssetRootKind.Content && rewrite.AssetType == EditorAssetType.Scene)
            {
                sceneRewrites.Add(new EditorScenePathRewrite(rewrite.OldPath, rewrite.NewPath));
            }
        }

        if (sceneRewrites.Count > 0 && _sceneAssetMoveService is not null)
        {
            _ = _sceneAssetMoveService.SynchronizeMovedScenePaths(sceneRewrites);
        }

        if (hadAmbiguousIdentityMatches)
        {
            _runtimeDiagnostic = $"资产监视器丢失事件后，已同步可唯一识别的移动；{root} 中仍有身份歧义项无法安全重写引用。";
        }
    }

    private bool TryBuildSimpleFileBatch(int limit, out EditorAssetChange[] batch)
    {
        batch = [];
        if (limit <= 0 || _pendingChanges.Count == 0)
        {
            return false;
        }

        List<EditorAssetChange> candidates = [];
        EditorAssetRootKind? root = null;
        foreach (EditorAssetChange change in _pendingChanges)
        {
            if (candidates.Count >= limit ||
                change.Kind == EditorAssetChangeKind.Renamed ||
                IsDirectoryChange(change) ||
                (root.HasValue && root.Value != change.Path.Root))
            {
                break;
            }

            root ??= change.Path.Root;
            candidates.Add(change);
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        batch = [.. candidates];
        return true;
    }

    private bool IsDirectoryChange(EditorAssetChange change)
    {
        string browserPath = ToBrowserPath(change.Path);
        if (change.Kind == EditorAssetChangeKind.Deleted &&
            _folderSnapshot.Any(folder => string.Equals(folder.Path, browserPath, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        string fullPath = Path.GetFullPath(Path.Combine(
            GetStore(change.Path.Root).ContentRoot,
            change.Path.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        return Directory.Exists(fullPath);
    }

    private bool ApplySimpleFileBatch(IReadOnlyList<EditorAssetChange> changes)
    {
        EditorAssetRootKind root = changes[0].Path.Root;
        EditorAssetRecordSyncResult result = GetStore(root).SynchronizeAssetRecords(
            [.. changes.Select(static change => change.Path.RelativePath)]);
        bool changed = false;
        for (int i = 0; i < result.Upserted.Length; i++)
        {
            changed |= UpsertSnapshot(root, result.Upserted[i]);
        }

        for (int i = 0; i < result.RemovedPaths.Length; i++)
        {
            string browserPath = ToBrowserPath(new EditorAssetPath(root, result.RemovedPaths[i]));
            changed |= RemoveSnapshot(null, browserPath);
            _runtimeDiagnostic = $"检测到外部删除资产 {browserPath}；资产索引已同步，请检查场景中的引用是否仍有效。";
        }

        return changed;
    }

    private bool HasQueuedChangeForRoot(EditorAssetRootKind root)
    {
        foreach (EditorAssetChange change in _pendingChanges)
        {
            if (change.Path.Root == root)
            {
                return true;
            }
        }

        return false;
    }

    private bool ApplyIncrementalChange(EditorAssetChange change)
    {
        EditorAssetManifestStore store = GetStore(change.Path.Root);
        return change.Kind switch
        {
            EditorAssetChangeKind.Created or EditorAssetChangeKind.Changed =>
                store.TryUpsertAssetFromDisk(change.Path.RelativePath, out EditorAssetRecord refreshed) &&
                UpsertSnapshot(change.Path.Root, refreshed),
            EditorAssetChangeKind.Deleted => RemoveDeletedPath(store, change.Path),
            EditorAssetChangeKind.Renamed => change.OldPath is { } oldPath &&
                MoveRenamedPath(store, oldPath, change.Path),
            _ => throw new ArgumentOutOfRangeException(
                nameof(change),
                change.Kind,
                "未知资产增量变更类型。"),
        };
    }

    private bool RemoveDeletedPath(EditorAssetManifestStore store, EditorAssetPath path)
    {
        string browserPath = ToBrowserPath(path);
        bool knownFolder = _folderSnapshot.Any(folder =>
            string.Equals(folder.Path, browserPath, StringComparison.OrdinalIgnoreCase));
        if (knownFolder)
        {
            _ = store.RemoveFolderRecords(path.RelativePath);
            AssetBrowserItem[] retained =
            [
                .. _assetSnapshot.Where(item => !IsSameOrChildPath(item.Path, browserPath)),
            ];
            bool removedFolderAssets = retained.Length != _assetSnapshot.Length;
            _assetSnapshot = retained;
            _runtimeDiagnostic = $"检测到外部删除文件夹 {browserPath}；资产索引已同步，请检查场景中的引用是否仍有效。";
            return removedFolderAssets || knownFolder;
        }

        AssetBrowserItem[] matches =
        [
            .. _assetSnapshot.Where(item => IsSameOrChildPath(item.Path, browserPath)),
        ];
        _ = store.RemoveAssetRecord(path.RelativePath);
        for (int i = 0; i < matches.Length; i++)
        {
            _ = RemoveSnapshot(matches[i].AssetId, matches[i].Path);
        }

        if (matches.Length > 0)
        {
            _runtimeDiagnostic = $"检测到外部删除资产 {browserPath}；资产索引已同步，请检查场景中的引用是否仍有效。";
        }

        return matches.Length > 0;
    }

    private bool MoveRenamedPath(EditorAssetManifestStore store, EditorAssetPath oldPath, EditorAssetPath newPath)
    {
        if (oldPath.Root != newPath.Root)
        {
            return false;
        }

        string targetFullPath = Path.GetFullPath(Path.Combine(
            store.ContentRoot,
            newPath.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (Directory.Exists(targetFullPath))
        {
            EditorScenePathRewrite[] sceneRewrites = BuildScenePathRewrites(oldPath, newPath);
            if (!store.TryReconcileExternalFolderMove(
                oldPath.RelativePath,
                newPath.RelativePath,
                _activeScene,
                out _))
            {
                return false;
            }

            if (oldPath.Root == EditorAssetRootKind.Content &&
                sceneRewrites.Length > 0 &&
                _sceneAssetMoveService is not null)
            {
                _ = _sceneAssetMoveService.SynchronizeMovedScenePaths(sceneRewrites);
            }

            return ReplaceRootSnapshot(oldPath.Root, store.Refresh());
        }

        if (EditorAssetManifestStore.Classify(oldPath.RelativePath) !=
            EditorAssetManifestStore.Classify(newPath.RelativePath))
        {
            _ = store.RemoveAssetRecord(oldPath.RelativePath);
            bool removed = RemoveSnapshot(null, ToBrowserPath(oldPath));
            return store.TryUpsertAssetFromDisk(newPath.RelativePath, out EditorAssetRecord reclassified)
                ? UpsertSnapshot(newPath.Root, reclassified) || removed
                : removed;
        }

        string oldBrowserPath = ToBrowserPath(oldPath);
        if (store.TryReconcileExternalAssetMove(
            oldPath.RelativePath,
            newPath.RelativePath,
            _activeScene,
            out EditorAssetMoveResult move))
        {
            if (oldPath.Root == EditorAssetRootKind.Content &&
                move.Asset.AssetType == EditorAssetType.Scene &&
                _sceneAssetMoveService is not null)
            {
                _ = _sceneAssetMoveService.SynchronizeMovedScenePaths(
                    [new EditorScenePathRewrite(oldPath.RelativePath, newPath.RelativePath)]);
            }

            return UpsertSnapshot(oldPath.Root, move.Asset, oldBrowserPath);
        }

        if (store.TryUpsertAssetFromDisk(newPath.RelativePath, out EditorAssetRecord discovered))
        {
            _ = store.RemoveAssetRecord(oldPath.RelativePath);
            return UpsertSnapshot(newPath.Root, discovered, oldBrowserPath);
        }

        return false;
    }

    private EditorScenePathRewrite[] BuildScenePathRewrites(EditorAssetPath oldPath, EditorAssetPath newPath)
    {
        if (oldPath.Root != EditorAssetRootKind.Content)
        {
            return [];
        }

        string oldBrowserPath = ToBrowserPath(oldPath);
        List<EditorScenePathRewrite> rewrites = [];
        for (int i = 0; i < _assetSnapshot.Length; i++)
        {
            AssetBrowserItem item = _assetSnapshot[i];
            if (item.Kind != AssetBrowserItemKind.Scene ||
                !IsSameOrChildPath(item.Path, oldBrowserPath) ||
                !TryGetRelativePath(item.Path, EditorAssetRootKind.Content, out string currentRelativePath))
            {
                continue;
            }

            string suffix = currentRelativePath.Length == oldPath.RelativePath.Length
                ? string.Empty
                : currentRelativePath[(oldPath.RelativePath.Length + 1)..];
            string nextRelativePath = string.IsNullOrEmpty(suffix)
                ? newPath.RelativePath
                : newPath.RelativePath + "/" + suffix;
            rewrites.Add(new EditorScenePathRewrite(currentRelativePath, nextRelativePath));
        }

        return [.. rewrites];
    }

    private bool ReplaceRootSnapshot(EditorAssetRootKind root, IReadOnlyList<EditorAssetRecord> records)
    {
        List<AssetBrowserItem> next =
        [
            .. _assetSnapshot.Where(item => !IsBrowserItemInRoot(item, root)),
        ];
        AppendRecords(next, root, records);
        AssetBrowserItem[] ordered =
        [
            .. next.OrderBy(static item => item.Path, StringComparer.OrdinalIgnoreCase),
        ];
        if (ordered.SequenceEqual(_assetSnapshot))
        {
            return false;
        }

        _assetSnapshot = ordered;
        return true;
    }

    private void AppendRecords(
        List<AssetBrowserItem> target,
        EditorAssetRootKind root,
        IReadOnlyList<EditorAssetRecord> records)
    {
        for (int i = 0; i < records.Count; i++)
        {
            if (ShouldIncludeRecord(root, records[i]))
            {
                target.Add(BuildBrowserItem(root, records[i]));
            }
        }
    }

    private AssetBrowserItem BuildBrowserItem(EditorAssetRootKind root, EditorAssetRecord record)
    {
        AssetThumbnail? thumbnail = root == EditorAssetRootKind.Content &&
            TryResolveThumbnail(record.LogicalPath, out AssetThumbnail resolved)
                ? resolved
                : null;
        AssetBrowserItemKind kind = MapKind(record.AssetType);
        return new AssetBrowserItem(
            ToBrowserPath(new EditorAssetPath(root, record.LogicalPath)),
            kind,
            record.SizeBytes,
            record.LastModifiedUtc,
            thumbnail,
            record.Id,
            BuildPreviewSummary(root, record, kind, thumbnail),
            BuildDescriptor(root, record, kind));
    }

    private bool UpsertSnapshot(EditorAssetRootKind root, EditorAssetRecord record, string? previousBrowserPath = null)
    {
        if (!ShouldIncludeRecord(root, record))
        {
            return RemoveSnapshot(record.Id, previousBrowserPath ?? ToBrowserPath(new EditorAssetPath(root, record.LogicalPath)));
        }

        AssetBrowserItem item = BuildBrowserItem(root, record);
        List<AssetBrowserItem> next = [.. _assetSnapshot];
        int index = next.FindIndex(candidate =>
            (!string.IsNullOrWhiteSpace(item.AssetId) &&
                string.Equals(candidate.AssetId, item.AssetId, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(previousBrowserPath) &&
                string.Equals(candidate.Path, previousBrowserPath, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(candidate.Path, item.Path, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            if (next[index].Equals(item))
            {
                return false;
            }

            next[index] = item;
        }
        else
        {
            next.Add(item);
        }

        _assetSnapshot = [.. next.OrderBy(static candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)];
        return true;
    }

    private bool RemoveSnapshot(string? assetId, string browserPath)
    {
        AssetBrowserItem[] retained =
        [
            .. _assetSnapshot.Where(item =>
                !(!string.IsNullOrWhiteSpace(assetId) && string.Equals(item.AssetId, assetId, StringComparison.OrdinalIgnoreCase)) &&
                !string.Equals(item.Path, browserPath, StringComparison.OrdinalIgnoreCase)),
        ];
        if (retained.Length == _assetSnapshot.Length)
        {
            return false;
        }

        _assetSnapshot = retained;
        return true;
    }

    private AssetBrowserFolderItem[] BuildFolderSnapshot()
    {
        Dictionary<string, int> folders = new(StringComparer.OrdinalIgnoreCase)
        {
            [string.Empty] = _assetSnapshot.Length,
        };
        AddPhysicalFolders(folders, EditorAssetRootKind.Content, _assets.ContentRoot);
        if (_scriptAssets is not null)
        {
            AddPhysicalFolders(folders, EditorAssetRootKind.ScriptSource, _scriptAssets.ContentRoot);
        }

        for (int i = 0; i < _assetSnapshot.Length; i++)
        {
            string? folder = Path.GetDirectoryName(_assetSnapshot[i].Path)?.Replace('\\', '/');
            while (!string.IsNullOrEmpty(folder))
            {
                folders[folder] = folders.TryGetValue(folder, out int count) ? count + 1 : 1;
                folder = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            }
        }

        return
        [
            .. folders
                .Select(static pair => new AssetBrowserFolderItem(pair.Key, pair.Value))
                .OrderBy(static item => item.Path.Length == 0 ? 0 : 1)
                .ThenBy(static item => item.Path, StringComparer.OrdinalIgnoreCase),
        ];
    }

    private void AddPhysicalFolders(
        Dictionary<string, int> folders,
        EditorAssetRootKind root,
        string physicalRoot)
    {
        if (!Directory.Exists(physicalRoot))
        {
            return;
        }

        string rootPath = _rootedBrowserPaths
            ? root == EditorAssetRootKind.Content
                ? EditorRootedBrowserPath.ContentRootName
                : EditorRootedBrowserPath.ScriptSourceRootName
            : string.Empty;
        if (!folders.ContainsKey(rootPath))
        {
            folders[rootPath] = 0;
        }

        foreach (string directory in Directory.EnumerateDirectories(physicalRoot, "*", SearchOption.AllDirectories))
        {
            if (root == EditorAssetRootKind.Content &&
                _scriptAssets is not null &&
                IsPathInsideRoot(_scriptAssets.ContentRoot, directory))
            {
                continue;
            }

            string relative = Path.GetRelativePath(physicalRoot, directory).Replace('\\', '/');
            string browserPath = _rootedBrowserPaths
                ? EditorRootedBrowserPath.Format(new EditorAssetPath(root, relative))
                : relative;
            if (!folders.ContainsKey(browserPath))
            {
                folders[browserPath] = 0;
            }
        }
    }

    private bool ShouldIncludeRecord(EditorAssetRootKind root, EditorAssetRecord record)
    {
        if (root == EditorAssetRootKind.ScriptSource)
        {
            return record.AssetType == EditorAssetType.Script;
        }

        if (record.AssetType != EditorAssetType.Script || _scriptAssets is null)
        {
            return true;
        }

        string fullPath = Path.GetFullPath(Path.Combine(
            _assets.ContentRoot,
            record.LogicalPath.Replace('/', Path.DirectorySeparatorChar)));
        return !IsPathInsideRoot(_scriptAssets.ContentRoot, fullPath);
    }

    private static bool IsPathInsideRoot(string root, string candidate)
    {
        string normalizedRoot = Path.GetFullPath(root);
        string normalizedCandidate = Path.GetFullPath(candidate);
        string rootWithSeparator = Path.EndsInDirectorySeparator(normalizedRoot)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        return string.Equals(normalizedCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            normalizedCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    public AssetBrowserDeleteResult DeleteAsset(AssetBrowserDeleteRequest request, EditorSceneModel? activeScene = null)
    {
        if (string.IsNullOrWhiteSpace(request.AssetId) || string.IsNullOrWhiteSpace(request.Path))
        {
            return new AssetBrowserDeleteResult(false, false, "删除请求缺少 stable asset id 或 logical path。");
        }

        if (!TryParseRequestPath(request.Path, EditorAssetRootKind.Content, out EditorAssetPath path, out string pathDiagnostic))
        {
            return new AssetBrowserDeleteResult(false, false, pathDiagnostic);
        }

        EditorAssetManifestStore store = GetStore(path.Root);
        if (!store.TryResolveAssetId(request.AssetId, out EditorAssetRecord record))
        {
            return new AssetBrowserDeleteResult(false, false, $"资产 manifest 缺少 stable asset id：{request.AssetId}");
        }

        EditorAssetType requestType = MapKind(request.Kind);
        if (!string.Equals(record.LogicalPath, path.RelativePath, StringComparison.OrdinalIgnoreCase) || record.AssetType != requestType)
        {
            return new AssetBrowserDeleteResult(false, false, $"删除请求与 manifest 不一致：{request.Path} / {request.Kind}。");
        }

        EditorAssetDeleteResult result = store.DeleteAsset(record.LogicalPath, activeScene, request.Confirmed);
        if (result.Deleted)
        {
            _ = RemoveSnapshot(request.AssetId, request.Path);
            _folderSnapshot = BuildFolderSnapshot();
        }

        return new AssetBrowserDeleteResult(result.Deleted, result.RequiresConfirmation, result.Diagnostic);
    }

    public AssetBrowserFolderDeleteResult DeleteFolder(AssetBrowserFolderDeleteRequest request, EditorSceneModel? activeScene = null)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return new AssetBrowserFolderDeleteResult(false, false, "文件夹删除请求缺少 logical path。");
        }

        try
        {
            if (!TryParseRequestPath(request.Path, EditorAssetRootKind.Content, out EditorAssetPath path, out string pathDiagnostic))
            {
                return new AssetBrowserFolderDeleteResult(false, false, pathDiagnostic);
            }

            EditorAssetManifestStore store = GetStore(path.Root);
            IReadOnlyList<EditorAssetRecord> currentAssets = store.ListFolderAssets(path.RelativePath);
            string[] currentIds =
            [
                .. currentAssets
                    .Select(static asset => asset.Id)
                    .Order(StringComparer.OrdinalIgnoreCase),
            ];
            string[] requestIds =
            [
                .. (request.AssetIds ?? [])
                    .Where(static assetId => !string.IsNullOrWhiteSpace(assetId))
                    .Select(static assetId => assetId.Trim())
                    .Order(StringComparer.OrdinalIgnoreCase),
            ];
            if (!currentIds.SequenceEqual(requestIds, StringComparer.OrdinalIgnoreCase))
            {
                return new AssetBrowserFolderDeleteResult(false, false, $"文件夹删除请求与当前资产集合不一致：{request.Path}。");
            }

            EditorAssetFolderDeleteResult result = store.DeleteFolder(path.RelativePath, activeScene, request.Confirmed);
            if (result.Deleted)
            {
                RebuildFullSnapshot();
            }

            return new AssetBrowserFolderDeleteResult(result.Deleted, result.RequiresConfirmation, result.Diagnostic);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            return new AssetBrowserFolderDeleteResult(false, false, ex.Message);
        }
    }

    public EditorAssetBrowserMoveResult MoveAsset(string currentPath, string newPath, EditorSceneModel? activeScene = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPath);
        if (!TryParseRequestPath(currentPath, EditorAssetRootKind.Content, out EditorAssetPath currentAssetPath, out string currentDiagnostic))
        {
            throw new InvalidOperationException(currentDiagnostic);
        }

        if (!TryParseRequestPath(newPath, currentAssetPath.Root, out EditorAssetPath newAssetPath, out string newDiagnostic))
        {
            throw new InvalidOperationException(newDiagnostic);
        }

        if (currentAssetPath.Root != newAssetPath.Root)
        {
            throw new InvalidOperationException("资产不能跨 Content 与 ScriptSource logical root 移动。");
        }

        EditorAssetManifestStore store = GetStore(currentAssetPath.Root);
        EditorAssetRecord current = store.EnsureAsset(currentAssetPath.RelativePath);
        string previousBrowserPath = ToBrowserPath(currentAssetPath);
        EditorAssetType newType = EditorAssetManifestStore.Classify(newAssetPath.RelativePath);
        if (current.AssetType != newType)
        {
            return new EditorAssetBrowserMoveResult(
                false,
                current,
                $"资产移动不能改变类型：{current.LogicalPath} -> {newPath}。");
        }

        if (currentAssetPath.Root == EditorAssetRootKind.Content &&
            current.AssetType == EditorAssetType.Scene &&
            _sceneAssetMoveService is not null)
        {
            EditorSceneAssetMoveResult result = _sceneAssetMoveService.MoveSceneAsset(
                current.LogicalPath,
                newAssetPath.RelativePath,
                activeScene);
            _ = UpsertSnapshot(EditorAssetRootKind.Content, result.AssetMove.Asset, previousBrowserPath);
            _folderSnapshot = BuildFolderSnapshot();
            return new EditorAssetBrowserMoveResult(
                true,
                result.AssetMove.Asset,
                $"已移动 Scene 资产并同步引用：{result.SettingsUpdates.FormatDiagnostic()}");
        }

        EditorAssetMoveResult move = store.MoveAsset(current.LogicalPath, newAssetPath.RelativePath, activeScene);
        _ = UpsertSnapshot(currentAssetPath.Root, move.Asset, previousBrowserPath);
        _folderSnapshot = BuildFolderSnapshot();
        return new EditorAssetBrowserMoveResult(
            true,
            move.Asset,
            move.UpdatedActiveScene || move.UpdatedReferenceDocuments > 0
                ? $"已移动资产并重写引用：active={move.UpdatedActiveScene}, documents={move.UpdatedReferenceDocuments.ToString(CultureInfo.InvariantCulture)}"
                : $"已移动资产：{move.Asset.LogicalPath}");
    }

    public AssetBrowserMoveResult MoveAsset(AssetBrowserMoveRequest request, EditorSceneModel? activeScene = null)
    {
        if (string.IsNullOrWhiteSpace(request.AssetId) ||
            string.IsNullOrWhiteSpace(request.Path) ||
            string.IsNullOrWhiteSpace(request.NewPath))
        {
            return new AssetBrowserMoveResult(false, "移动请求缺少 stable asset id、当前路径或目标路径。");
        }

        if (!TryParseRequestPath(request.Path, EditorAssetRootKind.Content, out EditorAssetPath path, out string pathDiagnostic))
        {
            return new AssetBrowserMoveResult(false, pathDiagnostic);
        }

        if (!TryParseRequestPath(request.NewPath, path.Root, out EditorAssetPath newPath, out string targetDiagnostic))
        {
            return new AssetBrowserMoveResult(false, targetDiagnostic);
        }

        if (path.Root != newPath.Root)
        {
            return new AssetBrowserMoveResult(false, "资产不能跨 Content 与 ScriptSource logical root 移动。");
        }

        EditorAssetManifestStore store = GetStore(path.Root);
        if (!store.TryResolveAssetId(request.AssetId, out EditorAssetRecord record))
        {
            return new AssetBrowserMoveResult(false, $"资产 manifest 缺少 stable asset id：{request.AssetId}");
        }

        EditorAssetType requestType = MapKind(request.Kind);
        if (!string.Equals(record.LogicalPath, path.RelativePath, StringComparison.OrdinalIgnoreCase) || record.AssetType != requestType)
        {
            return new AssetBrowserMoveResult(false, $"移动请求与 manifest 不一致：{request.Path} / {request.Kind}。");
        }

        try
        {
            EditorAssetBrowserMoveResult result = MoveAsset(
                ToBrowserPath(path),
                ToBrowserPath(newPath),
                activeScene);
            return new AssetBrowserMoveResult(result.Succeeded, result.Diagnostic);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            return new AssetBrowserMoveResult(false, ex.Message);
        }
    }

    public AssetBrowserFolderMoveResult MoveFolder(AssetBrowserFolderMoveRequest request, EditorSceneModel? activeScene = null)
    {
        if (string.IsNullOrWhiteSpace(request.Path) || string.IsNullOrWhiteSpace(request.NewPath))
        {
            return new AssetBrowserFolderMoveResult(false, "文件夹移动请求缺少当前路径或目标路径。");
        }

        try
        {
            if (!TryParseRequestPath(request.Path, EditorAssetRootKind.Content, out EditorAssetPath path, out string pathDiagnostic))
            {
                return new AssetBrowserFolderMoveResult(false, pathDiagnostic);
            }

            if (!TryParseRequestPath(request.NewPath, path.Root, out EditorAssetPath newPath, out string targetDiagnostic))
            {
                return new AssetBrowserFolderMoveResult(false, targetDiagnostic);
            }

            if (path.Root != newPath.Root)
            {
                return new AssetBrowserFolderMoveResult(false, "文件夹不能跨 Content 与 ScriptSource logical root 移动。");
            }

            EditorScenePathRewrite[] sceneRewrites = BuildScenePathRewrites(path, newPath);
            EditorAssetFolderMoveResult result = GetStore(path.Root).MoveFolder(
                path.RelativePath,
                newPath.RelativePath,
                activeScene);
            SceneSettingsSyncCounts sceneSettings = path.Root == EditorAssetRootKind.Content &&
                sceneRewrites.Length > 0 &&
                _sceneAssetMoveService is not null
                    ? _sceneAssetMoveService.SynchronizeMovedScenePaths(sceneRewrites)
                    : SceneSettingsSyncCounts.Empty;
            RebuildFullSnapshot();
            string movedAssets = result.MovedAssets.ToString(CultureInfo.InvariantCulture);
            string updatedDocuments = result.UpdatedReferenceDocuments.ToString(CultureInfo.InvariantCulture);
            return new AssetBrowserFolderMoveResult(
                true,
                result.UpdatedActiveScene || result.UpdatedReferenceDocuments > 0 || sceneSettings.Total > 0
                    ? $"已移动文件夹并重写引用：assets={movedAssets}, active={result.UpdatedActiveScene}, documents={updatedDocuments}, settings={sceneSettings.FormatDiagnostic()}"
                    : $"已移动文件夹：{result.LogicalPath} -> {result.NewLogicalPath}，assets={movedAssets}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            return new AssetBrowserFolderMoveResult(false, ex.Message);
        }
    }

    public AssetBrowserCreateResult CreateAsset(AssetBrowserCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return new AssetBrowserCreateResult(false, "资产创建请求缺少 logical path。");
        }

        if (!Enum.IsDefined(request.Kind))
        {
            return new AssetBrowserCreateResult(false, $"未知资产类型：{request.Kind}。");
        }

        if (!TryResolveCreatePath(request, out EditorAssetPath path, out string pathDiagnostic))
        {
            return new AssetBrowserCreateResult(false, pathDiagnostic);
        }

        EditorAssetManifestStore store = GetStore(path.Root);

        if (request.Kind == AssetBrowserItemKind.Folder)
        {
            try
            {
                string folder = store.CreateFolder(path.RelativePath);
                string browserPath = ToBrowserPath(new EditorAssetPath(path.Root, folder));
                _folderSnapshot = BuildFolderSnapshot();
                return new AssetBrowserCreateResult(
                    true,
                    $"已创建文件夹：{browserPath}",
                    null,
                    browserPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
            {
                return new AssetBrowserCreateResult(false, ex.Message);
            }
        }

        EditorAssetType type = MapKind(request.Kind);
        if (!IsCreatableType(type))
        {
            return new AssetBrowserCreateResult(false, $"Project Window 暂不支持直接创建 {request.Kind} 资产。");
        }

        try
        {
            EditorAssetRecord asset = store.CreateAsset(path.RelativePath, type);
            string browserPath = ToBrowserPath(new EditorAssetPath(path.Root, asset.LogicalPath));
            _ = UpsertSnapshot(path.Root, asset);
            _folderSnapshot = BuildFolderSnapshot();
            return new AssetBrowserCreateResult(
                true,
                $"已创建资产：{browserPath}",
                asset.Id,
                browserPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            return new AssetBrowserCreateResult(false, ex.Message);
        }
    }

    public AssetBrowserImportResult ImportAsset(AssetBrowserImportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SourceFullPath) || string.IsNullOrWhiteSpace(request.Path))
        {
            return new AssetBrowserImportResult(false, "资产导入请求缺少源文件或 logical path。");
        }

        if (!Enum.IsDefined(request.Kind))
        {
            return new AssetBrowserImportResult(false, $"未知资产类型：{request.Kind}。");
        }

        if (!TryParseRequestPath(request.Path, EditorAssetRootKind.Content, out EditorAssetPath path, out string pathDiagnostic))
        {
            return new AssetBrowserImportResult(false, pathDiagnostic);
        }

        if (path.Root != EditorAssetRootKind.Content)
        {
            return new AssetBrowserImportResult(false, "Texture / Audio 只能导入 Content logical root。");
        }

        EditorAssetType type = MapKind(request.Kind);
        if (!IsImportableType(type))
        {
            return new AssetBrowserImportResult(false, $"Project Window 暂不支持导入 {request.Kind} 资产。");
        }

        try
        {
            EditorAssetRecord asset = _assets.ImportAsset(request.SourceFullPath, path.RelativePath, type);
            string browserPath = ToBrowserPath(new EditorAssetPath(EditorAssetRootKind.Content, asset.LogicalPath));
            _ = UpsertSnapshot(EditorAssetRootKind.Content, asset);
            _folderSnapshot = BuildFolderSnapshot();
            return new AssetBrowserImportResult(
                true,
                $"已导入资产：{browserPath}",
                asset.Id,
                browserPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            return new AssetBrowserImportResult(false, ex.Message);
        }
    }

    private bool TryResolveCreatePath(
        AssetBrowserCreateRequest request,
        out EditorAssetPath path,
        out string diagnostic)
    {
        EditorAssetRootKind defaultRoot = _rootedBrowserPaths && request.Kind == AssetBrowserItemKind.Script
            ? EditorAssetRootKind.ScriptSource
            : EditorAssetRootKind.Content;
        string candidate = request.Path;
        if (_rootedBrowserPaths &&
            request.Kind == AssetBrowserItemKind.Script &&
            !EditorRootedBrowserPath.TryParse(candidate, out _, out _) &&
            candidate.Replace('\\', '/').StartsWith("scripts/", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate.Replace('\\', '/')["scripts/".Length..];
        }

        if (!TryParseRequestPath(candidate, defaultRoot, out path, out diagnostic))
        {
            return false;
        }

        if (!_rootedBrowserPaths)
        {
            return true;
        }

        if (request.Kind == AssetBrowserItemKind.Script && path.Root != EditorAssetRootKind.ScriptSource)
        {
            diagnostic = "Script 必须创建在 ScriptSource logical root，才能参与编译与 hot reload。";
            return false;
        }

        if (request.Kind is not (AssetBrowserItemKind.Script or AssetBrowserItemKind.Folder) &&
            path.Root != EditorAssetRootKind.Content)
        {
            diagnostic = $"{request.Kind} 必须创建在 Content logical root。";
            return false;
        }

        diagnostic = string.Empty;
        return true;
    }

    private bool TryParseRequestPath(
        string value,
        EditorAssetRootKind defaultRoot,
        out EditorAssetPath path,
        out string diagnostic)
    {
        if (!_rootedBrowserPaths)
        {
            path = new EditorAssetPath(EditorAssetRootKind.Content, value);
            diagnostic = string.Empty;
            return true;
        }

        string candidate = value?.Trim() ?? string.Empty;
        if (string.Equals(candidate, EditorRootedBrowserPath.ContentRootName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate, EditorRootedBrowserPath.ScriptSourceRootName, StringComparison.OrdinalIgnoreCase))
        {
            path = default;
            diagnostic = $"不能把 Project Window logical root 当作资产或可移动文件夹：{candidate}";
            return false;
        }

        if (EditorRootedBrowserPath.TryParse(value, out path, out diagnostic))
        {
            return true;
        }

        if (candidate.Contains('/', StringComparison.Ordinal) || candidate.Contains('\\', StringComparison.Ordinal))
        {
            string firstSegment = candidate.Replace('\\', '/').Split('/', 2)[0];
            if (string.Equals(firstSegment, EditorRootedBrowserPath.ContentRootName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(firstSegment, EditorRootedBrowserPath.ScriptSourceRootName, StringComparison.OrdinalIgnoreCase))
            {
                path = default;
                return false;
            }
        }

        try
        {
            path = EditorRootedBrowserPath.Create(defaultRoot, candidate);
            diagnostic = string.Empty;
            return true;
        }
        catch (InvalidOperationException exception)
        {
            path = default;
            diagnostic = exception.Message;
            return false;
        }
    }

    private EditorAssetManifestStore GetStore(EditorAssetRootKind root)
    {
        return root switch
        {
            EditorAssetRootKind.Content => _assets,
            EditorAssetRootKind.ScriptSource => _scriptAssets ??
                throw new InvalidOperationException("当前 Project Window 数据源未配置 ScriptSource logical root。"),
            _ => throw new ArgumentOutOfRangeException(nameof(root), root, "未知 Editor 资产逻辑根。"),
        };
    }

    private string ToBrowserPath(EditorAssetPath path)
    {
        return _rootedBrowserPaths
            ? EditorRootedBrowserPath.Format(path)
            : path.RelativePath;
    }

    private bool TryGetRelativePath(string browserPath, EditorAssetRootKind root, out string relativePath)
    {
        if (!_rootedBrowserPaths)
        {
            relativePath = browserPath;
            return root == EditorAssetRootKind.Content;
        }

        if (EditorRootedBrowserPath.TryParse(browserPath, out EditorAssetPath parsed, out _) && parsed.Root == root)
        {
            relativePath = parsed.RelativePath;
            return true;
        }

        relativePath = string.Empty;
        return false;
    }

    private bool IsBrowserItemInRoot(AssetBrowserItem item, EditorAssetRootKind root)
    {
        return TryGetRelativePath(item.Path, root, out _);
    }

    private static bool IsSameOrChildPath(string candidate, string rootPath)
    {
        return string.Equals(candidate, rootPath, StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(rootPath + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAssetDatabaseFailure(Exception exception)
    {
        return exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            NotSupportedException;
    }

    private bool TryResolveThumbnail(string logicalPath, out AssetThumbnail thumbnail)
    {
        thumbnail = default;
        return _thumbnailProvider is not null && _thumbnailProvider.TryGetThumbnail(logicalPath, out thumbnail);
    }

    private static AssetBrowserDescriptor BuildDescriptor(
        EditorAssetRootKind root,
        EditorAssetRecord record,
        AssetBrowserItemKind kind)
    {
        string path = record.LogicalPath.Replace('\\', '/');
        string fileName = Path.GetFileName(path);
        string extension = Path.GetExtension(path).ToLowerInvariant();
        AssetBrowserBadge badges = IsTestAsset(path, kind)
            ? AssetBrowserBadge.Test
            : AssetBrowserBadge.None;
        string typeLabel = GetTypeLabel(kind, extension, fileName);
        string purpose = GetPurpose(root, path, kind, extension);
        return new AssetBrowserDescriptor(typeLabel, purpose, badges);
    }

    private static string GetTypeLabel(AssetBrowserItemKind kind, string extension, string fileName)
    {
        return kind switch
        {
            AssetBrowserItemKind.Material when string.Equals(fileName, "reactions.json", StringComparison.OrdinalIgnoreCase) =>
                "材质反应规则",
            AssetBrowserItemKind.Other => extension switch
            {
                ".ttf" or ".otf" or ".woff" or ".woff2" => "字体",
                ".txt" or ".md" => "说明文档",
                ".ini" => "Editor 配置",
                _ => "文件",
            },
            AssetBrowserItemKind.Folder => "文件夹",
            AssetBrowserItemKind.Material => "材质目录",
            AssetBrowserItemKind.Texture => "纹理",
            AssetBrowserItemKind.Audio => "音频片段",
            AssetBrowserItemKind.Scene => "场景",
            AssetBrowserItemKind.Prefab => "Prefab",
            AssetBrowserItemKind.Script => "C# 脚本",
            AssetBrowserItemKind.UiScreen => "UI Screen",
            AssetBrowserItemKind.Json => "JSON 配置",
            _ => kind.ToString(),
        };
    }

    private static string GetPurpose(
        EditorAssetRootKind root,
        string path,
        AssetBrowserItemKind kind,
        string extension)
    {
        return root == EditorAssetRootKind.ScriptSource
            ? "项目运行时 C# 脚本，参与 Editor 热重载与 Player 编译"
            : path.ToLowerInvariant() switch
            {
                "materials.json" => "定义 CA 材质、渲染参数、温度与音频绑定的权威目录",
                "reactions.json" => "定义材质接触、温度与方向性反应规则",
                "startup.json" => "玩家运行时启动配置与启动场景入口",
                "weapons.json" => "武器、弹丸与材料工具的运行时目录",
                "audio/cues.json" => "把稳定音频 Cue handle 映射到音频片段",
                "ui/ui-manifest.json" => "注册 Web-first UI Screen、路径与预加载策略",
                "imgui.ini" => "Editor ImGui 工作台布局配置",
                _ => GetContentAssetPurpose(path, kind, extension),
            };
    }

    private static string GetContentAssetPurpose(
        string path,
        AssetBrowserItemKind kind,
        string extension)
    {
        return kind switch
        {
            AssetBrowserItemKind.Audio when IsPathUnder(path, "audio") =>
                $"运行时音效片段：{HumanizeFileName(path)}",
            AssetBrowserItemKind.UiScreen when IsPathUnder(path, "ui/screens") =>
                $"Web-first 游戏界面：{HumanizeFileName(path)}",
            AssetBrowserItemKind.Other when IsPathUnder(path, "ui/fonts") => extension switch
            {
                ".ttf" or ".otf" or ".woff" or ".woff2" => "游戏 UI 的本地化字体资源",
                _ when string.Equals(Path.GetFileName(path), "OFL.txt", StringComparison.OrdinalIgnoreCase) => "字体开放授权说明",
                _ when string.Equals(Path.GetFileName(path), "SOURCE.txt", StringComparison.OrdinalIgnoreCase) => "字体来源与版本说明",
                _ => "字体配套说明",
            },
            AssetBrowserItemKind.Scene when IsPathUnder(path, "scenes") =>
                IsTestAsset(path, kind)
                    ? GetProbePurpose(path)
                    : "可编辑的世界、GameObject 与 Behaviour authoring 场景",
            AssetBrowserItemKind.Texture when IsPathUnder(path, "maps") =>
                "初始世界材质图；像素颜色映射为模拟材质",
            AssetBrowserItemKind.Texture when IsPathUnder(path, "textures") =>
                $"材质渲染纹理：{HumanizeFileName(path)}",
            AssetBrowserItemKind.Scene => "可编辑场景资产",
            AssetBrowserItemKind.Prefab => "可复用 GameObject authoring 模板",
            AssetBrowserItemKind.Json => "工程运行时配置数据",
            AssetBrowserItemKind.Texture => "渲染或世界构建纹理",
            AssetBrowserItemKind.Audio => "运行时音频片段",
            AssetBrowserItemKind.UiScreen => "Web-first UI Screen 文档",
            AssetBrowserItemKind.Material => "模拟材质或反应定义",
            AssetBrowserItemKind.Script => "项目 C# 脚本",
            AssetBrowserItemKind.Other => "工程辅助资源",
            AssetBrowserItemKind.Folder => "逻辑文件夹",
            _ => "工程资源",
        };
    }

    private static bool IsTestAsset(string path, AssetBrowserItemKind kind)
    {
        string fileName = Path.GetFileName(path);
        return (kind == AssetBrowserItemKind.Scene && fileName.EndsWith("-probe.scene", StringComparison.OrdinalIgnoreCase)) ||
            IsPathUnder(path, "tests") ||
            IsPathUnder(path, "probes");
    }

    private static string GetProbePurpose(string path)
    {
        string fileName = Path.GetFileNameWithoutExtension(path);
        return fileName.ToLowerInvariant() switch
        {
            "empty-window-probe" => "空世界与窗口链路测试场景，不属于默认游戏流程",
            "lava-mine-audio-probe" => "熔岩矿井音频路由与空间音效测试场景",
            "lava-mine-camera-probe" => "熔岩矿井相机跟随与 viewport 测试场景",
            "lava-mine-goal-probe" => "熔岩矿井目标、出口与胜利流程测试场景",
            "lava-mine-health-probe" => "熔岩矿井伤害、生命与重生测试场景",
            "lava-mine-particle-light-probe" => "熔岩矿井粒子、光照与合成测试场景",
            "lava-mine-reaction-probe" => "熔岩矿井材质反应与温度测试场景",
            _ => $"{HumanizeFileName(path)} 测试场景，不属于默认游戏流程",
        };
    }

    private static bool IsPathUnder(string path, string folder)
    {
        return path.StartsWith(folder.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string HumanizeFileName(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        int numericPrefixSeparator = name.IndexOf('_');
        if (numericPrefixSeparator > 0 && name[..numericPrefixSeparator].All(char.IsDigit))
        {
            name = name[(numericPrefixSeparator + 1)..];
        }

        return name.Replace('-', ' ').Replace('_', ' ');
    }

    private static string NormalizeLogicalPathForComparison(string path)
    {
        string normalized = path.Replace('\\', '/').Trim().TrimStart('/');
        return normalized.StartsWith(EditorRootedBrowserPath.ContentRootName + "/", StringComparison.OrdinalIgnoreCase)
            ? normalized[(EditorRootedBrowserPath.ContentRootName.Length + 1)..]
            : normalized;
    }

    private string BuildPreviewSummary(
        EditorAssetRootKind root,
        EditorAssetRecord record,
        AssetBrowserItemKind kind,
        AssetThumbnail? thumbnail)
    {
        return kind switch
        {
            AssetBrowserItemKind.Folder => "文件夹",
            AssetBrowserItemKind.Texture => thumbnail is { } image
                ? $"纹理：{image.Width.ToString(CultureInfo.InvariantCulture)}×{image.Height.ToString(CultureInfo.InvariantCulture)}，{FormatSize(record.SizeBytes)}"
                : $"纹理：{FormatSize(record.SizeBytes)}",
            AssetBrowserItemKind.Audio => $"音频：{FormatSize(record.SizeBytes)}",
            AssetBrowserItemKind.Material => BuildJsonAssetPreview(root, record, "材质定义"),
            AssetBrowserItemKind.Scene => BuildSceneAssetPreview(root, record, "场景"),
            AssetBrowserItemKind.Prefab => BuildSceneAssetPreview(root, record, "Prefab"),
            AssetBrowserItemKind.Script => BuildScriptAssetPreview(root, record),
            AssetBrowserItemKind.UiScreen => BuildUiScreenAssetPreview(root, record),
            AssetBrowserItemKind.Json => BuildJsonAssetPreview(root, record, "JSON"),
            AssetBrowserItemKind.Other => $"文件：{FormatSize(record.SizeBytes)}",
            _ => $"文件：{FormatSize(record.SizeBytes)}",
        };
    }

    private string BuildSceneAssetPreview(EditorAssetRootKind root, EditorAssetRecord record, string label)
    {
        try
        {
            EngineSceneDocument document = EngineSceneDocumentLoader.LoadDocument(ResolveAssetFullPath(root, record.LogicalPath));
            EngineSceneEntityDocument[] entities = document.Entities ?? [];
            int rootCount = 0;
            int behaviourCount = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (!entities[i].ParentId.HasValue)
                {
                    rootCount++;
                }

                behaviourCount += entities[i].Behaviours?.Length ?? 0;
            }

            return $"{label}：{entities.Length.ToString(CultureInfo.InvariantCulture)} 个 GameObject，{rootCount.ToString(CultureInfo.InvariantCulture)} 个根，{behaviourCount.ToString(CultureInfo.InvariantCulture)} 个 Behaviour";
        }
        catch (Exception ex) when (IsPreviewFailure(ex))
        {
            return $"{label}摘要不可用：{FormatSize(record.SizeBytes)}";
        }
    }

    private string BuildJsonAssetPreview(EditorAssetRootKind root, EditorAssetRecord record, string label)
    {
        try
        {
            using FileStream stream = File.OpenRead(ResolveAssetFullPath(root, record.LogicalPath));
            using JsonDocument document = JsonDocument.Parse(stream);
            if (TryBuildKnownJsonPreview(record.LogicalPath, document.RootElement, record.SizeBytes, out string knownPreview))
            {
                return knownPreview;
            }

            string shape = TryCountJsonCollection(document.RootElement, out int count)
                ? $"{count.ToString(CultureInfo.InvariantCulture)} 项"
                : DescribeJsonShape(document.RootElement);
            return $"{label}：{shape}，{FormatSize(record.SizeBytes)}";
        }
        catch (Exception ex) when (IsPreviewFailure(ex))
        {
            return $"{label}摘要不可用：{FormatSize(record.SizeBytes)}";
        }
    }

    private string BuildScriptAssetPreview(EditorAssetRootKind root, EditorAssetRecord record)
    {
        try
        {
            string fullPath = ResolveAssetFullPath(root, record.LogicalPath);
            string? className = TryReadFirstClassName(fullPath);
            return string.IsNullOrWhiteSpace(className)
                ? $"脚本：{FormatSize(record.SizeBytes)}"
                : $"脚本：{className}，{FormatSize(record.SizeBytes)}";
        }
        catch (Exception ex) when (IsPreviewFailure(ex))
        {
            return $"脚本摘要不可用：{FormatSize(record.SizeBytes)}";
        }
    }

    private string BuildUiScreenAssetPreview(EditorAssetRootKind root, EditorAssetRecord record)
    {
        try
        {
            string fullPath = ResolveAssetFullPath(root, record.LogicalPath);
            string? title = TryReadRmlAttribute(fullPath, "title");
            string? screen = TryReadRmlAttribute(fullPath, "data-screen");
            string? contract = TryReadRmlAttribute(fullPath, "data-contract");
            List<string> details = [];
            if (!string.IsNullOrWhiteSpace(title))
            {
                details.Add(title);
            }

            if (!string.IsNullOrWhiteSpace(screen))
            {
                details.Add($"id={screen}");
            }

            if (!string.IsNullOrWhiteSpace(contract))
            {
                details.Add($"contract={contract}");
            }

            details.Add(FormatSize(record.SizeBytes));
            return $"UI Screen：{string.Join("，", details)}";
        }
        catch (Exception ex) when (IsPreviewFailure(ex))
        {
            return $"UI Screen 摘要不可用：{FormatSize(record.SizeBytes)}";
        }
    }

    private string ResolveAssetFullPath(EditorAssetRootKind root, string logicalPath)
    {
        string physicalRoot = GetStore(root).ContentRoot;
        return Path.GetFullPath(Path.Combine(physicalRoot, logicalPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static bool TryCountJsonCollection(JsonElement root, out int count)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            count = root.GetArrayLength();
            return true;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            if (TryGetArrayCount(root, "materials", out count) ||
                TryGetArrayCount(root, "reactions", out count) ||
                TryGetArrayCount(root, "items", out count) ||
                TryGetArrayCount(root, "weapons", out count) ||
                TryGetArrayCount(root, "cues", out count) ||
                TryGetArrayCount(root, "screens", out count))
            {
                return true;
            }

            count = root.EnumerateObject().Count();
            return count > 0;
        }

        count = 0;
        return false;
    }

    private static bool TryBuildKnownJsonPreview(
        string logicalPath,
        JsonElement root,
        long sizeBytes,
        out string preview)
    {
        string normalized = logicalPath.Replace('\\', '/');
        if (string.Equals(normalized, "startup.json", StringComparison.OrdinalIgnoreCase) &&
            root.TryGetProperty("startScene", out JsonElement startScene) &&
            startScene.ValueKind == JsonValueKind.String)
        {
            preview = $"启动配置：场景 {startScene.GetString()}，{FormatSize(sizeBytes)}";
            return true;
        }

        string? property = normalized.ToLowerInvariant() switch
        {
            "materials.json" => "materials",
            "reactions.json" => "reactions",
            "weapons.json" => "weapons",
            "audio/cues.json" => "cues",
            "ui/ui-manifest.json" => "screens",
            _ => null,
        };
        if (property is null ||
            !root.TryGetProperty(property, out JsonElement entries) ||
            entries.ValueKind != JsonValueKind.Array)
        {
            preview = string.Empty;
            return false;
        }

        int count = entries.GetArrayLength();
        if (string.Equals(normalized, "ui/ui-manifest.json", StringComparison.OrdinalIgnoreCase))
        {
            int preloadCount = 0;
            foreach (JsonElement entry in entries.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object &&
                    entry.TryGetProperty("preload", out JsonElement preload) &&
                    preload.ValueKind == JsonValueKind.True)
                {
                    preloadCount++;
                }
            }

            preview = $"UI 清单：{count.ToString(CultureInfo.InvariantCulture)} 个 Screen，{preloadCount.ToString(CultureInfo.InvariantCulture)} 个预加载，{FormatSize(sizeBytes)}";
            return true;
        }

        string label = property switch
        {
            "materials" => "材质目录",
            "reactions" => "反应规则",
            "weapons" => "武器目录",
            "cues" => "音频 Cue 映射",
            _ => "配置",
        };
        preview = $"{label}：{count.ToString(CultureInfo.InvariantCulture)} 项，{FormatSize(sizeBytes)}";
        return true;
    }

    private static bool TryGetArrayCount(JsonElement root, string propertyName, out int count)
    {
        if (root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.Array)
        {
            count = value.GetArrayLength();
            return true;
        }

        count = 0;
        return false;
    }

    private static string DescribeJsonShape(JsonElement root)
    {
        return root.ValueKind switch
        {
            JsonValueKind.Object => "JSON 对象",
            JsonValueKind.Array => "JSON 数组",
            JsonValueKind.String => "JSON 字符串",
            JsonValueKind.Number => "JSON 数字",
            JsonValueKind.True or JsonValueKind.False => "JSON 布尔值",
            JsonValueKind.Null => "JSON null",
            JsonValueKind.Undefined => "JSON 文档",
            _ => "JSON 文档",
        };
    }

    private static string? TryReadFirstClassName(string fullPath)
    {
        foreach (string line in File.ReadLines(fullPath).Take(120))
        {
            string trimmed = line.Trim();
            int classIndex = trimmed.IndexOf("class ", StringComparison.Ordinal);
            if (classIndex < 0)
            {
                continue;
            }

            int start = classIndex + "class ".Length;
            while (start < trimmed.Length && !IsIdentifierStart(trimmed[start]))
            {
                start++;
            }

            int end = start;
            while (end < trimmed.Length && IsIdentifierPart(trimmed[end]))
            {
                end++;
            }

            if (end > start)
            {
                return trimmed[start..end];
            }
        }

        return null;
    }

    private static string? TryReadRmlAttribute(string fullPath, string attributeName)
    {
        foreach (string line in File.ReadLines(fullPath).Take(40))
        {
            int attributeIndex = line.IndexOf(attributeName + "=", StringComparison.OrdinalIgnoreCase);
            if (attributeIndex < 0)
            {
                continue;
            }

            int quoteStart = attributeIndex + attributeName.Length + 1;
            while (quoteStart < line.Length && char.IsWhiteSpace(line[quoteStart]))
            {
                quoteStart++;
            }

            if (quoteStart >= line.Length || (line[quoteStart] != '"' && line[quoteStart] != '\''))
            {
                continue;
            }

            char quote = line[quoteStart];
            int valueStart = quoteStart + 1;
            int valueEnd = line.IndexOf(quote, valueStart);
            if (valueEnd > valueStart)
            {
                return line[valueStart..valueEnd];
            }
        }

        return null;
    }

    private static bool IsIdentifierStart(char value)
    {
        return char.IsLetter(value) || value == '_';
    }

    private static bool IsIdentifierPart(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_';
    }

    private static bool IsPreviewFailure(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or InvalidOperationException;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes.ToString(CultureInfo.InvariantCulture)} B";
        }

        double kib = bytes / 1024d;
        if (kib < 1024d)
        {
            return $"{kib.ToString("0.#", CultureInfo.InvariantCulture)} KiB";
        }

        double mib = kib / 1024d;
        return $"{mib.ToString("0.#", CultureInfo.InvariantCulture)} MiB";
    }

    private static AssetBrowserItemKind MapKind(EditorAssetType type)
    {
        return type switch
        {
            EditorAssetType.Material => AssetBrowserItemKind.Material,
            EditorAssetType.Texture => AssetBrowserItemKind.Texture,
            EditorAssetType.Audio => AssetBrowserItemKind.Audio,
            EditorAssetType.Scene => AssetBrowserItemKind.Scene,
            EditorAssetType.Prefab => AssetBrowserItemKind.Prefab,
            EditorAssetType.Script => AssetBrowserItemKind.Script,
            EditorAssetType.UiScreen => AssetBrowserItemKind.UiScreen,
            EditorAssetType.Json => AssetBrowserItemKind.Json,
            EditorAssetType.Other => AssetBrowserItemKind.Other,
            _ => AssetBrowserItemKind.Other,
        };
    }

    private static EditorAssetType MapKind(AssetBrowserItemKind kind)
    {
        return kind switch
        {
            AssetBrowserItemKind.Material => EditorAssetType.Material,
            AssetBrowserItemKind.Texture => EditorAssetType.Texture,
            AssetBrowserItemKind.Audio => EditorAssetType.Audio,
            AssetBrowserItemKind.Scene => EditorAssetType.Scene,
            AssetBrowserItemKind.Prefab => EditorAssetType.Prefab,
            AssetBrowserItemKind.Script => EditorAssetType.Script,
            AssetBrowserItemKind.UiScreen => EditorAssetType.UiScreen,
            AssetBrowserItemKind.Json => EditorAssetType.Json,
            AssetBrowserItemKind.Folder => EditorAssetType.Other,
            AssetBrowserItemKind.Other => EditorAssetType.Other,
            _ => EditorAssetType.Other,
        };
    }

    private static bool IsCreatableType(EditorAssetType type)
    {
        return type is EditorAssetType.Material or EditorAssetType.Scene or EditorAssetType.Prefab or EditorAssetType.Script or EditorAssetType.UiScreen or EditorAssetType.Json;
    }

    private static bool IsImportableType(EditorAssetType type)
    {
        return type is EditorAssetType.Texture or EditorAssetType.Audio;
    }
}

/// <summary>
/// EditorAssetBrowserMoveResult 数据结构。
/// </summary>
internal sealed record EditorAssetBrowserMoveResult(
    bool Succeeded,
    EditorAssetRecord Asset,
    string Diagnostic);
