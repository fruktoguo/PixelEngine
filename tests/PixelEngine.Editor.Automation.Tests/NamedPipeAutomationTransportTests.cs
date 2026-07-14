using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using PixelEngine.Editor.Automation.Client;
using PixelEngine.Editor.Automation.Protocol;
using PixelEngine.Editor.Automation.Server;

namespace PixelEngine.Editor.Automation.Tests;

/// <summary>
/// 真实 Windows Named Pipe Server/Client 集成测试。
/// </summary>
public sealed class NamedPipeAutomationTransportTests
{
    /// <summary>验证认证、system methods 与多并发 correlation。</summary>
    [Fact]
    public async Task ClientAuthenticatesPingsDescribesAndCorrelatesConcurrentRequests()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporary = new();
        TestHandler handler = new();
        await using EditorAutomationServer server = CreateServer(temporary.Path, handler);
        await server.StartAsync();
        AutomationDiscoveredInstance instance = await DiscoverSingleAsync(temporary.Path);
        await using EditorAutomationClient client = await ConnectAsync(
            instance,
            [AutomationScopes.EditorRead, AutomationScopes.EditorControl]);

        AutomationPingResponse ping = await client.PingAsync();
        AutomationInstanceDescriptor description = await client.DescribeAsync();
        Assert.Equal(server.InstanceId, ping.InstanceId);
        Assert.Equal(server.InstanceId, description.InstanceId);

        Task<JsonElement?>[] calls =
        [
            .. Enumerable.Range(0, 32)
                .Select(index => client.InvokeRawAsync(
                    TestHandler.EchoMethod,
                    JsonSerializer.SerializeToElement(new { index })).AsTask()),
        ];
        JsonElement?[] responses = await Task.WhenAll(calls);

