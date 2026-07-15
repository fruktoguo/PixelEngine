using System.Security.Cryptography;
using PixelEngine.Hosting;

namespace PixelEngine.Editor.Shell;

internal sealed partial class EditorAssetBrowserDataSource
{
    private const int MaximumAutomationSnapshotFiles = 8192;
    private const long MaximumAutomationSnapshotBytes = 128L * 1024 * 1024;

    internal EditorAssetAutomationFileSnapshot CaptureAutomationFileSnapshot(
        bool includeReferenceDocuments,
        IReadOnlyList<string>? additionalPaths = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase)
        {
            _assets.ManifestPath,
        };
        if (_scriptAssets is not null)
        {
            _ = paths.Add(_scriptAssets.ManifestPath);
        }

        if (additionalPaths is not null)
        {
            for (int i = 0; i < additionalPaths.Count; i++)
            {
                string path = additionalPaths[i];
                if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                {
                    throw new InvalidOperationException(
                        "资产事务 additional before-image path 必须是 canonical absolute path。");
                }

                _ = paths.Add(Path.GetFullPath(path));
            }
        }

        if (_project is not null)
        {
            _ = paths.Add(Path.Combine(_project.ProjectRoot, EditorProject.ProjectFileName));
            _ = paths.Add(Path.Combine(
                _project.ProjectRoot,
                EngineProjectSettingsStore.ProjectSettingsFileName));
            _ = paths.Add(Path.Combine(
                _project.ProjectRoot,
                EngineProjectSettingsStore.PlayerSettingsFileName));
            _ = paths.Add(Path.Combine(
                _project.ProjectRoot,
                EngineProjectSettingsStore.BuildSettingsFileName));
            _ = paths.Add(Path.Combine(_project.ContentRootPath, "ui", "ui-manifest.json"));

            if (includeReferenceDocuments && Directory.Exists(_project.ContentRootPath))
            {
                string[] referenceDocuments = EditorAssetFileTraversal.EnumerateFiles(
                    _project.ContentRootPath,
                    EditorAssetFileTraversalSelection.ReferenceDocuments,
                    MaximumAutomationSnapshotFiles,
                    "资产事务 reference document 扫描");
                foreach (string path in referenceDocuments)
                {
                    _ = paths.Add(path);
                }
            }
        }

        if (paths.Count > MaximumAutomationSnapshotFiles)
        {
            throw new InvalidOperationException(
                $"资产事务 before-image 文件数超过 {MaximumAutomationSnapshotFiles} 上限。");
        }

        EditorAssetAutomationFileState[] files = new EditorAssetAutomationFileState[paths.Count];
        long totalBytes = 0;
        int index = 0;
        foreach (string path in paths.Order(StringComparer.OrdinalIgnoreCase))
        {
            byte[]? contents = null;
            DateTime? lastWriteTimeUtc = null;
            if (File.Exists(path))
            {
                FileInfo before = new(path);
                totalBytes = checked(totalBytes + before.Length);
                if (totalBytes > MaximumAutomationSnapshotBytes)
                {
                    throw new InvalidOperationException(
                        $"资产事务 before-image 超过 {MaximumAutomationSnapshotBytes} 字节上限。");
                }

                contents = File.ReadAllBytes(path);
                lastWriteTimeUtc = before.LastWriteTimeUtc;
                FileInfo after = new(path);
                if (!after.Exists || after.Length != before.Length ||
                    after.LastWriteTimeUtc != before.LastWriteTimeUtc)
                {
                    throw new IOException($"捕获资产事务 before-image 时文件发生变化：{path}");
                }
            }

            files[index++] = new EditorAssetAutomationFileState(
                Path.GetFullPath(path),
                contents,
                lastWriteTimeUtc);
        }

