using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixelEngine.Editor.Automation.Protocol;

/// <summary>
/// Automation work 可访问 Editor/Engine 权威状态的稳定 safe phase。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationExecutionPhase>))]
public enum AutomationExecutionPhase
{
    /// <summary>Editor 主循环在 authoring/UI command 之前的入口安全点。</summary>
    EditorIngress,

    /// <summary>Engine InputAndTime 相位。</summary>
    EngineInputAndTime,

    /// <summary>Engine GameLogicAndScripts 相位。</summary>
    EngineGameLogicAndScripts,

    /// <summary>Engine ResidencyApply 相位。</summary>
    EngineResidencyApply,

    /// <summary>Engine ParticleToCell 相位。</summary>
    EngineParticleToCell,

    /// <summary>Engine CaSimulation 相位。</summary>
    EngineCaSimulation,

    /// <summary>Engine Temperature 相位。</summary>
    EngineTemperature,

    /// <summary>Engine DirtyRectSwap 相位。</summary>
    EngineDirtyRectSwap,

    /// <summary>Engine CellToParticle 相位。</summary>
    EngineCellToParticle,

    /// <summary>Engine PhysicsSync 相位。</summary>
    EnginePhysicsSync,

    /// <summary>Engine BuildRenderBuffer 相位。</summary>
    EngineBuildRenderBuffer,

    /// <summary>Engine GpuUploadAndRender 相位。</summary>
    EngineGpuUploadAndRender,

    /// <summary>Engine WorldStreaming 相位。</summary>
    EngineWorldStreaming,

    /// <summary>不访问 Editor/Engine 权威对象的后台工作。</summary>
    Background,
}

/// <summary>
/// Capability 对权威状态的访问类型。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationOperationKind>))]
public enum AutomationOperationKind
{
    /// <summary>冻结并返回只读 snapshot。</summary>
    Read,

    /// <summary>修改带 revision 的权威状态。</summary>
    Write,

    /// <summary>启动 Play/Build/Run 等非事务工作流。</summary>
    Command,
}

/// <summary>
/// Capability 的事务参与策略。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationTransactionMode>))]
public enum AutomationTransactionMode
{
    /// <summary>禁止在 transaction 中执行。</summary>
    Forbidden,

    /// <summary>既可独立执行，也可并入 transaction。</summary>
    Optional,

    /// <summary>必须在 transaction 中执行。</summary>
    Required,
}

/// <summary>
/// Transaction 生命周期状态。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationTransactionStatus>))]
public enum AutomationTransactionStatus
{
    /// <summary>仍可接纳 reversible operation。</summary>
    Active,

    /// <summary>已合并为一个 Undo item。</summary>
    Committed,

    /// <summary>已恢复 before image。</summary>
    RolledBack,

    /// <summary>lease 到期并恢复 before image。</summary>
    Expired,
}

/// <summary>
/// Event subscription 的连接生命周期状态。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationEventSubscriptionStatus>))]
public enum AutomationEventSubscriptionStatus
{
    /// <summary>已绑定当前 session 并会接收 live event。</summary>
    Active,

    /// <summary>连接已断开，但仍在 replay retention 内。</summary>
    Disconnected,

    /// <summary>已由调用方显式删除。</summary>
    Removed,
}

/// <summary>
/// 一个资源的 optimistic concurrency 前置条件。
/// </summary>
public sealed record AutomationExpectedResourceRevision
{
    /// <summary>DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>稳定资源 id。</summary>
    public required string ResourceId { get; init; }

    /// <summary>调用方读取到的 revision。</summary>
    public required long Revision { get; init; }
}

/// <summary>
/// 写请求携带的 global/resource revision 前置条件。
/// </summary>
public sealed record AutomationRevisionPrecondition
{
    /// <summary>DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>可选 global revision。</summary>
    public long? GlobalRevision { get; init; }

