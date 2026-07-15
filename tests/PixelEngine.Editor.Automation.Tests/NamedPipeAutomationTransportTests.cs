using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Cryptography;
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

        JsonElement? deferred = await client.InvokeRawAsync(TestHandler.DeferredMethod);
        Assert.Equal("background-complete", deferred?.GetProperty("state").GetString());
        Assert.True(handler.DeferredFactoryInvoked);

        string credential = (await File.ReadAllTextAsync(instance.CredentialPath)).Trim();
        string[] auditLines = await ReadAuditLinesAsync(server.AuditLogPath);
        JsonElement[] auditRecords =
        [
            .. auditLines.Select(ParseAuditRecord),
        ];
        Assert.Contains(auditRecords, static record =>
            record.GetProperty("capability").GetString() == AutomationProtocolConstants.HelloMethod);
        Assert.Contains(auditRecords, static record =>
            record.GetProperty("capability").GetString() == AutomationProtocolConstants.AuthenticateMethod &&
            record.GetProperty("principalId").GetString() is { Length: > 0 } &&
            record.GetProperty("sessionId").GetString() is { Length: > 0 });
        Assert.Contains(auditRecords, static record =>
            record.GetProperty("capability").GetString() == TestHandler.EchoMethod &&
            record.GetProperty("result").GetString() == "success" &&
            record.GetProperty("durationMicroseconds").GetInt64() >= 0);
        Assert.All(auditLines, line =>
        {
            Assert.DoesNotContain("payload", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("proof", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(credential, line, StringComparison.Ordinal);
        });
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

        JsonElement deniedAudit = (await ReadAuditLinesAsync(server.AuditLogPath))
            .Select(ParseAuditRecord)
            .Last(record => record.GetProperty("capability").GetString() == TestHandler.ControlMethod);
        Assert.Equal("error", deniedAudit.GetProperty("result").GetString());
        Assert.Equal(
            AutomationErrorCodes.PermissionDenied,
            deniedAudit.GetProperty("errorCode").GetString());
    }

    /// <summary>验证超限 semantic payload 在 success audit 前被转换为结构化 artifact 约束错误。</summary>
    [Fact]
    public async Task OversizedSemanticResponseReturnsStructuredErrorAndErrorAudit()
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
                MaxFrameBytes = 4096,
            },
            new TestHandler());
        await server.StartAsync();
        AutomationDiscoveredInstance instance = await DiscoverSingleAsync(temporary.Path);
        await using EditorAutomationClient client = await ConnectAsync(instance, [AutomationScopes.EditorRead]);

        AutomationRemoteException exception = await Assert.ThrowsAsync<AutomationRemoteException>(
            async () => await client.InvokeRawAsync(TestHandler.LargeResponseMethod));

        Assert.Equal(AutomationErrorCodes.ResponseTooLarge, exception.Error.Code);
        JsonElement audit = (await ReadAuditLinesAsync(server.AuditLogPath))
            .Select(ParseAuditRecord)
            .Last(record => record.GetProperty("capability").GetString() == TestHandler.LargeResponseMethod);
        Assert.Equal("error", audit.GetProperty("result").GetString());
        Assert.Equal(AutomationErrorCodes.ResponseTooLarge, audit.GetProperty("errorCode").GetString());
    }

    /// <summary>验证无效 handler response contract 被审计为 internal error，且 request slot 一定释放。</summary>
    [Fact]
    public async Task InvalidSemanticResponseContractReturnsInternalErrorWithoutHangingConnection()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporary = new();
        await using EditorAutomationServer server = CreateServer(temporary.Path, new TestHandler());
        await server.StartAsync();
        AutomationDiscoveredInstance instance = await DiscoverSingleAsync(temporary.Path);
        await using EditorAutomationClient client = await ConnectAsync(instance, [AutomationScopes.EditorRead]);

        AutomationRemoteException exception = await Assert.ThrowsAsync<AutomationRemoteException>(
            async () => await client.InvokeRawAsync(TestHandler.InvalidResponseMethod));
        AutomationPingResponse ping = await client.PingAsync();

        Assert.Equal(AutomationErrorCodes.Internal, exception.Error.Code);
        Assert.Equal(server.InstanceId, ping.InstanceId);
    }

    /// <summary>验证 handler 异常以有界结构返回原因链，且不回传 stack trace 或请求 payload。</summary>
    [Fact]
    public async Task HandlerFailureReturnsBoundedInternalErrorDetailsWithoutPayloadOrStackTrace()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporary = new();
        await using EditorAutomationServer server = CreateServer(temporary.Path, new TestHandler());
        await server.StartAsync();
        AutomationDiscoveredInstance instance = await DiscoverSingleAsync(temporary.Path);
        await using EditorAutomationClient client = await ConnectAsync(instance, [AutomationScopes.EditorRead]);
        const string PayloadMarker = "payload-secret-must-not-return";

        AutomationRemoteException exception = await Assert.ThrowsAsync<AutomationRemoteException>(
            async () => await client.InvokeRawAsync(
                TestHandler.InternalErrorMethod,
                JsonSerializer.SerializeToElement(new { secret = PayloadMarker })));

        Assert.Equal(AutomationErrorCodes.Internal, exception.Error.Code);
        Assert.Equal(AutomationErrorCategory.Internal, exception.Error.Category);
        JsonElement detailsElement = exception.Error.Details
            ?? throw new InvalidOperationException("internal_error 缺少结构化 details。");
        AutomationInternalErrorDetails details = detailsElement.Deserialize(
            AutomationJsonContext.Default.AutomationInternalErrorDetails)
            ?? throw new InvalidOperationException("internal_error details 无法反序列化。");
        Assert.Equal(typeof(AggregateException).FullName, details.ExceptionType);
        Assert.Contains("transport handler failure", details.Message, StringComparison.Ordinal);
        Assert.Collection(
            details.Causes,
            cause =>
            {
                Assert.Equal(typeof(InvalidOperationException).FullName, cause.ExceptionType);
                Assert.Equal("first bounded cause", cause.Message);
            },
            cause =>
            {
                Assert.Equal(typeof(ArgumentException).FullName, cause.ExceptionType);
                Assert.Equal("second bounded cause", cause.Message);
            });
        string detailsJson = detailsElement.GetRawText();
        Assert.DoesNotContain(PayloadMarker, detailsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("stackTrace", detailsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(NamedPipeAutomationTransportTests) + ".cs", detailsJson, StringComparison.Ordinal);
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

        AutomationConnectionException exception = await Assert.ThrowsAsync<AutomationConnectionException>(
            async () => await EditorAutomationClient.ConnectAsync(
                instance,
                CreateClientOptions([AutomationScopes.EditorRead]) with { CredentialPath = wrongCredential }));

        Assert.Contains("HMAC proof", exception.Message, StringComparison.Ordinal);
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
            ClientInstanceId = "unsupported-client-instance",
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

    /// <summary>验证 handshake identity/scope 数量与字符边界在进入 HMAC 前 fail-closed。</summary>
    [Fact]
    public async Task OversizedHandshakeIdentityIsRejectedBeforeChallenge()
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
            ClientInstanceId = new string('x', 129),
            ClientName = "bounded-client",
            ClientVersion = "1.0",
            SupportedVersions = [AutomationProtocolConstants.CurrentVersion],
            ClientNonce = AutomationAuthentication.GenerateNonce(),
            RequestedScopes = [AutomationScopes.EditorRead],
        };
        AutomationEnvelope request = new()
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Protocol = AutomationProtocolConstants.CurrentVersion,
            MessageId = "oversized-handshake",
            Kind = AutomationMessageKind.Request,
            Method = AutomationProtocolConstants.HelloMethod,
            Payload = JsonSerializer.SerializeToElement(
                hello,
                AutomationJsonContext.Default.AutomationHelloRequest),
        };

        await AutomationFrameCodec.WriteAsync(pipe, request);
        AutomationEnvelope response = await AutomationFrameCodec.ReadAsync(pipe);

        Assert.Equal(request.MessageId, response.CorrelationId);
        Assert.Equal(AutomationErrorCodes.InvalidRequest, response.Error?.Code);
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

    /// <summary>验证并发关闭会立即完成 pending 调用，不暴露内部同步原语的 disposed 竞态。</summary>
    [Fact]
    public async Task DisposingClientFailsPendingRequestWithConnectionError()
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
        EditorAutomationClient client = await ConnectAsync(instance, [AutomationScopes.EditorControl]);
        Task<JsonElement?> pending = client.InvokeRawAsync(
            TestHandler.WaitMethod,
            timeout: TimeSpan.FromSeconds(30)).AsTask();
        await handler.WaitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await client.DisposeAsync();

        _ = await Assert.ThrowsAsync<AutomationConnectionException>(async () => await pending);
        await handler.WaitCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>验证 Client 配置不能把 control frame 上限提升到 protocol 绝对边界之外。</summary>
    [Fact]
    public async Task ClientRejectsFrameLimitAboveProtocolAbsoluteLimit()
    {
        using TemporaryDirectory temporary = new();
        string credentialPath = Path.Combine(temporary.Path, "limit.token");
        await File.WriteAllTextAsync(
            credentialPath,
            Convert.ToBase64String(AutomationAuthentication.GenerateSecret()));
        AutomationDiscoveredInstance instance = CreateSyntheticInstance("unused-limit-pipe", credentialPath);

        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await EditorAutomationClient.ConnectAsync(
                instance,
                CreateClientOptions([AutomationScopes.EditorRead]) with
                {
                    MaxFrameBytes = AutomationProtocolConstants.AbsoluteMaxFrameBytes + 1,
                }));
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

    /// <summary>验证 Client 在发送自身 proof 前拒绝不能证明持有 credential 的伪 Pipe Server。</summary>
    [Fact]
    public async Task ClientRejectsInvalidServerProofBeforeAuthenticateRequest()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory temporary = new();
        string pipeName = $"pixelengine-invalid-server-proof-{Guid.NewGuid():N}";
        string credentialPath = Path.Combine(temporary.Path, "credential.token");
        byte[] credential = AutomationAuthentication.GenerateSecret();
        byte[] wrongSecret = AutomationAuthentication.GenerateSecret();
        try
        {
            await File.WriteAllTextAsync(credentialPath, Convert.ToBase64String(credential));
            AutomationDiscoveredInstance instance = CreateSyntheticInstance(pipeName, credentialPath);
            await using NamedPipeServerStream fakeServer = new(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            Task waitForConnection = fakeServer.WaitForConnectionAsync(CancellationToken.None);
            Task<EditorAutomationClient> connect = EditorAutomationClient.ConnectAsync(
                instance,
                CreateClientOptions([AutomationScopes.EditorRead])).AsTask();
            await waitForConnection.WaitAsync(TimeSpan.FromSeconds(5));

            AutomationEnvelope helloEnvelope = await AutomationFrameCodec.ReadAsync(fakeServer);
            AutomationHelloRequest hello = helloEnvelope.Payload?
                .Deserialize(AutomationJsonContext.Default.AutomationHelloRequest)
                ?? throw new InvalidOperationException("伪 Server 未收到 hello payload。");
            string serverNonce = AutomationAuthentication.GenerateNonce();
            AutomationHelloChallenge challenge = new()
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                InstanceId = instance.Descriptor.InstanceId,
                SelectedVersion = AutomationProtocolConstants.CurrentVersion,
                ServerNonce = serverNonce,
                ServerProof = AutomationAuthentication.ComputeServerProof(
                    wrongSecret,
                    instance.Descriptor.InstanceId,
                    hello.ClientInstanceId,
                    hello.ClientName,
                    hello.ClientVersion,
                    hello.ClientNonce,
                    serverNonce,
                    AutomationProtocolConstants.CurrentVersion,
                    hello.RequestedScopes,
                    AutomationScopes.All,
                    AutomationProtocolConstants.DefaultMaxFrameBytes),
                SupportedScopes = AutomationScopes.All,
                AuthenticationAlgorithm = AutomationProtocolConstants.AuthenticationAlgorithm,
                MaxFrameBytes = AutomationProtocolConstants.DefaultMaxFrameBytes,
            };
            await AutomationFrameCodec.WriteAsync(fakeServer, new AutomationEnvelope
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                Protocol = AutomationProtocolConstants.CurrentVersion,
                MessageId = Guid.NewGuid().ToString("N"),
                Kind = AutomationMessageKind.Response,
                CorrelationId = helloEnvelope.MessageId,
                Method = AutomationProtocolConstants.HelloMethod,
                Payload = JsonSerializer.SerializeToElement(
                    challenge,
                    AutomationJsonContext.Default.AutomationHelloChallenge),
            });

            AutomationConnectionException exception = await Assert.ThrowsAsync<AutomationConnectionException>(
                async () => await connect);
            Assert.Contains("HMAC proof", exception.Message, StringComparison.Ordinal);
            Assert.Equal(
                0,
                await ReadClosedPipeAsync(fakeServer, new byte[1]).WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(credential);
            CryptographicOperations.ZeroMemory(wrongSecret);
        }
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

    private static async Task<int> ReadClosedPipeAsync(Stream pipe, byte[] buffer)
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

    private static JsonElement ParseAuditRecord(string line)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        return document.RootElement.Clone();
    }

    private static async ValueTask<string[]> ReadAuditLinesAsync(string path)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);
        using StreamReader reader = new(stream);
        List<string> lines = [];
        while (await reader.ReadLineAsync() is { } line)
        {
            lines.Add(line);
        }

        return [.. lines];
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
        public const string LargeResponseMethod = "test.large-response";
        public const string InvalidResponseMethod = "test.invalid-response";
        public const string InternalErrorMethod = "test.internal-error";
        public const string DeferredMethod = "test.deferred";

        private readonly ConcurrentDictionary<string, AutomationMethodDescriptor> _descriptors = new(
            new Dictionary<string, AutomationMethodDescriptor>(StringComparer.Ordinal)
            {
                [EchoMethod] = new AutomationMethodDescriptor
                {
                    Method = EchoMethod,
                    RequiredScopes = [AutomationScopes.EditorRead],
                    OperationKind = AutomationOperationKind.Read,
                    ExecutionPhase = AutomationExecutionPhase.Background,
                    TransactionMode = AutomationTransactionMode.Forbidden,
                },
                [ControlMethod] = new AutomationMethodDescriptor
                {
                    Method = ControlMethod,
                    RequiredScopes = [AutomationScopes.EditorControl],
                    OperationKind = AutomationOperationKind.Command,
                    ExecutionPhase = AutomationExecutionPhase.Background,
                    TransactionMode = AutomationTransactionMode.Forbidden,
                },
                [WaitMethod] = new AutomationMethodDescriptor
                {
                    Method = WaitMethod,
                    RequiredScopes = [AutomationScopes.EditorControl],
                    OperationKind = AutomationOperationKind.Command,
                    ExecutionPhase = AutomationExecutionPhase.Background,
                    TransactionMode = AutomationTransactionMode.Forbidden,
                },
                [LargeResponseMethod] = new AutomationMethodDescriptor
                {
                    Method = LargeResponseMethod,
                    RequiredScopes = [AutomationScopes.EditorRead],
                    OperationKind = AutomationOperationKind.Read,
                    ExecutionPhase = AutomationExecutionPhase.Background,
                    TransactionMode = AutomationTransactionMode.Forbidden,
                },
                [InvalidResponseMethod] = new AutomationMethodDescriptor
                {
                    Method = InvalidResponseMethod,
                    RequiredScopes = [AutomationScopes.EditorRead],
                    OperationKind = AutomationOperationKind.Read,
                    ExecutionPhase = AutomationExecutionPhase.Background,
                    TransactionMode = AutomationTransactionMode.Forbidden,
                },
                [InternalErrorMethod] = new AutomationMethodDescriptor
                {
                    Method = InternalErrorMethod,
                    RequiredScopes = [AutomationScopes.EditorRead],
                    OperationKind = AutomationOperationKind.Read,
                    ExecutionPhase = AutomationExecutionPhase.Background,
                    TransactionMode = AutomationTransactionMode.Forbidden,
                },
                [DeferredMethod] = new AutomationMethodDescriptor
                {
                    Method = DeferredMethod,
                    RequiredScopes = [AutomationScopes.EditorRead],
                    OperationKind = AutomationOperationKind.Read,
                    ExecutionPhase = AutomationExecutionPhase.Background,
                    TransactionMode = AutomationTransactionMode.Forbidden,
                    ArtifactBehavior = AutomationArtifactBehavior.Required,
                },
            });

        public bool DeferredFactoryInvoked { get; private set; }

        public TaskCompletionSource WaitStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource WaitCancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool TryGetDescriptor(string method, out AutomationMethodDescriptor descriptor)
        {
            return _descriptors.TryGetValue(method, out descriptor!);
        }

        public async ValueTask<AutomationHandlerResult> HandleAsync(
            AutomationRequestContext context,
            string method,
            JsonElement? payload,
            CancellationToken cancellationToken)
        {
            if (string.Equals(method, EchoMethod, StringComparison.Ordinal))
            {
                await Task.Yield();
                return new AutomationHandlerResult { Payload = payload?.Clone() };
            }

            if (string.Equals(method, ControlMethod, StringComparison.Ordinal))
            {
                return new AutomationHandlerResult
                {
                    Payload = JsonSerializer.SerializeToElement(new { ok = true }),
                };
            }

            if (string.Equals(method, DeferredMethod, StringComparison.Ordinal))
            {
                return new AutomationHandlerResult
                {
                    Revision = new AutomationRevisionSnapshot
                    {
                        SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                        GlobalRevision = 7,
                        Resources = [],
                    },
                    DeferredPayloadFactory = async (revision, token) =>
                    {
                        await Task.Yield();
                        token.ThrowIfCancellationRequested();
                        DeferredFactoryInvoked = true;
                        return JsonSerializer.SerializeToElement(new
                        {
                            state = "background-complete",
                            revision = revision.GlobalRevision,
                        });
                    },
                };
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

            return string.Equals(method, InternalErrorMethod, StringComparison.Ordinal)
                ? throw new AggregateException(
                    "transport handler failure",
                    new InvalidOperationException("first bounded cause"),
                    new ArgumentException("second bounded cause"))
                : string.Equals(method, LargeResponseMethod, StringComparison.Ordinal)
                ? new AutomationHandlerResult
                {
                    Payload = JsonSerializer.SerializeToElement(new { value = new string('x', 16 * 1024) }),
                }
                : string.Equals(method, InvalidResponseMethod, StringComparison.Ordinal)
                    ? new AutomationHandlerResult
                    {
                        Revision = new AutomationRevisionSnapshot
                        {
                            SchemaVersion = 0,
                            GlobalRevision = 0,
                            Resources = [],
                        },
                    }
                    : throw new InvalidOperationException($"Unexpected method {method}.");
        }
    }
}
