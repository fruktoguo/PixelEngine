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
        string clientName,
        IEnumerable<string> grantedScopes,
        DateTimeOffset? deadlineUtc)
    {
        RequestId = requestId;
        CorrelationId = correlationId;
        SessionId = sessionId;
        ClientName = clientName;
        _grantedScopes = grantedScopes.ToFrozenSet(StringComparer.Ordinal);
        DeadlineUtc = deadlineUtc;
    }

    /// <summary>request message id。</summary>
    public string RequestId { get; }

    /// <summary>审计 correlation id。</summary>
    public string CorrelationId { get; }

    /// <summary>认证 session id。</summary>
    public string SessionId { get; }

    /// <summary>hello 声明的客户端名称。</summary>
    public string ClientName { get; }

    /// <summary>请求 deadline。</summary>
    public DateTimeOffset? DeadlineUtc { get; }

    /// <summary>
    /// 检查会话是否拥有 scope。
    /// </summary>
    /// <param name="scope">scope 名称。</param>
    /// <returns>是否已授予。</returns>
    public bool HasScope(string scope)
    {
        return _grantedScopes.Contains(scope);
    }
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
    ValueTask<JsonElement?> HandleAsync(
        AutomationRequestContext context,
        string method,
        JsonElement? payload,
        CancellationToken cancellationToken);
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

    public ValueTask<JsonElement?> HandleAsync(
        AutomationRequestContext context,
        string method,
        JsonElement? payload,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("不存在可执行的 automation handler。");
    }
}
