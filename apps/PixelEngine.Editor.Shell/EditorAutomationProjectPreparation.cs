using System.Security.Cryptography;
using PixelEngine.Editor.Shell.Settings;

namespace PixelEngine.Editor.Shell;

internal sealed record EditorAutomationProjectOpenPrepared(
    EditorProject Project,
    EditorAutomationFileFingerprint[] SourceFiles)
{
    internal static EditorAutomationProjectOpenPrepared Prepare(
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        string full = Path.GetFullPath(path.Trim());
        string projectFile = Directory.Exists(full) ||
            !Path.GetExtension(full).Equals(".pixelproj", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(full, EditorProject.ProjectFileName)
                : full;
        EditorAutomationPathSafety.EnsureNoReparsePoints(projectFile, requireLeaf: true);
        EditorAutomationFileFingerprint projectFingerprint =
            EditorAutomationFileFingerprint.Capture(projectFile, cancellationToken);
        EditorProject project = EditorProject.Load(projectFile);
        string settingsPath = new ProjectSettingsStore(project).SettingsPath;
        EditorAutomationFileFingerprint settingsFingerprint =
            EditorAutomationFileFingerprint.CaptureOptional(settingsPath, cancellationToken);
        EditorAutomationFileFingerprint[] sourceFiles = settingsFingerprint.Exists
            ? [projectFingerprint, settingsFingerprint]
            : [projectFingerprint];
        return new EditorAutomationProjectOpenPrepared(project, sourceFiles);
    }

    internal bool IsCurrent()
    {
        for (int i = 0; i < SourceFiles.Length; i++)
        {
            if (!SourceFiles[i].MatchesCurrent())
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed class EditorAutomationProjectCreatePrepared : IDisposable
{
    private readonly string _locationRoot;
    private readonly string _projectName;
    private readonly bool _locationExisted;
    private readonly bool _targetDirectoryExisted;
    private readonly EditorAutomationFileFingerprint[] _stagedFiles;
    private string? _stagingRoot;
    private int _committed;
    private int _disposed;

    private EditorAutomationProjectCreatePrepared(
        string locationRoot,
        string projectName,
        string targetRoot,
        string stagingRoot,
        bool locationExisted,
        bool targetDirectoryExisted,
        EditorAutomationFileFingerprint[] stagedFiles)
    {
        _locationRoot = locationRoot;
        _projectName = projectName;
        TargetRoot = targetRoot;
        _stagingRoot = stagingRoot;
        _locationExisted = locationExisted;
        _targetDirectoryExisted = targetDirectoryExisted;
        _stagedFiles = stagedFiles;
    }

    internal string TargetRoot { get; }

    internal string? StagingRootForTests => _stagingRoot;

    internal static EditorAutomationProjectCreatePrepared Prepare(
        string locationPath,
        string projectName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        cancellationToken.ThrowIfCancellationRequested();
        string locationRoot = Path.GetFullPath(locationPath.Trim());
        string normalizedName = projectName.Trim();
        if (!ProjectPickerWindow.TryResolveNewProjectPath(
            locationRoot,
            normalizedName,
            out string targetRoot,
            out string diagnostic))
        {
            throw new InvalidOperationException(diagnostic);
        }

        bool locationExisted = Directory.Exists(locationRoot);
        bool targetDirectoryExisted = Directory.Exists(targetRoot);
        if (!locationExisted)
        {
            _ = Directory.CreateDirectory(locationRoot);
        }

        EditorAutomationPathSafety.EnsureNoReparsePoints(locationRoot, requireLeaf: true);
        string stagingRoot = Path.Combine(
            locationRoot,
            ".pixelengine-create-" + Guid.NewGuid().ToString("N"));
        try
        {
            _ = EditorProject.CreateNew(stagingRoot, normalizedName);
            cancellationToken.ThrowIfCancellationRequested();
            string[] paths = EditorAssetFileTraversal.EnumerateFiles(
                stagingRoot,
                EditorAssetFileTraversalSelection.AllFiles,
                maximumSelectedFiles: 64,
                "Automation New Project preparation");
            EditorAutomationFileFingerprint[] stagedFiles = new EditorAutomationFileFingerprint[paths.Length];
            long totalBytes = 0;
            for (int i = 0; i < paths.Length; i++)
            {
                EditorAutomationFileFingerprint fingerprint =
                    EditorAutomationFileFingerprint.Capture(paths[i], cancellationToken);
                totalBytes = checked(totalBytes + fingerprint.Length);
                if (totalBytes > EditorAutomationFileFingerprint.MaximumAggregateBytes)
                {
                    throw new InvalidOperationException(
                        $"New Project staging 超过 {EditorAutomationFileFingerprint.MaximumAggregateBytes} 字节上限。");
                }

                stagedFiles[i] = fingerprint;
            }

            return new EditorAutomationProjectCreatePrepared(
                locationRoot,
                normalizedName,
                targetRoot,
                stagingRoot,
                locationExisted,
                targetDirectoryExisted,
                stagedFiles);
        }
        catch
        {
            DeleteDirectoryIfPresent(stagingRoot);
            if (!locationExisted)
            {
                DeleteDirectoryIfEmpty(locationRoot);
            }

            throw;
        }
    }

    internal EditorProject Commit()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Volatile.Read(ref _committed) != 0)
        {
            throw new InvalidOperationException("New Project preparation 只能提交一次。");
        }

        string stagingRoot = _stagingRoot ??
            throw new InvalidOperationException("New Project staging 已释放。");
        if (!ProjectPickerWindow.TryResolveNewProjectPath(
            _locationRoot,
            _projectName,
            out string currentTarget,
            out string diagnostic) ||
            !string.Equals(currentTarget, TargetRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(diagnostic)
                    ? "New Project 目标在提交前发生变化。"
                    : diagnostic);
        }

        EditorAutomationPathSafety.EnsureNoReparsePoints(_locationRoot, requireLeaf: true);
        EditorAutomationPathSafety.EnsureNoReparsePoints(stagingRoot, requireLeaf: true);
        for (int i = 0; i < _stagedFiles.Length; i++)
        {
            if (!_stagedFiles[i].MatchesCurrent())
            {
                throw new IOException($"New Project staging 文件在提交前发生变化：{_stagedFiles[i].Path}");
            }
        }

        bool removedEmptyTarget = false;
        try
        {
            if (_targetDirectoryExisted)
            {
                Directory.Delete(TargetRoot, recursive: false);
                removedEmptyTarget = true;
            }

            Directory.Move(stagingRoot, TargetRoot);
            EditorProject project = EditorProject.Load(TargetRoot);
            _stagingRoot = null;
            Volatile.Write(ref _committed, 1);
            return project;
        }
        catch (Exception operationException)
        {
            List<Exception> failures = [operationException];
            try
            {
                if (Directory.Exists(TargetRoot) && !Directory.Exists(stagingRoot))
                {
                    Directory.Move(TargetRoot, stagingRoot);
                }
            }
            catch (Exception rollbackException)
            {
                failures.Add(rollbackException);
            }

            try
            {
                if (removedEmptyTarget && !Directory.Exists(TargetRoot))
                {
                    _ = Directory.CreateDirectory(TargetRoot);
                }
            }
            catch (Exception rollbackException)
            {
                failures.Add(rollbackException);
            }

            throw failures.Count == 1
                ? operationException
                : new AggregateException(
                    "New Project 发布失败，且无法完整恢复目标目录 before state。",
                    failures);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        string? stagingRoot = Interlocked.Exchange(ref _stagingRoot, null);
        if (Volatile.Read(ref _committed) == 0 && stagingRoot is not null)
        {
            DeleteDirectoryIfPresent(stagingRoot);
        }

        if (!_locationExisted)
        {
            DeleteDirectoryIfEmpty(_locationRoot);
        }
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void DeleteDirectoryIfEmpty(string path)
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
        {
            Directory.Delete(path);
        }
    }
}

internal sealed record EditorAutomationFileFingerprint(
    string Path,
    bool Exists,
    long Length,
    DateTime? LastWriteTimeUtc,
    string Sha256)
{
    internal const long MaximumFileBytes = 4L * 1024L * 1024L;
    internal const long MaximumAggregateBytes = 16L * 1024L * 1024L;

    internal static EditorAutomationFileFingerprint Capture(
        string path,
        CancellationToken cancellationToken)
    {
        EditorAutomationFileFingerprint fingerprint = CaptureOptional(path, cancellationToken);
        return fingerprint.Exists
            ? fingerprint
            : throw new FileNotFoundException("Automation project source 文件不存在。", path);
    }

    internal static EditorAutomationFileFingerprint CaptureOptional(
        string path,
        CancellationToken cancellationToken)
    {
        string fullPath = System.IO.Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return new EditorAutomationFileFingerprint(fullPath, false, 0, null, string.Empty);
        }

        EditorAutomationPathSafety.EnsureNoReparsePoints(fullPath, requireLeaf: true);
        FileInfo info = new(fullPath);
        if (info.Length > MaximumFileBytes)
        {
            throw new InvalidOperationException(
                $"Automation project 文件超过 {MaximumFileBytes} 字节上限：{fullPath}");
        }

        using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        byte[] hash = SHA256.HashData(stream);
        cancellationToken.ThrowIfCancellationRequested();
        return new EditorAutomationFileFingerprint(
            fullPath,
            true,
            info.Length,
            info.LastWriteTimeUtc,
            Convert.ToHexStringLower(hash));
    }

    internal bool MatchesCurrent()
    {
        if (!Exists)
        {
            return !File.Exists(Path);
        }

        if (!File.Exists(Path))
        {
            return false;
        }

        EditorAutomationPathSafety.EnsureNoReparsePoints(Path, requireLeaf: true);
        FileInfo info = new(Path);
        if (info.Length != Length || info.LastWriteTimeUtc != LastWriteTimeUtc)
        {
            return false;
        }

        using FileStream stream = new(
            Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        return string.Equals(
            Convert.ToHexStringLower(SHA256.HashData(stream)),
            Sha256,
            StringComparison.Ordinal);
    }
}

internal static class EditorAutomationPathSafety
{
    internal static void EnsureNoReparsePoints(string path, bool requireLeaf)
    {
        string fullPath = Path.GetFullPath(path);
        if (requireLeaf && !File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            throw new FileNotFoundException("Automation project path 不存在。", fullPath);
        }

        string? current = fullPath;
        while (current is not null)
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Automation project path 包含 reparse point：{current}");
            }

            current = Path.GetDirectoryName(current);
        }
    }
}
