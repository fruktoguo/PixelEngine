using System.Diagnostics;

namespace PixelEngine.Editor.Shell.Build;

/// <summary>
/// 只启动成功 build 结果中的受信 launcher，并以 PID + start time 管理其完整生命周期。
/// </summary>
internal sealed class EditorPlayerProcessManager : IDisposable
{
    private const int MaximumRetainedProcesses = 64;
    private readonly Lock _sync = new();
    private readonly Dictionary<string, ProcessRecord> _records = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = [];
    private readonly IEditorPlayerProcessLauncher _launcher;
    private int _pendingChanges;
    private bool _disposed;

    internal EditorPlayerProcessManager(IEditorPlayerProcessLauncher? launcher = null)
    {
        _launcher = launcher ?? new EditorPlayerProcessLauncher();
    }

    internal event Action<EditorPlayerProcessSnapshot>? Changed;

    internal bool HasPendingChanges => Volatile.Read(ref _pendingChanges) != 0;

    internal EditorPlayerProcessSnapshot Launch(
        string buildId,
        BuildResult result,
        bool notifyChanged,
        string? requestedPlayerProcessId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        if (requestedPlayerProcessId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(requestedPlayerProcessId);
        }
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Ok || string.IsNullOrWhiteSpace(result.PlayerDir) ||
            string.IsNullOrWhiteSpace(result.LauncherExe))
        {
            throw new InvalidOperationException(
                "只有包含 playerDir 与 launcherExe 的成功 build 才能启动玩家。");
        }

        string playerDirectory = Path.GetFullPath(result.PlayerDir);
        string launcherPath = Path.GetFullPath(result.LauncherExe);
        EnsureLauncherIsTrusted(playerDirectory, launcherPath);
        lock (_sync)
        {
            ThrowIfDisposed();
            TrimCompletedHistory(MaximumRetainedProcesses - 1);
            if (_records.Count >= MaximumRetainedProcesses)
            {
                throw new InvalidOperationException(
                    $"并发运行的 player process 已达到 {MaximumRetainedProcesses} 上限。");
            }
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = launcherPath,
            WorkingDirectory = playerDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        Process process = _launcher.Launch(startInfo);
        ProcessRecord record;
        try
        {
            process.Exited += OnProcessExited;
            process.EnableRaisingEvents = true;
            DateTimeOffset startedAt = DateTimeOffset.UtcNow;
            record = new ProcessRecord(
                requestedPlayerProcessId ?? Guid.NewGuid().ToString("N"),
                buildId,
                process,
                process.Id,
                new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero),
                startedAt);
            lock (_sync)
            {
                ThrowIfDisposed();
                TrimCompletedHistory(MaximumRetainedProcesses - 1);
                if (_records.ContainsKey(record.PlayerProcessId))
                {
                    throw new InvalidOperationException(
                        $"Player process '{record.PlayerProcessId}' 已存在。");
                }

                if (_records.Count >= MaximumRetainedProcesses)
                {
                    throw new InvalidOperationException(
                        $"并发运行的 player process 已达到 {MaximumRetainedProcesses} 上限。");
                }

                _records.Add(record.PlayerProcessId, record);
                _order.Enqueue(record.PlayerProcessId);
            }
        }
        catch
        {
            process.Exited -= OnProcessExited;
            TryTerminate(process, entireProcessTree: true);
            process.Dispose();
            throw;
        }

        EditorPlayerProcessSnapshot snapshot;
        lock (_sync)
        {
            ProcessRecord current = RequireRecord(record.PlayerProcessId);
            snapshot = CaptureCore(current);
            current.ExitPublished = snapshot.State == EditorPlayerProcessState.Exited;
        }

        if (notifyChanged)
        {
            Changed?.Invoke(snapshot);
        }

