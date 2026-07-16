using System.Text.Json;
using PixelEngine.Editor.Automation.Client;
using PixelEngine.Editor.Automation.Protocol;
using PixelEngine.Editor.Automation.Server;

namespace PixelEngine.Editor.Automation.Tests;

/// <summary>
/// Event filter、ack、bounded replay、overflow 与真实 pipe 推送测试。
/// </summary>
public sealed class AutomationEventHubTests
{
    /// <summary>验证过滤、ack 释放 backlog、跨 session resume/replay 与 unsubscribe。</summary>
    [Fact]
    public void SubscriptionFiltersAcknowledgesAndReplaysAcrossSession()
    {
        using AutomationEventHub hub = new();
        TestEventSink firstSink = new(capacity: 8);
        hub.OpenSession(CreateSession("session-1", "client-1"), firstSink);
        AutomationEventSubscribeRequest request = SubscribeRequest("scene-events", ["scene.changed"], 4);
        AutomationSubscriptionInfo subscription = hub.Subscribe("session-1", request);

        Assert.Equal(0, hub.Publish("console.entry", Revision(1)));
        Assert.Equal(1, hub.Publish(
            "scene.changed",
            Revision(2, ("scene:main", 1)),
            "request-1",
            JsonSerializer.SerializeToElement(new { reason = "create" })));
        AutomationEventRecord first = Assert.Single(firstSink.Events);
        Assert.Equal(1, first.Sequence);
        Assert.Equal("create", first.Payload?.GetProperty("reason").GetString());

        AutomationSubscriptionInfo acknowledged = hub.Acknowledge("session-1", new AutomationEventAckRequest
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            SubscriptionId = subscription.SubscriptionId,
            Sequence = first.Sequence,
        });
        Assert.Equal(1, acknowledged.AcknowledgedSequence);
        Assert.Equal(0, acknowledged.BacklogCount);

        hub.CloseSession("session-1");
        Assert.Equal(1, hub.Publish("scene.changed", Revision(3, ("scene:main", 2))));
        TestEventSink secondSink = new(capacity: 8);
        hub.OpenSession(CreateSession("session-2", "client-1"), secondSink);
        AutomationSubscriptionInfo resumed = hub.Subscribe("session-2", request with
        {
            ResumeToken = subscription.ResumeToken,
            AfterSequence = 1,
        });

