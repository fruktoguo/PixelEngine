using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Server;

/// <summary>
/// Event replay retention、订阅数量与 payload 边界。
/// </summary>
public sealed record AutomationEventHubOptions
{
    /// <summary>全部 principal/client 的订阅总上限。</summary>
    public int MaxSubscriptions { get; init; } = 1024;

    /// <summary>单订阅允许的最大未 ack event 数。</summary>
    public int MaxBacklogLimit { get; init; } = 65_536;

    /// <summary>断线订阅可恢复的时长。</summary>
    public TimeSpan ResumeRetention { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>单 event 小型 JSON payload 上限。</summary>
    public int MaxEventPayloadBytes { get; init; } = 256 * 1024;

    /// <summary>全部订阅合计允许保留的最大 event record 数。</summary>
    public int MaxBufferedEvents { get; init; } = 65_536;

    /// <summary>全部订阅合计允许保留的完整 event record 保守估算字节数。</summary>
    public long MaxBufferedBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>可测试时钟。</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}

/// <summary>
/// 事件驱动、无轮询的 bounded replay hub；连接断开后按 principal/client/key 恢复。
/// </summary>
/// <param name="options">retention 与容量。</param>
public sealed class AutomationEventHub(AutomationEventHubOptions? options = null) : IDisposable
{
    private readonly Lock _sync = new();
    private readonly Dictionary<string, SessionState> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SubscriptionState> _subscriptions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _resumeTokens = new(StringComparer.Ordinal);
    private readonly Dictionary<SubscriptionIdentity, string> _subscriptionKeys = [];
    private readonly AutomationEventHubOptions _options = ValidateOptions(options ?? new AutomationEventHubOptions());
    private int _bufferedEventCount;
    private long _bufferedBytes;
    private bool _disposed;