        return snapshot;
    }

    internal EditorPlayerProcessSnapshot Capture(string playerProcessId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerProcessId);
        EditorPlayerProcessSnapshot snapshot;
        bool publishExit;
        lock (_sync)
        {
            ThrowIfDisposed();
            ProcessRecord record = RequireRecord(playerProcessId);
            snapshot = CaptureCore(record);
            publishExit = TryMarkExitPublished(record, snapshot);
        }

        if (publishExit)
        {
            Changed?.Invoke(snapshot);
        }

        return snapshot;
    }

    internal EditorPlayerProcessSnapshot[] CaptureAll()
    {
        EditorPlayerProcessSnapshot[] snapshots;
        List<EditorPlayerProcessSnapshot>? exits = null;
        lock (_sync)
        {
            ThrowIfDisposed();
            snapshots =
            [
                .. _order
                    .Select(id =>
                    {
                        ProcessRecord record = _records[id];
                        EditorPlayerProcessSnapshot snapshot = CaptureCore(record);
                        if (TryMarkExitPublished(record, snapshot))
                        {
                            (exits ??= []).Add(snapshot);
                        }

                        return snapshot;
                    })
                    .OrderByDescending(static snapshot => snapshot.StartedAtUtc),
            ];
        }

        PublishChanges(exits);
        return snapshots;
    }

    internal async Task<EditorPlayerProcessSnapshot> WaitForExitAsync(
        string playerProcessId,
        CancellationToken cancellationToken)
    {
        Process process;
        lock (_sync)
        {
            ThrowIfDisposed();
            process = RequireRecord(playerProcessId).Process;
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            ThrowIfDisposed();
            return CaptureCore(RequireRecord(playerProcessId));
        }
    }

    internal EditorPlayerProcessWaitWorkspace CaptureWaitWorkspace(string playerProcessId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerProcessId);
        lock (_sync)
        {
            ThrowIfDisposed();
            _ = RequireRecord(playerProcessId);
            return new EditorPlayerProcessWaitWorkspace(this, playerProcessId);
        }
    }

    internal bool RequestTermination(
        string playerProcessId,
        bool entireProcessTree,
        bool notifyChanged,
        out EditorPlayerProcessSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerProcessId);
        bool changed;
        bool publishExit;
        lock (_sync)
        {
            ThrowIfDisposed();
            ProcessRecord record = RequireRecord(playerProcessId);
            snapshot = CaptureCore(record);
            if (snapshot.State == EditorPlayerProcessState.Exited)
            {
                publishExit = TryMarkExitPublished(record, snapshot);
                changed = false;
            }
            else if (record.TerminationRequested)
            {
                publishExit = false;
                changed = false;
            }
            else
            {
                record.Process.Kill(entireProcessTree);
                record.TerminationRequested = true;
                snapshot = CaptureCore(record);
                record.ExitPublished |= snapshot.State == EditorPlayerProcessState.Exited;
                publishExit = false;
                changed = true;
            }
        }

        if (publishExit || (changed && notifyChanged))
        {
            Changed?.Invoke(snapshot);
        }

        return changed;
    }

    internal void Pump()
    {
        if (Interlocked.Exchange(ref _pendingChanges, 0) == 0)
        {
            return;
        }

        List<EditorPlayerProcessSnapshot>? changes = null;
        lock (_sync)
        {
            ThrowIfDisposed();
            foreach (string id in _order)
            {
                ProcessRecord record = _records[id];
                EditorPlayerProcessSnapshot snapshot = CaptureCore(record);
                if (TryMarkExitPublished(record, snapshot))
                {
                    (changes ??= []).Add(snapshot);
                }
            }
        }

        PublishChanges(changes);
    }

    public void Dispose()
    {
        Process[] processes;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            processes = [.. _records.Values.Select(static record => record.Process)];
            _records.Clear();
            _order.Clear();
        }

        for (int i = 0; i < processes.Length; i++)
        {
            processes[i].Exited -= OnProcessExited;
            processes[i].Dispose();
        }
    }

    private static void EnsureLauncherIsTrusted(string playerDirectory, string launcherPath)
    {
        string root = playerDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!Directory.Exists(playerDirectory) ||
            !launcherPath.StartsWith(root, comparison) ||
            !File.Exists(launcherPath))
        {
            throw new InvalidOperationException("player launcher 必须是 playerDir 内已存在的文件。");
        }

        EditorAutomationPathSafety.EnsureNoReparsePoints(playerDirectory, requireLeaf: true);
        EditorAutomationPathSafety.EnsureNoReparsePoints(launcherPath, requireLeaf: true);
    }

    private EditorPlayerProcessSnapshot CaptureCore(ProcessRecord record)
    {
        if (!record.ExitedAtUtc.HasValue && record.Process.HasExited)
        {
            record.ExitedAtUtc = DateTimeOffset.UtcNow;
            record.ExitCode = record.Process.ExitCode;
        }

        return new EditorPlayerProcessSnapshot(
            record.PlayerProcessId,
            record.BuildId,
            record.ProcessId,
            record.ProcessStartUtc,
            record.StartedAtUtc,
            record.ExitedAtUtc.HasValue ? EditorPlayerProcessState.Exited : EditorPlayerProcessState.Running,
            record.TerminationRequested,
            record.ExitedAtUtc,
            record.ExitCode);
    }

    private static bool TryMarkExitPublished(
        ProcessRecord record,
        EditorPlayerProcessSnapshot snapshot)
    {
        if (snapshot.State != EditorPlayerProcessState.Exited || record.ExitPublished)
        {
            return false;
        }

        record.ExitPublished = true;
        return true;
    }

    private void OnProcessExited(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        lock (_sync)
        {
            if (!_disposed)
            {
                Volatile.Write(ref _pendingChanges, 1);
            }
        }
    }

    private void PublishChanges(List<EditorPlayerProcessSnapshot>? changes)
    {
        if (changes is null)
        {
            return;
        }

        for (int i = 0; i < changes.Count; i++)
        {
            Changed?.Invoke(changes[i]);
        }
    }

    private ProcessRecord RequireRecord(string playerProcessId)
    {
        return _records.TryGetValue(playerProcessId, out ProcessRecord? record)
            ? record
            : throw new KeyNotFoundException($"Player process '{playerProcessId}' 不存在或已淘汰。");
    }

    private void TrimCompletedHistory(int maximumCount)
    {
        int inspected = 0;
        while (_records.Count > maximumCount && inspected < _order.Count)
        {
            string id = _order.Dequeue();
            ProcessRecord record = _records[id];
            if (!record.Process.HasExited)
            {
                _order.Enqueue(id);
                inspected++;
                continue;
            }

            _ = _records.Remove(id);
            record.Process.Exited -= OnProcessExited;
            record.Process.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static void TryTerminate(Process process, bool entireProcessTree)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private sealed class ProcessRecord(
        string playerProcessId,
        string buildId,
        Process process,
        int processId,
        DateTimeOffset processStartUtc,
        DateTimeOffset startedAtUtc)
    {
        public string PlayerProcessId { get; } = playerProcessId;

        public string BuildId { get; } = buildId;

        public Process Process { get; } = process;

        public int ProcessId { get; } = processId;

        public DateTimeOffset ProcessStartUtc { get; } = processStartUtc;

        public DateTimeOffset StartedAtUtc { get; } = startedAtUtc;

        public DateTimeOffset? ExitedAtUtc { get; set; }

        public int? ExitCode { get; set; }

        public bool TerminationRequested { get; set; }

        public bool ExitPublished { get; set; }
    }
}

/// <summary>冻结稳定 process ID 后供 automation worker 执行的可取消等待。</summary>
internal sealed class EditorPlayerProcessWaitWorkspace(
    EditorPlayerProcessManager manager,
    string playerProcessId)
{
    public Task<EditorPlayerProcessSnapshot> RunAsync(CancellationToken cancellationToken)
    {
        return manager.WaitForExitAsync(playerProcessId, cancellationToken);
    }
}

/// <summary>受信 launcher 的唯一 OS process 创建边界。</summary>
internal interface IEditorPlayerProcessLauncher
{
    Process Launch(ProcessStartInfo startInfo);
}

/// <summary>生产 player process launcher。</summary>
internal sealed class EditorPlayerProcessLauncher : IEditorPlayerProcessLauncher
{
    public Process Launch(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        return Process.Start(startInfo) ??
            throw new InvalidOperationException("操作系统未返回已启动的 player process。");
    }
}