        AutomationEventRecord replay = Assert.Single(secondSink.Events);
        Assert.Equal(2, replay.Sequence);
        Assert.Equal(2, resumed.ReplayFromSequence);
        Assert.Equal(3, resumed.NextSequence);
        Assert.Equal(1, resumed.BacklogCount);
        AutomationSubscriptionInfo removed = hub.Unsubscribe(
            "session-2",
            new AutomationEventSubscriptionRequest
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                SubscriptionId = subscription.SubscriptionId,
            });
        Assert.Equal(AutomationEventSubscriptionStatus.Removed, removed.Status);
        Assert.Equal(0, hub.Publish("scene.changed", Revision(4)));
    }

    /// <summary>验证慢消费者超过 backlog 后断开，resume 返回带 lost sequence 的结构化错误。</summary>
    [Fact]
    public void BacklogOverflowAbortsConnectionAndRequiresAuthoritativeResync()
    {
        using AutomationEventHub hub = new();
        TestEventSink firstSink = new(capacity: 8);
        hub.OpenSession(CreateSession("session-1", "client-1"), firstSink);
        AutomationEventSubscribeRequest request = SubscribeRequest("all-events", [], 2);
        AutomationSubscriptionInfo subscription = hub.Subscribe("session-1", request);

        Assert.Equal(1, hub.Publish("scene.changed", Revision(1)));
        Assert.Equal(1, hub.Publish("scene.changed", Revision(2)));
        Assert.Equal(0, hub.Publish("scene.changed", Revision(3)));
        Assert.True(firstSink.Aborted);

        hub.CloseSession("session-1");
        hub.OpenSession(CreateSession("session-2", "client-1"), new TestEventSink(capacity: 8));
        AutomationRequestException exception = Assert.Throws<AutomationRequestException>(() =>
            hub.Subscribe("session-2", request with
            {
                ResumeToken = subscription.ResumeToken,
                AfterSequence = 0,
            }));

        Assert.Equal(AutomationErrorCodes.EventOverflow, exception.Error.Code);
        AutomationEventResyncDetails details = exception.Error.Details?.Deserialize(
            AutomationJsonContext.Default.AutomationEventResyncDetails)
            ?? throw new InvalidOperationException("event_overflow 缺少 details。");
        Assert.Equal(subscription.SubscriptionId, details.SubscriptionId);
        Assert.Equal(3, details.LostSequence);
        Assert.Equal("backlog_overflow", details.Reason);
    }

    /// <summary>验证 resume token 不能跨 authenticated client instance 使用。</summary>
    [Fact]
    public void ResumeTokenIsBoundToPrincipalClientAndSubscriptionKey()
    {
        using AutomationEventHub hub = new();
        hub.OpenSession(CreateSession("session-1", "client-1"), new TestEventSink(capacity: 8));
        AutomationEventSubscribeRequest request = SubscribeRequest("secure-events", [], 4);
        AutomationSubscriptionInfo subscription = hub.Subscribe("session-1", request);
        hub.CloseSession("session-1");
        hub.OpenSession(CreateSession("session-2", "client-2"), new TestEventSink(capacity: 8));

        AutomationRequestException exception = Assert.Throws<AutomationRequestException>(() =>
            hub.Subscribe("session-2", request with { ResumeToken = subscription.ResumeToken }));

        Assert.Equal(AutomationErrorCodes.PermissionDenied, exception.Error.Code);
    }

    /// <summary>验证多个订阅共享全局 record 配额，不能把 per-subscription 上限相乘为无界内存。</summary>
    [Fact]
    public void GlobalReplayQuotaFailsClosedAcrossSubscriptions()
    {
        using AutomationEventHub hub = new(new AutomationEventHubOptions
        {
            MaxBufferedEvents = 1,
            MaxBufferedBytes = 1024,
        });
        TestEventSink firstSink = new(capacity: 8);
        TestEventSink secondSink = new(capacity: 8);
        hub.OpenSession(CreateSession("session-1", "client-1"), firstSink);
        hub.OpenSession(CreateSession("session-2", "client-2"), secondSink);
        _ = hub.Subscribe("session-1", SubscribeRequest("first", [], 8));
        _ = hub.Subscribe("session-2", SubscribeRequest("second", [], 8));

        Assert.Equal(1, hub.Publish("scene.changed", Revision(1)));
        Assert.Equal(1, firstSink.Events.Count + secondSink.Events.Count);
        Assert.NotEqual(firstSink.Aborted, secondSink.Aborted);
    }

    /// <summary>验证空 payload 也不能用巨大 revision snapshot 绕过全局 replay 字节配额。</summary>
    [Fact]
    public void RevisionSnapshotBytesAreIncludedInGlobalReplayQuota()
    {
        using AutomationEventHub hub = new(new AutomationEventHubOptions
        {
            MaxBufferedEvents = 8,
            MaxBufferedBytes = 1024,
        });
        TestEventSink sink = new(capacity: 8);
        hub.OpenSession(CreateSession("session-1", "client-1"), sink);
        _ = hub.Subscribe("session-1", SubscribeRequest("revision-bound", [], 8));
        AutomationRevisionSnapshot revision = Revision(
            1,
            ("scene:" + new string('x', 200), 1),
            ("project:" + new string('y', 200), 1));

        Assert.Equal(0, hub.Publish("scene.changed", revision));
        Assert.True(sink.Aborted);
        Assert.Empty(sink.Events);
    }

    /// <summary>真实 Named Pipe 验证 event envelope、Client buffer 与全新连接 resume/replay。</summary>
    [Fact]
    public async Task NamedPipeClientReceivesAndReplaysEventsAfterReconnect()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporary = new();
        using SchedulerHost host = new();
        await using EditorAutomationServer server = new(
            new AutomationServerOptions
            {
                DiscoveryRoot = temporary.Path,
                EditorVersion = "event-test",
                SupportedScopes = AutomationScopes.All,
                MaxQueuedEventsPerConnection = 16,
            },
            host.Scheduler);
        await server.StartAsync();
        AutomationDiscoverySnapshot snapshot = await AutomationDiscovery.DiscoverAsync(temporary.Path);
        AutomationDiscoveredInstance instance = Assert.Single(snapshot.Instances);
        const string clientInstanceId = "event-client-instance";
        AutomationClientOptions options = new()
        {
            ClientInstanceId = clientInstanceId,
            ClientName = "event-tests",
            ClientVersion = "1.0",
            RequestedScopes = [AutomationScopes.EditorRead],
            ConnectTimeout = TimeSpan.FromSeconds(5),
            RequestTimeout = TimeSpan.FromSeconds(5),
            MaxBufferedEvents = 16,
        };
        AutomationEventResumeState resumeState = new()
        {
            SubscriptionKey = "pipe-scene-events",
            EventTypes = ["scene.changed"],
            BacklogLimit = 8,
        };
        AutomationEventSubscription subscription;

        await using (EditorAutomationClient firstClient = await EditorAutomationClient.ConnectAsync(instance, options))
        {
            subscription = await firstClient.SubscribeOrResumeEventsAsync(resumeState);
            _ = host.Scheduler.Events.Publish(
                "scene.changed",
                host.Scheduler.Revisions.CaptureAll(),
                "pipe-request-1",
                JsonSerializer.SerializeToElement(new { value = 1 }));
            AutomationEventRecord first = await ReadOneAsync(firstClient);
            Assert.Equal(1, first.Sequence);
            Assert.Equal("pipe-request-1", first.CausationRequestId);
            subscription = await firstClient.AcknowledgeEventsAsync(subscription, first.Sequence);
            resumeState = subscription.ResumeState;
        }

        _ = host.Scheduler.Events.Publish(
            "scene.changed",
            host.Scheduler.Revisions.CaptureAll(),
            payload: JsonSerializer.SerializeToElement(new { value = 2 }));
        await using EditorAutomationClient secondClient = await EditorAutomationClient.ConnectAsync(instance, options);
        AutomationEventSubscription resumed = await secondClient.SubscribeOrResumeEventsAsync(resumeState);
        AutomationEventRecord replay = await ReadOneAsync(secondClient);

        Assert.Equal(2, replay.Sequence);
        Assert.Equal(2, replay.Payload?.GetProperty("value").GetInt32());
        Assert.Equal(2, resumed.Info.ReplayFromSequence);
        Assert.Equal(1, resumed.Info.BacklogCount);
    }

    /// <summary>验证 Client 的全局 event 字节配额满时主动断线，而不是只受 record 数量约束。</summary>
    [Fact]
    public async Task NamedPipeClientFailsClosedWhenEventByteQuotaIsExceeded()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporary = new();
        using SchedulerHost host = new();
        await using EditorAutomationServer server = new(
            new AutomationServerOptions
            {
                DiscoveryRoot = temporary.Path,
                EditorVersion = "event-byte-quota-test",
                SupportedScopes = AutomationScopes.All,
            },
            host.Scheduler);
        await server.StartAsync();
        AutomationDiscoveredInstance instance = Assert.Single(
            (await AutomationDiscovery.DiscoverAsync(temporary.Path)).Instances);
        await using EditorAutomationClient client = await EditorAutomationClient.ConnectAsync(
            instance,
            new AutomationClientOptions
            {
                ClientName = "event-byte-quota-tests",
                ClientVersion = "1.0",
                RequestedScopes = [AutomationScopes.EditorRead],
                ConnectTimeout = TimeSpan.FromSeconds(5),
                RequestTimeout = TimeSpan.FromSeconds(5),
                MaxBufferedEvents = 8,
                MaxBufferedEventBytes = 512,
            });
        _ = await client.SubscribeEventsAsync(SubscribeRequest("byte-quota", [], 8));

        _ = host.Scheduler.Events.Publish(
            "scene.changed",
            host.Scheduler.Revisions.CaptureAll(),
            payload: JsonSerializer.SerializeToElement(new { text = new string('x', 2048) }));
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        await using IAsyncEnumerator<AutomationEventRecord> events = client
            .ReadEventsAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        AutomationConnectionException exception = await Assert.ThrowsAsync<AutomationConnectionException>(
            async () => _ = await events.MoveNextAsync());
        Assert.Contains("连接已断开", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<AutomationEventRecord> ReadOneAsync(EditorAutomationClient client)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        await using IAsyncEnumerator<AutomationEventRecord> events = client
            .ReadEventsAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        Assert.True(await events.MoveNextAsync());
        return events.Current;
    }

    private static AutomationEventSubscribeRequest SubscribeRequest(
        string subscriptionKey,
        string[] eventTypes,
        int backlogLimit)
    {
        return new AutomationEventSubscribeRequest
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            SubscriptionKey = subscriptionKey,
            EventTypes = eventTypes,
            BacklogLimit = backlogLimit,
        };
    }

    private static AutomationSessionContext CreateSession(string sessionId, string clientInstanceId)
    {
        return new AutomationSessionContext
        {
            SessionId = sessionId,
            PrincipalId = new string('a', 64),
            ClientInstanceId = clientInstanceId,
            ClientName = "event-tests",
            GrantedScopes = [AutomationScopes.EditorRead],
        };
    }

    private static AutomationRevisionSnapshot Revision(
        long globalRevision,
        params (string ResourceId, long Revision)[] resources)
    {
        return new AutomationRevisionSnapshot
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            GlobalRevision = globalRevision,
            Resources =
            [
                .. resources.Select(static resource => new AutomationResourceRevision
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    ResourceId = resource.ResourceId,
                    Revision = resource.Revision,
                }),
            ],
        };
    }

    private sealed class TestEventSink(int capacity) : IAutomationEventSink
    {
        public List<AutomationEventRecord> Events { get; } = [];

        public bool Aborted { get; private set; }

        public bool TryPublish(AutomationEventRecord eventRecord)
        {
            if (Aborted || Events.Count >= capacity)
            {
                Abort();
                return false;
            }

            Events.Add(eventRecord);
            return true;
        }

        public void Abort()
        {
            Aborted = true;
        }
    }

    private sealed class SchedulerHost : IDisposable
    {
        private readonly AutoResetEvent _wake = new(false);
        private readonly Thread _thread;
        private readonly TaskCompletionSource<AutomationMainThreadScheduler> _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _stopping;

        public SchedulerHost()
        {
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "PixelEngine automation test Editor main thread",
            };
            _thread.Start();
            Scheduler = _started.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }

        public AutomationMainThreadScheduler Scheduler { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _stopping, 1) != 0)
            {
                return;
            }

            _ = _wake.Set();
            Assert.True(_thread.Join(TimeSpan.FromSeconds(5)));
            _wake.Dispose();
        }

        private void Run()
        {
            using AutomationMainThreadScheduler scheduler = new(
                [],
                new AutomationRevisionStore(),
                new TestUndoSink(),
                new TestTransactionParticipant(),
                new AutomationMainThreadSchedulerOptions { Wake = () => _wake.Set() });
            _ = _started.TrySetResult(scheduler);
            while (Volatile.Read(ref _stopping) == 0)
            {
                _ = _wake.WaitOne();
                while (scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress))
                {
                    _ = scheduler.Drain(AutomationExecutionPhase.EditorIngress);
                }
            }
        }
    }

    private sealed class TestUndoSink : IAutomationUndoSink
    {
        public void RecordExecuted(IAutomationUndoAction action)
        {
            throw new InvalidOperationException("Event test 不应写入 Undo。");
        }
    }

    private sealed class TestTransactionParticipant : IAutomationTransactionParticipant
    {
        public object CaptureState()
        {
            return new object();
        }

        public void RestoreState(object state)
        {
            ArgumentNullException.ThrowIfNull(state);
        }
    }
}
