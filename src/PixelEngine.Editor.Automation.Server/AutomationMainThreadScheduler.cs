using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Server;

/// <summary>
/// 多 producer、单 Editor 主线程 consumer 的有界 phase scheduler。
/// </summary>
public sealed class AutomationMainThreadScheduler :
    IAutomationRequestHandler,
    IAutomationSessionLifecycleHandler,
    IDisposable
{
    private const int PhaseCount = (int)AutomationExecutionPhase.Background + 1;
    private readonly Lock _sync = new();
    private readonly PhaseQueue[] _queues = new PhaseQueue[PhaseCount];
    private readonly int[] _phasePending = new int[PhaseCount];
    private readonly ConcurrentQueue<InternalWork> _internalEditorIngress = new();
    private readonly FrozenDictionary<string, AutomationMethodRegistration> _registrations;
    private readonly AutomationCapabilityDescriptor[] _capabilities;
    private readonly byte[] _capabilityCursorKey = RandomNumberGenerator.GetBytes(32);
    private readonly IAutomationUndoSink _undoSink;
    private readonly AutomationTransactionManager _transactions;
    private readonly AutomationIdempotencyCache _idempotency;
    private readonly AutomationMainThreadSchedulerOptions _options;
    private readonly int _ownerThreadId;
    private int _pendingCount;
    private int _internalPendingCount;
    private int _disposed;

    /// <summary>
    /// 在 Editor 主线程创建 scheduler 和 transaction manager。
    /// </summary>
    /// <param name="registrations">真实 semantic registrations。</param>
    /// <param name="revisions">revision store。</param>
    /// <param name="undoSink">唯一 Editor Undo history adapter。</param>
    /// <param name="transactionParticipant">transaction before state adapter。</param>
    /// <param name="options">容量、lease、时钟与 wake signal。</param>
    /// <param name="eventHub">可选共享 event hub；scheduler 接管其生命周期。</param>
    public AutomationMainThreadScheduler(
        IEnumerable<AutomationMethodRegistration> registrations,
        AutomationRevisionStore revisions,
        IAutomationUndoSink undoSink,
        IAutomationTransactionParticipant transactionParticipant,
        AutomationMainThreadSchedulerOptions? options = null,
        AutomationEventHub? eventHub = null)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        Revisions = revisions ?? throw new ArgumentNullException(nameof(revisions));
        _undoSink = undoSink ?? throw new ArgumentNullException(nameof(undoSink));
        ArgumentNullException.ThrowIfNull(transactionParticipant);
        _options = ValidateOptions(options ?? new AutomationMainThreadSchedulerOptions());
        _ownerThreadId = Environment.CurrentManagedThreadId;
        for (int i = 0; i < _queues.Length; i++)
        {
            _queues[i] = new PhaseQueue();
        }

        List<AutomationMethodRegistration> all = [.. registrations];
        all.AddRange(CreateTransactionRegistrations());
        all.AddRange(CreateEventRegistrations());
        all.Add(CreateCapabilityRegistration());
        ValidateRegistrations(all);
        _registrations = all.ToFrozenDictionary(
            static registration => registration.Descriptor.Method,
            StringComparer.Ordinal);
        _capabilities =
        [
            .. all
                .Select(static registration => registration.Descriptor.ToCapabilityDescriptor())
                .OrderBy(static descriptor => descriptor.Id, StringComparer.Ordinal),
        ];
        CapabilityDigest = ComputeCapabilityDigest(_capabilities);
        _idempotency = new AutomationIdempotencyCache(
            _options.TimeProvider,
            _options.IdempotencyRetention,
            _options.IdempotencyCapacity);
        Events = eventHub ?? new AutomationEventHub(new AutomationEventHubOptions
        {
            TimeProvider = _options.TimeProvider,
        });
        _transactions = new AutomationTransactionManager(
            Revisions,
            _undoSink,
            transactionParticipant,
            _options.TimeProvider,
            _options.DefaultTransactionLease,
            _options.MaxTransactionLease,
            _options.MaxTransactionOperations,
            _options.MaxTransactionStagedBytes,
            RequestTransactionExpiryRollback,
            staticInfo => PublishImplicitRollback(staticInfo, "expired"));
    }

    /// <summary>当前排队的外部请求数。</summary>
    public int PendingCount => Volatile.Read(ref _pendingCount);

    /// <summary>revision 权威存储。</summary>
    public AutomationRevisionStore Revisions { get; }

    /// <summary>事件订阅与 bounded replay 权威 hub。</summary>
    public AutomationEventHub Events { get; }

    /// <summary>真实 semantic registry 的 SHA256 digest。</summary>
    public string CapabilityDigest { get; }

    /// <summary>返回按 capability ID 排序的深复制 descriptor 快照。</summary>
    /// <returns>不可与 scheduler 内部数组共享可变集合的 descriptor 数组。</returns>
    public AutomationCapabilityDescriptor[] CaptureCapabilities()
    {
        return [.. _capabilities.Select(CloneCapability)];
    }

    /// <summary>当前主线程是否存在全局互斥 automation transaction 写租约。</summary>
    public bool HasActiveTransaction
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            AssertOwnerThread();
            return _transactions.HasActiveTransaction;
        }
    }

    /// <summary>
    /// 判断某 safe phase 是否有 work；空闲时仅做一次原子读取，不触碰队列或分配。
    /// </summary>
    /// <param name="phase">safe phase。</param>
    /// <returns>是否有外部或内部 work。</returns>
    public bool HasPendingWork(AutomationExecutionPhase phase)
    {
        ValidatePhase(phase, allowBackground: false);
        return Volatile.Read(ref _phasePending[(int)phase]) != 0 ||
            (phase == AutomationExecutionPhase.EditorIngress &&
             Volatile.Read(ref _internalPendingCount) != 0);
    }

    /// <summary>
    /// 只在声明 safe phase 由创建 scheduler 的 Editor 主线程调用。
    /// </summary>
    /// <param name="phase">当前 safe phase。</param>
    /// <param name="maxItems">覆盖单次 drain 上限；为空时使用 options。</param>
    /// <returns>实际处理 item 数。</returns>
    public int Drain(AutomationExecutionPhase phase, int? maxItems = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        AssertOwnerThread();
        ValidatePhase(phase, allowBackground: false);
        int limit = maxItems ?? _options.MaxItemsPerDrain;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        int processed = 0;

        if (phase == AutomationExecutionPhase.EditorIngress)
        {
            while (processed < limit && _internalEditorIngress.TryDequeue(out InternalWork? internalWork))
            {
                _ = Interlocked.Decrement(ref _internalPendingCount);
                internalWork.Action();
                processed++;
            }
        }

        while (processed < limit && Volatile.Read(ref _phasePending[(int)phase]) != 0)
        {
            WorkItem? item = TryDequeue(phase);
            if (item is null)
            {
                break;
            }

            Execute(item);
            processed++;
        }

        return processed;
    }

    /// <inheritdoc />
    public bool TryGetDescriptor(string method, out AutomationMethodDescriptor descriptor)
    {
        if (_registrations.TryGetValue(method, out AutomationMethodRegistration? registration))
        {
            descriptor = registration.Descriptor;
            return true;
        }

        descriptor = null!;
        return false;
    }

    /// <inheritdoc />
    public ValueTask<AutomationHandlerResult> HandleAsync(
        AutomationRequestContext context,
        string method,
        JsonElement? payload,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(context);
        if (!_registrations.TryGetValue(method, out AutomationMethodRegistration? registration))
        {
            throw RequestError(
                AutomationErrorCodes.MethodNotFound,
                AutomationErrorCategory.Validation,
                $"Automation method '{method}' 不存在。");
        }

        ValidateBeforeQueueMetadata(context, registration.Descriptor, cancellationToken);
        JsonElement? immutablePayload = payload?.Clone();
        AutomationRequestContext immutableContext = string.IsNullOrWhiteSpace(context.TransactionId)
            ? context
            : context.SnapshotForTransactionStaging();
        int stagedBytes = string.IsNullOrWhiteSpace(context.TransactionId)
            ? 0
            : EstimateTransactionStagedBytes(immutableContext, immutablePayload);
        Task<AutomationHandlerResult> task;
        bool cancelWaitOnly = false;
        if (!string.IsNullOrWhiteSpace(context.IdempotencyKey))
        {
            AutomationIdempotencyLookup lookup = _idempotency.GetOrAdd(
                context,
                method,
                immutablePayload,
                () =>
                {
                    ValidateRevisionBeforeQueue(immutableContext, registration.Descriptor);
                    return Schedule(
                        immutableContext,
                        registration,
                        immutablePayload,
                        stagedBytes,
                        cancellationToken);
                });
            task = lookup.Task;
            cancelWaitOnly = !lookup.Created;
        }
        else
        {
            ValidateRevisionBeforeQueue(immutableContext, registration.Descriptor);
            task = Schedule(
                immutableContext,
                registration,
                immutablePayload,
                stagedBytes,
                cancellationToken);
        }

        return cancelWaitOnly && cancellationToken.CanBeCanceled
            ? new ValueTask<AutomationHandlerResult>(WaitWithCancellationAsync(task, cancellationToken))
            : new ValueTask<AutomationHandlerResult>(task);
    }

    /// <inheritdoc />
    public void OnSessionOpened(AutomationSessionContext session, IAutomationEventSink eventSink)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(eventSink);
        Events.OpenSession(session, eventSink);
    }

    /// <inheritdoc />
    public void OnSessionClosed(AutomationSessionContext session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Events.CloseSession(session.SessionId);
        EnqueueInternal(() => PublishImplicitRollback(
            _transactions.RollbackOwnedBySession(session.SessionId),
            "rollback"));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        AssertOwnerThread();
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        List<WorkItem> cancelled = [];
        lock (_sync)
        {
            for (int i = 0; i < (int)AutomationExecutionPhase.Background; i++)
            {
                WorkItem? item;
                while ((item = _queues[i].RemoveFirst()) is not null)
                {
                    item.State = WorkItemState.Cancelled;
                    cancelled.Add(item);
                }

                _phasePending[i] = 0;
            }

            _pendingCount = 0;
            while (_internalEditorIngress.TryDequeue(out _))
            {
            }

            _internalPendingCount = 0;
        }

        AutomationConnectionExceptionForServer exception = new("Automation scheduler 已关闭。");
        for (int i = 0; i < cancelled.Count; i++)
        {
            cancelled[i].DisposeCancellationRegistration();
            _ = cancelled[i].Completion.TrySetException(exception);
        }

        Events.Dispose();
        _transactions.Dispose();
        CryptographicOperations.ZeroMemory(_capabilityCursorKey);
    }

    private Task<AutomationHandlerResult> Schedule(
        AutomationRequestContext context,
        AutomationMethodRegistration registration,
        JsonElement? payload,
        int stagedBytes,
        CancellationToken cancellationToken)
    {
        WorkItem item = new(context, registration, payload, stagedBytes, cancellationToken, this);
        bool wake;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_pendingCount >= _options.Capacity)
            {
                throw RequestError(
                    AutomationErrorCodes.Busy,
                    AutomationErrorCategory.Availability,
                    "Automation 主线程队列已满。",
                    transient: true,
                    retryAfterMilliseconds: 25);
            }

            int phaseIndex = (int)registration.Descriptor.ExecutionPhase;
            _queues[phaseIndex].AddLast(item);
            _phasePending[phaseIndex]++;
            wake = _pendingCount++ == 0 && Volatile.Read(ref _internalPendingCount) == 0;
        }

        item.SetCancellationRegistration(cancellationToken.UnsafeRegister(
            static state => ((WorkItem)state!).Owner.CancelQueued((WorkItem)state!),
            item));
        if (wake)
        {
            SignalWake(item);
        }

        return item.Completion.Task;
    }

    private static async Task<AutomationHandlerResult> WaitWithCancellationAsync(
        Task<AutomationHandlerResult> task,
        CancellationToken cancellationToken)
    {
        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private WorkItem? TryDequeue(AutomationExecutionPhase phase)
    {
        lock (_sync)
        {
            WorkItem? item = _queues[(int)phase].RemoveFirst();
            if (item is null)
            {
                return null;
            }

            item.State = WorkItemState.Executing;
            _phasePending[(int)phase]--;
            _pendingCount--;
            return item;
        }
    }

    private void CancelQueued(WorkItem item)
    {
        bool removed = false;
        lock (_sync)
        {
            if (item.State == WorkItemState.Pending)
            {
                int phase = (int)item.Registration.Descriptor.ExecutionPhase;
                _queues[phase].Remove(item);
                _phasePending[phase]--;
                _pendingCount--;
                item.State = WorkItemState.Cancelled;
                removed = true;
            }
        }

        if (removed)
        {
            item.UnregisterCancellationCallback();
            _ = item.Completion.TrySetCanceled(item.CancellationToken);
        }
    }

    private void Execute(WorkItem item)
    {
        item.DisposeCancellationRegistration();
        try
        {
            AutomationHandlerResult result = ExecuteOperation(
                item.Context,
                item.Registration,
                item.Payload,
                item.StagedBytes,
                item.CancellationToken);
            item.State = WorkItemState.Completed;
            _ = item.Completion.TrySetResult(result);
        }
        catch (OperationCanceledException) when (item.CancellationToken.IsCancellationRequested)
        {
            item.State = WorkItemState.Cancelled;
            _ = item.Completion.TrySetCanceled(item.CancellationToken);
        }
        catch (Exception exception)
        {
            item.State = WorkItemState.Completed;
            _ = item.Completion.TrySetException(exception);
        }
    }

    private AutomationHandlerResult ExecuteOperation(
        AutomationRequestContext context,
        AutomationMethodRegistration registration,
        JsonElement? payload,
        int stagedBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.DeadlineUtc is { } deadline && deadline <= _options.TimeProvider.GetUtcNow())
        {
            throw RequestError(
                AutomationErrorCodes.DeadlineExceeded,
                AutomationErrorCategory.Cancellation,
                "Automation request 在执行 safe point 前已超过 deadline。");
        }

        AutomationMethodDescriptor descriptor = registration.Descriptor;
        RequireScopes(context, descriptor.RequiredScopes);
        ValidateTransactionShape(context, descriptor);
        if (descriptor.OperationKind == AutomationOperationKind.Command)
        {
            if (context.ExpectedRevision is not null)
            {
                Revisions.Validate(context.ExpectedRevision);
            }
            else if (descriptor.RequiresExpectedRevision)
            {
                throw RequestError(
                    AutomationErrorCodes.InvalidRequest,
                    AutomationErrorCategory.Validation,
                    $"Automation method '{descriptor.Method}' 必须携带 expectedRevision。");
            }
        }

        if (descriptor.OperationKind == AutomationOperationKind.Write)
        {
            _transactions.EnsureWriteAllowed(context.SessionId, context.TransactionId);
            if (context.ExpectedRevision is not null)
            {
                Revisions.Validate(context.ExpectedRevision);
            }
            else if (descriptor.RequiresExpectedRevision)
            {
                throw RequestError(
                    AutomationErrorCodes.InvalidRequest,
                    AutomationErrorCategory.Validation,
                    $"Automation method '{descriptor.Method}' 必须携带 expectedRevision。");
            }

            if (!string.IsNullOrWhiteSpace(context.TransactionId))
            {
                AutomationTransactionStagedOperationInfo staged = _transactions.Stage(
                    context,
                    registration,
                    payload,
                    stagedBytes);
                string[] expectedResources = context.ExpectedRevision is null
                    ? []
                    :
                    [
                        .. context.ExpectedRevision.Resources
                            .Select(static resource => resource.ResourceId)
                            .Distinct(StringComparer.Ordinal)
                            .Order(StringComparer.Ordinal),
                    ];
                return new AutomationHandlerResult
                {
                    Payload = JsonSerializer.SerializeToElement(
                        staged,
                        AutomationJsonContext.Default.AutomationTransactionStagedOperationInfo),
                    Revision = Revisions.Capture(expectedResources),
                };
            }
        }

        AutomationScheduledContext scheduledContext = new(context, cancellationToken, Revisions);
        AutomationOperationResult operationResult = registration.Operation(scheduledContext, payload)
            ?? throw new InvalidOperationException($"Automation method '{descriptor.Method}' 返回了 null result。");
        bool writeCommitted = false;
        try
        {
            string[] resourceIds = NormalizeResourceIds(operationResult.ResourceIds);
            AutomationRevisionSnapshot revision;
            switch (descriptor.OperationKind)
            {
                case AutomationOperationKind.Read:
                    if (operationResult.UndoAction is not null)
                    {
                        throw new InvalidOperationException($"只读 method '{descriptor.Method}' 不得返回 Undo action。");
                    }

                    revision = operationResult.RevisionOverride is null
                        ? Revisions.Capture(resourceIds)
                        : NormalizeRevisionOverride(operationResult.RevisionOverride);
                    break;
                case AutomationOperationKind.Write:
                    revision = CompleteWrite(descriptor, operationResult, resourceIds);
                    writeCommitted = true;
                    break;
                case AutomationOperationKind.Command:
                    if (operationResult.UndoAction is not null)
                    {
                        throw new InvalidOperationException($"Command method '{descriptor.Method}' 不得返回 Undo action。");
                    }

                    revision = operationResult.RevisionOverride is null
                        ? Revisions.Capture(resourceIds)
                        : NormalizeRevisionOverride(operationResult.RevisionOverride);
                    if (operationResult.StateChanged &&
                        !operationResult.StateEventAlreadyPublished &&
                        descriptor.EventTypes.Contains(
                        AutomationProtocolConstants.StateChangedEventType,
                        StringComparer.Ordinal))
                    {
                        if (resourceIds.Length == 0 || operationResult.RevisionOverride is null)
                        {
                            throw new InvalidOperationException(
                                $"State-changing Command '{descriptor.Method}' 必须返回 resources 与显式 revision override。");
                        }

                        PublishStateChanged(
                            descriptor.Method,
                            resourceIds,
                            "execute",
                            null,
                            context.RequestId,
                            revision);
                    }
                    break;
                default:
                    throw new InvalidOperationException($"未知 automation operation kind {descriptor.OperationKind}。");
            }

            if (descriptor.OperationKind == AutomationOperationKind.Write &&
                operationResult.WriteStateChanged &&
                string.IsNullOrWhiteSpace(context.TransactionId))
            {
                PublishStateChanged(
                    descriptor.Method,
                    resourceIds,
                    "execute",
                    null,
                    context.RequestId,
                    revision);
            }

            return new AutomationHandlerResult
            {
                Payload = operationResult.Payload?.Clone(),
                Revision = revision,
            };
        }
        catch (Exception exception) when (
            descriptor.OperationKind == AutomationOperationKind.Write &&
            !writeCommitted && operationResult.UndoAction is not null)
        {
            try
            {
                operationResult.UndoAction.Undo();
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    $"Automation write '{descriptor.Method}' 提交失败，且 semantic rollback 也失败。",
                    exception,
                    rollbackException);
            }

            throw;
        }
    }

    private AutomationRevisionSnapshot CompleteWrite(
        AutomationMethodDescriptor descriptor,
        AutomationOperationResult result,
        string[] resourceIds)
    {
        if (result.RevisionOverride is not null)
        {
            throw new InvalidOperationException(
                $"Write method '{descriptor.Method}' 的 revision 只能由 scheduler 统一推进。");
        }

        if (!result.WriteStateChanged)
        {
            return result.UndoAction is null
                ? Revisions.Capture(resourceIds)
                : throw new InvalidOperationException(
                    $"No-change Write method '{descriptor.Method}' 不得返回 Undo action。");
        }

        if (descriptor.TransactionMode is AutomationTransactionMode.Optional or AutomationTransactionMode.Required)
        {
            if (result.UndoAction is null)
            {
                throw new InvalidOperationException(
                    $"Reversible write '{descriptor.Method}' 必须返回真实 Undo action。");
            }

            Revisions.EnsureCanAdvance(resourceIds);
            _undoSink.RecordExecuted(new RevisionTrackingUndoAction(
                result.UndoAction,
                Revisions,
                resourceIds));
        }
        else if (result.UndoAction is not null)
        {
            throw new InvalidOperationException(
                $"Transaction-forbidden write '{descriptor.Method}' 不得返回未登记的 Undo action。");
        }

        return Revisions.Advance(resourceIds);
    }

    private void ValidateBeforeQueueMetadata(
        AutomationRequestContext context,
        AutomationMethodDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireScopes(context, descriptor.RequiredScopes);
        if (context.DeadlineUtc is { } deadline && deadline <= _options.TimeProvider.GetUtcNow())
        {
            throw RequestError(
                AutomationErrorCodes.DeadlineExceeded,
                AutomationErrorCategory.Cancellation,
                "Automation request 入队前已超过 deadline。");
        }

        if (descriptor.RequiresIdempotencyKey && string.IsNullOrWhiteSpace(context.IdempotencyKey))
        {
            throw RequestError(
                AutomationErrorCodes.InvalidRequest,
                AutomationErrorCategory.Validation,
                $"Automation method '{descriptor.Method}' 必须携带 idempotencyKey。");
        }

        if (IsEventControlMethod(descriptor.Method) && !string.IsNullOrWhiteSpace(context.IdempotencyKey))
        {
            throw RequestError(
                AutomationErrorCodes.InvalidRequest,
                AutomationErrorCategory.Validation,
                $"Automation method '{descriptor.Method}' 使用 subscriptionKey/resumeToken，不接受通用 idempotencyKey。");
        }

        ValidateOptionalIdentifier(context.IdempotencyKey, "idempotencyKey");
        ValidateOptionalIdentifier(context.TransactionId, "transactionId");
        ValidateTransactionShape(context, descriptor);
    }

    private void ValidateRevisionBeforeQueue(
        AutomationRequestContext context,
        AutomationMethodDescriptor descriptor)
    {
        if (descriptor.OperationKind is AutomationOperationKind.Write or AutomationOperationKind.Command &&
            context.ExpectedRevision is not null)
        {
            Revisions.Validate(context.ExpectedRevision);
        }
    }

    private static void ValidateTransactionShape(
        AutomationRequestContext context,
        AutomationMethodDescriptor descriptor)
    {
        bool hasTransaction = !string.IsNullOrWhiteSpace(context.TransactionId);
        if (hasTransaction && descriptor.TransactionMode == AutomationTransactionMode.Forbidden)
        {
            throw RequestError(
                AutomationErrorCodes.TransactionInvalid,
                AutomationErrorCategory.Conflict,
                $"Automation method '{descriptor.Method}' 禁止在 transaction 中执行。");
        }

        if (!hasTransaction && descriptor.TransactionMode == AutomationTransactionMode.Required)
        {
            throw RequestError(
                AutomationErrorCodes.TransactionInvalid,
                AutomationErrorCategory.Conflict,
                $"Automation method '{descriptor.Method}' 必须在 transaction 中执行。");
        }
    }

    private static void RequireScopes(AutomationRequestContext context, IEnumerable<string> requiredScopes)
    {
        string[] missing =
        [
            .. requiredScopes
                .Where(scope => !context.HasScope(scope))
                .Order(StringComparer.Ordinal),
        ];
        if (missing.Length != 0)
        {
            throw RequestError(
                AutomationErrorCodes.PermissionDenied,
                AutomationErrorCategory.Authorization,
                $"Automation request 执行前仍缺少 scope：{string.Join(',', missing)}。");
        }
    }

    private List<AutomationMethodRegistration> CreateTransactionRegistrations()
    {
        return
        [
            CreateTransactionRegistration(
                AutomationProtocolConstants.TransactionBeginMethod,
                requiresIdempotency: true,
                BeginTransaction),
            CreateTransactionRegistration(
                AutomationProtocolConstants.TransactionCommitMethod,
                requiresIdempotency: true,
                CommitTransaction),
            CreateTransactionRegistration(
                AutomationProtocolConstants.TransactionRollbackMethod,
                requiresIdempotency: true,
                RollbackTransaction),
            CreateTransactionRegistration(
                AutomationProtocolConstants.TransactionStatusMethod,
                requiresIdempotency: false,
                GetTransactionStatus),
        ];
    }

    private List<AutomationMethodRegistration> CreateEventRegistrations()
    {
        return
        [
            CreateEventRegistration(AutomationProtocolConstants.EventSubscribeMethod, SubscribeEvents),
            CreateEventRegistration(AutomationProtocolConstants.EventAckMethod, AcknowledgeEvents),
            CreateEventRegistration(AutomationProtocolConstants.EventUnsubscribeMethod, UnsubscribeEvents),
        ];
    }

    private AutomationMethodRegistration CreateCapabilityRegistration()
    {
        return new AutomationMethodRegistration
        {
            Descriptor = new AutomationMethodDescriptor
            {
                Method = AutomationProtocolConstants.CapabilityListMethod,
                Domain = "automation",
                RequestSchema = "#/$defs/pageRequest",
                ResponseSchema = "#/$defs/capabilityListResponse",
                RequiredScopes = [AutomationScopes.EditorRead],
                SupportedModes = ["edit", "play", "paused"],
                OperationKind = AutomationOperationKind.Read,
                ExecutionPhase = AutomationExecutionPhase.EditorIngress,
                TransactionMode = AutomationTransactionMode.Forbidden,
                RequiresExpectedRevision = false,
                RequiresIdempotencyKey = false,
                EventTypes = [],
                ArtifactBehavior = AutomationArtifactBehavior.None,
                UiCommandIds = [],
            },
            Operation = ListCapabilities,
        };
    }

    private AutomationOperationResult ListCapabilities(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        AutomationPageRequest request;
        try
        {
            request = payload is null
                ? new AutomationPageRequest()
                : payload.Value.Deserialize(AutomationJsonContext.Default.AutomationPageRequest)
                    ?? throw RequestError(
                        AutomationErrorCodes.InvalidRequest,
                        AutomationErrorCategory.Validation,
                        "automation.capabilities.list payload 不能为空。");
        }
        catch (JsonException exception)
        {
            throw RequestError(
                AutomationErrorCodes.InvalidRequest,
                AutomationErrorCategory.Validation,
                $"automation.capabilities.list payload schema 无效：{exception.Message}");
        }
        ValidateSchemaVersion(request.SchemaVersion, AutomationProtocolConstants.CapabilityListMethod);
        if (request.PageSize is < 1 or > 500)
        {
            throw RequestError(
                AutomationErrorCodes.InvalidRequest,
                AutomationErrorCategory.Validation,
                "capability pageSize 必须在 1..500。");
        }

        if (request.Sort is null ||
            (request.Filter is not null && request.Filter.Clauses is null))
        {
            throw RequestError(
                AutomationErrorCodes.InvalidRequest,
                AutomationErrorCategory.Validation,
                "capability filter.clauses 与 sort 不得为 null。");
        }

        AutomationCapabilityDescriptor[] filtered = ApplyCapabilityFilter(request.Filter);
        SortCapabilities(filtered, request.Sort);
        string queryDigest = ComputeCapabilityQueryDigest(request);
        int offset = DecodeCapabilityCursor(request.Cursor, queryDigest, filtered.Length);
        int count = Math.Min(request.PageSize, filtered.Length - offset);
        AutomationCapabilityDescriptor[] items = new AutomationCapabilityDescriptor[count];
        for (int i = 0; i < count; i++)
        {
            items[i] = CloneCapability(filtered[offset + i]);
        }

        int nextOffset = checked(offset + count);
        AutomationCapabilityListResponse response = new()
        {
            CapabilityDigest = CapabilityDigest,
            Items = items,
            Page = new AutomationPageInfo
            {
                Returned = items.Length,
                Total = filtered.Length,
                NextCursor = nextOffset < filtered.Length
                    ? EncodeCapabilityCursor(nextOffset, queryDigest)
                    : null,
            },
        };
        return new AutomationOperationResult
        {
            Payload = JsonSerializer.SerializeToElement(
                response,
                AutomationJsonContext.Default.AutomationCapabilityListResponse),
            ResourceIds = ["automation:capabilities"],
        };
    }

    private AutomationCapabilityDescriptor[] ApplyCapabilityFilter(AutomationQueryFilter? filter)
    {
        if (filter is null || filter.Clauses is { Length: 0 })
        {
            return CaptureCapabilities();
        }

        if (!Enum.IsDefined(filter.Match) || filter.Clauses is not { Length: >= 1 and <= 32 })
        {
            throw RequestError(
                AutomationErrorCodes.InvalidRequest,
                AutomationErrorCategory.Validation,
                "capability filter match/clauses 无效或超过 32 条。");
        }

        List<AutomationCapabilityDescriptor> result = [];
        for (int i = 0; i < _capabilities.Length; i++)
        {
            bool matched = filter.Match == AutomationFilterMatch.All;
            for (int clauseIndex = 0; clauseIndex < filter.Clauses.Length; clauseIndex++)
            {
                bool clauseMatched = CapabilityMatches(_capabilities[i], filter.Clauses[clauseIndex]);
                if (filter.Match == AutomationFilterMatch.All)
                {
                    matched &= clauseMatched;
                    if (!matched)
                    {
                        break;
                    }
                }
                else
                {
                    matched |= clauseMatched;
                    if (matched)
                    {
                        break;
                    }
                }
            }

            if (matched)
            {
                result.Add(CloneCapability(_capabilities[i]));
            }
        }

        return [.. result];
    }

    private static bool CapabilityMatches(
        AutomationCapabilityDescriptor descriptor,
        AutomationFilterClause clause)
    {
        ArgumentNullException.ThrowIfNull(clause);
        string actual = clause.Field switch
        {
            "id" => descriptor.Id,
            "domain" => descriptor.Domain,
            "operationKind" => descriptor.OperationKind.ToString(),
            "executionPhase" => descriptor.ExecutionPhase.ToString(),
            "transactionMode" => descriptor.TransactionMode.ToString(),
            "artifactBehavior" => descriptor.ArtifactBehavior.ToString(),
            _ => throw RequestError(
                AutomationErrorCodes.InvalidRequest,
                AutomationErrorCategory.Validation,
                $"capability filter field '{clause.Field}' 不受支持。"),
        };
        if (clause.Value.ValueKind != JsonValueKind.String)
        {
            throw RequestError(
                AutomationErrorCodes.InvalidRequest,
                AutomationErrorCategory.Validation,
                $"capability filter field '{clause.Field}' 只接受 string value。");
        }

        string expected = clause.Value.GetString() ?? string.Empty;
        bool equals = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        return clause.Operator switch
        {
            AutomationFilterOperator.Equals => equals,
            AutomationFilterOperator.NotEquals => !equals,
            AutomationFilterOperator.Contains => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            AutomationFilterOperator.StartsWith => actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase),
            AutomationFilterOperator.LessThan or
            AutomationFilterOperator.LessThanOrEqual or
            AutomationFilterOperator.GreaterThan or
            AutomationFilterOperator.GreaterThanOrEqual => throw RequestError(
                AutomationErrorCodes.InvalidRequest,
                AutomationErrorCategory.Validation,
                $"capability filter 不支持 operator {clause.Operator}。"),
            _ => throw new ArgumentOutOfRangeException(nameof(clause), clause.Operator, "未知 filter operator。"),
        };
    }

    private static void SortCapabilities(
        AutomationCapabilityDescriptor[] capabilities,
        AutomationSortClause[] sort)
    {
        ArgumentNullException.ThrowIfNull(sort);
        if (sort.Length > 8 || sort.Any(static clause => clause is null || !Enum.IsDefined(clause.Direction)))
        {
            throw RequestError(
                AutomationErrorCodes.InvalidRequest,
                AutomationErrorCategory.Validation,
                "capability sort 无效或超过 8 条。");
        }

        for (int i = 0; i < sort.Length; i++)
        {
            _ = GetCapabilitySortValue(capabilities.Length == 0 ? null : capabilities[0], sort[i].Field);
        }

        Array.Sort(capabilities, (left, right) =>
        {
            for (int i = 0; i < sort.Length; i++)
            {
                int compared = string.Compare(
                    GetCapabilitySortValue(left, sort[i].Field),
                    GetCapabilitySortValue(right, sort[i].Field),
                    StringComparison.Ordinal);
                if (compared != 0)
                {
                    return sort[i].Direction == AutomationSortDirection.Descending ? -compared : compared;
                }
            }

            return string.Compare(left.Id, right.Id, StringComparison.Ordinal);
        });
    }

    private static string GetCapabilitySortValue(
        AutomationCapabilityDescriptor? descriptor,
        string field)
    {
        return field switch
        {
            "id" => descriptor?.Id ?? string.Empty,
            "domain" => descriptor?.Domain ?? string.Empty,
            "operationKind" => descriptor?.OperationKind.ToString() ?? string.Empty,
            "executionPhase" => descriptor?.ExecutionPhase.ToString() ?? string.Empty,
            "transactionMode" => descriptor?.TransactionMode.ToString() ?? string.Empty,
            "artifactBehavior" => descriptor?.ArtifactBehavior.ToString() ?? string.Empty,
            _ => throw RequestError(
                AutomationErrorCodes.InvalidRequest,
                AutomationErrorCategory.Validation,
                $"capability sort field '{field}' 不受支持。"),
        };
    }

    private string ComputeCapabilityQueryDigest(AutomationPageRequest request)
    {
        AutomationPageRequest canonical = request with { Cursor = null };
        byte[] utf8 = JsonSerializer.SerializeToUtf8Bytes(
            canonical,
            AutomationJsonContext.Default.AutomationPageRequest);
        try
        {
            return Convert.ToHexStringLower(SHA256.HashData(utf8));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(utf8);
        }
    }

    private string EncodeCapabilityCursor(int offset, string queryDigest)
    {
        byte[] body = Encoding.UTF8.GetBytes(string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"v1\n{CapabilityDigest}\n{queryDigest}\n{offset}"));
        try
        {
            byte[] signature = HMACSHA256.HashData(_capabilityCursorKey, body);
            try
            {
                return $"{Base64UrlEncode(body)}.{Base64UrlEncode(signature)}";
            }
            finally
            {
                CryptographicOperations.ZeroMemory(signature);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(body);
        }
    }

    private int DecodeCapabilityCursor(string? cursor, string queryDigest, int total)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return 0;
        }

        if (cursor.Length > 2048)
        {
            throw InvalidCapabilityCursor();
        }

        try
        {
            string[] tokenParts = cursor.Split('.');
            if (tokenParts.Length != 2)
            {
                throw InvalidCapabilityCursor();
            }

            byte[] body = Base64UrlDecode(tokenParts[0]);
            byte[] suppliedSignature = Base64UrlDecode(tokenParts[1]);
            try
            {
                byte[] expectedSignature = HMACSHA256.HashData(_capabilityCursorKey, body);
                try
                {
                    if (!CryptographicOperations.FixedTimeEquals(
                        suppliedSignature,
                        expectedSignature))
                    {
                        throw InvalidCapabilityCursor();
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(expectedSignature);
                }

                string[] parts = Encoding.UTF8.GetString(body).Split('\n');
                return parts.Length != 4 ||
                    !string.Equals(parts[0], "v1", StringComparison.Ordinal) ||
                    !string.Equals(parts[1], CapabilityDigest, StringComparison.Ordinal) ||
                    !string.Equals(parts[2], queryDigest, StringComparison.Ordinal) ||
                    !int.TryParse(
                        parts[3],
                        System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out int offset) ||
                    offset < 0 ||
                    offset > total
                        ? throw InvalidCapabilityCursor()
                        : offset;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(body);
                CryptographicOperations.ZeroMemory(suppliedSignature);
            }
        }
        catch (Exception exception) when (exception is FormatException or DecoderFallbackException)
        {
            throw InvalidCapabilityCursor();
        }
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string value)
    {
        string normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = (normalized.Length % 4) switch
        {
            0 => normalized,
            2 => normalized + "==",
            3 => normalized + "=",
            _ => throw new FormatException(),
        };
        return Convert.FromBase64String(normalized);
    }

    private static AutomationRequestException InvalidCapabilityCursor()
    {
        return RequestError(
            AutomationErrorCodes.RevisionConflict,
            AutomationErrorCategory.Conflict,
            "capability cursor 已失效、被篡改或不属于当前 query/digest。");
    }

    private static AutomationMethodRegistration CreateEventRegistration(
        string method,
        AutomationScheduledOperation operation)
    {
        string requestSchema = method switch
        {
            AutomationProtocolConstants.EventSubscribeMethod => "eventSubscribeRequest",
            AutomationProtocolConstants.EventAckMethod => "eventAckRequest",
            AutomationProtocolConstants.EventUnsubscribeMethod => "eventSubscriptionRequest",
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "未知 event control method。"),
        };
        return new AutomationMethodRegistration
        {
            Descriptor = new AutomationMethodDescriptor
            {
                Method = method,
                Domain = "event",
                RequestSchema = $"#/$defs/{requestSchema}",
                ResponseSchema = "#/$defs/subscriptionInfo",
                RequiredScopes = [AutomationScopes.EditorRead],
                SupportedModes = ["edit", "play", "paused"],
                OperationKind = AutomationOperationKind.Command,
                ExecutionPhase = AutomationExecutionPhase.EditorIngress,
                TransactionMode = AutomationTransactionMode.Forbidden,
                RequiresExpectedRevision = false,
                RequiresIdempotencyKey = false,
                EventTypes = [],
                ArtifactBehavior = AutomationArtifactBehavior.None,
                UiCommandIds = [],
            },
            Operation = operation,
        };
    }

    private static AutomationMethodRegistration CreateTransactionRegistration(
        string method,
        bool requiresIdempotency,
        AutomationScheduledOperation operation)
    {
        bool status = string.Equals(
            method,
            AutomationProtocolConstants.TransactionStatusMethod,
            StringComparison.Ordinal);
        string requestSchema = string.Equals(
            method,
            AutomationProtocolConstants.TransactionBeginMethod,
            StringComparison.Ordinal)
            ? "transactionBeginRequest"
            : "transactionRequest";
        string responseSchema = string.Equals(
            method,
            AutomationProtocolConstants.TransactionCommitMethod,
            StringComparison.Ordinal)
            ? "transactionCommitResult"
            : "transactionInfo";
        bool changesTransaction = method is
            AutomationProtocolConstants.TransactionCommitMethod or
            AutomationProtocolConstants.TransactionRollbackMethod;
        return new AutomationMethodRegistration
        {
            Descriptor = new AutomationMethodDescriptor
            {
                Method = method,
                Domain = "transaction",
                RequestSchema = $"#/$defs/{requestSchema}",
                ResponseSchema = $"#/$defs/{responseSchema}",
                RequiredScopes = [AutomationScopes.EditorControl],
                SupportedModes = ["edit", "play", "paused"],
                OperationKind = status
                    ? AutomationOperationKind.Read
                    : AutomationOperationKind.Command,
                ExecutionPhase = AutomationExecutionPhase.EditorIngress,
                TransactionMode = AutomationTransactionMode.Forbidden,
                RequiresExpectedRevision = false,
                RequiresIdempotencyKey = requiresIdempotency,
                EventTypes = changesTransaction
                    ? [AutomationProtocolConstants.TransactionChangedEventType]
                    : [],
                ArtifactBehavior = AutomationArtifactBehavior.None,
                UiCommandIds = [],
            },
            Operation = operation,
        };
    }

    private AutomationOperationResult BeginTransaction(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationTransactionBeginRequest request = DeserializeRequired(
            payload,
            AutomationJsonContext.Default.AutomationTransactionBeginRequest,
            AutomationProtocolConstants.TransactionBeginMethod);
        AutomationTransactionInfo info = _transactions.Begin(context.Request.SessionId, request);
        return new AutomationOperationResult
        {
            Payload = JsonSerializer.SerializeToElement(
                info,
                AutomationJsonContext.Default.AutomationTransactionInfo),
            RevisionOverride = info.BaseRevision,
        };
    }

    private AutomationOperationResult CommitTransaction(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationTransactionRequest request = DeserializeTransactionRequest(
            payload,
            AutomationProtocolConstants.TransactionCommitMethod);
        (AutomationTransactionCommitResult commitResult, AutomationRevisionSnapshot revision) = _transactions.Commit(
            context.Request.SessionId,
            request.TransactionId,
            context.CancellationToken);
        AutomationTransactionInfo info = commitResult.Transaction;
        PublishStateChanged(
            AutomationProtocolConstants.TransactionCommitMethod,
            info.ResourceIds,
            "commit",
            info.TransactionId,
            context.Request.RequestId,
            revision,
            AutomationProtocolConstants.TransactionChangedEventType);
        return new AutomationOperationResult
        {
            Payload = JsonSerializer.SerializeToElement(
                commitResult,
                AutomationJsonContext.Default.AutomationTransactionCommitResult),
            RevisionOverride = revision,
        };
    }

    private AutomationOperationResult RollbackTransaction(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationTransactionRequest request = DeserializeTransactionRequest(
            payload,
            AutomationProtocolConstants.TransactionRollbackMethod);
        AutomationTransactionInfo info = _transactions.Rollback(context.Request.SessionId, request.TransactionId);
        AutomationRevisionSnapshot revision = Revisions.Capture(info.ResourceIds);
        PublishStateChanged(
            AutomationProtocolConstants.TransactionRollbackMethod,
            info.ResourceIds,
            "rollback",
            info.TransactionId,
            context.Request.RequestId,
            revision,
            AutomationProtocolConstants.TransactionChangedEventType);
        return new AutomationOperationResult
        {
            Payload = JsonSerializer.SerializeToElement(
                info,
                AutomationJsonContext.Default.AutomationTransactionInfo),
            RevisionOverride = revision,
        };
    }

    private AutomationOperationResult GetTransactionStatus(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationTransactionRequest request = DeserializeTransactionRequest(
            payload,
            AutomationProtocolConstants.TransactionStatusMethod);
        AutomationTransactionInfo info = _transactions.GetInfo(context.Request.SessionId, request.TransactionId);
        return new AutomationOperationResult
        {
            Payload = JsonSerializer.SerializeToElement(
                info,
                AutomationJsonContext.Default.AutomationTransactionInfo),
            RevisionOverride = Revisions.CaptureAll(),
        };
    }

    private AutomationOperationResult SubscribeEvents(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationEventSubscribeRequest request = DeserializeRequired(
            payload,
            AutomationJsonContext.Default.AutomationEventSubscribeRequest,
            AutomationProtocolConstants.EventSubscribeMethod);
        AutomationSubscriptionInfo info = Events.Subscribe(context.Request.SessionId, request);
        return EventControlResult(info);
    }

    private AutomationOperationResult AcknowledgeEvents(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationEventAckRequest request = DeserializeRequired(
            payload,
            AutomationJsonContext.Default.AutomationEventAckRequest,
            AutomationProtocolConstants.EventAckMethod);
        AutomationSubscriptionInfo info = Events.Acknowledge(context.Request.SessionId, request);
        return EventControlResult(info);
    }

    private AutomationOperationResult UnsubscribeEvents(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationEventSubscriptionRequest request = DeserializeRequired(
            payload,
            AutomationJsonContext.Default.AutomationEventSubscriptionRequest,
            AutomationProtocolConstants.EventUnsubscribeMethod);
        AutomationSubscriptionInfo info = Events.Unsubscribe(context.Request.SessionId, request);
        return EventControlResult(info);
    }

    private AutomationOperationResult EventControlResult(AutomationSubscriptionInfo info)
    {
        return new AutomationOperationResult
        {
            Payload = JsonSerializer.SerializeToElement(
                info,
                AutomationJsonContext.Default.AutomationSubscriptionInfo),
            RevisionOverride = Revisions.CaptureAll(),
        };
    }

    private static bool IsEventControlMethod(string method)
    {
        return string.Equals(method, AutomationProtocolConstants.EventSubscribeMethod, StringComparison.Ordinal) ||
            string.Equals(method, AutomationProtocolConstants.EventAckMethod, StringComparison.Ordinal) ||
            string.Equals(method, AutomationProtocolConstants.EventUnsubscribeMethod, StringComparison.Ordinal);
    }

    private void PublishStateChanged(
        string method,
        string[] resourceIds,
        string changeKind,
        string? transactionId,
        string? causationRequestId,
        AutomationRevisionSnapshot revision,
        string eventType = AutomationProtocolConstants.StateChangedEventType)
    {
        AutomationStateChangedEvent payload = new()
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Method = method,
            ResourceIds = [.. resourceIds],
            ChangeKind = changeKind,
            TransactionId = transactionId,
        };
        _ = Events.Publish(
            eventType,
            revision,
            causationRequestId,
            JsonSerializer.SerializeToElement(
                payload,
                AutomationJsonContext.Default.AutomationStateChangedEvent));
    }

    private static AutomationTransactionRequest DeserializeTransactionRequest(
        JsonElement? payload,
        string method)
    {
        AutomationTransactionRequest request = DeserializeRequired(
            payload,
            AutomationJsonContext.Default.AutomationTransactionRequest,
            method);
        return request.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
            string.IsNullOrWhiteSpace(request.TransactionId)
            ? throw RequestError(
                AutomationErrorCodes.InvalidRequest,
                AutomationErrorCategory.Validation,
                $"Automation method '{method}' transaction payload 无效。")
            : request;
    }

    private static T DeserializeRequired<T>(
        JsonElement? payload,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        string method)
        where T : class
    {
        try
        {
            return payload?.Deserialize(typeInfo)
                ?? throw RequestError(
                    AutomationErrorCodes.InvalidRequest,
                    AutomationErrorCategory.Validation,
                    $"Automation method '{method}' 缺少 payload。");
        }
        catch (JsonException exception)
        {
            throw RequestError(
                AutomationErrorCodes.InvalidRequest,
                AutomationErrorCategory.Validation,
                $"Automation method '{method}' payload schema 无效：{exception.Message}");
        }
    }

    private void RequestTransactionExpiryRollback(string transactionId)
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            EnqueueInternal(() => PublishImplicitRollback(_transactions.Expire(transactionId), "expired"));
        }
    }

    private void PublishImplicitRollback(AutomationTransactionInfo? info, string changeKind)
    {
        if (info is null)
        {
            return;
        }

        PublishStateChanged(
            string.Equals(changeKind, "expired", StringComparison.Ordinal)
                ? "transaction.expired"
                : "transaction.disconnected",
            info.ResourceIds,
            changeKind,
            info.TransactionId,
            null,
            Revisions.Capture(info.ResourceIds),
            AutomationProtocolConstants.TransactionChangedEventType);
    }

    private void EnqueueInternal(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        bool wake;
        lock (_sync)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            _internalEditorIngress.Enqueue(new InternalWork(action));
            wake = Interlocked.Increment(ref _internalPendingCount) == 1 && _pendingCount == 0;
        }

        if (wake)
        {
            SignalWake(item: null);
        }
    }

    private void SignalWake(WorkItem? item)
    {
        try
        {
            _options.Wake?.Invoke();
        }
        catch (Exception exception)
        {
            // Wake 是跨平台窗口后端的 advisory signal。外部请求不能在 callback 抛错后遗留为
            // 不可观察的 queued work；内部清理仍留在 ingress，下一次正常 safe point 会处理它。
            if (item is null)
            {
                return;
            }

            bool removed = false;
            lock (_sync)
            {
                if (item.State == WorkItemState.Pending)
                {
                    int phase = (int)item.Registration.Descriptor.ExecutionPhase;
                    _queues[phase].Remove(item);
                    _phasePending[phase]--;
                    _pendingCount--;
                    item.State = WorkItemState.Completed;
                    removed = true;
                }
            }

            if (removed)
            {
                item.UnregisterCancellationCallback();
                _ = item.Completion.TrySetException(new AutomationConnectionExceptionForServer(
                    "Automation Editor wake signal 失败，请求未进入主线程执行。",
                    exception));
            }
        }
    }

    private static int EstimateTransactionStagedBytes(
        AutomationRequestContext context,
        JsonElement? payload)
    {
        long bytes = 256;
        if (payload is { } value)
        {
            bytes += Encoding.UTF8.GetByteCount(value.GetRawText());
        }

        if (context.ExpectedRevision is { } expected)
        {
            bytes += 64;
            for (int i = 0; i < expected.Resources.Length; i++)
            {
                bytes += 48 + Encoding.UTF8.GetByteCount(expected.Resources[i].ResourceId);
                if (bytes >= int.MaxValue)
                {
                    return int.MaxValue;
                }
            }
        }

        return checked((int)bytes);
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
                $"Automation operation resource 数不得超过 {AutomationProtocolConstants.MaxRevisionResources}。",
                nameof(resourceIds));
    }

    private static AutomationRevisionSnapshot NormalizeRevisionOverride(
        AutomationRevisionSnapshot revision)
    {
        if (revision.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
            revision.GlobalRevision < 0 || revision.Resources is null ||
            revision.Resources.Length > AutomationProtocolConstants.MaxRevisionResources)
        {
            throw new InvalidOperationException("Automation operation revision override 无效。");
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        AutomationResourceRevision[] resources =
        [
            .. revision.Resources
                .Select(resource => resource is not null &&
                    resource.SchemaVersion == AutomationProtocolConstants.WireSchemaVersion &&
                    resource.ResourceId is { Length: >= 1 } resourceId &&
                    resourceId.Length <= AutomationProtocolConstants.MaxResourceIdLength &&
                    !string.IsNullOrWhiteSpace(resourceId) && !resourceId.Any(char.IsControl) &&
                    resource.Revision >= 0 && ids.Add(resourceId)
                        ? resource with { }
                        : throw new InvalidOperationException(
                            "Automation operation revision override resource 无效或重复。"))
                .OrderBy(static resource => resource.ResourceId, StringComparer.Ordinal),
        ];
        return revision with { Resources = resources };
    }

    private static string ComputeCapabilityDigest(AutomationCapabilityDescriptor[] capabilities)
    {
        byte[] utf8 = JsonSerializer.SerializeToUtf8Bytes(
            capabilities,
            AutomationJsonContext.Default.AutomationCapabilityDescriptorArray);
        try
        {
            return Convert.ToHexStringLower(SHA256.HashData(utf8));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(utf8);
        }
    }

    private static AutomationCapabilityDescriptor CloneCapability(
        AutomationCapabilityDescriptor descriptor)
    {
        return descriptor with
        {
            RequiredScopes = [.. descriptor.RequiredScopes],
            SupportedModes = [.. descriptor.SupportedModes],
            EventTypes = [.. descriptor.EventTypes],
            UiCommandIds = [.. descriptor.UiCommandIds],
        };
    }

    private static void ValidateSchemaVersion(int schemaVersion, string method)
    {
        if (schemaVersion != AutomationProtocolConstants.WireSchemaVersion)
        {
            throw RequestError(
                AutomationErrorCodes.InvalidRequest,
                AutomationErrorCategory.Validation,
                $"Automation method '{method}' schemaVersion 必须为 {AutomationProtocolConstants.WireSchemaVersion}。");
        }
    }

    private static void ValidateRegistrations(IReadOnlyList<AutomationMethodRegistration> registrations)
    {
        HashSet<string> methods = new(StringComparer.Ordinal);
        for (int i = 0; i < registrations.Count; i++)
        {
            AutomationMethodRegistration registration = registrations[i]
                ?? throw new ArgumentException("Automation registration 不能为 null。", nameof(registrations));
            AutomationMethodDescriptor descriptor = registration.Descriptor
                ?? throw new ArgumentException("Automation descriptor 不能为 null。", nameof(registrations));
            ArgumentNullException.ThrowIfNull(registration.Operation);
            ArgumentNullException.ThrowIfNull(descriptor.RequiredScopes);
            ArgumentNullException.ThrowIfNull(descriptor.SupportedModes);
            ArgumentNullException.ThrowIfNull(descriptor.EventTypes);
            ArgumentNullException.ThrowIfNull(descriptor.UiCommandIds);
            if (!IsSemanticIdentifier(descriptor.Method, 256))
            {
                throw new ArgumentException(
                    "Automation method 必须是 1..256 字符的 ASCII semantic id。",
                    nameof(registrations));
            }

            if (!IsSemanticIdentifier(descriptor.Domain, 64) ||
                descriptor.RequestSchema is not { Length: >= 9 and <= 256 } ||
                !descriptor.RequestSchema.StartsWith("#/$defs/", StringComparison.Ordinal) ||
                descriptor.ResponseSchema is not { Length: >= 9 and <= 256 } ||
                !descriptor.ResponseSchema.StartsWith("#/$defs/", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Automation method '{descriptor.Method}' 的 domain 或 schema reference 无效。",
                    nameof(registrations));
            }

            if (descriptor.SupportedModes is not { Length: >= 1 and <= 16 } ||
                descriptor.SupportedModes.Any(static mode => !IsSemanticIdentifier(mode, 32)) ||
                descriptor.SupportedModes.Distinct(StringComparer.Ordinal).Count() != descriptor.SupportedModes.Length ||
                descriptor.EventTypes.Length > 256 ||
                descriptor.EventTypes.Any(static eventType => !IsSemanticIdentifier(eventType, 128)) ||
                descriptor.EventTypes.Distinct(StringComparer.Ordinal).Count() != descriptor.EventTypes.Length ||
                descriptor.UiCommandIds.Length > 256 ||
                descriptor.UiCommandIds.Any(static commandId => !IsSemanticIdentifier(commandId, 128)) ||
                descriptor.UiCommandIds.Distinct(StringComparer.Ordinal).Count() != descriptor.UiCommandIds.Length ||
                !Enum.IsDefined(descriptor.ArtifactBehavior))
            {
                throw new ArgumentException(
                    $"Automation method '{descriptor.Method}' 的 modes/events/artifact/UI command metadata 无效。",
                    nameof(registrations));
            }

            if (descriptor.RequiredScopes is not { Length: >= 1 and <= 32 } ||
                descriptor.RequiredScopes.Any(static scope => !IsSemanticIdentifier(scope, 64) ||
                    !char.IsAsciiLetter(scope[0])) ||
                descriptor.RequiredScopes.Distinct(StringComparer.Ordinal).Count() != descriptor.RequiredScopes.Length)
            {
                throw new ArgumentException(
                    $"Automation method '{descriptor.Method}' 的 required scopes 无效、重复或超过上限。",
                    nameof(registrations));
            }

            if (!Enum.IsDefined(descriptor.OperationKind) || !Enum.IsDefined(descriptor.TransactionMode))
            {
                throw new ArgumentException(
                    $"Automation method '{descriptor.Method}' 的 operation/transaction enum 无效。",
                    nameof(registrations));
            }

            ValidatePhase(descriptor.ExecutionPhase, allowBackground: false);
            if (!methods.Add(descriptor.Method))
            {
                throw new ArgumentException($"Automation method '{descriptor.Method}' 重复登记。", nameof(registrations));
            }

            if (descriptor.TransactionMode != AutomationTransactionMode.Forbidden &&
                descriptor.OperationKind != AutomationOperationKind.Write)
            {
                throw new ArgumentException(
                    $"只有 Write capability 可声明 transaction mode {descriptor.TransactionMode}。",
                    nameof(registrations));
            }

            if (descriptor.OperationKind == AutomationOperationKind.Write &&
                descriptor.TransactionMode == AutomationTransactionMode.Forbidden)
            {
                throw new ArgumentException(
                    $"Write capability '{descriptor.Method}' 必须可逆并声明 Optional/Required transaction mode；不可逆动作应为 Command。",
                    nameof(registrations));
            }

            if (descriptor.RequiresExpectedRevision && descriptor.OperationKind == AutomationOperationKind.Read)
            {
                throw new ArgumentException(
                    $"Read capability 不得要求 expected revision：'{descriptor.Method}'。",
                    nameof(registrations));
            }

            if (descriptor.RequiresIdempotencyKey && descriptor.OperationKind == AutomationOperationKind.Read)
            {
                throw new ArgumentException(
                    $"Read capability 不得要求 idempotency key：'{descriptor.Method}'。",
                    nameof(registrations));
            }

            if (descriptor.TransactionMode != AutomationTransactionMode.Forbidden &&
                descriptor.ExecutionPhase != AutomationExecutionPhase.EditorIngress)
            {
                throw new ArgumentException(
                    $"Transaction-capable method '{descriptor.Method}' 必须在 EditorIngress 原子提交。",
                    nameof(registrations));
            }
        }
    }

    private static bool IsSemanticIdentifier(string? value, int maxLength)
    {
        return value is { Length: >= 1 } && value.Length <= maxLength &&
            char.IsAsciiLetter(value[0]) && value.All(static character =>
                char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
    }

    private static AutomationMainThreadSchedulerOptions ValidateOptions(
        AutomationMainThreadSchedulerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options.TimeProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxItemsPerDrain);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.DefaultTransactionLease, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxTransactionLease, options.DefaultTransactionLease);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxTransactionOperations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxTransactionStagedBytes);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.IdempotencyRetention, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.IdempotencyCapacity);
        return options;
    }

    private static void ValidateOptionalIdentifier(string? value, string field)
    {
        if (value is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl))
        {
            throw RequestError(
                AutomationErrorCodes.InvalidRequest,
                AutomationErrorCategory.Validation,
                $"Automation {field} 长度或字符无效。");
        }
    }

    private static void ValidatePhase(AutomationExecutionPhase phase, bool allowBackground)
    {
        if ((uint)phase >= PhaseCount || (!allowBackground && phase == AutomationExecutionPhase.Background))
        {
            throw new ArgumentOutOfRangeException(nameof(phase), phase, "未知或不可 drain 的 automation phase。");
        }
    }

    private static AutomationRequestException RequestError(
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

    private void AssertOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("Automation scheduler 只能由创建它的 Editor 主线程 drain/dispose。");
        }
    }

    private sealed class WorkItem(
        AutomationRequestContext context,
        AutomationMethodRegistration registration,
        JsonElement? payload,
        int stagedBytes,
        CancellationToken cancellationToken,
        AutomationMainThreadScheduler owner)
    {
        private readonly Lock _registrationSync = new();
        private CancellationTokenRegistration _cancellationRegistration;
        private bool _registrationAssigned;

        public AutomationRequestContext Context { get; } = context;

        public AutomationMethodRegistration Registration { get; } = registration;

        public JsonElement? Payload { get; } = payload;

        public int StagedBytes { get; } = stagedBytes;

        public CancellationToken CancellationToken { get; } = cancellationToken;

        public AutomationMainThreadScheduler Owner { get; } = owner;

        public TaskCompletionSource<AutomationHandlerResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public WorkItemState State
        {
            get => (WorkItemState)Volatile.Read(ref _state);
            set => Volatile.Write(ref _state, (int)value);
        }

        public WorkItem? Previous { get; set; }

        public WorkItem? Next { get; set; }

        private int _state = (int)WorkItemState.Pending;

        public void SetCancellationRegistration(CancellationTokenRegistration registration)
        {
            lock (_registrationSync)
            {
                if (State is WorkItemState.Cancelled or WorkItemState.Completed or WorkItemState.Executing)
                {
                    registration.Dispose();
                    return;
                }

                _cancellationRegistration = registration;
                _registrationAssigned = true;
            }
        }

        public void DisposeCancellationRegistration()
        {
            lock (_registrationSync)
            {
                if (!_registrationAssigned)
                {
                    return;
                }

                _cancellationRegistration.Dispose();
                _registrationAssigned = false;
            }
        }

        public void UnregisterCancellationCallback()
        {
            lock (_registrationSync)
            {
                if (!_registrationAssigned)
                {
                    return;
                }

                _ = _cancellationRegistration.Unregister();
                _registrationAssigned = false;
            }
        }
    }

    private sealed class PhaseQueue
    {
        private WorkItem? _head;
        private WorkItem? _tail;

        public void AddLast(WorkItem item)
        {
            item.Previous = _tail;
            item.Next = null;
            if (_tail is null)
            {
                _head = item;
            }
            else
            {
                _tail.Next = item;
            }

            _tail = item;
        }

        public WorkItem? RemoveFirst()
        {
            WorkItem? item = _head;
            if (item is not null)
            {
                Remove(item);
            }

            return item;
        }

        public void Remove(WorkItem item)
        {
            if (item.Previous is null)
            {
                _head = item.Next;
            }
            else
            {
                item.Previous.Next = item.Next;
            }

            if (item.Next is null)
            {
                _tail = item.Previous;
            }
            else
            {
                item.Next.Previous = item.Previous;
            }

            item.Previous = null;
            item.Next = null;
        }
    }

    private sealed record InternalWork(Action Action);

    private enum WorkItemState
    {
        Pending,
        Executing,
        Completed,
        Cancelled,
    }

    private sealed class AutomationConnectionExceptionForServer : Exception
    {
        public AutomationConnectionExceptionForServer(string message)
            : base(message)
        {
        }

        public AutomationConnectionExceptionForServer(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}

internal sealed class RevisionTrackingUndoAction(
    IAutomationUndoAction action,
    AutomationRevisionStore revisions,
    string[] resourceIds) : IAutomationUndoAction
{
    public string Name => action.Name;

    public void Undo()
    {
        action.Undo();
        _ = revisions.Advance(resourceIds);
    }

    public void Redo()
    {
        action.Redo();
        _ = revisions.Advance(resourceIds);
    }
}
