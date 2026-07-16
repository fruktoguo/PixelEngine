using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Client;

/// <summary>可跨 Client 重连持久化的 event subscription resume state。</summary>
public sealed record AutomationEventResumeState
{
    /// <summary>客户端生成并跨重连保持的稳定 key。</summary>
    public required string SubscriptionKey { get; init; }

    /// <summary>确定排序且去重的 event type filter。</summary>
    public required string[] EventTypes { get; init; }

    /// <summary>服务端 opaque resume token；首次订阅为空。</summary>
    public string? ResumeToken { get; init; }

    /// <summary>最后完整处理并 ack 的 sequence；首次订阅为空。</summary>
    public long? AcknowledgedSequence { get; init; }

    /// <summary>服务端未 ack backlog 上限。</summary>
    public int BacklogLimit { get; init; } = 1024;
}

/// <summary>当前 subscription info 与下一次重连所需 resume state。</summary>
public sealed record AutomationEventSubscription
{
    /// <summary>服务端订阅状态。</summary>
    public required AutomationSubscriptionInfo Info { get; init; }

    /// <summary>已更新 token/sequence 的可持久化状态。</summary>
    public required AutomationEventResumeState ResumeState { get; init; }
}
