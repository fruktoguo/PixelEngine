using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PixelEngine.Editor.Automation.Client;
using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Cli;

internal static class CliApplication
{
    private static readonly string ClientVersion = typeof(CliApplication).Assembly
        .GetName()
        .Version?
        .ToString(3) ?? "unknown";

    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        try
        {
            CliArguments arguments = new(args);
            CliGlobalOptions globals = CliArguments.ParseGlobals(arguments);
            if (globals.Version)
            {
                arguments.EnsureEmpty();
                Console.WriteLine(ClientVersion);
                return 0;
            }

            if (globals.Help || arguments.Count == 0)
            {
                PrintHelp();
                return 0;
            }

            string command = arguments.TakeRequiredPositional("command");
            using CancellationTokenSource cancellation = new();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };
            CliOutput output = new(globals.OutputMode);
            if (string.Equals(command, "discover", StringComparison.Ordinal))
            {
                arguments.EnsureEmpty();
                AutomationDiscoverySnapshot discovery = await AutomationDiscovery.DiscoverAsync(
                    globals.DiscoveryRoot,
                    cancellation.Token).ConfigureAwait(false);
                output.WriteDiscovery(discovery);
                return discovery.Instances.Length == 0 ? 3 : 0;
            }

            if (string.Equals(command, "help", StringComparison.Ordinal) && arguments.Count == 0)
            {
                PrintHelp();
                return 0;
            }

