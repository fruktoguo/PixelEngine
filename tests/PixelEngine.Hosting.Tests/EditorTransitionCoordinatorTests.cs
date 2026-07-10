using PixelEngine.Editor.Shell;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Editor 未保存场景转场协调器测试。
/// </summary>
public sealed class EditorTransitionCoordinatorTests
{
    /// <summary>
    /// 验证全部受保护转场在场景干净时立即执行且不调用保存。
    /// </summary>
    [Theory]
    [InlineData((int)EditorTransitionKind.NewScene)]
    [InlineData((int)EditorTransitionKind.OpenScene)]
    [InlineData((int)EditorTransitionKind.OpenProject)]
    [InlineData((int)EditorTransitionKind.CreateProject)]
    [InlineData((int)EditorTransitionKind.CloseProject)]
    [InlineData((int)EditorTransitionKind.Exit)]
    public void CleanTransitionExecutesImmediately(int kindValue)
    {
        EditorTransitionKind kind = (EditorTransitionKind)kindValue;
        int saved = 0;
        int executed = 0;
        EditorTransitionCoordinator coordinator = new(
            isDirty: static () => false,
            save: () =>
            {
                saved++;
                return EditorTransitionSaveResult.Success();
            });

        EditorTransitionResult result = coordinator.Request(kind, () => executed++, " target ");

        Assert.Equal(EditorTransitionStatus.Executed, result.Status);
        Assert.True(result.Executed);
        Assert.Equal(1, executed);
        Assert.Equal(0, saved);
        Assert.False(coordinator.HasPendingTransition);
        Assert.Null(coordinator.Pending);
    }

    /// <summary>
    /// 验证全部受保护转场在场景脏时仅发布确认快照。
    /// </summary>
    [Theory]
    [InlineData((int)EditorTransitionKind.NewScene)]
    [InlineData((int)EditorTransitionKind.OpenScene)]
    [InlineData((int)EditorTransitionKind.OpenProject)]
    [InlineData((int)EditorTransitionKind.CreateProject)]
    [InlineData((int)EditorTransitionKind.CloseProject)]
    [InlineData((int)EditorTransitionKind.Exit)]
    public void DirtyTransitionPublishesPromptWithoutExecuting(int kindValue)
    {
        EditorTransitionKind kind = (EditorTransitionKind)kindValue;
        int executed = 0;
        EditorTransitionCoordinator coordinator = new(
            isDirty: static () => true,
            save: static () => EditorTransitionSaveResult.Success());

        EditorTransitionResult result = coordinator.Request(kind, () => executed++, " scenes/main.scene ");

        Assert.Equal(EditorTransitionStatus.ConfirmationRequired, result.Status);
        Assert.False(result.Executed);
        Assert.Equal(0, executed);
        Assert.True(coordinator.HasPendingTransition);
        EditorTransitionPrompt prompt = Assert.IsType<EditorTransitionPrompt>(coordinator.Pending);
        Assert.Equal(kind, prompt.Kind);
        Assert.Equal("scenes/main.scene", prompt.Target);
    }

    /// <summary>
    /// 验证保存成功后待处理转场只执行一次。
    /// </summary>
    [Fact]
    public void SaveSuccessExecutesPendingTransitionExactlyOnce()
    {
        int saved = 0;
        int executed = 0;
        EditorTransitionCoordinator coordinator = new(
            isDirty: static () => true,
            save: () =>
            {
                saved++;
                return EditorTransitionSaveResult.Success();
            });
        _ = coordinator.Request(EditorTransitionKind.OpenScene, () => executed++, "scenes/next.scene");

        EditorTransitionResult result = coordinator.Resolve(EditorTransitionDecision.Save);
        EditorTransitionResult second = coordinator.Resolve(EditorTransitionDecision.Save);

        Assert.Equal(EditorTransitionStatus.Executed, result.Status);
        Assert.Equal(EditorTransitionStatus.NoPendingTransition, second.Status);
        Assert.Equal(1, saved);
        Assert.Equal(1, executed);
        Assert.False(coordinator.HasPendingTransition);
    }