        return new EditorAssetAutomationFileSnapshot(files);
    }

    internal EditorAssetAutomationBrowserSnapshot CaptureAutomationBrowserSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new EditorAssetAutomationBrowserSnapshot(
            [.. _assetSnapshot],
            [.. _folderSnapshot],
            LastDiagnostic,
            _runtimeDiagnostic);
    }

    internal void RestoreAutomationBrowserSnapshot(
        EditorAssetAutomationBrowserSnapshot snapshot,
        EditorAssetAutomationBrowserSnapshot? sourceSnapshot = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (sourceSnapshot is not null)
        {
            RestoreAutomationFolderTopology(snapshot.Folders, sourceSnapshot.Folders);
        }

        _assetSnapshot = [.. snapshot.Assets];
        _folderSnapshot = [.. snapshot.Folders];
        LastDiagnostic = snapshot.LastDiagnostic;
        _runtimeDiagnostic = snapshot.RuntimeDiagnostic;
    }

    private void RestoreAutomationFolderTopology(
        AssetBrowserFolderItem[] targetFolders,
        AssetBrowserFolderItem[] sourceFolders)
    {
        Dictionary<string, string> target = ResolveAutomationFolders(targetFolders);
        Dictionary<string, string> source = ResolveAutomationFolders(sourceFolders);
        foreach (KeyValuePair<string, string> folder in target
            .Where(pair => !source.ContainsKey(pair.Key))
            .OrderBy(static pair => pair.Key.Count(static character => character == '/'))
            .ThenBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            EnsureAutomationFolderPath(folder.Value, create: true);
        }

        foreach (KeyValuePair<string, string> folder in source
            .Where(pair => !target.ContainsKey(pair.Key))
            .OrderByDescending(static pair => pair.Key.Count(static character => character == '/'))
            .ThenByDescending(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            EnsureAutomationFolderPath(folder.Value, create: false);
            if (Directory.Exists(folder.Value) && !Directory.EnumerateFileSystemEntries(folder.Value).Any())
            {
                Directory.Delete(folder.Value);
            }
        }
    }

    private Dictionary<string, string> ResolveAutomationFolders(AssetBrowserFolderItem[] folders)
    {
        Dictionary<string, string> resolved = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < folders.Length; i++)
        {
            string path = folders[i].Path;
            if (string.IsNullOrEmpty(path) ||
                string.Equals(path, EditorRootedBrowserPath.ContentRootName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, EditorRootedBrowserPath.ScriptSourceRootName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryParseRequestPath(
                path,
                EditorAssetRootKind.Content,
                out EditorAssetPath parsed,
                out string diagnostic))
            {
                throw new InvalidOperationException(diagnostic);
            }

            string key = $"{parsed.Root}:{parsed.RelativePath}";
            resolved[key] = ResolveAssetFullPath(parsed.Root, parsed.RelativePath);
        }

        return resolved;
    }

    private void EnsureAutomationFolderPath(string path, bool create)
    {
        string fullPath = Path.GetFullPath(path);
        string root = _project is not null && IsPathInsideRoot(_project.ScriptSourcePath, fullPath)
            ? Path.GetFullPath(_project.ScriptSourcePath)
            : Path.GetFullPath(_assets.ContentRoot);
        if (!IsPathInsideRoot(root, fullPath) || string.Equals(root, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Automation folder topology 越过资产根：{fullPath}");
        }

        string relative = Path.GetRelativePath(root, fullPath);
        string current = root;
        string[] parts = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            current = Path.Combine(current, parts[i]);
            if (!Directory.Exists(current))
            {
                if (!create)
                {
                    return;
                }

                _ = Directory.CreateDirectory(current);
            }

            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Automation folder topology 路径包含 reparse point：{current}");
            }
        }
    }

    internal string ResolveAutomationPath(string browserPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return TryParseRequestPath(
                browserPath,
                EditorAssetRootKind.Content,
                out EditorAssetPath path,
                out string diagnostic)
            ? ResolveAssetFullPath(path.Root, path.RelativePath)
            : throw new InvalidOperationException(diagnostic);
    }

    internal string CreateAutomationArchivePath(string originalPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EditorProject project = _project ??
            throw new InvalidOperationException("Legacy Asset Database 不支持 automation undo archive。");
        string archivePath = Path.Combine(
            project.ProjectRoot,
            ".pixelengine",
            "automation-undo",
            Guid.NewGuid().ToString("N"),
            Path.GetFileName(originalPath));
        string parent = Path.GetDirectoryName(archivePath)
            ?? throw new InvalidOperationException("Automation asset archive 缺少父目录。");
        _ = Directory.CreateDirectory(parent);
        EnsureAutomationArchiveHasNoReparsePoint(project.ProjectRoot, parent);
        return archivePath;
    }

    internal bool TryCreateAutomationAssetReferencePlan(
        string assetId,
        EditorSceneModel activeScene,
        out EditorAssetAutomationReferencePlan plan)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentNullException.ThrowIfNull(activeScene);
        AssetBrowserItem item = ListAssets().FirstOrDefault(candidate =>
            string.Equals(candidate.AssetId, assetId, StringComparison.OrdinalIgnoreCase));
        if (item.AssetId is null)
        {
            plan = default;
            return false;
        }

        if (!TryParseRequestPath(
            item.Path,
            EditorAssetRootKind.Content,
            out EditorAssetPath path,
            out string diagnostic))
        {
            throw new InvalidOperationException(diagnostic);
        }

        EditorAssetManifestStore store = GetStore(path.Root);
        plan = new EditorAssetAutomationReferencePlan(
            new EditorAssetRecord(
                item.AssetId,
                path.RelativePath,
                EditorAssetManifestStore.Classify(path.RelativePath),
                item.SizeBytes,
                item.LastModifiedUtc),
            store.ContentRoot,
            store.ReferenceDocumentRoot,
            activeScene.ToDocument());
        return true;
    }

    private static void EnsureAutomationArchiveHasNoReparsePoint(string projectRoot, string parent)
    {
        string root = Path.GetFullPath(projectRoot);
        string current = Path.GetFullPath(parent);
        while (true)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    "Automation asset archive 路径不得包含 reparse point。");
            }

            if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            current = Path.GetDirectoryName(current)
                ?? throw new InvalidOperationException(
                    "Automation asset archive 无法回溯到工程根。");
        }
    }

}

