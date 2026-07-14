using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Server;

/// <summary>
/// 在 Editor 主线程维护互斥 transaction lease、有界 staging、原子 commit 与单一 Undo 合并。
/// transaction write 在 commit 前绝不执行，因而不会把中间态暴露给 UI 或其他会话。
/// </summary>
public sealed class AutomationTransactionManager : IDisposable
{
    private const int CompletedHistoryLimit = 256;
    private readonly AutomationRevisionStore _revisions;
    private readonly IAutomationUndoSink _undoSink;
    private readonly IAutomationTransactionParticipant _participant;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _defaultLease;
    private readonly TimeSpan _maxLease;
    private readonly int _maxOperations;
    private readonly int _maxStagedBytes;
    private readonly Action<string> _requestExpiryRollback;
    private readonly Action<AutomationTransactionInfo> _publishSynchronousExpiry;
    private readonly int _ownerThreadId;
    private readonly Dictionary<string, AutomationTransactionInfo> _completed = new(StringComparer.Ordinal);
    private readonly Queue<string> _completedOrder = new();
    private TransactionState? _active;
    private bool _disposed;

    /// <summary>
    /// 创建 transaction manager；所有状态变更方法必须由当前线程调用。
    /// </summary>
    public AutomationTransactionManager(
        AutomationRevisionStore revisions,
        IAutomationUndoSink undoSink,
        IAutomationTransactionParticipant participant,
        TimeProvider timeProvider,
        TimeSpan defaultLease,
        TimeSpan maxLease,
        int maxOperations,
        int maxStagedBytes,
        Action<string> requestExpiryRollback,
        Action<AutomationTransactionInfo> publishSynchronousExpiry)
    {
        _revisions = revisions ?? throw new ArgumentNullException(nameof(revisions));
        _undoSink = undoSink ?? throw new ArgumentNullException(nameof(undoSink));
        _participant = participant ?? throw new ArgumentNullException(nameof(participant));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(defaultLease, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLease, defaultLease);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxOperations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxStagedBytes);
        _defaultLease = defaultLease;
        _maxLease = maxLease;
        _maxOperations = maxOperations;
        _maxStagedBytes = maxStagedBytes;
        _requestExpiryRollback = requestExpiryRollback ?? throw new ArgumentNullException(nameof(requestExpiryRollback));
        _publishSynchronousExpiry = publishSynchronousExpiry ??
            throw new ArgumentNullException(nameof(publishSynchronousExpiry));
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    /// <summary>是否存在 active transaction。</summary>
    public bool HasActiveTransaction
    {
        get
        {
            AssertOwnerThread();
            return _active is not null;
        }
    }

    /// <summary>确认当前 write 是否持有或避开全局互斥 transaction 租约。</summary>
    public void EnsureWriteAllowed(string sessionId, string? transactionId)
    {
        AssertUsable();
        if (_active is { } lease && TryExpireLease(lease))
        {
            if (string.IsNullOrWhiteSpace(transactionId))
            {
                return;
            }

            throw InvalidTransactionRequest("请求引用的 transaction lease 已过期并回滚。");
        }

        if (_active is null)
        {
            if (!string.IsNullOrWhiteSpace(transactionId))
            {
                throw InvalidTransactionRequest("请求引用的 transaction 不存在或已结束。");
            }

            return;
        }

        if (string.Equals(_active.SessionId, sessionId, StringComparison.Ordinal) &&
            string.Equals(_active.TransactionId, transactionId, StringComparison.Ordinal))
        {
            return;
        }

        throw TransactionConflict("另一个 transaction 正持有 Editor 写租约。");
    }