    /// <summary>
    /// 验证可恢复的保存失败会保留待处理转场且不执行 action。
    /// </summary>
    [Fact]
    public void SaveFailureKeepsPendingTransitionAndDoesNotExecuteAction()
    {
        int executed = 0;
        EditorTransitionCoordinator coordinator = new(
            isDirty: static () => true,
            save: static () => EditorTransitionSaveResult.Failure("磁盘已满"));
        _ = coordinator.Request(EditorTransitionKind.Exit, () => executed++);

        EditorTransitionResult result = coordinator.Resolve(EditorTransitionDecision.Save);

        Assert.Equal(EditorTransitionStatus.SaveFailed, result.Status);
        Assert.Equal("磁盘已满", result.Diagnostic);
        Assert.Equal(0, executed);
        Assert.True(coordinator.HasPendingTransition);
        Assert.Equal(EditorTransitionKind.Exit, coordinator.Pending?.Kind);
    }

    /// <summary>
    /// 验证意外保存异常向上传播，同时保留待处理转场供 UI 恢复。
    /// </summary>
    [Fact]
    public void SaveExceptionKeepsPendingTransitionAndPropagates()
    {
        int executed = 0;
        EditorTransitionCoordinator coordinator = new(
            isDirty: static () => true,
            save: static () => throw new IOException("write failed"));
        _ = coordinator.Request(EditorTransitionKind.CloseProject, () => executed++);

        IOException exception = Assert.Throws<IOException>(() => coordinator.Resolve(EditorTransitionDecision.Save));

        Assert.Equal("write failed", exception.Message);
        Assert.Equal(0, executed);
        Assert.True(coordinator.HasPendingTransition);
    }

    /// <summary>
    /// 验证 Discard 跳过保存并执行 action，而 Cancel 丢弃 action。
    /// </summary>
    [Fact]
    public void DiscardExecutesWithoutSavingAndCancelDropsPendingAction()
    {
        int saved = 0;
        int discardedAction = 0;
        int cancelledAction = 0;
        EditorTransitionCoordinator coordinator = new(
            isDirty: static () => true,
            save: () =>
            {
                saved++;
                return EditorTransitionSaveResult.Success();
            });

        _ = coordinator.Request(EditorTransitionKind.NewScene, () => discardedAction++);
        EditorTransitionResult discarded = coordinator.Resolve(EditorTransitionDecision.Discard);
        _ = coordinator.Request(EditorTransitionKind.CreateProject, () => cancelledAction++);
        EditorTransitionResult cancelled = coordinator.Resolve(EditorTransitionDecision.Cancel);

        Assert.Equal(EditorTransitionStatus.Executed, discarded.Status);
        Assert.Equal(EditorTransitionStatus.Cancelled, cancelled.Status);
        Assert.Equal(0, saved);
        Assert.Equal(1, discardedAction);
        Assert.Equal(0, cancelledAction);
        Assert.False(coordinator.HasPendingTransition);
    }

    /// <summary>
    /// 验证新的请求不能覆盖尚未确认的转场。
    /// </summary>
    [Fact]
    public void PendingTransitionCannotBeOverwrittenByAnotherRequest()
    {
        int firstAction = 0;
        int secondAction = 0;
        EditorTransitionCoordinator coordinator = new(
            isDirty: static () => true,
            save: static () => EditorTransitionSaveResult.Success());

        _ = coordinator.Request(EditorTransitionKind.OpenScene, () => firstAction++, "scenes/first.scene");
        EditorTransitionResult rejected = coordinator.Request(
            EditorTransitionKind.Exit,
            () => secondAction++,
            "application");
        EditorTransitionPrompt prompt = Assert.IsType<EditorTransitionPrompt>(coordinator.Pending);
        _ = coordinator.Resolve(EditorTransitionDecision.Discard);

        Assert.Equal(EditorTransitionStatus.PendingTransitionExists, rejected.Status);
        Assert.Equal(EditorTransitionKind.OpenScene, prompt.Kind);
        Assert.Equal("scenes/first.scene", prompt.Target);
        Assert.Equal(1, firstAction);
        Assert.Equal(0, secondAction);
    }
}
