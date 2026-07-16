using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Client;

/// <summary>
/// 单次 automation 调用的 timeout、revision、幂等与 transaction 选项。
/// </summary>
public sealed record AutomationInvocationOptions
{
    /// <summary>覆盖 Client 默认 timeout。</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>写请求的 optimistic concurrency 前置条件。</summary>
    public AutomationRevisionPrecondition? ExpectedRevision { get; init; }

    /// <summary>跨连接重试保持不变的幂等 key。</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>可逆写操作要并入的 transaction id。</summary>
    public string? TransactionId { get; init; }
}

/// <summary>
/// raw payload 及其同一安全点 revision。
/// </summary>
public sealed record AutomationInvocationResult
{
    /// <summary>method 返回的小型 payload。</summary>
    public JsonElement? Payload { get; init; }

    /// <summary>执行后或 snapshot 对应的权威 revision。</summary>
    public AutomationRevisionSnapshot? Revision { get; init; }
}

/// <summary>强类型 response 及其同一安全点 revision。</summary>
/// <typeparam name="TResponse">公开 Protocol response DTO。</typeparam>
public sealed record AutomationTypedInvocationResult<TResponse>
{
    /// <summary>已通过 source-generated metadata 反序列化的响应。</summary>
    public required TResponse Response { get; init; }

    /// <summary>执行后或 snapshot 对应的权威 revision。</summary>
    public AutomationRevisionSnapshot? Revision { get; init; }
}