    /// <summary>必须同时匹配的资源 revisions。</summary>
    public required AutomationExpectedResourceRevision[] Resources { get; init; }
}

/// <summary>
/// 响应或事件中的资源 revision。
/// </summary>
public sealed record AutomationResourceRevision
{
    /// <summary>DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>稳定资源 id。</summary>
    public required string ResourceId { get; init; }

    /// <summary>当前 revision。</summary>
    public required long Revision { get; init; }
}

/// <summary>
/// 一次不可变的 global/resource revision snapshot。
/// </summary>
public sealed record AutomationRevisionSnapshot
{
    /// <summary>DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>全局单调 revision。</summary>
    public required long GlobalRevision { get; init; }

    /// <summary>请求涉及资源的当前 revisions。</summary>
    public required AutomationResourceRevision[] Resources { get; init; }
}

/// <summary>
/// 一个未满足的资源 revision 前置条件。
/// </summary>
public sealed record AutomationResourceRevisionConflict
{
    /// <summary>DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>稳定资源 id。</summary>
    public required string ResourceId { get; init; }

    /// <summary>调用方期望值。</summary>
    public required long ExpectedRevision { get; init; }

    /// <summary>服务端当前值。</summary>
    public required long CurrentRevision { get; init; }
}

/// <summary>
/// revision_conflict 的 machine-readable details。
/// </summary>
public sealed record AutomationRevisionConflictDetails
{
    /// <summary>DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>请求期望的 global revision。</summary>
    public long? ExpectedGlobalRevision { get; init; }

    /// <summary>当前 global revision。</summary>
    public required long CurrentGlobalRevision { get; init; }

    /// <summary>所有不匹配资源。</summary>
    public required AutomationResourceRevisionConflict[] ResourceConflicts { get; init; }
}

/// <summary>
/// 原子制品文件引用；控制面只传引用，不内联大型数据。
/// </summary>
public sealed record AutomationArtifactReference
{
    /// <summary>DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>session 内稳定 artifact id。</summary>
    public required string ArtifactId { get; init; }

    /// <summary>制品 canonical absolute path。</summary>
    public required string Path { get; init; }

    /// <summary>session artifact root 下的相对路径。</summary>
    public required string RelativePath { get; init; }

    /// <summary>IANA media type。</summary>
    public required string MediaType { get; init; }

    /// <summary>文件字节数。</summary>
    public required long ByteLength { get; init; }

    /// <summary>64 位小写 SHA256。</summary>
    public required string Sha256 { get; init; }

    /// <summary>原子发布时间。</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>生成 snapshot 的 revision。</summary>
    public required AutomationRevisionSnapshot SourceRevision { get; init; }

    /// <summary>可选像素宽度。</summary>
    public int? Width { get; init; }

    /// <summary>可选像素高度。</summary>
    public int? Height { get; init; }

    /// <summary>可选编码名称，例如 png、utf-8、speedscope。</summary>
    public string? Encoding { get; init; }

    /// <summary>capability 特有的小型结构化元数据。</summary>
    public JsonElement? Metadata { get; init; }
}

/// <summary>
/// transaction.begin 请求。
/// </summary>
public sealed record AutomationTransactionBeginRequest
{
    /// <summary>DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>最终 Undo item 名称。</summary>
    public required string Name { get; init; }

    /// <summary>transaction lease 毫秒数。</summary>
    public required int LeaseMilliseconds { get; init; }
}

/// <summary>
/// commit/rollback/status 共用的 transaction id 请求。
/// </summary>
public sealed record AutomationTransactionRequest
{
    /// <summary>DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>transaction id。</summary>
    public required string TransactionId { get; init; }
}

/// <summary>
/// transaction 当前状态。
/// </summary>
public sealed record AutomationTransactionInfo
{
    /// <summary>DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>transaction id。</summary>
    public required string TransactionId { get; init; }

    /// <summary>所有者 session id。</summary>
    public required string SessionId { get; init; }

    /// <summary>Undo item 名称。</summary>
    public required string Name { get; init; }

