using System.Diagnostics;
using PixelEngine.Editor.Shell;
using PixelEngine.Editor.Shell.Build;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>UI 与 automation 共用的 build/player 语义状态机回归。</summary>
public sealed class EditorAutomationBuildTests
{
    /// <summary>Automation build 使用稳定 ID、逐次 launch 语义与有界 job log。</summary>
    [Fact]
    public void AutomationBuildKeepsCommandLaunchModeEphemeralAndBoundsLogs()
    {
        using TemporaryDirectory temp = new();
        EditorProject project = EditorProject.CreateNew(
            Path.Combine(temp.Path, "project"),
            "Automation Build");
        ControlledBuildService service = new();
        using BuildSettingsPanel panel = new(project, service);
        _ = panel.ApplyScriptedBuildSettingsProbe(Path.Combine(temp.Path, "out"));
        panel.PrepareFrame();
        Assert.False(panel.HasPendingWork);
        long idleAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            panel.PrepareFrame();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - idleAllocationStart);
        int changedCount = 0;
        panel.BuildChanged += _ => changedCount++;
        string buildId = new('a', 32);

        Assert.True(panel.TryStartAutomationBuild(
            buildId,
            launchOnSuccess: false,
            out EditorBuildExecutionSnapshot started,
            out string diagnostic), diagnostic);

        Assert.Equal(buildId, started.BuildId);
        Assert.False(started.LaunchOnSuccess);
        Assert.Equal(EditorBuildExecutionState.Running, started.State);
        Assert.True(panel.CaptureAutomationSettings().RunAfterBuild);
        Assert.False(Assert.Single(service.Requests).RunAfterBuild);
        Assert.Equal(0, changedCount);

        string oversized = new('x', 9000);
        for (int i = 0; i < 700; i++)
        {
            service.Progress.Report(new BuildProgressEvent(
                BuildEventKind.Log,
                BuildPhase.Publish,
                0.5f,
                BuildLogLevel.Info,
                oversized,
                DateTimeOffset.UtcNow));
        }

        Assert.True(panel.HasPendingWork);
        EditorBuildExecutionLogSnapshot log = panel.CaptureAutomationBuildLog(buildId);
        Assert.NotEmpty(log.Entries);
        Assert.InRange(log.Entries.Length, 2, 513);
        Assert.Contains(log.Entries, entry =>
            entry.Level == BuildLogLevel.Warning &&
            entry.Message.Contains("188", StringComparison.Ordinal));
        Assert.All(
            log.Entries.Where(static entry => entry.Level != BuildLogLevel.Warning),
            entry => Assert.Equal(8192, entry.Message.Length));
        Assert.True(changedCount > 0);
        panel.PrepareFrame();
        Assert.False(panel.HasPendingWork);