internal sealed record EditorAssetAutomationFileState(
    string FullPath,
    byte[]? Contents,
    DateTime? LastWriteTimeUtc);

internal sealed record EditorAssetAutomationFileSnapshot(
    EditorAssetAutomationFileState[] Files);

internal sealed class EditorAssetAutomationFileJournal : IDisposable
{
    private sealed record Entry(
        string TargetPath,
        string BeforeArchivePath,
        string AfterArchivePath,
        FileIdentity Before,
        FileIdentity After);

    private readonly record struct FileIdentity(
        bool Exists,
        long Length,
        byte[]? Sha256);

    private readonly string _journalRoot;
    private readonly string _projectRoot;
    private readonly Entry[] _entries;
    private int _state;
    private int _disposed;

    private EditorAssetAutomationFileJournal(
        string projectRoot,
        string journalRoot,
        Entry[] entries,
        EditorAssetAutomationFileSnapshot afterSnapshot)
    {
        _projectRoot = projectRoot;
        _journalRoot = journalRoot;
        _entries = entries;
        AfterSnapshot = afterSnapshot;
    }

    internal EditorAssetAutomationFileSnapshot AfterSnapshot { get; }

    internal static EditorAssetAutomationFileJournal Stage(
        string projectRoot,
        EditorAssetAutomationFileSnapshot before,
        EditorAssetAutomationFileSnapshot after)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        string canonicalProjectRoot = Path.GetFullPath(projectRoot);
        string archiveRoot = Path.Combine(canonicalProjectRoot, ".pixelengine", "automation-undo");
        _ = Directory.CreateDirectory(archiveRoot);
        EnsureSafePath(canonicalProjectRoot, archiveRoot);
        string journalRoot = Path.Combine(archiveRoot, Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(journalRoot);
        EnsureSafePath(archiveRoot, journalRoot);
        try
        {
            Dictionary<string, EditorAssetAutomationFileState> beforeByPath = before.Files.ToDictionary(
                static file => file.FullPath,
                StringComparer.OrdinalIgnoreCase);
            Dictionary<string, EditorAssetAutomationFileState> afterByPath = after.Files.ToDictionary(
                static file => file.FullPath,
                StringComparer.OrdinalIgnoreCase);
            string[] paths =
            [
                .. beforeByPath.Keys
                    .Concat(afterByPath.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase),
            ];
            Entry[] entries = new Entry[paths.Length];
            EditorAssetAutomationFileState[] normalizedAfterFiles =
                new EditorAssetAutomationFileState[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                string target = Path.GetFullPath(paths[i]);
                if (!IsInside(target, canonicalProjectRoot))
                {
                    throw new InvalidOperationException(
                        $"Automation file journal target 越过工程根：{target}");
                }

                EnsureTargetPathSafe(canonicalProjectRoot, target);

                EditorAssetAutomationFileState? beforeState = beforeByPath.GetValueOrDefault(target);
                EditorAssetAutomationFileState? afterState = afterByPath.GetValueOrDefault(target);
                string beforeArchive = Path.Combine(journalRoot, $"{i:D5}.before");
                string afterArchive = Path.Combine(journalRoot, $"{i:D5}.after");
                if (afterState?.Contents is { } afterContents)
                {
                    EditorAtomicTextFile.WriteAllBytes(afterArchive, afterContents);
                    if (afterState.LastWriteTimeUtc is { } timestamp)
                    {
                        File.SetLastWriteTimeUtc(afterArchive, timestamp);
                    }
                }

                EditorAssetAutomationFileState normalizedAfter = new(
                    target,
                    afterState?.Contents,
                    File.Exists(afterArchive) ? File.GetLastWriteTimeUtc(afterArchive) : null);
                normalizedAfterFiles[i] = normalizedAfter;

                entries[i] = new Entry(
                    target,
                    beforeArchive,
                    afterArchive,
                    ToIdentity(beforeState),
                    ToIdentity(normalizedAfter));
            }

            return new EditorAssetAutomationFileJournal(
                canonicalProjectRoot,
                journalRoot,
                entries,
                new EditorAssetAutomationFileSnapshot(normalizedAfterFiles));
        }
        catch
        {
            Directory.Delete(journalRoot, recursive: true);
            DeleteEmptyParent(journalRoot);
            throw;
        }
    }

