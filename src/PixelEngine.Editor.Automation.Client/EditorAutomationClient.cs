using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Client;

/// <summary>
/// 支持并发 correlation、deadline 与显式 cancel 的 .NET Editor automation Client。
/// </summary>
public sealed class EditorAutomationClient : IAsyncDisposable
{
    private readonly AutomationClientOptions _options;
    private readonly NamedPipeClientStream _pipe;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<AutomationEnvelope>> _pending =
        new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _reader;
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
        AutomationInstanceDescriptor descriptor = instance.Descriptor;
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        TimeSpan effectiveTimeout = timeout ?? _options.RequestTimeout;
        ValidateTimeout(effectiveTimeout, nameof(timeout));

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
            Payload = payload,
        };

        try
        {
            await WriteAsync(request, cancellationToken).ConfigureAwait(false);
            AutomationEnvelope response;
            try
            {
                response = await completion.Task
                    .WaitAsync(effectiveTimeout + TimeSpan.FromSeconds(1), cancellationToken)
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
                ? response.Payload
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
        JsonElement payload = JsonSerializer.SerializeToElement(request, requestTypeInfo);
        JsonElement? response = await InvokeRawAsync(method, payload, timeout, cancellationToken)
            .ConfigureAwait(false);
        JsonElement responsePayload = response
            ?? throw new AutomationConnectionException($"Automation method '{method}' 没有返回所需 payload。");

        try
        {
            return responsePayload.Deserialize(responseTypeInfo)
                ?? throw new AutomationConnectionException($"Automation method '{method}' 返回 null payload。");
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

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _shutdown.Cancel();
        await _pipe.DisposeAsync().ConfigureAwait(false);
        try
        {
            await _reader.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }

        AutomationConnectionException exception = new("Automation Client 已关闭。");
        FailPending(exception);
        _writeLock.Dispose();
        _shutdown.Dispose();
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
            challenge.SelectedVersion is null || string.IsNullOrWhiteSpace(challenge.ServerNonce) ||
            challenge.SupportedScopes is null || challenge.MaxFrameBytes <= 0 ||
            !string.Equals(challenge.InstanceId, descriptor.InstanceId, StringComparison.Ordinal) ||
            challenge.SelectedVersion.Major != AutomationProtocolConstants.CurrentMajor ||
            !string.Equals(challenge.AuthenticationAlgorithm, AutomationProtocolConstants.AuthenticationAlgorithm, StringComparison.Ordinal))
        {
            throw new AutomationConnectionException("Server hello identity/version/algorithm 与 discovery 或 Client 不一致。");
        }

        string proof = AutomationAuthentication.ComputeProof(
            secret.Span,
            descriptor.InstanceId,
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
        return session.SchemaVersion == AutomationProtocolConstants.WireSchemaVersion &&
            session.Protocol == challenge.SelectedVersion && session.GrantedScopes is not null &&
            !session.GrantedScopes.Except(options.RequestedScopes, StringComparer.Ordinal).Any() &&
            !string.IsNullOrWhiteSpace(session.SessionId) && !string.IsNullOrWhiteSpace(session.ServerVersion)
            ? (challenge.SelectedVersion, session, Math.Min(options.MaxFrameBytes, challenge.MaxFrameBytes))
            : throw new AutomationConnectionException("Server session protocol/id 无效。");
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
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

                if (envelope.Kind == AutomationMessageKind.Response &&
                    envelope.CorrelationId is not null &&
                    _pending.TryRemove(envelope.CorrelationId, out TaskCompletionSource<AutomationEnvelope>? completion))
                {
                    _ = completion.TrySetResult(envelope);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException or AutomationProtocolException or ObjectDisposedException)
        {
            FailPending(new AutomationConnectionException("Automation Server 连接已断开。", exception));
        }
    }

    private async ValueTask WriteAsync(
        AutomationEnvelope envelope,
        CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AutomationFrameCodec.WriteAsync(
                _pipe,
                envelope,
                _options.MaxFrameBytes,
                cancellationToken).ConfigureAwait(false);
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
            await WriteAsync(envelope, _shutdown.Token).ConfigureAwait(false);
        }
        catch (Exception) when (_shutdown.IsCancellationRequested || !_pipe.IsConnected)
        {
        }
        catch (IOException)
        {
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
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ClientName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ClientVersion);
        ArgumentNullException.ThrowIfNull(options.RequestedScopes);
        ArgumentNullException.ThrowIfNull(options.TimeProvider);
        ValidateTimeout(options.ConnectTimeout, nameof(options.ConnectTimeout));
        ValidateTimeout(options.RequestTimeout, nameof(options.RequestTimeout));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxFrameBytes);
        return options with
        {
            RequestedScopes =
            [
                .. options.RequestedScopes
                    .Where(static scope => !string.IsNullOrWhiteSpace(scope))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal),
            ],
        };
    }

    private static byte[] ReadCredential(string path)
    {
        string canonicalPath = Path.GetFullPath(path);
        FileInfo file = new(canonicalPath);
        if (!file.Exists || file.Length is <= 0 or > AutomationProtocolConstants.MaxCredentialFileBytes ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"Automation credential 必须是 1..{AutomationProtocolConstants.MaxCredentialFileBytes} 字节的普通文件。");
        }

        string text = File.ReadAllText(canonicalPath).Trim();
        try
        {
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
    }

    private static void ValidateTimeout(TimeSpan timeout, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(timeout, TimeSpan.FromHours(24), parameterName);
    }
}
