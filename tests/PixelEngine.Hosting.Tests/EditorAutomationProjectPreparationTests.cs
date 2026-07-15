using PixelEngine.Editor.Shell;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>Project create/open automation 的后台冻结、原子发布与陈旧输入回归。</summary>
public sealed class EditorAutomationProjectPreparationTests
{
    /// <summary>New Project 只在 commit 时发布目标目录，重复提交被拒绝。</summary>
    [Fact]
    public void CreatePreparationDefersTargetUntilAtomicCommit()
    {
        using TemporaryRoot root = new();
        string location = Path.Combine(root.Path, "projects");
        using EditorAutomationProjectCreatePrepared prepared =
            EditorAutomationProjectCreatePrepared.Prepare(
                location,
                "Automation Game",
                CancellationToken.None);
        string staging = Assert.IsType<string>(prepared.StagingRootForTests);

        Assert.True(Directory.Exists(staging));
        Assert.False(Directory.Exists(prepared.TargetRoot));
        EditorProject project = prepared.Commit();

        Assert.Equal(Path.GetFullPath(prepared.TargetRoot), project.ProjectRoot);
        Assert.True(File.Exists(Path.Combine(prepared.TargetRoot, EditorProject.ProjectFileName)));
        Assert.False(Directory.Exists(staging));
        _ = Assert.Throws<InvalidOperationException>(prepared.Commit);
    }

    /// <summary>取消 preparation 会清理 staging；staging 内容被改写时 commit 必须失败且目标保持不存在。</summary>
    [Fact]
    public void CreatePreparationCancelAndTamperLeaveNoTarget()
    {
        using TemporaryRoot root = new();
        string cancelLocation = Path.Combine(root.Path, "cancel-location");
        EditorAutomationProjectCreatePrepared cancelled =
            EditorAutomationProjectCreatePrepared.Prepare(
                cancelLocation,
                "Cancelled Game",
                CancellationToken.None);
        string cancelledTarget = cancelled.TargetRoot;
        cancelled.Dispose();
        Assert.False(Directory.Exists(cancelledTarget));
        Assert.False(Directory.Exists(cancelLocation));

        string tamperLocation = Path.Combine(root.Path, "tamper-location");
        using EditorAutomationProjectCreatePrepared tampered =
            EditorAutomationProjectCreatePrepared.Prepare(
                tamperLocation,
                "Tampered Game",
                CancellationToken.None);
        string staging = Assert.IsType<string>(tampered.StagingRootForTests);
        File.AppendAllText(
            Path.Combine(staging, EditorProject.ProjectFileName),
            Environment.NewLine);

        IOException failure = Assert.Throws<IOException>(tampered.Commit);
        Assert.Contains("发生变化", failure.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(tampered.TargetRoot));
    }

    /// <summary>Open Project 在后台冻结的 project/settings 任一变化后均不得提交旧模型。</summary>
    [Fact]
    public void OpenPreparationDetectsChangedProjectSource()
    {
        using TemporaryRoot root = new();
        EditorProject project = EditorProject.CreateNew(
            Path.Combine(root.Path, "Existing"),
            "Existing");
        EditorAutomationProjectOpenPrepared prepared =
            EditorAutomationProjectOpenPrepared.Prepare(
                project.ProjectRoot,
                CancellationToken.None);
        Assert.True(prepared.IsCurrent());

        File.AppendAllText(project.ProjectFilePath, Environment.NewLine);

        Assert.False(prepared.IsCurrent());
    }

    private sealed class TemporaryRoot : IDisposable
    {
        public TemporaryRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "pixelengine-project-preparation",
                Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
