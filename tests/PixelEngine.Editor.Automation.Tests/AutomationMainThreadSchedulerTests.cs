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
        _ = Assert.Single(undo.Actions);
        undo.Actions[0].Undo();
        Assert.Equal(0, state.Value);
        Assert.Equal(2, scheduler.Revisions.GlobalRevision);
        undo.Actions[0].Redo();
        Assert.Equal(42, state.Value);
        Assert.Equal(3, scheduler.Revisions.GlobalRevision);
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
                TransactionMode = AutomationTransactionMode.Forbidden,
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
        int value)
    {
        Task<AutomationHandlerResult> pending = scheduler.HandleAsync(
            CreateContext(Expected(0, 0), idempotencyKey, transactionId),
            SetValueMethod,
            JsonSerializer.SerializeToElement(new { value }),
            CancellationToken.None).AsTask();
        _ = scheduler.Drain(AutomationExecutionPhase.EditorIngress);
        AutomationHandlerResult result = pending.GetAwaiter().GetResult();
        Assert.Equal(0, result.Revision?.GlobalRevision);
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
        string idempotencyKey)
    {
        return scheduler.HandleAsync(
            CreateContext(idempotencyKey: idempotencyKey),
            AutomationProtocolConstants.TransactionCommitMethod,
            JsonSerializer.SerializeToElement(new AutomationTransactionRequest
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                TransactionId = transactionId,
            }, AutomationJsonContext.Default.AutomationTransactionRequest),
            CancellationToken.None).AsTask();
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
}
#pragma warning restore xUnit1031
