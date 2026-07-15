using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;
using PixelEngine.Editor.Automation.Server;

namespace PixelEngine.Editor.Automation.Tests;

/// <summary>
/// 主线程 phase、revision、取消、幂等与 transaction/Undo 回归。
/// </summary>
#pragma warning disable xUnit1031 // 这些测试必须保持 scheduler 创建、drain 与 dispose 位于同一 OS 线程。
public sealed class AutomationMainThreadSchedulerTests
{
    private const string SetValueMethod = "test.state.set";
    private const string PreparedSetValueMethod = "test.state.prepared-set";
    private const string ResourceId = "test:state";

    /// <summary>验证 work 只在声明 phase 执行且一次写入产生一个 revision/Undo item。</summary>
    [Fact]
    public void WorkExecutesOnlyAtDeclaredPhaseAndAdvancesRevisionOnce()
    {
        TestState state = new();
        TestUndoSink undo = new();
        TestTransactionParticipant participant = new();
        using AutomationMainThreadScheduler scheduler = CreateScheduler(state, undo, participant);
        AutomationRequestContext context = CreateContext(
            expectedRevision: Expected(global: 0, resource: 0),
            idempotencyKey: "phase-write");
        Task<AutomationHandlerResult> pending = scheduler.HandleAsync(
            context,
            SetValueMethod,
            JsonSerializer.SerializeToElement(new { value = 42 }),
            CancellationToken.None).AsTask();

        Assert.True(scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress));
        Assert.Equal(0, scheduler.Drain(AutomationExecutionPhase.EngineInputAndTime));
        Assert.Equal(0, state.Value);
        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        AutomationHandlerResult result = pending.GetAwaiter().GetResult();

