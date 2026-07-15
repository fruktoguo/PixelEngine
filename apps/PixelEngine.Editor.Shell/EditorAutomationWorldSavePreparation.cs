using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;
using PixelEngine.Editor.Automation.Server;
using PixelEngine.Serialization;
using PixelEngine.World;

namespace PixelEngine.Editor.Shell;

internal sealed partial class EditorAutomationAuthoringApi
{
    private AutomationBackgroundPreparation PrepareRuntimeSave(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationSaveSlotRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationSaveSlotRequest,
            AutomationProtocolConstants.RuntimeSaveSlotSaveMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.RuntimeSaveSlotSaveMethod);
        EditorProjectSession session = RequireSession();
        string slotId = SaveSlotPath.Normalize(request.SlotId);
        string saveRoot = session.CaptureAutomationSaveRoot();
        string targetPath = SaveSlotPath.Resolve(saveRoot, slotId);
        WorldSaveSnapshot snapshot = session.Engine.CaptureWorldSaveSnapshot(context.CancellationToken);
        PreparedWorldSave? prepared = null;
        return new AutomationBackgroundPreparation
        {
            PrepareAsync = cancellationToken =>
            {
                PreparedWorldSave result = PreparedWorldSave.Create(
                    session,
                    slotId,
                    saveRoot,
                    targetPath,
                    snapshot,
                    cancellationToken);
                Volatile.Write(ref prepared, result);
                return ValueTask.FromResult<object?>(result);
            },
            AbortAtEditorIngress = () => Volatile.Read(ref prepared)?.DisposeUncommitted(),
        };
    }

    private AutomationBackgroundPreparation PrepareRuntimeLoad(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationSaveSlotRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationSaveSlotRequest,
            AutomationProtocolConstants.RuntimeSaveSlotLoadMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.RuntimeSaveSlotLoadMethod);
        EditorProjectSession session = RequireEditSession();
        string slotId = SaveSlotPath.Normalize(request.SlotId);
        string saveRoot = session.CaptureAutomationSaveRoot();
        string targetPath = SaveSlotPath.Resolve(saveRoot, slotId);
        MaterialNameTable materialNames = new(
            session.Engine.Context.GetService<PixelEngine.Simulation.MaterialTable>().BuildIdNameTable());
        return new AutomationBackgroundPreparation
        {
            PrepareAsync = cancellationToken => ValueTask.FromResult<object?>(
                PreparedWorldLoad.Create(
                    session,
                    slotId,
                    saveRoot,
                    targetPath,
                    materialNames,
                    cancellationToken)),
        };
    }

    private AutomationOperationResult CommitRuntimeSave(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = payload;
        PreparedWorldSave prepared = context.RequirePreparedState<PreparedWorldSave>();
        if (!ReferenceEquals(RequireSession(), prepared.Session))
        {
            throw StateUnavailable("runtime.saves.save preparation 期间 project session 已变化。");
        }

        AutomationSaveSlotOperationResult response = prepared.CreateResponse();
        JsonElement serialized = JsonSerializer.SerializeToElement(
            response,
            AutomationJsonContext.Default.AutomationSaveSlotOperationResult);
        string[] resources =
        [
            RuntimeSavesResource,
            RuntimeSaveSlotResource(prepared.SlotId),
        ];
        IAutomationUndoAction? action = prepared.Apply();
        return action is null
            ? new AutomationOperationResult
            {
                Payload = serialized,
                ResourceIds = resources,
                WriteStateChanged = false,
            }
            : new AutomationOperationResult
            {
                Payload = serialized,
                ResourceIds = resources,
                UndoAction = action,
            };
    }

    private AutomationOperationResult CommitRuntimeLoad(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = payload;
        PreparedWorldLoad prepared = context.RequirePreparedState<PreparedWorldLoad>();
        EditorProjectSession session = RequireEditSession();
        if (!ReferenceEquals(session, prepared.Session))
        {
            throw StateUnavailable("runtime.saves.load preparation 期间 project session 已变化。");
        }

        if (!prepared.IsSourceCurrent())
        {
            throw StateUnavailable("runtime.saves.load preparation 后 slot 内容已变化；请重试。");
        }

        AutomationSaveSlotOperationResult response = prepared.CreateResponse();
        JsonElement serialized = JsonSerializer.SerializeToElement(
            response,
            AutomationJsonContext.Default.AutomationSaveSlotOperationResult);
        WorldSaveSnapshot before = session.Engine.CaptureWorldSaveSnapshot(context.CancellationToken);
        string[] resources = RuntimeWorldResources(session);
        if (before.ContentEquals(prepared.Snapshot))
        {
            return new AutomationOperationResult
            {
                Payload = serialized,
                ResourceIds = resources,
                WriteStateChanged = false,
            };
        }

        try
        {
            _ = session.Engine.ApplyWorldSaveSnapshot(prepared.Snapshot);
        }
        catch (Exception operationException)
        {
            ThrowWorldRollbackFailure(
                session,
                before,
                "runtime.saves.load 应用失败",
                operationException);
            throw;
        }

        return new AutomationOperationResult
        {
            Payload = serialized,
            ResourceIds = resources,
            UndoAction = new EditorAutomationWorldLoadUndoAction(
                session,
                before,
                prepared.Snapshot),
        };
    }

    private static string RuntimeSaveSlotResource(string slotId)
    {
        return $"editor:runtime:saves:{slotId}";
    }

    private static AutomationSaveSlotInfo MapSaveSlot(SaveSlotInfo slot)
    {
        return new AutomationSaveSlotInfo
        {
            SlotId = slot.Id,
            Path = slot.Path,
            LastWriteUtc = slot.TimestampUtc,
            FormatVersion = slot.FormatVersion,
            WorldSeed = slot.WorldSeed,
            GameTimeTicks = slot.GameTimeTicks,
            ChunkCount = slot.ChunkCount,
        };
    }

    private static void ThrowWorldRollbackFailure(
        EditorProjectSession session,
        WorldSaveSnapshot rollback,
        string operation,
        Exception operationException)
    {
        try
        {
            _ = session.Engine.ApplyWorldSaveSnapshot(rollback);
        }
        catch (Exception rollbackException)
        {
            throw new AggregateException(
                $"{operation}，且 world before-image 回滚失败。",
                operationException,
                rollbackException);
        }
    }

    private sealed class PreparedWorldSave
    {
        private readonly string _saveRoot;
        private readonly string _targetPath;
        private readonly string? _preparationRoot;
        private readonly string? _stagingPath;
        private readonly EditorWorldSaveDirectoryIdentity _beforeIdentity;
        private readonly EditorWorldSaveDirectoryIdentity _afterIdentity;
        private readonly WorldSaveSnapshot _snapshot;
        private int _consumed;

        private PreparedWorldSave(
            EditorProjectSession session,
            string slotId,
            string saveRoot,
            string targetPath,
            string? preparationRoot,
            string? stagingPath,
            EditorWorldSaveDirectoryIdentity beforeIdentity,
            EditorWorldSaveDirectoryIdentity afterIdentity,
            WorldSaveSnapshot snapshot,
            bool stateChanged)
        {
            Session = session;
            SlotId = slotId;
            _saveRoot = saveRoot;
            _targetPath = targetPath;
            _preparationRoot = preparationRoot;
            _stagingPath = stagingPath;
            _beforeIdentity = beforeIdentity;
            _afterIdentity = afterIdentity;
            _snapshot = snapshot;
            StateChanged = stateChanged;
        }

        internal EditorProjectSession Session { get; }

        internal string SlotId { get; }

        internal bool StateChanged { get; }

        internal static PreparedWorldSave Create(
            EditorProjectSession session,
            string slotId,
            string saveRoot,
            string targetPath,
            WorldSaveSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            string projectRoot = Path.GetFullPath(session.Project.ProjectRoot);
            string preparationRoot = Path.Combine(
                projectRoot,
                ".pixelengine",
                "automation-world-save-preparation",
                Guid.NewGuid().ToString("N"));
            string stagingPath = Path.Combine(preparationRoot, "after");
            try
            {
                EditorWorldSaveDirectoryIdentity before =
                    EditorWorldSaveDirectoryIdentity.Capture(
                        projectRoot,
                        targetPath,
                        includeContentHash: true,
                        cancellationToken);
                _ = new WorldSaveService().WriteSnapshot(snapshot, stagingPath, cancellationToken);
                EditorWorldSaveDirectoryIdentity after =
                    EditorWorldSaveDirectoryIdentity.Capture(
                        projectRoot,
                        stagingPath,
                        includeContentHash: true,
                        cancellationToken);
                bool changed = !before.ContentEquals(after);
                if (!changed)
                {
                    Directory.Delete(preparationRoot, recursive: true);
                    preparationRoot = string.Empty;
                    stagingPath = string.Empty;
                }

                return new PreparedWorldSave(
                    session,
                    slotId,
                    saveRoot,
                    targetPath,
                    string.IsNullOrEmpty(preparationRoot) ? null : preparationRoot,
                    string.IsNullOrEmpty(stagingPath) ? null : stagingPath,
                    before,
                    after,
                    snapshot,
                    changed);
            }
            catch
            {
                DeleteDirectoryIfExists(preparationRoot);
                throw;
            }
        }

        internal AutomationSaveSlotOperationResult CreateResponse()
        {
            EditorWorldSaveDirectoryIdentity identity = StateChanged
                ? _afterIdentity
                : _beforeIdentity;
            SaveSlotInfo slot = identity.ToSlotInfo(SlotId, _targetPath);
            return new AutomationSaveSlotOperationResult
            {
                Slot = MapSaveSlot(slot),
                WorldSeed = slot.WorldSeed,
                GameTimeTicks = slot.GameTimeTicks,
                ChunkCount = slot.ChunkCount,
                MaterialFallbackHitCount = 0,
            };
        }

        internal IAutomationUndoAction? Apply()
        {
            if (Volatile.Read(ref _consumed) != 0 ||
                (StateChanged && (_preparationRoot is null || _stagingPath is null)))
            {
                throw new InvalidOperationException("Prepared world save apply 状态无效。");
            }

            EditorAutomationWorldSaveDirectoryJournal? journal = null;
            bool diskApplied = false;
            bool persistenceStateChanged = false;
            try
            {
                ValidateSourceCurrent();
                if (StateChanged)
                {
                    journal = EditorAutomationWorldSaveDirectoryJournal.Create(
                        Session.Project.ProjectRoot,
                        _targetPath,
                        _stagingPath!,
                        _beforeIdentity,
                        _afterIdentity);
                    DeleteDirectoryIfExists(_preparationRoot!);
                    journal.ApplyAfter();
                    diskApplied = true;
                }

                persistenceStateChanged = Session.Engine.MarkWorldSnapshotPersisted(_snapshot);
                _ = Interlocked.Exchange(ref _consumed, 1);
                return !StateChanged && !persistenceStateChanged
                    ? null
                    : new EditorAutomationWorldSaveUndoAction(
                        Session,
                        SlotId,
                        journal,
                        _snapshot,
                        persistenceStateChanged);
            }
            catch (Exception operationException)
            {
                List<Exception>? rollbackFailures = null;
                if (persistenceStateChanged)
                {
                    try
                    {
                        _ = Session.Engine.RestoreWorldSnapshotPersistenceState(_snapshot);
                    }
                    catch (Exception rollbackException)
                    {
                        (rollbackFailures ??= [operationException]).Add(rollbackException);
                    }
                }

                if (diskApplied)
                {
                    try
                    {
                        journal!.ApplyBefore();
                    }
                    catch (Exception rollbackException)
                    {
                        (rollbackFailures ??= [operationException]).Add(rollbackException);
                    }
                }

                try
                {
                    journal?.Dispose();
                }
                catch (Exception cleanupException)
                {
                    (rollbackFailures ??= [operationException]).Add(cleanupException);
                }

                if (_preparationRoot is not null)
                {
                    try
                    {
                        DeleteDirectoryIfExists(_preparationRoot);
                    }
                    catch (Exception cleanupException)
                    {
                        (rollbackFailures ??= [operationException]).Add(cleanupException);
                    }
                }

                if (rollbackFailures is not null)
                {
                    throw new AggregateException(
                        "World save 提交失败，且磁盘或 residency before-image 回滚/清理失败。",
                        rollbackFailures);
                }

                throw;
            }
        }

        internal void DisposeUncommitted()
        {
            if (Interlocked.Exchange(ref _consumed, 1) != 0)
            {
                return;
            }

            if (_preparationRoot is not null)
            {
                DeleteDirectoryIfExists(_preparationRoot);
            }
        }

        private void ValidateSourceCurrent()
        {
            EditorWorldSaveDirectoryIdentity current = EditorWorldSaveDirectoryIdentity.Capture(
                Session.Project.ProjectRoot,
                _targetPath,
                includeContentHash: false,
                CancellationToken.None);
            if (!_beforeIdentity.MetadataEquals(current))
            {
                throw StateUnavailable(
                    "runtime.saves.save preparation 后目标 slot 已被外部修改；请重试。");
            }

            _ = _saveRoot;
        }
    }

    private sealed class PreparedWorldLoad
    {
        private PreparedWorldLoad(
            EditorProjectSession session,
            string slotId,
            string targetPath,
            WorldSaveSnapshot snapshot,
            EditorWorldSaveDirectoryIdentity sourceIdentity)
        {
            Session = session;
            SlotId = slotId;
            TargetPath = targetPath;
            Snapshot = snapshot;
            SourceIdentity = sourceIdentity;
        }

        internal EditorProjectSession Session { get; }

        internal string SlotId { get; }

        internal string TargetPath { get; }

        internal WorldSaveSnapshot Snapshot { get; }

        private EditorWorldSaveDirectoryIdentity SourceIdentity { get; }

        internal static PreparedWorldLoad Create(
            EditorProjectSession session,
            string slotId,
            string saveRoot,
            string targetPath,
            MaterialNameTable materialNames,
            CancellationToken cancellationToken)
        {
            EditorWorldSaveDirectoryIdentity before = EditorWorldSaveDirectoryIdentity.Capture(
                session.Project.ProjectRoot,
                targetPath,
                includeContentHash: true,
                cancellationToken);
            if (!before.Exists)
            {
                throw new FileNotFoundException($"存档点不存在：{slotId}", targetPath);
            }

            WorldSaveSnapshot snapshot = new WorldSaveService().ReadSnapshot(
                targetPath,
                materialNames,
                fallbackMaterialId: 0,
                cancellationToken);
            EditorWorldSaveDirectoryIdentity after = EditorWorldSaveDirectoryIdentity.Capture(
                session.Project.ProjectRoot,
                targetPath,
                includeContentHash: true,
                cancellationToken);
            if (!before.ContentEquals(after))
            {
                throw new IOException("读取 world save 时 slot 内容发生变化；请重试。");
            }

            _ = saveRoot;
            return new PreparedWorldLoad(session, slotId, targetPath, snapshot, after);
        }

        internal bool IsSourceCurrent()
        {
            EditorWorldSaveDirectoryIdentity current = EditorWorldSaveDirectoryIdentity.Capture(
                Session.Project.ProjectRoot,
                TargetPath,
                includeContentHash: false,
                CancellationToken.None);
            return SourceIdentity.MetadataEquals(current);
        }

        internal AutomationSaveSlotOperationResult CreateResponse()
        {
            SaveSlotInfo slot = SourceIdentity.ToSlotInfo(SlotId, TargetPath);
            return new AutomationSaveSlotOperationResult
            {
                Slot = MapSaveSlot(slot),
                WorldSeed = Snapshot.WorldSeed,
                GameTimeTicks = Snapshot.GameTimeTicks,
                ChunkCount = Snapshot.ChunkCount,
                MaterialFallbackHitCount = Snapshot.MaterialFallbackHitCount,
            };
        }
    }

    private sealed class EditorAutomationWorldSaveUndoAction(
        EditorProjectSession session,
        string slotId,
        EditorAutomationWorldSaveDirectoryJournal? journal,
        WorldSaveSnapshot snapshot,
        bool restorePersistenceState) :
        IAutomationUndoAction,
        IDisposable
    {
        private bool _isBefore;
        private int _disposed;

        public string Name => $"Save World Slot {slotId}";

        public void Undo()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            Apply(before: true);
        }

        public void Redo()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            Apply(before: false);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                journal?.Dispose();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                session.ReportAutomationCleanupFailure(
                    $"清理 world save Undo journal 失败，已保留供人工恢复。{exception.Message}");
            }
        }

        private void Apply(bool before)
        {
            if (_isBefore == before)
            {
                return;
            }

            try
            {
                if (before)
                {
                    journal?.ApplyBefore();
                    if (restorePersistenceState)
                    {
                        _ = session.Engine.RestoreWorldSnapshotPersistenceState(snapshot);
                    }
                }
                else
                {
                    journal?.ApplyAfter();
                    if (restorePersistenceState)
                    {
                        _ = session.Engine.MarkWorldSnapshotPersisted(snapshot);
                    }
                }

                _isBefore = before;
            }
            catch (Exception operationException)
            {
                try
                {
                    if (before)
                    {
                        if (restorePersistenceState)
                        {
                            _ = session.Engine.MarkWorldSnapshotPersisted(snapshot);
                        }

                        journal?.ApplyAfter();
                    }
                    else
                    {
                        if (restorePersistenceState)
                        {
                            _ = session.Engine.RestoreWorldSnapshotPersistenceState(snapshot);
                        }

                        journal?.ApplyBefore();
                    }
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException(
                        $"World save {(before ? "Undo" : "Redo")} 失败，且组合状态回滚失败。",
                        operationException,
                        rollbackException);
                }

                throw;
            }
        }
    }

    private sealed class EditorAutomationWorldLoadUndoAction(
        EditorProjectSession session,
        WorldSaveSnapshot before,
        WorldSaveSnapshot after) : IAutomationUndoAction
    {
        private bool _isBefore;

        public string Name => "Load World Save";

        public void Undo()
        {
            Apply(target: before, rollback: after, beforeDirection: true);
        }

        public void Redo()
        {
            Apply(target: after, rollback: before, beforeDirection: false);
        }

        private void Apply(
            WorldSaveSnapshot target,
            WorldSaveSnapshot rollback,
            bool beforeDirection)
        {
            if (_isBefore == beforeDirection)
            {
                return;
            }

            try
            {
                _ = session.Engine.ApplyWorldSaveSnapshot(target);
                _isBefore = beforeDirection;
            }
            catch (Exception operationException)
            {
                ThrowWorldRollbackFailure(
                    session,
                    rollback,
                    $"World load {(beforeDirection ? "Undo" : "Redo")} 失败",
                    operationException);
                throw;
            }
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

internal sealed class EditorWorldSaveDirectoryIdentity
{
    private const int MaximumFiles = 4096;
    private const long MaximumBytes = 16L * 1024 * 1024 * 1024;
    private readonly Entry[] _entries;
    private readonly ManifestSummary? _manifest;

    private EditorWorldSaveDirectoryIdentity(
        bool exists,
        Entry[] entries,
        string? contentSha256,
        ManifestSummary? manifest)
    {
        Exists = exists;
        _entries = entries;
        ContentSha256 = contentSha256;
        _manifest = manifest;
    }

    internal bool Exists { get; }

    private string? ContentSha256 { get; }

    internal static EditorWorldSaveDirectoryIdentity Capture(
        string authorityRoot,
        string directory,
        bool includeContentHash,
        CancellationToken cancellationToken)
    {
        string authority = Path.TrimEndingDirectorySeparator(Path.GetFullPath(authorityRoot));
        string root = Path.GetFullPath(directory);
        EnsureSafePath(authority, root);
        if (!Directory.Exists(root))
        {
            return new EditorWorldSaveDirectoryIdentity(
                false,
                [],
                includeContentHash ? EmptySha256 : null,
                manifest: null);
        }

        List<Entry> entries = [];
        List<string> files = [];
        Stack<string> pending = new();
        pending.Push(root);
        while (pending.TryPop(out string? current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureNotReparse(current);
            foreach (string childDirectory in Directory.EnumerateDirectories(current)
                .Order(StringComparer.OrdinalIgnoreCase))
            {
                EnsureNotReparse(childDirectory);
                pending.Push(childDirectory);
            }

            foreach (string file in Directory.EnumerateFiles(current)
                .Order(StringComparer.OrdinalIgnoreCase))
            {
                EnsureNotReparse(file);
                files.Add(file);
                if (files.Count > MaximumFiles)
                {
                    throw new InvalidDataException(
                        $"World save 文件数超过 {MaximumFiles} 上限：{root}");
                }
            }
        }

        long totalBytes = 0;
        using IncrementalHash? aggregate = includeContentHash
            ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
            : null;
        byte[] copyBuffer = new byte[128 * 1024];
        ManifestSummary? manifest = null;
        foreach (string file in files.Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileInfo before = new(file);
            totalBytes = checked(totalBytes + before.Length);
            if (totalBytes > MaximumBytes)
            {
                throw new InvalidDataException(
                    $"World save 总字节数超过 {MaximumBytes} 上限：{root}");
            }

            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (aggregate is not null)
            {
                byte[] header = Encoding.UTF8.GetBytes($"{relative}\0{before.Length}\0");
                aggregate.AppendData(header);
                using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                int read;
                while ((read = stream.Read(copyBuffer)) != 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    aggregate.AppendData(copyBuffer.AsSpan(0, read));
                }
            }

            FileInfo after = new(file);
            if (!after.Exists || after.Length != before.Length ||
                after.LastWriteTimeUtc != before.LastWriteTimeUtc)
            {
                throw new IOException($"捕获 world save identity 时文件发生变化：{file}");
            }

            entries.Add(new Entry(relative, before.Length, before.LastWriteTimeUtc));
            if (string.Equals(relative, "manifest.bin", StringComparison.OrdinalIgnoreCase))
            {
                if (before.Length > 16L * 1024 * 1024)
                {
                    throw new InvalidDataException($"World save manifest 大小无效：{file}");
                }

                WorldManifest decoded = new ManifestCodec().Decode(File.ReadAllBytes(file));
                FileInfo afterManifest = new(file);
                if (!afterManifest.Exists || afterManifest.Length != before.Length ||
                    afterManifest.LastWriteTimeUtc != before.LastWriteTimeUtc)
                {
                    throw new IOException($"读取 world save manifest 时文件发生变化：{file}");
                }

                manifest = new ManifestSummary(
                    decoded.FormatVersion,
                    decoded.WorldSeed,
                    decoded.GameTimeTicks,
                    decoded.ChunkIndex.Length,
                    before.LastWriteTimeUtc);
            }
        }

        string? hash = aggregate is null
            ? null
            : Convert.ToHexString(aggregate.GetHashAndReset()).ToLowerInvariant();
        return new EditorWorldSaveDirectoryIdentity(true, [.. entries], hash, manifest);
    }

    internal bool MetadataEquals(EditorWorldSaveDirectoryIdentity other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Exists == other.Exists && _entries.AsSpan().SequenceEqual(other._entries);
    }

    internal bool ContentEquals(EditorWorldSaveDirectoryIdentity other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return MetadataEqualsIgnoringTimestamps(other) &&
            ContentSha256 is not null &&
            string.Equals(ContentSha256, other.ContentSha256, StringComparison.Ordinal);
    }

    internal SaveSlotInfo ToSlotInfo(string slotId, string targetPath)
    {
        if (!Exists)
        {
            throw new InvalidOperationException("不存在的 world save identity 不能映射为 slot。");
        }

        ManifestSummary manifest = _manifest ??
            throw new InvalidDataException("World save identity 缺少有效 manifest.bin。");
        return new SaveSlotInfo(
            slotId,
            targetPath,
            manifest.LastWriteTimeUtc,
            manifest.FormatVersion,
            manifest.WorldSeed,
            manifest.GameTimeTicks,
            manifest.ChunkCount);
    }

    private bool MetadataEqualsIgnoringTimestamps(EditorWorldSaveDirectoryIdentity other)
    {
        if (Exists != other.Exists || _entries.Length != other._entries.Length)
        {
            return false;
        }

        for (int i = 0; i < _entries.Length; i++)
        {
            if (!string.Equals(
                    _entries[i].RelativePath,
                    other._entries[i].RelativePath,
                    StringComparison.OrdinalIgnoreCase) ||
                _entries[i].Length != other._entries[i].Length)
            {
                return false;
            }
        }

        return true;
    }

    private static void EnsureSafePath(string authorityRoot, string path)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(path, authorityRoot, comparison) &&
            !path.StartsWith(authorityRoot + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidOperationException($"World save path 越过工程根：{path}");
        }

        string? current = path;
        while (current is not null)
        {
            if (File.Exists(current) || Directory.Exists(current))
            {
                EnsureNotReparse(current);
            }

            if (string.Equals(current, authorityRoot, comparison))
            {
                return;
            }

            current = Path.GetDirectoryName(current);
        }

        throw new InvalidOperationException("World save path 无法回溯到工程根。");
    }

    private static void EnsureNotReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"World save path 包含 reparse point：{path}");
        }
    }

    private const string EmptySha256 =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private sealed record Entry(string RelativePath, long Length, DateTime LastWriteTimeUtc);

    private sealed record ManifestSummary(
        int FormatVersion,
        ulong WorldSeed,
        long GameTimeTicks,
        int ChunkCount,
        DateTime LastWriteTimeUtc);
}