    /// <summary>开始全局互斥 transaction 并捕获完整 before image。</summary>
    public AutomationTransactionInfo Begin(string sessionId, AutomationTransactionBeginRequest request)
    {
        AssertUsable();
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(request);
        if (request.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
            string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 128 ||
            request.Name.Any(char.IsControl) || request.LeaseMilliseconds < 0)
        {
            throw InvalidTransactionRequest("transaction.begin payload 无效。");
        }

        if (_active is not null)
        {
            throw TransactionConflict("已有 transaction 持有 Editor 写租约。");
        }

        TimeSpan requestedLease = request.LeaseMilliseconds == 0
            ? _defaultLease
            : TimeSpan.FromMilliseconds(request.LeaseMilliseconds);
        if (requestedLease > _maxLease)
        {
            throw InvalidTransactionRequest($"transaction lease 不得超过 {_maxLease}。");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        _ = _participant.CaptureState()
            ?? throw new InvalidOperationException("Automation transaction participant 返回了 null state。");
        AutomationRevisionSnapshot baseRevision = _revisions.CaptureAll();
        TransactionState state = new()
        {
            TransactionId = Guid.NewGuid().ToString("N"),
            SessionId = sessionId,
            Name = request.Name,
            CreatedAtUtc = now,
            ExpiresAtUtc = now + requestedLease,
            BaseRevision = baseRevision,
        };
        ITimer timer = _timeProvider.CreateTimer(
            static callbackState =>
            {
                ExpiryCallbackState expiry = (ExpiryCallbackState)callbackState!;
                expiry.Callback(expiry.TransactionId);
            },
            new ExpiryCallbackState(state.TransactionId, _requestExpiryRollback),
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        state.Timer = timer;
        if (!timer.Change(requestedLease, Timeout.InfiniteTimeSpan))
        {
            timer.Dispose();
            throw new InvalidOperationException("Automation transaction lease timer 无法启动。");
        }

        _active = state;
        return ToInfo(state, AutomationTransactionStatus.Active);
    }

    /// <summary>
    /// 接纳一个尚未执行的 reversible write。payload 已在 I/O 线程冻结，byte count 已在入队前计算。
    /// </summary>
    public AutomationTransactionStagedOperationInfo Stage(
        AutomationRequestContext context,
        AutomationMethodRegistration registration,
        JsonElement? payload,
        int stagedBytes)
    {
        AssertUsable();
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentOutOfRangeException.ThrowIfNegative(stagedBytes);
        TransactionState state = RequireActiveOwner(
            context.SessionId,
            context.TransactionId ?? string.Empty);
        EnsureLeaseActive(state);
        AutomationMethodDescriptor descriptor = registration.Descriptor;
        if (descriptor.OperationKind != AutomationOperationKind.Write ||
            descriptor.TransactionMode == AutomationTransactionMode.Forbidden ||
            descriptor.ExecutionPhase != AutomationExecutionPhase.EditorIngress)
        {
            throw new InvalidOperationException(
                $"Transaction method '{descriptor.Method}' 必须是 EditorIngress reversible write。");
        }

        if (state.StagedOperations.Count >= _maxOperations)
        {
            throw TransactionCapacity("transaction staged operation 数量已达上限。");
        }

        if (stagedBytes > _maxStagedBytes - state.StagedBytes)
        {
            throw TransactionCapacity("transaction staged payload/precondition 字节已达上限。");
        }

        if (context.ExpectedRevision is { } stagedExpected)
        {
            int additionalResourceCount = 0;
            for (int i = 0; i < stagedExpected.Resources.Length; i++)
            {
                if (!state.ResourceIds.Contains(stagedExpected.Resources[i].ResourceId))
                {
                    additionalResourceCount++;
                }
            }

            if (additionalResourceCount > AutomationProtocolConstants.MaxRevisionResources - state.ResourceIds.Count)
            {
                throw TransactionCapacity(
                    $"transaction resource 数不得超过 {AutomationProtocolConstants.MaxRevisionResources}。");
            }
        }

        StagedOperation operation = new()
        {
            OperationId = Guid.NewGuid().ToString("N"),
            Ordinal = state.StagedOperations.Count,
            Request = context,
            Registration = registration,
            Payload = payload,
            AcceptedAtUtc = _timeProvider.GetUtcNow(),
        };
        state.StagedOperations.Add(operation);
        state.StagedBytes = checked(state.StagedBytes + stagedBytes);
        if (context.ExpectedRevision is { } expected)
        {
            for (int i = 0; i < expected.Resources.Length; i++)
            {
                _ = state.ResourceIds.Add(expected.Resources[i].ResourceId);
            }
        }

        return new AutomationTransactionStagedOperationInfo
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            OperationId = operation.OperationId,
            TransactionId = state.TransactionId,
            Ordinal = operation.Ordinal,
            Method = descriptor.Method,
            AcceptedAtUtc = operation.AcceptedAtUtc,
        };
    }

    /// <summary>
    /// 在一个 EditorIngress safe point 先校验全部 precondition，再连续执行全部 staged operation。
    /// 任何失败都逆序 Undo 已执行 action 并恢复 participant before image。
    /// </summary>
    public (AutomationTransactionCommitResult Result, AutomationRevisionSnapshot Revision) Commit(
        string sessionId,
        string transactionId,
        CancellationToken cancellationToken)
    {
        AssertUsable();
        TransactionState state = RequireActiveOwner(sessionId, transactionId);
        EnsureLeaseActive(state);
        List<IAutomationUndoAction> applied = [];
        StagedOperation? current = null;
        object? commitBeforeState = null;
        try
        {
            commitBeforeState = _participant.CaptureState()
                ?? throw new InvalidOperationException("Automation transaction participant 返回了 null state。");
            for (int i = 0; i < state.StagedOperations.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                current = state.StagedOperations[i];
                if (current.Request.ExpectedRevision is { } expected)
                {
                    _revisions.Validate(expected);
                }
            }

            AutomationTransactionOperationResult[] results =
                new AutomationTransactionOperationResult[state.StagedOperations.Count];
            for (int i = 0; i < state.StagedOperations.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                current = state.StagedOperations[i];
                AutomationScheduledContext scheduledContext = new(
                    current.Request,
                    cancellationToken,
                    _revisions);
                AutomationOperationResult operationResult = current.Registration.Operation(
                    scheduledContext,
                    current.Payload) ?? throw new InvalidOperationException(
                    $"Transaction method '{current.Registration.Descriptor.Method}' 返回了 null result。");
                if (operationResult.WriteStateChanged)
                {
                    if (operationResult.UndoAction is null)
                    {
                        throw new InvalidOperationException(
                            $"Transaction method '{current.Registration.Descriptor.Method}' 必须返回真实 Undo action。");
                    }

                    // operation 已经修改权威状态，先登记 rollback action，再验证其余 handler contract。
                    applied.Add(operationResult.UndoAction);
                }
                else if (operationResult.UndoAction is not null)
                {
                    throw new InvalidOperationException(
                        $"No-change transaction method '{current.Registration.Descriptor.Method}' 不得返回 Undo action。");
                }

                if (operationResult.RevisionOverride is not null)
                {
                    throw new InvalidOperationException(
                        $"Transaction method '{current.Registration.Descriptor.Method}' 不得自行推进 revision。");
                }

                string[] resources = NormalizeResourceIds(operationResult.ResourceIds);
                for (int resourceIndex = 0; resourceIndex < resources.Length; resourceIndex++)
                {
                    _ = state.ResourceIds.Add(resources[resourceIndex]);
                }

                if (state.ResourceIds.Count > AutomationProtocolConstants.MaxRevisionResources)
                {
                    throw new InvalidOperationException(
                        $"Transaction resource 数不得超过 {AutomationProtocolConstants.MaxRevisionResources}。");
                }

                results[i] = new AutomationTransactionOperationResult
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    OperationId = current.OperationId,
                    RequestId = current.Request.RequestId,
                    Method = current.Registration.Descriptor.Method,
                    Payload = operationResult.Payload?.Clone(),
                    ResourceIds = resources,
                };
            }

            if (applied.Count != 0)
            {
                object afterState = _participant.CaptureState();
                _revisions.EnsureCanAdvance(state.ResourceIds);
                _undoSink.RecordExecuted(new CompositeAutomationUndoAction(
                    state.Name,
                    [.. applied],
                    _participant,
                    commitBeforeState,
                    afterState,
                    _revisions,
                    [.. state.ResourceIds.Order(StringComparer.Ordinal)]));
            }

            AutomationRevisionSnapshot revision = applied.Count == 0
                ? _revisions.Capture(state.ResourceIds)
                : _revisions.Advance(state.ResourceIds);
            AutomationTransactionInfo info = Complete(state, AutomationTransactionStatus.Committed);
            return (new AutomationTransactionCommitResult
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                Transaction = info,
                Operations = results,
            }, revision);
        }
        catch (Exception exception)
        {
            bool rollbackSucceeded = RestoreBeforeImage(applied, commitBeforeState);
            _ = Complete(state, AutomationTransactionStatus.RolledBack);
            throw CommitFailure(state.TransactionId, current, exception, rollbackSucceeded);
        }
    }

    /// <summary>丢弃全部尚未执行的 staged writes。</summary>
    public AutomationTransactionInfo Rollback(string sessionId, string transactionId)
    {
        AssertUsable();
        TransactionState state = RequireActiveOwner(sessionId, transactionId);
        return RollbackCore(state, AutomationTransactionStatus.RolledBack);
    }

    /// <summary>查询 active 或近期完成 transaction。</summary>
    public AutomationTransactionInfo GetInfo(string sessionId, string transactionId)
    {
        AssertUsable();
        return _active is { } active &&
            string.Equals(active.TransactionId, transactionId, StringComparison.Ordinal) &&
            string.Equals(active.SessionId, sessionId, StringComparison.Ordinal)
            ? ToInfo(active, AutomationTransactionStatus.Active)
            : _completed.TryGetValue(transactionId, out AutomationTransactionInfo? completed) &&
              string.Equals(completed.SessionId, sessionId, StringComparison.Ordinal)
                ? CloneInfo(completed)
                : throw InvalidTransactionRequest("transaction 不存在或不属于当前 session。");
    }

    /// <summary>连接关闭时在 EditorIngress 丢弃该 session 的 staged transaction。</summary>
    public AutomationTransactionInfo? RollbackOwnedBySession(string sessionId)
    {
        AssertUsable();
        return _active is { } active && string.Equals(active.SessionId, sessionId, StringComparison.Ordinal)
            ? RollbackCore(active, AutomationTransactionStatus.RolledBack)
            : null;
    }

    /// <summary>lease timer 到期后在 EditorIngress fail-closed 丢弃 staging。</summary>
    public AutomationTransactionInfo? Expire(string transactionId)
    {
        AssertUsable();
        if (_active is not { } active ||
            !string.Equals(active.TransactionId, transactionId, StringComparison.Ordinal))
        {
            return null;
        }

        TimeSpan remaining = active.ExpiresAtUtc - _timeProvider.GetUtcNow();
        if (remaining > TimeSpan.Zero)
        {
            // OS timer 可以受分辨率影响略早抵达；不能提前夺走调用方仍有效的 lease。
            return active.Timer?.Change(remaining, Timeout.InfiniteTimeSpan) == true
                ? null
                : throw new InvalidOperationException("Automation transaction lease timer 无法重新调度。");
        }

        return RollbackCore(active, AutomationTransactionStatus.Expired);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        AssertOwnerThread();
        if (_active is { } active)
        {
            _ = RollbackCore(active, AutomationTransactionStatus.RolledBack);
        }

        _disposed = true;
    }

    private void EnsureLeaseActive(TransactionState state)
    {
        if (!TryExpireLease(state))
        {
            return;
        }

        throw InvalidTransactionRequest("transaction lease 已过期并回滚。");
    }

    private bool TryExpireLease(TransactionState state)
    {
        if (_timeProvider.GetUtcNow() < state.ExpiresAtUtc)
        {
            return false;
        }

        AutomationTransactionInfo info = RollbackCore(state, AutomationTransactionStatus.Expired);
        _publishSynchronousExpiry(info);
        return true;
    }

    private AutomationTransactionInfo RollbackCore(
        TransactionState state,
        AutomationTransactionStatus status)
    {
        return Complete(state, status);
    }

    private bool RestoreBeforeImage(
        IReadOnlyList<IAutomationUndoAction> applied,
        object? participantState)
    {
        bool succeeded = true;
        for (int i = applied.Count - 1; i >= 0; i--)
        {
            try
            {
                applied[i].Undo();
            }
            catch (Exception)
            {
                succeeded = false;
            }
        }

        if (participantState is null)
        {
            return succeeded;
        }

        try
        {
            _participant.RestoreState(participantState);
        }
        catch (Exception)
        {
            succeeded = false;
        }

        return succeeded;
    }

    private AutomationTransactionInfo Complete(
        TransactionState state,
        AutomationTransactionStatus status)
    {
        state.Timer?.Dispose();
        AutomationTransactionInfo info = ToInfo(state, status);
        _completed[state.TransactionId] = info;
        _completedOrder.Enqueue(state.TransactionId);
        while (_completedOrder.Count > CompletedHistoryLimit)
        {
            _ = _completed.Remove(_completedOrder.Dequeue());
        }

        _active = null;
        return CloneInfo(info);
    }

    private TransactionState RequireActiveOwner(string sessionId, string transactionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        return _active is not { } active ||
            !string.Equals(active.TransactionId, transactionId, StringComparison.Ordinal) ||
            !string.Equals(active.SessionId, sessionId, StringComparison.Ordinal)
            ? throw InvalidTransactionRequest("transaction 不存在、不属于当前 session 或已结束。")
            : active;
    }

    private static AutomationTransactionInfo ToInfo(
        TransactionState state,
        AutomationTransactionStatus status)
    {
        return new AutomationTransactionInfo
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            TransactionId = state.TransactionId,
            SessionId = state.SessionId,
            Name = state.Name,
            Status = status,
            CreatedAtUtc = state.CreatedAtUtc,
            ExpiresAtUtc = state.ExpiresAtUtc,
            OperationCount = state.StagedOperations.Count,
            ResourceIds = [.. state.ResourceIds.Order(StringComparer.Ordinal)],
            BaseRevision = CloneRevision(state.BaseRevision),
        };
    }

    private static AutomationTransactionInfo CloneInfo(AutomationTransactionInfo info)
    {
        return info with
        {
            ResourceIds = [.. info.ResourceIds],
            BaseRevision = CloneRevision(info.BaseRevision),
        };
    }

    private static AutomationRevisionSnapshot CloneRevision(AutomationRevisionSnapshot revision)
    {
        return revision with
        {
            Resources = [.. revision.Resources.Select(static resource => resource with { })],
        };
    }

    private static string[] NormalizeResourceIds(IEnumerable<string>? resourceIds)
    {
        if (resourceIds is null)
        {
            return [];
        }

        string[] normalized =
        [
            .. resourceIds
                .Select(static resourceId =>
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
                    return resourceId.Length <= AutomationProtocolConstants.MaxResourceIdLength &&
                        !resourceId.Any(char.IsControl)
                        ? resourceId
                        : throw new ArgumentException(
                            "Automation resource id 长度或字符无效。",
                            nameof(resourceIds));
                })
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        return normalized.Length <= AutomationProtocolConstants.MaxRevisionResources
            ? normalized
            : throw new ArgumentException(
                $"Automation transaction resource 数不得超过 {AutomationProtocolConstants.MaxRevisionResources}。",
                nameof(resourceIds));
    }

    private static AutomationRequestException CommitFailure(
        string transactionId,
        StagedOperation? operation,
        Exception exception,
        bool rollbackSucceeded)
    {
        AutomationError cause = exception switch
        {
            AutomationRequestException requestException => requestException.Error,
            OperationCanceledException => new AutomationError
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                Code = AutomationErrorCodes.Cancelled,
                Category = AutomationErrorCategory.Cancellation,
                Message = "transaction.commit 已取消。",
                Transient = false,
            },
            _ => new AutomationError
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                Code = AutomationErrorCodes.Internal,
                Category = AutomationErrorCategory.Internal,
                Message = "staged semantic operation 执行失败。",
                Transient = false,
            },
        };
        AutomationTransactionFailureDetails details = new()
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            TransactionId = transactionId,
            OperationId = operation?.OperationId,
            Ordinal = operation?.Ordinal,
            Method = operation?.Registration.Descriptor.Method,
            RollbackSucceeded = rollbackSucceeded,
            Cause = cause,
        };
        return new AutomationRequestException(new AutomationError
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Code = rollbackSucceeded
                ? AutomationErrorCodes.TransactionFailed
                : AutomationErrorCodes.TransactionRollbackFailed,
            Category = rollbackSucceeded ? cause.Category : AutomationErrorCategory.Internal,
            Message = rollbackSucceeded
                ? "transaction.commit 失败；全部 staged 变化已恢复。"
                : "transaction.commit 失败，且无法完整恢复 before image；Editor 状态可能不一致。",
            Details = JsonSerializer.SerializeToElement(
                details,
                AutomationJsonContext.Default.AutomationTransactionFailureDetails),
            Transient = false,
            CurrentRevision = cause.CurrentRevision,
        });
    }

    private static AutomationRequestException InvalidTransactionRequest(string message)
    {
        return Error(AutomationErrorCodes.TransactionInvalid, AutomationErrorCategory.Conflict, message);
    }

    private static AutomationRequestException TransactionConflict(string message)
    {
        return Error(
            AutomationErrorCodes.TransactionConflict,
            AutomationErrorCategory.Conflict,
            message,
            transient: true,
            retryAfterMilliseconds: 25);
    }

    private static AutomationRequestException TransactionCapacity(string message)
    {
        return Error(
            AutomationErrorCodes.Busy,
            AutomationErrorCategory.Availability,
            message,
            transient: true,
            retryAfterMilliseconds: 25);
    }

    private static AutomationRequestException Error(
        string code,
        AutomationErrorCategory category,
        string message,
        bool transient = false,
        int? retryAfterMilliseconds = null)
    {
        return new AutomationRequestException(new AutomationError
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Code = code,
            Category = category,
            Message = message,
            Transient = transient,
            RetryAfterMilliseconds = retryAfterMilliseconds,
        });
    }

    private void AssertUsable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        AssertOwnerThread();
    }

    private void AssertOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("Automation transaction 只能在创建它的 Editor 主线程访问。");
        }
    }

    private sealed class TransactionState
    {
        public required string TransactionId { get; init; }

        public required string SessionId { get; init; }

        public required string Name { get; init; }

        public required DateTimeOffset CreatedAtUtc { get; init; }

        public required DateTimeOffset ExpiresAtUtc { get; init; }

        public required AutomationRevisionSnapshot BaseRevision { get; init; }

        public List<StagedOperation> StagedOperations { get; } = [];

        public HashSet<string> ResourceIds { get; } = new(StringComparer.Ordinal);

        public int StagedBytes { get; set; }

        public ITimer? Timer { get; set; }
    }

    private sealed class StagedOperation
    {
        public required string OperationId { get; init; }

        public required int Ordinal { get; init; }

        public required AutomationRequestContext Request { get; init; }

        public required AutomationMethodRegistration Registration { get; init; }

        public required JsonElement? Payload { get; init; }

        public required DateTimeOffset AcceptedAtUtc { get; init; }

    }

    private sealed record ExpiryCallbackState(string TransactionId, Action<string> Callback);
}

internal sealed class CompositeAutomationUndoAction(
    string name,
    IAutomationUndoAction[] actions,
    IAutomationTransactionParticipant participant,
    object beforeState,
    object afterState,
    AutomationRevisionStore revisions,
    string[] resourceIds) : IAutomationUndoAction
{
    public string Name { get; } = string.IsNullOrWhiteSpace(name)
        ? throw new ArgumentException("Undo action name 不能为空。", nameof(name))
        : name;

    public void Undo()
    {
        for (int i = actions.Length - 1; i >= 0; i--)
        {
            actions[i].Undo();
        }

        participant.RestoreState(beforeState);
        _ = revisions.Advance(resourceIds);
    }

    public void Redo()
    {
        for (int i = 0; i < actions.Length; i++)
        {
            actions[i].Redo();
        }

        participant.RestoreState(afterState);
        _ = revisions.Advance(resourceIds);
    }
}
