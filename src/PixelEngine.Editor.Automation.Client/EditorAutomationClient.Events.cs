using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Client;

public sealed partial class EditorAutomationClient
{
    /// <summary>首次创建或在新连接上恢复 event subscription。</summary>
    /// <param name="state">稳定 key、filter、token 与最后 ack sequence。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前订阅与更新后的 resume state。</returns>
    public async ValueTask<AutomationEventSubscription> SubscribeOrResumeEventsAsync(
        AutomationEventResumeState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(state.SubscriptionKey) ||
            state.SubscriptionKey.Length > 128 ||
            state.SubscriptionKey.Any(char.IsControl) ||
            state.BacklogLimit is < 1 ||
            state.BacklogLimit > _options.MaxBufferedEvents ||
            state.AcknowledgedSequence < 0 ||
            (state.ResumeToken is null && state.AcknowledgedSequence.HasValue) ||
            (state.ResumeToken is { Length: > 4096 }))
        {
            throw new ArgumentException("Event resume state identity/token/sequence/backlog 无效。", nameof(state));
        }

        string[] eventTypes = NormalizeEventTypes(state.EventTypes);
        AutomationSubscriptionInfo info = await SubscribeEventsAsync(
            new AutomationEventSubscribeRequest
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                SubscriptionKey = state.SubscriptionKey,
                EventTypes = eventTypes,
                ResumeToken = state.ResumeToken,
                AfterSequence = state.AcknowledgedSequence,
                BacklogLimit = state.BacklogLimit,
            },
            cancellationToken).ConfigureAwait(false);
        return CreateEventSubscription(state, eventTypes, info);
    }

    /// <summary>确认连续处理进度并返回可用于下一次重连的更新状态。</summary>
    /// <param name="subscription">当前 subscription。</param>
    /// <param name="sequence">已完整处理的最大连续 sequence。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>ack 后 subscription/resume state。</returns>
    public async ValueTask<AutomationEventSubscription> AcknowledgeEventsAsync(
        AutomationEventSubscription subscription,
        long sequence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        AutomationSubscriptionInfo info = await AcknowledgeEventsAsync(
            new AutomationEventAckRequest
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                SubscriptionId = subscription.Info.SubscriptionId,
                Sequence = sequence,
            },
            cancellationToken).ConfigureAwait(false);
        return CreateEventSubscription(
            subscription.ResumeState,
            subscription.ResumeState.EventTypes,
            info);
    }

    private static AutomationEventSubscription CreateEventSubscription(
        AutomationEventResumeState source,
        string[] eventTypes,
        AutomationSubscriptionInfo info)
    {
        return new AutomationEventSubscription
        {
            Info = info,
            ResumeState = source with
            {
                EventTypes = [.. eventTypes],
                ResumeToken = info.ResumeToken,
                AcknowledgedSequence = info.AcknowledgedSequence,
            },
        };
    }

    private static string[] NormalizeEventTypes(string[] eventTypes)
    {
        ArgumentNullException.ThrowIfNull(eventTypes);
        return eventTypes.Length <= 64 && eventTypes.All(static value =>
            !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(char.IsControl))
            ? [.. eventTypes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]
            : throw new ArgumentException("Event type filter 无效或超过 64 条。", nameof(eventTypes));
    }
}
