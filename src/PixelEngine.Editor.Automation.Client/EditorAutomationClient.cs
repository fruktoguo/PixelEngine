using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Client;

/// <summary>
/// 支持并发 correlation、deadline 与显式 cancel 的 .NET Editor automation Client。
/// </summary>
public sealed partial class EditorAutomationClient : IAsyncDisposable
{
    private readonly AutomationClientOptions _options;
    private readonly NamedPipeClientStream _pipe;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<AutomationEnvelope>> _pending =
        new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Channel<BufferedClientEvent> _events;
    private readonly Task _reader;
    private long _bufferedEventBytes;
    private int _disposed;

    private EditorAutomationClient(
        AutomationDiscoveredInstance instance,
        AutomationClientOptions options,
        NamedPipeClientStream pipe,
        AutomationProtocolVersion protocol,
        AutomationSessionInfo session)
    {
        Instance = instance;
        _options = options;
        _pipe = pipe;
        Protocol = protocol;
        Session = session;
        _events = Channel.CreateBounded<BufferedClientEvent>(new BoundedChannelOptions(options.MaxBufferedEvents)
        {
            SingleReader = false,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });
        _reader = ReadLoopAsync(_shutdown.Token);
    }

    /// <summary>已连接实例。</summary>
    public AutomationDiscoveredInstance Instance { get; }

    /// <summary>协商协议版本。</summary>
    public AutomationProtocolVersion Protocol { get; }

    /// <summary>认证 session。</summary>
    public AutomationSessionInfo Session { get; }

    /// <summary>
    /// 连接经过 discovery 校验的 Windows Named Pipe 实例并完成 challenge/HMAC。
    /// </summary>
    /// <param name="instance">经过 discovery 校验的实例。</param>
    /// <param name="options">Client 配置。</param>
    /// <param name="cancellationToken">连接取消令牌。</param>
    /// <returns>已认证 Client。</returns>
    public static async ValueTask<EditorAutomationClient> ConnectAsync(
        AutomationDiscoveredInstance instance,
        AutomationClientOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        options = ValidateOptions(options);
        AutomationInstanceDescriptor descriptor = ValidateConnectInstance(instance);
        if (descriptor.Endpoint.Kind != AutomationTransportKind.WindowsNamedPipe || !OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("AUTO-001 v1 Client 当前只连接 Windows Named Pipe。");
        }

        string credentialPath = string.IsNullOrWhiteSpace(options.CredentialPath)
            ? instance.CredentialPath
            : Path.GetFullPath(options.CredentialPath);
        byte[] secret = ReadCredential(credentialPath);
        NamedPipeClientStream pipe = new(
            serverName: ".",
            pipeName: descriptor.Endpoint.Address,
            direction: PipeDirection.InOut,
            options: PipeOptions.Asynchronous);
        try
        {
            using CancellationTokenSource connectCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCancellation.CancelAfter(options.ConnectTimeout);
            await pipe.ConnectAsync(connectCancellation.Token).ConfigureAwait(false);

            (AutomationProtocolVersion protocol, AutomationSessionInfo session, int maxFrameBytes) = await AuthenticateAsync(
                pipe,
                descriptor,
                options,
                secret,
                connectCancellation.Token).ConfigureAwait(false);
            return new EditorAutomationClient(
                instance,
                options with { MaxFrameBytes = maxFrameBytes },
                pipe,
                protocol,
                session);
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    /// <summary>
    /// 调用 method 并返回 raw JSON payload。
    /// </summary>
    /// <param name="method">稳定 method/capability id。</param>
    /// <param name="payload">请求 payload。</param>
    /// <param name="timeout">覆盖默认 deadline。</param>
    /// <param name="cancellationToken">调用方取消令牌。</param>
    /// <returns>响应 payload。</returns>
    public async ValueTask<JsonElement?> InvokeRawAsync(
        string method,
        JsonElement? payload = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        AutomationInvocationResult result = await InvokeDetailedAsync(
            method,
            payload,
            new AutomationInvocationOptions { Timeout = timeout },
            cancellationToken).ConfigureAwait(false);
        return result.Payload;
    }

    /// <summary>
    /// 调用 method 并返回 payload 与同一安全点 revision。
    /// </summary>
    /// <param name="method">稳定 method/capability id。</param>
    /// <param name="payload">请求 payload。</param>
    /// <param name="options">timeout、revision、幂等与 transaction 选项。</param>
    /// <param name="cancellationToken">调用方取消令牌。</param>
    /// <returns>payload 与 revision。</returns>
    public async ValueTask<AutomationInvocationResult> InvokeDetailedAsync(
        string method,
        JsonElement? payload = null,
        AutomationInvocationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!IsBoundedText(method, 256))
        {
            throw new ArgumentException("Automation method 长度或字符无效。", nameof(method));
        }

        options ??= new AutomationInvocationOptions();
        TimeSpan effectiveTimeout = options.Timeout ?? _options.RequestTimeout;
        ValidateTimeout(effectiveTimeout, nameof(options));
        ValidateInvocationOptions(options);
        JsonElement? immutablePayload = payload?.Clone();
        AutomationRevisionPrecondition? immutableExpectedRevision =
            CloneAndValidateExpectedRevision(options.ExpectedRevision);

        string requestId = Guid.NewGuid().ToString("N");
        TaskCompletionSource<AutomationEnvelope> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(requestId, completion))
        {
            throw new InvalidOperationException("无法登记唯一 automation request id。");
        }

        AutomationEnvelope request = new()
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Protocol = Protocol,
            MessageId = requestId,
            Kind = AutomationMessageKind.Request,
            CorrelationId = requestId,
            Method = method,
            SessionId = Session.SessionId,
            DeadlineUtc = _options.TimeProvider.GetUtcNow() + effectiveTimeout,
            ExpectedRevision = immutableExpectedRevision,
            IdempotencyKey = options.IdempotencyKey,
            TransactionId = options.TransactionId,
            Payload = immutablePayload,
        };