        Assert.Equal(42, state.Value);
        Assert.Equal(1, result.Revision?.GlobalRevision);
        Assert.Equal(1, Assert.Single(result.Revision!.Resources).Revision);
        IAutomationResourceScopedUndoAction scoped = Assert.IsAssignableFrom<IAutomationResourceScopedUndoAction>(
            Assert.Single(undo.Actions));
        Assert.Equal([ResourceId], scoped.ResourceIds);
        undo.Actions[0].Undo();
        Assert.Equal(0, state.Value);
        Assert.Equal(2, scheduler.Revisions.GlobalRevision);
        undo.Actions[0].Redo();
        Assert.Equal(42, state.Value);
        Assert.Equal(3, scheduler.Revisions.GlobalRevision);
    }

    /// <summary>Engine phase write 可禁止跨操作 transaction，但仍必须进入唯一 Undo/Redo 历史。</summary>
    [Fact]
    public void TransactionForbiddenWriteStillRecordsUndoAtDeclaredEnginePhase()
    {
        const string method = "test.engine.write";
        TestState state = new();
        TestUndoSink undo = new();
        AutomationMethodRegistration registration = new()
        {
            Descriptor = new AutomationMethodDescriptor
            {
                Method = method,
                RequiredScopes = [AutomationScopes.EditorControl],
                OperationKind = AutomationOperationKind.Write,
                ExecutionPhase = AutomationExecutionPhase.EngineWorldStreaming,
                TransactionMode = AutomationTransactionMode.Forbidden,
                RequiresExpectedRevision = true,
                RequiresIdempotencyKey = true,
            },
            Operation = (_, _) =>
            {
                int before = state.Value;
                state.Value = 17;
                return new AutomationOperationResult
                {
                    ResourceIds = [ResourceId],
                    UndoAction = new TestValueUndoAction(state, before, 17),
                };
            },
        };
        using AutomationMainThreadScheduler scheduler = new(
            [registration],
            new AutomationRevisionStore(),
            undo,
            new TestTransactionParticipant());

        Task<AutomationHandlerResult> pending = scheduler.HandleAsync(
            CreateContext(Expected(0, 0), "engine-write"),
            method,
            payload: null,
            CancellationToken.None).AsTask();
        Assert.Equal(0, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EngineWorldStreaming));
        AutomationHandlerResult result = pending.GetAwaiter().GetResult();

        Assert.Equal(17, state.Value);
        Assert.Equal(1, result.Revision?.GlobalRevision);
        IAutomationUndoAction action = Assert.Single(undo.Actions);
        action.Undo();
        Assert.Equal(0, state.Value);
        action.Redo();
        Assert.Equal(17, state.Value);

        AutomationRequestException exception = Assert.Throws<AutomationRequestException>(
            () => scheduler.HandleAsync(
                CreateContext(Expected(3, 3), "engine-write-transaction", "transaction-1"),
                method,
                payload: null,
                CancellationToken.None));
        Assert.Equal(AutomationErrorCodes.TransactionInvalid, exception.Error.Code);
    }

    /// <summary>幂等 Write 必须返回当前 revision，且不得登记 Undo 或推进全局/资源 revision。</summary>
    [Fact]
    public void NoChangeWriteDoesNotAdvanceRevisionOrRecordUndo()
    {
        TestState state = new();
        TestUndoSink undo = new();
        using AutomationMainThreadScheduler scheduler = CreateScheduler(
            state,
            undo,
            new TestTransactionParticipant());
        Task<AutomationHandlerResult> pending = scheduler.HandleAsync(
            CreateContext(Expected(0, 0), "no-change-write"),
            SetValueMethod,
            JsonSerializer.SerializeToElement(new { value = 0 }),
            CancellationToken.None).AsTask();

        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        AutomationHandlerResult result = pending.GetAwaiter().GetResult();

        Assert.Equal(0, state.Value);
        Assert.Equal(0, result.Revision?.GlobalRevision);
        Assert.Equal(0, Assert.Single(result.Revision!.Resources).Revision);
        Assert.Equal(0, scheduler.Revisions.GlobalRevision);
        Assert.Empty(undo.Actions);
    }

    /// <summary>只读 safe phase 可以冻结 revision 后把编码/I/O payload 延迟到 Server 后台。</summary>
    [Fact]
    public void ReadCanReturnDeferredPayloadWithoutExecutingItOnOwnerSafePoint()
    {
        bool factoryInvoked = false;
        AutomationMethodRegistration registration = new()
        {
            Descriptor = new AutomationMethodDescriptor
            {
                Method = "test.snapshot.export",
                Domain = "test",
                RequestSchema = "#/$defs/emptyRequest",
                ResponseSchema = "#/$defs/artifactReference",
                RequiredScopes = [AutomationScopes.EditorRead],
                SupportedModes = ["edit"],
                OperationKind = AutomationOperationKind.Read,
                ExecutionPhase = AutomationExecutionPhase.EditorIngress,
                TransactionMode = AutomationTransactionMode.Forbidden,
                ArtifactBehavior = AutomationArtifactBehavior.Required,
            },
            Operation = (_, _) => new AutomationOperationResult
            {
                ResourceIds = [ResourceId],
                DeferredPayloadFactory = (revision, _) =>
                {
                    factoryInvoked = true;
                    return ValueTask.FromResult<JsonElement?>(
                        JsonSerializer.SerializeToElement(new { revision = revision.GlobalRevision }));
                },
            },
        };
        using AutomationMainThreadScheduler scheduler = new(
            [registration],
            new AutomationRevisionStore(),
            new TestUndoSink(),
            new TestTransactionParticipant());
        Task<AutomationHandlerResult> pending = scheduler.HandleAsync(
            CreateContext(),
            registration.Descriptor.Method,
            payload: null,
            CancellationToken.None).AsTask();

        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        AutomationHandlerResult result = pending.GetAwaiter().GetResult();
        Assert.False(factoryInvoked);
        Assert.Null(result.Payload);
        Assert.NotNull(result.DeferredPayloadFactory);
        Assert.Equal(0, result.Revision?.GlobalRevision);

        JsonElement? payload = result.DeferredPayloadFactory!(
            result.Revision!,
            CancellationToken.None).AsTask().GetAwaiter().GetResult();
        Assert.True(factoryInvoked);
        Assert.Equal(0, payload?.GetProperty("revision").GetInt64());
    }

    /// <summary>不修改编辑器状态的 Command 可以冻结 session 后把制品 I/O 延迟到 Server 后台。</summary>
    [Fact]
    public void CommandCanReturnDeferredPayloadWithoutAdvancingRevision()
    {
        bool factoryInvoked = false;
        AutomationMethodRegistration registration = new()
        {
            Descriptor = new AutomationMethodDescriptor
            {
                Method = "test.artifact.delete",
                Domain = "test",
                RequestSchema = "#/$defs/artifactRequest",
                ResponseSchema = "#/$defs/artifactDeleteResult",
                RequiredScopes = [AutomationScopes.EditorControl],
                SupportedModes = ["edit", "play", "paused"],
                OperationKind = AutomationOperationKind.Command,
                ExecutionPhase = AutomationExecutionPhase.EditorIngress,
                TransactionMode = AutomationTransactionMode.Forbidden,
                ArtifactBehavior = AutomationArtifactBehavior.Required,
            },
            Operation = (_, _) => new AutomationOperationResult
            {
                ResourceIds = [ResourceId],
                DeferredPayloadFactory = (revision, _) =>
                {
                    factoryInvoked = true;
                    return ValueTask.FromResult<JsonElement?>(
                        JsonSerializer.SerializeToElement(new { revision = revision.GlobalRevision }));
                },
            },
        };
        using AutomationMainThreadScheduler scheduler = new(
            [registration],
            new AutomationRevisionStore(),
            new TestUndoSink(),
            new TestTransactionParticipant());
        Task<AutomationHandlerResult> pending = scheduler.HandleAsync(
            CreateContext(),
            registration.Descriptor.Method,
            payload: null,
            CancellationToken.None).AsTask();

        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        AutomationHandlerResult result = pending.GetAwaiter().GetResult();
        Assert.False(factoryInvoked);
        Assert.NotNull(result.DeferredPayloadFactory);
        Assert.Equal(0, result.Revision?.GlobalRevision);
        Assert.Equal(0, scheduler.Revisions.GlobalRevision);

        JsonElement? payload = result.DeferredPayloadFactory!(
            result.Revision!,
            CancellationToken.None).AsTask().GetAwaiter().GetResult();
        Assert.True(factoryInvoked);
        Assert.Equal(0, payload?.GetProperty("revision").GetInt64());
    }

    /// <summary>可回滚 Write 不得把任何工作延迟到 revision/Undo 提交点之后。</summary>
    [Fact]
    public void WriteCannotReturnDeferredPayload()
    {
        AutomationMethodRegistration registration = new()
        {
            Descriptor = new AutomationMethodDescriptor
            {
                Method = "test.state.deferred-set",
                Domain = "test",
                RequestSchema = "#/$defs/emptyRequest",
                ResponseSchema = "#/$defs/emptyRequest",
                RequiredScopes = [AutomationScopes.EditorControl],
                SupportedModes = ["edit"],
                OperationKind = AutomationOperationKind.Write,
                ExecutionPhase = AutomationExecutionPhase.EditorIngress,
                TransactionMode = AutomationTransactionMode.Optional,
                RequiresExpectedRevision = true,
                RequiresIdempotencyKey = true,
            },
            Operation = (_, _) => new AutomationOperationResult
            {
                ResourceIds = [ResourceId],
                DeferredPayloadFactory = static (_, _) => ValueTask.FromResult<JsonElement?>(null),
            },
        };
        using AutomationMainThreadScheduler scheduler = new(
            [registration],
            new AutomationRevisionStore(),
            new TestUndoSink(),
            new TestTransactionParticipant());
        Task<AutomationHandlerResult> pending = scheduler.HandleAsync(
            CreateContext(Expected(0, 0), "deferred-write"),
            registration.Descriptor.Method,
            payload: null,
            CancellationToken.None).AsTask();

        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => pending.GetAwaiter().GetResult());
        Assert.Contains("不得越过 revision/Undo", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, scheduler.Revisions.GlobalRevision);
    }

    /// <summary>后台 preparation 占用有界容量，完成后只在原 owner safe phase 提交。</summary>
    [Fact]
    public void BackgroundPreparationReentersOwnerPhaseAndRetainsCapacity()
    {
        const string method = "test.read.prepared";
        int ownerThreadId = Environment.CurrentManagedThreadId;
        int commitCount = 0;
        using ManualResetEventSlim workerStarted = new();
        TaskCompletionSource<string> releaseWorker = new(TaskCreationOptions.RunContinuationsAsynchronously);
        AutomationMethodRegistration registration = new()
        {
            Descriptor = new AutomationMethodDescriptor
            {
                Method = method,
                RequiredScopes = [AutomationScopes.EditorRead],
                OperationKind = AutomationOperationKind.Read,
                ExecutionPhase = AutomationExecutionPhase.EditorIngress,
                TransactionMode = AutomationTransactionMode.Forbidden,
                UsesBackgroundPreparation = true,
            },
            Preparation = (_, _) => new AutomationBackgroundPreparation
            {
                PrepareAsync = async cancellationToken =>
                {
                    workerStarted.Set();
                    return await releaseWorker.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                },
            },
            Operation = (context, _) =>
            {
                Assert.Equal(ownerThreadId, Environment.CurrentManagedThreadId);
                string prepared = context.RequirePreparedState<string>();
                commitCount++;
                return new AutomationOperationResult
                {
                    Payload = JsonSerializer.SerializeToElement(new { prepared }),
                    ResourceIds = [ResourceId],
                };
            },
        };
        using AutomationMainThreadScheduler scheduler = new(
            [registration],
            new AutomationRevisionStore(),
            new TestUndoSink(),
            new TestTransactionParticipant(),
            new AutomationMainThreadSchedulerOptions { Capacity = 1 });

        Task<AutomationHandlerResult> pending = scheduler.HandleAsync(
            CreateContext(),
            method,
            payload: null,
            CancellationToken.None).AsTask();
        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        Assert.True(workerStarted.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(pending.IsCompleted);
        AutomationRequestException capacity = Assert.Throws<AutomationRequestException>(() =>
        {
            _ = scheduler.HandleAsync(
                CreateContext(),
                method,
                payload: null,
                CancellationToken.None);
        });
        Assert.Equal(AutomationErrorCodes.Busy, capacity.Error.Code);

        releaseWorker.SetResult("ready");
        Assert.True(SpinWait.SpinUntil(
            () => scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
            TimeSpan.FromSeconds(5)));
        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        AutomationHandlerResult result = pending.GetAwaiter().GetResult();

        Assert.Equal(1, commitCount);
        Assert.Equal("ready", result.Payload?.GetProperty("prepared").GetString());
    }

    /// <summary>后台 preparation 使用有界常驻 worker，连续请求不会在 owner thread 执行同步前缀。</summary>
    [Fact]
    public void BackgroundPreparationReusesPersistentWorkerOutsideOwnerThread()
    {
        const string method = "test.read.persistent-preparation-worker";
        int ownerThreadId = Environment.CurrentManagedThreadId;
        List<int> preparationThreadIds = [];
        AutomationMethodRegistration registration = new()
        {
            Descriptor = new AutomationMethodDescriptor
            {
                Method = method,
                RequiredScopes = [AutomationScopes.EditorRead],
                OperationKind = AutomationOperationKind.Read,
                ExecutionPhase = AutomationExecutionPhase.EditorIngress,
                TransactionMode = AutomationTransactionMode.Forbidden,
                UsesBackgroundPreparation = true,
            },
            Preparation = (_, _) => new AutomationBackgroundPreparation
            {
                PrepareAsync = _ => ValueTask.FromResult<object?>(Environment.CurrentManagedThreadId),
            },
            Operation = (context, _) =>
            {
                preparationThreadIds.Add(context.RequirePreparedState<int>());
                return new AutomationOperationResult { ResourceIds = [ResourceId] };
            },
        };
        using AutomationMainThreadScheduler scheduler = new(
            [registration],
            new AutomationRevisionStore(),
            new TestUndoSink(),
            new TestTransactionParticipant(),
            new AutomationMainThreadSchedulerOptions
            {
                BackgroundPreparationWorkerCount = 1,
            });

        for (int i = 0; i < 2; i++)
        {
            Task<AutomationHandlerResult> pending = scheduler.HandleAsync(
                CreateContext(),
                method,
                payload: null,
                CancellationToken.None).AsTask();
            Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
            Assert.True(SpinWait.SpinUntil(
                () => scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
                TimeSpan.FromSeconds(5)));
            Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
            _ = pending.GetAwaiter().GetResult();
        }

        Assert.Equal(2, preparationThreadIds.Count);
        Assert.NotEqual(ownerThreadId, preparationThreadIds[0]);
        Assert.Equal(preparationThreadIds[0], preparationThreadIds[1]);
    }

    /// <summary>取消 preparation 会物理释放容量、只排一次 owner-thread cleanup，且不执行 commit。</summary>
    [Fact]
    public void CancellingBackgroundPreparationReleasesCapacityWithoutCommit()
    {
        const string method = "test.read.prepared-cancel";
        int preparationCount = 0;
        int commitCount = 0;
        int abortCount = 0;
        using ManualResetEventSlim firstWorkerStarted = new();
        AutomationMethodRegistration registration = new()
        {
            Descriptor = new AutomationMethodDescriptor
            {
                Method = method,
                RequiredScopes = [AutomationScopes.EditorRead],
                OperationKind = AutomationOperationKind.Read,
                ExecutionPhase = AutomationExecutionPhase.EditorIngress,
                TransactionMode = AutomationTransactionMode.Forbidden,
                UsesBackgroundPreparation = true,
            },
            Preparation = (_, _) =>
            {
                int ordinal = Interlocked.Increment(ref preparationCount);
                return new AutomationBackgroundPreparation
                {
                    PrepareAsync = async cancellationToken =>
                    {
                        if (ordinal == 1)
                        {
                            firstWorkerStarted.Set();
                            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                        }

                        return "ready";
                    },
                    AbortAtEditorIngress = () => abortCount++,
                };
            },
            Operation = (context, payload) =>
            {
                _ = payload;
                Assert.Equal("ready", context.RequirePreparedState<string>());
                commitCount++;
                return new AutomationOperationResult { ResourceIds = [ResourceId] };
            },
        };
        using AutomationMainThreadScheduler scheduler = new(
            [registration],
            new AutomationRevisionStore(),
            new TestUndoSink(),
            new TestTransactionParticipant(),
            new AutomationMainThreadSchedulerOptions { Capacity = 1 });
        using CancellationTokenSource cancellation = new();

        Task<AutomationHandlerResult> cancelled = scheduler.HandleAsync(
            CreateContext(),
            method,
            payload: null,
            cancellation.Token).AsTask();
        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        Assert.True(firstWorkerStarted.Wait(TimeSpan.FromSeconds(5)));
        cancellation.Cancel();
        _ = Assert.ThrowsAny<OperationCanceledException>(() => cancelled.GetAwaiter().GetResult());
        Assert.True(SpinWait.SpinUntil(
            () => scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
            TimeSpan.FromSeconds(5)));
        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        Assert.Equal(1, abortCount);
        Assert.Equal(0, commitCount);

        Task<AutomationHandlerResult> retry = scheduler.HandleAsync(
            CreateContext(),
            method,
            payload: null,
            CancellationToken.None).AsTask();
        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        Assert.True(SpinWait.SpinUntil(
            () => scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
            TimeSpan.FromSeconds(5)));
        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        _ = retry.GetAwaiter().GetResult();
        Assert.Equal(2, preparationCount);
        Assert.Equal(1, commitCount);
        Assert.Equal(1, abortCount);
    }

    /// <summary>关闭 scheduler 会取消运行中的 worker，并在 owner thread 执行一次 preparation cleanup。</summary>
    [Fact]
    public void DisposingSchedulerCancelsBackgroundPreparationAndRunsOwnerCleanup()
    {
        const string method = "test.read.prepared-dispose";
        int ownerThreadId = Environment.CurrentManagedThreadId;
        int abortThreadId = 0;
        int abortCount = 0;
        using ManualResetEventSlim workerStarted = new();
        using ManualResetEventSlim workerCancelled = new();
        AutomationMethodRegistration registration = new()
        {
            Descriptor = new AutomationMethodDescriptor
            {
                Method = method,
                RequiredScopes = [AutomationScopes.EditorRead],
                OperationKind = AutomationOperationKind.Read,
                ExecutionPhase = AutomationExecutionPhase.EditorIngress,
                TransactionMode = AutomationTransactionMode.Forbidden,
                UsesBackgroundPreparation = true,
            },
            Preparation = (_, _) => new AutomationBackgroundPreparation
            {
                PrepareAsync = async cancellationToken =>
                {
                    workerStarted.Set();
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        workerCancelled.Set();
                    }

                    return null;
                },
                AbortAtEditorIngress = () =>
                {
                    abortThreadId = Environment.CurrentManagedThreadId;
                    abortCount++;
                },
            },
            Operation = static (_, _) => throw new InvalidOperationException("关闭后不得提交 preparation。"),
        };
        AutomationMainThreadScheduler scheduler = new(
            [registration],
            new AutomationRevisionStore(),
            new TestUndoSink(),
            new TestTransactionParticipant());
        Task<AutomationHandlerResult> pending = scheduler.HandleAsync(
            CreateContext(),
            method,
            payload: null,
            CancellationToken.None).AsTask();
        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        Assert.True(workerStarted.Wait(TimeSpan.FromSeconds(5)));

        scheduler.Dispose();

        Exception exception = Assert.ThrowsAny<Exception>(
            () => pending.GetAwaiter().GetResult());
        Assert.Contains("scheduler", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, abortCount);
        Assert.Equal(ownerThreadId, abortThreadId);
        Assert.True(workerCancelled.Wait(TimeSpan.FromSeconds(5)));
    }

    /// <summary>同一 preparation scope/key 只冻结一次 workspace，并按逆注册顺序释放全部资源。</summary>
    [Fact]
    public void PreparationScopeReusesWorkspaceAndDisposesResourcesInReverseOrder()
    {
        List<string> disposed = [];
        AutomationPreparationScope scope = new();
        TestPreparationWorkspace first = scope.GetOrAdd(
            "first",
            () => new TestPreparationWorkspace("first", disposed));
        TestPreparationWorkspace reused = scope.GetOrAdd<TestPreparationWorkspace>(
            "first",
            () => throw new InvalidOperationException("复用 key 不得再次执行 factory。"));
        TestPreparationWorkspace second = scope.GetOrAdd(
            "second",
            () => new TestPreparationWorkspace("second", disposed));

        Assert.Same(first, reused);
        Assert.NotSame(first, second);
        Exception? ownerFailure = null;
        Thread worker = new(() =>
        {
            try
            {
                _ = scope.GetOrAdd("worker", () => first);
            }
            catch (Exception exception)
            {
                ownerFailure = exception;
            }
        });
        worker.Start();
        worker.Join();
        InvalidOperationException ownerException = Assert.IsType<InvalidOperationException>(ownerFailure);
        Assert.Contains("owner safe phase", ownerException.Message, StringComparison.Ordinal);

        scope.DisposeResources();
        scope.DisposeResources();
        Assert.Equal(["second", "first"], disposed);
        _ = Assert.Throws<ObjectDisposedException>(() => scope.GetOrAdd("late", () => first));
    }

    /// <summary>非事务 write 在 handler contract 校验失败时也必须撤回已经执行的 semantic action。</summary>
    [Fact]
    public void WriteContractFailureRollsBackAppliedActionBeforeCommit()
    {
        TestState state = new();
        TestUndoSink undo = new();
        using AutomationMainThreadScheduler scheduler = CreateScheduler(
            state,
            undo,
            new TestTransactionParticipant());
        Task<AutomationHandlerResult> pending = scheduler.HandleAsync(
            CreateContext(Expected(0, 0), "contract-failure-write"),
            SetValueMethod,
            JsonSerializer.SerializeToElement(new { value = int.MaxValue }),
            CancellationToken.None).AsTask();

        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        _ = Assert.Throws<InvalidOperationException>(() => pending.GetAwaiter().GetResult());

        Assert.Equal(0, state.Value);
        Assert.Equal(0, scheduler.Revisions.GlobalRevision);
        Assert.Empty(undo.Actions);
    }

    /// <summary>提交失败的 write 必须释放尚未移交给 Undo 历史的 semantic action。</summary>
    [Fact]
    public void WriteContractFailureDisposesUncommittedUndoAction()
    {
        const string method = "test.state.disposable-contract-failure";
        TestState state = new();
        TestUndoSink undo = new();
        DisposableTestUndoAction? action = null;
        AutomationMethodRegistration registration = new()
        {
            Descriptor = new AutomationMethodDescriptor
            {
                Method = method,
                RequiredScopes = [AutomationScopes.EditorControl],
                OperationKind = AutomationOperationKind.Write,
                ExecutionPhase = AutomationExecutionPhase.EditorIngress,
                TransactionMode = AutomationTransactionMode.Optional,
                RequiresIdempotencyKey = true,
            },
            Operation = (_, _) =>
            {
                state.Value = 23;
                action = new DisposableTestUndoAction(state, before: 0, after: 23);
                return new AutomationOperationResult
                {
                    UndoAction = action,
                    ResourceIds = [string.Empty],
                };
            },
        };
        using AutomationMainThreadScheduler scheduler = new(
            [registration],
            new AutomationRevisionStore(),
            undo,
            new TestTransactionParticipant());
        Task<AutomationHandlerResult> pending = scheduler.HandleAsync(
            CreateContext(idempotencyKey: "disposable-contract-failure"),
            method,
            payload: null,
            CancellationToken.None).AsTask();

        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        _ = Assert.Throws<ArgumentException>(() => pending.GetAwaiter().GetResult());

        DisposableTestUndoAction completedAction = Assert.IsType<DisposableTestUndoAction>(action);
        Assert.Equal(1, completedAction.UndoCount);
        Assert.Equal(1, completedAction.DisposeCount);
        Assert.Equal(0, state.Value);
        Assert.Equal(0, scheduler.Revisions.GlobalRevision);
        Assert.Empty(undo.Actions);
    }

    /// <summary>验证排队后发生 revision 变化时，执行前第二次校验拒绝 stale write。</summary>
    [Fact]
    public void RevisionIsCheckedAgainAtExecutionSafePoint()
    {
        TestState state = new();
        using AutomationMainThreadScheduler scheduler = CreateScheduler(
            state,
            new TestUndoSink(),
            new TestTransactionParticipant());
        Task<AutomationHandlerResult> pending = scheduler.HandleAsync(
            CreateContext(Expected(0, 0), "stale-at-execute"),
            SetValueMethod,
            JsonSerializer.SerializeToElement(new { value = 7 }),
            CancellationToken.None).AsTask();
        _ = scheduler.Revisions.Advance([ResourceId]);

        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        AutomationRequestException exception = Assert.Throws<AutomationRequestException>(
            () => pending.GetAwaiter().GetResult());

        Assert.Equal(AutomationErrorCodes.RevisionConflict, exception.Error.Code);
        Assert.Equal(0, state.Value);
    }

    /// <summary>验证取消会物理移除尚未执行 item 并立即释放有界容量。</summary>
    [Fact]
    public void CancellationRemovesQueuedWorkAndReleasesCapacity()
    {
        TestState state = new();
        using AutomationMainThreadScheduler scheduler = CreateScheduler(
            state,
            new TestUndoSink(),
            new TestTransactionParticipant(),
            new AutomationMainThreadSchedulerOptions { Capacity = 1 });
        using CancellationTokenSource cancellation = new();
        Task<AutomationHandlerResult> cancelled = scheduler.HandleAsync(
            CreateContext(Expected(0, 0), "cancelled"),
            SetValueMethod,
            JsonSerializer.SerializeToElement(new { value = 1 }),
            cancellation.Token).AsTask();

        Assert.Equal(1, scheduler.PendingCount);
        cancellation.Cancel();
        Assert.Equal(0, scheduler.PendingCount);

        Task<AutomationHandlerResult> replacement = scheduler.HandleAsync(
            CreateContext(Expected(0, 0), "replacement"),
            SetValueMethod,
            JsonSerializer.SerializeToElement(new { value = 2 }),
            CancellationToken.None).AsTask();
        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        _ = Assert.ThrowsAny<OperationCanceledException>(() => cancelled.GetAwaiter().GetResult());
        _ = replacement.GetAwaiter().GetResult();
        Assert.Equal(2, state.Value);
    }

    /// <summary>验证窗口 wake callback 失败不会留下调用方已无法观察的 queued mutation。</summary>
    [Fact]
    public void WakeFailureFaultsRequestAndRemovesQueuedWork()
    {
        TestState state = new();
        using AutomationMainThreadScheduler scheduler = CreateScheduler(
            state,
            new TestUndoSink(),
            new TestTransactionParticipant(),
            new AutomationMainThreadSchedulerOptions
            {
                Wake = static () => throw new InvalidOperationException("wake failed"),
            });

        Task<AutomationHandlerResult> rejected = scheduler.HandleAsync(
            CreateContext(Expected(0, 0), "wake-failure"),
            SetValueMethod,
            JsonSerializer.SerializeToElement(new { value = 9 }),
            CancellationToken.None).AsTask();

        Exception exception = Assert.ThrowsAny<Exception>(() => rejected.GetAwaiter().GetResult());
        Assert.Contains("wake signal", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, scheduler.PendingCount);
        Assert.False(scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress));
        Assert.Equal(0, state.Value);
    }

    /// <summary>验证 semantic mutation 后的 revision 边界失败会执行真实 Undo，且不污染统一历史。</summary>
    [Fact]
    public void RevisionCommitFailureRollsBackMutationWithoutRecordingUndo()
    {
        TestState state = new();
        TestUndoSink undo = new();
        AutomationRevisionStore revisions = new(long.MaxValue);
        using AutomationMainThreadScheduler scheduler = CreateScheduler(
            state,
            undo,
            new TestTransactionParticipant(),
            revisions: revisions);
        Task<AutomationHandlerResult> rejected = scheduler.HandleAsync(
            CreateContext(Expected(long.MaxValue, 0), "revision-overflow"),
            SetValueMethod,
            JsonSerializer.SerializeToElement(new { value = 11 }),
            CancellationToken.None).AsTask();

        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        _ = Assert.Throws<OverflowException>(() => rejected.GetAwaiter().GetResult());

        Assert.Equal(0, state.Value);
        Assert.Empty(undo.Actions);
        Assert.Equal(long.MaxValue, revisions.GlobalRevision);
    }

    /// <summary>验证 registration 的 method、scope 与行为标志在 Server 启动前一次收口。</summary>
    [Fact]
    public void InvalidRegistrationContractsAreRejectedBeforeServing()
    {
        AutomationMethodDescriptor valid = new()
        {
            Method = "test.read",
            RequiredScopes = [AutomationScopes.EditorRead],
            OperationKind = AutomationOperationKind.Read,
            ExecutionPhase = AutomationExecutionPhase.EditorIngress,
            TransactionMode = AutomationTransactionMode.Forbidden,
        };
        AutomationMethodDescriptor[] invalid =
        [
            valid with { Method = "1-invalid" },
            valid with { RequiredScopes = [] },
            valid with { RequiredScopes = [AutomationScopes.EditorRead, AutomationScopes.EditorRead] },
            valid with { OperationKind = (AutomationOperationKind)int.MaxValue },
            valid with { RequiresExpectedRevision = true },
            valid with { RequiresIdempotencyKey = true },
            valid with
            {
                RequiredScopes = [AutomationScopes.EditorControl],
                OperationKind = AutomationOperationKind.Write,
                ExecutionPhase = AutomationExecutionPhase.EngineWorldStreaming,
                TransactionMode = AutomationTransactionMode.Optional,
            },
        ];

        foreach (AutomationMethodDescriptor descriptor in invalid)
        {
            _ = Assert.Throws<ArgumentException>(() => new AutomationMainThreadScheduler(
                [new AutomationMethodRegistration
                {
                    Descriptor = descriptor,
                    Operation = static (_, _) => new AutomationOperationResult(),
                }],
                new AutomationRevisionStore(),
                new TestUndoSink(),
                new TestTransactionParticipant()));
        }
    }

    /// <summary>验证 background preparation 的并发度与关闭等待始终保持显式有界。</summary>
    [Fact]
    public void InvalidBackgroundPreparationWorkerOptionsAreRejectedBeforeStartingThreads()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => CreateScheduler(
            new TestState(),
            new TestUndoSink(),
            new TestTransactionParticipant(),
            new AutomationMainThreadSchedulerOptions
            {
                BackgroundPreparationWorkerCount = 0,
            }));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => CreateScheduler(
            new TestState(),
            new TestUndoSink(),
            new TestTransactionParticipant(),
            new AutomationMainThreadSchedulerOptions
            {
                BackgroundPreparationShutdownTimeout = TimeSpan.FromMinutes(2),
            }));
    }

    /// <summary>验证已进入 safe phase 且实际完成的 operation 不会被等待层伪报为已取消。</summary>
    [Fact]
    public void CancellationDuringExecutingOperationReportsTheCommittedResult()
    {
        const string method = "test.state.executing-cancel";
        TestState state = new();
        using ManualResetEventSlim started = new();
        AutomationMethodRegistration registration = new()
        {
            Descriptor = new AutomationMethodDescriptor
            {
                Method = method,
                RequiredScopes = [AutomationScopes.EditorControl],
                OperationKind = AutomationOperationKind.Write,
                ExecutionPhase = AutomationExecutionPhase.EditorIngress,
                TransactionMode = AutomationTransactionMode.Optional,
                RequiresIdempotencyKey = true,
            },
            Operation = (context, _) =>
            {
                started.Set();
                Assert.True(SpinWait.SpinUntil(
                    () => context.CancellationToken.IsCancellationRequested,
                    TimeSpan.FromSeconds(5)));
                state.Value = 17;
                return new AutomationOperationResult
                {
                    UndoAction = new TestValueUndoAction(state, before: 0, after: 17),
                    ResourceIds = [ResourceId],
                };
            },
        };
        using AutomationMainThreadScheduler scheduler = new(
            [registration],
            new AutomationRevisionStore(),
            new TestUndoSink(),
            new TestTransactionParticipant());
        using CancellationTokenSource cancellation = new();
        Task<AutomationHandlerResult> pending = scheduler.HandleAsync(
            CreateContext(idempotencyKey: "executing-cancel"),
            method,
            payload: null,
            cancellation.Token).AsTask();
        Thread cancellationThread = new(() =>
        {
            started.Wait();
            cancellation.Cancel();
        });

        cancellationThread.Start();
        try
        {
            Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        }
        finally
        {
            cancellationThread.Join();
        }

        AutomationHandlerResult result = pending.GetAwaiter().GetResult();
        Assert.Equal(17, state.Value);
        Assert.Equal(1, result.Revision?.GlobalRevision);
    }

    /// <summary>验证同一幂等请求跨 stale revision 重试不会重复执行，异内容复用 key 会冲突。</summary>
    [Fact]
    public void IdempotencyReplaysSuccessfulResultBeforeStaleRevisionValidation()
    {
        TestState state = new();
        TestUndoSink undo = new();
        using AutomationMainThreadScheduler scheduler = CreateScheduler(
            state,
            undo,
            new TestTransactionParticipant());
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 9 });
        AutomationRequestContext firstContext = CreateContext(Expected(0, 0), "same-write");
        Task<AutomationHandlerResult> first = scheduler.HandleAsync(
            firstContext,
            SetValueMethod,
            payload,
            CancellationToken.None).AsTask();
        _ = scheduler.Drain(AutomationExecutionPhase.EditorIngress);
        AutomationHandlerResult firstResult = first.GetAwaiter().GetResult();

        AutomationHandlerResult replay = scheduler.HandleAsync(
            CreateContext(Expected(0, 0), "same-write"),
            SetValueMethod,
            payload,
            CancellationToken.None).AsTask().GetAwaiter().GetResult();

        Assert.Equal(9, state.Value);
        Assert.Equal(firstResult.Revision, replay.Revision);
        Assert.Equal(0, scheduler.PendingCount);
        _ = Assert.Single(undo.Actions);

        AutomationRequestException conflict = Assert.Throws<AutomationRequestException>(() =>
            scheduler.HandleAsync(
                CreateContext(Expected(0, 0), "same-write"),
                SetValueMethod,
                JsonSerializer.SerializeToElement(new { value = 10 }),
                CancellationToken.None).AsTask().GetAwaiter().GetResult());
        Assert.Equal(AutomationErrorCodes.IdempotencyConflict, conflict.Error.Code);
    }

    /// <summary>验证幂等 in-flight 重复调用可取消自身等待，但不会取消唯一 semantic operation。</summary>
    [Fact]
    public void CancellingIdempotencyFollowerDoesNotCancelTheOwnerOperation()
    {
        TestState state = new();
        using AutomationMainThreadScheduler scheduler = CreateScheduler(
            state,
            new TestUndoSink(),
            new TestTransactionParticipant());
        JsonElement payload = JsonSerializer.SerializeToElement(new { value = 23 });
        Task<AutomationHandlerResult> owner = scheduler.HandleAsync(
            CreateContext(Expected(0, 0), "in-flight-owner"),
            SetValueMethod,
            payload,
            CancellationToken.None).AsTask();
        using CancellationTokenSource followerCancellation = new();
        Task<AutomationHandlerResult> follower = scheduler.HandleAsync(
            CreateContext(Expected(0, 0), "in-flight-owner"),
            SetValueMethod,
            payload,
            followerCancellation.Token).AsTask();

        followerCancellation.Cancel();
        _ = Assert.ThrowsAny<OperationCanceledException>(() => follower.GetAwaiter().GetResult());
        Assert.Equal(1, scheduler.PendingCount);
        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        _ = owner.GetAwaiter().GetResult();
        Assert.Equal(23, state.Value);
    }

    /// <summary>验证 retention 窗口内 cache 满载会 backpressure，而不会淘汰成功 key 后重复执行。</summary>
    [Fact]
    public void IdempotencyCapacityNeverEvictsSuccessfulResultBeforeRetention()
    {
        TestState state = new();
        TestUndoSink undo = new();
        using AutomationMainThreadScheduler scheduler = CreateScheduler(
            state,
            undo,
            new TestTransactionParticipant(),
            new AutomationMainThreadSchedulerOptions { IdempotencyCapacity = 1 });
        JsonElement firstPayload = JsonSerializer.SerializeToElement(new { value = 31 });
        Task<AutomationHandlerResult> first = scheduler.HandleAsync(
            CreateContext(Expected(0, 0), "retained-key"),
            SetValueMethod,
            firstPayload,
            CancellationToken.None).AsTask();
        _ = scheduler.Drain(AutomationExecutionPhase.EditorIngress);
        _ = first.GetAwaiter().GetResult();

        AutomationRequestException capacity = Assert.Throws<AutomationRequestException>(() =>
            scheduler.HandleAsync(
                CreateContext(Expected(1, 1), "new-key"),
                SetValueMethod,
                JsonSerializer.SerializeToElement(new { value = 32 }),
                CancellationToken.None).AsTask().GetAwaiter().GetResult());
        AutomationHandlerResult replay = scheduler.HandleAsync(
            CreateContext(Expected(0, 0), "retained-key"),
            SetValueMethod,
            firstPayload,
            CancellationToken.None).AsTask().GetAwaiter().GetResult();

        Assert.Equal(AutomationErrorCodes.Busy, capacity.Error.Code);
        Assert.True(capacity.Error.Transient);
        Assert.Equal(31, state.Value);
        Assert.Equal(1, replay.Revision?.GlobalRevision);
        _ = Assert.Single(undo.Actions);
    }

    /// <summary>验证错误线程 Dispose 只拒绝调用，不会先把 scheduler 永久标记为 disposed。</summary>
    [Fact]
    public void WrongThreadDisposeDoesNotPoisonOwnerThreadScheduler()
    {
        TestState state = new();
        using AutomationMainThreadScheduler scheduler = CreateScheduler(
            state,
            new TestUndoSink(),
            new TestTransactionParticipant());
        Exception? failure = null;
        Thread wrongThread = new(() =>
        {
            try
            {
                scheduler.Dispose();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        wrongThread.Start();
        wrongThread.Join();

        _ = Assert.IsType<InvalidOperationException>(failure);
        Task<AutomationHandlerResult> pending = scheduler.HandleAsync(
            CreateContext(Expected(0, 0), "after-wrong-dispose"),
            SetValueMethod,
            JsonSerializer.SerializeToElement(new { value = 37 }),
            CancellationToken.None).AsTask();
        _ = scheduler.Drain(AutomationExecutionPhase.EditorIngress);
        _ = pending.GetAwaiter().GetResult();
        Assert.Equal(37, state.Value);
    }

    /// <summary>验证多步 transaction commit 只产生一个 Undo item 和一次 revision 推进。</summary>
    [Fact]
    public void TransactionCommitMergesUndoAndRevision()
    {
        TestState state = new();
        TestUndoSink undo = new();
        TestTransactionParticipant participant = new();
        using AutomationMainThreadScheduler scheduler = CreateScheduler(state, undo, participant);
        AutomationTransactionInfo transaction = BeginTransaction(scheduler, "commit-transaction");

        AutomationTransactionStagedOperationInfo first = ExecuteTransactionalWrite(
            scheduler,
            transaction.TransactionId,
            "tx-write-1",
            3);
        AutomationTransactionStagedOperationInfo second = ExecuteTransactionalWrite(
            scheduler,
            transaction.TransactionId,
            "tx-write-2",
            8);
        Assert.Equal(0, first.Ordinal);
        Assert.Equal(1, second.Ordinal);
        Assert.Equal(0, state.Value);
        Assert.Equal(0, scheduler.Revisions.GlobalRevision);
        Assert.Empty(undo.Actions);

        Task<AutomationHandlerResult> commit = scheduler.HandleAsync(
            CreateContext(idempotencyKey: "tx-commit"),
            AutomationProtocolConstants.TransactionCommitMethod,
            JsonSerializer.SerializeToElement(new AutomationTransactionRequest
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                TransactionId = transaction.TransactionId,
            }, AutomationJsonContext.Default.AutomationTransactionRequest),
            CancellationToken.None).AsTask();
        _ = scheduler.Drain(AutomationExecutionPhase.EditorIngress);
        AutomationHandlerResult result = commit.GetAwaiter().GetResult();

        Assert.Equal(1, result.Revision?.GlobalRevision);
        AutomationTransactionCommitResult commitResult = result.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationTransactionCommitResult)
            ?? throw new InvalidOperationException("transaction.commit 未返回合并结果。");
        Assert.Equal(AutomationTransactionStatus.Committed, commitResult.Transaction.Status);
        Assert.Equal([first.OperationId, second.OperationId],
            commitResult.Operations.Select(static operation => operation.OperationId));
        Assert.Equal(8, state.Value);
        IAutomationUndoAction composite = Assert.Single(undo.Actions);
        Assert.Equal(0, participant.RestoreCount);
        composite.Undo();
        Assert.Equal(0, state.Value);
        composite.Redo();
        Assert.Equal(8, state.Value);
        Assert.Equal(2, participant.RestoreCount);
        Assert.Equal(3, scheduler.Revisions.GlobalRevision);
    }

    /// <summary>全为幂等操作的 transaction 可以提交，但不得创建空 Undo 或虚假 revision。</summary>
    [Fact]
    public void NoChangeTransactionCommitsWithoutUndoOrRevisionAdvance()
    {
        TestState state = new();
        TestUndoSink undo = new();
        using AutomationMainThreadScheduler scheduler = CreateScheduler(
            state,
            undo,
            new TestTransactionParticipant());
        AutomationTransactionInfo transaction = BeginTransaction(scheduler, "no-change-transaction");
        AutomationTransactionStagedOperationInfo staged = ExecuteTransactionalWrite(
            scheduler,
            transaction.TransactionId,
            "no-change-transaction-write",
            0);

        Task<AutomationHandlerResult> commit = scheduler.HandleAsync(
            CreateContext(idempotencyKey: "no-change-transaction-commit"),
            AutomationProtocolConstants.TransactionCommitMethod,
            JsonSerializer.SerializeToElement(new AutomationTransactionRequest
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                TransactionId = transaction.TransactionId,
            }, AutomationJsonContext.Default.AutomationTransactionRequest),
            CancellationToken.None).AsTask();
        _ = scheduler.Drain(AutomationExecutionPhase.EditorIngress);
        AutomationHandlerResult result = commit.GetAwaiter().GetResult();
        AutomationTransactionCommitResult commitResult = result.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationTransactionCommitResult)
            ?? throw new InvalidOperationException("transaction.commit 未返回结果。");

        Assert.Equal(AutomationTransactionStatus.Committed, commitResult.Transaction.Status);
        Assert.Equal(staged.OperationId, Assert.Single(commitResult.Operations).OperationId);
        Assert.Equal(0, result.Revision?.GlobalRevision);
        Assert.Equal(0, scheduler.Revisions.GlobalRevision);
        Assert.Equal(0, state.Value);
        Assert.Empty(undo.Actions);
    }

    /// <summary>验证连接关闭排队到 EditorIngress 后回滚 transaction 且不推进 revision。</summary>
    [Fact]
    public void SessionDisconnectRollsBackTransactionAtEditorIngress()
    {
        TestState state = new();
        TestUndoSink undo = new();
        TestTransactionParticipant participant = new();
        using AutomationMainThreadScheduler scheduler = CreateScheduler(state, undo, participant);
        AutomationTransactionInfo transaction = BeginTransaction(scheduler, "disconnect-transaction");
        _ = ExecuteTransactionalWrite(scheduler, transaction.TransactionId, "tx-disconnect-write", 13);
        Assert.Equal(0, state.Value);

        scheduler.OnSessionClosed(new AutomationSessionContext
        {
            SessionId = "session-1",
            PrincipalId = new string('a', 64),
            ClientInstanceId = "client-instance",
            ClientName = "scheduler-tests",
            GrantedScopes = [AutomationScopes.EditorRead, AutomationScopes.EditorControl],
        });
        Assert.True(scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress));
        _ = scheduler.Drain(AutomationExecutionPhase.EditorIngress);

        Assert.Equal(0, state.Value);
        Assert.Equal(0, scheduler.Revisions.GlobalRevision);
        Assert.Empty(undo.Actions);
        Assert.Equal(0, participant.RestoreCount);
    }

    /// <summary>验证 lease timer 只排队 signal，真正 expiry rollback 仍在 EditorIngress。</summary>
    [Fact]
    public void TransactionLeaseExpiryQueuesMainThreadRollback()
    {
        using AutoResetEvent wake = new(false);
        TestState state = new();
        TestTransactionParticipant participant = new();
        using AutomationMainThreadScheduler scheduler = CreateScheduler(
            state,
            new TestUndoSink(),
            participant,
            new AutomationMainThreadSchedulerOptions
            {
                DefaultTransactionLease = TimeSpan.FromMilliseconds(50),
                MaxTransactionLease = TimeSpan.FromSeconds(1),
                Wake = () => wake.Set(),
            });
        AutomationTransactionInfo transaction = BeginTransaction(
            scheduler,
            "expiring-transaction",
            leaseMilliseconds: 50);
        _ = ExecuteTransactionalWrite(scheduler, transaction.TransactionId, "tx-expiry-write", 21);
        Assert.Equal(0, state.Value);
        while (wake.WaitOne(0))
        {
        }

        Assert.True(wake.WaitOne(TimeSpan.FromSeconds(5)));
        TimeSpan remaining = transaction.ExpiresAtUtc - TimeProvider.System.GetUtcNow();
        if (remaining > TimeSpan.Zero)
        {
            // Windows timer 可能略早唤醒；跨过绝对 lease 截止点后再验证 safe-point expiry。
            Thread.Sleep(remaining + TimeSpan.FromMilliseconds(10));
        }

        _ = scheduler.Drain(AutomationExecutionPhase.EditorIngress);

        Assert.Equal(0, state.Value);
        Assert.Equal(0, participant.RestoreCount);
        AutomationTransactionInfo status = GetTransactionStatus(scheduler, transaction.TransactionId);
        Assert.Equal(AutomationTransactionStatus.Expired, status.Status);
    }

    /// <summary>验证略早到达的 timer signal 会按剩余 lease 重排，而不会提前过期。</summary>
    [Fact]
    public void EarlyTransactionExpirySignalDoesNotRevokeValidLease()
    {
        using AutomationTransactionManager transactions = new(
            new AutomationRevisionStore(),
            new TestUndoSink(),
            new TestTransactionParticipant(),
            TimeProvider.System,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30),
            maxOperations: 8,
            maxStagedBytes: 4096,
            requestExpiryRollback: static _ => { },
            publishSynchronousExpiry: static _ => { });
        AutomationTransactionInfo transaction = transactions.Begin(
            "early-signal-session",
            new AutomationTransactionBeginRequest
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                Name = "Early Signal",
                LeaseMilliseconds = 5000,
            });

        Assert.Null(transactions.Expire(transaction.TransactionId));
        Assert.True(transactions.HasActiveTransaction);
        Assert.Equal(
            AutomationTransactionStatus.Active,
            transactions.GetInfo("early-signal-session", transaction.TransactionId).Status);
        _ = transactions.Rollback("early-signal-session", transaction.TransactionId);
    }

    /// <summary>验证 commit 在执行任何 staged operation 前统一重验全部 optimistic precondition。</summary>
    [Fact]
    public void TransactionCommitValidatesAllPreconditionsBeforeMutation()
    {
        TestState state = new();
        TestUndoSink undo = new();
        TestTransactionParticipant participant = new();
        using AutomationMainThreadScheduler scheduler = CreateScheduler(state, undo, participant);
        AutomationTransactionInfo transaction = BeginTransaction(scheduler, "stale-transaction");
        _ = ExecuteTransactionalWrite(
            scheduler,
            transaction.TransactionId,
            "stale-transaction-write",
            17);
        _ = scheduler.Revisions.Advance([ResourceId]);

        Task<AutomationHandlerResult> commit = CommitTransaction(
            scheduler,
            transaction.TransactionId,
            "stale-transaction-commit");
        _ = scheduler.Drain(AutomationExecutionPhase.EditorIngress);
        AutomationRequestException exception = Assert.Throws<AutomationRequestException>(
            () => commit.GetAwaiter().GetResult());

        Assert.Equal(AutomationErrorCodes.TransactionFailed, exception.Error.Code);
        AutomationTransactionFailureDetails details = exception.Error.Details?.Deserialize(
            AutomationJsonContext.Default.AutomationTransactionFailureDetails)
            ?? throw new InvalidOperationException("transaction failure 缺少 details。");
        Assert.True(details.RollbackSucceeded);
        Assert.Equal(AutomationErrorCodes.RevisionConflict, details.Cause.Code);
        Assert.Equal(0, state.Value);
        Assert.Empty(undo.Actions);
        Assert.Equal(1, participant.RestoreCount);
        AutomationTransactionInfo status = GetTransactionStatus(scheduler, transaction.TransactionId);
        Assert.Equal(AutomationTransactionStatus.RolledBack, status.Status);
    }

    /// <summary>验证后续 operation 失败时，已执行的早先 action 被逆序恢复且 revision 不前进。</summary>
    [Fact]
    public void TransactionOperationFailureRestoresEarlierAppliedActions()
    {
        TestState state = new();
        TestUndoSink undo = new();
        TestTransactionParticipant participant = new();
        using AutomationMainThreadScheduler scheduler = CreateScheduler(state, undo, participant);
        AutomationTransactionInfo transaction = BeginTransaction(scheduler, "failing-transaction");
        _ = ExecuteTransactionalWrite(
            scheduler,
            transaction.TransactionId,
            "failing-transaction-write-1",
            23);
        AutomationTransactionStagedOperationInfo failing = ExecuteTransactionalWrite(
            scheduler,
            transaction.TransactionId,
            "failing-transaction-write-2",
            int.MinValue);

        Task<AutomationHandlerResult> commit = CommitTransaction(
            scheduler,
            transaction.TransactionId,
            "failing-transaction-commit");
        _ = scheduler.Drain(AutomationExecutionPhase.EditorIngress);
        AutomationRequestException exception = Assert.Throws<AutomationRequestException>(
            () => commit.GetAwaiter().GetResult());

        AutomationTransactionFailureDetails details = exception.Error.Details?.Deserialize(
            AutomationJsonContext.Default.AutomationTransactionFailureDetails)
            ?? throw new InvalidOperationException("transaction failure 缺少 details。");
        Assert.Equal(AutomationErrorCodes.TransactionFailed, exception.Error.Code);
        Assert.Equal(failing.OperationId, details.OperationId);
        Assert.Equal(AutomationErrorCodes.Internal, details.Cause.Code);
        Assert.True(details.RollbackSucceeded);
        Assert.Equal(0, state.Value);
        Assert.Equal(0, scheduler.Revisions.GlobalRevision);
        Assert.Empty(undo.Actions);
        Assert.Equal(1, participant.RestoreCount);
    }

    /// <summary>验证已执行 operation 返回非法 revision override 时，当前 action 本身也进入逆序 rollback。</summary>
    [Fact]
    public void TransactionContractFailureRollsBackCurrentAppliedAction()
    {
        TestState state = new();
        TestUndoSink undo = new();
        TestTransactionParticipant participant = new();
        using AutomationMainThreadScheduler scheduler = CreateScheduler(state, undo, participant);
        AutomationTransactionInfo transaction = BeginTransaction(scheduler, "contract-failure-transaction");
        _ = ExecuteTransactionalWrite(
            scheduler,
            transaction.TransactionId,
            "contract-failure-write",
            int.MaxValue);

        Task<AutomationHandlerResult> commit = CommitTransaction(
            scheduler,
            transaction.TransactionId,
            "contract-failure-commit");
        _ = scheduler.Drain(AutomationExecutionPhase.EditorIngress);
        AutomationRequestException exception = Assert.Throws<AutomationRequestException>(
            () => commit.GetAwaiter().GetResult());

        Assert.Equal(AutomationErrorCodes.TransactionFailed, exception.Error.Code);
        Assert.Equal(0, state.Value);
        Assert.Empty(undo.Actions);
        Assert.Equal(1, participant.RestoreCount);
        Assert.Equal(0, scheduler.Revisions.GlobalRevision);
    }

    /// <summary>
    /// transaction preparation 在 worker 期间不暴露状态、不释放租约；回到 EditorIngress 后只提交一次。
    /// </summary>
    [Fact]
    public void TransactionBackgroundPreparationCommitsAtomicallyAtOwnerSafePoint()
    {
        TestState state = new();
        TestUndoSink undo = new();
        TestTransactionParticipant participant = new();
        using ManualResetEventSlim workerStarted = new();
        using ManualResetEventSlim workerFinished = new();
        TaskCompletionSource<int> releaseWorker = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int abortCount = 0;
        using AutomationMainThreadScheduler scheduler = CreatePreparedScheduler(
            state,
            undo,
            participant,
            workerStarted,
            workerFinished,
            releaseWorker,
            () => abortCount++);
        AutomationTransactionInfo transaction = BeginTransaction(scheduler, "prepared-transaction");
        _ = ExecuteTransactionalWrite(
            scheduler,
            transaction.TransactionId,
            "prepared-transaction-write",
            7,
            method: PreparedSetValueMethod);
        Task<AutomationHandlerResult> commit = CommitTransaction(
            scheduler,
            transaction.TransactionId,
            "prepared-transaction-commit");

        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        Assert.True(workerStarted.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(commit.IsCompleted);
        Assert.Equal(0, state.Value);
        Assert.Equal(0, scheduler.Revisions.GlobalRevision);
        Assert.Empty(undo.Actions);

        Task<AutomationHandlerResult> lateStage = scheduler.HandleAsync(
            CreateContext(Expected(0, 0), "prepared-late-stage", transaction.TransactionId),
            PreparedSetValueMethod,
            JsonSerializer.SerializeToElement(new { value = 9 }),
            CancellationToken.None).AsTask();
        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        AutomationRequestException conflict = Assert.Throws<AutomationRequestException>(
            () => lateStage.GetAwaiter().GetResult());
        Assert.Equal(AutomationErrorCodes.TransactionConflict, conflict.Error.Code);

        releaseWorker.SetResult(14);
        Assert.True(workerFinished.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(SpinWait.SpinUntil(
            () => scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
            TimeSpan.FromSeconds(5)));
        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        AutomationHandlerResult result = commit.GetAwaiter().GetResult();
        AutomationTransactionCommitResult committed = result.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationTransactionCommitResult)
            ?? throw new InvalidOperationException("prepared transaction 未返回 commit result。");

        Assert.Equal(14, state.Value);
        Assert.Equal(1, result.Revision?.GlobalRevision);
        Assert.Equal(1, scheduler.Revisions.GlobalRevision);
        _ = Assert.Single(undo.Actions);
        Assert.True(Assert.Single(committed.Operations).StateChanged);
        Assert.Equal(0, abortCount);
        Assert.Equal(0, participant.RestoreCount);
        Assert.Equal(
            AutomationTransactionStatus.Committed,
            GetTransactionStatus(scheduler, transaction.TransactionId).Status);
    }

    /// <summary>取消运行中的 transaction preparation 会恢复 before-image、回滚 staging 且不执行 operation。</summary>
    [Fact]
    public void CancellingTransactionBackgroundPreparationRollsBackWithoutMutation()
    {
        TestState state = new();
        TestUndoSink undo = new();
        TestTransactionParticipant participant = new();
        using ManualResetEventSlim workerStarted = new();
        using ManualResetEventSlim workerFinished = new();
        TaskCompletionSource<int> releaseWorker = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int abortCount = 0;
        using AutomationMainThreadScheduler scheduler = CreatePreparedScheduler(
            state,
            undo,
            participant,
            workerStarted,
            workerFinished,
            releaseWorker,
            () => abortCount++);
        AutomationTransactionInfo transaction = BeginTransaction(scheduler, "cancel-prepared-transaction");
        _ = ExecuteTransactionalWrite(
            scheduler,
            transaction.TransactionId,
            "cancel-prepared-transaction-write",
            8,
            method: PreparedSetValueMethod);
        using CancellationTokenSource cancellation = new();
        Task<AutomationHandlerResult> commit = CommitTransaction(
            scheduler,
            transaction.TransactionId,
            "cancel-prepared-transaction-commit",
            cancellation.Token);
        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        Assert.True(workerStarted.Wait(TimeSpan.FromSeconds(5)));

        cancellation.Cancel();

        _ = Assert.ThrowsAny<OperationCanceledException>(() => commit.GetAwaiter().GetResult());
        Assert.True(workerFinished.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(SpinWait.SpinUntil(
            () => scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
            TimeSpan.FromSeconds(5)));
        _ = scheduler.Drain(AutomationExecutionPhase.EditorIngress);
        Assert.Equal(0, state.Value);
        Assert.Equal(0, scheduler.Revisions.GlobalRevision);
        Assert.Empty(undo.Actions);
        Assert.Equal(1, abortCount);
        Assert.Equal(1, participant.RestoreCount);
        Assert.Equal(
            AutomationTransactionStatus.RolledBack,
            GetTransactionStatus(scheduler, transaction.TransactionId).Status);
    }

    /// <summary>worker 返回后必须重验全部 staged revision；过期结果不得进入 semantic commit。</summary>
    [Fact]
    public void TransactionBackgroundPreparationRevalidatesRevisionBeforeCommit()
    {
        TestState state = new();
        TestUndoSink undo = new();
        TestTransactionParticipant participant = new();
        using ManualResetEventSlim workerStarted = new();
        using ManualResetEventSlim workerFinished = new();
        TaskCompletionSource<int> releaseWorker = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int abortCount = 0;
        using AutomationMainThreadScheduler scheduler = CreatePreparedScheduler(
            state,
            undo,
            participant,
            workerStarted,
            workerFinished,
            releaseWorker,
            () => abortCount++);
        AutomationTransactionInfo transaction = BeginTransaction(scheduler, "stale-prepared-transaction");
        _ = ExecuteTransactionalWrite(
            scheduler,
            transaction.TransactionId,
            "stale-prepared-transaction-write",
            10,
            method: PreparedSetValueMethod);
        Task<AutomationHandlerResult> commit = CommitTransaction(
            scheduler,
            transaction.TransactionId,
            "stale-prepared-transaction-commit");
        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        Assert.True(workerStarted.Wait(TimeSpan.FromSeconds(5)));

        _ = scheduler.Revisions.Advance([ResourceId]);
        releaseWorker.SetResult(20);
        Assert.True(workerFinished.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(SpinWait.SpinUntil(
            () => scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
            TimeSpan.FromSeconds(5)));
        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));

        AutomationRequestException exception = Assert.Throws<AutomationRequestException>(
            () => commit.GetAwaiter().GetResult());
        AutomationTransactionFailureDetails details = exception.Error.Details?.Deserialize(
            AutomationJsonContext.Default.AutomationTransactionFailureDetails)
            ?? throw new InvalidOperationException("prepared transaction failure 缺少 details。");
        Assert.Equal(AutomationErrorCodes.TransactionFailed, exception.Error.Code);
        Assert.Equal(AutomationErrorCodes.RevisionConflict, details.Cause.Code);
        Assert.True(details.RollbackSucceeded);
        Assert.Equal(0, state.Value);
        Assert.Equal(1, scheduler.Revisions.GlobalRevision);
        Assert.Empty(undo.Actions);
        Assert.Equal(1, abortCount);
        Assert.Equal(1, participant.RestoreCount);
        Assert.Equal(
            AutomationTransactionStatus.RolledBack,
            GetTransactionStatus(scheduler, transaction.TransactionId).Status);
    }

    /// <summary>验证 transaction staging 数量受全局配置约束，超限不执行也不挤占主线程队列。</summary>
    [Fact]
    public void TransactionStagingCapacityIsBounded()
    {
        TestState state = new();
        using AutomationMainThreadScheduler scheduler = CreateScheduler(
            state,
            new TestUndoSink(),
            new TestTransactionParticipant(),
            new AutomationMainThreadSchedulerOptions { MaxTransactionOperations = 1 });
        AutomationTransactionInfo transaction = BeginTransaction(scheduler, "bounded-transaction");
        _ = ExecuteTransactionalWrite(
            scheduler,
            transaction.TransactionId,
            "bounded-transaction-write-1",
            1);
        Task<AutomationHandlerResult> rejected = scheduler.HandleAsync(
            CreateContext(Expected(0, 0), "bounded-transaction-write-2", transaction.TransactionId),
            SetValueMethod,
            JsonSerializer.SerializeToElement(new { value = 2 }),
            CancellationToken.None).AsTask();

        _ = scheduler.Drain(AutomationExecutionPhase.EditorIngress);
        AutomationRequestException exception = Assert.Throws<AutomationRequestException>(
            () => rejected.GetAwaiter().GetResult());

        Assert.Equal(AutomationErrorCodes.Busy, exception.Error.Code);
        Assert.True(exception.Error.Transient);
        Assert.Equal(0, state.Value);
        AutomationTransactionInfo status = GetTransactionStatus(scheduler, transaction.TransactionId);
        Assert.Equal(1, status.OperationCount);
    }

    /// <summary>
    /// Capability registry、digest、分页与 HMAC cursor 必须来自同一组真实 registration，
    /// 且任意 token 篡改都以 revision_conflict fail closed。
    /// </summary>
    [Fact]
    public void CapabilityRegistryPublishesStableDigestAndRejectsTamperedCursor()
    {
        TestState state = new();
        using AutomationMainThreadScheduler scheduler = CreateScheduler(
            state,
            new TestUndoSink(),
            new TestTransactionParticipant());
        using AutomationMainThreadScheduler sameRegistry = CreateScheduler(
            new TestState(),
            new TestUndoSink(),
            new TestTransactionParticipant());

        Assert.Equal(scheduler.CapabilityDigest, sameRegistry.CapabilityDigest);
        Assert.Matches("^[0-9a-f]{64}$", scheduler.CapabilityDigest);
        AutomationCapabilityDescriptor[] descriptors = scheduler.CaptureCapabilities();
        Assert.Contains(descriptors, descriptor =>
            descriptor.Id == SetValueMethod &&
            descriptor.OperationKind == AutomationOperationKind.Write &&
            descriptor.RequiresExpectedRevision &&
            descriptor.RequiresIdempotencyKey);

        AutomationPageRequest firstRequest = new() { PageSize = 1 };
        Task<AutomationHandlerResult> firstPending = scheduler.HandleAsync(
            CreateContext(),
            AutomationProtocolConstants.CapabilityListMethod,
            JsonSerializer.SerializeToElement(
                firstRequest,
                AutomationJsonContext.Default.AutomationPageRequest),
            CancellationToken.None).AsTask();
        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        AutomationCapabilityListResponse first = firstPending.GetAwaiter().GetResult()
            .Payload?.Deserialize(AutomationJsonContext.Default.AutomationCapabilityListResponse)
            ?? throw new InvalidOperationException("capability list 未返回响应。");

        Assert.Equal(scheduler.CapabilityDigest, first.CapabilityDigest);
        Assert.Equal(1, first.Page.Returned);
        Assert.Equal(descriptors.Length, first.Page.Total);
        string cursor = Assert.IsType<string>(first.Page.NextCursor);
        char replacement = cursor[0] == 'A' ? 'B' : 'A';
        string tampered = replacement + cursor[1..];
        Task<AutomationHandlerResult> rejected = scheduler.HandleAsync(
            CreateContext(),
            AutomationProtocolConstants.CapabilityListMethod,
            JsonSerializer.SerializeToElement(
                firstRequest with { Cursor = tampered },
                AutomationJsonContext.Default.AutomationPageRequest),
            CancellationToken.None).AsTask();

        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        AutomationRequestException exception = Assert.Throws<AutomationRequestException>(
            () => rejected.GetAwaiter().GetResult());
        Assert.Equal(AutomationErrorCodes.RevisionConflict, exception.Error.Code);
    }

    /// <summary>
    /// 非事务 command 只要会改变权威状态，也必须在执行点复核 expected revision；
    /// 缺失或排队后变 stale 时 delegate 不得运行。
    /// </summary>
    [Fact]
    public void StateChangingCommandRequiresAndRechecksExpectedRevision()
    {
        const string method = "test.command.set";
        TestState state = new();
        AutomationMethodRegistration command = new()
        {
            Descriptor = new AutomationMethodDescriptor
            {
                Method = method,
                RequiredScopes = [AutomationScopes.EditorControl],
                OperationKind = AutomationOperationKind.Command,
                ExecutionPhase = AutomationExecutionPhase.EditorIngress,
                TransactionMode = AutomationTransactionMode.Forbidden,
                RequiresExpectedRevision = true,
                RequiresIdempotencyKey = true,
                EventTypes = [AutomationProtocolConstants.StateChangedEventType],
            },
            Operation = (context, _) =>
            {
                string[] resources = [ResourceId];
                context.Revisions.EnsureCanAdvance(resources);
                state.Value++;
                AutomationRevisionSnapshot revision = context.Revisions.Advance(resources);
                return new AutomationOperationResult
                {
                    Payload = JsonSerializer.SerializeToElement(new { state.Value }),
                    ResourceIds = resources,
                    RevisionOverride = revision,
                    StateChanged = true,
                };
            },
        };
        using AutomationMainThreadScheduler scheduler = new(
            [command],
            new AutomationRevisionStore(),
            new TestUndoSink(),
            new TestTransactionParticipant());

        Task<AutomationHandlerResult> missing = scheduler.HandleAsync(
            CreateContext(idempotencyKey: "command-missing-revision"),
            method,
            payload: null,
            CancellationToken.None).AsTask();
        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        AutomationRequestException missingException = Assert.Throws<AutomationRequestException>(
            () => missing.GetAwaiter().GetResult());
        Assert.Equal(AutomationErrorCodes.InvalidRequest, missingException.Error.Code);
        Assert.Equal(0, state.Value);

        Task<AutomationHandlerResult> stale = scheduler.HandleAsync(
            CreateContext(Expected(0, 0), "command-stale"),
            method,
            payload: null,
            CancellationToken.None).AsTask();
        _ = scheduler.Revisions.Advance([ResourceId]);
        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        AutomationRequestException staleException = Assert.Throws<AutomationRequestException>(
            () => stale.GetAwaiter().GetResult());
        Assert.Equal(AutomationErrorCodes.RevisionConflict, staleException.Error.Code);
        Assert.Equal(0, state.Value);

        Task<AutomationHandlerResult> accepted = scheduler.HandleAsync(
            CreateContext(Expected(1, 1), "command-accepted"),
            method,
            payload: null,
            CancellationToken.None).AsTask();
        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        AutomationHandlerResult result = accepted.GetAwaiter().GetResult();
        Assert.Equal(1, state.Value);
        Assert.Equal(2, result.Revision?.GlobalRevision);
    }

    /// <summary>
    /// 普通写与 transaction commit 同时发布通用及领域事件；staging 和 no-change 不泄漏伪事件。
    /// </summary>
    [Fact]
    public void WritesPublishDomainEventsOnlyAfterCommittedStateChange()
    {
        TestState state = new();
        using AutomationMainThreadScheduler scheduler = CreateScheduler(
            state,
            new TestUndoSink(),
            new TestTransactionParticipant());
        TestEventSink sink = new(capacity: 32);
        scheduler.OnSessionOpened(new AutomationSessionContext
        {
            SessionId = "session-1",
            PrincipalId = new string('a', 64),
            ClientInstanceId = "client-instance",
            ClientName = "scheduler-tests",
            GrantedScopes = [AutomationScopes.EditorRead, AutomationScopes.EditorControl],
        }, sink);
        _ = scheduler.Events.Subscribe("session-1", new AutomationEventSubscribeRequest
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            SubscriptionKey = "scheduler-domain-events",
            EventTypes = [],
            BacklogLimit = 32,
        });

        AutomationRequestContext writeContext = CreateContext(Expected(0, 0), "event-write");
        Task<AutomationHandlerResult> write = scheduler.HandleAsync(
            writeContext,
            SetValueMethod,
            JsonSerializer.SerializeToElement(new { value = 1 }),
            CancellationToken.None).AsTask();
        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        AutomationHandlerResult writeResult = write.GetAwaiter().GetResult();

        Assert.Equal(
            [
                AutomationProtocolConstants.StateChangedEventType,
                AutomationProtocolConstants.AssetsChangedEventType,
            ],
            sink.Events.Select(static eventRecord => eventRecord.EventType));
        Assert.All(sink.Events, eventRecord =>
        {
            Assert.Equal(writeContext.RequestId, eventRecord.CausationRequestId);
            Assert.Equal(writeResult.Revision?.GlobalRevision, eventRecord.StateRevision.GlobalRevision);
        });

        sink.Events.Clear();
        Task<AutomationHandlerResult> noChange = scheduler.HandleAsync(
            CreateContext(Expected(1, 1), "event-no-change"),
            SetValueMethod,
            JsonSerializer.SerializeToElement(new { value = 1 }),
            CancellationToken.None).AsTask();
        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        _ = noChange.GetAwaiter().GetResult();
        Assert.Empty(sink.Events);

        AutomationTransactionInfo transaction = BeginTransaction(scheduler, "event-transaction-begin");
        _ = ExecuteTransactionalWrite(
            scheduler,
            transaction.TransactionId,
            "event-staged-write",
            2,
            expectedRevision: 1);
        Assert.Empty(sink.Events);
        Task<AutomationHandlerResult> commit = CommitTransaction(
            scheduler,
            transaction.TransactionId,
            "event-transaction-commit");
        Assert.Equal(1, scheduler.Drain(AutomationExecutionPhase.EditorIngress));
        AutomationHandlerResult commitHandlerResult = commit.GetAwaiter().GetResult();
        AutomationTransactionCommitResult commitResult = commitHandlerResult.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationTransactionCommitResult)
            ?? throw new InvalidOperationException("transaction.commit 未返回结果。");

        Assert.True(Assert.Single(commitResult.Operations).StateChanged);
        Assert.Equal(
            [
                AutomationProtocolConstants.AssetsChangedEventType,
                AutomationProtocolConstants.StateChangedEventType,
                AutomationProtocolConstants.TransactionChangedEventType,
            ],
            sink.Events.Select(static eventRecord => eventRecord.EventType));
        Assert.Equal(commitResult.Operations[0].RequestId, sink.Events[0].CausationRequestId);
        Assert.All(sink.Events, eventRecord =>
            Assert.Equal(
                commitHandlerResult.Revision?.GlobalRevision,
                eventRecord.StateRevision.GlobalRevision));
    }

    private static AutomationMainThreadScheduler CreateScheduler(
        TestState state,
        TestUndoSink undo,
        TestTransactionParticipant participant,
        AutomationMainThreadSchedulerOptions? options = null,
        AutomationRevisionStore? revisions = null)
    {
        AutomationMethodRegistration registration = new()
        {
            Descriptor = new AutomationMethodDescriptor
            {
                Method = SetValueMethod,
                RequiredScopes = [AutomationScopes.EditorControl],
                OperationKind = AutomationOperationKind.Write,
                ExecutionPhase = AutomationExecutionPhase.EditorIngress,
                TransactionMode = AutomationTransactionMode.Optional,
                RequiresExpectedRevision = true,
                RequiresIdempotencyKey = true,
                EventTypes =
                [
                    AutomationProtocolConstants.StateChangedEventType,
                    AutomationProtocolConstants.AssetsChangedEventType,
                ],
            },
            Operation = (context, payload) =>
            {
                int next = payload?.GetProperty("value").GetInt32()
                    ?? throw new InvalidOperationException("测试 payload 缺少 value。");
                if (next == int.MinValue)
                {
                    throw new InvalidOperationException("测试 staged operation 故意失败。");
                }

                int previous = state.Value;
                if (next == previous)
                {
                    return new AutomationOperationResult
                    {
                        Payload = JsonSerializer.SerializeToElement(new { value = next }),
                        ResourceIds = [ResourceId],
                        WriteStateChanged = false,
                    };
                }

                state.Value = next;
                return new AutomationOperationResult
                {
                    Payload = JsonSerializer.SerializeToElement(new { value = next }),
                    UndoAction = new TestValueUndoAction(state, previous, next),
                    ResourceIds = [ResourceId],
                    RevisionOverride = next == int.MaxValue
                        ? context.Revisions.Capture([ResourceId])
                        : null,
                };
            },
        };
        return new AutomationMainThreadScheduler(
            [registration],
            revisions ?? new AutomationRevisionStore(),
            undo,
            participant,
            options);
    }

    private static AutomationMainThreadScheduler CreatePreparedScheduler(
        TestState state,
        TestUndoSink undo,
        TestTransactionParticipant participant,
        ManualResetEventSlim workerStarted,
        ManualResetEventSlim workerFinished,
        TaskCompletionSource<int> releaseWorker,
        Action abort)
    {
        AutomationMethodRegistration registration = new()
        {
            Descriptor = new AutomationMethodDescriptor
            {
                Method = PreparedSetValueMethod,
                RequiredScopes = [AutomationScopes.EditorControl],
                OperationKind = AutomationOperationKind.Write,
                ExecutionPhase = AutomationExecutionPhase.EditorIngress,
                TransactionMode = AutomationTransactionMode.Optional,
                RequiresExpectedRevision = true,
                RequiresIdempotencyKey = true,
                UsesBackgroundPreparation = true,
                EventTypes = [AutomationProtocolConstants.StateChangedEventType],
            },
            Preparation = (context, payload) =>
            {
                _ = context;
                _ = payload?.GetProperty("value").GetInt32()
                    ?? throw new InvalidOperationException("测试 payload 缺少 value。");
                return new AutomationBackgroundPreparation
                {
                    PrepareAsync = async cancellationToken =>
                    {
                        workerStarted.Set();
                        try
                        {
                            return await releaseWorker.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                        }
                        finally
                        {
                            workerFinished.Set();
                        }
                    },
                    AbortAtEditorIngress = abort,
                };
            },
            Operation = (context, _) =>
            {
                int next = context.RequirePreparedState<int>();
                int previous = state.Value;
                state.Value = next;
                return new AutomationOperationResult
                {
                    Payload = JsonSerializer.SerializeToElement(new { value = next }),
                    UndoAction = new TestValueUndoAction(state, previous, next),
                    ResourceIds = [ResourceId],
                };
            },
        };
        return new AutomationMainThreadScheduler(
            [registration],
            new AutomationRevisionStore(),
            undo,
            participant);
    }

    private static AutomationTransactionInfo BeginTransaction(
        AutomationMainThreadScheduler scheduler,
        string idempotencyKey,
        int leaseMilliseconds = 0)
    {
        AutomationTransactionBeginRequest request = new()
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Name = "Batch Set Value",
            LeaseMilliseconds = leaseMilliseconds,
        };
        Task<AutomationHandlerResult> pending = scheduler.HandleAsync(
            CreateContext(idempotencyKey: idempotencyKey),
            AutomationProtocolConstants.TransactionBeginMethod,
            JsonSerializer.SerializeToElement(
                request,
                AutomationJsonContext.Default.AutomationTransactionBeginRequest),
            CancellationToken.None).AsTask();
        _ = scheduler.Drain(AutomationExecutionPhase.EditorIngress);
        AutomationHandlerResult result = pending.GetAwaiter().GetResult();
        return result.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationTransactionInfo)
            ?? throw new InvalidOperationException("transaction.begin 未返回 info。");
    }

    private static AutomationTransactionStagedOperationInfo ExecuteTransactionalWrite(
        AutomationMainThreadScheduler scheduler,
        string transactionId,
        string idempotencyKey,
        int value,
        long expectedRevision = 0,
        string method = SetValueMethod)
    {
        Task<AutomationHandlerResult> pending = scheduler.HandleAsync(
            CreateContext(Expected(expectedRevision, expectedRevision), idempotencyKey, transactionId),
            method,
            JsonSerializer.SerializeToElement(new { value }),
            CancellationToken.None).AsTask();
        _ = scheduler.Drain(AutomationExecutionPhase.EditorIngress);
        AutomationHandlerResult result = pending.GetAwaiter().GetResult();
        Assert.Equal(expectedRevision, result.Revision?.GlobalRevision);
        return result.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationTransactionStagedOperationInfo)
            ?? throw new InvalidOperationException("transaction write 未返回 staging 回执。");
    }

    private static AutomationTransactionInfo GetTransactionStatus(
        AutomationMainThreadScheduler scheduler,
        string transactionId)
    {
        Task<AutomationHandlerResult> pending = scheduler.HandleAsync(
            CreateContext(),
            AutomationProtocolConstants.TransactionStatusMethod,
            JsonSerializer.SerializeToElement(new AutomationTransactionRequest
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                TransactionId = transactionId,
            }, AutomationJsonContext.Default.AutomationTransactionRequest),
            CancellationToken.None).AsTask();
        _ = scheduler.Drain(AutomationExecutionPhase.EditorIngress);
        AutomationHandlerResult result = pending.GetAwaiter().GetResult();
        return result.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationTransactionInfo)
            ?? throw new InvalidOperationException("transaction.status 未返回 info。");
    }

    private static Task<AutomationHandlerResult> CommitTransaction(
        AutomationMainThreadScheduler scheduler,
        string transactionId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.HandleAsync(
            CreateContext(idempotencyKey: idempotencyKey),
            AutomationProtocolConstants.TransactionCommitMethod,
            JsonSerializer.SerializeToElement(new AutomationTransactionRequest
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                TransactionId = transactionId,
            }, AutomationJsonContext.Default.AutomationTransactionRequest),
            cancellationToken).AsTask();
    }

    private static AutomationRequestContext CreateContext(
        AutomationRevisionPrecondition? expectedRevision = null,
        string? idempotencyKey = null,
        string? transactionId = null)
    {
        string requestId = Guid.NewGuid().ToString("N");
        return new AutomationRequestContext(
            requestId,
            requestId,
            "session-1",
            new string('a', 64),
            "client-instance",
            "scheduler-tests",
            [AutomationScopes.EditorRead, AutomationScopes.EditorControl],
            DateTimeOffset.UtcNow.AddMinutes(1),
            expectedRevision,
            idempotencyKey,
            transactionId);
    }

    private static AutomationRevisionPrecondition Expected(long global, long resource)
    {
        return new AutomationRevisionPrecondition
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            GlobalRevision = global,
            Resources =
            [
                new AutomationExpectedResourceRevision
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    ResourceId = ResourceId,
                    Revision = resource,
                },
            ],
        };
    }

    private sealed class TestState
    {
        public int Value { get; set; }
    }

    private sealed class TestPreparationWorkspace(string name, List<string> disposed) : IDisposable
    {
        public void Dispose()
        {
            disposed.Add(name);
        }
    }

    private sealed class TestValueUndoAction(TestState state, int before, int after) : IAutomationUndoAction
    {
        public string Name => "Set Test Value";

        public void Undo()
        {
            state.Value = before;
        }

        public void Redo()
        {
            state.Value = after;
        }
    }

    private sealed class DisposableTestUndoAction(TestState state, int before, int after) :
        IAutomationUndoAction,
        IDisposable
    {
        public string Name => "Set Disposable Test Value";

        public int UndoCount { get; private set; }

        public int DisposeCount { get; private set; }

        public void Undo()
        {
            UndoCount++;
            state.Value = before;
        }

        public void Redo()
        {
            state.Value = after;
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    private sealed class TestUndoSink : IAutomationUndoSink
    {
        public List<IAutomationUndoAction> Actions { get; } = [];

        public void RecordExecuted(IAutomationUndoAction action)
        {
            Actions.Add(action);
        }
    }

    private sealed class TestTransactionParticipant : IAutomationTransactionParticipant
    {
        public int RestoreCount { get; private set; }

        public object CaptureState()
        {
            return new object();
        }

        public void RestoreState(object state)
        {
            ArgumentNullException.ThrowIfNull(state);
            RestoreCount++;
        }
    }

    private sealed class TestEventSink(int capacity) : IAutomationEventSink
    {
        public List<AutomationEventRecord> Events { get; } = [];

        public bool TryPublish(AutomationEventRecord eventRecord)
        {
            if (Events.Count >= capacity)
            {
                return false;
            }

            Events.Add(eventRecord);
            return true;
        }

        public void Abort()
        {
        }
    }
}
#pragma warning restore xUnit1031
