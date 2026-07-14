namespace PixelEngine.Editor.Automation.Protocol;

/// <summary>
/// Editor automation wire protocol 的稳定常量。
/// </summary>
public static class AutomationProtocolConstants
{
    /// <summary>当前协议 major 版本。</summary>
    public const int CurrentMajor = 1;

    /// <summary>当前协议 minor 版本。</summary>
    public const int CurrentMinor = 0;

    /// <summary>v1 wire DTO 的显式 schema version。</summary>
    public const int WireSchemaVersion = 1;

    /// <summary>frame header 版本。</summary>
    public const ushort FrameHeaderVersion = 1;

    /// <summary>frame header 字节数。</summary>
    public const int FrameHeaderSize = 16;

    /// <summary>默认控制面最大 payload 字节数。</summary>
    public const int DefaultMaxFrameBytes = 1024 * 1024;

    /// <summary>
    /// v1 控制面允许配置的绝对 frame 上限。更大的截图、快照与构建数据必须写入 artifact，
    /// 防止错误配置或恶意对端诱导单帧巨量分配。
    /// </summary>
    public const int AbsoluteMaxFrameBytes = 16 * 1024 * 1024;

    /// <summary>单个 discovery descriptor 的最大字节数。</summary>
    public const int MaxDiscoveryDescriptorBytes = 64 * 1024;

    /// <summary>base64 credential 文件的最大字节数。</summary>
    public const int MaxCredentialFileBytes = 4096;

    /// <summary>单个 revision snapshot/precondition 最多包含的资源数。</summary>
    public const int MaxRevisionResources = 4096;

    /// <summary>稳定 resource id 的最大字符数。</summary>
    public const int MaxResourceIdLength = 256;

    /// <summary>单个 event subscription 最多声明的 event type filter 数。</summary>
    public const int MaxEventFilterTypes = 256;

    /// <summary>实例 descriptor schema id。</summary>
    public const string InstanceDescriptorSchema = "pixelengine.editor-automation-instance/v1";

    /// <summary>HMAC 算法标识。</summary>
    public const string AuthenticationAlgorithm = "hmac-sha256";

    /// <summary>初始版本协商方法。</summary>
    public const string HelloMethod = "system.hello";

    /// <summary>会话认证方法。</summary>
    public const string AuthenticateMethod = "system.authenticate";

    /// <summary>请求取消方法。</summary>
    public const string CancelMethod = "system.cancel";

    /// <summary>连通性探测方法。</summary>
    public const string PingMethod = "system.ping";

    /// <summary>实例描述读取方法。</summary>
    public const string DescribeMethod = "system.describe";

    /// <summary>开始可逆 transaction。</summary>
    public const string TransactionBeginMethod = "transaction.begin";

    /// <summary>提交 transaction 并合并为一个 Undo item。</summary>
    public const string TransactionCommitMethod = "transaction.commit";

    /// <summary>回滚 transaction。</summary>
    public const string TransactionRollbackMethod = "transaction.rollback";

    /// <summary>读取 transaction 状态。</summary>
    public const string TransactionStatusMethod = "transaction.status";

    /// <summary>创建或恢复 event subscription。</summary>
    public const string EventSubscribeMethod = "event.subscribe";

    /// <summary>确认事件 sequence。</summary>
    public const string EventAckMethod = "event.ack";

    /// <summary>删除 event subscription。</summary>
    public const string EventUnsubscribeMethod = "event.unsubscribe";

    /// <summary>Server→Client 的 event envelope method。</summary>
    public const string EventNotificationMethod = "event.notification";

    /// <summary>任意已提交权威状态写入后的通用事件类型。</summary>
    public const string StateChangedEventType = "editor.state.changed";

    /// <summary>transaction commit/rollback/expiry 事件类型。</summary>
    public const string TransactionChangedEventType = "editor.transaction.changed";

    /// <summary>当前协议版本。</summary>
    public static AutomationProtocolVersion CurrentVersion { get; } = new(CurrentMajor, CurrentMinor);
}

/// <summary>
/// 稳定结构化错误码。
/// </summary>
public static class AutomationErrorCodes
{
    /// <summary>请求格式或字段无效。</summary>
    public const string InvalidRequest = "invalid_request";

    /// <summary>frame 或协议无效。</summary>
    public const string InvalidProtocol = "invalid_protocol";

    /// <summary>没有共同协议版本。</summary>
    public const string ProtocolVersionUnsupported = "protocol_version_unsupported";

    /// <summary>会话尚未认证。</summary>
    public const string AuthenticationRequired = "authentication_required";

    /// <summary>认证 proof 无效。</summary>
    public const string AuthenticationFailed = "authentication_failed";

    /// <summary>scope 不允许该操作。</summary>
    public const string PermissionDenied = "permission_denied";

    /// <summary>请求 deadline 已过。</summary>
    public const string DeadlineExceeded = "deadline_exceeded";

    /// <summary>请求已被取消。</summary>
    public const string Cancelled = "cancelled";

    /// <summary>请求队列或并发额度已满。</summary>
    public const string Busy = "busy";

    /// <summary>方法不存在。</summary>
    public const string MethodNotFound = "method_not_found";

    /// <summary>服务端执行失败。</summary>
    public const string Internal = "internal_error";

    /// <summary>optimistic concurrency 前置 revision 已过期。</summary>
    public const string RevisionConflict = "revision_conflict";

    /// <summary>幂等 key 被不同请求复用。</summary>
    public const string IdempotencyConflict = "idempotency_conflict";

    /// <summary>transaction 不存在、不属于当前 session 或已结束。</summary>
    public const string TransactionInvalid = "transaction_invalid";

    /// <summary>另一个 transaction 正持有互斥写租约。</summary>
    public const string TransactionConflict = "transaction_conflict";

    /// <summary>transaction commit 的预校验或某个 staged operation 失败且已回滚。</summary>
    public const string TransactionFailed = "transaction_failed";

    /// <summary>transaction commit 失败后无法完整恢复 before image。</summary>
    public const string TransactionRollbackFailed = "transaction_rollback_failed";

    /// <summary>event replay window 已淘汰所需 sequence。</summary>
    public const string ResyncRequired = "resync_required";

    /// <summary>慢消费者超过订阅 backlog。</summary>
    public const string EventOverflow = "event_overflow";

    /// <summary>artifact session 或单文件配额不足。</summary>
    public const string ArtifactQuotaExceeded = "artifact_quota_exceeded";

    /// <summary>canonical path 越过获准 root 或包含链接逃逸。</summary>
    public const string PathNotAllowed = "path_not_allowed";

    /// <summary>请求在当前 Editor/项目/Play 状态不可执行。</summary>
    public const string StateUnavailable = "state_unavailable";

    /// <summary>semantic handler 返回了本应写入 artifact 的超限控制面响应。</summary>
    public const string ResponseTooLarge = "response_too_large";
}