    /// <summary>生命周期状态。</summary>
    public required AutomationTransactionStatus Status { get; init; }

    /// <summary>创建时间。</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>lease 截止时间。</summary>
    public required DateTimeOffset ExpiresAtUtc { get; init; }

    /// <summary>已接纳 operation 数量。</summary>
    public required int OperationCount { get; init; }

    /// <summary>transaction 已修改的确定排序 stable resource ids。</summary>
    public required string[] ResourceIds { get; init; }

    /// <summary>begin 时 revision。</summary>
    public required AutomationRevisionSnapshot BaseRevision { get; init; }
}

/// <summary>
/// transaction write 已被有界 staging 接纳、但尚未修改 Editor 状态的回执。
/// </summary>
public sealed record AutomationTransactionStagedOperationInfo
{
    /// <summary>DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>transaction 内稳定 operation id。</summary>
    public required string OperationId { get; init; }

    /// <summary>所属 transaction id。</summary>
    public required string TransactionId { get; init; }

    /// <summary>从 0 开始的提交顺序。</summary>
    public required int Ordinal { get; init; }

    /// <summary>真实 semantic method id。</summary>
    public required string Method { get; init; }

    /// <summary>Server 接纳 staging 的时间。</summary>
    public required DateTimeOffset AcceptedAtUtc { get; init; }
}

/// <summary>
/// transaction commit 中一个 staged operation 的真实执行结果。
/// </summary>
public sealed record AutomationTransactionOperationResult
{
    /// <summary>DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>staging 回执中的 operation id。</summary>
    public required string OperationId { get; init; }

    /// <summary>原始 staging request id，用于因果追踪。</summary>
    public required string RequestId { get; init; }

    /// <summary>真实 semantic method id。</summary>
    public required string Method { get; init; }

    /// <summary>operation 的小型结果 payload。</summary>
    public JsonElement? Payload { get; init; }

    /// <summary>该 operation 实际修改的稳定 resource ids。</summary>
    public required string[] ResourceIds { get; init; }
}

/// <summary>
/// transaction 原子 commit 的合并结果。
/// </summary>
public sealed record AutomationTransactionCommitResult
{
    /// <summary>DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>已完成 transaction 信息。</summary>
    public required AutomationTransactionInfo Transaction { get; init; }

    /// <summary>按 staging ordinal 排列的真实 operation 结果。</summary>
    public required AutomationTransactionOperationResult[] Operations { get; init; }
}

/// <summary>
/// transaction commit 失败并执行 fail-closed rollback 的结构化细节。
/// </summary>
public sealed record AutomationTransactionFailureDetails
{
    /// <summary>DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>失败 transaction id。</summary>
    public required string TransactionId { get; init; }

    /// <summary>失败 staged operation id；预校验失败时可为空。</summary>
    public string? OperationId { get; init; }

    /// <summary>失败 operation ordinal；预校验失败时可为空。</summary>
    public int? Ordinal { get; init; }

    /// <summary>失败 semantic method；预校验失败时可为空。</summary>
    public string? Method { get; init; }

    /// <summary>before image 是否已完整恢复。</summary>
    public required bool RollbackSucceeded { get; init; }

    /// <summary>触发 commit 失败的原始结构化错误。</summary>
    public required AutomationError Cause { get; init; }
}

/// <summary>
/// Server 推送的可 replay 事件。
/// </summary>
public sealed record AutomationEventRecord
{
    /// <summary>DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>subscription id。</summary>
    public required string SubscriptionId { get; init; }

    /// <summary>subscription 内单调 sequence。</summary>
    public required long Sequence { get; init; }

    /// <summary>稳定 event type。</summary>
    public required string EventType { get; init; }

    /// <summary>事件对应的权威 revision。</summary>
    public required AutomationRevisionSnapshot StateRevision { get; init; }

    /// <summary>产生事件的 request id；人工操作或后台事件可为空。</summary>
    public string? CausationRequestId { get; init; }

