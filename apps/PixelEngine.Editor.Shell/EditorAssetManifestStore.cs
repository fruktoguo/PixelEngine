using System.Globalization;
using System.Text.Json;
using PixelEngine.Hosting;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Content 资产类型枚举。
/// </summary>
internal enum EditorAssetType
{
    Material,
    Texture,
    Audio,
    Scene,
    Prefab,
    Script,
    UiScreen,
    Json,
    Other,
}

/// <summary>
/// 资产清单中的一条记录，含稳定 id 与逻辑路径。
/// </summary>
internal readonly record struct EditorAssetRecord(
    string Id,
    string LogicalPath,
    EditorAssetType AssetType,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc);

internal readonly record struct EditorAssetMoveResult(
    EditorAssetRecord Asset,
    int UpdatedReferenceDocuments,
    bool UpdatedActiveScene);

internal readonly record struct EditorAssetFolderMoveResult(
    string LogicalPath,
    string NewLogicalPath,
    int MovedAssets,
    int UpdatedReferenceDocuments,
    bool UpdatedActiveScene);

internal readonly record struct EditorAssetPathRewrite(
    string AssetId,
    string OldPath,
    string NewPath,
    EditorAssetType AssetType);

internal sealed record EditorAssetRecordSyncResult(
    EditorAssetRecord[] Upserted,
    string[] RemovedPaths,
    bool ManifestChanged);

/// <summary>
/// EditorAssetReferenceDocumentMovePlan。
/// </summary>
internal sealed record EditorAssetReferenceDocumentMovePlan(
    string OriginalFullPath,
    string SaveFullPath,
    string OriginalText,
    EditorSceneModel Model);

/// <summary>
/// EditorAssetReferenceDocumentWriteRollback。
/// </summary>
internal sealed record EditorAssetReferenceDocumentWriteRollback(
    string OriginalFullPath,
    string SaveFullPath,
    string OriginalText);

internal readonly record struct EditorAssetDeletePreflight(
    EditorAssetRecord Asset,
    int ReferenceCount,
    int ReferenceDocuments,
    bool ActiveSceneHasReferences,
    IReadOnlyList<string> ReferenceLocations)
{
    public bool CanDelete => ReferenceCount == 0;
}

internal readonly record struct EditorAssetDeleteResult(
    EditorAssetRecord Asset,
    bool Deleted,
    bool RequiresConfirmation,
    EditorAssetDeletePreflight Preflight,
    string Diagnostic);

internal readonly record struct EditorAssetFolderDeletePreflight(
    string LogicalPath,
    int AssetCount,
    int ReferenceCount,
    int ReferenceDocuments,
    bool ActiveSceneHasReferences,
    IReadOnlyList<string> ReferenceLocations)
{
    public bool CanDelete => ReferenceCount == 0;
}

internal readonly record struct EditorAssetFolderDeleteResult(
    string LogicalPath,
    int AssetCount,
    bool Deleted,
    bool RequiresConfirmation,
    EditorAssetFolderDeletePreflight Preflight,
    string Diagnostic);

internal readonly record struct EditorUiManifestScreenEntry(
    string Id,
    string Path,
    bool Preload,
    bool FileExists,
    string? AssetId,
    string LogicalPath);

internal readonly record struct EditorUiManifestSyncResult(
    int RegisteredScreens,
    int ExistingScreens,
    int MissingFiles,
    string Diagnostic);

/// <summary>
/// Content 资产清单的扫描、索引、移动与脚本模板生成。
/// </summary>
internal sealed class EditorAssetManifestStore
{
    public const int CurrentFormatVersion = 1;
    public const string ManifestRelativePath = ".pixelengine/assets.json";

    private readonly record struct RefreshIdentitySignature(
        EditorAssetType AssetType,
        long SizeBytes,
        DateTimeOffset LastModifiedUtc);

    private readonly record struct ScannedAsset(
        string LogicalPath,
        EditorAssetType AssetType,
        long SizeBytes,
        DateTimeOffset LastModifiedUtc)
    {
        public RefreshIdentitySignature Signature => new(AssetType, SizeBytes, LastModifiedUtc);
    }

    private readonly record struct FileRollbackSnapshot(string FullPath, byte[]? Contents);

    private readonly Action<EngineSceneDocument, string> _saveReferenceDocument;

    public EditorAssetManifestStore(EditorProject project)
        : this(
            project?.ProjectRoot ?? throw new ArgumentNullException(nameof(project)),
            project.ContentRootPath)
    {
    }

    public EditorAssetManifestStore(string projectRoot, string contentRoot)
        : this(projectRoot, contentRoot, ManifestRelativePath, referenceDocumentRoot: null)
    {
    }

    public EditorAssetManifestStore(string projectRoot, string contentRoot, string manifestRelativePath)
        : this(projectRoot, contentRoot, manifestRelativePath, referenceDocumentRoot: null)
    {
    }

    public EditorAssetManifestStore(
        string projectRoot,
        string contentRoot,
        string manifestRelativePath,
        string? referenceDocumentRoot,
        Action<EngineSceneDocument, string>? saveReferenceDocument = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestRelativePath);
        ProjectRoot = Path.GetFullPath(projectRoot);
        ContentRoot = Path.IsPathRooted(contentRoot)
            ? Path.GetFullPath(contentRoot)
            : Path.GetFullPath(Path.Combine(ProjectRoot, contentRoot));
        if (Path.IsPathRooted(manifestRelativePath))
        {
            throw new ArgumentException("资产 manifest 路径必须相对工程根目录。", nameof(manifestRelativePath));
        }

