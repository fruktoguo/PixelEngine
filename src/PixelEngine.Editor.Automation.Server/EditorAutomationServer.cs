using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Server;

/// <summary>
/// 事件驱动、current-user-only 的 Windows Named Pipe Editor automation Server。
/// </summary>
public sealed class EditorAutomationServer : IAsyncDisposable
{
    private readonly AutomationServerOptions _options;
    private readonly IAutomationRequestHandler _handler;
    private readonly AutomationDiscoveryPublisher _discovery;
    private readonly byte[] _secret;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<int, Task> _connections = new();
    private readonly SemaphoreSlim _connectionSlots;
    private Task? _acceptLoop;
    private int _connectionSequence;
    private int _started;
    private int _disposed;

    /// <summary>
    /// 创建 Server；调用 <see cref="StartAsync" /> 后才发布 discovery descriptor。
    /// </summary>
    /// <param name="options">Server 配置。</param>
    /// <param name="handler">Editor semantic request handler；为空时只开放 system methods。</param>
    public EditorAutomationServer(
        AutomationServerOptions options,
        IAutomationRequestHandler? handler = null)
    {
        _options = ValidateOptions(options);
        _handler = handler ?? EmptyAutomationRequestHandler.Instance;
        _discovery = new AutomationDiscoveryPublisher(_options.DiscoveryRoot);
        _connectionSlots = new SemaphoreSlim(_options.MaxConnections, _options.MaxConnections);

        InstanceId = string.IsNullOrWhiteSpace(_options.InstanceId)
            ? Guid.NewGuid().ToString("N")
            : _options.InstanceId;
        PipeName = string.IsNullOrWhiteSpace(_options.PipeName)
            ? $"pixelengine-editor-v1-{InstanceId}"
            : _options.PipeName;
        ValidateIdentifier(InstanceId, nameof(options.InstanceId));
        ValidatePipeName(PipeName);

        _secret = LoadOrCreateSecret(_options.CredentialInputPath);
        Descriptor = CreateDescriptor();
    }

    /// <summary>本次进程不可复用的实例 id。</summary>
    public string InstanceId { get; }

    /// <summary>Named Pipe name。</summary>
    public string PipeName { get; }

    /// <summary>当前发布的 discovery descriptor。</summary>
    public AutomationInstanceDescriptor Descriptor { get; private set; }