    /// <summary>事件发布时间。</summary>
    public required DateTimeOffset PublishedAtUtc { get; init; }

    /// <summary>event type 对应的小型 payload。</summary>
    public JsonElement? Payload { get; init; }
}

/// <summary>
/// event.subscribe 请求，亦用于携带 resume token 恢复旧订阅。
/// </summary>
public sealed record AutomationEventSubscribeRequest
{
    /// <summary>DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>客户端生成、跨请求重试稳定的订阅 key。</summary>
    public required string SubscriptionKey { get; init; }

    /// <summary>确定排序且去重的 event type filter；空数组表示全部。</summary>
    public required string[] EventTypes { get; init; }

    /// <summary>断线前得到的 opaque resume token。</summary>
    public string? ResumeToken { get; init; }

    /// <summary>客户端最后完整处理的 sequence。</summary>
    public long? AfterSequence { get; init; }

    /// <summary>该订阅允许积压的最大未 ack 事件数。</summary>
    public required int BacklogLimit { get; init; }
}

/// <summary>
/// 新建或恢复后的 subscription 信息。
/// </summary>
public sealed record AutomationSubscriptionInfo
{
    /// <summary>DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>subscription id。</summary>
    public required string SubscriptionId { get; init; }

    /// <summary>opaque resume token。</summary>
    public required string ResumeToken { get; init; }

    /// <summary>订阅当前生命周期状态。</summary>
    public required AutomationEventSubscriptionStatus Status { get; init; }

    /// <summary>下一条 live event 的 sequence。</summary>
    public required long NextSequence { get; init; }

    /// <summary>服务端已接受的最大连续 ack sequence。</summary>
    public required long AcknowledgedSequence { get; init; }

    /// <summary>当前保留、尚未 ack 的 replay event 数。</summary>
    public required int BacklogCount { get; init; }

    /// <summary>本次响应开始 replay 的 sequence；无 replay 时为空。</summary>
    public long? ReplayFromSequence { get; init; }

    /// <summary>断线后保留订阅用于 resume 的截止时间。</summary>
    public required DateTimeOffset ResumeExpiresAtUtc { get; init; }
}

/// <summary>
/// event.ack 请求。
/// </summary>
public sealed record AutomationEventAckRequest
{
    /// <summary>DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>subscription id。</summary>
    public required string SubscriptionId { get; init; }

    /// <summary>客户端已完整处理的最大连续 sequence。</summary>
    public required long Sequence { get; init; }
}

/// <summary>
/// event.unsubscribe 请求。
/// </summary>
public sealed record AutomationEventSubscriptionRequest
{
    /// <summary>DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>要删除的 subscription id。</summary>
    public required string SubscriptionId { get; init; }
}

/// <summary>
/// event_overflow/resync_required 的机器可读恢复细节。
/// </summary>
public sealed record AutomationEventResyncDetails
{
    /// <summary>DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>可识别时的 subscription id。</summary>
    public string? SubscriptionId { get; init; }

    /// <summary>最后成功 ack 的 sequence。</summary>
    public long? AcknowledgedSequence { get; init; }

    /// <summary>第一条无法保留的 sequence。</summary>
    public long? LostSequence { get; init; }

    /// <summary>稳定原因标识。</summary>
    public required string Reason { get; init; }
}

/// <summary>
/// editor.state.changed / editor.transaction.changed 的小型语义 payload。
/// </summary>
public sealed record AutomationStateChangedEvent
{
    /// <summary>DTO schema version。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>产生变化的 capability/method。</summary>
    public required string Method { get; init; }

    /// <summary>确定排序的受影响 stable resource ids。</summary>
    public required string[] ResourceIds { get; init; }

    /// <summary>execute、commit、rollback、expired、undo 或 redo。</summary>
    public required string ChangeKind { get; init; }

    /// <summary>相关 transaction id。</summary>
    public string? TransactionId { get; init; }
}