internal sealed class EditorAutomationWorldSaveDirectoryJournal : IDisposable
{
    private readonly string _projectRoot;
    private readonly string _targetPath;
    private readonly string _journalRoot;
    private readonly string _beforeArchivePath;
    private readonly string _afterArchivePath;
    private readonly EditorWorldSaveDirectoryIdentity _before;
    private readonly EditorWorldSaveDirectoryIdentity _after;
    private bool _isBefore = true;
    private int _disposed;

    private EditorAutomationWorldSaveDirectoryJournal(
        string projectRoot,
        string targetPath,
        string journalRoot,
        EditorWorldSaveDirectoryIdentity before,
        EditorWorldSaveDirectoryIdentity after)
    {
        _projectRoot = projectRoot;
        _targetPath = targetPath;
        _journalRoot = journalRoot;
        _beforeArchivePath = Path.Combine(journalRoot, "before");
        _afterArchivePath = Path.Combine(journalRoot, "after");
        _before = before;
        _after = after;
    }

    internal static EditorAutomationWorldSaveDirectoryJournal Create(
        string projectRoot,
        string targetPath,
        string stagingPath,
        EditorWorldSaveDirectoryIdentity before,
        EditorWorldSaveDirectoryIdentity after)
    {
        string authority = Path.GetFullPath(projectRoot);
        string root = Path.Combine(
            authority,
            ".pixelengine",
            "automation-world-save-undo",
            Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(root);
        EditorAutomationWorldSaveDirectoryJournal journal = new(
            authority,
            Path.GetFullPath(targetPath),
            root,
            before,
            after);
        try
        {
            Directory.Move(stagingPath, journal._afterArchivePath);
            return journal;
        }
        catch
        {
            journal.Dispose();
            throw;
        }
    }

    internal void ApplyAfter()
    {
        Transition(before: false);
    }

    internal void ApplyBefore()
    {
        Transition(before: true);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (Directory.Exists(_journalRoot))
        {
            Directory.Delete(_journalRoot, recursive: true);
        }
    }

    private void Transition(bool before)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_isBefore == before)
        {
            return;
        }