            AutomationDiscoveredInstance instance = await SelectInstanceAsync(
                globals,
                cancellation.Token).ConfigureAwait(false);
            await using EditorAutomationClient client = await EditorAutomationClient.ConnectAsync(
                instance,
                new AutomationClientOptions
                {
                    ClientInstanceId = globals.ClientInstanceId,
                    ClientName = "pixelengine-editor-cli",
                    ClientVersion = ClientVersion,
                    RequestedScopes = globals.Scopes,
                    ConnectTimeout = globals.ConnectTimeout,
                    RequestTimeout = globals.RequestTimeout,
                    CredentialPath = globals.CredentialPath,
                },
                cancellation.Token).ConfigureAwait(false);
            return await DispatchConnectedAsync(
                command,
                arguments,
                client,
                output,
                cancellation.Token).ConfigureAwait(false);
        }
        catch (CliUsageException exception)
        {
            CliOutput.WriteError("usage", exception.Message, 2);
            return 2;
        }
        catch (AutomationRemoteException exception)
        {
            CliOutput.WriteError(exception.Error.Code, exception.Message, 4);
            return 4;
        }
        catch (AutomationRequestTimeoutException exception)
        {
            CliOutput.WriteError("timeout", exception.Message, 5);
            return 5;
        }
        catch (AutomationConnectionException exception)
        {
            CliOutput.WriteError("connection", exception.Message, 3);
            return 3;
        }
        catch (OperationCanceledException)
        {
            CliOutput.WriteError("cancelled", "操作已取消。", 130);
            return 130;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            CliOutput.WriteError("invalid_input", exception.Message, 2);
            return 2;
        }
        catch (Exception)
        {
            CliOutput.WriteError("internal_error", "CLI 内部错误。", 1);
            return 1;
        }
    }

    private static async Task<int> DispatchConnectedAsync(
        string command,
        CliArguments arguments,
        EditorAutomationClient client,
        CliOutput output,
        CancellationToken cancellationToken)
    {
        switch (command)
        {
            case "ping":
                arguments.EnsureEmpty();
                WriteTyped(
                    output,
                    new AutomationTypedInvocationResult<AutomationPingResponse>
                    {
                        Response = await client.PingAsync(cancellationToken).ConfigureAwait(false),
                    },
                    AutomationJsonContext.Default.AutomationPingResponse);
                return 0;
            case "describe":
                arguments.EnsureEmpty();
                WriteTyped(
                    output,
                    new AutomationTypedInvocationResult<AutomationInstanceDescriptor>
                    {
                        Response = await client.DescribeAsync(cancellationToken).ConfigureAwait(false),
                    },
                    AutomationJsonContext.Default.AutomationInstanceDescriptor);
                return 0;
            case "capabilities":
                return await RunCapabilitiesAsync(arguments, client, output, cancellationToken)
                    .ConfigureAwait(false);
            case "help":
                return await RunCapabilityHelpAsync(arguments, client, output, cancellationToken)
                    .ConfigureAwait(false);
            case "call":
                return await RunRawCallAsync(arguments, client, output, cancellationToken)
                    .ConfigureAwait(false);
            case "events":
                return await RunEventsAsync(arguments, client, output, cancellationToken)
                    .ConfigureAwait(false);
            case "transaction":
                return await RunTransactionAsync(arguments, client, output, cancellationToken)
                    .ConfigureAwait(false);
            case "build":
                return await RunBuildAsync(arguments, client, output, cancellationToken)
                    .ConfigureAwait(false);
            case "player":
                return await RunPlayerAsync(arguments, client, output, cancellationToken)
                    .ConfigureAwait(false);
            case "artifact":
                return await RunArtifactAsync(arguments, client, output, cancellationToken)
                    .ConfigureAwait(false);
            default:
                throw new CliUsageException($"未知 command '{command}'。使用 --help 查看命令。");
        }
    }

    private static async Task<int> RunCapabilitiesAsync(
        CliArguments arguments,
        EditorAutomationClient client,
        CliOutput output,
        CancellationToken cancellationToken)
    {
        string? domain = arguments.TakeOption("--domain");
        string? scope = arguments.TakeOption("--scope");
        bool matrix = arguments.TakeFlag("--matrix");
        arguments.EnsureEmpty();
        if (matrix)
        {
            if (domain is not null || scope is not null)
            {
                throw new CliUsageException("--matrix 需要完整闭包，不能与 --domain/--scope 过滤组合。");
            }

            AutomationTypedInvocationResult<AutomationCapabilityMatrixSnapshot> result =
                await client.GetCapabilityMatrixAsync(cancellationToken).ConfigureAwait(false);
            output.WriteCapabilityMatrix(result.Response, result.Revision);
            return 0;
        }

        AutomationCapabilityCatalog catalog = await client.GetCapabilitiesAsync(
            cancellationToken: cancellationToken).ConfigureAwait(false);
        AutomationCapabilityDescriptor[] filtered =
        [
            .. catalog.Items.Where(item =>
                (domain is null || string.Equals(item.Domain, domain, StringComparison.Ordinal)) &&
                (scope is null || item.RequiredScopes.Contains(scope, StringComparer.Ordinal))),
        ];
        output.WriteCapabilities(catalog with { Items = filtered });
        return 0;
    }

    private static async Task<int> RunCapabilityHelpAsync(
        CliArguments arguments,
        EditorAutomationClient client,
        CliOutput output,
        CancellationToken cancellationToken)
    {
        string method = arguments.TakeRequiredPositional("capability method");
        arguments.EnsureEmpty();
        AutomationCapabilityCatalog catalog = await client.GetCapabilitiesAsync(
            cancellationToken: cancellationToken).ConfigureAwait(false);
        AutomationCapabilityDescriptor descriptor = RequireCapability(catalog, method);
        output.WriteCapability(descriptor);
        return 0;
    }

    private static async Task<int> RunRawCallAsync(
        CliArguments arguments,
        EditorAutomationClient client,
        CliOutput output,
        CancellationToken cancellationToken)
    {
        string method = arguments.TakeRequiredPositional("method");
        string? inlinePayload = arguments.TakeOption("--payload");
        string? payloadFile = arguments.TakeOption("--payload-file");
        bool verifyArtifact = arguments.TakeFlag("--verify-artifact");
        if (inlinePayload is not null && payloadFile is not null)
        {
            throw new CliUsageException("--payload 与 --payload-file 只能指定一个。");
        }

        JsonElement? payload = inlinePayload is not null
            ? ParseJson(inlinePayload)
            : payloadFile is not null
                ? await ReadPayloadFileAsync(payloadFile, cancellationToken).ConfigureAwait(false)
                : null;
        AutomationCapabilityCatalog catalog = await client.GetCapabilitiesAsync(
            cancellationToken: cancellationToken).ConfigureAwait(false);
        AutomationCapabilityDescriptor descriptor = RequireCapability(catalog, method);
        AutomationInvocationOptions invocationOptions = CreateInvocationOptions(
            arguments,
            descriptor,
            catalog.Revision);
        arguments.EnsureEmpty();
        AutomationInvocationResult result = await client.InvokeDetailedAsync(
            method,
            payload,
            invocationOptions,
            cancellationToken).ConfigureAwait(false);
        if (verifyArtifact)
        {
            AutomationArtifactReference artifact = result.Payload?.Deserialize(
                AutomationJsonContext.Default.AutomationArtifactReference)
                ?? throw new CliUsageException("响应不是 artifactReference。");
            AutomationArtifactVerification verification = await client.VerifyArtifactAsync(
                artifact,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            output.WriteArtifact(artifact, verification);
            return verification.Verified ? 0 : 6;
        }

        output.WriteInvocation(result.Payload, result.Revision);
        return 0;
    }

    private static async Task<int> RunEventsAsync(
        CliArguments arguments,
        EditorAutomationClient client,
        CliOutput output,
        CancellationToken cancellationToken)
    {
        string subcommand = arguments.TakeRequiredPositional("events subcommand");
        if (!string.Equals(subcommand, "follow", StringComparison.Ordinal))
        {
            throw new CliUsageException("events 只支持 follow。");
        }

        if (output.Mode == CliOutputMode.Json)
        {
            throw new CliUsageException("events follow 是流式输出，请使用 compact 或 --output ndjson。");
        }

        string subscriptionKey = arguments.TakeOption("--subscription-key") ??
            $"cli-{Guid.NewGuid():N}";
        string[] eventTypes = SplitCsv(arguments.TakeOption("--types"));
        string? resumeToken = arguments.TakeOption("--resume-token");
        long? afterSequence = ParseNullableLong(arguments.TakeOption("--after-sequence"), "--after-sequence");
        int backlog = ParseInt(arguments.TakeOption("--backlog"), 1024, 1, 4096, "--backlog");
        int maxEvents = ParseInt(arguments.TakeOption("--max-events"), int.MaxValue, 1, int.MaxValue, "--max-events");
        bool acknowledge = !arguments.TakeFlag("--no-ack");
        arguments.EnsureEmpty();
        AutomationEventSubscription subscription = await client.SubscribeOrResumeEventsAsync(
            new AutomationEventResumeState
            {
                SubscriptionKey = subscriptionKey,
                EventTypes = eventTypes,
                ResumeToken = resumeToken,
                AcknowledgedSequence = afterSequence,
                BacklogLimit = backlog,
            },
            cancellationToken).ConfigureAwait(false);
        output.WriteResumeState(subscription);
        int count = 0;
        await foreach (AutomationEventRecord record in client.ReadEventsAsync(cancellationToken)
                           .ConfigureAwait(false))
        {
            output.WriteEvent(record);
            if (acknowledge)
            {
                subscription = await client.AcknowledgeEventsAsync(
                    subscription,
                    record.Sequence,
                    cancellationToken).ConfigureAwait(false);
            }

            count++;
            if (count >= maxEvents)
            {
                break;
            }
        }

        output.WriteResumeState(subscription);
        return 0;
    }

    private static async Task<int> RunTransactionAsync(
        CliArguments arguments,
        EditorAutomationClient client,
        CliOutput output,
        CancellationToken cancellationToken)
    {
        string subcommand = arguments.TakeRequiredPositional("transaction subcommand");
        if (!string.Equals(subcommand, "execute", StringComparison.Ordinal))
        {
            throw new CliUsageException("transaction 只支持 execute。");
        }

        string planPath = arguments.TakeOption("--plan-file") ??
            throw new CliUsageException("transaction execute 需要 --plan-file PATH。");
        arguments.EnsureEmpty();
        JsonElement planJson = await ReadPayloadFileAsync(planPath, cancellationToken).ConfigureAwait(false);
        CliTransactionPlan plan = CliTransactionPlanReader.Parse(planJson);
        AutomationCapabilityCatalog catalog = await client.GetCapabilitiesAsync(
            cancellationToken: cancellationToken).ConfigureAwait(false);
        AutomationCapabilityDescriptor[] descriptors = new AutomationCapabilityDescriptor[plan.Operations.Length];
        for (int i = 0; i < plan.Operations.Length; i++)
        {
            descriptors[i] = RequireCapability(catalog, plan.Operations[i].Method);
            if (descriptors[i].OperationKind == AutomationOperationKind.Read ||
                descriptors[i].TransactionMode == AutomationTransactionMode.Forbidden)
            {
                throw new CliUsageException(
                    $"Capability '{descriptors[i].Id}' 不支持 transaction staging。");
            }
        }

        string beginKey = DeriveTransactionIdempotencyKey("begin", plan.IdempotencyKey);
        string commitKey = DeriveTransactionIdempotencyKey("commit", plan.IdempotencyKey);
        string rollbackKey = DeriveTransactionIdempotencyKey("rollback", plan.IdempotencyKey);
        AutomationInvocationResult begun = await client.InvokeDetailedAsync(
            AutomationProtocolConstants.TransactionBeginMethod,
            JsonSerializer.SerializeToElement(
                new AutomationTransactionBeginRequest
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    Name = plan.Name,
                    LeaseMilliseconds = plan.LeaseMilliseconds,
                },
                AutomationJsonContext.Default.AutomationTransactionBeginRequest),
            new AutomationInvocationOptions { IdempotencyKey = beginKey },
            cancellationToken).ConfigureAwait(false);
        AutomationTransactionInfo transaction = begun.Payload?.Deserialize(
            AutomationJsonContext.Default.AutomationTransactionInfo)
            ?? throw new InvalidDataException("transaction.begin 未返回 transactionInfo。");
        bool completed = false;
        try
        {
            for (int i = 0; i < plan.Operations.Length; i++)
            {
                CliTransactionOperation operation = plan.Operations[i];
                AutomationInvocationResult staged = await client.InvokeDetailedAsync(
                    operation.Method,
                    operation.Payload,
                    new AutomationInvocationOptions
                    {
                        ExpectedRevision = AutomationRevisionPreconditions.FromSnapshot(transaction.BaseRevision),
                        IdempotencyKey = operation.IdempotencyKey,
                        TransactionId = transaction.TransactionId,
                    },
                    cancellationToken).ConfigureAwait(false);
                AutomationTransactionStagedOperationInfo stagedInfo = staged.Payload?.Deserialize(
                    AutomationJsonContext.Default.AutomationTransactionStagedOperationInfo)
                    ?? throw new InvalidDataException(
                        $"Transaction operation[{i}] 未返回 staging 回执。");
                if (stagedInfo.Ordinal != i ||
                    !string.Equals(stagedInfo.TransactionId, transaction.TransactionId, StringComparison.Ordinal) ||
                    !string.Equals(stagedInfo.Method, operation.Method, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Transaction operation[{i}] staging 回执身份不匹配。");
                }
            }

            AutomationInvocationResult committed = await client.InvokeDetailedAsync(
                AutomationProtocolConstants.TransactionCommitMethod,
                SerializeTransactionRequest(transaction.TransactionId),
                new AutomationInvocationOptions { IdempotencyKey = commitKey },
                cancellationToken).ConfigureAwait(false);
            AutomationTransactionCommitResult commitResult = committed.Payload?.Deserialize(
                AutomationJsonContext.Default.AutomationTransactionCommitResult)
                ?? throw new InvalidDataException("transaction.commit 未返回 transactionCommitResult。");
            if (commitResult.Operations.Length != plan.Operations.Length ||
                commitResult.Transaction.Status != AutomationTransactionStatus.Committed)
            {
                throw new InvalidDataException("transaction.commit 返回的 operation 数量或终态不匹配。");
            }

            for (int i = 0; i < commitResult.Operations.Length; i++)
            {
                if (!string.Equals(
                        commitResult.Operations[i].Method,
                        plan.Operations[i].Method,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"transaction.commit operation[{i}] method 不匹配。");
                }
            }

            completed = true;
            output.WriteInvocation(committed.Payload, committed.Revision);
            return 0;
        }
        catch (Exception exception)
        {
            if (!completed)
            {
                try
                {
                    await RecoverTransactionAfterFailureAsync(
                        client,
                        transaction.TransactionId,
                        rollbackKey).ConfigureAwait(false);
                }
                catch (Exception recoveryException)
                {
                    throw new InvalidOperationException(
                        "Transaction execute 失败，且同 session 终态恢复或验证也失败。",
                        new AggregateException(exception, recoveryException));
                }
            }

            throw;
        }
    }

    private static async Task RecoverTransactionAfterFailureAsync(
        EditorAutomationClient client,
        string transactionId,
        string rollbackKey)
    {
        JsonElement request = SerializeTransactionRequest(transactionId);
        Exception? statusFailure = null;
        try
        {
            using CancellationTokenSource statusTimeout = new(TimeSpan.FromSeconds(10));
            AutomationInvocationResult statusResult = await client.InvokeDetailedAsync(
                AutomationProtocolConstants.TransactionStatusMethod,
                request,
                cancellationToken: statusTimeout.Token).ConfigureAwait(false);
            AutomationTransactionInfo status = statusResult.Payload?.Deserialize(
                AutomationJsonContext.Default.AutomationTransactionInfo)
                ?? throw new InvalidDataException("transaction.status 未返回 transactionInfo。");
            ValidateRecoveredTransaction(status, transactionId, allowActive: true);
            if (status.Status != AutomationTransactionStatus.Active)
            {
                return;
            }
        }
        catch (Exception exception)
        {
            statusFailure = exception;
        }

        try
        {
            using CancellationTokenSource rollbackTimeout = new(TimeSpan.FromSeconds(10));
            AutomationInvocationResult rollbackResult = await client.InvokeDetailedAsync(
                AutomationProtocolConstants.TransactionRollbackMethod,
                request,
                new AutomationInvocationOptions { IdempotencyKey = rollbackKey },
                rollbackTimeout.Token).ConfigureAwait(false);
            AutomationTransactionInfo rolledBack = rollbackResult.Payload?.Deserialize(
                AutomationJsonContext.Default.AutomationTransactionInfo)
                ?? throw new InvalidDataException("transaction.rollback 未返回 transactionInfo。");
            ValidateRecoveredTransaction(rolledBack, transactionId, allowActive: false);
        }
        catch (Exception rollbackFailure) when (statusFailure is not null)
        {
            throw new AggregateException(
                "transaction.status 与 transaction.rollback 均失败。",
                statusFailure,
                rollbackFailure);
        }
    }

    internal static void ValidateRecoveredTransaction(
        AutomationTransactionInfo transaction,
        string expectedTransactionId,
        bool allowActive)
    {
        bool validStatus = allowActive
            ? transaction.Status is
                AutomationTransactionStatus.Active or
                AutomationTransactionStatus.RolledBack or
                AutomationTransactionStatus.Expired or
                AutomationTransactionStatus.Committed
            : transaction.Status == AutomationTransactionStatus.RolledBack;
        if (!string.Equals(transaction.TransactionId, expectedTransactionId, StringComparison.Ordinal) ||
            !validStatus)
        {
            throw new InvalidDataException(
                $"Transaction 恢复状态不匹配：id={transaction.TransactionId}, status={transaction.Status}。");
        }
    }

    private static JsonElement SerializeTransactionRequest(string transactionId)
    {
        return JsonSerializer.SerializeToElement(
            new AutomationTransactionRequest
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                TransactionId = transactionId,
            },
            AutomationJsonContext.Default.AutomationTransactionRequest);
    }

    private static string DeriveTransactionIdempotencyKey(string operation, string planKey)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(planKey));
        return $"cli-tx-{operation}-{Convert.ToHexString(digest.AsSpan(0, 16)).ToLowerInvariant()}";
    }

    private static async Task<int> RunBuildAsync(
        CliArguments arguments,
        EditorAutomationClient client,
        CliOutput output,
        CancellationToken cancellationToken)
    {
        string subcommand = arguments.TakeRequiredPositional("build subcommand");
        switch (subcommand)
        {
            case "preflight":
                {
                    arguments.EnsureEmpty();
                    AutomationTypedInvocationResult<AutomationBuildPreflightResult> result =
                        await client.PreflightBuildAsync(cancellationToken).ConfigureAwait(false);
                    WriteTyped(output, result, AutomationJsonContext.Default.AutomationBuildPreflightResult);
                    return result.Response.Ok ? 0 : 7;
                }
            case "start":
                {
                    bool launch = arguments.TakeFlag("--launch");
                    bool wait = arguments.TakeFlag("--wait");
                    AutomationCapabilityCatalog catalog = await client.GetCapabilitiesAsync(
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    AutomationCapabilityDescriptor descriptor = RequireCapability(
                        catalog,
                        AutomationProtocolConstants.BuildStartMethod);
                    AutomationInvocationOptions commandOptions = CreateInvocationOptions(
                        arguments,
                        descriptor,
                        catalog.Revision);
                    arguments.EnsureEmpty();
                    AutomationTypedInvocationResult<AutomationBuildSnapshot> started =
                        await client.StartBuildAsync(
                            new AutomationBuildStartRequest { LaunchOnSuccess = launch },
                            commandOptions,
                            cancellationToken).ConfigureAwait(false);
                    AutomationTypedInvocationResult<AutomationBuildSnapshot> result = !wait
                        ? started
                        : await client.WaitForBuildAsync(
                            started.Response.BuildId,
                            new AutomationInvocationOptions { Timeout = commandOptions.Timeout },
                            cancellationToken).ConfigureAwait(false);
                    WriteTyped(output, result, AutomationJsonContext.Default.AutomationBuildSnapshot);
                    return !wait || BuildCommandSucceeded(result.Response) ? 0 : 7;
                }
            case "list":
                {
                    AutomationPageRequest page = ParsePageRequest(arguments);
                    arguments.EnsureEmpty();
                    List<AutomationBuildSnapshot> builds = [];
                    await foreach (AutomationBuildSnapshot build in client.EnumerateBuildsAsync(
                                       page,
                                       cancellationToken: cancellationToken).ConfigureAwait(false))
                    {
                        builds.Add(build);
                    }

                    WriteArray(output, [.. builds], AutomationJsonContext.Default.AutomationBuildSnapshotArray);
                    return 0;
                }
            case "get":
            case "wait":
                {
                    string buildId = arguments.TakeRequiredPositional("build ID");
                    AutomationInvocationOptions options = CreateReadOptions(arguments);
                    arguments.EnsureEmpty();
                    AutomationTypedInvocationResult<AutomationBuildSnapshot> result = subcommand == "get"
                        ? await client.GetBuildAsync(buildId, options, cancellationToken).ConfigureAwait(false)
                        : await client.WaitForBuildAsync(buildId, options, cancellationToken).ConfigureAwait(false);
                    WriteTyped(output, result, AutomationJsonContext.Default.AutomationBuildSnapshot);
                    return subcommand == "get" || BuildCommandSucceeded(result.Response) ? 0 : 7;
                }
            case "cancel":
                {
                    string buildId = arguments.TakeRequiredPositional("build ID");
                    AutomationTypedInvocationResult<AutomationBuildSnapshot> before =
                        await client.GetBuildAsync(buildId, cancellationToken: cancellationToken).ConfigureAwait(false);
                    AutomationCapabilityCatalog catalog = await client.GetCapabilitiesAsync(
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    AutomationInvocationOptions options = CreateInvocationOptions(
                        arguments,
                        RequireCapability(catalog, AutomationProtocolConstants.BuildCancelMethod),
                        before.Revision);
                    arguments.EnsureEmpty();
                    AutomationTypedInvocationResult<AutomationBuildSnapshot> result = await client.CancelBuildAsync(
                        buildId,
                        options,
                        cancellationToken).ConfigureAwait(false);
                    WriteTyped(output, result, AutomationJsonContext.Default.AutomationBuildSnapshot);
                    return 0;
                }
            case "logs":
                {
                    string buildId = arguments.TakeRequiredPositional("build ID");
                    bool verify = !arguments.TakeFlag("--no-verify");
                    arguments.EnsureEmpty();
                    AutomationTypedInvocationResult<AutomationArtifactReference> result =
                        await client.ExportBuildLogAsync(
                            buildId,
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                    AutomationArtifactVerification? verification = verify
                        ? await client.VerifyArtifactAsync(
                            result.Response,
                            cancellationToken: cancellationToken).ConfigureAwait(false)
                        : null;
                    output.WriteArtifact(result.Response, verification);
                    return verification is { Verified: false } ? 6 : 0;
                }
            default:
                throw new CliUsageException($"未知 build subcommand '{subcommand}'。");
        }
    }

    private static async Task<int> RunPlayerAsync(
        CliArguments arguments,
        EditorAutomationClient client,
        CliOutput output,
        CancellationToken cancellationToken)
    {
        string subcommand = arguments.TakeRequiredPositional("player subcommand");
        switch (subcommand)
        {
            case "launch":
                {
                    string buildId = arguments.TakeRequiredPositional("build ID");
                    bool wait = arguments.TakeFlag("--wait");
                    AutomationTypedInvocationResult<AutomationBuildSnapshot> build =
                        await client.GetBuildAsync(buildId, cancellationToken: cancellationToken).ConfigureAwait(false);
                    AutomationCapabilityCatalog catalog = await client.GetCapabilitiesAsync(
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    AutomationInvocationOptions options = CreateInvocationOptions(
                        arguments,
                        RequireCapability(catalog, AutomationProtocolConstants.PlayerLaunchMethod),
                        build.Revision);
                    arguments.EnsureEmpty();
                    AutomationTypedInvocationResult<AutomationPlayerProcessSnapshot> launched =
                        await client.LaunchPlayerAsync(buildId, options, cancellationToken).ConfigureAwait(false);
                    AutomationTypedInvocationResult<AutomationPlayerProcessSnapshot> result = !wait
                        ? launched
                        : await client.WaitForPlayerAsync(
                            launched.Response.PlayerProcessId,
                            new AutomationInvocationOptions { Timeout = options.Timeout },
                            cancellationToken).ConfigureAwait(false);
                    WriteTyped(output, result, AutomationJsonContext.Default.AutomationPlayerProcessSnapshot);
                    return !wait ||
                        result.Response is
                        {
                            State: AutomationPlayerProcessState.Exited,
                            ExitCode: 0,
                        }
                        ? 0
                        : 8;
                }
            case "list":
                {
                    AutomationPageRequest page = ParsePageRequest(arguments);
                    arguments.EnsureEmpty();
                    List<AutomationPlayerProcessSnapshot> players = [];
                    await foreach (AutomationPlayerProcessSnapshot player in client.EnumeratePlayersAsync(
                                       page,
                                       cancellationToken: cancellationToken).ConfigureAwait(false))
                    {
                        players.Add(player);
                    }

                    WriteArray(
                        output,
                        [.. players],
                        AutomationJsonContext.Default.AutomationPlayerProcessSnapshotArray);
                    return 0;
                }
            case "get":
            case "wait":
                {
                    string playerId = arguments.TakeRequiredPositional("player process ID");
                    AutomationInvocationOptions options = CreateReadOptions(arguments);
                    arguments.EnsureEmpty();
                    AutomationTypedInvocationResult<AutomationPlayerProcessSnapshot> result = subcommand == "get"
                        ? await client.GetPlayerAsync(playerId, options, cancellationToken).ConfigureAwait(false)
                        : await client.WaitForPlayerAsync(playerId, options, cancellationToken).ConfigureAwait(false);
                    WriteTyped(output, result, AutomationJsonContext.Default.AutomationPlayerProcessSnapshot);
                    return subcommand == "get" ||
                        result.Response is
                        {
                            State: AutomationPlayerProcessState.Exited,
                            ExitCode: 0,
                        }
                        ? 0
                        : 8;
                }
            case "terminate":
                {
                    string playerId = arguments.TakeRequiredPositional("player process ID");
                    bool entireTree = !arguments.TakeFlag("--single-process");
                    bool wait = arguments.TakeFlag("--wait");
                    AutomationTypedInvocationResult<AutomationPlayerProcessSnapshot> before =
                        await client.GetPlayerAsync(playerId, cancellationToken: cancellationToken).ConfigureAwait(false);
                    AutomationCapabilityCatalog catalog = await client.GetCapabilitiesAsync(
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    AutomationInvocationOptions options = CreateInvocationOptions(
                        arguments,
                        RequireCapability(catalog, AutomationProtocolConstants.PlayerTerminateMethod),
                        before.Revision);
                    arguments.EnsureEmpty();
                    AutomationTypedInvocationResult<AutomationPlayerProcessSnapshot> terminated =
                        await client.TerminatePlayerAsync(
                            playerId,
                            entireTree,
                            options,
                            cancellationToken).ConfigureAwait(false);
                    AutomationTypedInvocationResult<AutomationPlayerProcessSnapshot> result = !wait ||
                        terminated.Response.State == AutomationPlayerProcessState.Exited
                        ? terminated
                        : await client.WaitForPlayerAsync(
                            playerId,
                            new AutomationInvocationOptions { Timeout = options.Timeout },
                            cancellationToken).ConfigureAwait(false);
                    WriteTyped(output, result, AutomationJsonContext.Default.AutomationPlayerProcessSnapshot);
                    return !wait || result.Response.State == AutomationPlayerProcessState.Exited ? 0 : 8;
                }
            default:
                throw new CliUsageException($"未知 player subcommand '{subcommand}'。");
        }
    }

    private static async Task<int> RunArtifactAsync(
        CliArguments arguments,
        EditorAutomationClient client,
        CliOutput output,
        CancellationToken cancellationToken)
    {
        string subcommand = arguments.TakeRequiredPositional("artifact subcommand");
        if (!string.Equals(subcommand, "verify", StringComparison.Ordinal))
        {
            throw new CliUsageException("artifact 只支持 verify。");
        }

        string artifactId = arguments.TakeRequiredPositional("artifact ID");
        arguments.EnsureEmpty();
        AutomationArtifactReference reference = await FindArtifactAsync(
            client,
            artifactId,
            cancellationToken).ConfigureAwait(false);
        AutomationArtifactVerification verification = await client.VerifyArtifactAsync(
            reference,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        output.WriteArtifact(reference, verification);
        return verification.Verified ? 0 : 6;
    }

    private static async ValueTask<AutomationArtifactReference> FindArtifactAsync(
        EditorAutomationClient client,
        string artifactId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.GetArtifactAsync(artifactId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (KeyNotFoundException exception)
        {
            throw new CliUsageException(exception.Message);
        }
    }

    private static AutomationInvocationOptions CreateInvocationOptions(
        CliArguments arguments,
        AutomationCapabilityDescriptor descriptor,
        AutomationRevisionSnapshot? fallbackRevision)
    {
        string? expectedGlobalValue = arguments.TakeOption("--expected-global");
        string[] expectedResourceValues = arguments.TakeOptions("--expected-resource");
        AutomationRevisionPrecondition? expected = ParseExpectedRevision(
            expectedGlobalValue,
            expectedResourceValues);
        if (descriptor.RequiresExpectedRevision && expected is null)
        {
            expected = fallbackRevision is null
                ? throw new CliUsageException(
                    $"{descriptor.Id} 需要 expected revision；请提供 --expected-global/--expected-resource。")
                : AutomationRevisionPreconditions.FromSnapshot(fallbackRevision);
        }

        string? idempotencyKey = arguments.TakeOption("--idempotency-key");
        if (descriptor.RequiresIdempotencyKey && string.IsNullOrWhiteSpace(idempotencyKey))
        {
            idempotencyKey = $"cli-{Guid.NewGuid():N}";
        }

        string? transactionId = arguments.TakeOption("--transaction");
        string? timeout = arguments.TakeOption("--request-timeout");
        return new AutomationInvocationOptions
        {
            Timeout = timeout is null
                ? null
                : CliArguments.ParseDuration(timeout, default, "--request-timeout"),
            ExpectedRevision = expected,
            IdempotencyKey = idempotencyKey,
            TransactionId = transactionId,
        };
    }

    private static AutomationInvocationOptions CreateReadOptions(CliArguments arguments)
    {
        string? timeout = arguments.TakeOption("--request-timeout");
        return new AutomationInvocationOptions
        {
            Timeout = timeout is null
                ? null
                : CliArguments.ParseDuration(timeout, default, "--request-timeout"),
        };
    }

    private static AutomationRevisionPrecondition? ParseExpectedRevision(
        string? globalValue,
        string[] resourceValues)
    {
        if (globalValue is null && resourceValues.Length == 0)
        {
            return null;
        }

        long? global = globalValue is null ? null : ParseNonNegativeLong(globalValue, "--expected-global");
        AutomationExpectedResourceRevision[] resources = new AutomationExpectedResourceRevision[resourceValues.Length];
        HashSet<string> ids = new(StringComparer.Ordinal);
        for (int i = 0; i < resourceValues.Length; i++)
        {
            int separator = resourceValues[i].LastIndexOf('=');
            if (separator <= 0 || separator == resourceValues[i].Length - 1)
            {
                throw new CliUsageException("--expected-resource 格式为 resourceId=revision。");
            }

            string resourceId = resourceValues[i][..separator];
            if (!ids.Add(resourceId))
            {
                throw new CliUsageException($"重复 expected resource '{resourceId}'。");
            }

            resources[i] = new AutomationExpectedResourceRevision
            {
                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                ResourceId = resourceId,
                Revision = ParseNonNegativeLong(
                    resourceValues[i][(separator + 1)..],
                    "--expected-resource"),
            };
        }

        return new AutomationRevisionPrecondition
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            GlobalRevision = global,
            Resources = resources,
        };
    }

    private static AutomationPageRequest ParsePageRequest(CliArguments arguments)
    {
        int pageSize = ParseInt(arguments.TakeOption("--page-size"), 100, 1, 500, "--page-size");
        string? cursor = arguments.TakeOption("--cursor");
        string? filterJson = arguments.TakeOption("--filter-json");
        AutomationQueryFilter? filter = filterJson is null
            ? null
            : ParseJson(filterJson).Deserialize(AutomationJsonContext.Default.AutomationQueryFilter)
                ?? throw new CliUsageException("--filter-json 返回 null。");
        string[] sortValues = arguments.TakeOptions("--sort");
        AutomationSortClause[] sort = new AutomationSortClause[sortValues.Length];
        for (int i = 0; i < sortValues.Length; i++)
        {
            string[] parts = sortValues[i].Split(':', 2);
            sort[i] = new AutomationSortClause
            {
                Field = parts[0],
                Direction = parts.Length == 1 || parts[1] == "asc"
                    ? AutomationSortDirection.Ascending
                    : parts[1] == "desc"
                        ? AutomationSortDirection.Descending
                        : throw new CliUsageException("--sort 格式为 field[:asc|desc]。"),
            };
        }

        return new AutomationPageRequest
        {
            Filter = filter,
            Sort = sort,
            Cursor = cursor,
            PageSize = pageSize,
        };
    }

    private static async ValueTask<AutomationDiscoveredInstance> SelectInstanceAsync(
        CliGlobalOptions globals,
        CancellationToken cancellationToken)
    {
        AutomationDiscoverySnapshot discovery = await AutomationDiscovery.DiscoverAsync(
            globals.DiscoveryRoot,
            cancellationToken).ConfigureAwait(false);
        return globals.InstanceId is { } instanceId
            ? discovery.Instances.SingleOrDefault(instance => string.Equals(
                    instance.Descriptor.InstanceId,
                    instanceId,
                    StringComparison.Ordinal)) ??
                throw new AutomationConnectionException(
                    $"Discovery 中不存在实例 '{instanceId}'。诊断数={discovery.Diagnostics.Length}。")
            : discovery.Instances.Length switch
            {
                1 => discovery.Instances[0],
                0 => throw new AutomationConnectionException(
                    $"Discovery root '{globals.DiscoveryRoot}' 没有 live Editor 实例。"),
                _ => throw new CliUsageException(
                    $"发现 {discovery.Instances.Length} 个实例；请用 --instance 选择。"),
            };
    }

    private static AutomationCapabilityDescriptor RequireCapability(
        AutomationCapabilityCatalog catalog,
        string method)
    {
        return catalog.Items.SingleOrDefault(item => string.Equals(item.Id, method, StringComparison.Ordinal)) ??
            throw new CliUsageException($"当前实例未发布 capability '{method}'。");
    }

    private static void WriteTyped<T>(
        CliOutput output,
        AutomationTypedInvocationResult<T> result,
        JsonTypeInfo<T> typeInfo)
    {
        output.WriteInvocation(JsonSerializer.SerializeToElement(result.Response, typeInfo), result.Revision);
    }

    private static void WriteArray<T>(CliOutput output, T[] items, JsonTypeInfo<T[]> typeInfo)
    {
        output.WriteInvocation(JsonSerializer.SerializeToElement(items, typeInfo), revision: null);
    }

    private static JsonElement ParseJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            MaxDepth = 128,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
        });
        return document.RootElement.Clone();
    }

    private static async ValueTask<JsonElement> ReadPayloadFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(path);
        FileInfo file = new(fullPath);
        if (!file.Exists || file.Length is <= 0 or > AutomationProtocolConstants.DefaultMaxFrameBytes)
        {
            throw new CliUsageException("Payload 文件不存在、为空或超过 frame 上限。");
        }

        byte[] bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        return ParseJson(Encoding.UTF8.GetString(bytes));
    }

    private static string[] SplitCsv(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            :
            [
                .. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal),
            ];
    }

    private static int ParseInt(string? value, int defaultValue, int minimum, int maximum, string option)
    {
        return value is null
            ? defaultValue
            : int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) &&
                parsed >= minimum && parsed <= maximum
                ? parsed
                : throw new CliUsageException($"{option} 必须位于 {minimum}..{maximum}。");
    }

    private static long? ParseNullableLong(string? value, string option)
    {
        return value is null ? null : ParseNonNegativeLong(value, option);
    }

    private static long ParseNonNegativeLong(string value, string option)
    {
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed) && parsed >= 0
            ? parsed
            : throw new CliUsageException($"{option} 必须是非负整数。");
    }

    private static bool BuildCommandSucceeded(AutomationBuildSnapshot snapshot)
    {
        return snapshot.State == AutomationBuildState.Succeeded &&
            (!snapshot.LaunchOnSuccess || snapshot.PlayerProcessId is not null);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("pixelengine-editor [global options] <command>");
        Console.WriteLine("commands: discover, ping, describe, capabilities, help <method>, call <method>");
        Console.WriteLine("          events follow, transaction execute, build preflight|start|list|get|wait|cancel|logs");
        Console.WriteLine("          player launch|list|get|wait|terminate, artifact verify");
        Console.WriteLine("global:   --discovery-root PATH --instance ID --credential PATH --scopes CSV");
        Console.WriteLine("          --client-instance-id ID --connect-timeout SEC --timeout SEC");
        Console.WriteLine("          --output compact|json|ndjson --version（默认 scope: editor.read）");
        Console.WriteLine("writes:   --expected-global N --expected-resource ID=N --idempotency-key KEY");
        Console.WriteLine("          --transaction ID --request-timeout SEC");
        Console.WriteLine("call:     --payload JSON | --payload-file PATH [--verify-artifact]");
        Console.WriteLine("tx:       transaction execute --plan-file PATH（同一连接 begin→stage→commit/rollback）");
        Console.WriteLine("matrix:   capabilities --matrix（独立校验双向 UI/capability 闭包与 SHA256）");
        Console.WriteLine("lists:    --page-size N --filter-json JSON --sort field[:asc|desc] --cursor TOKEN");
    }
}