    /// <summary>
    /// 启动 accept loop 并原子发布 credential 与 descriptor。
    /// </summary>
    /// <param name="cancellationToken">启动取消令牌。</param>
    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("AUTO-001 v1 Server 当前只发布 Windows Named Pipe；Unix Domain Socket 仅保留 wire 值。");
        }

        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("Automation Server 已启动。");
        }

        try
        {
            _acceptLoop = AcceptLoopAsync(_shutdown.Token);
            await _discovery.WriteCredentialAsync(InstanceId, _secret, cancellationToken).ConfigureAwait(false);
            await _discovery.PublishAsync(Descriptor, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _shutdown.Cancel();
            _discovery.Remove(InstanceId);
            throw;
        }
    }

    /// <summary>
    /// 原子更新 discovery 中的项目摘要与 capability digest。
    /// </summary>
    /// <param name="project">当前项目摘要。</param>
    /// <param name="capabilityDigest">64 位小写 SHA256。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async ValueTask UpdateDescriptorAsync(
        AutomationProjectSummary? project,
        string capabilityDigest,
        CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        ValidateSha256(capabilityDigest, nameof(capabilityDigest));
        AutomationInstanceDescriptor next = Descriptor with
        {
            Project = project,
            CapabilityDigest = capabilityDigest,
            PublishedAtUtc = _options.TimeProvider.GetUtcNow(),
        };
        await _discovery.PublishAsync(next, cancellationToken).ConfigureAwait(false);
        Descriptor = next;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _shutdown.Cancel();
        if (_acceptLoop is not null)
        {
            await IgnoreCancellationAsync(_acceptLoop).ConfigureAwait(false);
        }

        Task[] connections = [.. _connections.Values];
        if (connections.Length != 0)
        {
            await Task.WhenAll(connections).ConfigureAwait(false);
        }

        _discovery.Remove(InstanceId);
        CryptographicOperations.ZeroMemory(_secret);
        _connectionSlots.Dispose();
        _shutdown.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await _connectionSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                int connectionId = Interlocked.Increment(ref _connectionSequence);
                Task task = HandleConnectionAndReleaseSlotAsync(pipe, cancellationToken);
                pipe = null;
                _connections[connectionId] = task;
                _ = ObserveConnectionAsync(connectionId, task);
            }
            catch
            {
                if (pipe is not null)
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                }

                _ = _connectionSlots.Release();
                throw;
            }
        }
    }

    private NamedPipeServerStream CreatePipe()
    {
        return new NamedPipeServerStream(
            PipeName,
            PipeDirection.InOut,
            _options.MaxConnections,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            inBufferSize: 0,
            outBufferSize: 0);
    }

    private async Task ObserveConnectionAsync(int connectionId, Task connection)
    {
        try
        {
            await connection.ConfigureAwait(false);
        }
        catch (Exception) when (_shutdown.IsCancellationRequested)
        {
        }
        finally
        {
            _ = _connections.TryRemove(connectionId, out _);
        }
    }

    private async Task HandleConnectionAndReleaseSlotAsync(
        NamedPipeServerStream pipe,
        CancellationToken serverCancellation)
    {
        try
        {
            await HandleConnectionAndDisposeAsync(pipe, serverCancellation).ConfigureAwait(false);
        }
        finally
        {
            _ = _connectionSlots.Release();
        }
    }

    private async Task HandleConnectionAndDisposeAsync(
        NamedPipeServerStream pipe,
        CancellationToken serverCancellation)
    {
        await using (pipe.ConfigureAwait(false))
        using (CancellationTokenSource connectionShutdown =
               CancellationTokenSource.CreateLinkedTokenSource(serverCancellation))
        using (CancellationTokenSource handshakeCancellation =
               CancellationTokenSource.CreateLinkedTokenSource(connectionShutdown.Token))
        using (SemaphoreSlim writeLock = new(1, 1))
        using (SemaphoreSlim requestSlots = new(_options.MaxConcurrentRequestsPerConnection))
        {
            handshakeCancellation.CancelAfter(_options.HandshakeTimeout);
            ConnectionState state = new();
            ConcurrentDictionary<string, CancellationTokenSource> activeRequests = new(StringComparer.Ordinal);
            TaskCompletionSource requestsDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                while (!connectionShutdown.IsCancellationRequested && pipe.IsConnected)
                {
                    CancellationToken readCancellation = state.SessionId is null
                        ? handshakeCancellation.Token
                        : connectionShutdown.Token;
                    AutomationEnvelope envelope = await AutomationFrameCodec.ReadAsync(
                        pipe,
                        _options.MaxFrameBytes,
                        readCancellation).ConfigureAwait(false);

                    if (envelope.Kind == AutomationMessageKind.Cancel)
                    {
                        await HandleCancelAsync(pipe, writeLock, state, envelope, activeRequests, connectionShutdown.Token)
                            .ConfigureAwait(false);
                        continue;
                    }

                    if (envelope.Kind != AutomationMessageKind.Request)
                    {
                        await WriteErrorAsync(
                            pipe,
                            writeLock,
                            envelope,
                            CreateError(
                                AutomationErrorCodes.InvalidRequest,
                                AutomationErrorCategory.Validation,
                                "客户端只能发送 Request 或 Cancel envelope。"),
                            connectionShutdown.Token).ConfigureAwait(false);
                        continue;
                    }

                    if (state.SessionId is null)
                    {
                        bool keepOpen;
                        try
                        {
                            keepOpen = await HandleHandshakeAsync(
                                pipe,
                                writeLock,
                                state,
                                envelope,
                                connectionShutdown.Token).ConfigureAwait(false);
                        }
                        catch (AutomationRequestException exception)
                        {
                            await WriteErrorAsync(
                                pipe,
                                writeLock,
                                envelope,
                                exception.Error,
                                connectionShutdown.Token).ConfigureAwait(false);
                            keepOpen = true;
                        }

                        if (!keepOpen)
                        {
                            break;
                        }

                        if (state.SessionId is not null)
                        {
                            handshakeCancellation.CancelAfter(Timeout.InfiniteTimeSpan);
                        }

                        continue;
                    }

                    if (!requestSlots.Wait(0))
                    {
                        await WriteErrorAsync(
                            pipe,
                            writeLock,
                            envelope,
                            CreateError(
                                AutomationErrorCodes.Busy,
                                AutomationErrorCategory.Availability,
                                "该连接的并发请求额度已满。",
                                transient: true,
                                retryAfterMilliseconds: 25),
                            connectionShutdown.Token).ConfigureAwait(false);
                        continue;
                    }

                    CancellationTokenSource requestCancellation =
                        CancellationTokenSource.CreateLinkedTokenSource(connectionShutdown.Token);
                    if (!activeRequests.TryAdd(envelope.MessageId, requestCancellation))
                    {
                        requestCancellation.Dispose();
                        _ = requestSlots.Release();
                        await WriteErrorAsync(
                            pipe,
                            writeLock,
                            envelope,
                            CreateError(
                                AutomationErrorCodes.InvalidRequest,
                                AutomationErrorCategory.Conflict,
                                "同一连接内 request message id 必须唯一。"),
                            connectionShutdown.Token).ConfigureAwait(false);
                        continue;
                    }

                    _ = ProcessRequestAndReleaseAsync(
                        pipe,
                        writeLock,
                        requestSlots,
                        state,
                        envelope,
                        requestCancellation,
                        activeRequests,
                        requestsDrained,
                        connectionShutdown.Token);
                }
            }
            catch (EndOfStreamException)
            {
            }
            catch (IOException)
            {
            }
            catch (AutomationProtocolException)
            {
            }
            catch (ObjectDisposedException) when (connectionShutdown.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException) when (connectionShutdown.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException) when (handshakeCancellation.IsCancellationRequested)
            {
            }
            finally
            {
                connectionShutdown.Cancel();
                foreach (CancellationTokenSource request in activeRequests.Values)
                {
                    request.Cancel();
                }

                if (activeRequests.IsEmpty)
                {
                    _ = requestsDrained.TrySetResult();
                }

                await requestsDrained.Task.ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> HandleHandshakeAsync(
        Stream pipe,
        SemaphoreSlim writeLock,
        ConnectionState state,
        AutomationEnvelope request,
        CancellationToken cancellationToken)
    {
        if (string.Equals(request.Method, AutomationProtocolConstants.HelloMethod, StringComparison.Ordinal))
        {
            AutomationHelloRequest? hello = DeserializePayload(
                request,
                AutomationJsonContext.Default.AutomationHelloRequest);
            if (hello is null || hello.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
                string.IsNullOrWhiteSpace(hello.ClientName) ||
                string.IsNullOrWhiteSpace(hello.ClientVersion) || string.IsNullOrWhiteSpace(hello.ClientNonce) ||
                hello.SupportedVersions is null || hello.SupportedVersions.Length == 0 ||
                hello.SupportedVersions.Any(static version => version is null) || hello.RequestedScopes is null)
            {
                await WriteErrorAsync(
                    pipe,
                    writeLock,
                    request,
                    CreateError(
                        AutomationErrorCodes.InvalidRequest,
                        AutomationErrorCategory.Validation,
                        "system.hello payload 不完整。"),
                    cancellationToken).ConfigureAwait(false);
                return true;
            }

            AutomationProtocolVersion? selected = SelectVersion(hello.SupportedVersions);
            if (selected is null)
            {
                await WriteErrorAsync(
                    pipe,
                    writeLock,
                    request,
                    CreateError(
                        AutomationErrorCodes.ProtocolVersionUnsupported,
                        AutomationErrorCategory.Validation,
                        "客户端与 Server 没有共同协议 major 版本。"),
                    cancellationToken).ConfigureAwait(false);
                return false;
            }

            state.ClientName = hello.ClientName;
            state.ClientNonce = hello.ClientNonce;
            state.ServerNonce = AutomationAuthentication.GenerateNonce();
            state.SelectedVersion = selected;
            state.HelloRequestedScopes = [.. hello.RequestedScopes.Distinct(StringComparer.Ordinal)];

            AutomationHelloChallenge challenge = new()
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                InstanceId = InstanceId,
                SelectedVersion = selected,
                ServerNonce = state.ServerNonce,
                SupportedScopes = [.. _options.SupportedScopes],
                AuthenticationAlgorithm = AutomationProtocolConstants.AuthenticationAlgorithm,
                MaxFrameBytes = _options.MaxFrameBytes,
            };
            await WritePayloadAsync(
                pipe,
                writeLock,
                request,
                JsonSerializer.SerializeToElement(
                    challenge,
                    AutomationJsonContext.Default.AutomationHelloChallenge),
                selected,
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (string.Equals(request.Method, AutomationProtocolConstants.AuthenticateMethod, StringComparison.Ordinal))
        {
            if (state.SelectedVersion is null || state.ClientNonce is null || state.ServerNonce is null)
            {
                await WriteErrorAsync(
                    pipe,
                    writeLock,
                    request,
                    CreateError(
                        AutomationErrorCodes.AuthenticationRequired,
                        AutomationErrorCategory.Authentication,
                        "必须先完成 system.hello。"),
                    cancellationToken).ConfigureAwait(false);
                return false;
            }

            AutomationAuthenticateRequest? authentication = DeserializePayload(
                request,
                AutomationJsonContext.Default.AutomationAuthenticateRequest);
            bool noncesMatch = authentication is not null &&
                authentication.SchemaVersion == AutomationProtocolConstants.WireSchemaVersion &&
                authentication.RequestedScopes is not null &&
                !string.IsNullOrWhiteSpace(authentication.Proof) &&
                string.Equals(authentication.ClientNonce, state.ClientNonce, StringComparison.Ordinal) &&
                string.Equals(authentication.ServerNonce, state.ServerNonce, StringComparison.Ordinal);
            bool proofValid = noncesMatch && AutomationAuthentication.VerifyProof(
                _secret,
                InstanceId,
                state.ClientNonce,
                state.ServerNonce,
                state.SelectedVersion,
                authentication!.RequestedScopes!,
                authentication!.Proof);
            if (!proofValid)
            {
                await WriteErrorAsync(
                    pipe,
                    writeLock,
                    request,
                    CreateError(
                        AutomationErrorCodes.AuthenticationFailed,
                        AutomationErrorCategory.Authentication,
                        "Automation challenge proof 无效。"),
                    cancellationToken).ConfigureAwait(false);
                return false;
            }

            string[] grantedScopes =
            [
                .. authentication!.RequestedScopes!
                    .Intersect(state.HelloRequestedScopes, StringComparer.Ordinal)
                    .Intersect(_options.SupportedScopes, StringComparer.Ordinal)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal),
            ];
            state.SessionId = Guid.NewGuid().ToString("N");
            state.GrantedScopes = grantedScopes;

            AutomationSessionInfo session = new()
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                SessionId = state.SessionId,
                GrantedScopes = grantedScopes,
                ServerVersion = _options.EditorVersion,
                Protocol = state.SelectedVersion,
            };
            await WritePayloadAsync(
                pipe,
                writeLock,
                request,
                JsonSerializer.SerializeToElement(session, AutomationJsonContext.Default.AutomationSessionInfo),
                state.SelectedVersion,
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        await WriteErrorAsync(
            pipe,
            writeLock,
            request,
            CreateError(
                AutomationErrorCodes.AuthenticationRequired,
                AutomationErrorCategory.Authentication,
                "连接认证前只允许 system.hello 与 system.authenticate。"),
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task ProcessRequestAndReleaseAsync(
        Stream pipe,
        SemaphoreSlim writeLock,
        SemaphoreSlim requestSlots,
        ConnectionState state,
        AutomationEnvelope request,
        CancellationTokenSource requestCancellation,
        ConcurrentDictionary<string, CancellationTokenSource> activeRequests,
        TaskCompletionSource requestsDrained,
        CancellationToken connectionCancellation)
    {
        try
        {
            ApplyDeadline(request, requestCancellation);
            await ProcessAuthenticatedRequestAsync(
                pipe,
                writeLock,
                state,
                request,
                requestCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AutomationError error = request.DeadlineUtc is not null &&
                request.DeadlineUtc <= _options.TimeProvider.GetUtcNow()
                ? CreateError(
                    AutomationErrorCodes.DeadlineExceeded,
                    AutomationErrorCategory.Cancellation,
                    "Automation request deadline 已超过。")
                : CreateError(
                    AutomationErrorCodes.Cancelled,
                    AutomationErrorCategory.Cancellation,
                    "Automation request 已取消。");
            await TryWriteErrorAsync(pipe, writeLock, request, error).ConfigureAwait(false);
        }
        catch (AutomationRequestException exception)
        {
            await TryWriteErrorAsync(pipe, writeLock, request, exception.Error).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await TryWriteErrorAsync(
                pipe,
                writeLock,
                request,
                CreateError(
                    AutomationErrorCodes.Internal,
                    AutomationErrorCategory.Internal,
                    $"Automation handler 失败：{exception.GetType().Name}。"))
                .ConfigureAwait(false);
        }
        finally
        {
            _ = activeRequests.TryRemove(request.MessageId, out _);
            requestCancellation.Dispose();
            _ = requestSlots.Release();
            if (connectionCancellation.IsCancellationRequested && activeRequests.IsEmpty)
            {
                _ = requestsDrained.TrySetResult();
            }
        }
    }

    private async Task ProcessAuthenticatedRequestAsync(
        Stream pipe,
        SemaphoreSlim writeLock,
        ConnectionState state,
        AutomationEnvelope request,
        CancellationToken cancellationToken)
    {
        if (request.Protocol != state.SelectedVersion ||
            !string.Equals(request.SessionId, state.SessionId, StringComparison.Ordinal))
        {
            throw new AutomationRequestException(CreateError(
                AutomationErrorCodes.AuthenticationRequired,
                AutomationErrorCategory.Authentication,
                "request 的 protocol/session 与已认证连接不一致。"));
        }

        string method = request.Method ?? string.Empty;
        JsonElement? payload;
        if (string.Equals(method, AutomationProtocolConstants.PingMethod, StringComparison.Ordinal))
        {
            payload = JsonSerializer.SerializeToElement(
                new AutomationPingResponse
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    InstanceId = InstanceId,
                    ServerTimeUtc = _options.TimeProvider.GetUtcNow(),
                },
                AutomationJsonContext.Default.AutomationPingResponse);
        }
        else if (string.Equals(method, AutomationProtocolConstants.DescribeMethod, StringComparison.Ordinal))
        {
            RequireScopes(state, request, [AutomationScopes.EditorRead]);
            payload = JsonSerializer.SerializeToElement(
                Descriptor,
                AutomationJsonContext.Default.AutomationInstanceDescriptor);
        }
        else
        {
            if (!_handler.TryGetDescriptor(method, out AutomationMethodDescriptor descriptor))
            {
                throw new AutomationRequestException(CreateError(
                    AutomationErrorCodes.MethodNotFound,
                    AutomationErrorCategory.Validation,
                    $"Automation method '{method}' 不存在。"));
            }

            RequireScopes(state, request, descriptor.RequiredScopes);
            AutomationRequestContext context = new(
                request.MessageId,
                request.CorrelationId ?? request.MessageId,
                state.SessionId!,
                state.ClientName ?? "unknown",
                state.GrantedScopes,
                request.DeadlineUtc);
            payload = await _handler.HandleAsync(context, method, request.Payload, cancellationToken)
                .ConfigureAwait(false);
        }

        await WritePayloadAsync(
            pipe,
            writeLock,
            request,
            payload,
            state.SelectedVersion!,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleCancelAsync(
        Stream pipe,
        SemaphoreSlim writeLock,
        ConnectionState state,
        AutomationEnvelope request,
        ConcurrentDictionary<string, CancellationTokenSource> activeRequests,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Method, AutomationProtocolConstants.CancelMethod, StringComparison.Ordinal))
        {
            await WriteErrorAsync(
                pipe,
                writeLock,
                request,
                CreateError(
                    AutomationErrorCodes.InvalidRequest,
                    AutomationErrorCategory.Validation,
                    "Cancel envelope 的 method 必须是 system.cancel。"),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (state.SessionId is null || !string.Equals(request.SessionId, state.SessionId, StringComparison.Ordinal))
        {
            await WriteErrorAsync(
                pipe,
                writeLock,
                request,
                CreateError(
                    AutomationErrorCodes.AuthenticationRequired,
                    AutomationErrorCategory.Authentication,
                    "取消请求需要已认证 session。"),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        AutomationCancelRequest? cancel = DeserializePayload(
            request,
            AutomationJsonContext.Default.AutomationCancelRequest);
        if (cancel is null || cancel.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
            string.IsNullOrWhiteSpace(cancel.TargetRequestId))
        {
            await WriteErrorAsync(
                pipe,
                writeLock,
                request,
                CreateError(
                    AutomationErrorCodes.InvalidRequest,
                    AutomationErrorCategory.Validation,
                    "Cancel payload 必须包含 targetRequestId。"),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (activeRequests.TryGetValue(cancel.TargetRequestId, out CancellationTokenSource? target))
        {
            target.Cancel();
        }

        await WritePayloadAsync(
            pipe,
            writeLock,
            request,
            JsonSerializer.SerializeToElement(
                new AutomationCancelRequest
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    TargetRequestId = cancel.TargetRequestId,
                },
                AutomationJsonContext.Default.AutomationCancelRequest),
            state.SelectedVersion ?? AutomationProtocolConstants.CurrentVersion,
            cancellationToken).ConfigureAwait(false);
    }

    private void RequireScopes(
        ConnectionState state,
        AutomationEnvelope request,
        IEnumerable<string> requiredScopes)
    {
        string[] missing =
        [
            .. requiredScopes
                .Except(state.GrantedScopes, StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        if (missing.Length == 0)
        {
            return;
        }

        JsonElement details = JsonSerializer.SerializeToElement(
            missing,
            AutomationJsonContext.Default.StringArray);
        throw new AutomationRequestException(new AutomationError
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Code = AutomationErrorCodes.PermissionDenied,
            Category = AutomationErrorCategory.Authorization,
            Message = $"Automation method '{request.Method}' 缺少所需 scope。",
            Details = details,
            Transient = false,
            CorrelationId = request.CorrelationId ?? request.MessageId,
        });
    }

    private void ApplyDeadline(AutomationEnvelope request, CancellationTokenSource cancellation)
    {
        if (request.DeadlineUtc is null)
        {
            return;
        }

        TimeSpan remaining = request.DeadlineUtc.Value - _options.TimeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            cancellation.Cancel();
            return;
        }

        if (remaining > _options.MaxRequestLifetime)
        {
            throw new AutomationRequestException(CreateError(
                AutomationErrorCodes.InvalidRequest,
                AutomationErrorCategory.Validation,
                $"Automation request deadline 不得超过 {_options.MaxRequestLifetime}。"));
        }

        cancellation.CancelAfter(remaining);
    }

    private async ValueTask WritePayloadAsync(
        Stream pipe,
        SemaphoreSlim writeLock,
        AutomationEnvelope request,
        JsonElement? payload,
        AutomationProtocolVersion protocol,
        CancellationToken cancellationToken)
    {
        AutomationEnvelope response = new()
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Protocol = protocol,
            MessageId = Guid.NewGuid().ToString("N"),
            Kind = AutomationMessageKind.Response,
            CorrelationId = request.MessageId,
            Method = request.Method,
            SessionId = request.SessionId,
            Payload = payload,
        };
        await WriteEnvelopeAsync(pipe, writeLock, response, cancellationToken).ConfigureAwait(false);
    }

    private ValueTask WriteErrorAsync(
        Stream pipe,
        SemaphoreSlim writeLock,
        AutomationEnvelope request,
        AutomationError error,
        CancellationToken cancellationToken)
    {
        AutomationEnvelope response = new()
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Protocol = request.Protocol,
            MessageId = Guid.NewGuid().ToString("N"),
            Kind = AutomationMessageKind.Response,
            CorrelationId = request.MessageId,
            Method = request.Method,
            SessionId = request.SessionId,
            Error = error with { CorrelationId = error.CorrelationId ?? request.CorrelationId ?? request.MessageId },
        };
        return WriteEnvelopeAsync(pipe, writeLock, response, cancellationToken);
    }

    private async ValueTask TryWriteErrorAsync(
        Stream pipe,
        SemaphoreSlim writeLock,
        AutomationEnvelope request,
        AutomationError error)
    {
        try
        {
            await WriteErrorAsync(pipe, writeLock, request, error, _shutdown.Token).ConfigureAwait(false);
        }
        catch (Exception) when (_shutdown.IsCancellationRequested || !pipe.CanWrite)
        {
        }
        catch (IOException)
        {
        }
    }

    private async ValueTask WriteEnvelopeAsync(
        Stream pipe,
        SemaphoreSlim writeLock,
        AutomationEnvelope envelope,
        CancellationToken cancellationToken)
    {
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AutomationFrameCodec.WriteAsync(
                pipe,
                envelope,
                _options.MaxFrameBytes,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = writeLock.Release();
        }
    }

    private AutomationInstanceDescriptor CreateDescriptor()
    {
        using Process process = Process.GetCurrentProcess();
        DateTimeOffset processStart = process.StartTime.ToUniversalTime();
        string capabilityDigest = string.IsNullOrWhiteSpace(_options.CapabilityDigest)
            ? AutomationDiscoveryPublisher.ComputeSystemCapabilityDigest()
            : _options.CapabilityDigest;
        ValidateSha256(capabilityDigest, nameof(_options.CapabilityDigest));

        return new AutomationInstanceDescriptor
        {
            Schema = AutomationProtocolConstants.InstanceDescriptorSchema,
            InstanceId = InstanceId,
            ProcessId = Environment.ProcessId,
            ProcessStartUtc = processStart,
            PublishedAtUtc = _options.TimeProvider.GetUtcNow(),
            EditorVersion = _options.EditorVersion,
            ProtocolVersions = [AutomationProtocolConstants.CurrentVersion],
            Endpoint = new AutomationEndpointDescriptor
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                Kind = AutomationTransportKind.WindowsNamedPipe,
                Address = PipeName,
            },
            CredentialPath = _discovery.GetCredentialPath(InstanceId),
            CapabilityDigest = capabilityDigest,
            LivenessMode = "processIdentity",
            Project = _options.Project,
        };
    }

    private static AutomationServerOptions ValidateOptions(AutomationServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DiscoveryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.EditorVersion);
        ArgumentNullException.ThrowIfNull(options.SupportedScopes);
        ArgumentNullException.ThrowIfNull(options.TimeProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxFrameBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxConcurrentRequestsPerConnection);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxConnections, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaxConnections, 254);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.HandshakeTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.HandshakeTimeout, TimeSpan.FromMinutes(5));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.MaxRequestLifetime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaxRequestLifetime, TimeSpan.FromDays(7));

        AutomationProjectSummary? project = options.Project;
        if (project is not null)
        {
            if (project.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
                string.IsNullOrWhiteSpace(project.ProjectId) || string.IsNullOrWhiteSpace(project.Name) ||
                string.IsNullOrWhiteSpace(project.RootPath))
            {
                throw new ArgumentException("Automation project summary 不完整或 schema 不受支持。", nameof(options));
            }

            project = project with { RootPath = Path.GetFullPath(project.RootPath) };
        }

        return options with
        {
            DiscoveryRoot = Path.GetFullPath(options.DiscoveryRoot),
            Project = project,
            SupportedScopes =
            [
                .. options.SupportedScopes
                    .Where(static scope => !string.IsNullOrWhiteSpace(scope))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal),
            ],
        };
    }

    private static byte[] LoadOrCreateSecret(string? inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return AutomationAuthentication.GenerateSecret();
        }

        string path = Path.GetFullPath(inputPath);
        FileInfo file = new(path);
        if (!file.Exists || file.Length is <= 0 or > AutomationProtocolConstants.MaxCredentialFileBytes ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"Automation credential input 必须是 1..{AutomationProtocolConstants.MaxCredentialFileBytes} 字节的普通文件。");
        }

        string text = File.ReadAllText(path).Trim();
        byte[] secret;
        try
        {
            secret = Convert.FromBase64String(text);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("Automation credential input 必须是 base64。", exception);
        }

        if (secret.Length < 32)
        {
            CryptographicOperations.ZeroMemory(secret);
            throw new InvalidDataException("Automation credential input 至少需要 256 bit。");
        }

        return secret;
    }

    private static AutomationProtocolVersion? SelectVersion(IEnumerable<AutomationProtocolVersion> versions)
    {
        return versions
            .Where(static version => version is not null)
            .Where(static version => version.Major == AutomationProtocolConstants.CurrentMajor)
            .OrderByDescending(static version => version.Minor)
            .Select(static version => new AutomationProtocolVersion(
                AutomationProtocolConstants.CurrentMajor,
                Math.Min(version.Minor, AutomationProtocolConstants.CurrentMinor)))
            .FirstOrDefault();
    }

    private static T? DeserializePayload<T>(
        AutomationEnvelope envelope,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        if (envelope.Payload is null)
        {
            return default;
        }

        try
        {
            return envelope.Payload.Value.Deserialize(typeInfo);
        }
        catch (JsonException exception)
        {
            throw new AutomationRequestException(new AutomationError
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                Code = AutomationErrorCodes.InvalidRequest,
                Category = AutomationErrorCategory.Validation,
                Message = $"Automation payload JSON 与 method schema 不匹配：{exception.Message}",
                Transient = false,
                CorrelationId = envelope.CorrelationId ?? envelope.MessageId,
            });
        }
    }

    private static AutomationError CreateError(
        string code,
        AutomationErrorCategory category,
        string message,
        bool transient = false,
        int? retryAfterMilliseconds = null)
    {
        return new AutomationError
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Code = code,
            Category = category,
            Message = message,
            Transient = transient,
            RetryAfterMilliseconds = retryAfterMilliseconds,
        };
    }

    private static void ValidateIdentifier(string value, string parameterName)
    {
        if (value.Length is < 1 or > 128 || value.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("Automation identifier 只能包含 ASCII 字母、数字、'-' 与 '_'。", parameterName);
        }
    }

    private static void ValidatePipeName(string pipeName)
    {
        ValidateIdentifier(pipeName, nameof(pipeName));
    }

    private static void ValidateSha256(string value, string parameterName)
    {
        if (value.Length != 64 || value.Any(static character => !char.IsAsciiHexDigit(character) || char.IsUpper(character)))
        {
            throw new ArgumentException("Capability digest 必须是 64 位小写 SHA256。", parameterName);
        }
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void EnsureStarted()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Volatile.Read(ref _started) == 0)
        {
            throw new InvalidOperationException("Automation Server 尚未启动。");
        }
    }

    private sealed class ConnectionState
    {
        public string? ClientName { get; set; }

        public string? ClientNonce { get; set; }

        public string? ServerNonce { get; set; }

        public AutomationProtocolVersion? SelectedVersion { get; set; }

        public string[] HelloRequestedScopes { get; set; } = [];

        public string? SessionId { get; set; }

        public string[] GrantedScopes { get; set; } = [];
    }
}
