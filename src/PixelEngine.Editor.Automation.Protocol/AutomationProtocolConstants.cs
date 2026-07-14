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

    /// <summary>单个 discovery descriptor 的最大字节数。</summary>
    public const int MaxDiscoveryDescriptorBytes = 64 * 1024;

    /// <summary>base64 credential 文件的最大字节数。</summary>
    public const int MaxCredentialFileBytes = 4096;

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
}
