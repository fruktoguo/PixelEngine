using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Client;

/// <summary>
/// Server 返回结构化错误时抛出的异常。
/// </summary>
/// <param name="error">远端结构化错误。</param>
public sealed class AutomationRemoteException(AutomationError error)
    : Exception((error ?? throw new ArgumentNullException(nameof(error))).Message)
{
    /// <summary>远端结构化错误。</summary>
    public AutomationError Error { get; } = error;
}

/// <summary>
/// transport 断开或 reader 失败时抛出的异常。
/// </summary>
/// <param name="message">诊断文本。</param>
/// <param name="innerException">内部异常。</param>
public sealed class AutomationConnectionException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// 客户端 deadline 到达且未收到 Server 终态响应时抛出的异常。
/// </summary>
/// <param name="requestId">超时 request id。</param>
/// <param name="timeout">实际 timeout。</param>
public sealed class AutomationRequestTimeoutException(string requestId, TimeSpan timeout)
    : TimeoutException($"Automation request '{requestId}' 在 {timeout} 内未完成。")
{
    /// <summary>request id。</summary>
    public string RequestId { get; } = requestId;

    /// <summary>实际 timeout。</summary>
    public TimeSpan Timeout { get; } = timeout;
}
