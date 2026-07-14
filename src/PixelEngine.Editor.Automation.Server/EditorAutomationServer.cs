using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Automation.Server;

/// <summary>
/// 事件驱动、current-user-only 的 Windows Named Pipe Editor automation Server。
/// </summary>
public sealed class EditorAutomationServer : IAsyncDisposable
{
    private const int MaxHandshakeVersions = 16;
    private const int MaxHandshakeScopes = 32;
    private readonly AutomationServerOptions _options;
    private readonly IAutomationRequestHandler _handler;
    private readonly AutomationDiscoveryPublisher _discovery;
    private readonly AutomationAuditLog _auditLog;
    private readonly byte[] _secret;
    private readonly string _principalId;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<int, Task> _connections = new();
    private readonly SemaphoreSlim _connectionSlots;
    private readonly SemaphoreSlim _descriptorUpdateLock = new(1, 1);
    private AutomationInstanceDescriptor _descriptor;
    private Task? _acceptLoop;
    private Exception? _fatalInfrastructureFailure;
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
        try
        {
            _principalId = AutomationAuthentication.ComputePrincipalId(_secret);
            _auditLog = new AutomationAuditLog(
                _options.AuditRoot!,
                InstanceId,
                _options.MaxAuditFileBytes,
                _options.MaxAuditFiles);
            _descriptor = CreateDescriptor();
        }
        catch
        {
            CryptographicOperations.ZeroMemory(_secret);
            throw;
        }
    }

    /// <summary>本次进程不可复用的实例 id。</summary>
    public string InstanceId { get; }

    /// <summary>Named Pipe name。</summary>
    public string PipeName { get; }

    /// <summary>当前实例 active JSONL 审计日志的 canonical path。</summary>
    public string AuditLogPath => _auditLog.CurrentPath;

    /// <summary>当前发布的 discovery descriptor。</summary>
    public AutomationInstanceDescriptor Descriptor => CloneDescriptor(Volatile.Read(ref _descriptor));

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

        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
        {
            throw new InvalidOperationException("Automation Server 已启动。");
        }

        try
        {
            _acceptLoop = AcceptLoopAsync(_shutdown.Token);
            await _discovery.WriteCredentialAsync(InstanceId, _secret, cancellationToken).ConfigureAwait(false);
            await _discovery.PublishAsync(Volatile.Read(ref _descriptor), cancellationToken).ConfigureAwait(false);
            if (Volatile.Read(ref _fatalInfrastructureFailure) is { } fatalFailure)
            {
                throw new InvalidOperationException(
                    "Automation Server accept infrastructure 在发布期间失败。",
                    fatalFailure);
            }

            Volatile.Write(ref _started, 2);
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
        AutomationProjectSummary? normalizedProject = NormalizeProjectSummary(project, nameof(project));
        using CancellationTokenSource updateCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdown.Token);
        await _descriptorUpdateLock.WaitAsync(updateCancellation.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            AutomationInstanceDescriptor next = Volatile.Read(ref _descriptor) with
            {
                Project = normalizedProject,
                CapabilityDigest = capabilityDigest,
                PublishedAtUtc = _options.TimeProvider.GetUtcNow(),
            };
            await _discovery.PublishAsync(next, updateCancellation.Token).ConfigureAwait(false);
            Volatile.Write(ref _descriptor, next);
        }
        finally
        {
            _ = _descriptorUpdateLock.Release();
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
            if (_acceptLoop is not null)
            {
                await IgnoreCancellationAsync(_acceptLoop).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        try
        {
            Task[] connections = [.. _connections.Values];
            if (connections.Length != 0)
            {
                await Task.WhenAll(connections).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        try
        {
            await _auditLog.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        if (Volatile.Read(ref _fatalInfrastructureFailure) is { } fatalFailure)
        {
            (failures ??= []).Add(fatalFailure);
        }

        try
        {
            await _descriptorUpdateLock.WaitAsync().ConfigureAwait(false);
            _ = _descriptorUpdateLock.Release();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }
        finally
        {
            _discovery.Remove(InstanceId);
            CryptographicOperations.ZeroMemory(_secret);
            _connectionSlots.Dispose();
            _descriptorUpdateLock.Dispose();
            _shutdown.Dispose();
        }

        if (failures is not null)
        {
            throw new AggregateException("Automation Server 关闭时遇到错误。", failures);
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            FailInfrastructure(exception);
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
        catch (Exception exception)
        {
            FailInfrastructure(exception);
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
            ConnectionEventSink eventSink = new(
                _options.MaxQueuedEventsPerConnection,
                connectionShutdown);
            Task eventPump = PumpEventsAsync(
                pipe,
                writeLock,
                state,
                eventSink,
                connectionShutdown.Token);
            ConcurrentDictionary<string, RequestCancellationState> activeRequests = new(StringComparer.Ordinal);
            ConnectionRequestDrainState requestDrain = new();
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
                    long startedTimestamp = Stopwatch.GetTimestamp();

                    if (envelope.Kind == AutomationMessageKind.Cancel)
                    {
                        await HandleCancelAsync(
                                pipe,
                                writeLock,
                                state,
                                envelope,
                                startedTimestamp,
                                activeRequests,
                                connectionShutdown.Token)
                            .ConfigureAwait(false);
                        continue;
                    }

                    if (envelope.Kind != AutomationMessageKind.Request)
                    {
                        bool audited = await WriteErrorWithAuditAsync(
                                pipe,
                                writeLock,
                                state,
                                envelope,
                                CreateError(
                                AutomationErrorCodes.InvalidRequest,
                                AutomationErrorCategory.Validation,
                                "客户端只能发送 Request 或 Cancel envelope。"),
                                startedTimestamp,
                                connectionShutdown.Token).ConfigureAwait(false);
                        if (!audited)
                        {
                            break;
                        }

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
                                eventSink,
                                envelope,
                                startedTimestamp,
                                connectionShutdown.Token).ConfigureAwait(false);
                        }
                        catch (AutomationRequestException exception)
                        {
                            keepOpen = await WriteErrorWithAuditAsync(
                                pipe,
                                writeLock,
                                state,
                                envelope,
                                exception.Error,
                                startedTimestamp,
                                connectionShutdown.Token).ConfigureAwait(false);
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
                        bool audited = await WriteErrorWithAuditAsync(
                                pipe,
                                writeLock,
                                state,
                                envelope,
                                CreateError(
                                AutomationErrorCodes.Busy,
                                AutomationErrorCategory.Availability,
                                "该连接的并发请求额度已满。",
                                transient: true,
                                retryAfterMilliseconds: 25),
                                startedTimestamp,
                                connectionShutdown.Token).ConfigureAwait(false);
                        if (!audited)
                        {
                            break;
                        }

                        continue;
                    }

                    RequestCancellationState requestCancellation = new(
                        connectionShutdown.Token,
                        _options.TimeProvider);
                    if (!activeRequests.TryAdd(envelope.MessageId, requestCancellation))
                    {
                        requestCancellation.Dispose();
                        _ = requestSlots.Release();
                        bool audited = await WriteErrorWithAuditAsync(
                                pipe,
                                writeLock,
                                state,
                                envelope,
                                CreateError(
                                AutomationErrorCodes.InvalidRequest,
                                AutomationErrorCategory.Conflict,
                                "同一连接内 request message id 必须唯一。"),
                                startedTimestamp,
                                connectionShutdown.Token).ConfigureAwait(false);
                        if (!audited)
                        {
                            break;
                        }

                        continue;
                    }

                    _ = ProcessRequestAndReleaseAsync(
                        pipe,
                        writeLock,
                        requestSlots,
                        state,
                        envelope,
                        startedTimestamp,
                        requestCancellation,
                        activeRequests,
                        requestDrain,
                        connectionShutdown);
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
                requestDrain.BeginClosing(activeRequests.IsEmpty);
                foreach (RequestCancellationState request in activeRequests.Values)
                {
                    request.CancelConnection();
                }

                await requestDrain.Completion.ConfigureAwait(false);
                try
                {
                    if (state.SessionId is not null && _handler is IAutomationSessionLifecycleHandler lifecycle)
                    {
                        lifecycle.OnSessionClosed(CreateSessionContext(state));
                    }
                }
                finally
                {
                    eventSink.Complete();
                    await IgnoreConnectionTerminationAsync(eventPump).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task<bool> HandleHandshakeAsync(
        Stream pipe,
        SemaphoreSlim writeLock,
        ConnectionState state,
        ConnectionEventSink eventSink,
        AutomationEnvelope request,
        long startedTimestamp,
        CancellationToken cancellationToken)
    {
        if (string.Equals(request.Method, AutomationProtocolConstants.HelloMethod, StringComparison.Ordinal))
        {
            AutomationHelloRequest? hello = DeserializePayload(
                request,
                AutomationJsonContext.Default.AutomationHelloRequest);
            if (!IsValidHello(hello))
            {
                return await WriteErrorWithAuditAsync(
                    pipe,
                    writeLock,
                    state,
                    request,
                    CreateError(
                        AutomationErrorCodes.InvalidRequest,
                        AutomationErrorCategory.Validation,
                        "system.hello payload 不完整。"),
                    startedTimestamp,
                    cancellationToken).ConfigureAwait(false);
            }

            AutomationHelloRequest validHello = hello!;
            AutomationProtocolVersion? selected = SelectVersion(validHello.SupportedVersions);
            if (selected is null)
            {
                _ = await WriteErrorWithAuditAsync(
                    pipe,
                    writeLock,
                    state,
                    request,
                    CreateError(
                        AutomationErrorCodes.ProtocolVersionUnsupported,
                        AutomationErrorCategory.Validation,
                        "客户端与 Server 没有共同协议 major 版本。"),
                    startedTimestamp,
                    cancellationToken).ConfigureAwait(false);
                return false;
            }

            state.ClientName = validHello.ClientName;
            state.ClientInstanceId = validHello.ClientInstanceId;
            state.ClientVersion = validHello.ClientVersion;
            state.ClientNonce = validHello.ClientNonce;
            state.ServerNonce = AutomationAuthentication.GenerateNonce();
            state.SelectedVersion = selected;
            state.HelloRequestedScopes = [.. validHello.RequestedScopes.Order(StringComparer.Ordinal)];

            AutomationHelloChallenge challenge = new()
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                InstanceId = InstanceId,
                SelectedVersion = selected,
                ServerNonce = state.ServerNonce,
                ServerProof = AutomationAuthentication.ComputeServerProof(
                    _secret,
                    InstanceId,
                    validHello.ClientInstanceId,
                    validHello.ClientName,
                    validHello.ClientVersion,
                    validHello.ClientNonce,
                    state.ServerNonce,
                    selected,
                    validHello.RequestedScopes,
                    _options.SupportedScopes,
                    _options.MaxFrameBytes),
                SupportedScopes = [.. _options.SupportedScopes],
                AuthenticationAlgorithm = AutomationProtocolConstants.AuthenticationAlgorithm,
                MaxFrameBytes = _options.MaxFrameBytes,
            };
            return await WritePayloadWithAuditAsync(
                pipe,
                writeLock,
                state,
                request,
                JsonSerializer.SerializeToElement(
                    challenge,
                    AutomationJsonContext.Default.AutomationHelloChallenge),
                selected,
                startedTimestamp,
                cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(request.Method, AutomationProtocolConstants.AuthenticateMethod, StringComparison.Ordinal))
        {
            if (state.SelectedVersion is null || state.ClientNonce is null || state.ServerNonce is null)
            {
                _ = await WriteErrorWithAuditAsync(
                    pipe,
                    writeLock,
                    state,
                    request,
                    CreateError(
                        AutomationErrorCodes.AuthenticationRequired,
                        AutomationErrorCategory.Authentication,
                        "必须先完成 system.hello。"),
                    startedTimestamp,
                    cancellationToken).ConfigureAwait(false);
                return false;
            }

            AutomationAuthenticateRequest? authentication = DeserializePayload(
                request,
                AutomationJsonContext.Default.AutomationAuthenticateRequest);
            bool noncesMatch = authentication is not null &&
                authentication.SchemaVersion == AutomationProtocolConstants.WireSchemaVersion &&
                IsValidScopeSet(authentication.RequestedScopes) &&
                IsBase64Sha256(authentication.Proof) &&
                IsBase64Sha256(authentication.ClientNonce) &&
                IsBase64Sha256(authentication.ServerNonce) &&
                !authentication.RequestedScopes.Except(
                    state.HelloRequestedScopes,
                    StringComparer.Ordinal).Any() &&
                string.Equals(authentication.ClientNonce, state.ClientNonce, StringComparison.Ordinal) &&
                string.Equals(authentication.ServerNonce, state.ServerNonce, StringComparison.Ordinal);
            bool proofValid = noncesMatch && AutomationAuthentication.VerifyProof(
                _secret,
                InstanceId,
                state.ClientInstanceId!,
                state.ClientName!,
                state.ClientVersion!,
                state.ClientNonce,
                state.ServerNonce,
                state.SelectedVersion,
                authentication!.RequestedScopes!,
                authentication!.Proof);
            if (!proofValid)
            {
                _ = await WriteErrorWithAuditAsync(
                    pipe,
                    writeLock,
                    state,
                    request,
                    CreateError(
                        AutomationErrorCodes.AuthenticationFailed,
                        AutomationErrorCategory.Authentication,
                        "Automation challenge proof 无效。"),
                    startedTimestamp,
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
                PrincipalId = _principalId,
                GrantedScopes = grantedScopes,
                ServerVersion = _options.EditorVersion,
                Protocol = state.SelectedVersion,
            };
            bool responseWritten = await WritePayloadWithAuditAsync(
                pipe,
                writeLock,
                state,
                request,
                JsonSerializer.SerializeToElement(session, AutomationJsonContext.Default.AutomationSessionInfo),
                state.SelectedVersion,
                startedTimestamp,
                cancellationToken).ConfigureAwait(false);
            if (responseWritten && _handler is IAutomationSessionLifecycleHandler lifecycle)
            {
                // 必须先完整发送 authenticate response，再开放 event sink；否则 lifecycle
                // 同步发布的首个 event 会抢在 session response 前破坏 Client 握手状态机。
                lifecycle.OnSessionOpened(CreateSessionContext(state), eventSink);
            }

            return responseWritten;
        }

        return await WriteErrorWithAuditAsync(
            pipe,
            writeLock,
            state,
            request,
            CreateError(
                AutomationErrorCodes.AuthenticationRequired,
                AutomationErrorCategory.Authentication,
                "连接认证前只允许 system.hello 与 system.authenticate。"),
            startedTimestamp,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ProcessRequestAndReleaseAsync(
        Stream pipe,
        SemaphoreSlim writeLock,
        SemaphoreSlim requestSlots,
        ConnectionState state,
        AutomationEnvelope request,
        long startedTimestamp,
        RequestCancellationState requestCancellation,
        ConcurrentDictionary<string, RequestCancellationState> activeRequests,
        ConnectionRequestDrainState requestDrain,
        CancellationTokenSource connectionShutdown)
    {
        CancellationToken connectionCancellation = connectionShutdown.Token;
        ProcessedAutomationResponse? response = null;
        AutomationError? error = null;
        try
        {
            ApplyDeadline(request, requestCancellation);
            response = await ProcessAuthenticatedRequestAsync(
                state,
                request,
                requestCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (requestCancellation.Token.IsCancellationRequested)
        {
            error = requestCancellation.Reason == RequestCancellationReason.Deadline
                ? CreateError(
                    AutomationErrorCodes.DeadlineExceeded,
                    AutomationErrorCategory.Cancellation,
                    "Automation request deadline 已超过。")
                : CreateError(
                    AutomationErrorCodes.Cancelled,
                    AutomationErrorCategory.Cancellation,
                    "Automation request 已取消。");
        }
        catch (AutomationRequestException exception)
        {
            error = exception.Error;
        }
        catch (Exception exception)
        {
            error = CreateError(
                AutomationErrorCodes.Internal,
                AutomationErrorCategory.Internal,
                $"Automation handler 失败：{exception.GetType().Name}。");
        }

        AutomationEnvelope? responseEnvelope = null;
        if (error is null && response is not null)
        {
            responseEnvelope = CreatePayloadResponse(
                request,
                response.Payload,
                state.SelectedVersion!,
                response.Revision);
            try
            {
                _ = AutomationFrameCodec.ValidateWritable(responseEnvelope, _options.MaxFrameBytes);
            }
            catch (AutomationFrameSizeException)
            {
                error = CreateError(
                    AutomationErrorCodes.ResponseTooLarge,
                    AutomationErrorCategory.Internal,
                    "Automation semantic response 超过控制面 frame 上限；大型数据必须返回 artifact 引用。") with
                {
                    CurrentRevision = response.Revision?.GlobalRevision,
                };
                responseEnvelope = null;
            }
            catch (Exception exception)
            {
                error = CreateError(
                    AutomationErrorCodes.Internal,
                    AutomationErrorCategory.Internal,
                    $"Automation semantic response contract 无效：{exception.GetType().Name}。") with
                {
                    CurrentRevision = response.Revision?.GlobalRevision,
                };
                responseEnvelope = null;
            }
        }

        try
        {
            if (!await TryAppendAuditAsync(
                    state,
                    request,
                    error,
                    response?.Revision,
                    startedTimestamp).ConfigureAwait(false))
            {
                return;
            }

            if (error is not null)
            {
                await TryWriteErrorAsync(pipe, writeLock, request, error).ConfigureAwait(false);
                return;
            }

            AutomationEnvelope completed = responseEnvelope
                ?? throw new InvalidOperationException("Automation request 未生成可编码 response。");
            await WriteEnvelopeAsync(pipe, writeLock, completed, connectionCancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (connectionCancellation.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
            connectionShutdown.Cancel();
        }
        catch (ObjectDisposedException) when (connectionCancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            // response 编码/写入属于连接基础设施；semantic 错误已在上面转换并审计。
            // 任何其余异常都使当前 byte stream 的边界不再可信，必须关闭连接。
            connectionShutdown.Cancel();
        }
        finally
        {
            _ = activeRequests.TryRemove(request.MessageId, out _);
            requestCancellation.Dispose();
            _ = requestSlots.Release();
            requestDrain.RequestCompleted(activeRequests.IsEmpty);
        }
    }

    private async ValueTask<ProcessedAutomationResponse> ProcessAuthenticatedRequestAsync(
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
        AutomationRevisionSnapshot? revision = null;
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
                Volatile.Read(ref _descriptor),
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
                _principalId,
                state.ClientInstanceId ?? "unknown",
                state.ClientName ?? "unknown",
                state.GrantedScopes,
                request.DeadlineUtc,
                request.ExpectedRevision,
                request.IdempotencyKey,
                request.TransactionId);
            AutomationHandlerResult result = await _handler.HandleAsync(
                context,
                method,
                request.Payload,
                cancellationToken).ConfigureAwait(false);
            payload = result.Payload;
            revision = result.Revision;
        }

        return new ProcessedAutomationResponse(payload, revision);
    }

    private async ValueTask<bool> TryAppendAuditAsync(
        ConnectionState state,
        AutomationEnvelope request,
        AutomationError? error,
        AutomationRevisionSnapshot? revision,
        long startedTimestamp)
    {
        try
        {
            TimeSpan elapsed = Stopwatch.GetElapsedTime(startedTimestamp);
            await _auditLog.AppendAsync(new AutomationAuditRecord
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                TimestampUtc = _options.TimeProvider.GetUtcNow(),
                InstanceId = InstanceId,
                SessionId = state.SessionId,
                PrincipalId = state.SessionId is null ? null : _principalId,
                ClientInstanceId = state.ClientInstanceId,
                RequestId = request.MessageId,
                CorrelationId = request.CorrelationId ?? request.MessageId,
                Capability = string.IsNullOrWhiteSpace(request.Method) ? "<invalid>" : request.Method,
                Result = error is null ? "success" : "error",
                ErrorCode = error?.Code,
                DurationMicroseconds = Math.Max(0, elapsed.Ticks / 10),
                Revision = revision,
                CurrentRevision = error?.CurrentRevision,
                TransactionId = request.TransactionId,
            }).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception)
        {
            // 审计是安全边界：一旦无法持久写入，立即撤销 discovery 并停止接受/执行后续请求。
            FailInfrastructure(exception);
            return false;
        }
    }

    private async Task HandleCancelAsync(
        Stream pipe,
        SemaphoreSlim writeLock,
        ConnectionState state,
        AutomationEnvelope request,
        long startedTimestamp,
        ConcurrentDictionary<string, RequestCancellationState> activeRequests,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Method, AutomationProtocolConstants.CancelMethod, StringComparison.Ordinal))
        {
            _ = await WriteErrorWithAuditAsync(
                pipe,
                writeLock,
                state,
                request,
                CreateError(
                    AutomationErrorCodes.InvalidRequest,
                    AutomationErrorCategory.Validation,
                    "Cancel envelope 的 method 必须是 system.cancel。"),
                startedTimestamp,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (state.SessionId is null || !string.Equals(request.SessionId, state.SessionId, StringComparison.Ordinal))
        {
            _ = await WriteErrorWithAuditAsync(
                pipe,
                writeLock,
                state,
                request,
                CreateError(
                    AutomationErrorCodes.AuthenticationRequired,
                    AutomationErrorCategory.Authentication,
                    "取消请求需要已认证 session。"),
                startedTimestamp,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        AutomationCancelRequest? cancel = DeserializePayload(
            request,
            AutomationJsonContext.Default.AutomationCancelRequest);
        if (cancel is null || cancel.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
            !IsBoundedText(cancel.TargetRequestId, 128))
        {
            _ = await WriteErrorWithAuditAsync(
                pipe,
                writeLock,
                state,
                request,
                CreateError(
                    AutomationErrorCodes.InvalidRequest,
                    AutomationErrorCategory.Validation,
                    "Cancel payload 必须包含 targetRequestId。"),
                startedTimestamp,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (activeRequests.TryGetValue(cancel.TargetRequestId, out RequestCancellationState? target))
        {
            target.CancelExplicitly();
        }

        _ = await WritePayloadWithAuditAsync(
            pipe,
            writeLock,
            state,
            request,
            JsonSerializer.SerializeToElement(
                new AutomationCancelRequest
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    TargetRequestId = cancel.TargetRequestId,
                },
                AutomationJsonContext.Default.AutomationCancelRequest),
            state.SelectedVersion ?? AutomationProtocolConstants.CurrentVersion,
            startedTimestamp,
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

    private void ApplyDeadline(AutomationEnvelope request, RequestCancellationState cancellation)
    {
        if (request.DeadlineUtc is null)
        {
            return;
        }

        TimeSpan remaining = request.DeadlineUtc.Value - _options.TimeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            cancellation.CancelForDeadline();
            return;
        }

        if (remaining > _options.MaxRequestLifetime)
        {
            throw new AutomationRequestException(CreateError(
                AutomationErrorCodes.InvalidRequest,
                AutomationErrorCategory.Validation,
                $"Automation request deadline 不得超过 {_options.MaxRequestLifetime}。"));
        }

        cancellation.ScheduleDeadline(remaining);
    }

    private async ValueTask<bool> WritePayloadWithAuditAsync(
        Stream pipe,
        SemaphoreSlim writeLock,
        ConnectionState state,
        AutomationEnvelope request,
        JsonElement? payload,
        AutomationProtocolVersion protocol,
        long startedTimestamp,
        CancellationToken cancellationToken,
        AutomationRevisionSnapshot? revision = null)
    {
        if (!await TryAppendAuditAsync(
                state,
                request,
                error: null,
                revision,
                startedTimestamp).ConfigureAwait(false))
        {
            return false;
        }

        await WritePayloadAsync(
            pipe,
            writeLock,
            request,
            payload,
            protocol,
            cancellationToken,
            revision).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<bool> WriteErrorWithAuditAsync(
        Stream pipe,
        SemaphoreSlim writeLock,
        ConnectionState state,
        AutomationEnvelope request,
        AutomationError error,
        long startedTimestamp,
        CancellationToken cancellationToken)
    {
        if (!await TryAppendAuditAsync(
                state,
                request,
                error,
                revision: null,
                startedTimestamp).ConfigureAwait(false))
        {
            return false;
        }

        await WriteErrorAsync(
            pipe,
            writeLock,
            request,
            error,
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask WritePayloadAsync(
        Stream pipe,
        SemaphoreSlim writeLock,
        AutomationEnvelope request,
        JsonElement? payload,
        AutomationProtocolVersion protocol,
        CancellationToken cancellationToken,
        AutomationRevisionSnapshot? revision = null)
    {
        AutomationEnvelope response = CreatePayloadResponse(request, payload, protocol, revision);
        await WriteEnvelopeAsync(pipe, writeLock, response, cancellationToken).ConfigureAwait(false);
    }

    private static AutomationEnvelope CreatePayloadResponse(
        AutomationEnvelope request,
        JsonElement? payload,
        AutomationProtocolVersion protocol,
        AutomationRevisionSnapshot? revision)
    {
        return new AutomationEnvelope
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Protocol = protocol,
            MessageId = Guid.NewGuid().ToString("N"),
            Kind = AutomationMessageKind.Response,
            CorrelationId = request.MessageId,
            Method = request.Method,
            SessionId = request.SessionId,
            Payload = payload,
            Revision = revision,
        };
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

    private async Task PumpEventsAsync(
        Stream pipe,
        SemaphoreSlim writeLock,
        ConnectionState state,
        ConnectionEventSink eventSink,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (AutomationEventRecord eventRecord in eventSink.Reader.ReadAllAsync(cancellationToken))
            {
                AutomationProtocolVersion protocol = state.SelectedVersion
                    ?? throw new AutomationProtocolException("未认证连接不得发送 automation event。");
                string sessionId = state.SessionId
                    ?? throw new AutomationProtocolException("Automation event 缺少认证 session。");
                AutomationEnvelope envelope = new()
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    Protocol = protocol,
                    MessageId = Guid.NewGuid().ToString("N"),
                    Kind = AutomationMessageKind.Event,
                    CorrelationId = eventRecord.CausationRequestId,
                    Method = AutomationProtocolConstants.EventNotificationMethod,
                    SessionId = sessionId,
                    Revision = eventRecord.StateRevision,
                    Payload = JsonSerializer.SerializeToElement(
                        eventRecord,
                        AutomationJsonContext.Default.AutomationEventRecord),
                };
                await WriteEnvelopeAsync(
                    pipe,
                    writeLock,
                    envelope,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or AutomationProtocolException)
        {
            eventSink.Abort();
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
        if (!IsValidScopeSet(options.SupportedScopes))
        {
            throw new ArgumentException("Automation supported scope 集合无效、重复或超过上限。", nameof(options));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxFrameBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            options.MaxFrameBytes,
            AutomationProtocolConstants.AbsoluteMaxFrameBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxConcurrentRequestsPerConnection);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxQueuedEventsPerConnection);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxConnections, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaxConnections, 254);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.HandshakeTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.HandshakeTimeout, TimeSpan.FromMinutes(5));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.MaxRequestLifetime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaxRequestLifetime, TimeSpan.FromDays(7));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxAuditFileBytes, 4L * 1024 * 1024);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaxAuditFileBytes, 16L * 1024 * 1024 * 1024);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxAuditFiles, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaxAuditFiles, 32);
        if (options.AuditRoot is not null && string.IsNullOrWhiteSpace(options.AuditRoot))
        {
            throw new ArgumentException("Automation audit root 不能是空白字符串。", nameof(options));
        }

        AutomationProjectSummary? project = NormalizeProjectSummary(options.Project, nameof(options));

        string discoveryRoot = Path.GetFullPath(options.DiscoveryRoot);
        string auditRoot = options.AuditRoot is null
            ? Path.Combine(discoveryRoot, "audit")
            : Path.GetFullPath(options.AuditRoot);
        return options with
        {
            DiscoveryRoot = discoveryRoot,
            AuditRoot = auditRoot,
            Project = project,
            SupportedScopes =
            [
                .. options.SupportedScopes.Order(StringComparer.Ordinal),
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

        AutomationSecureStorage.EnsurePrivateFile(path);

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

    private static AutomationProjectSummary? NormalizeProjectSummary(
        AutomationProjectSummary? project,
        string parameterName)
    {
        return project is null
            ? null
            : project.SchemaVersion != AutomationProtocolConstants.WireSchemaVersion ||
              !IsBoundedText(project.ProjectId, 128) || !IsBoundedText(project.Name, 256) ||
              !IsBoundedText(project.RootPath, 32767) ||
              (project.SceneId is not null && !IsBoundedText(project.SceneId, 128))
                ? throw new ArgumentException(
                    "Automation project summary 字段、长度或 schema 无效。",
                    parameterName)
                : project with { RootPath = Path.GetFullPath(project.RootPath) };
    }

    private static AutomationInstanceDescriptor CloneDescriptor(AutomationInstanceDescriptor descriptor)
    {
        return descriptor with
        {
            ProtocolVersions =
            [
                .. descriptor.ProtocolVersions.Select(static version => version with { }),
            ],
            Endpoint = descriptor.Endpoint with { },
            Project = descriptor.Project is null ? null : descriptor.Project with { },
        };
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

    private static bool IsValidHello(AutomationHelloRequest? hello)
    {
        return hello is not null && hello.SchemaVersion == AutomationProtocolConstants.WireSchemaVersion &&
            IsBoundedText(hello.ClientInstanceId, 128) && IsBoundedText(hello.ClientName, 128) &&
            IsBoundedText(hello.ClientVersion, 128) && IsBase64Sha256(hello.ClientNonce) &&
            hello.SupportedVersions is { Length: >= 1 and <= MaxHandshakeVersions } versions &&
            versions.All(static version => version is not null && version.Major > 0 && version.Minor >= 0) &&
            versions.Distinct().Count() == versions.Length && IsValidScopeSet(hello.RequestedScopes);
    }

    private static bool IsValidScopeSet(string[]? scopes)
    {
        return scopes is { Length: <= MaxHandshakeScopes } &&
            scopes.All(static scope => scope is { Length: >= 1 and <= 64 } &&
                char.IsAsciiLetter(scope[0]) && scope.All(static character =>
                    char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')) &&
            scopes.Distinct(StringComparer.Ordinal).Count() == scopes.Length;
    }

    private static bool IsBoundedText(string? value, int maxLength)
    {
        return value is { Length: >= 1 } && value.Length <= maxLength &&
            !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);
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

    private AutomationSessionContext CreateSessionContext(ConnectionState state)
    {
        return new AutomationSessionContext
        {
            SessionId = state.SessionId ?? throw new InvalidOperationException("Automation session 尚未认证。"),
            PrincipalId = _principalId,
            ClientInstanceId = state.ClientInstanceId ?? throw new InvalidOperationException("Automation client instance 缺失。"),
            ClientName = state.ClientName ?? throw new InvalidOperationException("Automation client name 缺失。"),
            GrantedScopes = [.. state.GrantedScopes],
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

    private static async Task IgnoreConnectionTerminationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
    }

    private void EnsureStarted()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Volatile.Read(ref _started) != 2)
        {
            throw new InvalidOperationException("Automation Server 尚未完成启动。");
        }
    }

    private void FailInfrastructure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _ = Interlocked.CompareExchange(ref _fatalInfrastructureFailure, exception, null);
        _discovery.Remove(InstanceId);
        try
        {
            _shutdown.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private sealed class ConnectionState
    {
        public string? ClientName { get; set; }

        public string? ClientInstanceId { get; set; }

        public string? ClientVersion { get; set; }

        public string? ClientNonce { get; set; }

        public string? ServerNonce { get; set; }

        public AutomationProtocolVersion? SelectedVersion { get; set; }

        public string[] HelloRequestedScopes { get; set; } = [];

        public string? SessionId { get; set; }

        public string[] GrantedScopes { get; set; } = [];
    }

    private sealed record ProcessedAutomationResponse(
        JsonElement? Payload,
        AutomationRevisionSnapshot? Revision);

    private sealed class ConnectionRequestDrainState
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _closing;

        public Task Completion => _completion.Task;

        public void BeginClosing(bool requestsAlreadyEmpty)
        {
            Volatile.Write(ref _closing, 1);
            if (requestsAlreadyEmpty)
            {
                _ = _completion.TrySetResult();
            }
        }

        public void RequestCompleted(bool requestsNowEmpty)
        {
            if (requestsNowEmpty && Volatile.Read(ref _closing) != 0)
            {
                _ = _completion.TrySetResult();
            }
        }
    }

    private sealed class RequestCancellationState(
        CancellationToken connectionToken,
        TimeProvider timeProvider) : IDisposable
    {
        private readonly Lock _sync = new();
        private readonly CancellationTokenSource _cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(connectionToken);
        private readonly TimeProvider _timeProvider =
            timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        private ITimer? _deadlineTimer;
        private int _reason;
        private int _disposed;

        public CancellationToken Token => _cancellation.Token;

        public RequestCancellationReason Reason =>
            (RequestCancellationReason)Volatile.Read(ref _reason);

        public void ScheduleDeadline(TimeSpan dueTime)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dueTime, TimeSpan.Zero);
            lock (_sync)
            {
                if (Volatile.Read(ref _disposed) != 0 || _cancellation.IsCancellationRequested)
                {
                    return;
                }

                _deadlineTimer = _timeProvider.CreateTimer(
                    static state => ((RequestCancellationState)state!).CancelForDeadline(),
                    this,
                    dueTime,
                    Timeout.InfiniteTimeSpan);
            }
        }

        public void CancelForDeadline()
        {
            Cancel(RequestCancellationReason.Deadline);
        }

        public void CancelExplicitly()
        {
            Cancel(RequestCancellationReason.Explicit);
        }

        public void CancelConnection()
        {
            Cancel(RequestCancellationReason.Connection);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            lock (_sync)
            {
                _deadlineTimer?.Dispose();
                _deadlineTimer = null;
            }

            _cancellation.Dispose();
        }

        private void Cancel(RequestCancellationReason reason)
        {
            if (Volatile.Read(ref _disposed) != 0 ||
                Interlocked.CompareExchange(ref _reason, (int)reason, (int)RequestCancellationReason.None) !=
                (int)RequestCancellationReason.None)
            {
                return;
            }

            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private enum RequestCancellationReason
    {
        None,
        Explicit,
        Deadline,
        Connection,
    }

    private sealed class ConnectionEventSink : IAutomationEventSink
    {
        private readonly Channel<AutomationEventRecord> _channel;
        private readonly CancellationTokenSource _connectionShutdown;
        private int _completed;

        public ConnectionEventSink(int capacity, CancellationTokenSource connectionShutdown)
        {
            _connectionShutdown = connectionShutdown;
            _channel = Channel.CreateBounded<AutomationEventRecord>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false,
            });
        }

        public ChannelReader<AutomationEventRecord> Reader => _channel.Reader;

        public bool TryPublish(AutomationEventRecord eventRecord)
        {
            ArgumentNullException.ThrowIfNull(eventRecord);
            if (Volatile.Read(ref _completed) == 0 && _channel.Writer.TryWrite(eventRecord))
            {
                return true;
            }

            Abort();
            return false;
        }

        public void Abort()
        {
            try
            {
                _connectionShutdown.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Complete()
        {
            if (Interlocked.Exchange(ref _completed, 1) == 0)
            {
                _ = _channel.Writer.TryComplete();
            }
        }
    }
}