    /// <summary>登记已认证 connection 的 event sink。</summary>
    /// <param name="session">认证身份。</param>
    /// <param name="sink">非阻塞有界 sink。</param>
    public void OpenSession(AutomationSessionContext session, IAutomationEventSink sink)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(sink);
        ValidateSession(session);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            PruneExpiredCore(_options.TimeProvider.GetUtcNow());
            if (!_sessions.TryAdd(session.SessionId, new SessionState(session, sink)))
            {
                throw new InvalidOperationException($"Automation event session '{session.SessionId}' 已登记。");
            }
        }
    }

    /// <summary>断开 session，并保留其订阅用于 bounded replay。</summary>
    /// <param name="sessionId">已关闭 session。</param>
    public void CloseSession(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        lock (_sync)
        {
            if (_disposed || !_sessions.Remove(sessionId))
            {
                return;
            }

            DateTimeOffset expiresAtUtc = _options.TimeProvider.GetUtcNow() + _options.ResumeRetention;
            foreach (SubscriptionState subscription in _subscriptions.Values)
            {
                if (string.Equals(subscription.ConnectedSessionId, sessionId, StringComparison.Ordinal))
                {
                    subscription.ConnectedSessionId = null;
                    subscription.Sink = null;
                    subscription.ResumeExpiresAtUtc = expiresAtUtc;
                }
            }
        }
    }

    /// <summary>创建或恢复订阅，并在响应前把 replay 按 sequence 排入连接。</summary>
    /// <param name="sessionId">当前认证 session。</param>
    /// <param name="request">filter、resume 与 backlog 配置。</param>
    /// <returns>订阅及 replay 边界。</returns>
    public AutomationSubscriptionInfo Subscribe(
        string sessionId,
        AutomationEventSubscribeRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        string[] eventTypes = ValidateSubscribeRequest(request);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            DateTimeOffset now = _options.TimeProvider.GetUtcNow();
            PruneExpiredCore(now);
            SessionState session = RequireSession(sessionId);
            SubscriptionIdentity identity = new(
                session.Context.PrincipalId,
                session.Context.ClientInstanceId,
                request.SubscriptionKey);
            SubscriptionState subscription = ResolveSubscription(request, identity, eventTypes, now);
            if (subscription.Overflowed)
            {
                AutomationRequestException exception = ResyncError(subscription, overflow: true);
                RemoveSubscriptionCore(subscription);
                throw exception;
            }

            bool alreadyActiveOnSession = string.Equals(
                subscription.ConnectedSessionId,
                sessionId,
                StringComparison.Ordinal);
            if (request.AfterSequence is { } afterSequence)
            {
                AcknowledgeCore(subscription, afterSequence);
            }

            IAutomationEventSink? previousSink = alreadyActiveOnSession ? null : subscription.Sink;
            previousSink?.Abort();
            long? replayFrom = null;
            if (!alreadyActiveOnSession)
            {
                subscription.ConnectedSessionId = sessionId;
                subscription.Sink = session.Sink;
                subscription.ResumeExpiresAtUtc = now + _options.ResumeRetention;
                foreach (BufferedEvent buffered in subscription.Backlog)
                {
                    AutomationEventRecord eventRecord = buffered.Record;
                    replayFrom ??= eventRecord.Sequence;
                    if (!session.Sink.TryPublish(eventRecord))
                    {
                        subscription.ConnectedSessionId = null;
                        subscription.Sink = null;
                        subscription.ResumeExpiresAtUtc = now + _options.ResumeRetention;
                        session.Sink.Abort();
                        throw Busy("连接 event 队列无法容纳 subscription replay。");
                    }
                }
            }

            return ToInfo(subscription, AutomationEventSubscriptionStatus.Active, replayFrom, now);
        }
    }

    /// <summary>确认连续处理完成的最大 sequence 并释放 replay 容量。</summary>
    /// <param name="sessionId">当前 session。</param>
    /// <param name="request">subscription 与 sequence。</param>
    /// <returns>ack 后状态。</returns>
    public AutomationSubscriptionInfo Acknowledge(
        string sessionId,
        AutomationEventAckRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ValidateAckRequest(request);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            DateTimeOffset now = _options.TimeProvider.GetUtcNow();
            PruneExpiredCore(now);
            SessionState session = RequireSession(sessionId);
            SubscriptionState subscription = RequireOwnedSubscription(request.SubscriptionId, session.Context);
            if (!string.Equals(subscription.ConnectedSessionId, sessionId, StringComparison.Ordinal))
            {
                throw InvalidRequest("只能由当前绑定 subscription 的 session ack event。");
            }

            AcknowledgeCore(subscription, request.Sequence);
            return ToInfo(subscription, AutomationEventSubscriptionStatus.Active, replayFrom: null, now);
        }
    }

    /// <summary>显式删除 subscription 及其 replay backlog。</summary>
    /// <param name="sessionId">当前 session。</param>
    /// <param name="request">subscription id。</param>
    /// <returns>removed 状态。</returns>
    public AutomationSubscriptionInfo Unsubscribe(
        string sessionId,
        AutomationEventSubscriptionRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(request);
        if (request.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
            !IsIdentifier(request.SubscriptionId, 128))
        {
            throw InvalidRequest("event.unsubscribe payload 无效。");
        }

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            DateTimeOffset now = _options.TimeProvider.GetUtcNow();
            SessionState session = RequireSession(sessionId);
            SubscriptionState subscription = RequireOwnedSubscription(request.SubscriptionId, session.Context);
            AutomationSubscriptionInfo info = ToInfo(
                subscription,
                AutomationEventSubscriptionStatus.Removed,
                replayFrom: null,
                now);
            RemoveSubscriptionCore(subscription);
            return info;
        }
    }

    /// <summary>发布一条 semantic event；只为匹配订阅分配 record，不做帧轮询。</summary>
    /// <param name="eventType">稳定 semantic event type。</param>
    /// <param name="stateRevision">事件对应权威 snapshot。</param>
    /// <param name="causationRequestId">可选产生事件的 request id。</param>
    /// <param name="payload">小型 payload；大型数据必须用 artifact reference。</param>
    /// <returns>成功进入 replay backlog 的订阅数。</returns>
    public int Publish(
        string eventType,
        AutomationRevisionSnapshot stateRevision,
        string? causationRequestId = null,
        JsonElement? payload = null)
    {
        int recordBytes = ValidateEvent(eventType, stateRevision, causationRequestId, payload);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            DateTimeOffset now = _options.TimeProvider.GetUtcNow();
            PruneExpiredCore(now);
            int published = 0;
            foreach (SubscriptionState subscription in _subscriptions.Values)
            {
                if (subscription.Overflowed || !Matches(subscription.EventTypes, eventType))
                {
                    continue;
                }

                if (subscription.Backlog.Count >= subscription.BacklogLimit ||
                    _bufferedEventCount >= _options.MaxBufferedEvents ||
                    recordBytes > _options.MaxBufferedBytes - _bufferedBytes)
                {
                    MarkOverflowed(subscription, now);
                    continue;
                }

                AutomationEventRecord eventRecord = new()
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    SubscriptionId = subscription.SubscriptionId,
                    Sequence = subscription.NextSequence++,
                    EventType = eventType,
                    StateRevision = CloneRevision(stateRevision),
                    CausationRequestId = causationRequestId,
                    PublishedAtUtc = now,
                    Payload = payload?.Clone(),
                };
                subscription.Backlog.Enqueue(new BufferedEvent(eventRecord, recordBytes));
                _bufferedEventCount++;
                _bufferedBytes += recordBytes;
                published++;
                if (subscription.Sink is { } liveSink && !liveSink.TryPublish(eventRecord))
                {
                    subscription.Sink = null;
                    subscription.ConnectedSessionId = null;
                    subscription.ResumeExpiresAtUtc = now + _options.ResumeRetention;
                    liveSink.Abort();
                }
            }

            return published;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        IAutomationEventSink[] sinks;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            sinks = [.. _sessions.Values.Select(static session => session.Sink).Distinct()];
            _sessions.Clear();
            _subscriptions.Clear();
            _resumeTokens.Clear();
            _subscriptionKeys.Clear();
            _bufferedEventCount = 0;
            _bufferedBytes = 0;
        }

        for (int i = 0; i < sinks.Length; i++)
        {
            sinks[i].Abort();
        }
    }

    private SubscriptionState ResolveSubscription(
        AutomationEventSubscribeRequest request,
        SubscriptionIdentity identity,
        string[] eventTypes,
        DateTimeOffset now)
    {
        SubscriptionState? subscription = null;
        if (!string.IsNullOrWhiteSpace(request.ResumeToken))
        {
            if (!_resumeTokens.TryGetValue(request.ResumeToken, out string? subscriptionId) ||
                !_subscriptions.TryGetValue(subscriptionId, out subscription))
            {
                throw ResyncError(subscription: null, overflow: false);
            }
        }
        else if (_subscriptionKeys.TryGetValue(identity, out string? existingId))
        {
            subscription = _subscriptions[existingId];
        }

        if (subscription is null)
        {
            if (request.AfterSequence is > 0)
            {
                throw InvalidRequest("新 subscription 的 afterSequence 只能为空或 0。");
            }

            if (_subscriptions.Count >= _options.MaxSubscriptions)
            {
                throw Busy("Automation event subscription 数量已达上限。");
            }

            subscription = new SubscriptionState
            {
                SubscriptionId = Guid.NewGuid().ToString("N"),
                ResumeToken = GenerateResumeToken(),
                Identity = identity,
                EventTypes = eventTypes,
                BacklogLimit = request.BacklogLimit,
                ResumeExpiresAtUtc = now + _options.ResumeRetention,
            };
            _subscriptions.Add(subscription.SubscriptionId, subscription);
            _resumeTokens.Add(subscription.ResumeToken, subscription.SubscriptionId);
            _subscriptionKeys.Add(identity, subscription.SubscriptionId);
            return subscription;
        }

        return subscription.Identity != identity
            ? throw PermissionDenied("resume token 不属于当前 principal/client/subscription key。")
            : subscription.BacklogLimit != request.BacklogLimit ||
              !subscription.EventTypes.SequenceEqual(eventTypes, StringComparer.Ordinal)
                ? throw InvalidRequest("恢复 subscription 时 eventTypes/backlogLimit 必须与创建时一致。")
                : subscription;
    }

    private void AcknowledgeCore(SubscriptionState subscription, long sequence)
    {
        if (sequence < 0 || sequence >= subscription.NextSequence)
        {
            throw InvalidRequest("event ack/after sequence 超出已发布范围。");
        }

        if (sequence <= subscription.AcknowledgedSequence)
        {
            return;
        }

        subscription.AcknowledgedSequence = sequence;
        while (subscription.Backlog.TryPeek(out BufferedEvent buffered) &&
               buffered.Record.Sequence <= sequence)
        {
            _ = subscription.Backlog.Dequeue();
            _bufferedEventCount--;
            _bufferedBytes -= buffered.RecordBytes;
        }
    }

    private SubscriptionState RequireOwnedSubscription(
        string subscriptionId,
        AutomationSessionContext session)
    {
        SubscriptionState subscription = _subscriptions.TryGetValue(
            subscriptionId,
            out SubscriptionState? found)
            ? found
            : throw ResyncError(subscription: null, overflow: false);
        return string.Equals(subscription.Identity.PrincipalId, session.PrincipalId, StringComparison.Ordinal) &&
            string.Equals(subscription.Identity.ClientInstanceId, session.ClientInstanceId, StringComparison.Ordinal)
            ? subscription
            : throw PermissionDenied("subscription 不属于当前 principal/client。");
    }

    private SessionState RequireSession(string sessionId)
    {
        return _sessions.TryGetValue(sessionId, out SessionState? session)
            ? session
            : throw InvalidRequest("当前 event session 未登记或已经关闭。");
    }

    private void PruneExpiredCore(DateTimeOffset now)
    {
        List<SubscriptionState>? expired = null;
        foreach (SubscriptionState subscription in _subscriptions.Values)
        {
            if (subscription.ConnectedSessionId is null && subscription.ResumeExpiresAtUtc <= now)
            {
                (expired ??= []).Add(subscription);
            }
        }

        if (expired is null)
        {
            return;
        }

        for (int i = 0; i < expired.Count; i++)
        {
            RemoveSubscriptionCore(expired[i]);
        }
    }

    private void RemoveSubscriptionCore(SubscriptionState subscription)
    {
        _ = _subscriptions.Remove(subscription.SubscriptionId);
        _ = _resumeTokens.Remove(subscription.ResumeToken);
        _ = _subscriptionKeys.Remove(subscription.Identity);
        while (subscription.Backlog.TryDequeue(out BufferedEvent buffered))
        {
            _bufferedEventCount--;
            _bufferedBytes -= buffered.RecordBytes;
        }

        subscription.Sink = null;
        subscription.ConnectedSessionId = null;
    }

    private void MarkOverflowed(SubscriptionState subscription, DateTimeOffset now)
    {
        subscription.Overflowed = true;
        subscription.LostSequence = subscription.NextSequence;
        subscription.NextSequence++;
        IAutomationEventSink? overflowSink = subscription.Sink;
        subscription.Sink = null;
        subscription.ConnectedSessionId = null;
        subscription.ResumeExpiresAtUtc = now + _options.ResumeRetention;
        overflowSink?.Abort();
    }

    private AutomationSubscriptionInfo ToInfo(
        SubscriptionState subscription,
        AutomationEventSubscriptionStatus status,
        long? replayFrom,
        DateTimeOffset now)
    {
        return new AutomationSubscriptionInfo
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            SubscriptionId = subscription.SubscriptionId,
            ResumeToken = subscription.ResumeToken,
            Status = status,
            NextSequence = subscription.NextSequence,
            AcknowledgedSequence = subscription.AcknowledgedSequence,
            BacklogCount = subscription.Backlog.Count,
            ReplayFromSequence = replayFrom,
            ResumeExpiresAtUtc = status == AutomationEventSubscriptionStatus.Active
                ? now + _options.ResumeRetention
                : subscription.ResumeExpiresAtUtc,
        };
    }

    private string[] ValidateSubscribeRequest(AutomationEventSubscribeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
            string.IsNullOrWhiteSpace(request.SubscriptionKey) || request.SubscriptionKey.Length > 128 ||
            request.SubscriptionKey.Any(char.IsControl) || request.EventTypes is null ||
            request.BacklogLimit <= 0 || request.BacklogLimit > _options.MaxBacklogLimit ||
            request.AfterSequence < 0 || request.ResumeToken?.Length > 256 ||
            request.ResumeToken?.Any(char.IsControl) == true ||
            (request.ResumeToken is not null && string.IsNullOrWhiteSpace(request.ResumeToken)))
        {
            throw InvalidRequest("event.subscribe payload 无效。");
        }

        string[] eventTypes =
        [
            .. request.EventTypes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
        ];
        return eventTypes.Length <= AutomationProtocolConstants.MaxEventFilterTypes &&
            eventTypes.Length == request.EventTypes.Length &&
            eventTypes.All(static eventType => IsEventType(eventType))
            ? eventTypes
            : throw InvalidRequest("eventTypes 必须是唯一、有效的稳定 event type。");
    }

    private static void ValidateAckRequest(AutomationEventAckRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
            !IsIdentifier(request.SubscriptionId, 128) || request.Sequence < 0)
        {
            throw InvalidRequest("event.ack payload 无效。");
        }
    }

    private int ValidateEvent(
        string eventType,
        AutomationRevisionSnapshot revision,
        string? causationRequestId,
        JsonElement? payload)
    {
        if (payload is { ValueKind: JsonValueKind.Undefined })
        {
            throw new ArgumentException("Automation event payload 不得是 Undefined。", nameof(payload));
        }

        int payloadBytes = payload is { } value
            ? Encoding.UTF8.GetByteCount(value.GetRawText())
            : 0;
        if (!IsEventType(eventType) ||
            (causationRequestId is not null && !IsIdentifier(causationRequestId, 128)) ||
            payloadBytes > _options.MaxEventPayloadBytes)
        {
            throw new ArgumentException("Automation event type/causation/payload 无效。", nameof(eventType));
        }

        ValidateRevision(revision);
        long recordBytes = 512L + payloadBytes +
            (2L * Encoding.UTF8.GetByteCount(eventType)) +
            (causationRequestId is null ? 0 : 2L * Encoding.UTF8.GetByteCount(causationRequestId));
        for (int i = 0; i < revision.Resources.Length; i++)
        {
            // JSON escaping、对象/array 与 managed clone 开销均保守计入，防止小 payload + 巨大 revision 绕过配额。
            recordBytes += 128L + (2L * Encoding.UTF8.GetByteCount(revision.Resources[i].ResourceId));
        }

        return recordBytes >= int.MaxValue ? int.MaxValue : checked((int)recordBytes);
    }

    private static bool Matches(string[] eventTypes, string eventType)
    {
        return eventTypes.Length == 0 || Array.BinarySearch(eventTypes, eventType, StringComparer.Ordinal) >= 0;
    }

    private static bool IsEventType(string? eventType)
    {
        return eventType is { Length: >= 1 and <= 128 } && eventType.All(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or ':');
    }

    private static void ValidateSession(AutomationSessionContext session)
    {
        if (!IsIdentifier(session.SessionId, 128) || !IsIdentifier(session.PrincipalId, 128) ||
            !IsIdentifier(session.ClientInstanceId, 128) || !IsIdentifier(session.ClientName, 128) ||
            session.GrantedScopes is not { Length: <= 32 } ||
            session.GrantedScopes.Any(static scope => scope is not { Length: >= 1 and <= 64 } ||
                !char.IsAsciiLetter(scope[0]) || scope.Any(static character =>
                    !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not '-')) ||
            session.GrantedScopes.Distinct(StringComparer.Ordinal).Count() != session.GrantedScopes.Length)
        {
            throw new ArgumentException("Automation event session identity 不完整。", nameof(session));
        }
    }

    private static void ValidateRevision(AutomationRevisionSnapshot revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        if (revision.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
            revision.GlobalRevision < 0 || revision.Resources is null ||
            revision.Resources.Length > AutomationProtocolConstants.MaxRevisionResources ||
            revision.Resources.Any(static resource => resource is null ||
                resource.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
                !IsIdentifier(resource.ResourceId, AutomationProtocolConstants.MaxResourceIdLength) ||
                resource.Revision < 0) ||
            revision.Resources.Select(static resource => resource.ResourceId)
                .Distinct(StringComparer.Ordinal).Count() != revision.Resources.Length)
        {
            throw new ArgumentException("Automation event revision 无效。", nameof(revision));
        }
    }

    private static AutomationRevisionSnapshot CloneRevision(AutomationRevisionSnapshot revision)
    {
        return revision with
        {
            Resources = [.. revision.Resources.Select(static resource => resource with { })],
        };
    }

    private static string GenerateResumeToken()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        try
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static bool IsIdentifier(string? value, int maxLength)
    {
        return value is { Length: >= 1 } && value.Length <= maxLength &&
            !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);
    }

    private static AutomationRequestException ResyncError(SubscriptionState? subscription, bool overflow)
    {
        AutomationEventResyncDetails details = new()
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            SubscriptionId = subscription?.SubscriptionId,
            AcknowledgedSequence = subscription?.AcknowledgedSequence,
            LostSequence = subscription?.LostSequence,
            Reason = overflow ? "backlog_overflow" : "resume_state_unavailable",
        };
        return new AutomationRequestException(new AutomationError
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Code = overflow ? AutomationErrorCodes.EventOverflow : AutomationErrorCodes.ResyncRequired,
            Category = AutomationErrorCategory.Conflict,
            Message = overflow
                ? "Event backlog 已溢出；必须重新读取权威 snapshot 后创建新订阅。"
                : "Event resume state 不存在或已超过 retention；必须重新读取权威 snapshot。",
            Details = JsonSerializer.SerializeToElement(
                details,
                AutomationJsonContext.Default.AutomationEventResyncDetails),
            Transient = false,
        });
    }

    private static AutomationRequestException InvalidRequest(string message)
    {
        return Error(AutomationErrorCodes.InvalidRequest, AutomationErrorCategory.Validation, message);
    }

    private static AutomationRequestException PermissionDenied(string message)
    {
        return Error(AutomationErrorCodes.PermissionDenied, AutomationErrorCategory.Authorization, message);
    }

    private static AutomationRequestException Busy(string message)
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

    private static AutomationEventHubOptions ValidateOptions(AutomationEventHubOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.TimeProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxSubscriptions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxBacklogLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxEventPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxBufferedEvents);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxBufferedBytes);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.ResumeRetention, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.ResumeRetention, TimeSpan.FromDays(7));
        return options;
    }

    private sealed record SessionState(AutomationSessionContext Context, IAutomationEventSink Sink);

    private readonly record struct SubscriptionIdentity(
        string PrincipalId,
        string ClientInstanceId,
        string SubscriptionKey);

    private sealed class SubscriptionState
    {
        public required string SubscriptionId { get; init; }

        public required string ResumeToken { get; init; }

        public required SubscriptionIdentity Identity { get; init; }

        public required string[] EventTypes { get; init; }

        public required int BacklogLimit { get; init; }

        public required DateTimeOffset ResumeExpiresAtUtc { get; set; }

        public Queue<BufferedEvent> Backlog { get; } = new();

        public long NextSequence { get; set; } = 1;

        public long AcknowledgedSequence { get; set; }

        public long? LostSequence { get; set; }

        public bool Overflowed { get; set; }

        public string? ConnectedSessionId { get; set; }

        public IAutomationEventSink? Sink { get; set; }
    }

    private readonly record struct BufferedEvent(AutomationEventRecord Record, int RecordBytes);
}
