using System.Collections.Frozen;
using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Server;

/// <summary>
/// 一个可执行 automation method 的权限与调度前置描述。
/// </summary>
public sealed record AutomationMethodDescriptor
{
    /// <summary>稳定 method/capability id。</summary>
    public required string Method { get; init; }

    /// <summary>执行该 method 所需全部 scopes。</summary>
    public required string[] RequiredScopes { get; init; }

    /// <summary>稳定 capability domain。</summary>
    public string Domain { get; init; } = "system";

    /// <summary>请求 DTO 的 JSON Schema reference。</summary>
    public string RequestSchema { get; init; } = "#/$defs/emptyRequest";

    /// <summary>响应 DTO 的 JSON Schema reference。</summary>
    public string ResponseSchema { get; init; } = "#/$defs/emptyResponse";

    /// <summary>支持的 Editor mode。</summary>
    public string[] SupportedModes { get; init; } = ["edit", "play", "paused"];

    /// <summary>只读、写入或非事务 command。</summary>
    public required AutomationOperationKind OperationKind { get; init; }

    /// <summary>允许访问权威对象的唯一 safe phase。</summary>
    public required AutomationExecutionPhase ExecutionPhase { get; init; }

    /// <summary>transaction 参与策略。</summary>
    public required AutomationTransactionMode TransactionMode { get; init; }

    /// <summary>是否强制写请求携带 expected revision。</summary>
    public bool RequiresExpectedRevision { get; init; }

    /// <summary>是否强制请求携带跨连接 idempotency key。</summary>
    public bool RequiresIdempotencyKey { get; init; }

    /// <summary>是否声明 safe-phase freeze → background prepare → safe-phase commit。</summary>
    public bool UsesBackgroundPreparation { get; init; }

    /// <summary>该 capability 可能发布的 event type。</summary>
    public string[] EventTypes { get; init; } = [];

    /// <summary>大型结果的 artifact 行为。</summary>
    public AutomationArtifactBehavior ArtifactBehavior { get; init; }

    /// <summary>复用相同 semantic implementation 的 UI command IDs。</summary>
    public string[] UiCommandIds { get; init; } = [];

    /// <summary>生成独立于 Server assembly 的发布 descriptor。</summary>
    /// <returns>深复制的 protocol descriptor。</returns>
    public AutomationCapabilityDescriptor ToCapabilityDescriptor()
    {
        return new AutomationCapabilityDescriptor
        {
            Id = Method,
            Domain = Domain,
            OperationKind = OperationKind,
            RequestSchema = RequestSchema,
            ResponseSchema = ResponseSchema,
            RequiredScopes = [.. RequiredScopes],
            SupportedModes = [.. SupportedModes],
            ExecutionPhase = ExecutionPhase,
            TransactionMode = TransactionMode,
            RequiresExpectedRevision = RequiresExpectedRevision,
            RequiresIdempotencyKey = RequiresIdempotencyKey,
            UsesBackgroundPreparation = UsesBackgroundPreparation,
            EventTypes = [.. EventTypes],
            ArtifactBehavior = ArtifactBehavior,
            UiCommandIds = [.. UiCommandIds],
        };
    }
}

/// <summary>
/// 认证 session 的稳定身份上下文。
/// </summary>
public sealed record AutomationSessionContext
{
    /// <summary>短命 pipe session id。</summary>
    public required string SessionId { get; init; }

    /// <summary>credential 派生 principal id。</summary>
    public required string PrincipalId { get; init; }

    /// <summary>外部进程在重连期间保持不变的 client instance id。</summary>
    public required string ClientInstanceId { get; init; }

    /// <summary>hello 声明的客户端名称。</summary>
    public required string ClientName { get; init; }

    /// <summary>实际授予 scopes。</summary>
    public required string[] GrantedScopes { get; init; }
}

/// <summary>
/// 认证后的请求上下文。
/// </summary>
public sealed class AutomationRequestContext
{
    private readonly FrozenSet<string> _grantedScopes;

    internal AutomationRequestContext(
        string requestId,
        string correlationId,
        string sessionId,
        string principalId,
        string clientInstanceId,
        string clientName,
        IEnumerable<string> grantedScopes,
        DateTimeOffset? deadlineUtc,
        AutomationRevisionPrecondition? expectedRevision,
        string? idempotencyKey,
        string? transactionId)
    {
        RequestId = requestId;
        CorrelationId = correlationId;
        SessionId = sessionId;
        PrincipalId = principalId;
        ClientInstanceId = clientInstanceId;
        ClientName = clientName;
        _grantedScopes = grantedScopes.ToFrozenSet(StringComparer.Ordinal);
        DeadlineUtc = deadlineUtc;
        ExpectedRevision = expectedRevision;
        IdempotencyKey = idempotencyKey;
        TransactionId = transactionId;
    }

    /// <summary>request message id。</summary>
    public string RequestId { get; }

    /// <summary>审计 correlation id。</summary>
    public string CorrelationId { get; }

    /// <summary>认证 session id。</summary>
    public string SessionId { get; }

    /// <summary>credential 派生 principal id。</summary>
    public string PrincipalId { get; }