        EditorWorldSaveDirectoryIdentity source = _isBefore ? _before : _after;
        EditorWorldSaveDirectoryIdentity target = before ? _before : _after;
        string sourceArchive = _isBefore ? _beforeArchivePath : _afterArchivePath;
        string targetArchive = before ? _beforeArchivePath : _afterArchivePath;
        EditorWorldSaveDirectoryIdentity current = EditorWorldSaveDirectoryIdentity.Capture(
            _projectRoot,
            _targetPath,
            includeContentHash: false,
            CancellationToken.None);
        if (!source.MetadataEquals(current))
        {
            throw new IOException("World save Undo/Redo target 已被外部修改，拒绝覆盖。");
        }

        if (source.Exists && Directory.Exists(sourceArchive))
        {
            throw new IOException($"World save source archive 已存在：{sourceArchive}");
        }

        if (target.Exists && !Directory.Exists(targetArchive))
        {
            throw new IOException($"World save target archive 缺失：{targetArchive}");
        }

        if (source.Exists)
        {
            Directory.Move(_targetPath, sourceArchive);
        }

        try
        {
            if (target.Exists)
            {
                string? parent = Path.GetDirectoryName(_targetPath);
                if (!string.IsNullOrEmpty(parent))
                {
                    _ = Directory.CreateDirectory(parent);
                }

                Directory.Move(targetArchive, _targetPath);
            }

            _isBefore = before;
        }
        catch
        {
            if (source.Exists && Directory.Exists(sourceArchive) && !Directory.Exists(_targetPath))
            {
                Directory.Move(sourceArchive, _targetPath);
            }

            throw;
        }
    }
}