    internal void ApplyAfter()
    {
        Transition(toAfter: true);
    }

    internal void ApplyBefore()
    {
        Transition(toAfter: false);
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

        DeleteEmptyParent(_journalRoot);
    }

    private void Transition(bool toAfter)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        int targetState = toAfter ? 1 : 0;
        int sourceState = Volatile.Read(ref _state);
        if (sourceState == targetState)
        {
            return;
        }

        int completed = 0;
        try
        {
            for (; completed < _entries.Length; completed++)
            {
                TransitionEntry(_entries[completed], sourceState, targetState, validateTarget: true);
            }

            Volatile.Write(ref _state, targetState);
        }
        catch (Exception operationException)
        {
            List<Exception> failures = [operationException];
            for (int i = completed - 1; i >= 0; i--)
            {
                try
                {
                    TransitionEntry(_entries[i], targetState, sourceState, validateTarget: false);
                }
                catch (Exception rollbackException)
                {
                    failures.Add(rollbackException);
                }
            }

            throw new AggregateException(
                "Automation file journal 切换失败；已尝试恢复原文件状态。",
                failures);
        }
    }

    private void TransitionEntry(
        Entry entry,
        int sourceState,
        int targetState,
        bool validateTarget)
    {
        EnsureTargetPathSafe(_projectRoot, entry.TargetPath);
        FileIdentity source = sourceState == 0 ? entry.Before : entry.After;
        FileIdentity target = targetState == 0 ? entry.Before : entry.After;
        string sourceArchive = sourceState == 0 ? entry.BeforeArchivePath : entry.AfterArchivePath;
        string targetArchive = targetState == 0 ? entry.BeforeArchivePath : entry.AfterArchivePath;
        if (validateTarget)
        {
            ValidateTarget(entry.TargetPath, source);
        }

        if (source.Exists && File.Exists(sourceArchive))
        {
            throw new IOException($"Automation file journal source archive 已存在：{sourceArchive}");
        }

        if (target.Exists && !File.Exists(targetArchive))
        {
            throw new IOException($"Automation file journal target archive 缺失：{targetArchive}");
        }

        if (source.Exists)
        {
            File.Move(entry.TargetPath, sourceArchive);
        }

        try
        {
            if (target.Exists)
            {
                string? parent = Path.GetDirectoryName(entry.TargetPath);
                if (!string.IsNullOrEmpty(parent))
                {
                    _ = Directory.CreateDirectory(parent);
                    EnsureTargetPathSafe(_projectRoot, entry.TargetPath);
                }

                File.Move(targetArchive, entry.TargetPath);
            }
        }
        catch
        {
            if (source.Exists && File.Exists(sourceArchive) && !File.Exists(entry.TargetPath))
            {
                File.Move(sourceArchive, entry.TargetPath);
            }

            throw;
        }
    }

    private static void ValidateTarget(string path, FileIdentity expected)
    {
        FileInfo info = new(path);
        if (info.Exists != expected.Exists)
        {
            throw new IOException(
                $"Automation file journal target existence 已变化：expected={expected.Exists}, path={path}");
        }

        if (!expected.Exists)
        {
            return;
        }

        if (info.Length != expected.Length || expected.Sha256 is null)
        {
            throw new IOException($"Automation file journal target content size 已变化：{path}");
        }

        DateTime observedWriteTime = info.LastWriteTimeUtc;
        byte[] actualHash;
        using (FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan))
        {
            actualHash = SHA256.HashData(stream);
        }

        FileInfo after = new(path);
        if (!after.Exists ||
            after.Length != info.Length ||
            after.LastWriteTimeUtc != observedWriteTime)
        {
            throw new IOException(
                $"Automation file journal target 在内容验证期间发生变化：{path}");
        }

        if (!CryptographicOperations.FixedTimeEquals(actualHash, expected.Sha256))
        {
            throw new IOException($"Automation file journal target content SHA256 已变化：{path}");
        }
    }

    private static FileIdentity ToIdentity(EditorAssetAutomationFileState? state)
    {
        return state?.Contents is { } contents
            ? new FileIdentity(
                true,
                contents.LongLength,
                SHA256.HashData(contents))
            : new FileIdentity(false, 0, null);
    }

    private static void EnsureSafePath(string root, string path)
    {
        string canonicalRoot = Path.GetFullPath(root);
        string current = Path.GetFullPath(path);
        while (true)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Automation file journal 路径包含 reparse point：{current}");
            }

            if (string.Equals(current, canonicalRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            current = Path.GetDirectoryName(current)
                ?? throw new InvalidOperationException("Automation file journal 无法回溯到权威根。");
        }
    }

    private static bool IsInside(string candidate, string root)
    {
        string canonicalCandidate = Path.GetFullPath(candidate);
        string canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return canonicalCandidate.StartsWith(
            canonicalRoot + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static void EnsureTargetPathSafe(string projectRoot, string targetPath)
    {
        string canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
        string canonicalTarget = Path.GetFullPath(targetPath);
        if (!IsInside(canonicalTarget, canonicalRoot))
        {
            throw new InvalidOperationException(
                $"Automation file journal target 越过工程根：{canonicalTarget}");
        }

        string? current = canonicalTarget;
        while (current is not null)
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Automation file journal target 路径包含 reparse point：{current}");
            }

            if (string.Equals(
                current,
                canonicalRoot,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                return;
            }

            current = Path.GetDirectoryName(current);
        }

        throw new InvalidOperationException("Automation file journal target 无法回溯到工程根。");
    }

    private static void DeleteEmptyParent(string path)
    {
        string? parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent) &&
            !Directory.EnumerateFileSystemEntries(parent).Any())
        {
            Directory.Delete(parent);
        }
    }
}

internal sealed record EditorAssetAutomationBrowserSnapshot(
    AssetBrowserItem[] Assets,
    AssetBrowserFolderItem[] Folders,
    string LastDiagnostic,
    string RuntimeDiagnostic);

internal readonly record struct EditorAssetAutomationReferencePlan(
    EditorAssetRecord Asset,
    string ContentRoot,
    string ReferenceDocumentRoot,
    EngineSceneDocument ActiveScene);