        try
        {
            await WriteAsync(request, cancellationToken).ConfigureAwait(false);
            AutomationEnvelope response;
            try
            {
                response = await completion.Task
                    .WaitAsync(
                        effectiveTimeout + TimeSpan.FromSeconds(1),
                        _options.TimeProvider,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                await TrySendCancelAsync(requestId).ConfigureAwait(false);
                throw new AutomationRequestTimeoutException(requestId, effectiveTimeout);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await TrySendCancelAsync(requestId).ConfigureAwait(false);
                throw;
            }

            return response.Error is null
                ? new AutomationInvocationResult
                {
                    Payload = response.Payload,
                    Revision = response.Revision,
                }
                : throw new AutomationRemoteException(response.Error);
        }
        finally
        {
            _ = _pending.TryRemove(requestId, out _);
        }
    }

    /// <summary>
    /// 用 source-generated JSON metadata 调用 typed method。
    /// </summary>
    /// <typeparam name="TRequest">请求 DTO。</typeparam>
    /// <typeparam name="TResponse">响应 DTO。</typeparam>
    /// <param name="method">稳定 method/capability id。</param>
    /// <param name="request">请求 DTO。</param>
    /// <param name="requestTypeInfo">请求 source-generated metadata。</param>
    /// <param name="responseTypeInfo">响应 source-generated metadata。</param>
    /// <param name="timeout">覆盖默认 deadline。</param>
    /// <param name="cancellationToken">调用方取消令牌。</param>
    /// <returns>typed response。</returns>
    public async ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(
        string method,
        TRequest request,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        AutomationTypedInvocationResult<TResponse> result = await InvokeDetailedAsync(
            method,
            request,
            requestTypeInfo,
            responseTypeInfo,
            new AutomationInvocationOptions { Timeout = timeout },
            cancellationToken).ConfigureAwait(false);
        return result.Response;
    }

    /// <summary>用 source-generated metadata 调用 typed method，并保留同一安全点 revision。</summary>
    /// <typeparam name="TRequest">请求 DTO。</typeparam>
    /// <typeparam name="TResponse">响应 DTO。</typeparam>
    /// <param name="method">稳定 method/capability id。</param>
    /// <param name="request">请求 DTO。</param>
    /// <param name="requestTypeInfo">请求 source-generated metadata。</param>
    /// <param name="responseTypeInfo">响应 source-generated metadata。</param>
    /// <param name="options">timeout、revision、幂等与 transaction 选项。</param>
    /// <param name="cancellationToken">调用方取消令牌。</param>
    /// <returns>typed response 与 revision。</returns>
    public async ValueTask<AutomationTypedInvocationResult<TResponse>> InvokeDetailedAsync<TRequest, TResponse>(
        string method,
        TRequest request,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo,
        AutomationInvocationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestTypeInfo);
        ArgumentNullException.ThrowIfNull(responseTypeInfo);
        JsonElement payload = JsonSerializer.SerializeToElement(request, requestTypeInfo);
        AutomationInvocationResult result = await InvokeDetailedAsync(
            method,
            payload,
            options,
            cancellationToken).ConfigureAwait(false);
        JsonElement responsePayload = result.Payload
            ?? throw new AutomationConnectionException($"Automation method '{method}' 没有返回所需 payload。");

