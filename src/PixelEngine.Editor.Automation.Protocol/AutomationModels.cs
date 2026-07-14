using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixelEngine.Editor.Automation.Protocol;

/// <summary>
/// 协议版本；major 表示不兼容边界，minor 表示可协商扩展。
/// </summary>
/// <param name="Major">主版本。</param>
/// <param name="Minor">次版本。</param>
public sealed record AutomationProtocolVersion(int Major, int Minor);

/// <summary>
/// wire message 类型。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationMessageKind>))]
public enum AutomationMessageKind
{
    /// <summary>请求。</summary>
    Request,

    /// <summary>响应。</summary>
    Response,

    /// <summary>服务端事件。</summary>
    Event,

    /// <summary>显式取消。</summary>
    Cancel,
}

/// <summary>
/// 本地 IPC transport 类型。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationTransportKind>))]
public enum AutomationTransportKind
{
    /// <summary>Windows Named Pipe。</summary>
    WindowsNamedPipe,

    /// <summary>为非 Windows 平台保留的 Unix Domain Socket wire 值。</summary>
    UnixDomainSocket,
}

/// <summary>
/// 结构化错误分类。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationErrorCategory>))]
public enum AutomationErrorCategory
{
    /// <summary>协议或输入错误。</summary>
    Validation,

    /// <summary>认证错误。</summary>
    Authentication,

    /// <summary>授权错误。</summary>
    Authorization,

    /// <summary>超时或取消。</summary>
    Cancellation,

    /// <summary>资源或并发冲突。</summary>
    Conflict,

    /// <summary>当前状态下不可用。</summary>
    Availability,

    /// <summary>服务端意外失败。</summary>
    Internal,
}

/// <summary>
/// request、response、event 和 cancel 共用的 wire envelope。
/// </summary>
public sealed record AutomationEnvelope
{
    /// <summary>envelope DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>协商后的协议版本。</summary>
    public required AutomationProtocolVersion Protocol { get; init; }

    /// <summary>消息唯一 id。</summary>
    public required string MessageId { get; init; }

    /// <summary>消息类型。</summary>
    public required AutomationMessageKind Kind { get; init; }

    /// <summary>响应或事件对应的原始 request id。</summary>
    public string? CorrelationId { get; init; }

    /// <summary>稳定 method/capability id。</summary>
    public string? Method { get; init; }

    /// <summary>认证成功后的 session id。</summary>
    public string? SessionId { get; init; }

    /// <summary>服务端开始执行前必须检查的绝对 deadline。</summary>
    public DateTimeOffset? DeadlineUtc { get; init; }

    /// <summary>写请求的 global/resource optimistic concurrency 前置条件。</summary>
    public AutomationRevisionPrecondition? ExpectedRevision { get; init; }

    /// <summary>响应或事件观察到的权威 revision。</summary>
    public AutomationRevisionSnapshot? Revision { get; init; }

    /// <summary>跨连接重试仍稳定的幂等 key。</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>可逆写操作要并入的 transaction id。</summary>
    public string? TransactionId { get; init; }

    /// <summary>方法对应的 JSON payload。</summary>
    public JsonElement? Payload { get; init; }

    /// <summary>失败响应的结构化错误。</summary>
    public AutomationError? Error { get; init; }
}

/// <summary>
/// 协议结构化错误。
/// </summary>
public sealed record AutomationError
{
    /// <summary>error DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>稳定机器错误码。</summary>
    public required string Code { get; init; }

    /// <summary>错误分类。</summary>
    public required AutomationErrorCategory Category { get; init; }

    /// <summary>面向开发者的诊断文本。</summary>
    public required string Message { get; init; }

    /// <summary>可选结构化细节。</summary>
    public JsonElement? Details { get; init; }

    /// <summary>相同请求是否可重试。</summary>
    public bool Transient { get; init; }

    /// <summary>建议重试延迟。</summary>
    public int? RetryAfterMilliseconds { get; init; }

    /// <summary>冲突发生时的当前权威 revision。</summary>
    public long? CurrentRevision { get; init; }

    /// <summary>服务端 correlation id。</summary>
    public string? CorrelationId { get; init; }
}

/// <summary>
/// 初始 hello 请求。
/// </summary>
public sealed record AutomationHelloRequest
{
    /// <summary>hello request DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>同一外部进程在重连期间保持不变的稳定客户端实例 id。</summary>
    public required string ClientInstanceId { get; init; }

    /// <summary>客户端名称。</summary>
    public required string ClientName { get; init; }

    /// <summary>客户端版本。</summary>
    public required string ClientVersion { get; init; }

    /// <summary>客户端按偏好顺序支持的协议版本。</summary>
    public required AutomationProtocolVersion[] SupportedVersions { get; init; }

    /// <summary>客户端随机 nonce。</summary>
    public required string ClientNonce { get; init; }

    /// <summary>客户端请求的 permission scopes。</summary>
    public required string[] RequestedScopes { get; init; }
}

/// <summary>
/// 服务端 hello challenge。
/// </summary>
public sealed record AutomationHelloChallenge
{
    /// <summary>hello challenge DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>实例 id。</summary>
    public required string InstanceId { get; init; }