    /// <summary>重连期间稳定的 client instance id。</summary>
    public string ClientInstanceId { get; }

    /// <summary>hello 声明的客户端名称。</summary>
    public string ClientName { get; }

    /// <summary>请求 deadline。</summary>
    public DateTimeOffset? DeadlineUtc { get; }

    /// <summary>optimistic concurrency 前置条件。</summary>
    public AutomationRevisionPrecondition? ExpectedRevision { get; }

    /// <summary>跨连接幂等 key。</summary>
    public string? IdempotencyKey { get; }

    /// <summary>可逆写操作要并入的 transaction id。</summary>
    public string? TransactionId { get; }

    /// <summary>
    /// 检查会话是否拥有 scope。
    /// </summary>
    /// <param name="scope">scope 名称。</param>
    /// <returns>是否已授予。</returns>
    public bool HasScope(string scope)
    {
        return _grantedScopes.Contains(scope);
    }

    internal AutomationRequestContext SnapshotForTransactionStaging()
    {
        AutomationRevisionPrecondition? expected = ExpectedRevision is null
            ? null
            : ExpectedRevision with
            {
                Resources =
                [
                    .. ExpectedRevision.Resources.Select(static resource => resource with { }),
                ],
            };
        return new AutomationRequestContext(
            RequestId,
            CorrelationId,
            SessionId,
            PrincipalId,
            ClientInstanceId,
            ClientName,
            _grantedScopes,
            null,
            expected,
            IdempotencyKey,
            TransactionId);
    }
}

/// <summary>
/// semantic handler 返回的 payload 与同一安全点 revision。
/// </summary>
public sealed record AutomationHandlerResult
{
    /// <summary>小型 JSON payload。</summary>
    public JsonElement? Payload { get; init; }

    /// <summary>
    /// safe phase 后需要在 Server 后台完成的 payload producer；普通 handler 应保持为空。
    /// </summary>
    public AutomationDeferredPayloadFactory? DeferredPayloadFactory { get; init; }

    /// <summary>执行后或 snapshot 对应的 revision。</summary>
    public AutomationRevisionSnapshot? Revision { get; init; }
}

/// <summary>
/// Editor Shell semantic adapter 实现的请求处理边界。
/// </summary>
public interface IAutomationRequestHandler
{
    /// <summary>
    /// 查询 method descriptor；不存在时返回 false。
    /// </summary>
    /// <param name="method">稳定 method id。</param>
    /// <param name="descriptor">成功时返回 descriptor。</param>
    /// <returns>method 是否存在。</returns>
    bool TryGetDescriptor(string method, out AutomationMethodDescriptor descriptor);

    /// <summary>
    /// 执行已通过认证、scope 与 deadline 前置校验的请求。
    /// </summary>
    /// <param name="context">请求上下文。</param>
    /// <param name="method">稳定 method id。</param>
    /// <param name="payload">请求 payload。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>响应 payload；无 payload 时为 null。</returns>
    ValueTask<AutomationHandlerResult> HandleAsync(
        AutomationRequestContext context,
        string method,
        JsonElement? payload,
        CancellationToken cancellationToken);
}

/// <summary>
/// 需要在认证成功和连接关闭时管理 transaction/subscription 的 handler 生命周期扩展。
/// </summary>
public interface IAutomationSessionLifecycleHandler
{
    /// <summary>认证成功后通知。</summary>
    /// <param name="session">session 身份。</param>
    /// <param name="eventSink">该连接的有界 event 输出端。</param>
    void OnSessionOpened(AutomationSessionContext session, IAutomationEventSink eventSink);

    /// <summary>连接关闭后通知；实现只能排队清理，不得在 I/O 线程访问 Editor 对象。</summary>
    /// <param name="session">已关闭 session。</param>
    void OnSessionClosed(AutomationSessionContext session);
}

/// <summary>
/// Server connection 提供给 semantic event hub 的非阻塞、有界输出端。
/// </summary>
public interface IAutomationEventSink
{
    /// <summary>
    /// 尝试按顺序排入一条事件；false 表示连接消费速度不足或已经关闭。
    /// </summary>
    /// <param name="eventRecord">不可变 event record。</param>
    /// <returns>是否成功进入连接队列。</returns>
    bool TryPublish(AutomationEventRecord eventRecord);

    /// <summary>立即中止连接，使客户端通过 resume/replay 恢复。</summary>
    void Abort();
}

/// <summary>
/// handler 用于返回稳定结构化错误的异常。
/// </summary>
public sealed class AutomationRequestException(AutomationError error)
    : Exception((error ?? throw new ArgumentNullException(nameof(error))).Message)
{
    /// <summary>结构化错误。</summary>
    public AutomationError Error { get; } = error;
}

internal sealed class EmptyAutomationRequestHandler : IAutomationRequestHandler
{
    public static EmptyAutomationRequestHandler Instance { get; } = new();

    public bool TryGetDescriptor(string method, out AutomationMethodDescriptor descriptor)
    {
        descriptor = null!;
        return false;
    }

    public ValueTask<AutomationHandlerResult> HandleAsync(
        AutomationRequestContext context,
        string method,
        JsonElement? payload,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("不存在可执行的 automation handler。");
    }
}