        try
        {
            TResponse response = responsePayload.Deserialize(responseTypeInfo)
                ?? throw new AutomationConnectionException($"Automation method '{method}' 返回 null payload。");
            return new AutomationTypedInvocationResult<TResponse>
            {
                Response = response,
                Revision = result.Revision,
            };
        }
        catch (JsonException exception)
        {
            throw new AutomationConnectionException(
                $"Automation method '{method}' 响应不符合 Client schema。",
                exception);
        }
    }

    /// <summary>调用无 typed request 的 method，并严格反序列化响应与 revision。</summary>
    /// <typeparam name="TResponse">响应 DTO。</typeparam>
    /// <param name="method">稳定 method/capability id。</param>
    /// <param name="responseTypeInfo">响应 source-generated metadata。</param>
    /// <param name="payload">可选 raw payload。</param>
    /// <param name="options">timeout、revision、幂等与 transaction 选项。</param>
    /// <param name="cancellationToken">调用方取消令牌。</param>
    /// <returns>typed response 与 revision。</returns>
    public async ValueTask<AutomationTypedInvocationResult<TResponse>> InvokeDetailedAsync<TResponse>(
        string method,
        JsonTypeInfo<TResponse> responseTypeInfo,
        JsonElement? payload = null,
        AutomationInvocationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(responseTypeInfo);
        AutomationInvocationResult result = await InvokeDetailedAsync(
            method,
            payload,
            options,
            cancellationToken).ConfigureAwait(false);
        JsonElement responsePayload = result.Payload
            ?? throw new AutomationConnectionException($"Automation method '{method}' 没有返回所需 payload。");
        try
        {
            TResponse response = responsePayload.Deserialize(responseTypeInfo)
                ?? throw new AutomationConnectionException($"Automation method '{method}' 返回 null payload。");
            return new AutomationTypedInvocationResult<TResponse>
            {
                Response = response,
                Revision = result.Revision,
            };
        }
        catch (JsonException exception)
        {
            throw new AutomationConnectionException(
                $"Automation method '{method}' 响应不符合 Client schema。",
                exception);
        }
    }

    /// <summary>
    /// 探测 session 与实例连通性。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>Server ping。</returns>
    public async ValueTask<AutomationPingResponse> PingAsync(CancellationToken cancellationToken = default)
    {
        JsonElement? payload = await InvokeRawAsync(
            AutomationProtocolConstants.PingMethod,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        AutomationPingResponse response = payload?.Deserialize(AutomationJsonContext.Default.AutomationPingResponse)
            ?? throw new AutomationConnectionException("system.ping 没有返回 payload。");
        return response.SchemaVersion == AutomationProtocolConstants.WireSchemaVersion
            ? response
            : throw new AutomationConnectionException($"不支持 ping response schema v{response.SchemaVersion}。");
    }

    /// <summary>
    /// 读取当前实例 descriptor。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前 descriptor。</returns>
    public async ValueTask<AutomationInstanceDescriptor> DescribeAsync(
        CancellationToken cancellationToken = default)
    {
        JsonElement? payload = await InvokeRawAsync(
            AutomationProtocolConstants.DescribeMethod,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        AutomationInstanceDescriptor response = payload?
            .Deserialize(AutomationJsonContext.Default.AutomationInstanceDescriptor)
            ?? throw new AutomationConnectionException("system.describe 没有返回 payload。");
        return string.Equals(
            response.Schema,
            AutomationProtocolConstants.InstanceDescriptorSchema,
            StringComparison.Ordinal)
            ? response
            : throw new AutomationConnectionException($"不支持 instance descriptor schema '{response.Schema}'。");
    }

    /// <summary>创建或恢复 event subscription。</summary>
    /// <param name="request">filter、稳定 subscription key 与 resume state。</param>
    /// <param name="cancellationToken">调用方取消。</param>
    /// <returns>订阅和 replay 边界。</returns>
    public async ValueTask<AutomationSubscriptionInfo> SubscribeEventsAsync(
        AutomationEventSubscribeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            request.BacklogLimit,
            _options.MaxBufferedEvents,
            nameof(request));

        return await InvokeAsync(
            AutomationProtocolConstants.EventSubscribeMethod,
            request,
            AutomationJsonContext.Default.AutomationEventSubscribeRequest,
            AutomationJsonContext.Default.AutomationSubscriptionInfo,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>确认已完整处理的最大连续 event sequence。</summary>
    /// <param name="request">subscription 与 sequence。</param>
    /// <param name="cancellationToken">调用方取消。</param>
    /// <returns>ack 后订阅状态。</returns>
    public ValueTask<AutomationSubscriptionInfo> AcknowledgeEventsAsync(
        AutomationEventAckRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return InvokeAsync(
            AutomationProtocolConstants.EventAckMethod,
            request,
            AutomationJsonContext.Default.AutomationEventAckRequest,
            AutomationJsonContext.Default.AutomationSubscriptionInfo,
            cancellationToken: cancellationToken);
    }

    /// <summary>显式删除 subscription 与服务端 replay backlog。</summary>
    /// <param name="request">subscription id。</param>
    /// <param name="cancellationToken">调用方取消。</param>
    /// <returns>removed 状态。</returns>
    public ValueTask<AutomationSubscriptionInfo> UnsubscribeEventsAsync(
        AutomationEventSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return InvokeAsync(
            AutomationProtocolConstants.EventUnsubscribeMethod,
            request,
            AutomationJsonContext.Default.AutomationEventSubscriptionRequest,
            AutomationJsonContext.Default.AutomationSubscriptionInfo,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 按 pipe 接收顺序读取 event；业务处理成功后由调用方显式 ack。
    /// </summary>
    /// <param name="cancellationToken">停止当前枚举，不会关闭 Client。</param>
    /// <returns>at-least-once event stream。</returns>
    public async IAsyncEnumerable<AutomationEventRecord> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (BufferedClientEvent buffered in _events.Reader.ReadAllAsync(cancellationToken)
                           .ConfigureAwait(false))
        {
            _ = Interlocked.Add(ref _bufferedEventBytes, -buffered.Bytes);
            yield return buffered.Record;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        List<Exception>? failures = null;
        try
        {
            _shutdown.Cancel();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        try
        {
            await _pipe.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        try
        {
            await _reader.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException)
        {
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }
        finally
        {
            AutomationConnectionException closed = new("Automation Client 已关闭。");
            FailPending(closed);
            _ = _events.Writer.TryComplete();
            // Dispose 可与已经通过入口检查的并发调用竞态。保留纯托管同步原语到 GC，
            // 避免等待写锁的调用在释放时看到 ObjectDisposedException；Pipe、reader 与
            // 所有 pending completion 已在上面确定性关闭。
        }

        if (failures is not null)
        {
            throw new AggregateException("Automation Client 关闭时遇到错误。", failures);
        }
    }

    private static async ValueTask<(
        AutomationProtocolVersion Protocol,
        AutomationSessionInfo Session,
        int MaxFrameBytes)> AuthenticateAsync(
        Stream pipe,
        AutomationInstanceDescriptor descriptor,
        AutomationClientOptions options,
        ReadOnlyMemory<byte> secret,
        CancellationToken cancellationToken)
    {
        string clientNonce = AutomationAuthentication.GenerateNonce();
        AutomationHelloRequest hello = new()
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            ClientInstanceId = options.ClientInstanceId,
            ClientName = options.ClientName,
            ClientVersion = options.ClientVersion,
            SupportedVersions = [AutomationProtocolConstants.CurrentVersion],
            ClientNonce = clientNonce,
            RequestedScopes = [.. options.RequestedScopes],
        };
        AutomationEnvelope helloRequest = CreateHandshakeRequest(
            AutomationProtocolConstants.HelloMethod,
            JsonSerializer.SerializeToElement(hello, AutomationJsonContext.Default.AutomationHelloRequest));
        await AutomationFrameCodec.WriteAsync(
            pipe,
            helloRequest,
            options.MaxFrameBytes,
            cancellationToken).ConfigureAwait(false);
        AutomationEnvelope helloResponse = await AutomationFrameCodec.ReadAsync(
            pipe,
            options.MaxFrameBytes,
            cancellationToken).ConfigureAwait(false);
        ThrowIfHandshakeError(helloRequest, helloResponse);

        AutomationHelloChallenge challenge = helloResponse.Payload?
            .Deserialize(AutomationJsonContext.Default.AutomationHelloChallenge)
            ?? throw new AutomationConnectionException("system.hello 没有返回 challenge。");
        if (challenge.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
            challenge.SelectedVersion is null || !IsBase64Sha256(challenge.ServerNonce) ||
            !IsBase64Sha256(challenge.ServerProof) || !IsValidScopeSet(challenge.SupportedScopes) ||
            challenge.MaxFrameBytes <= 0 ||
            !string.Equals(challenge.InstanceId, descriptor.InstanceId, StringComparison.Ordinal) ||
            challenge.SelectedVersion.Major != AutomationProtocolConstants.CurrentMajor ||
            challenge.SelectedVersion.Minor is < 0 or > AutomationProtocolConstants.CurrentMinor ||
            !string.Equals(challenge.AuthenticationAlgorithm, AutomationProtocolConstants.AuthenticationAlgorithm, StringComparison.Ordinal))
        {
            throw new AutomationConnectionException("Server hello identity/version/algorithm 与 discovery 或 Client 不一致。");
        }

        if (!AutomationAuthentication.VerifyServerProof(
                secret.Span,
                descriptor.InstanceId,
                options.ClientInstanceId,
                options.ClientName,
                options.ClientVersion,
                clientNonce,
                challenge.ServerNonce,
                challenge.SelectedVersion,
                options.RequestedScopes,
                challenge.SupportedScopes,
                challenge.MaxFrameBytes,
                challenge.ServerProof))
        {
            throw new AutomationConnectionException("Server hello HMAC proof 无效，Pipe 对端未证明持有 credential。");
        }

        string proof = AutomationAuthentication.ComputeProof(
            secret.Span,
            descriptor.InstanceId,
            options.ClientInstanceId,
            options.ClientName,
            options.ClientVersion,
            clientNonce,
            challenge.ServerNonce,
            challenge.SelectedVersion,
            options.RequestedScopes);
        AutomationAuthenticateRequest authentication = new()
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            ClientNonce = clientNonce,
            ServerNonce = challenge.ServerNonce,
            Proof = proof,
            RequestedScopes = [.. options.RequestedScopes],
        };
        AutomationEnvelope authenticationRequest = CreateHandshakeRequest(
            AutomationProtocolConstants.AuthenticateMethod,
            JsonSerializer.SerializeToElement(
                authentication,
                AutomationJsonContext.Default.AutomationAuthenticateRequest),
            challenge.SelectedVersion);
        await AutomationFrameCodec.WriteAsync(
            pipe,
            authenticationRequest,
            Math.Min(options.MaxFrameBytes, challenge.MaxFrameBytes),
            cancellationToken).ConfigureAwait(false);
        AutomationEnvelope authenticationResponse = await AutomationFrameCodec.ReadAsync(
            pipe,
            Math.Min(options.MaxFrameBytes, challenge.MaxFrameBytes),
            cancellationToken).ConfigureAwait(false);
        ThrowIfHandshakeError(authenticationRequest, authenticationResponse);

        AutomationSessionInfo session = authenticationResponse.Payload?
            .Deserialize(AutomationJsonContext.Default.AutomationSessionInfo)
            ?? throw new AutomationConnectionException("system.authenticate 没有返回 session。");
        string expectedPrincipalId = AutomationAuthentication.ComputePrincipalId(secret.Span);
        return session.SchemaVersion == AutomationProtocolConstants.WireSchemaVersion &&
            session.Protocol == challenge.SelectedVersion && IsValidScopeSet(session.GrantedScopes) &&
            !session.GrantedScopes.Except(options.RequestedScopes, StringComparer.Ordinal).Any() &&
            !session.GrantedScopes.Except(challenge.SupportedScopes, StringComparer.Ordinal).Any() &&
            IsBoundedText(session.SessionId, 128) &&
            string.Equals(session.PrincipalId, expectedPrincipalId, StringComparison.Ordinal) &&
            IsBoundedText(session.ServerVersion, 128)
            ? (challenge.SelectedVersion, session, Math.Min(options.MaxFrameBytes, challenge.MaxFrameBytes))
            : throw new AutomationConnectionException("Server session protocol/id 无效。");
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        AutomationConnectionException? connectionFailure = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                AutomationEnvelope envelope = await AutomationFrameCodec.ReadAsync(
                    _pipe,
                    _options.MaxFrameBytes,
                    cancellationToken).ConfigureAwait(false);
                if (envelope.Protocol != Protocol ||
                    !string.Equals(envelope.SessionId, Session.SessionId, StringComparison.Ordinal))
                {
                    throw new AutomationProtocolException("Automation response/event 的 protocol 或 session 无效。");
                }

                if (envelope.Kind == AutomationMessageKind.Response)
                {
                    if (envelope.CorrelationId is null)
                    {
                        throw new AutomationProtocolException("Automation response 缺少 correlationId。");
                    }

                    if (_pending.TryRemove(
                            envelope.CorrelationId,
                            out TaskCompletionSource<AutomationEnvelope>? completion))
                    {
                        _ = completion.TrySetResult(envelope);
                    }

                    continue;
                }

                if (envelope.Kind != AutomationMessageKind.Event ||
                    !string.Equals(
                        envelope.Method,
                        AutomationProtocolConstants.EventNotificationMethod,
                        StringComparison.Ordinal))
                {
                    throw new AutomationProtocolException("Server 发送了未知 automation envelope kind/method。");
                }

                AutomationEventRecord eventRecord = envelope.Payload?
                    .Deserialize(AutomationJsonContext.Default.AutomationEventRecord)
                    ?? throw new AutomationProtocolException("Automation event 缺少 record payload。");
                ValidateEventEnvelope(envelope, eventRecord);
                int eventBytes = envelope.Payload is { } eventPayload
                    ? Encoding.UTF8.GetByteCount(eventPayload.GetRawText())
                    : 0;
                long bufferedBytes = Interlocked.Add(ref _bufferedEventBytes, eventBytes);
                if (bufferedBytes > _options.MaxBufferedEventBytes ||
                    !_events.Writer.TryWrite(new BufferedClientEvent(eventRecord, eventBytes)))
                {
                    _ = Interlocked.Add(ref _bufferedEventBytes, -eventBytes);
                    throw new AutomationProtocolException(
                        "Automation Client event record/byte buffer 已满；连接将关闭，调用方必须 resume/replay。");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            connectionFailure = new AutomationConnectionException("Automation Server 连接已断开。", exception);
            FailPending(connectionFailure);
            try
            {
                _shutdown.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                _pipe.Dispose();
            }
            catch (Exception)
            {
                // 原始协议/连接错误才是所有 pending request 的权威失败原因。
            }
        }
        finally
        {
            _ = _events.Writer.TryComplete(connectionFailure);
        }
    }

    private static void ValidateEventEnvelope(
        AutomationEnvelope envelope,
        AutomationEventRecord eventRecord)
    {
        if (eventRecord.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
            !IsBoundedText(eventRecord.SubscriptionId, 128) || eventRecord.Sequence <= 0 ||
            !IsBoundedText(eventRecord.EventType, 128) || eventRecord.StateRevision is null ||
            envelope.Revision is null || !SameRevision(eventRecord.StateRevision, envelope.Revision))
        {
            throw new AutomationProtocolException("Automation event record/envelope revision 不一致或无效。");
        }
    }

    private static bool SameRevision(
        AutomationRevisionSnapshot left,
        AutomationRevisionSnapshot right)
    {
        if (left.SchemaVersion != right.SchemaVersion || left.GlobalRevision != right.GlobalRevision ||
            left.Resources is null || right.Resources is null || left.Resources.Length != right.Resources.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Resources.Length; i++)
        {
            AutomationResourceRevision? leftResource = left.Resources[i];
            AutomationResourceRevision? rightResource = right.Resources[i];
            if (leftResource is null || rightResource is null ||
                leftResource.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
                rightResource.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
                leftResource.Revision != rightResource.Revision ||
                leftResource.Revision < 0 ||
                !IsBoundedText(leftResource.ResourceId, AutomationProtocolConstants.MaxResourceIdLength) ||
                !string.Equals(leftResource.ResourceId, rightResource.ResourceId, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private async ValueTask WriteAsync(
        AutomationEnvelope envelope,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdown.Token);
        await _writeLock.WaitAsync(waitCancellation.Token).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _shutdown.Token.ThrowIfCancellationRequested();
            await AutomationFrameCodec.WriteAsync(
                _pipe,
                envelope,
                _options.MaxFrameBytes,
                _shutdown.Token).ConfigureAwait(false);
        }
        finally
        {
            _ = _writeLock.Release();
        }
    }

    private async ValueTask TrySendCancelAsync(string requestId)
    {
        try
        {
            using CancellationTokenSource timeout = new(_options.CancelSendTimeout, _options.TimeProvider);
            using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                timeout.Token,
                _shutdown.Token);
            AutomationCancelRequest cancel = new()
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                TargetRequestId = requestId,
            };
            AutomationEnvelope envelope = new()
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                Protocol = Protocol,
                MessageId = Guid.NewGuid().ToString("N"),
                Kind = AutomationMessageKind.Cancel,
                Method = AutomationProtocolConstants.CancelMethod,
                SessionId = Session.SessionId,
                Payload = JsonSerializer.SerializeToElement(
                    cancel,
                    AutomationJsonContext.Default.AutomationCancelRequest),
            };
            await WriteAsync(envelope, cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // protocol cancel 是 best effort；不得取代调用方原始 timeout/cancellation。
        }
    }

    private void FailPending(Exception exception)
    {
        foreach ((string requestId, TaskCompletionSource<AutomationEnvelope> completion) in _pending)
        {
            if (_pending.TryRemove(requestId, out _))
            {
                _ = completion.TrySetException(exception);
            }
        }
    }

    private static AutomationEnvelope CreateHandshakeRequest(
        string method,
        JsonElement payload,
        AutomationProtocolVersion? protocol = null)
    {
        string messageId = Guid.NewGuid().ToString("N");
        return new AutomationEnvelope
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Protocol = protocol ?? AutomationProtocolConstants.CurrentVersion,
            MessageId = messageId,
            Kind = AutomationMessageKind.Request,
            CorrelationId = messageId,
            Method = method,
            Payload = payload,
        };
    }

    private static void ThrowIfHandshakeError(
        AutomationEnvelope request,
        AutomationEnvelope response)
    {
        if (response.Kind != AutomationMessageKind.Response ||
            !string.Equals(response.CorrelationId, request.MessageId, StringComparison.Ordinal))
        {
            throw new AutomationConnectionException("Server handshake response correlation 无效。");
        }

        if (response.Error is not null)
        {
            throw new AutomationRemoteException(response.Error);
        }
    }

    private static AutomationClientOptions ValidateOptions(AutomationClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!IsBoundedText(options.ClientInstanceId, 128) ||
            !IsBoundedText(options.ClientName, 128) ||
            !IsBoundedText(options.ClientVersion, 128))
        {
            throw new ArgumentException("Automation client identity 长度或字符无效。", nameof(options));
        }

        ArgumentNullException.ThrowIfNull(options.RequestedScopes);
        if (options.RequestedScopes.Length > 32 ||
            options.RequestedScopes.Any(static scope => scope is not { Length: >= 1 and <= 64 } ||
                !char.IsAsciiLetter(scope[0]) || scope.Any(static character =>
                    !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not '-')) ||
            options.RequestedScopes.Distinct(StringComparer.Ordinal).Count() != options.RequestedScopes.Length)
        {
            throw new ArgumentException("Automation requested scope 集合无效、重复或超过上限。", nameof(options));
        }

        ArgumentNullException.ThrowIfNull(options.TimeProvider);
        ValidateTimeout(options.ConnectTimeout, nameof(options.ConnectTimeout));
        ValidateTimeout(options.RequestTimeout, nameof(options.RequestTimeout));
        ValidateTimeout(options.CancelSendTimeout, nameof(options.CancelSendTimeout));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxFrameBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            options.MaxFrameBytes,
            AutomationProtocolConstants.AbsoluteMaxFrameBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxBufferedEvents);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxBufferedEventBytes);
        return options with
        {
            RequestedScopes =
            [
                .. options.RequestedScopes
                    .Order(StringComparer.Ordinal),
            ],
        };
    }

    private static bool IsBoundedText(string? value, int maxLength)
    {
        return value is { Length: >= 1 } && value.Length <= maxLength &&
            !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);
    }

    private static AutomationInstanceDescriptor ValidateConnectInstance(AutomationDiscoveredInstance instance)
    {
        AutomationInstanceDescriptor? descriptor = instance.Descriptor;
        bool invalid = descriptor is null ||
            !string.Equals(descriptor.Schema, AutomationProtocolConstants.InstanceDescriptorSchema, StringComparison.Ordinal) ||
            !IsAsciiIdentifier(descriptor.InstanceId, 128) ||
            descriptor.ProcessId <= 0 ||
            descriptor.ProcessStartUtc == default ||
            descriptor.PublishedAtUtc == default ||
            !IsBoundedText(descriptor.EditorVersion, 128) ||
            descriptor.ProtocolVersions is not { Length: >= 1 and <= 16 } ||
            descriptor.ProtocolVersions.Any(static version => version is null || version.Major <= 0 || version.Minor < 0) ||
            descriptor.ProtocolVersions.Distinct().Count() != descriptor.ProtocolVersions.Length ||
            descriptor.Endpoint is null ||
            descriptor.Endpoint.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
            !Enum.IsDefined(descriptor.Endpoint.Kind) ||
            !IsBoundedText(descriptor.Endpoint.Address, 32767) ||
            (descriptor.Endpoint.Kind == AutomationTransportKind.WindowsNamedPipe &&
             !IsAsciiIdentifier(descriptor.Endpoint.Address, 128)) ||
            !IsBoundedText(instance.CredentialPath, 32767) ||
            descriptor.CapabilityDigest is not { Length: 64 } ||
            descriptor.CapabilityDigest.Any(static character => !char.IsAsciiHexDigit(character) || char.IsUpper(character)) ||
            !string.Equals(descriptor.LivenessMode, "processIdentity", StringComparison.Ordinal);
        return invalid
            ? throw new ArgumentException(
                "Automation discovered instance 未通过连接所需的 descriptor shape 校验。",
                nameof(instance))
            : descriptor!;
    }

    private static bool IsSemanticIdentifier(string? value, int maxLength)
    {
        return value is { Length: >= 1 } && value.Length <= maxLength &&
            char.IsAsciiLetter(value[0]) && value.All(static character =>
                char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
    }

    private static bool IsAsciiIdentifier(string? value, int maxLength)
    {
        return value is { Length: >= 1 } && value.Length <= maxLength && value.All(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    }

    private static bool IsValidScopeSet(string[]? scopes)
    {
        return scopes is { Length: <= 32 } &&
            scopes.All(static scope => scope is { Length: >= 1 and <= 64 } &&
                char.IsAsciiLetter(scope[0]) && scope.All(static character =>
                    char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')) &&
            scopes.Distinct(StringComparer.Ordinal).Count() == scopes.Length;
    }

    private static bool IsBase64Sha256(string? value)
    {
        if (value is not { Length: 44 })
        {
            return false;
        }

        Span<byte> decoded = stackalloc byte[32];
        return Convert.TryFromBase64String(value, decoded, out int written) && written == decoded.Length;
    }

    private static byte[] ReadCredential(string path)
    {
        string canonicalPath = Path.GetFullPath(path);
        RejectUnsafeCredentialPath(canonicalPath);
        using FileStream file = new(
            canonicalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: AutomationProtocolConstants.MaxCredentialFileBytes,
            FileOptions.SequentialScan);
        if (file.Length is <= 0 or > AutomationProtocolConstants.MaxCredentialFileBytes)
        {
            throw new InvalidDataException(
                $"Automation credential 必须是 1..{AutomationProtocolConstants.MaxCredentialFileBytes} 字节的普通文件。");
        }

        byte[] encoded = new byte[checked((int)file.Length)];
        try
        {
            file.ReadExactly(encoded);
            string text = Encoding.ASCII.GetString(encoded).Trim();
            byte[] secret = Convert.FromBase64String(text);
            if (secret.Length >= 32)
            {
                return secret;
            }

            CryptographicOperations.ZeroMemory(secret);
            throw new InvalidDataException("Automation credential 至少需要 256 bit。");
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("Automation credential 必须是 base64。", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encoded);
        }
    }

    private static void RejectUnsafeCredentialPath(string path)
    {
        string? root = Path.GetPathRoot(path);
        if (OperatingSystem.IsWindows() &&
            (path.StartsWith(@"\\", StringComparison.Ordinal) || root is null ||
             new DriveInfo(root).DriveType == DriveType.Network))
        {
            throw new InvalidDataException("Automation credential 拒绝 UNC/device path。");
        }

        string? current = path;
        while (current is not null)
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Automation credential path 包含 reparse point。");
            }

            current = Path.GetDirectoryName(current);
        }
    }

    private static void ValidateTimeout(TimeSpan timeout, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(timeout, TimeSpan.FromHours(24), parameterName);
    }

    private static void ValidateInvocationOptions(AutomationInvocationOptions options)
    {
        if ((options.IdempotencyKey is not null && !IsBoundedText(options.IdempotencyKey, 128)) ||
            (options.TransactionId is not null && !IsBoundedText(options.TransactionId, 128)))
        {
            throw new ArgumentException("Automation idempotencyKey/transactionId 长度或字符无效。", nameof(options));
        }
    }

    private static AutomationRevisionPrecondition? CloneAndValidateExpectedRevision(
        AutomationRevisionPrecondition? revision)
    {
        if (revision is null)
        {
            return null;
        }

        if (revision.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
            revision.GlobalRevision < 0 || revision.Resources is null ||
            revision.Resources.Length > AutomationProtocolConstants.MaxRevisionResources)
        {
            throw new ArgumentException("Automation expectedRevision schema 或数量无效。", nameof(revision));
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        AutomationExpectedResourceRevision[] resources =
            new AutomationExpectedResourceRevision[revision.Resources.Length];
        for (int i = 0; i < revision.Resources.Length; i++)
        {
            AutomationExpectedResourceRevision? resource = revision.Resources[i];
            if (resource is null ||
                resource.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
                resource.Revision < 0 ||
                !IsBoundedText(resource.ResourceId, AutomationProtocolConstants.MaxResourceIdLength) ||
                !ids.Add(resource.ResourceId))
            {
                throw new ArgumentException("Automation expectedRevision resource 无效或重复。", nameof(revision));
            }

            resources[i] = resource with { };
        }

        return revision with { Resources = resources };
    }

    private readonly record struct BufferedClientEvent(AutomationEventRecord Record, int Bytes);
}