        ManifestPath = Path.GetFullPath(Path.Combine(
            ProjectRoot,
            manifestRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        string projectRootWithSeparator = Path.EndsInDirectorySeparator(ProjectRoot)
            ? ProjectRoot
            : ProjectRoot + Path.DirectorySeparatorChar;
        if (!ManifestPath.StartsWith(projectRootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("资产 manifest 路径不能越过工程根目录。", nameof(manifestRelativePath));
        }

        ReferenceDocumentRoot = string.IsNullOrWhiteSpace(referenceDocumentRoot)
            ? ContentRoot
            : Path.IsPathRooted(referenceDocumentRoot)
                ? Path.GetFullPath(referenceDocumentRoot)
                : Path.GetFullPath(Path.Combine(ProjectRoot, referenceDocumentRoot));
        _saveReferenceDocument = saveReferenceDocument ?? EngineSceneDocumentLoader.SaveDocument;
    }

    public string ProjectRoot { get; }

    public string ContentRoot { get; }

    public string ManifestPath { get; }

    public string ReferenceDocumentRoot { get; }

    /// <summary>
    /// 最近一次 manifest 读取或刷新产生的恢复诊断。
    /// </summary>
    public string LastDiagnostic { get; private set; } = string.Empty;

    /// <summary>
    /// 最近一次损坏恢复所隔离的原 manifest 路径。
    /// </summary>
    public string? LastCorruptManifestPath { get; private set; }

    /// <summary>
    /// 最近一次完整刷新根据唯一文件签名推断出的旧路径到新路径映射。
    /// </summary>
    public IReadOnlyList<EditorAssetPathRewrite> LastRefreshPathRewrites { get; private set; } = [];

    /// <summary>
    /// 最近一次完整刷新是否遇到无法安全判定身份的一对多或多对多候选。
    /// </summary>
    public bool LastRefreshHadAmbiguousIdentityMatches { get; private set; }

    /// <summary>
    /// 重新扫描 content 目录，仅在结果变化时写回 manifest，并返回最新资产列表。
    /// </summary>
    public IReadOnlyList<EditorAssetRecord> Refresh()
    {
        ResetManifestDiagnostic();
        return RefreshCore();
    }

    /// <summary>
    /// 从磁盘读取单个 logical path，并在 manifest 中新增或刷新该记录。
    /// </summary>
    public bool TryUpsertAssetFromDisk(string logicalPath, out EditorAssetRecord record)
    {
        ResetManifestDiagnostic();
        string normalized = NormalizeLogicalPath(logicalPath, nameof(logicalPath));
        string fullPath = ResolveFullPath(normalized);
        if (!File.Exists(fullPath))
        {
            record = default;
            return false;
        }

        EditorAssetManifestDocument document = LoadDocumentForIncrementalUpdate();
        EditorAssetRecordDocument[] records = NormalizeRecords(document.Assets);
        int existingIndex = FindRecordIndexByPath(records, normalized);
        HashSet<string> usedIds = CollectUsedIds(records, existingIndex);
        string id = existingIndex >= 0 &&
            !string.IsNullOrWhiteSpace(records[existingIndex].Id) &&
            usedIds.Add(records[existingIndex].Id)
                ? records[existingIndex].Id
                : AllocateAssetId(usedIds);
        EditorAssetRecordDocument refreshed = CreateRecordFromFile(id, normalized, fullPath);
        if (existingIndex >= 0 && ManifestRecordsEqual(records[existingIndex], refreshed))
        {
            record = ToAssetRecord(refreshed);
            return true;
        }

        if (existingIndex >= 0)
        {
            records[existingIndex] = refreshed;
        }
        else
        {
            records = [.. records, refreshed];
        }

        SaveDocument(new EditorAssetManifestDocument
        {
            FormatVersion = CurrentFormatVersion,
            Assets = records,
        });
        record = ToAssetRecord(refreshed);
        return true;
    }

    /// <summary>
    /// 批量同步一组外部文件状态，只读取/写入一次 manifest；目录路径会被忽略。
    /// </summary>
    public EditorAssetRecordSyncResult SynchronizeAssetRecords(IReadOnlyList<string> logicalPaths)
    {
        ArgumentNullException.ThrowIfNull(logicalPaths);
        ResetManifestDiagnostic();
        EditorAssetManifestDocument document = LoadDocumentForIncrementalUpdate();
        EditorAssetRecordDocument[] original = NormalizeRecords(document.Assets);
        Dictionary<string, EditorAssetRecordDocument> byPath = BuildRecordMap(original);
        HashSet<string> usedIds = new(
            original
                .Where(static record => !string.IsNullOrWhiteSpace(record.Id))
                .Select(static record => record.Id),
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, EditorAssetRecord> upserted = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> removed = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < logicalPaths.Count; i++)
        {
            string normalized = NormalizeLogicalPath(logicalPaths[i], nameof(logicalPaths));
            if (!visited.Add(normalized))
            {
                continue;
            }

            string fullPath = ResolveFullPath(normalized);
            if (File.Exists(fullPath))
            {
                string id = byPath.TryGetValue(normalized, out EditorAssetRecordDocument? existing) &&
                    !string.IsNullOrWhiteSpace(existing.Id)
                        ? existing.Id
                        : AllocateAssetId(usedIds);
                _ = usedIds.Add(id);
                EditorAssetRecordDocument refreshed = CreateRecordFromFile(id, normalized, fullPath);
                byPath[normalized] = refreshed;
                upserted[normalized] = ToAssetRecord(refreshed);
                _ = removed.Remove(normalized);
            }
            else if (byPath.Remove(normalized))
            {
                _ = removed.Add(normalized);
                _ = upserted.Remove(normalized);
            }
        }

        EditorAssetManifestDocument next = new()
        {
            FormatVersion = CurrentFormatVersion,
            Assets = [.. byPath.Values],
        };
        bool changed = !ManifestRecordArraysEqual(original, NormalizeRecords(next.Assets));
        if (changed)
        {
            SaveDocument(next);
        }

        return new EditorAssetRecordSyncResult(
            [.. upserted.Values.OrderBy(static record => record.LogicalPath, StringComparer.OrdinalIgnoreCase)],
            [.. removed.Order(StringComparer.OrdinalIgnoreCase)],
            changed);
    }

    /// <summary>
    /// 仅移除指定 logical path 的 manifest 记录，不删除磁盘文件或重写引用。
    /// </summary>
    public bool RemoveAssetRecord(string logicalPath)
    {
        ResetManifestDiagnostic();
        string normalized = NormalizeLogicalPath(logicalPath, nameof(logicalPath));
        EditorAssetManifestDocument document = LoadDocumentForIncrementalUpdate();
        EditorAssetRecordDocument[] records = NormalizeRecords(document.Assets);
        int index = FindRecordIndexByPath(records, normalized);
        if (index < 0)
        {
            return false;
        }

        List<EditorAssetRecordDocument> retained = new(records.Length - 1);
        for (int i = 0; i < records.Length; i++)
        {
            if (i != index)
            {
                retained.Add(records[i]);
            }
        }

        SaveDocument(new EditorAssetManifestDocument
        {
            FormatVersion = CurrentFormatVersion,
            Assets = [.. retained],
        });
        return true;
    }

    /// <summary>
    /// 外部文件夹已删除后，一次性移除其全部 manifest 记录；不再对每个文件重复写 manifest。
    /// </summary>
    public int RemoveFolderRecords(string logicalFolderPath)
    {
        ResetManifestDiagnostic();
        string normalized = NormalizeLogicalPath(logicalFolderPath, nameof(logicalFolderPath));
        EditorAssetManifestDocument document = LoadDocumentForIncrementalUpdate();
        EditorAssetRecordDocument[] records = NormalizeRecords(document.Assets);
        List<EditorAssetRecordDocument> retained = new(records.Length);
        int removed = 0;
        for (int i = 0; i < records.Length; i++)
        {
            if (IsUnderLogicalFolder(records[i].LogicalPath, normalized))
            {
                removed++;
            }
            else
            {
                retained.Add(records[i]);
            }
        }

        if (removed > 0)
        {
            SaveDocument(new EditorAssetManifestDocument
            {
                FormatVersion = CurrentFormatVersion,
                Assets = [.. retained],
            });
        }

        return removed;
    }

    /// <summary>
    /// 在外部已完成文件 rename/move 后，同步 manifest 与引用并保留 stable asset id。
    /// </summary>
    public bool TryMoveAssetRecordFromDisk(
        string currentLogicalPath,
        string newLogicalPath,
        out EditorAssetRecord record)
    {
        bool moved = TryReconcileExternalAssetMove(
            currentLogicalPath,
            newLogicalPath,
            activeScene: null,
            out EditorAssetMoveResult result);
        record = moved ? result.Asset : default;
        return moved;
    }

    /// <summary>
    /// 对 FileSystemWatcher 已观察到的外部资产移动执行完整引用同步；失败时尽量把物理文件恢复到旧路径。
    /// </summary>
    public bool TryReconcileExternalAssetMove(
        string currentLogicalPath,
        string newLogicalPath,
        EditorSceneModel? activeScene,
        out EditorAssetMoveResult result)
    {
        ResetManifestDiagnostic();
        string current = NormalizeLogicalPath(currentLogicalPath, nameof(currentLogicalPath));
        string next = NormalizeLogicalPath(newLogicalPath, nameof(newLogicalPath));
        if (string.Equals(current, next, StringComparison.OrdinalIgnoreCase))
        {
            bool upserted = TryUpsertAssetFromDisk(next, out EditorAssetRecord sameAsset);
            result = upserted
                ? new EditorAssetMoveResult(sameAsset, 0, false)
                : default;
            return upserted;
        }

        EditorAssetType currentType = Classify(current);
        EditorAssetType nextType = Classify(next);
        if (currentType != nextType)
        {
            throw new InvalidOperationException($"资产 manifest 增量移动不能改变类型：{currentType} -> {nextType}。");
        }

        string targetFullPath = ResolveFullPath(next);
        if (!File.Exists(targetFullPath))
        {
            result = default;
            return false;
        }

        EditorAssetManifestDocument originalManifest = LoadDocumentForIncrementalUpdate();
        EditorAssetRecordDocument[] records = NormalizeRecords(originalManifest.Assets);
        int sourceIndex = FindRecordIndexByPath(records, current);
        int targetIndex = FindRecordIndexByPath(records, next);
        if (targetIndex >= 0 && targetIndex != sourceIndex)
        {
            if (sourceIndex >= 0)
            {
                throw new InvalidOperationException($"资产 manifest 已存在目标路径：{next}");
            }

            EditorAssetRecord existingTarget = ToAssetRecord(records[targetIndex]);
            return ReconcileExternalMoveReferences(
                current,
                next,
                existingTarget,
                originalManifest,
                manifestChanged: false,
                activeScene,
                out result);
        }

        if (sourceIndex < 0)
        {
            result = default;
            return false;
        }

        EditorAssetRecordDocument moved = CreateRecordFromFile(records[sourceIndex].Id, next, targetFullPath);
        records[sourceIndex] = moved;
        EditorAssetManifestDocument movedManifest = new()
        {
            FormatVersion = CurrentFormatVersion,
            Assets = records,
        };
        return ReconcileExternalMoveReferences(
            current,
            next,
            ToAssetRecord(moved),
            originalManifest,
            manifestChanged: true,
            activeScene,
            out result,
            movedManifest);
    }

    private bool ReconcileExternalMoveReferences(
        string current,
        string next,
        EditorAssetRecord movedAsset,
        EditorAssetManifestDocument originalManifest,
        bool manifestChanged,
        EditorSceneModel? activeScene,
        out EditorAssetMoveResult result,
        EditorAssetManifestDocument? movedManifest = null)
    {
        string sourceFullPath = ResolveFullPath(current);
        string targetFullPath = ResolveFullPath(next);
        IReadOnlyList<EditorAssetReferenceDocumentMovePlan> referenceDocuments =
            LoadReferenceDocumentExternalMovePlans(sourceFullPath, targetFullPath);
        List<EditorAssetReferenceDocumentWriteRollback> writtenReferenceDocuments = [];
        try
        {
            if (manifestChanged)
            {
                SaveDocument(movedManifest ?? throw new InvalidOperationException("外部资产移动缺少目标 manifest。"));
            }

            RewriteUiManifestScreenPaths(
                [new EditorAssetPathRewrite(movedAsset.Id, current, next, movedAsset.AssetType)],
                writtenReferenceDocuments);
            int updatedDocuments = RewriteReferencesInReferenceDocuments(
                referenceDocuments,
                current,
                next,
                movedAsset.Id,
                movedAsset.AssetType,
                writtenReferenceDocuments);
            bool updatedActiveScene = activeScene is not null &&
                RewriteReferences(activeScene, current, next, movedAsset.Id, movedAsset.AssetType);
            result = new EditorAssetMoveResult(movedAsset, updatedDocuments, updatedActiveScene);
            return true;
        }
        catch
        {
            if (manifestChanged)
            {
                RollBackMove(sourceFullPath, targetFullPath, originalManifest, writtenReferenceDocuments);
            }
            else
            {
                RestoreReferenceDocuments(writtenReferenceDocuments);
            }

            throw;
        }
    }

    /// <summary>
    /// 对 FileSystemWatcher 已观察到的外部文件夹移动执行一次 manifest 与引用批量同步。
    /// </summary>
    public bool TryReconcileExternalFolderMove(
        string currentFolderPath,
        string newFolderPath,
        EditorSceneModel? activeScene,
        out EditorAssetFolderMoveResult result)
    {
        ResetManifestDiagnostic();
        string current = NormalizeLogicalPath(currentFolderPath, nameof(currentFolderPath));
        string next = NormalizeLogicalPath(newFolderPath, nameof(newFolderPath));
        string targetFullPath = ResolveFullPath(next);
        if (!Directory.Exists(targetFullPath))
        {
            result = default;
            return false;
        }

        EditorAssetManifestDocument originalManifest = LoadDocumentForIncrementalUpdate();
        EditorAssetPathRewrite[] rewrites = BuildFolderAssetRewrites(originalManifest, current, next);
        bool manifestChanged = rewrites.Length > 0;
        if (!manifestChanged)
        {
            rewrites = BuildAlreadyMovedFolderAssetRewrites(originalManifest, current, next);
        }

        if (rewrites.Length == 0)
        {
            result = new EditorAssetFolderMoveResult(current, next, 0, 0, false);
            return true;
        }

        EditorAssetManifestDocument movedManifest = manifestChanged
            ? ReplaceFolderLogicalPathPrefix(originalManifest, rewrites)
            : originalManifest;
        string sourceFullPath = ResolveFullPath(current);
        IReadOnlyList<EditorAssetReferenceDocumentMovePlan> referenceDocuments =
            LoadReferenceDocumentExternalFolderMovePlans(sourceFullPath, targetFullPath, next);
        List<EditorAssetReferenceDocumentWriteRollback> writtenReferenceDocuments = [];
        try
        {
            if (manifestChanged)
            {
                SaveDocument(movedManifest);
            }

            RewriteUiManifestScreenPaths(rewrites, writtenReferenceDocuments);
            int updatedDocuments = RewriteFolderReferencesInReferenceDocuments(
                referenceDocuments,
                rewrites,
                writtenReferenceDocuments);
            bool updatedActiveScene = false;
            if (activeScene is not null)
            {
                for (int i = 0; i < rewrites.Length; i++)
                {
                    updatedActiveScene |= RewriteReferences(
                        activeScene,
                        rewrites[i].OldPath,
                        rewrites[i].NewPath,
                        rewrites[i].AssetId,
                        rewrites[i].AssetType);
                }
            }

            result = new EditorAssetFolderMoveResult(
                current,
                next,
                rewrites.Length,
                updatedDocuments,
                updatedActiveScene);
            return true;
        }
        catch
        {
            if (manifestChanged)
            {
                RollBackFolderMove(sourceFullPath, targetFullPath, originalManifest, writtenReferenceDocuments);
            }
            else
            {
                RestoreReferenceDocuments(writtenReferenceDocuments);
            }

            throw;
        }
    }

    public bool TryResolveAssetId(string? assetId, out EditorAssetRecord record)
    {
        if (string.IsNullOrWhiteSpace(assetId))
        {
            record = default;
            return false;
        }

        IReadOnlyList<EditorAssetRecord> records = Refresh();
        for (int i = 0; i < records.Count; i++)
        {
            if (string.Equals(records[i].Id, assetId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                record = records[i];
                return true;
            }
        }

        record = default;
        return false;
    }

    public bool TryResolveLogicalPath(string logicalPath, out EditorAssetRecord record)
    {
        string normalized = NormalizeLogicalPath(logicalPath, nameof(logicalPath));
        IReadOnlyList<EditorAssetRecord> records = Refresh();
        for (int i = 0; i < records.Count; i++)
        {
            if (string.Equals(records[i].LogicalPath, normalized, StringComparison.OrdinalIgnoreCase))
            {
                record = records[i];
                return true;
            }
        }

        record = default;
        return false;
    }

    public EditorAssetRecord EnsureAsset(string logicalPath)
    {
        string normalized = NormalizeLogicalPath(logicalPath, nameof(logicalPath));
        string fullPath = ResolveFullPath(normalized);
        return !File.Exists(fullPath)
            ? throw new FileNotFoundException("资产文件不存在，无法登记到 manifest。", fullPath)
            : TryResolveLogicalPath(normalized, out EditorAssetRecord record)
            ? record
            : throw new InvalidOperationException($"无法登记资产：{normalized}");
    }

    public IReadOnlyList<EditorAssetRecord> ListFolderAssets(string logicalFolderPath)
    {
        string normalized = NormalizeLogicalPath(logicalFolderPath, nameof(logicalFolderPath));
        return CollectFolderAssets(normalized);
    }

    public IReadOnlyList<EditorUiManifestScreenEntry> ListUiManifestScreens()
    {
        string manifestPath = Path.Combine(ContentRoot, "ui", "ui-manifest.json");
        EditorUiManifestDocument document = LoadUiManifestDocument(manifestPath);
        EditorUiManifestScreenDocument[] screens = NormalizeUiScreens(document.Screens);
        Dictionary<string, EditorAssetRecord> assetsByPath = BuildAssetRecordPathMap(Refresh());
        List<EditorUiManifestScreenEntry> entries = new(screens.Length);
        for (int i = 0; i < screens.Length; i++)
        {
            string logicalPath = "ui/" + screens[i].Path;
            bool exists = File.Exists(Path.Combine(ContentRoot, "ui", screens[i].Path.Replace('/', Path.DirectorySeparatorChar)));
            _ = assetsByPath.TryGetValue(logicalPath, out EditorAssetRecord asset);
            entries.Add(new EditorUiManifestScreenEntry(
                screens[i].Id,
                screens[i].Path,
                screens[i].Preload,
                exists,
                string.IsNullOrWhiteSpace(asset.Id) ? null : asset.Id,
                logicalPath));
        }

        return [.. entries.OrderBy(static entry => entry.Id, StringComparer.OrdinalIgnoreCase)];
    }

    public EditorUiManifestSyncResult SyncUiManifestScreens()
    {
        string uiRoot = Path.Combine(ContentRoot, "ui");
        string manifestPath = Path.Combine(uiRoot, "ui-manifest.json");
        _ = Directory.CreateDirectory(uiRoot);
        EditorUiManifestDocument document = LoadUiManifestDocument(manifestPath);
        EditorUiManifestScreenDocument[] screens = NormalizeUiScreens(document.Screens);
        Dictionary<string, EditorUiManifestScreenDocument> byPath = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> usedIds = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < screens.Length; i++)
        {
            byPath[screens[i].Path] = screens[i];
            _ = usedIds.Add(screens[i].Id);
        }

        IReadOnlyList<EditorAssetRecord> records = Refresh();
        List<EditorUiManifestScreenDocument> updated = [.. screens];
        int registered = 0;
        for (int i = 0; i < records.Count; i++)
        {
            EditorAssetRecord record = records[i];
            if (record.AssetType != EditorAssetType.UiScreen ||
                !TryBuildUiScreenManifestEntry(record.LogicalPath, out string candidateId, out string screenPath) ||
                byPath.ContainsKey(screenPath))
            {
                continue;
            }

            string id = AllocateUiScreenId(candidateId, usedIds);
            EditorUiManifestScreenDocument screen = new()
            {
                Id = id,
                Path = screenPath,
                Preload = true,
            };
            updated.Add(screen);
            byPath[screen.Path] = screen;
            registered++;
        }

        int missing = CountMissingUiManifestScreens(updated);
        if (registered > 0)
        {
            SaveUiManifestDocument(manifestPath, new EditorUiManifestDocument
            {
                Screens = [.. updated],
                Images = document.Images,
            });
        }

        int existing = updated.Count - registered;
        return new EditorUiManifestSyncResult(
            registered,
            existing,
            missing,
            registered == 0
                ? $"UI manifest 已同步：existing={existing.ToString(CultureInfo.InvariantCulture)}, missing={missing.ToString(CultureInfo.InvariantCulture)}"
                : $"UI manifest 已登记 {registered.ToString(CultureInfo.InvariantCulture)} 个 screen：existing={existing.ToString(CultureInfo.InvariantCulture)}, missing={missing.ToString(CultureInfo.InvariantCulture)}");
    }

    public bool TrySetUiManifestScreenPreload(string screenId, bool preload, out string diagnostic)
    {
        if (string.IsNullOrWhiteSpace(screenId))
        {
            diagnostic = "UI screen id 不能为空。";
            return false;
        }

        string manifestPath = Path.Combine(ContentRoot, "ui", "ui-manifest.json");
        EditorUiManifestDocument document = LoadUiManifestDocument(manifestPath);
        EditorUiManifestScreenDocument[] screens = NormalizeUiScreens(document.Screens);
        for (int i = 0; i < screens.Length; i++)
        {
            if (!string.Equals(screens[i].Id, screenId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            screens[i] = new EditorUiManifestScreenDocument
            {
                Id = screens[i].Id,
                Path = screens[i].Path,
                Preload = preload,
            };
            SaveUiManifestDocument(manifestPath, new EditorUiManifestDocument
            {
                Screens = screens,
                Images = document.Images,
            });
            diagnostic = $"已更新 UI screen {screens[i].Id} preload={preload}。";
            return true;
        }

        diagnostic = $"UI manifest 缺少 screen id：{screenId}";
        return false;
    }

    public EditorAssetRecord CreateAsset(string logicalPath, EditorAssetType assetType, string? textContents = null)
    {
        string normalized = NormalizeLogicalPath(logicalPath, nameof(logicalPath));
        EditorAssetType classified = Classify(normalized);
        if (assetType != EditorAssetType.Other && classified != assetType)
        {
            throw new InvalidOperationException($"资产路径 {normalized} 的类型 {classified} 与请求类型 {assetType} 不一致。");
        }

        string fullPath = ResolveFullPath(normalized);
        if (File.Exists(fullPath))
        {
            throw new InvalidOperationException($"资产已存在：{normalized}");
        }

        FileRollbackSnapshot assetManifestSnapshot = CaptureFileSnapshot(ManifestPath);
        FileRollbackSnapshot? uiManifestSnapshot = assetType == EditorAssetType.UiScreen
            ? CaptureFileSnapshot(GetUiManifestPath())
            : null;
        try
        {
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                _ = Directory.CreateDirectory(directory);
            }

            WriteDefaultAsset(fullPath, normalized, assetType, textContents);
            EditorAssetRecord asset = EnsureAsset(normalized);
            if (assetType == EditorAssetType.UiScreen)
            {
                UpsertUiScreenManifestEntry(normalized);
            }

            return asset;
        }
        catch (Exception operationException)
        {
            Exception? rollbackException = TryRollBackCreatedAsset(
                fullPath,
                assetManifestSnapshot,
                uiManifestSnapshot);
            if (rollbackException is not null)
            {
                throw new AggregateException(
                    "资产创建失败，且 manifest 回滚未完整完成。",
                    operationException,
                    rollbackException);
            }

            throw;
        }
    }

    public EditorAssetRecord ImportAsset(string sourceFullPath, string logicalPath, EditorAssetType assetType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFullPath);
        string sourcePath = Path.GetFullPath(sourceFullPath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("导入源文件不存在。", sourcePath);
        }

        string normalized = NormalizeLogicalPath(logicalPath, nameof(logicalPath));
        EditorAssetType classified = Classify(normalized);
        if (assetType != EditorAssetType.Other && classified != assetType)
        {
            throw new InvalidOperationException($"导入目标 {normalized} 的类型 {classified} 与请求类型 {assetType} 不一致。");
        }

        string targetPath = ResolveFullPath(normalized);
        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("导入源文件与目标路径相同。");
        }

        if (File.Exists(targetPath) || Directory.Exists(targetPath))
        {
            throw new InvalidOperationException($"目标资产已存在：{normalized}");
        }

        FileRollbackSnapshot assetManifestSnapshot = CaptureFileSnapshot(ManifestPath);
        FileRollbackSnapshot? uiManifestSnapshot = assetType == EditorAssetType.UiScreen
            ? CaptureFileSnapshot(GetUiManifestPath())
            : null;
        try
        {
            string? targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDirectory))
            {
                _ = Directory.CreateDirectory(targetDirectory);
            }

            File.Copy(sourcePath, targetPath, overwrite: false);
            EditorAssetRecord asset = EnsureAsset(normalized);
            if (assetType == EditorAssetType.UiScreen)
            {
                UpsertUiScreenManifestEntry(normalized);
            }

            return asset;
        }
        catch (Exception operationException)
        {
            Exception? rollbackException = TryRollBackCreatedAsset(
                targetPath,
                assetManifestSnapshot,
                uiManifestSnapshot);
            if (rollbackException is not null)
            {
                throw new AggregateException(
                    "资产导入失败，且 manifest 回滚未完整完成。",
                    operationException,
                    rollbackException);
            }

            throw;
        }
    }

    public string CreateFolder(string logicalFolderPath)
    {
        string normalized = NormalizeLogicalPath(logicalFolderPath, nameof(logicalFolderPath));
        string fullPath = ResolveFullPath(normalized);
        if (File.Exists(fullPath))
        {
            throw new InvalidOperationException($"目标路径已存在同名资产文件：{normalized}");
        }

        if (Directory.Exists(fullPath))
        {
            throw new InvalidOperationException($"文件夹已存在：{normalized}");
        }

        _ = Directory.CreateDirectory(fullPath);
        return normalized;
    }

    /// <summary>
    /// 移动资产文件并同步 manifest 与场景/预制体中的引用编码。
    /// </summary>
    public EditorAssetMoveResult MoveAsset(string currentLogicalPath, string newLogicalPath, EditorSceneModel? activeScene = null)
    {
        // 文件移动 + manifest 更新 + 引用文档重写，失败时整体回滚
        string current = NormalizeLogicalPath(currentLogicalPath, nameof(currentLogicalPath));
        string next = NormalizeLogicalPath(newLogicalPath, nameof(newLogicalPath));
        if (string.Equals(current, next, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("资产移动目标与源路径相同。");
        }

        EditorAssetRecord source = EnsureAsset(current);
        EditorAssetType nextType = Classify(next);
        if (nextType != source.AssetType)
        {
            throw new InvalidOperationException($"资产移动不能改变类型：{source.AssetType} -> {nextType}。");
        }

        string sourceFullPath = ResolveFullPath(current);
        string targetFullPath = ResolveFullPath(next);
        if (File.Exists(targetFullPath))
        {
            throw new InvalidOperationException($"目标资产已存在：{next}");
        }

        string? targetDirectory = Path.GetDirectoryName(targetFullPath);
        if (!string.IsNullOrEmpty(targetDirectory))
        {
            _ = Directory.CreateDirectory(targetDirectory);
        }

        EditorAssetManifestDocument originalManifest = LoadDocument();
        EditorAssetManifestDocument movedManifest = ReplaceRecordLogicalPath(originalManifest, source.Id, next);
        IReadOnlyList<EditorAssetReferenceDocumentMovePlan> referenceDocuments = LoadReferenceDocumentMovePlans(sourceFullPath, targetFullPath);

        List<EditorAssetReferenceDocumentWriteRollback> writtenReferenceDocuments = [];
        File.Move(sourceFullPath, targetFullPath);
        try
        {
            SaveDocument(movedManifest);
            RewriteUiManifestScreenPaths(
                [new EditorAssetPathRewrite(source.Id, current, next, source.AssetType)],
                writtenReferenceDocuments);
            int updatedDocuments = RewriteReferencesInReferenceDocuments(
                referenceDocuments,
                current,
                next,
                source.Id,
                source.AssetType,
                writtenReferenceDocuments);
            EditorAssetRecord moved = EnsureAsset(next);
            bool updatedActiveScene = activeScene is not null && RewriteReferences(activeScene, current, next, source.Id, source.AssetType);
            return new EditorAssetMoveResult(moved, updatedDocuments, updatedActiveScene);
        }
        catch
        {
            RollBackMove(sourceFullPath, targetFullPath, originalManifest, writtenReferenceDocuments);
            throw;
        }
    }

    public EditorAssetFolderMoveResult MoveFolder(string currentFolderPath, string newFolderPath, EditorSceneModel? activeScene = null)
    {
        string current = NormalizeLogicalPath(currentFolderPath, nameof(currentFolderPath));
        string next = NormalizeLogicalPath(newFolderPath, nameof(newFolderPath));
        if (string.Equals(current, next, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("文件夹移动目标与源路径相同。");
        }

        if (IsUnderLogicalFolder(next, current))
        {
            throw new InvalidOperationException($"文件夹不能移动到自身子目录：{current} -> {next}");
        }

        string sourceFullPath = ResolveFullPath(current);
        string targetFullPath = ResolveFullPath(next);
        if (!Directory.Exists(sourceFullPath))
        {
            throw new DirectoryNotFoundException($"文件夹不存在：{current}");
        }

        if (File.Exists(targetFullPath) || Directory.Exists(targetFullPath))
        {
            throw new InvalidOperationException($"目标路径已存在：{next}");
        }

        EditorAssetManifestDocument originalManifest = LoadDocument();
        EditorAssetPathRewrite[] rewrites = BuildFolderAssetRewrites(originalManifest, current, next);
        EditorAssetManifestDocument movedManifest = ReplaceFolderLogicalPathPrefix(originalManifest, rewrites);
        IReadOnlyList<EditorAssetReferenceDocumentMovePlan> referenceDocuments = LoadReferenceDocumentFolderMovePlans(sourceFullPath, targetFullPath, current);
        string? targetParent = Path.GetDirectoryName(targetFullPath);
        if (!string.IsNullOrEmpty(targetParent))
        {
            _ = Directory.CreateDirectory(targetParent);
        }

        List<EditorAssetReferenceDocumentWriteRollback> writtenReferenceDocuments = [];
        Directory.Move(sourceFullPath, targetFullPath);
        try
        {
            SaveDocument(movedManifest);
            RewriteUiManifestScreenPaths(rewrites, writtenReferenceDocuments);
            int updatedDocuments = RewriteFolderReferencesInReferenceDocuments(referenceDocuments, rewrites, writtenReferenceDocuments);
            bool updatedActiveScene = false;
            if (activeScene is not null)
            {
                for (int i = 0; i < rewrites.Length; i++)
                {
                    updatedActiveScene |= RewriteReferences(
                        activeScene,
                        rewrites[i].OldPath,
                        rewrites[i].NewPath,
                        rewrites[i].AssetId,
                        rewrites[i].AssetType);
                }
            }

            return new EditorAssetFolderMoveResult(current, next, rewrites.Length, updatedDocuments, updatedActiveScene);
        }
        catch
        {
            RollBackFolderMove(sourceFullPath, targetFullPath, originalManifest, writtenReferenceDocuments);
            throw;
        }
    }

    public EditorAssetDeletePreflight PreflightDeleteAsset(string logicalPath, EditorSceneModel? activeScene = null)
    {
        string normalized = NormalizeLogicalPath(logicalPath, nameof(logicalPath));
        EditorAssetRecord asset = EnsureAsset(normalized);
        return BuildDeletePreflight(asset, activeScene);
    }

    public EditorAssetDeleteResult DeleteAsset(string logicalPath, EditorSceneModel? activeScene = null, bool confirmed = false)
    {
        string normalized = NormalizeLogicalPath(logicalPath, nameof(logicalPath));
        EditorAssetRecord asset = EnsureAsset(normalized);
        EditorAssetDeletePreflight preflight = BuildDeletePreflight(asset, activeScene);
        if (!preflight.CanDelete)
        {
            return new EditorAssetDeleteResult(
                asset,
                false,
                false,
                preflight,
                BuildDeleteBlockedDiagnostic(preflight));
        }

        if (!confirmed)
        {
            return new EditorAssetDeleteResult(
                asset,
                false,
                true,
                preflight,
                BuildDeleteConfirmationDiagnostic(preflight));
        }

        string fullPath = ResolveFullPath(asset.LogicalPath);
        File.Delete(fullPath);
        SaveDocument(RemoveRecord(LoadDocument(), asset.Id));
        return new EditorAssetDeleteResult(
            asset,
            true,
            false,
            preflight,
            $"已删除资产 {asset.LogicalPath}。");
    }

    public EditorAssetFolderDeletePreflight PreflightDeleteFolder(string logicalFolderPath, EditorSceneModel? activeScene = null)
    {
        string normalized = NormalizeLogicalPath(logicalFolderPath, nameof(logicalFolderPath));
        string fullPath = ResolveFullPath(normalized);
        return !Directory.Exists(fullPath)
            ? throw new DirectoryNotFoundException($"文件夹不存在：{normalized}")
            : BuildFolderDeletePreflight(normalized, CollectFolderAssets(normalized), activeScene);
    }

    public EditorAssetFolderDeleteResult DeleteFolder(string logicalFolderPath, EditorSceneModel? activeScene = null, bool confirmed = false)
    {
        string normalized = NormalizeLogicalPath(logicalFolderPath, nameof(logicalFolderPath));
        string fullPath = ResolveFullPath(normalized);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"文件夹不存在：{normalized}");
        }

        IReadOnlyList<EditorAssetRecord> assets = CollectFolderAssets(normalized);
        EditorAssetFolderDeletePreflight preflight = BuildFolderDeletePreflight(normalized, assets, activeScene);
        if (!preflight.CanDelete)
        {
            return new EditorAssetFolderDeleteResult(
                normalized,
                assets.Count,
                false,
                false,
                preflight,
                BuildFolderDeleteBlockedDiagnostic(preflight));
        }

        if (!confirmed)
        {
            return new EditorAssetFolderDeleteResult(
                normalized,
                assets.Count,
                false,
                true,
                preflight,
                BuildFolderDeleteConfirmationDiagnostic(preflight));
        }

        Directory.Delete(fullPath, recursive: true);
        SaveDocument(RemoveRecords(LoadDocument(), [.. assets.Select(static asset => asset.Id)]));
        RemoveUiManifestScreenEntries(assets);
        return new EditorAssetFolderDeleteResult(
            normalized,
            assets.Count,
            true,
            false,
            preflight,
            $"已删除文件夹 {normalized}，包含 {assets.Count.ToString(CultureInfo.InvariantCulture)} 个资产。");
    }

    internal static EditorAssetType Classify(string logicalPath)
    {
        string fileName = Path.GetFileName(logicalPath);
        string extension = Path.GetExtension(logicalPath).ToLowerInvariant();
        return string.Equals(fileName, "materials.json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "reactions.json", StringComparison.OrdinalIgnoreCase)
            ? EditorAssetType.Material
            : extension switch
            {
                ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" or ".webp" => EditorAssetType.Texture,
                ".wav" or ".ogg" or ".flac" or ".mp3" => EditorAssetType.Audio,
                ".scene" or ".world" => EditorAssetType.Scene,
                ".prefab" => EditorAssetType.Prefab,
                ".cs" => EditorAssetType.Script,
                ".xhtml" or ".html" when IsUnderLogicalFolder(logicalPath, "ui/screens") => EditorAssetType.UiScreen,
                ".json" => EditorAssetType.Json,
                _ => EditorAssetType.Other,
            };
    }

    // 扫描 content 目录并与 manifest 合并，尽量保留已有 stable asset id
    private IReadOnlyList<EditorAssetRecord> RefreshCore()
    {
        EditorAssetManifestDocument document = LoadDocument(
            out bool recoveredCorruptManifest,
            out bool requiresCanonicalRewrite);
        Dictionary<string, EditorAssetRecordDocument> byPath = BuildRecordMap(document.Assets);
        List<ScannedAsset> scanned = [];
        if (Directory.Exists(ContentRoot))
        {
            foreach (string fullPath in Directory.EnumerateFiles(ContentRoot, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
            {
                string logicalPath = NormalizeLogicalPath(Path.GetRelativePath(ContentRoot, fullPath), "content file");
                FileInfo info = new(fullPath);
                scanned.Add(new ScannedAsset(
                    logicalPath,
                    Classify(logicalPath),
                    info.Length,
                    info.LastWriteTimeUtc));
            }
        }

        string?[] assignedIds = new string?[scanned.Count];
        HashSet<string> assignedIdsSet = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> reservedOrAllocatedIds = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> oldIdCounts = new(StringComparer.OrdinalIgnoreCase);
        foreach (EditorAssetRecordDocument existing in byPath.Values)
        {
            if (string.IsNullOrWhiteSpace(existing.Id))
            {
                continue;
            }

            string id = existing.Id.Trim();
            _ = reservedOrAllocatedIds.Add(id);
            oldIdCounts[id] = oldIdCounts.TryGetValue(id, out int count) ? count + 1 : 1;
        }

        HashSet<string> diskPaths = new(StringComparer.OrdinalIgnoreCase);
        List<int> unmatchedDiskIndices = [];
        for (int i = 0; i < scanned.Count; i++)
        {
            ScannedAsset asset = scanned[i];
            _ = diskPaths.Add(asset.LogicalPath);
            if (!byPath.TryGetValue(asset.LogicalPath, out EditorAssetRecordDocument? existing))
            {
                unmatchedDiskIndices.Add(i);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(existing.Id) && assignedIdsSet.Add(existing.Id))
            {
                assignedIds[i] = existing.Id;
            }
        }

        Dictionary<RefreshIdentitySignature, List<EditorAssetRecordDocument>> missingOldBySignature = [];
        foreach (EditorAssetRecordDocument existing in byPath.Values)
        {
            if (diskPaths.Contains(existing.LogicalPath))
            {
                continue;
            }

            RefreshIdentitySignature signature = new(
                existing.AssetType,
                existing.SizeBytes,
                existing.LastModifiedUtc);
            if (!missingOldBySignature.TryGetValue(signature, out List<EditorAssetRecordDocument>? candidates))
            {
                candidates = [];
                missingOldBySignature.Add(signature, candidates);
            }

            candidates.Add(existing);
        }

        Dictionary<RefreshIdentitySignature, List<int>> unmatchedDiskBySignature = [];
        for (int i = 0; i < unmatchedDiskIndices.Count; i++)
        {
            int scannedIndex = unmatchedDiskIndices[i];
            RefreshIdentitySignature signature = scanned[scannedIndex].Signature;
            if (!unmatchedDiskBySignature.TryGetValue(signature, out List<int>? candidates))
            {
                candidates = [];
                unmatchedDiskBySignature.Add(signature, candidates);
            }

            candidates.Add(scannedIndex);
        }

        List<EditorAssetPathRewrite> inferredRewrites = [];
        bool ambiguousIdentityMatches = false;
        foreach ((RefreshIdentitySignature signature, List<int> diskCandidates) in unmatchedDiskBySignature)
        {
            if (!missingOldBySignature.TryGetValue(signature, out List<EditorAssetRecordDocument>? oldCandidates))
            {
                continue;
            }

            if (diskCandidates.Count != 1 || oldCandidates.Count != 1)
            {
                ambiguousIdentityMatches = true;
                continue;
            }

            string id = oldCandidates[0].Id;
            if (!string.IsNullOrWhiteSpace(id) &&
                oldIdCounts.TryGetValue(id, out int idCount) &&
                idCount == 1 &&
                assignedIdsSet.Add(id))
            {
                assignedIds[diskCandidates[0]] = id;
                ScannedAsset newAsset = scanned[diskCandidates[0]];
                inferredRewrites.Add(new EditorAssetPathRewrite(
                    id,
                    oldCandidates[0].LogicalPath,
                    newAsset.LogicalPath,
                    newAsset.AssetType));
            }
        }

        List<EditorAssetRecordDocument> refreshed = new(scanned.Count);
        for (int i = 0; i < scanned.Count; i++)
        {
            ScannedAsset asset = scanned[i];
            string id = assignedIds[i] ?? AllocateAssetId(reservedOrAllocatedIds);
            _ = assignedIdsSet.Add(id);
            refreshed.Add(new EditorAssetRecordDocument
            {
                Id = id,
                LogicalPath = asset.LogicalPath,
                AssetType = asset.AssetType,
                SizeBytes = asset.SizeBytes,
                LastModifiedUtc = asset.LastModifiedUtc,
            });
        }

        EditorAssetManifestDocument normalized = new()
        {
            FormatVersion = CurrentFormatVersion,
            Assets = [.. refreshed.OrderBy(static item => item.LogicalPath, StringComparer.OrdinalIgnoreCase)],
        };
        if (recoveredCorruptManifest || requiresCanonicalRewrite || !ManifestDocumentsEqual(document, normalized))
        {
            SaveDocument(normalized);
            if (recoveredCorruptManifest)
            {
                LastDiagnostic = LastDiagnostic.Replace(
                    "将从 content 目录重建",
                    "已从 content 目录重建",
                    StringComparison.Ordinal);
            }
        }

        LastRefreshPathRewrites = [.. inferredRewrites.OrderBy(static rewrite => rewrite.OldPath, StringComparer.OrdinalIgnoreCase)];
        LastRefreshHadAmbiguousIdentityMatches = ambiguousIdentityMatches;

        return [.. normalized.Assets.Select(static item => new EditorAssetRecord(
            item.Id,
            item.LogicalPath,
            item.AssetType,
            item.SizeBytes,
            item.LastModifiedUtc))];
    }

    private string ResolveFullPath(string logicalPath)
    {
        string normalized = NormalizeLogicalPath(logicalPath, nameof(logicalPath));
        string fullPath = Path.GetFullPath(Path.Combine(ContentRoot, normalized));
        string rootWithSeparator = ContentRoot.EndsWith(Path.DirectorySeparatorChar)
            ? ContentRoot
            : ContentRoot + Path.DirectorySeparatorChar;
        bool insideContentRoot = string.Equals(fullPath, ContentRoot, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        return insideContentRoot
            ? fullPath
            : throw new InvalidOperationException($"资产路径越过 content 根目录：{logicalPath}");
    }

    private EditorAssetManifestDocument LoadDocument()
    {
        return LoadDocument(out _, out _);
    }

    private EditorAssetManifestDocument LoadDocumentForIncrementalUpdate()
    {
        EditorAssetManifestDocument document = LoadDocument(
            out bool recoveredCorruptManifest,
            out _);
        if (!recoveredCorruptManifest)
        {
            return document;
        }

        _ = RefreshCore();
        return LoadDocument();
    }

    private EditorAssetManifestDocument LoadDocument(
        out bool recoveredCorruptManifest,
        out bool requiresCanonicalRewrite)
    {
        recoveredCorruptManifest = false;
        requiresCanonicalRewrite = false;
        if (!File.Exists(ManifestPath))
        {
            requiresCanonicalRewrite = true;
            return new EditorAssetManifestDocument { FormatVersion = CurrentFormatVersion, Assets = [] };
        }

        try
        {
            string json = File.ReadAllText(ManifestPath);
            EditorAssetManifestDocument document = JsonSerializer.Deserialize(
                    json,
                    EditorShellJsonContext.Default.EditorAssetManifestDocument) ??
                throw new JsonException("资产 manifest 为空或格式无效。");
            if (document.FormatVersion != CurrentFormatVersion)
            {
                throw new NotSupportedException($"不支持的资产 manifest 版本：{document.FormatVersion}。");
            }

            EditorAssetRecordDocument[] normalizedAssets = NormalizeRecords(document.Assets);
            requiresCanonicalRewrite = !ManifestRecordArraysEqual(document.Assets, normalizedAssets);
            return new EditorAssetManifestDocument
            {
                FormatVersion = CurrentFormatVersion,
                Assets = normalizedAssets,
            };
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            string corruptPath = QuarantineCorruptManifest();
            recoveredCorruptManifest = true;
            requiresCanonicalRewrite = true;
            LastCorruptManifestPath = corruptPath;
            LastDiagnostic = $"资产 manifest 已损坏并隔离到 {corruptPath}；将从 content 目录重建。原因：{exception.Message}";
            return new EditorAssetManifestDocument { FormatVersion = CurrentFormatVersion, Assets = [] };
        }
    }

    private void SaveDocument(EditorAssetManifestDocument document)
    {
        string? directory = Path.GetDirectoryName(ManifestPath);
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        EditorAssetManifestDocument normalized = new()
        {
            FormatVersion = CurrentFormatVersion,
            Assets = NormalizeRecords(document.Assets),
        };
        string json = JsonSerializer.Serialize(
            normalized,
            EditorShellJsonContext.Default.EditorAssetManifestDocument);
        EditorAtomicTextFile.WriteAllText(ManifestPath, json);
    }

    private string QuarantineCorruptManifest()
    {
        string directory = Path.GetDirectoryName(ManifestPath) ?? ProjectRoot;
        string fileName = Path.GetFileNameWithoutExtension(ManifestPath);
        string extension = Path.GetExtension(ManifestPath);
        string corruptPath = Path.Combine(
            directory,
            $"{fileName}.corrupt-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}{extension}");
        File.Move(ManifestPath, corruptPath, overwrite: false);
        return corruptPath;
    }

    private void ResetManifestDiagnostic()
    {
        LastDiagnostic = string.Empty;
        LastCorruptManifestPath = null;
        LastRefreshPathRewrites = [];
        LastRefreshHadAmbiguousIdentityMatches = false;
    }

    private void UpsertUiScreenManifestEntry(string logicalPath)
    {
        if (!TryBuildUiScreenManifestEntry(logicalPath, out string screenId, out string screenPath))
        {
            return;
        }

        string manifestPath = GetUiManifestPath();
        string uiRoot = Path.GetDirectoryName(manifestPath)!;
        _ = Directory.CreateDirectory(uiRoot);
        EditorUiManifestDocument document = LoadUiManifestDocument(manifestPath);
        EditorUiManifestScreenDocument[] screens = NormalizeUiScreens(document.Screens);
        for (int i = 0; i < screens.Length; i++)
        {
            EditorUiManifestScreenDocument screen = screens[i];
            bool sameId = string.Equals(screen.Id, screenId, StringComparison.OrdinalIgnoreCase);
            bool samePath = string.Equals(screen.Path, screenPath, StringComparison.OrdinalIgnoreCase);
            if (sameId && samePath)
            {
                SaveUiManifestDocument(manifestPath, new EditorUiManifestDocument
                {
                    Screens = screens,
                    Images = document.Images,
                });
                return;
            }

            if (sameId)
            {
                throw new InvalidOperationException($"UI manifest 已存在同名 screen id：{screenId}");
            }
        }

        EditorUiManifestScreenDocument[] updated =
        [
            .. screens,
            new EditorUiManifestScreenDocument
            {
                Id = screenId,
                Path = screenPath,
                Preload = true,
            },
        ];
        SaveUiManifestDocument(manifestPath, new EditorUiManifestDocument
        {
            Screens = [.. updated.OrderBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)],
            Images = document.Images,
        });
    }

    private string GetUiManifestPath()
    {
        return Path.Combine(ContentRoot, "ui", "ui-manifest.json");
    }

    private static FileRollbackSnapshot CaptureFileSnapshot(string fullPath)
    {
        return new FileRollbackSnapshot(
            fullPath,
            File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null);
    }

    private static Exception? TryRollBackCreatedAsset(
        string assetFullPath,
        FileRollbackSnapshot assetManifestSnapshot,
        FileRollbackSnapshot? uiManifestSnapshot)
    {
        List<Exception>? failures = null;
        TryRollBack(
            () =>
            {
                if (File.Exists(assetFullPath))
                {
                    File.Delete(assetFullPath);
                }
            },
            ref failures);
        TryRollBack(() => RestoreFileSnapshot(assetManifestSnapshot), ref failures);
        if (uiManifestSnapshot is FileRollbackSnapshot snapshot)
        {
            TryRollBack(() => RestoreFileSnapshot(snapshot), ref failures);
        }

        return failures is null
            ? null
            : new AggregateException("回滚资产文件或 manifest 时发生错误。", failures);
    }

    private static void TryRollBack(Action rollback, ref List<Exception>? failures)
    {
        try
        {
            rollback();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failures ??= [];
            failures.Add(exception);
        }
    }

    private static void RestoreFileSnapshot(FileRollbackSnapshot snapshot)
    {
        if (snapshot.Contents is null)
        {
            if (File.Exists(snapshot.FullPath))
            {
                File.Delete(snapshot.FullPath);
            }

            return;
        }

        string? directory = Path.GetDirectoryName(snapshot.FullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(snapshot.FullPath, snapshot.Contents);
    }

    private static EditorUiManifestDocument LoadUiManifestDocument(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return new EditorUiManifestDocument { Screens = [], Images = [] };
        }

        try
        {
            string json = File.ReadAllText(manifestPath);
            return JsonSerializer.Deserialize(
                    json,
                    EditorShellJsonContext.Default.EditorUiManifestDocument) ??
                new EditorUiManifestDocument { Screens = [], Images = [] };
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"UI manifest 格式无效：{manifestPath}", ex);
        }
    }

    private static void SaveUiManifestDocument(string manifestPath, EditorUiManifestDocument document)
    {
        string json = JsonSerializer.Serialize(
            new EditorUiManifestDocument
            {
                Screens = NormalizeUiScreens(document.Screens),
                Images = document.Images ?? [],
            },
            EditorShellJsonContext.Default.EditorUiManifestDocument);
        File.WriteAllText(manifestPath, json);
    }

    private static EditorUiManifestScreenDocument[] NormalizeUiScreens(EditorUiManifestScreenDocument[]? screens)
    {
        if (screens is null || screens.Length == 0)
        {
            return [];
        }

        Dictionary<string, EditorUiManifestScreenDocument> byPath = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < screens.Length; i++)
        {
            EditorUiManifestScreenDocument screen = screens[i];
            if (string.IsNullOrWhiteSpace(screen.Id) || string.IsNullOrWhiteSpace(screen.Path))
            {
                continue;
            }

            byPath[screen.Path.Trim().Replace('\\', '/')] = new EditorUiManifestScreenDocument
            {
                Id = screen.Id.Trim(),
                Path = screen.Path.Trim().Replace('\\', '/'),
                Preload = screen.Preload,
            };
        }

        return [.. byPath.Values.OrderBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)];
    }

    private static bool ManifestDocumentsEqual(
        EditorAssetManifestDocument left,
        EditorAssetManifestDocument right)
    {
        return left.FormatVersion == right.FormatVersion &&
            ManifestRecordArraysEqual(left.Assets, right.Assets);
    }

    private static bool ManifestRecordsEqual(
        EditorAssetRecordDocument left,
        EditorAssetRecordDocument right)
    {
        return string.Equals(left.Id, right.Id, StringComparison.Ordinal) &&
            string.Equals(left.LogicalPath, right.LogicalPath, StringComparison.Ordinal) &&
            left.AssetType == right.AssetType &&
            left.SizeBytes == right.SizeBytes &&
            left.LastModifiedUtc == right.LastModifiedUtc;
    }

    private static bool ManifestRecordArraysEqual(
        EditorAssetRecordDocument[]? left,
        EditorAssetRecordDocument[]? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            EditorAssetRecordDocument? leftRecord = left[i];
            EditorAssetRecordDocument? rightRecord = right[i];
            if (leftRecord is null ||
                rightRecord is null ||
                !string.Equals(leftRecord.Id, rightRecord.Id, StringComparison.Ordinal) ||
                !string.Equals(leftRecord.LogicalPath, rightRecord.LogicalPath, StringComparison.Ordinal) ||
                leftRecord.AssetType != rightRecord.AssetType ||
                leftRecord.SizeBytes != rightRecord.SizeBytes ||
                leftRecord.LastModifiedUtc != rightRecord.LastModifiedUtc)
            {
                return false;
            }
        }

        return true;
    }

    private static int FindRecordIndexByPath(
        EditorAssetRecordDocument[] records,
        string logicalPath)
    {
        for (int i = 0; i < records.Length; i++)
        {
            if (string.Equals(records[i].LogicalPath, logicalPath, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static HashSet<string> CollectUsedIds(
        EditorAssetRecordDocument[] records,
        int excludedIndex)
    {
        HashSet<string> usedIds = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < records.Length; i++)
        {
            if (i != excludedIndex && !string.IsNullOrWhiteSpace(records[i].Id))
            {
                _ = usedIds.Add(records[i].Id);
            }
        }

        return usedIds;
    }

    private static EditorAssetRecordDocument CreateRecordFromFile(
        string id,
        string logicalPath,
        string fullPath)
    {
        FileInfo info = new(fullPath);
        return new EditorAssetRecordDocument
        {
            Id = id,
            LogicalPath = logicalPath,
            AssetType = Classify(logicalPath),
            SizeBytes = info.Length,
            LastModifiedUtc = info.LastWriteTimeUtc,
        };
    }

    private static EditorAssetRecord ToAssetRecord(EditorAssetRecordDocument document)
    {
        return new EditorAssetRecord(
            document.Id,
            document.LogicalPath,
            document.AssetType,
            document.SizeBytes,
            document.LastModifiedUtc);
    }

    private static EditorAssetRecordDocument[] NormalizeRecords(EditorAssetRecordDocument[]? records)
    {
        if (records is null || records.Length == 0)
        {
            return [];
        }

        Dictionary<string, EditorAssetRecordDocument> unique = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < records.Length; i++)
        {
            EditorAssetRecordDocument record = records[i] ??
                throw new InvalidOperationException($"资产 manifest 第 {i} 条记录为空。");

            if (string.IsNullOrWhiteSpace(record.Id) || string.IsNullOrWhiteSpace(record.LogicalPath))
            {
                continue;
            }

            string logicalPath = NormalizeLogicalPath(record.LogicalPath, nameof(record.LogicalPath));
            unique[logicalPath] = new EditorAssetRecordDocument
            {
                Id = record.Id.Trim(),
                LogicalPath = logicalPath,
                AssetType = Classify(logicalPath),
                SizeBytes = Math.Max(0, record.SizeBytes),
                LastModifiedUtc = record.LastModifiedUtc,
            };
        }

        return [.. unique.Values.OrderBy(static item => item.LogicalPath, StringComparer.OrdinalIgnoreCase)];
    }

    private static Dictionary<string, EditorAssetRecordDocument> BuildRecordMap(EditorAssetRecordDocument[]? records)
    {
        Dictionary<string, EditorAssetRecordDocument> byPath = new(StringComparer.OrdinalIgnoreCase);
        EditorAssetRecordDocument[] normalized = NormalizeRecords(records);
        for (int i = 0; i < normalized.Length; i++)
        {
            byPath[normalized[i].LogicalPath] = normalized[i];
        }

        return byPath;
    }

    private static Dictionary<string, EditorAssetRecord> BuildAssetRecordPathMap(IReadOnlyList<EditorAssetRecord> records)
    {
        Dictionary<string, EditorAssetRecord> byPath = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < records.Count; i++)
        {
            byPath[records[i].LogicalPath] = records[i];
        }

        return byPath;
    }

    private static EditorAssetManifestDocument ReplaceRecordLogicalPath(EditorAssetManifestDocument document, string assetId, string newLogicalPath)
    {
        EditorAssetRecordDocument[] records = NormalizeRecords(document.Assets);
        bool replaced = false;
        for (int i = 0; i < records.Length; i++)
        {
            if (!string.Equals(records[i].Id, assetId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            records[i] = new EditorAssetRecordDocument
            {
                Id = records[i].Id,
                LogicalPath = newLogicalPath,
                AssetType = Classify(newLogicalPath),
                SizeBytes = records[i].SizeBytes,
                LastModifiedUtc = records[i].LastModifiedUtc,
            };
            replaced = true;
            break;
        }

        return !replaced
            ? throw new InvalidOperationException($"资产 manifest 缺少 asset id：{assetId}")
            : new EditorAssetManifestDocument { FormatVersion = CurrentFormatVersion, Assets = records };
    }

    private static EditorAssetPathRewrite[] BuildFolderAssetRewrites(EditorAssetManifestDocument document, string currentFolderPath, string newFolderPath)
    {
        EditorAssetRecordDocument[] records = NormalizeRecords(document.Assets);
        List<EditorAssetPathRewrite> rewrites = [];
        for (int i = 0; i < records.Length; i++)
        {
            EditorAssetRecordDocument record = records[i];
            if (!IsUnderLogicalFolder(record.LogicalPath, currentFolderPath))
            {
                continue;
            }

            string movedPath = MoveLogicalPath(record.LogicalPath, currentFolderPath, newFolderPath);
            EditorAssetType movedType = Classify(movedPath);
            if (movedType != record.AssetType)
            {
                throw new InvalidOperationException($"文件夹移动会改变资产类型：{record.LogicalPath} {record.AssetType} -> {movedPath} {movedType}。");
            }

            rewrites.Add(new EditorAssetPathRewrite(record.Id, record.LogicalPath, movedPath, record.AssetType));
        }

        return [.. rewrites.OrderBy(static item => item.OldPath, StringComparer.OrdinalIgnoreCase)];
    }

    private static EditorAssetPathRewrite[] BuildAlreadyMovedFolderAssetRewrites(
        EditorAssetManifestDocument document,
        string currentFolderPath,
        string newFolderPath)
    {
        EditorAssetRecordDocument[] records = NormalizeRecords(document.Assets);
        List<EditorAssetPathRewrite> rewrites = [];
        for (int i = 0; i < records.Length; i++)
        {
            EditorAssetRecordDocument record = records[i];
            if (!IsUnderLogicalFolder(record.LogicalPath, newFolderPath))
            {
                continue;
            }

            string oldPath = MoveLogicalPath(record.LogicalPath, newFolderPath, currentFolderPath);
            if (Classify(oldPath) != record.AssetType)
            {
                continue;
            }

            rewrites.Add(new EditorAssetPathRewrite(record.Id, oldPath, record.LogicalPath, record.AssetType));
        }

        return [.. rewrites.OrderBy(static item => item.OldPath, StringComparer.OrdinalIgnoreCase)];
    }

    private static EditorAssetManifestDocument ReplaceFolderLogicalPathPrefix(
        EditorAssetManifestDocument document,
        IReadOnlyList<EditorAssetPathRewrite> rewrites)
    {
        EditorAssetRecordDocument[] records = NormalizeRecords(document.Assets);
        Dictionary<string, EditorAssetPathRewrite> byId = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < rewrites.Count; i++)
        {
            byId[rewrites[i].AssetId] = rewrites[i];
        }

        for (int i = 0; i < records.Length; i++)
        {
            if (!byId.TryGetValue(records[i].Id, out EditorAssetPathRewrite rewrite))
            {
                continue;
            }

            records[i] = new EditorAssetRecordDocument
            {
                Id = records[i].Id,
                LogicalPath = rewrite.NewPath,
                AssetType = rewrite.AssetType,
                SizeBytes = records[i].SizeBytes,
                LastModifiedUtc = records[i].LastModifiedUtc,
            };
        }

        return new EditorAssetManifestDocument { FormatVersion = CurrentFormatVersion, Assets = records };
    }

    private static EditorAssetManifestDocument RemoveRecord(EditorAssetManifestDocument document, string assetId)
    {
        EditorAssetRecordDocument[] records = NormalizeRecords(document.Assets);
        List<EditorAssetRecordDocument> retained = new(records.Length);
        bool removed = false;
        for (int i = 0; i < records.Length; i++)
        {
            if (string.Equals(records[i].Id, assetId, StringComparison.OrdinalIgnoreCase))
            {
                removed = true;
                continue;
            }

            retained.Add(records[i]);
        }

        return !removed
            ? throw new InvalidOperationException($"资产 manifest 缺少 asset id：{assetId}")
            : new EditorAssetManifestDocument { FormatVersion = CurrentFormatVersion, Assets = [.. retained] };
    }

    private static EditorAssetManifestDocument RemoveRecords(EditorAssetManifestDocument document, IReadOnlyList<string> assetIds)
    {
        if (assetIds.Count == 0)
        {
            return document;
        }

        HashSet<string> removedIds = new(assetIds, StringComparer.OrdinalIgnoreCase);
        EditorAssetRecordDocument[] records = NormalizeRecords(document.Assets);
        List<EditorAssetRecordDocument> retained = new(records.Length);
        for (int i = 0; i < records.Length; i++)
        {
            if (removedIds.Contains(records[i].Id))
            {
                continue;
            }

            retained.Add(records[i]);
        }

        return new EditorAssetManifestDocument { FormatVersion = CurrentFormatVersion, Assets = [.. retained] };
    }

    private IReadOnlyList<EditorAssetRecord> CollectFolderAssets(string logicalFolderPath)
    {
        IReadOnlyList<EditorAssetRecord> records = Refresh();
        List<EditorAssetRecord> assets = [];
        for (int i = 0; i < records.Count; i++)
        {
            if (IsUnderLogicalFolder(records[i].LogicalPath, logicalFolderPath))
            {
                assets.Add(records[i]);
            }
        }

        return [.. assets.OrderBy(static asset => asset.LogicalPath, StringComparer.OrdinalIgnoreCase)];
    }

    private EditorAssetDeletePreflight BuildDeletePreflight(EditorAssetRecord asset, EditorSceneModel? activeScene)
    {
        return BuildDeletePreflight(asset, activeScene, ignoredReferenceFolder: null);
    }

    private EditorAssetDeletePreflight BuildDeletePreflight(EditorAssetRecord asset, EditorSceneModel? activeScene, string? ignoredReferenceFolder)
    {
        List<string> locations = [];
        int documents = CollectReferenceDocuments(asset, locations, ignoredReferenceFolder);
        bool activeSceneHasReferences = false;
        if (activeScene is not null)
        {
            int before = locations.Count;
            CollectReferenceLocations(activeScene, "active scene", asset, locations);
            activeSceneHasReferences = locations.Count != before;
        }

        return new EditorAssetDeletePreflight(asset, locations.Count, documents, activeSceneHasReferences, locations);
    }

    private EditorAssetFolderDeletePreflight BuildFolderDeletePreflight(
        string logicalFolderPath,
        IReadOnlyList<EditorAssetRecord> assets,
        EditorSceneModel? activeScene)
    {
        List<string> locations = [];
        int documents = 0;
        bool activeSceneHasReferences = false;
        for (int i = 0; i < assets.Count; i++)
        {
            documents += CollectReferenceDocuments(assets[i], locations, logicalFolderPath);
            if (activeScene is not null)
            {
                int before = locations.Count;
                CollectReferenceLocations(activeScene, "active scene", assets[i], locations);
                activeSceneHasReferences |= locations.Count != before;
            }
        }

        return new EditorAssetFolderDeletePreflight(
            logicalFolderPath,
            assets.Count,
            locations.Count,
            documents,
            activeSceneHasReferences,
            locations);
    }

    private int CollectReferenceDocuments(EditorAssetRecord asset, List<string> locations, string? ignoredReferenceFolder = null)
    {
        if (!Directory.Exists(ReferenceDocumentRoot))
        {
            return 0;
        }

        int referencedDocuments = 0;
        string[] files =
        [
            .. Directory.EnumerateFiles(ReferenceDocumentRoot, "*.scene", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(ReferenceDocumentRoot, "*.prefab", SearchOption.AllDirectories))
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
        bool sharedRoot = PathsEqual(ContentRoot, ReferenceDocumentRoot);
        for (int i = 0; i < files.Length; i++)
        {
            string logicalDocumentPath = NormalizeLogicalPath(
                Path.GetRelativePath(ReferenceDocumentRoot, files[i]),
                "reference document");
            if (sharedRoot && string.Equals(logicalDocumentPath, asset.LogicalPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (sharedRoot &&
                !string.IsNullOrWhiteSpace(ignoredReferenceFolder) &&
                IsUnderLogicalFolder(logicalDocumentPath, ignoredReferenceFolder))
            {
                continue;
            }

            EngineSceneDocument document = EngineSceneDocumentLoader.LoadDocument(files[i]);
            EditorSceneModel model = EditorSceneModel.FromDocument(document);
            int before = locations.Count;
            CollectReferenceLocations(model, logicalDocumentPath, asset, locations);
            if (locations.Count != before)
            {
                referencedDocuments++;
            }
        }

        return referencedDocuments;
    }

    private static void CollectReferenceLocations(EditorSceneModel scene, string documentName, EditorAssetRecord asset, List<string> locations)
    {
        EditorGameObject[] objects = [.. scene.EnumerateDepthFirst()];
        for (int i = 0; i < objects.Length; i++)
        {
            EditorGameObject gameObject = objects[i];
            if (asset.AssetType == EditorAssetType.Prefab &&
                gameObject.PrefabLink is { } prefabLink &&
                MatchesPrefabReference(prefabLink, asset.LogicalPath, asset.Id))
            {
                locations.Add($"{documentName}:{gameObject.Name}.Prefab");
            }

            for (int componentIndex = 0; componentIndex < gameObject.Components.Count; componentIndex++)
            {
                EditorComponentModel component = gameObject.Components[componentIndex];
                foreach (KeyValuePair<string, string> field in component.SerializedFields)
                {
                    if (MatchesAssetReference(field.Value, asset.LogicalPath, asset.Id, asset.AssetType))
                    {
                        locations.Add($"{documentName}:{gameObject.Name}.{component.TypeName}.{field.Key}");
                    }
                }
            }
        }
    }

    private static bool MatchesAssetReference(string value, string logicalPath, string assetId, EditorAssetType assetType)
    {
        return EditorAssetReferenceCodec.TryDecode(value, out EditorAssetReference reference) &&
            reference.AssetType == assetType &&
            (string.Equals(reference.AssetId, assetId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(reference.LogicalPath, logicalPath, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildDeleteConfirmationDiagnostic(EditorAssetDeletePreflight preflight)
    {
        return $"删除资产 {preflight.Asset.LogicalPath} 需要确认；预检未发现引用。";
    }

    private static string BuildDeleteBlockedDiagnostic(EditorAssetDeletePreflight preflight)
    {
        string sample = preflight.ReferenceLocations.Count == 0
            ? "无可显示引用位置"
            : string.Join("; ", preflight.ReferenceLocations.Take(3));
        return $"资产 {preflight.Asset.LogicalPath} 仍被 {preflight.ReferenceCount} 处引用（{sample}），已拒绝删除以避免丢失引用。";
    }

    private static string BuildFolderDeleteConfirmationDiagnostic(EditorAssetFolderDeletePreflight preflight)
    {
        return $"删除文件夹 {preflight.LogicalPath} 需要确认；将递归删除 {preflight.AssetCount.ToString(CultureInfo.InvariantCulture)} 个资产。";
    }

    private static string BuildFolderDeleteBlockedDiagnostic(EditorAssetFolderDeletePreflight preflight)
    {
        string sample = preflight.ReferenceLocations.Count == 0
            ? "无可显示引用位置"
            : string.Join("; ", preflight.ReferenceLocations.Take(3));
        return $"文件夹 {preflight.LogicalPath} 内资产仍被 {preflight.ReferenceCount.ToString(CultureInfo.InvariantCulture)} 处引用（{sample}），已拒绝删除以避免丢失引用。";
    }

    private IReadOnlyList<EditorAssetReferenceDocumentMovePlan> LoadReferenceDocumentMovePlans(string sourceFullPath, string targetFullPath)
    {
        if (!Directory.Exists(ReferenceDocumentRoot))
        {
            return [];
        }

        string[] files =
        [
            .. Directory.EnumerateFiles(ReferenceDocumentRoot, "*.scene", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(ReferenceDocumentRoot, "*.prefab", SearchOption.AllDirectories))
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
        EditorAssetReferenceDocumentMovePlan[] plans = new EditorAssetReferenceDocumentMovePlan[files.Length];
        for (int i = 0; i < files.Length; i++)
        {
            EngineSceneDocument document = EngineSceneDocumentLoader.LoadDocument(files[i]);
            string savePath = PathsEqual(ContentRoot, ReferenceDocumentRoot) && string.Equals(
                Path.GetFullPath(files[i]),
                sourceFullPath,
                StringComparison.OrdinalIgnoreCase)
                ? targetFullPath
                : files[i];
            plans[i] = new EditorAssetReferenceDocumentMovePlan(
                files[i],
                savePath,
                File.ReadAllText(files[i]),
                EditorSceneModel.FromDocument(document));
        }

        return plans;
    }

    private IReadOnlyList<EditorAssetReferenceDocumentMovePlan> LoadReferenceDocumentExternalMovePlans(
        string sourceFullPath,
        string targetFullPath)
    {
        if (!Directory.Exists(ReferenceDocumentRoot))
        {
            return [];
        }

        string[] files =
        [
            .. Directory.EnumerateFiles(ReferenceDocumentRoot, "*.scene", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(ReferenceDocumentRoot, "*.prefab", SearchOption.AllDirectories))
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
        bool sharedRoot = PathsEqual(ContentRoot, ReferenceDocumentRoot);
        EditorAssetReferenceDocumentMovePlan[] plans = new EditorAssetReferenceDocumentMovePlan[files.Length];
        for (int i = 0; i < files.Length; i++)
        {
            bool movedReferenceDocument = sharedRoot && PathsEqual(files[i], targetFullPath);
            EngineSceneDocument document = EngineSceneDocumentLoader.LoadDocument(files[i]);
            plans[i] = new EditorAssetReferenceDocumentMovePlan(
                movedReferenceDocument ? sourceFullPath : files[i],
                files[i],
                File.ReadAllText(files[i]),
                EditorSceneModel.FromDocument(document));
        }

        return plans;
    }

    private IReadOnlyList<EditorAssetReferenceDocumentMovePlan> LoadReferenceDocumentExternalFolderMovePlans(
        string sourceFolderFullPath,
        string targetFolderFullPath,
        string newFolderPath)
    {
        if (!Directory.Exists(ReferenceDocumentRoot))
        {
            return [];
        }

        string[] files =
        [
            .. Directory.EnumerateFiles(ReferenceDocumentRoot, "*.scene", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(ReferenceDocumentRoot, "*.prefab", SearchOption.AllDirectories))
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
        bool sharedRoot = PathsEqual(ContentRoot, ReferenceDocumentRoot);
        EditorAssetReferenceDocumentMovePlan[] plans = new EditorAssetReferenceDocumentMovePlan[files.Length];
        for (int i = 0; i < files.Length; i++)
        {
            string logicalDocumentPath = NormalizeLogicalPath(
                Path.GetRelativePath(ReferenceDocumentRoot, files[i]),
                "reference document");
            bool movedReferenceDocument = sharedRoot && IsUnderLogicalFolder(logicalDocumentPath, newFolderPath);
            string originalPath = movedReferenceDocument
                ? Path.Combine(sourceFolderFullPath, Path.GetRelativePath(targetFolderFullPath, files[i]))
                : files[i];

            EngineSceneDocument document = EngineSceneDocumentLoader.LoadDocument(files[i]);
            plans[i] = new EditorAssetReferenceDocumentMovePlan(
                originalPath,
                files[i],
                File.ReadAllText(files[i]),
                EditorSceneModel.FromDocument(document));
        }

        return plans;
    }

    private IReadOnlyList<EditorAssetReferenceDocumentMovePlan> LoadReferenceDocumentFolderMovePlans(
        string sourceFolderFullPath,
        string targetFolderFullPath,
        string currentFolderPath)
    {
        if (!Directory.Exists(ReferenceDocumentRoot))
        {
            return [];
        }

        string[] files =
        [
            .. Directory.EnumerateFiles(ReferenceDocumentRoot, "*.scene", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(ReferenceDocumentRoot, "*.prefab", SearchOption.AllDirectories))
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
        bool sharedRoot = PathsEqual(ContentRoot, ReferenceDocumentRoot);
        EditorAssetReferenceDocumentMovePlan[] plans = new EditorAssetReferenceDocumentMovePlan[files.Length];
        for (int i = 0; i < files.Length; i++)
        {
            string logicalDocumentPath = NormalizeLogicalPath(
                Path.GetRelativePath(ReferenceDocumentRoot, files[i]),
                "reference document");
            string savePath = sharedRoot && IsUnderLogicalFolder(logicalDocumentPath, currentFolderPath)
                ? Path.Combine(targetFolderFullPath, Path.GetRelativePath(sourceFolderFullPath, files[i]))
                : files[i];
            EngineSceneDocument document = EngineSceneDocumentLoader.LoadDocument(files[i]);
            plans[i] = new EditorAssetReferenceDocumentMovePlan(
                files[i],
                savePath,
                File.ReadAllText(files[i]),
                EditorSceneModel.FromDocument(document));
        }

        return plans;
    }

    private int RewriteReferencesInReferenceDocuments(
        IReadOnlyList<EditorAssetReferenceDocumentMovePlan> documents,
        string oldPath,
        string newPath,
        string assetId,
        EditorAssetType assetType,
        List<EditorAssetReferenceDocumentWriteRollback> writtenDocuments)
    {
        int updated = 0;
        for (int i = 0; i < documents.Count; i++)
        {
            EditorAssetReferenceDocumentMovePlan document = documents[i];
            if (!RewriteReferences(document.Model, oldPath, newPath, assetId, assetType))
            {
                continue;
            }

            _saveReferenceDocument(document.Model.ToDocument(), document.SaveFullPath);
            writtenDocuments.Add(new EditorAssetReferenceDocumentWriteRollback(
                document.OriginalFullPath,
                document.SaveFullPath,
                document.OriginalText));
            updated++;
        }

        return updated;
    }

    private int RewriteFolderReferencesInReferenceDocuments(
        IReadOnlyList<EditorAssetReferenceDocumentMovePlan> documents,
        IReadOnlyList<EditorAssetPathRewrite> rewrites,
        List<EditorAssetReferenceDocumentWriteRollback> writtenDocuments)
    {
        int updated = 0;
        for (int documentIndex = 0; documentIndex < documents.Count; documentIndex++)
        {
            EditorAssetReferenceDocumentMovePlan document = documents[documentIndex];
            bool changed = false;
            for (int rewriteIndex = 0; rewriteIndex < rewrites.Count; rewriteIndex++)
            {
                EditorAssetPathRewrite rewrite = rewrites[rewriteIndex];
                changed |= RewriteReferences(document.Model, rewrite.OldPath, rewrite.NewPath, rewrite.AssetId, rewrite.AssetType);
            }

            if (!changed)
            {
                continue;
            }

            _saveReferenceDocument(document.Model.ToDocument(), document.SaveFullPath);
            writtenDocuments.Add(new EditorAssetReferenceDocumentWriteRollback(
                document.OriginalFullPath,
                document.SaveFullPath,
                document.OriginalText));
            updated++;
        }

        return updated;
    }

    private void RewriteUiManifestScreenPaths(
        IReadOnlyList<EditorAssetPathRewrite> rewrites,
        List<EditorAssetReferenceDocumentWriteRollback> writtenDocuments)
    {
        bool hasUiScreenRewrite = false;
        for (int i = 0; i < rewrites.Count; i++)
        {
            if (rewrites[i].AssetType == EditorAssetType.UiScreen)
            {
                hasUiScreenRewrite = true;
                break;
            }
        }

        if (!hasUiScreenRewrite)
        {
            return;
        }

        string manifestPath = Path.Combine(ContentRoot, "ui", "ui-manifest.json");
        if (!File.Exists(manifestPath))
        {
            return;
        }

        string originalText = File.ReadAllText(manifestPath);
        EditorUiManifestDocument document = LoadUiManifestDocument(manifestPath);
        EditorUiManifestScreenDocument[] screens = NormalizeUiScreens(document.Screens);
        bool changed = false;
        for (int screenIndex = 0; screenIndex < screens.Length; screenIndex++)
        {
            for (int rewriteIndex = 0; rewriteIndex < rewrites.Count; rewriteIndex++)
            {
                EditorAssetPathRewrite rewrite = rewrites[rewriteIndex];
                if (rewrite.AssetType != EditorAssetType.UiScreen ||
                    !TryBuildUiScreenManifestPath(rewrite.OldPath, out string oldScreenPath) ||
                    !TryBuildUiScreenManifestPath(rewrite.NewPath, out string newScreenPath) ||
                    !string.Equals(screens[screenIndex].Path, oldScreenPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                screens[screenIndex] = new EditorUiManifestScreenDocument
                {
                    Id = screens[screenIndex].Id,
                    Path = newScreenPath,
                    Preload = screens[screenIndex].Preload,
                };
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        SaveUiManifestDocument(manifestPath, new EditorUiManifestDocument
        {
            Screens = screens,
            Images = document.Images,
        });
        writtenDocuments.Add(new EditorAssetReferenceDocumentWriteRollback(manifestPath, manifestPath, originalText));
    }

    private void RemoveUiManifestScreenEntries(IReadOnlyList<EditorAssetRecord> assets)
    {
        bool hasUiScreen = false;
        for (int i = 0; i < assets.Count; i++)
        {
            if (assets[i].AssetType == EditorAssetType.UiScreen)
            {
                hasUiScreen = true;
                break;
            }
        }

        if (!hasUiScreen)
        {
            return;
        }

        string manifestPath = Path.Combine(ContentRoot, "ui", "ui-manifest.json");
        if (!File.Exists(manifestPath))
        {
            return;
        }

        HashSet<string> removedScreenPaths = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < assets.Count; i++)
        {
            if (assets[i].AssetType == EditorAssetType.UiScreen &&
                TryBuildUiScreenManifestPath(assets[i].LogicalPath, out string screenPath))
            {
                _ = removedScreenPaths.Add(screenPath);
            }
        }

        if (removedScreenPaths.Count == 0)
        {
            return;
        }

        EditorUiManifestDocument document = LoadUiManifestDocument(manifestPath);
        EditorUiManifestScreenDocument[] screens = NormalizeUiScreens(document.Screens);
        List<EditorUiManifestScreenDocument> retained = new(screens.Length);
        for (int i = 0; i < screens.Length; i++)
        {
            if (removedScreenPaths.Contains(screens[i].Path))
            {
                continue;
            }

            retained.Add(screens[i]);
        }

        if (retained.Count == screens.Length)
        {
            return;
        }

        SaveUiManifestDocument(manifestPath, new EditorUiManifestDocument
        {
            Screens = [.. retained],
            Images = document.Images,
        });
    }

    private void RollBackMove(
        string sourceFullPath,
        string targetFullPath,
        EditorAssetManifestDocument originalManifest,
        IReadOnlyList<EditorAssetReferenceDocumentWriteRollback> writtenReferenceDocuments)
    {
        if (File.Exists(targetFullPath) && !File.Exists(sourceFullPath))
        {
            string? sourceDirectory = Path.GetDirectoryName(sourceFullPath);
            if (!string.IsNullOrEmpty(sourceDirectory))
            {
                _ = Directory.CreateDirectory(sourceDirectory);
            }

            File.Move(targetFullPath, sourceFullPath);
        }

        RestoreReferenceDocuments(writtenReferenceDocuments);
        SaveDocument(originalManifest);
    }

    private void RollBackFolderMove(
        string sourceFullPath,
        string targetFullPath,
        EditorAssetManifestDocument originalManifest,
        IReadOnlyList<EditorAssetReferenceDocumentWriteRollback> writtenReferenceDocuments)
    {
        if (Directory.Exists(targetFullPath) && !Directory.Exists(sourceFullPath))
        {
            string? sourceParent = Path.GetDirectoryName(sourceFullPath);
            if (!string.IsNullOrEmpty(sourceParent))
            {
                _ = Directory.CreateDirectory(sourceParent);
            }

            Directory.Move(targetFullPath, sourceFullPath);
        }

        RestoreReferenceDocuments(writtenReferenceDocuments);
        SaveDocument(originalManifest);
    }

    private static void RestoreReferenceDocuments(IReadOnlyList<EditorAssetReferenceDocumentWriteRollback> writtenReferenceDocuments)
    {
        for (int i = writtenReferenceDocuments.Count - 1; i >= 0; i--)
        {
            EditorAssetReferenceDocumentWriteRollback rollback = writtenReferenceDocuments[i];
            string restorePath = File.Exists(rollback.OriginalFullPath) || !string.Equals(rollback.SaveFullPath, rollback.OriginalFullPath, StringComparison.OrdinalIgnoreCase)
                ? rollback.OriginalFullPath
                : rollback.SaveFullPath;
            string? directory = Path.GetDirectoryName(Path.GetFullPath(restorePath));
            if (!string.IsNullOrEmpty(directory))
            {
                _ = Directory.CreateDirectory(directory);
            }

            File.WriteAllText(restorePath, rollback.OriginalText);
        }
    }

    private static bool RewriteReferences(EditorSceneModel scene, string oldPath, string newPath, string assetId, EditorAssetType assetType)
    {
        bool changed = false;
        EditorGameObject[] objects = [.. scene.EnumerateDepthFirst()];
        for (int i = 0; i < objects.Length; i++)
        {
            if (assetType == EditorAssetType.Prefab && RewritePrefabLink(scene, objects[i], oldPath, newPath, assetId))
            {
                changed = true;
            }

            for (int componentIndex = 0; componentIndex < objects[i].Components.Count; componentIndex++)
            {
                EditorComponentModel component = objects[i].Components[componentIndex];
                string[] fieldNames = [.. component.SerializedFields.Keys];
                for (int fieldIndex = 0; fieldIndex < fieldNames.Length; fieldIndex++)
                {
                    string fieldName = fieldNames[fieldIndex];
                    string value = component.SerializedFields[fieldName];
                    if (!EditorAssetReferenceCodec.TryRewrite(value, oldPath, newPath, assetId, assetType, out string rewritten))
                    {
                        continue;
                    }

                    scene.SetComponentField(objects[i].StableId, componentIndex, fieldName, rewritten);
                    changed = true;
                }
            }
        }

        return changed;
    }

    private static bool RewritePrefabLink(EditorSceneModel scene, EditorGameObject gameObject, string oldPath, string newPath, string assetId)
    {
        EditorPrefabLink? link = gameObject.PrefabLink;
        if (link is null || !MatchesPrefabReference(link, oldPath, assetId))
        {
            return false;
        }

        EditorPrefabLink updated = link.Clone();
        updated.AssetId = assetId;
        updated.AssetPath = newPath;
        scene.SetPrefabLink(gameObject.StableId, updated);
        return true;
    }

    private static bool MatchesPrefabReference(EditorPrefabLink link, string oldPath, string assetId)
    {
        if (!string.IsNullOrWhiteSpace(link.AssetId) &&
            string.Equals(link.AssetId, assetId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(link.AssetPath))
        {
            return false;
        }

        try
        {
            return string.Equals(NormalizeLogicalPath(link.AssetPath, nameof(link.AssetPath)), oldPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void WriteDefaultAsset(string fullPath, string logicalPath, EditorAssetType assetType, string? textContents)
    {
        if (textContents is not null)
        {
            File.WriteAllText(fullPath, textContents);
            return;
        }

        switch (assetType)
        {
            case EditorAssetType.Scene:
                EngineSceneDocumentLoader.SaveDocument(
                    new EngineSceneDocument
                    {
                        FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
                        Name = Path.GetFileNameWithoutExtension(logicalPath),
                        Entities = [],
                    },
                    fullPath);
                break;
            case EditorAssetType.Prefab:
                EngineSceneDocumentLoader.SaveDocument(
                    new EngineSceneDocument
                    {
                        FormatVersion = EngineSceneDocumentLoader.CurrentFormatVersion,
                        Name = Path.GetFileNameWithoutExtension(logicalPath),
                        Entities =
                        [
                            new EngineSceneEntityDocument
                            {
                                StableId = 1,
                                Name = Path.GetFileNameWithoutExtension(logicalPath),
                                Transform = new EngineSceneTransformDocument(),
                            },
                        ],
                    },
                    fullPath);
                break;
            case EditorAssetType.Script:
                File.WriteAllText(fullPath, CreateScriptTemplate(logicalPath));
                break;
            case EditorAssetType.UiScreen:
                File.WriteAllText(fullPath, CreateUiScreenTemplate(logicalPath));
                break;
            case EditorAssetType.Material:
                File.WriteAllText(fullPath, /*lang=json,strict*/ "{\"materials\":[]}" + Environment.NewLine);
                break;
            case EditorAssetType.Json:
                File.WriteAllText(fullPath, "{}" + Environment.NewLine);
                break;
            case EditorAssetType.Texture:
                break;
            case EditorAssetType.Audio:
                break;
            case EditorAssetType.Other:
                break;
            default:
                File.WriteAllText(fullPath, string.Empty);
                break;
        }
    }

    private static bool IsUnderLogicalFolder(string logicalPath, string folderName)
    {
        string normalized = logicalPath.Replace('\\', '/');
        return normalized.StartsWith(folderName + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string MoveLogicalPath(string logicalPath, string currentFolderPath, string newFolderPath)
    {
        return newFolderPath.TrimEnd('/') + logicalPath[currentFolderPath.Length..];
    }

    private static bool TryBuildUiScreenManifestPath(string logicalPath, out string screenPath)
    {
        if (TryBuildUiScreenManifestEntry(logicalPath, out _, out string resolvedPath))
        {
            screenPath = resolvedPath;
            return true;
        }

        screenPath = string.Empty;
        return false;
    }

    private int CountMissingUiManifestScreens(IReadOnlyList<EditorUiManifestScreenDocument> screens)
    {
        int missing = 0;
        for (int i = 0; i < screens.Count; i++)
        {
            string path = Path.Combine(ContentRoot, "ui", screens[i].Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                missing++;
            }
        }

        return missing;
    }

    private static string AllocateUiScreenId(string candidateId, HashSet<string> usedIds)
    {
        string baseId = string.IsNullOrWhiteSpace(candidateId) ? "screen" : candidateId;
        if (usedIds.Add(baseId))
        {
            return baseId;
        }

        for (int index = 2; index < 10_000; index++)
        {
            string id = baseId + "-" + index.ToString(CultureInfo.InvariantCulture);
            if (usedIds.Add(id))
            {
                return id;
            }
        }

        throw new InvalidOperationException($"无法生成唯一 UI screen id：{baseId}");
    }

    private static bool TryBuildUiScreenManifestEntry(string logicalPath, out string screenId, out string screenPath)
    {
        string normalized = logicalPath.Replace('\\', '/');
        const string Prefix = "ui/";
        if (!normalized.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            screenId = string.Empty;
            screenPath = string.Empty;
            return false;
        }

        string name = Path.GetFileNameWithoutExtension(normalized);
        screenId = ToKebabCase(string.IsNullOrWhiteSpace(name) ? "new-screen" : name);
        screenPath = normalized[Prefix.Length..];
        return true;
    }

    private static string CreateScriptTemplate(string logicalPath)
    {
        string name = Path.GetFileNameWithoutExtension(logicalPath);
        string className = SanitizeIdentifier(string.IsNullOrWhiteSpace(name) ? "NewBehaviour" : name);
        return $$"""
using PixelEngine.Scripting;

public sealed class {{className}} : Behaviour
{
}
""";
    }

    private static string CreateUiScreenTemplate(string logicalPath)
    {
        string name = Path.GetFileNameWithoutExtension(logicalPath);
        string screenId = ToKebabCase(string.IsNullOrWhiteSpace(name) ? "new-screen" : name);
        string title = string.IsNullOrWhiteSpace(name) ? "New Screen" : name;
        return $$"""
<rml title="{{title}}" data-screen="{{screenId}}" data-contract="editor.ui-screen/v1" style="left: 24px; top: 24px; width: 360px; min-height: 160px">
  <head>
    <style>
      body { font-family: "Noto Sans SC"; color: #e8edf2; background-color: rgba(8, 10, 14, 180); padding: 12px; }
      p { margin: 4px 0px; }
    </style>
  </head>
  <body>
    <p id="{{screenId}}_title">{{title}}</p>
  </body>
</rml>
""";
    }

    private static string ToKebabCase(string value)
    {
        ReadOnlySpan<char> source = value.AsSpan();
        Span<char> buffer = stackalloc char[Math.Min(source.Length * 2, 256)];
        int length = 0;
        bool previousWasSeparator = false;
        for (int i = 0; i < source.Length && length < buffer.Length; i++)
        {
            char current = source[i];
            if (char.IsLetterOrDigit(current))
            {
                if (char.IsUpper(current) && length > 0 && !previousWasSeparator && length < buffer.Length)
                {
                    buffer[length++] = '-';
                }

                if (length < buffer.Length)
                {
                    buffer[length++] = char.ToLowerInvariant(current);
                    previousWasSeparator = false;
                }
            }
            else if (length > 0 && !previousWasSeparator && length < buffer.Length)
            {
                buffer[length++] = '-';
                previousWasSeparator = true;
            }
        }

        while (length > 0 && buffer[length - 1] == '-')
        {
            length--;
        }

        return length == 0 ? "new-screen" : new string(buffer[..length]);
    }

    private static string SanitizeIdentifier(string value)
    {
        Span<char> buffer = stackalloc char[value.Length + 1];
        int length = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char ch = value[i];
            bool valid = length == 0
                ? char.IsLetter(ch) || ch == '_'
                : char.IsLetterOrDigit(ch) || ch == '_';
            if (valid)
            {
                buffer[length++] = ch;
            }
        }

        if (length == 0)
        {
            return "NewBehaviour";
        }

        string candidate = new(buffer[..length]);
        if (!char.IsLetter(candidate[0]) && candidate[0] != '_')
        {
            candidate = "Asset" + candidate;
        }

        return candidate;
    }

    private static string AllocateAssetId(HashSet<string> usedIds)
    {
        for (int i = 0; i < 10; i++)
        {
            string id = "asset_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            if (usedIds.Add(id))
            {
                return id;
            }
        }

        throw new InvalidOperationException("无法生成唯一 asset id。");
    }

    private static string NormalizeLogicalPath(string value, string fieldName)
    {
        string candidate = string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{fieldName} 不能为空。")
            : value.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(candidate) || candidate.StartsWith('/'))
        {
            throw new InvalidOperationException($"{fieldName} 必须是 content 内相对路径：{candidate}");
        }

        string[] parts = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        List<string> normalized = new(parts.Length);
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            if (part.Length == 0 || part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                throw new InvalidOperationException($"{fieldName} 不能越过 content 根目录：{candidate}");
            }

            normalized.Add(part);
        }

        return normalized.Count == 0 ? throw new InvalidOperationException($"{fieldName} 不能解析为空路径。") : string.Join('/', normalized);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}

/// <summary>
/// EditorAssetManifestDocument JSON 文档模型。
/// </summary>
internal sealed class EditorAssetManifestDocument
{
    public int FormatVersion { get; init; } = EditorAssetManifestStore.CurrentFormatVersion;

    public EditorAssetRecordDocument[]? Assets { get; init; }
}

/// <summary>
/// EditorAssetRecordDocument JSON 文档模型。
/// </summary>
internal sealed class EditorAssetRecordDocument
{
    public string Id { get; init; } = string.Empty;

    public string LogicalPath { get; init; } = string.Empty;

    public EditorAssetType AssetType { get; init; }

    public long SizeBytes { get; init; }

    public DateTimeOffset LastModifiedUtc { get; init; }
}

/// <summary>
/// content/ui/ui-manifest.json 的最小编辑器投影。
/// </summary>
internal sealed class EditorUiManifestDocument
{
    public EditorUiManifestScreenDocument[]? Screens { get; init; }

    public JsonElement[]? Images { get; init; }
}

/// <summary>
/// UI manifest screen 条目。
/// </summary>
internal sealed class EditorUiManifestScreenDocument
{
    public string Id { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public bool Preload { get; init; }
}