    /// <summary>协商得到的协议版本。</summary>
    public required AutomationProtocolVersion SelectedVersion { get; init; }

    /// <summary>服务端随机 nonce。</summary>
    public required string ServerNonce { get; init; }

    /// <summary>角色域分离的 base64 Server HMAC proof，证明实例持有 discovery credential。</summary>
    public required string ServerProof { get; init; }

    /// <summary>服务端可授予的 scopes。</summary>
    public required string[] SupportedScopes { get; init; }

    /// <summary>认证算法。</summary>
    public required string AuthenticationAlgorithm { get; init; }

    /// <summary>服务端最大 frame payload。</summary>
    public required int MaxFrameBytes { get; init; }
}

/// <summary>
/// challenge/HMAC 认证请求。
/// </summary>
public sealed record AutomationAuthenticateRequest
{
    /// <summary>authenticate request DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>hello 中的客户端 nonce。</summary>
    public required string ClientNonce { get; init; }

    /// <summary>challenge 中的服务端 nonce。</summary>
    public required string ServerNonce { get; init; }

    /// <summary>base64 HMAC-SHA256 proof。</summary>
    public required string Proof { get; init; }

    /// <summary>最终请求的 scopes。</summary>
    public required string[] RequestedScopes { get; init; }
}

/// <summary>
/// 认证成功后的会话信息。
/// </summary>
public sealed record AutomationSessionInfo
{
    /// <summary>session info DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>会话 id。</summary>
    public required string SessionId { get; init; }

    /// <summary>由 credential 派生、不可逆且不泄漏 secret 的稳定 principal id。</summary>
    public required string PrincipalId { get; init; }

    /// <summary>实际授予的 scopes。</summary>
    public required string[] GrantedScopes { get; init; }

    /// <summary>Editor/Server 产品版本。</summary>
    public required string ServerVersion { get; init; }

    /// <summary>协商后的协议版本。</summary>
    public required AutomationProtocolVersion Protocol { get; init; }
}

/// <summary>
/// 显式取消请求。
/// </summary>
public sealed record AutomationCancelRequest
{
    /// <summary>cancel request DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>要取消的 request message id。</summary>
    public required string TargetRequestId { get; init; }
}

/// <summary>
/// 实例 transport endpoint。
/// </summary>
public sealed record AutomationEndpointDescriptor
{
    /// <summary>endpoint DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>transport 类型。</summary>
    public required AutomationTransportKind Kind { get; init; }

    /// <summary>pipe name 或 socket path。</summary>
    public required string Address { get; init; }
}

/// <summary>
/// discovery 中公开的项目摘要。
/// </summary>
public sealed record AutomationProjectSummary
{
    /// <summary>project summary DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>项目 stable id。</summary>
    public required string ProjectId { get; init; }

    /// <summary>项目显示名称。</summary>
    public required string Name { get; init; }

    /// <summary>项目根的 canonical path。</summary>
    public required string RootPath { get; init; }

    /// <summary>当前场景 stable id；无场景时为空。</summary>
    public string? SceneId { get; init; }
}

/// <summary>
/// 原子写入 discovery root 的 Editor 实例 descriptor。
/// </summary>
public sealed record AutomationInstanceDescriptor
{
    /// <summary>descriptor schema。</summary>
    public required string Schema { get; init; }

    /// <summary>本次进程不可复用的 instance id。</summary>
    public required string InstanceId { get; init; }

    /// <summary>Editor 进程 id。</summary>
    public required int ProcessId { get; init; }

    /// <summary>用于拒绝 PID reuse 的进程启动时间。</summary>
    public required DateTimeOffset ProcessStartUtc { get; init; }

    /// <summary>descriptor 原子发布时间。</summary>
    public required DateTimeOffset PublishedAtUtc { get; init; }

    /// <summary>Editor/Server 产品版本。</summary>
    public required string EditorVersion { get; init; }

    /// <summary>服务端支持的协议版本。</summary>
    public required AutomationProtocolVersion[] ProtocolVersions { get; init; }

    /// <summary>真实可连接 endpoint；不得登记空壳 transport。</summary>
    public required AutomationEndpointDescriptor Endpoint { get; init; }

    /// <summary>current-user credential 文件 canonical path。</summary>
    public required string CredentialPath { get; init; }

    /// <summary>能力矩阵 SHA256；能力尚未装配时使用协议层固定 digest。</summary>
    public required string CapabilityDigest { get; init; }

    /// <summary>liveness 验证方式；v1 固定为 processIdentity。</summary>
    public required string LivenessMode { get; init; }

    /// <summary>当前项目摘要。</summary>
    public AutomationProjectSummary? Project { get; init; }
}

/// <summary>
/// ping 响应。
/// </summary>
public sealed record AutomationPingResponse
{
    /// <summary>ping response DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>服务端 UTC 时间。</summary>
    public required DateTimeOffset ServerTimeUtc { get; init; }

    /// <summary>实例 id。</summary>
    public required string InstanceId { get; init; }
}