        Assert.True(panel.RequestAutomationBuildCancellation(
            buildId,
            notifyChanged: false,
            out EditorBuildExecutionSnapshot cancellation));
        Assert.True(cancellation.CancellationRequested);
        Assert.False(panel.RequestAutomationBuildCancellation(
            buildId,
            notifyChanged: false,
            out _));
        Assert.True(SpinWait.SpinUntil(
            () => panel.CaptureAutomationBuild(buildId).State == EditorBuildExecutionState.Cancelled,
            TimeSpan.FromSeconds(5)));
        EditorBuildExecutionSnapshot completed = panel.CaptureAutomationBuild(buildId);
        Assert.Equal(-2, completed.Result?.ExitCode);
        _ = Assert.NotNull(completed.CompletedAtUtc);
    }

    /// <summary>Build job 历史在容量边界淘汰最旧终态记录。</summary>
    [Fact]
    public void BuildHistoryRetainsLatestSixtyFourTerminalJobs()
    {
        using TemporaryDirectory temp = new();
        EditorProject project = EditorProject.CreateNew(
            Path.Combine(temp.Path, "project"),
            "Build History");
        ImmediateBuildService service = new();
        using BuildSettingsPanel panel = new(project, service);
        _ = panel.ApplyScriptedBuildSettingsProbe(Path.Combine(temp.Path, "out"));
        panel.PrepareFrame();

        for (int i = 0; i < 65; i++)
        {
            string buildId = i.ToString("x32", System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(panel.TryStartAutomationBuild(
                buildId,
                launchOnSuccess: false,
                out _,
                out string diagnostic), diagnostic);
            Assert.Equal(
                EditorBuildExecutionState.Failed,
                panel.CaptureAutomationBuild(buildId).State);
        }

        EditorBuildExecutionSnapshot[] retained = panel.CaptureAutomationBuilds();
        Assert.Equal(64, retained.Length);
        Assert.DoesNotContain(retained, item => item.BuildId == new string('0', 32));
        Assert.Contains(retained, item => item.BuildId == 64.ToString("x32", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>取消 wait 不终止 player，显式终止后退出事件不重复。</summary>
    [Fact]
    public async Task PlayerWaitCancellationDoesNotTerminateProcessAndExitPublishesOnce()
    {
        using TemporaryDirectory temp = new();
        string launcherPath = Path.Combine(temp.Path, "trusted-launcher.exe");
        await File.WriteAllTextAsync(launcherPath, "test");
        using EditorPlayerProcessManager manager = new(new SleepingProcessLauncher());
        BuildResult result = SuccessfulBuildResult(temp.Path, launcherPath);
        string playerId = new('b', 32);
        List<EditorPlayerProcessSnapshot> changes = [];
        manager.Changed += changes.Add;

        EditorPlayerProcessSnapshot launched = manager.Launch(
            new string('a', 32),
            result,
            notifyChanged: true,
            playerId);
        Assert.Equal(EditorPlayerProcessState.Running, launched.State);
        Assert.False(launched.TerminationRequested);
        Assert.False(manager.HasPendingChanges);
        EditorPlayerProcessWaitWorkspace workspace = manager.CaptureWaitWorkspace(playerId);
        using (CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(100)))
        {
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => workspace.RunAsync(cancellation.Token));
        }

        Assert.Equal(EditorPlayerProcessState.Running, manager.Capture(playerId).State);
        Assert.True(manager.RequestTermination(
            playerId,
            entireProcessTree: true,
            notifyChanged: true,
            out EditorPlayerProcessSnapshot terminating));
        Assert.True(terminating.TerminationRequested);
        Assert.False(manager.RequestTermination(
            playerId,
            entireProcessTree: true,
            notifyChanged: true,
            out EditorPlayerProcessSnapshot repeated));
        Assert.True(repeated.TerminationRequested);
        using CancellationTokenSource exitTimeout = new(TimeSpan.FromSeconds(10));
        EditorPlayerProcessSnapshot exited = await manager.WaitForExitAsync(playerId, exitTimeout.Token);
        Assert.Equal(EditorPlayerProcessState.Exited, exited.State);
        Assert.True(SpinWait.SpinUntil(
            () => manager.HasPendingChanges,
            TimeSpan.FromSeconds(5)));
        manager.Pump();
        _ = manager.Capture(playerId);
        Assert.False(manager.HasPendingChanges);
        Assert.False(changes[0].TerminationRequested);
        Assert.Equal(EditorPlayerProcessState.Running, changes[0].State);
        Assert.True(changes[^1].TerminationRequested);
        Assert.Equal(EditorPlayerProcessState.Exited, changes[^1].State);
        _ = Assert.Single(changes, static change => change.State == EditorPlayerProcessState.Exited);
    }

    /// <summary>Player process 历史在恰好满 64 条时仍能淘汰终态并接受新启动。</summary>
    [Fact]
    public async Task PlayerHistoryEvictsCompletedRecordAtCapacityBoundary()
    {
        using TemporaryDirectory temp = new();
        string launcherPath = Path.Combine(temp.Path, "trusted-launcher.exe");
        await File.WriteAllTextAsync(launcherPath, "test");
        using EditorPlayerProcessManager manager = new(new ImmediateProcessLauncher());
        BuildResult result = SuccessfulBuildResult(temp.Path, launcherPath);

        for (int i = 0; i < 65; i++)
        {
            string playerId = i.ToString("x32", System.Globalization.CultureInfo.InvariantCulture);
            _ = manager.Launch(
                new string('a', 32),
                result,
                notifyChanged: false,
                playerId);
            using CancellationTokenSource exitTimeout = new(TimeSpan.FromSeconds(10));
            _ = await manager.WaitForExitAsync(playerId, exitTimeout.Token);
        }

        EditorPlayerProcessSnapshot[] retained = manager.CaptureAll();
        Assert.Equal(64, retained.Length);
        Assert.DoesNotContain(retained, item => item.PlayerProcessId == new string('0', 32));
    }

    /// <summary>成功 build result 只能引用本次 output/player 下的真实非 reparse launcher。</summary>
    [Fact]
    public async Task BuildResultPathsAreBoundToCurrentOutputRoot()
    {
        using TemporaryDirectory temp = new();
        string output = Path.Combine(temp.Path, "output");
        string player = Path.Combine(output, "player");
        string package = Path.Combine(output, "package", "expanded");
        string archive = Path.Combine(output, "package.zip");
        string launcher = Path.Combine(player, "Trusted Player.exe");
        _ = Directory.CreateDirectory(player);
        _ = Directory.CreateDirectory(package);
        await File.WriteAllTextAsync(archive, "archive");
        await File.WriteAllTextAsync(launcher, "launcher");
        BuildResult source = SuccessfulBuildResult(player, launcher) with
        {
            PackageArchive = archive,
            PackageDir = package,
        };

        BuildResult validated = PlayerBuildService.ValidateBuildResultPaths(source, output);

        Assert.True(validated.Ok, validated.Error);
        Assert.Equal(Path.GetFullPath(player), validated.PlayerDir);
        Assert.Equal(Path.GetFullPath(launcher), validated.LauncherExe);

        string outside = Path.Combine(temp.Path, "outside");
        _ = Directory.CreateDirectory(outside);
        string outsideLauncher = Path.Combine(outside, "payload.exe");
        await File.WriteAllTextAsync(outsideLauncher, "payload");
        BuildResult escaped = PlayerBuildService.ValidateBuildResultPaths(
            source with { PlayerDir = outside, LauncherExe = outsideLauncher },
            output);

        Assert.False(escaped.Ok);
        Assert.Null(escaped.PlayerDir);
        Assert.Null(escaped.LauncherExe);
        Assert.Contains("越出", escaped.Error, StringComparison.Ordinal);
    }

    /// <summary>Build And Run 的 launcher 失败必须保留 build，并在快照中显式返回失败。</summary>
    [Fact]
    public async Task AutomaticPlayerLaunchFailureIsExplicitWithoutDiscardingSuccessfulBuild()
    {
        using TemporaryDirectory temp = new();
        EditorProject project = EditorProject.CreateNew(
            Path.Combine(temp.Path, "project"),
            "Build Launch Failure");
        string playerDirectory = Path.Combine(temp.Path, "player");
        _ = Directory.CreateDirectory(playerDirectory);
        string launcherPath = Path.Combine(playerDirectory, "player.exe");
        await File.WriteAllTextAsync(launcherPath, "launcher");
        SuccessfulBuildService service = new(SuccessfulBuildResult(playerDirectory, launcherPath));
        using EditorPlayerProcessManager processes = new(new ThrowingProcessLauncher());
        using BuildSettingsPanel panel = new(project, service, playerProcesses: processes);
        _ = panel.ApplyScriptedBuildSettingsProbe(Path.Combine(temp.Path, "out"));
        panel.PrepareFrame();
        string buildId = new('c', 32);

        Assert.True(panel.TryStartAutomationBuild(
            buildId,
            launchOnSuccess: true,
            out _,
            out string diagnostic), diagnostic);
        Assert.True(SpinWait.SpinUntil(
            () => panel.CaptureAutomationBuild(buildId).State == EditorBuildExecutionState.Succeeded,
            TimeSpan.FromSeconds(5)));

        EditorBuildExecutionSnapshot completed = panel.CaptureAutomationBuild(buildId);
        Assert.True(completed.Result?.Ok);
        Assert.True(completed.LaunchOnSuccess);
        Assert.Null(completed.PlayerProcessId);
        Assert.Contains("simulated launch denial", completed.PlayerLaunchError, StringComparison.Ordinal);
    }

    private static BuildResult SuccessfulBuildResult(string playerDirectory, string launcherPath)
    {
        return new BuildResult
        {
            Ok = true,
            PlayerDir = playerDirectory,
            LauncherExe = launcherPath,
            ExitCode = 0,
        };
    }

    private sealed class ControlledBuildService : IPlayerBuildService
    {
        private readonly TaskCompletionSource<BuildResult> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public List<BuildRequest> Requests { get; } = [];

        public IProgress<BuildProgressEvent> Progress { get; private set; } = null!;

        public Task<BuildPreflight> PreflightAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(new BuildPreflight { Ok = true, Diagnostic = "ok" });
        }

        public Task<BuildResult> RunAsync(
            BuildRequest request,
            IProgress<BuildProgressEvent> progress,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Progress = progress;
            _ = cancellationToken.Register(() => _completion.TrySetResult(new BuildResult
            {
                Ok = false,
                Error = "cancelled",
                ExitCode = -2,
            }));
            return _completion.Task;
        }
    }

    private sealed class ImmediateBuildService : IPlayerBuildService
    {
        public Task<BuildPreflight> PreflightAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(new BuildPreflight { Ok = true, Diagnostic = "ok" });
        }

        public Task<BuildResult> RunAsync(
            BuildRequest request,
            IProgress<BuildProgressEvent> progress,
            CancellationToken cancellationToken)
        {
            _ = request;
            _ = progress;
            _ = cancellationToken;
            return Task.FromResult(new BuildResult
            {
                Ok = false,
                Error = "expected",
                ExitCode = 1,
            });
        }
    }

    private sealed class SuccessfulBuildService(BuildResult result) : IPlayerBuildService
    {
        public Task<BuildPreflight> PreflightAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(new BuildPreflight { Ok = true, Diagnostic = "ok" });
        }

        public Task<BuildResult> RunAsync(
            BuildRequest request,
            IProgress<BuildProgressEvent> progress,
            CancellationToken cancellationToken)
        {
            _ = request;
            _ = progress;
            _ = cancellationToken;
            return Task.FromResult(result);
        }
    }

    private sealed class SleepingProcessLauncher : IEditorPlayerProcessLauncher
    {
        public Process Launch(ProcessStartInfo startInfo)
        {
            _ = startInfo;
            return StartShellProcess(OperatingSystem.IsWindows()
                ? "/d /c ping 127.0.0.1 -n 30 > nul"
                : "-c \"sleep 30\"");
        }
    }

    private sealed class ImmediateProcessLauncher : IEditorPlayerProcessLauncher
    {
        public Process Launch(ProcessStartInfo startInfo)
        {
            _ = startInfo;
            return StartShellProcess(OperatingSystem.IsWindows()
                ? "/d /c exit 0"
                : "-c \"exit 0\"");
        }
    }

    private sealed class ThrowingProcessLauncher : IEditorPlayerProcessLauncher
    {
        public Process Launch(ProcessStartInfo startInfo)
        {
            _ = startInfo;
            throw new InvalidOperationException("simulated launch denial");
        }
    }

    private static Process StartShellProcess(string arguments)
    {
        string shell = OperatingSystem.IsWindows()
            ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe"
            : "/bin/sh";
        return Process.Start(new ProcessStartInfo
        {
            FileName = shell,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("测试 shell process 未启动。");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "pixelengine-automation-build-tests",
                Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