        Assert.Equal(
            Enumerable.Range(0, 32),
            responses.Select(static response => response!.Value.GetProperty("index").GetInt32()).Order());
    }

    /// <summary>验证缺失 scope 返回稳定结构化授权错误。</summary>
    [Fact]
    public async Task ServerRejectsMissingScopeWithStructuredError()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporary = new();
        TestHandler handler = new();
        await using EditorAutomationServer server = CreateServer(temporary.Path, handler);
        await server.StartAsync();
        AutomationDiscoveredInstance instance = await DiscoverSingleAsync(temporary.Path);
        await using EditorAutomationClient client = await ConnectAsync(instance, [AutomationScopes.EditorRead]);

        AutomationRemoteException exception = await Assert.ThrowsAsync<AutomationRemoteException>(
            async () => await client.InvokeRawAsync(TestHandler.ControlMethod));

        Assert.Equal(AutomationErrorCodes.PermissionDenied, exception.Error.Code);
        Assert.Equal(AutomationErrorCategory.Authorization, exception.Error.Category);
        Assert.False(exception.Error.Transient);
    }

    /// <summary>验证错误 credential 无法创建 session。</summary>
    [Fact]
    public async Task WrongCredentialFailsChallengeWithoutCreatingSession()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporary = new();
        await using EditorAutomationServer server = CreateServer(temporary.Path, new TestHandler());
        await server.StartAsync();
        AutomationDiscoveredInstance instance = await DiscoverSingleAsync(temporary.Path);
        string wrongCredential = Path.Combine(temporary.Path, "wrong.token");
        await File.WriteAllTextAsync(
            wrongCredential,
            Convert.ToBase64String(AutomationAuthentication.GenerateSecret()));

        AutomationRemoteException exception = await Assert.ThrowsAsync<AutomationRemoteException>(
            async () => await EditorAutomationClient.ConnectAsync(
                instance,
                CreateClientOptions([AutomationScopes.EditorRead]) with { CredentialPath = wrongCredential }));

        Assert.Equal(AutomationErrorCodes.AuthenticationFailed, exception.Error.Code);
    }

    /// <summary>验证没有共同 major 版本时 Server 返回稳定错误并拒绝继续握手。</summary>
    [Fact]
    public async Task UnsupportedProtocolMajorIsRejectedDuringHello()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporary = new();
        await using EditorAutomationServer server = CreateServer(temporary.Path, new TestHandler());
        await server.StartAsync();
        AutomationDiscoveredInstance instance = await DiscoverSingleAsync(temporary.Path);
        await using NamedPipeClientStream pipe = new(
            ".",
            instance.Descriptor.Endpoint.Address,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(CancellationToken.None);
        AutomationHelloRequest hello = new()
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            ClientName = "unsupported-client",
            ClientVersion = "1.0",
            SupportedVersions = [new AutomationProtocolVersion(2, 0)],
            ClientNonce = AutomationAuthentication.GenerateNonce(),
            RequestedScopes = [AutomationScopes.EditorRead],
        };
        AutomationEnvelope request = new()
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Protocol = new AutomationProtocolVersion(2, 0),
            MessageId = "unsupported-major",
            Kind = AutomationMessageKind.Request,
            Method = AutomationProtocolConstants.HelloMethod,
            Payload = JsonSerializer.SerializeToElement(
                hello,
                AutomationJsonContext.Default.AutomationHelloRequest),
        };

        await AutomationFrameCodec.WriteAsync(pipe, request);
        AutomationEnvelope response = await AutomationFrameCodec.ReadAsync(pipe);

        Assert.Equal(request.MessageId, response.CorrelationId);
        Assert.Equal(AutomationErrorCodes.ProtocolVersionUnsupported, response.Error?.Code);
    }

    /// <summary>验证调用方取消会发送协议 cancel 并抵达 handler。</summary>
    [Fact]
    public async Task CallerCancellationSendsProtocolCancelToRunningHandler()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporary = new();
        TestHandler handler = new();
        await using EditorAutomationServer server = CreateServer(temporary.Path, handler);
        await server.StartAsync();
        AutomationDiscoveredInstance instance = await DiscoverSingleAsync(temporary.Path);
        await using EditorAutomationClient client = await ConnectAsync(
            instance,
            [AutomationScopes.EditorControl]);
        using CancellationTokenSource cancellation = new();
        Task<JsonElement?> request = client.InvokeRawAsync(
            TestHandler.WaitMethod,
            timeout: TimeSpan.FromSeconds(10),
            cancellationToken: cancellation.Token).AsTask();

        await handler.WaitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await request);
        await handler.WaitCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>验证 Server deadline 取消 handler 并返回稳定错误。</summary>
    [Fact]
    public async Task ServerDeadlineCancelsHandlerAndReturnsStableError()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporary = new();
        TestHandler handler = new();
        await using EditorAutomationServer server = CreateServer(temporary.Path, handler);
        await server.StartAsync();
        AutomationDiscoveredInstance instance = await DiscoverSingleAsync(temporary.Path);
        await using EditorAutomationClient client = await ConnectAsync(
            instance,
            [AutomationScopes.EditorControl]);

        AutomationRemoteException exception = await Assert.ThrowsAsync<AutomationRemoteException>(
            async () => await client.InvokeRawAsync(
                TestHandler.WaitMethod,
                timeout: TimeSpan.FromMilliseconds(100)));

        Assert.Equal(AutomationErrorCodes.DeadlineExceeded, exception.Error.Code);
        await handler.WaitCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>验证连接上限为一时 accept loop 会在前一连接释放后继续接受新客户端。</summary>
    [Fact]
    public async Task SingleConnectionLimitAcceptsSequentialAuthenticatedClients()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporary = new();
        await using EditorAutomationServer server = new(
            new AutomationServerOptions
            {
                DiscoveryRoot = temporary.Path,
                EditorVersion = "transport-test",
                SupportedScopes = AutomationScopes.All,
                MaxConnections = 1,
            },
            new TestHandler());
        await server.StartAsync();
        AutomationDiscoveredInstance instance = await DiscoverSingleAsync(temporary.Path);

        await using (EditorAutomationClient first = await ConnectAsync(instance, [AutomationScopes.EditorRead]))
        {
            _ = await first.PingAsync();
        }

        await using EditorAutomationClient second = await ConnectAsync(instance, [AutomationScopes.EditorRead]);
        AutomationPingResponse ping = await second.PingAsync();
        Assert.Equal(server.InstanceId, ping.InstanceId);
    }

    /// <summary>验证未认证空闲连接按握手时限关闭且不会永久占用唯一连接槽。</summary>
    [Fact]
    public async Task IdleUnauthenticatedConnectionTimesOutAndReleasesSlot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporary = new();
        await using EditorAutomationServer server = new(
            new AutomationServerOptions
            {
                DiscoveryRoot = temporary.Path,
                EditorVersion = "transport-test",
                SupportedScopes = AutomationScopes.All,
                MaxConnections = 1,
                HandshakeTimeout = TimeSpan.FromMilliseconds(100),
            },
            new TestHandler());
        await server.StartAsync();
        AutomationDiscoveredInstance instance = await DiscoverSingleAsync(temporary.Path);
        await using (NamedPipeClientStream idle = new(
                         ".",
                         instance.Descriptor.Endpoint.Address,
                         PipeDirection.InOut,
                         PipeOptions.Asynchronous))
        {
            await idle.ConnectAsync(CancellationToken.None);
            byte[] oneByte = new byte[1];
            int read = await ReadClosedPipeAsync(idle, oneByte).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, read);
        }

        await using EditorAutomationClient client = await ConnectAsync(instance, [AutomationScopes.EditorRead]);
        _ = await client.PingAsync();
    }

    /// <summary>验证 Client 的 connect timeout 覆盖已连接但不回应 challenge 的伪 Server。</summary>
    [Fact]
    public async Task ClientConnectTimeoutCoversAuthenticationHandshake()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporary = new();
        string pipeName = $"pixelengine-editor-v1-stall-{Guid.NewGuid():N}";
        string credentialPath = Path.Combine(temporary.Path, "stall.token");
        await File.WriteAllTextAsync(
            credentialPath,
            Convert.ToBase64String(AutomationAuthentication.GenerateSecret()));
        AutomationDiscoveredInstance instance = CreateSyntheticInstance(pipeName, credentialPath);
        await using NamedPipeServerStream stalledServer = new(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        Task waitForConnection = stalledServer.WaitForConnectionAsync(CancellationToken.None);

        Task connect = EditorAutomationClient.ConnectAsync(
            instance,
            CreateClientOptions([AutomationScopes.EditorRead]) with
            {
                ConnectTimeout = TimeSpan.FromMilliseconds(100),
            }).AsTask();
        await waitForConnection.WaitAsync(TimeSpan.FromSeconds(5));
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await connect.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private static EditorAutomationServer CreateServer(string root, IAutomationRequestHandler handler)
    {
        return new EditorAutomationServer(
            new AutomationServerOptions
            {
                DiscoveryRoot = root,
                EditorVersion = "transport-test",
                SupportedScopes = AutomationScopes.All,
            },
            handler);
    }

    private static async ValueTask<AutomationDiscoveredInstance> DiscoverSingleAsync(string root)
    {
        AutomationDiscoverySnapshot snapshot = await AutomationDiscovery.DiscoverAsync(root);
        Assert.Empty(snapshot.Diagnostics);
        return Assert.Single(snapshot.Instances);
    }

    private static async ValueTask<EditorAutomationClient> ConnectAsync(
        AutomationDiscoveredInstance instance,
        string[] scopes)
    {
        return await EditorAutomationClient.ConnectAsync(instance, CreateClientOptions(scopes));
    }

    private static AutomationClientOptions CreateClientOptions(string[] scopes)
    {
        return new AutomationClientOptions
        {
            ClientName = "transport-tests",
            ClientVersion = "1.0",
            RequestedScopes = scopes,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            RequestTimeout = TimeSpan.FromSeconds(5),
        };
    }

    private static async Task<int> ReadClosedPipeAsync(NamedPipeClientStream pipe, byte[] buffer)
    {
        try
        {
            return await pipe.ReadAsync(buffer);
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static AutomationDiscoveredInstance CreateSyntheticInstance(string pipeName, string credentialPath)
    {
        AutomationInstanceDescriptor descriptor = new()
        {
            Schema = AutomationProtocolConstants.InstanceDescriptorSchema,
            InstanceId = "stall",
            ProcessId = Environment.ProcessId,
            ProcessStartUtc = DateTimeOffset.UtcNow,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            EditorVersion = "stalled-server",
            ProtocolVersions = [AutomationProtocolConstants.CurrentVersion],
            Endpoint = new AutomationEndpointDescriptor
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                Kind = AutomationTransportKind.WindowsNamedPipe,
                Address = pipeName,
            },
            CredentialPath = credentialPath,
            CapabilityDigest = new string('0', 64),
            LivenessMode = "processIdentity",
        };
        return new AutomationDiscoveredInstance
        {
            Descriptor = descriptor,
            DescriptorPath = Path.Combine(Path.GetDirectoryName(credentialPath)!, "stall.json"),
            CredentialPath = credentialPath,
        };
    }

    private sealed class TestHandler : IAutomationRequestHandler
    {
        public const string EchoMethod = "test.echo";
        public const string ControlMethod = "test.control";
        public const string WaitMethod = "test.wait";

        private readonly ConcurrentDictionary<string, AutomationMethodDescriptor> _descriptors = new(
            new Dictionary<string, AutomationMethodDescriptor>(StringComparer.Ordinal)
            {
                [EchoMethod] = new AutomationMethodDescriptor
                {
                    Method = EchoMethod,
                    RequiredScopes = [AutomationScopes.EditorRead],
                },
                [ControlMethod] = new AutomationMethodDescriptor
                {
                    Method = ControlMethod,
                    RequiredScopes = [AutomationScopes.EditorControl],
                },
                [WaitMethod] = new AutomationMethodDescriptor
                {
                    Method = WaitMethod,
                    RequiredScopes = [AutomationScopes.EditorControl],
                },
            });

        public TaskCompletionSource WaitStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource WaitCancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool TryGetDescriptor(string method, out AutomationMethodDescriptor descriptor)
        {
            return _descriptors.TryGetValue(method, out descriptor!);
        }

        public async ValueTask<JsonElement?> HandleAsync(
            AutomationRequestContext context,
            string method,
            JsonElement? payload,
            CancellationToken cancellationToken)
        {
            if (string.Equals(method, EchoMethod, StringComparison.Ordinal))
            {
                await Task.Yield();
                return payload?.Clone();
            }

            if (string.Equals(method, ControlMethod, StringComparison.Ordinal))
            {
                return JsonSerializer.SerializeToElement(new { ok = true });
            }

            if (string.Equals(method, WaitMethod, StringComparison.Ordinal))
            {
                _ = WaitStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _ = WaitCancelled.TrySetResult();
                    throw;
                }
            }

            throw new InvalidOperationException($"Unexpected method {method}.");
        }
    }
}
