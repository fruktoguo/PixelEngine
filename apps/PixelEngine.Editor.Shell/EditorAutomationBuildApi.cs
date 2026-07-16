using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;
using PixelEngine.Editor.Automation.Server;
using PixelEngine.Editor.Shell.Build;

namespace PixelEngine.Editor.Shell;

internal sealed partial class EditorAutomationAuthoringApi
{
    private const string BuildResource = "editor:build";
    private const string PlayerResource = "editor:player";

    private AutomationMethodRegistration[] CreateBuildAndPlayerRegistrations()
    {
        return
        [
            ExternalRead(
                AutomationProtocolConstants.BuildPanelGetMethod,
                "build",
                "emptyRequest",
                "buildPanelSnapshot",
                [AutomationScopes.EditorRead],
                [],
                GetBuildPanel),
            ExternalCommand(
                AutomationProtocolConstants.BuildPanelSetMethod,
                "build",
                "buildPanelSetRequest",
                "buildPanelSnapshot",
                [AutomationScopes.EditorControl],
                AllModes,
                ["panel.build-settings.log.auto-scroll"],
                SetBuildPanel),
            ExternalRead(
                AutomationProtocolConstants.BuildPreflightMethod,
                "build",
                "emptyRequest",
                "buildPreflightResult",
                [AutomationScopes.ProcessBuild],
                ["panel.build-settings.preflight"],
                GetBuildPreflight,
                preparation: PrepareBuildPreflight),
            ExternalCommand(
                AutomationProtocolConstants.BuildStartMethod,
                "build",
                "buildStartRequest",
                "buildSnapshot",
                [AutomationScopes.ProcessBuild],
                ["edit"],
                ["panel.build-settings.build", "panel.build-settings.build-and-run"],
                StartBuild),
            ExternalRead(
                AutomationProtocolConstants.BuildListMethod,
                "build",
                "pageRequest",
                "buildListResponse",
                [AutomationScopes.ProcessBuild],
                ["panel.build-settings.progress"],
                ListBuilds),
            ExternalRead(
                AutomationProtocolConstants.BuildGetMethod,
                "build",
                "buildRequest",
                "buildSnapshot",
                [AutomationScopes.ProcessBuild],
                ["panel.build-settings.progress"],
                GetBuild),
            ExternalRead(
                AutomationProtocolConstants.BuildWaitMethod,
                "build",
                "buildRequest",
                "buildSnapshot",
                [AutomationScopes.ProcessBuild],
                [],
                WaitBuild,
                preparation: PrepareBuildWait),
            ExternalCommand(
                AutomationProtocolConstants.BuildCancelMethod,
                "build",
                "buildRequest",
                "buildSnapshot",
                [AutomationScopes.ProcessBuild],
                AllModes,
                ["panel.build-settings.cancel"],
                CancelBuild),
            ExternalRead(
                AutomationProtocolConstants.BuildLogExportMethod,
                "build",
                "buildRequest",
                "artifactReference",
                [AutomationScopes.ProcessBuild],
                ["panel.build-settings.copy-log", "panel.build-settings.open-log"],
                ExportBuildLog,
                AutomationArtifactBehavior.Required),
            ExternalCommand(
                AutomationProtocolConstants.PlayerLaunchMethod,
                "player",
                "playerLaunchRequest",
                "playerProcessSnapshot",
                [AutomationScopes.ProcessLaunch],
                AllModes,
                ["panel.build-settings.build-and-run"],
                LaunchPlayer),
            ExternalRead(
                AutomationProtocolConstants.PlayerListMethod,
                "player",
                "pageRequest",
                "playerProcessListResponse",
                [AutomationScopes.ProcessLaunch],
                [],
                ListPlayers),
            ExternalRead(
                AutomationProtocolConstants.PlayerGetMethod,
                "player",
                "playerProcessRequest",
                "playerProcessSnapshot",
                [AutomationScopes.ProcessLaunch],
                [],
                GetPlayer),
            ExternalRead(
                AutomationProtocolConstants.PlayerWaitMethod,
                "player",
                "playerProcessRequest",
                "playerProcessSnapshot",
                [AutomationScopes.ProcessLaunch],
                [],
                WaitPlayer,
                preparation: PreparePlayerWait),
            ExternalCommand(
                AutomationProtocolConstants.PlayerTerminateMethod,
                "player",
                "playerTerminateRequest",
                "playerProcessSnapshot",
                [AutomationScopes.ProcessLaunch],
                AllModes,
                [],
                TerminatePlayer),
        ];
    }

    private AutomationOperationResult GetBuildPanel(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        EnsureEmpty(payload, AutomationProtocolConstants.BuildPanelGetMethod);
        return Result(
            CaptureBuildPanel(),
            AutomationJsonContext.Default.AutomationBuildPanelSnapshot,
            [BuildResource]);
    }

    private AutomationOperationResult SetBuildPanel(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        AutomationBuildPanelSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationBuildPanelSetRequest,
            AutomationProtocolConstants.BuildPanelSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.BuildPanelSetMethod);
        EditorProjectSession session = RequireSession();
        AutomationBuildPanelSnapshot before = CaptureBuildPanel(session);
        if (before.LogAutoScroll == request.LogAutoScroll)
        {
            return NoChange(
                before,
                AutomationJsonContext.Default.AutomationBuildPanelSnapshot,
                [BuildResource]);
        }

        try
        {
            session.SetAutomationBuildLogAutoScroll(request.LogAutoScroll);
            AutomationBuildPanelSnapshot after = CaptureBuildPanel(session);
            return new AutomationOperationResult
            {
                Payload = JsonSerializer.SerializeToElement(
                    after,
                    AutomationJsonContext.Default.AutomationBuildPanelSnapshot),
                ResourceIds = [BuildResource],
                WriteStateChanged = true,
            };
        }
        catch (Exception exception)
        {
            try
            {
                session.SetAutomationBuildLogAutoScroll(before.LogAutoScroll);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "Build panel 更新失败且无法恢复 before state。",
                    exception,
                    rollbackException);
            }

            throw;
        }
    }

    private AutomationBuildPanelSnapshot CaptureBuildPanel()
    {
        return CaptureBuildPanel(RequireSession());
    }

    private static AutomationBuildPanelSnapshot CaptureBuildPanel(EditorProjectSession session)
    {
        BuildSettingsPanelUiSnapshot snapshot = session.CaptureAutomationBuildPanelState();
        return new AutomationBuildPanelSnapshot
        {
            LogAutoScroll = snapshot.LogAutoScroll,
            BuildRunning = snapshot.BuildRunning,
            RequiresRepair = snapshot.RequiresRepair,
            Diagnostic = snapshot.Diagnostic,
        };
    }

    private static AutomationMethodRegistration ExternalRead(
        string method,
        string domain,
        string requestSchema,
        string responseSchema,
        string[] scopes,
        string[] uiCommandIds,
        AutomationScheduledOperation operation,
        AutomationArtifactBehavior artifactBehavior = AutomationArtifactBehavior.None,
        AutomationScheduledPreparation? preparation = null)
    {
        return Registration(
            method,
            domain,
            requestSchema,
            responseSchema,
            scopes,
            AllModes,
            AutomationOperationKind.Read,
            AutomationTransactionMode.Forbidden,
            requiresExpectedRevision: false,
            requiresIdempotencyKey: false,
            eventTypes: artifactBehavior == AutomationArtifactBehavior.Required
                ? [AutomationProtocolConstants.ArtifactChangedEventType]
                : [],
            uiCommandIds,
            operation,
            artifactBehavior,
            preparation: preparation);
    }

    private static AutomationMethodRegistration ExternalCommand(
        string method,
        string domain,
        string requestSchema,
        string responseSchema,
        string[] scopes,
        string[] modes,
        string[] uiCommandIds,
        AutomationScheduledOperation operation)
    {
        return Registration(
            method,
            domain,
            requestSchema,
            responseSchema,
            scopes,
            modes,
            AutomationOperationKind.Command,
            AutomationTransactionMode.Forbidden,
            requiresExpectedRevision: true,
            requiresIdempotencyKey: true,
            eventTypes: EditorAutomationEventRouting.ForCapability(method, domain),
            uiCommandIds,
            operation);
    }

    private AutomationBackgroundPreparation PrepareBuildPreflight(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        EnsureEmpty(payload, AutomationProtocolConstants.BuildPreflightMethod);
        EditorProjectSession session = RequireSession();
        EditorBuildPreflightWorkspace workspace = session.CaptureAutomationBuildPreflightWorkspace();
        return new AutomationBackgroundPreparation
        {
            PrepareAsync = async cancellationToken => new BuildPreflightPrepared(
                session,
                await workspace.RunAsync(cancellationToken).ConfigureAwait(false)),
        };
    }

    private AutomationOperationResult GetBuildPreflight(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.BuildPreflightMethod);
        BuildPreflightPrepared prepared = context.RequirePreparedState<BuildPreflightPrepared>();
        EnsureCurrentSession(prepared.Session, AutomationProtocolConstants.BuildPreflightMethod);
        return Result(
            MapBuildPreflight(prepared.Preflight),
            AutomationJsonContext.Default.AutomationBuildPreflightResult,
            [BuildResource]);
    }

    private AutomationOperationResult StartBuild(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationBuildStartRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationBuildStartRequest,
            AutomationProtocolConstants.BuildStartMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.BuildStartMethod);
        EditorProjectSession session = RequireEditSession();
        string buildId = Guid.NewGuid().ToString("N");
        string[] resources = BuildResources(buildId);
        context.Revisions.EnsureCanAdvance(resources);
        if (!session.TryStartAutomationBuild(
            buildId,
            request.LaunchOnSuccess,
            out EditorBuildExecutionSnapshot snapshot,
            out string diagnostic))
        {
            throw StateUnavailable(diagnostic);
        }

        try
        {
            JsonElement serialized = JsonSerializer.SerializeToElement(
                MapBuild(snapshot),
                AutomationJsonContext.Default.AutomationBuildSnapshot);
            AutomationRevisionSnapshot revision = AdvanceAndCapture(
                context.Revisions,
                resources,
                resources);
            return new AutomationOperationResult
            {
                Payload = serialized,
                ResourceIds = resources,
                RevisionOverride = revision,
                StateChanged = true,
            };
        }
        catch
        {
            _ = session.RequestAutomationBuildCancellation(
                buildId,
                notifyChanged: false,
                out _);
            throw;
        }
    }

    private AutomationOperationResult ListBuilds(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        AutomationPageRequest request = DeserializePage(
            payload,
            AutomationProtocolConstants.BuildListMethod);
        EditorBuildExecutionSnapshot[] source = RequireSession().CaptureAutomationBuilds();
        AutomationBuildSnapshot[] items = new AutomationBuildSnapshot[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            items[i] = MapBuild(source[i]);
        }

        AutomationBuildSnapshot[] filtered = ApplyFilter(items, request.Filter, MatchBuild);
        SortBuilds(filtered, request.Sort);
        string fingerprint = Fingerprint(
            items,
            AutomationJsonContext.Default.AutomationBuildSnapshotArray);
        PageSlice<AutomationBuildSnapshot> page = SlicePage(
            "build.jobs",
            fingerprint,
            request,
            filtered);
        return Result(
            new AutomationBuildListResponse { Items = page.Items, Page = page.Info },
            AutomationJsonContext.Default.AutomationBuildListResponse,
            [BuildResource]);
    }

    private AutomationOperationResult GetBuild(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        AutomationBuildRequest request = DeserializeBuildRequest(
            payload,
            AutomationProtocolConstants.BuildGetMethod);
        AutomationBuildSnapshot response = MapBuild(
            RequireSession().CaptureAutomationBuild(request.BuildId));
        return Result(
            response,
            AutomationJsonContext.Default.AutomationBuildSnapshot,
            BuildResources(request.BuildId));
    }

    private AutomationBackgroundPreparation PrepareBuildWait(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        AutomationBuildRequest request = DeserializeBuildRequest(
            payload,
            AutomationProtocolConstants.BuildWaitMethod);
        EditorProjectSession session = RequireSession();
        Task<BuildResult> completion = session.CaptureAutomationBuildCompletion(request.BuildId);
        return new AutomationBackgroundPreparation
        {
            PrepareAsync = async cancellationToken =>
            {
                _ = await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
                return new BuildWaitPrepared(session, request.BuildId);
            },
        };
    }

    private AutomationOperationResult WaitBuild(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationBuildRequest request = DeserializeBuildRequest(
            payload,
            AutomationProtocolConstants.BuildWaitMethod);
        BuildWaitPrepared prepared = context.RequirePreparedState<BuildWaitPrepared>();
        EnsurePreparedId(prepared.BuildId, request.BuildId, AutomationProtocolConstants.BuildWaitMethod);
        EnsureCurrentSession(prepared.Session, AutomationProtocolConstants.BuildWaitMethod);
        return Result(
            MapBuild(prepared.Session.CaptureAutomationBuild(request.BuildId)),
            AutomationJsonContext.Default.AutomationBuildSnapshot,
            BuildResources(request.BuildId));
    }

    private AutomationOperationResult CancelBuild(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationBuildRequest request = DeserializeBuildRequest(
            payload,
            AutomationProtocolConstants.BuildCancelMethod);
        EditorProjectSession session = RequireSession();
        string[] resources = BuildResources(request.BuildId);
        EditorBuildExecutionSnapshot before = session.CaptureAutomationBuild(request.BuildId);
        if (before.State != EditorBuildExecutionState.Running || before.CancellationRequested)
        {
            return Result(
                MapBuild(before),
                AutomationJsonContext.Default.AutomationBuildSnapshot,
                resources);
        }

        context.Revisions.EnsureCanAdvance(resources);
        bool changed = session.RequestAutomationBuildCancellation(
            request.BuildId,
            notifyChanged: false,
            out EditorBuildExecutionSnapshot after);
        JsonElement serialized = JsonSerializer.SerializeToElement(
            MapBuild(after),
            AutomationJsonContext.Default.AutomationBuildSnapshot);
        if (!changed)
        {
            return new AutomationOperationResult { Payload = serialized, ResourceIds = resources };
        }

        AutomationRevisionSnapshot revision = AdvanceAndCapture(
            context.Revisions,
            resources,
            resources);
        return new AutomationOperationResult
        {
            Payload = serialized,
            ResourceIds = resources,
            RevisionOverride = revision,
            StateChanged = true,
        };
    }

    private AutomationOperationResult ExportBuildLog(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationBuildRequest request = DeserializeBuildRequest(
            payload,
            AutomationProtocolConstants.BuildLogExportMethod);
        EditorBuildExecutionLogSnapshot source = RequireSession().CaptureAutomationBuildLog(request.BuildId);
        AutomationBuildLogEntry[] entries = new AutomationBuildLogEntry[source.Entries.Length];
        for (int i = 0; i < source.Entries.Length; i++)
        {
            entries[i] = MapBuildLog(source.Entries[i]);
        }

        string sessionId = context.Request.SessionId;
        string requestId = context.Request.RequestId;
        return new AutomationOperationResult
        {
            ResourceIds =
            [
                .. BuildResources(request.BuildId),
                ArtifactResource(sessionId),
            ],
            DeferredPayloadFactory = (revision, cancellationToken) => ExportBuildLogAsync(
                sessionId,
                requestId,
                request.BuildId,
                entries,
                revision,
                cancellationToken),
        };
    }

    private AutomationOperationResult LaunchPlayer(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationPlayerLaunchRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationPlayerLaunchRequest,
            AutomationProtocolConstants.PlayerLaunchMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.PlayerLaunchMethod);
        string buildId = ValidateExternalId(request.BuildId, "buildId");
        EditorProjectSession session = RequireSession();
        string playerProcessId = Guid.NewGuid().ToString("N");
        string[] changedResources = PlayerResources(playerProcessId);
        string[] resources =
        [
            .. changedResources
                .Concat(BuildResources(buildId))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        context.Revisions.EnsureCanAdvance(changedResources);
        EditorPlayerProcessSnapshot launched = session.LaunchAutomationPlayer(
            buildId,
            notifyChanged: false,
            playerProcessId);
        try
        {
            JsonElement serialized = JsonSerializer.SerializeToElement(
                MapPlayer(launched),
                AutomationJsonContext.Default.AutomationPlayerProcessSnapshot);
            AutomationRevisionSnapshot revision = AdvanceAndCapture(
                context.Revisions,
                changedResources,
                resources);
            return new AutomationOperationResult
            {
                Payload = serialized,
                ResourceIds = resources,
                RevisionOverride = revision,
                StateChanged = true,
            };
        }
        catch (Exception operationException)
        {
            try
            {
                _ = session.RequestAutomationPlayerTermination(
                    playerProcessId,
                    entireProcessTree: true,
                    out _);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "player.launch 提交失败，且进程树回滚失败。",
                    operationException,
                    rollbackException);
            }

            throw;
        }
    }

    private AutomationOperationResult ListPlayers(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        AutomationPageRequest request = DeserializePage(
            payload,
            AutomationProtocolConstants.PlayerListMethod);
        EditorPlayerProcessSnapshot[] source = RequireSession().CaptureAutomationPlayers();
        AutomationPlayerProcessSnapshot[] items = new AutomationPlayerProcessSnapshot[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            items[i] = MapPlayer(source[i]);
        }

        AutomationPlayerProcessSnapshot[] filtered = ApplyFilter(items, request.Filter, MatchPlayer);
        SortPlayers(filtered, request.Sort);
        string fingerprint = Fingerprint(
            items,
            AutomationJsonContext.Default.AutomationPlayerProcessSnapshotArray);
        PageSlice<AutomationPlayerProcessSnapshot> page = SlicePage(
            "player.processes",
            fingerprint,
            request,
            filtered);
        return Result(
            new AutomationPlayerProcessListResponse { Items = page.Items, Page = page.Info },
            AutomationJsonContext.Default.AutomationPlayerProcessListResponse,
            [PlayerResource]);
    }

    private AutomationOperationResult GetPlayer(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        AutomationPlayerProcessRequest request = DeserializePlayerRequest(
            payload,
            AutomationProtocolConstants.PlayerGetMethod);
        return Result(
            MapPlayer(RequireSession().CaptureAutomationPlayer(request.PlayerProcessId)),
            AutomationJsonContext.Default.AutomationPlayerProcessSnapshot,
            PlayerResources(request.PlayerProcessId));
    }

    private AutomationBackgroundPreparation PreparePlayerWait(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        AutomationPlayerProcessRequest request = DeserializePlayerRequest(
            payload,
            AutomationProtocolConstants.PlayerWaitMethod);
        EditorProjectSession session = RequireSession();
        EditorPlayerProcessWaitWorkspace workspace =
            session.CaptureAutomationPlayerWaitWorkspace(request.PlayerProcessId);
        return new AutomationBackgroundPreparation
        {
            PrepareAsync = async cancellationToken =>
            {
                _ = await workspace.RunAsync(cancellationToken).ConfigureAwait(false);
                return new PlayerWaitPrepared(session, request.PlayerProcessId);
            },
        };
    }

    private AutomationOperationResult WaitPlayer(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationPlayerProcessRequest request = DeserializePlayerRequest(
            payload,
            AutomationProtocolConstants.PlayerWaitMethod);
        PlayerWaitPrepared prepared = context.RequirePreparedState<PlayerWaitPrepared>();
        EnsurePreparedId(
            prepared.PlayerProcessId,
            request.PlayerProcessId,
            AutomationProtocolConstants.PlayerWaitMethod);
        EnsureCurrentSession(prepared.Session, AutomationProtocolConstants.PlayerWaitMethod);
        return Result(
            MapPlayer(prepared.Session.CaptureAutomationPlayer(request.PlayerProcessId)),
            AutomationJsonContext.Default.AutomationPlayerProcessSnapshot,
            PlayerResources(request.PlayerProcessId));
    }

    private AutomationOperationResult TerminatePlayer(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationPlayerTerminateRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationPlayerTerminateRequest,
            AutomationProtocolConstants.PlayerTerminateMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.PlayerTerminateMethod);
        string playerProcessId = ValidateExternalId(request.PlayerProcessId, "playerProcessId");
        EditorProjectSession session = RequireSession();
        string[] resources = PlayerResources(playerProcessId);
        EditorPlayerProcessSnapshot before = session.CaptureAutomationPlayer(playerProcessId);
        if (before.State == EditorPlayerProcessState.Exited)
        {
            return Result(
                MapPlayer(before),
                AutomationJsonContext.Default.AutomationPlayerProcessSnapshot,
                resources);
        }

        context.Revisions.EnsureCanAdvance(resources);
        bool changed = session.RequestAutomationPlayerTermination(
            playerProcessId,
            request.EntireProcessTree,
            out EditorPlayerProcessSnapshot after);
        JsonElement serialized = JsonSerializer.SerializeToElement(
            MapPlayer(after),
            AutomationJsonContext.Default.AutomationPlayerProcessSnapshot);
        if (!changed)
        {
            return new AutomationOperationResult { Payload = serialized, ResourceIds = resources };
        }

        AutomationRevisionSnapshot revision = AdvanceAndCapture(
            context.Revisions,
            resources,
            resources);
        return new AutomationOperationResult
        {
            Payload = serialized,
            ResourceIds = resources,
            RevisionOverride = revision,
            StateChanged = true,
        };
    }

    private async ValueTask<JsonElement?> ExportBuildLogAsync(
        string sessionId,
        string requestId,
        string buildId,
        AutomationBuildLogEntry[] entries,
        AutomationRevisionSnapshot sourceRevision,
        CancellationToken cancellationToken)
    {
        AutomationArtifactReference artifact = await _artifacts.WriteAsync(
            sessionId,
            "json",
            "application/json",
            sourceRevision,
            (stream, token) => new ValueTask(JsonSerializer.SerializeAsync(
                stream,
                entries,
                AutomationJsonContext.Default.AutomationBuildLogEntryArray,
                token)),
            new AutomationArtifactMetadata
            {
                Encoding = "utf-8",
                Data = JsonSerializer.SerializeToElement(new
                {
                    buildId,
                    entryCount = entries.Length,
                    itemSchema = "#/$defs/buildLogEntry",
                }),
            },
            cancellationToken).ConfigureAwait(false);
        PublishArtifactChanged(
            AutomationProtocolConstants.BuildLogExportMethod,
            sessionId,
            requestId,
            artifact,
            "created");
        return JsonSerializer.SerializeToElement(
            artifact,
            AutomationJsonContext.Default.AutomationArtifactReference);
    }

    private static AutomationBuildPreflightResult MapBuildPreflight(BuildPreflight value)
    {
        return new AutomationBuildPreflightResult
        {
            Ok = value.Ok,
            BuildPlayerAvailable = value.Tools.BuildPlayerExists,
            DotnetVersion = LimitBuildText(value.DotnetVersion, 1024),
            ShellVersion = LimitBuildText(value.ShellVersion, 4096),
            Diagnostic = LimitBuildText(value.Diagnostic, 32768),
        };
    }

    private static AutomationBuildSnapshot MapBuild(EditorBuildExecutionSnapshot value)
    {
        return new AutomationBuildSnapshot
        {
            BuildId = value.BuildId,
            State = MapBuildState(value.State),
            Phase = MapBuildPhase(value.Phase),
            Percent = NormalizeBuildPercent(value.Percent),
            StartedAtUtc = value.StartedAtUtc,
            CompletedAtUtc = value.CompletedAtUtc,
            LaunchOnSuccess = value.LaunchOnSuccess,
            CancellationRequested = value.CancellationRequested,
            PlayerProcessId = value.PlayerProcessId,
            PlayerLaunchError = value.PlayerLaunchError is null
                ? null
                : LimitBuildText(value.PlayerLaunchError, 8192),
            Result = value.Result is null ? null : MapBuildResult(value.Result),
        };
    }

    private static AutomationBuildResult MapBuildResult(BuildResult value)
    {
        return new AutomationBuildResult
        {
            Ok = value.Ok,
            Rid = NonEmptyBuildText(value.Rid),
            Channel = NonEmptyBuildText(value.Channel),
            ReleaseChannel = NonEmptyBuildText(value.ReleaseChannel),
            WindowMode = NonEmptyBuildText(value.WindowMode),
            Configuration = NonEmptyBuildText(value.Configuration),
            Version = LimitBuildText(value.Version, 128),
            InformationalVersion = LimitBuildText(value.InformationalVersion, 256),
            PackageArchivePath = ValidateBuildPath(value.PackageArchive),
            PackageDirectory = ValidateBuildPath(value.PackageDir),
            PlayerDirectory = ValidateBuildPath(value.PlayerDir),
            LauncherPath = ValidateBuildPath(value.LauncherExe),
            Sha256 = ValidateBuildSha(value.Sha256),
            SizeBytes = Math.Max(0, value.SizeBytes),
            PhaseTimings =
            [
                .. value.PhaseTimingsMs
                    .OrderBy(static item => item.Key)
                    .Select(static item => new AutomationBuildPhaseTiming
                    {
                        Phase = MapBuildPhase(item.Key),
                        Milliseconds = NormalizeBuildDuration(item.Value),
                    }),
            ],
            Warnings =
            [
                .. value.Warnings
                    .Take(1024)
                    .Select(static warning => LimitBuildText(warning, 8192)),
            ],
            Error = value.Error is null ? null : LimitBuildText(value.Error, 32768),
            ExitCode = value.ExitCode,
        };
    }

    private static AutomationBuildLogEntry MapBuildLog(BuildProgressEvent value)
    {
        return new AutomationBuildLogEntry
        {
            Phase = MapBuildPhase(value.Phase),
            Level = value.Level switch
            {
                BuildLogLevel.Info => AutomationBuildLogLevel.Info,
                BuildLogLevel.Warning => AutomationBuildLogLevel.Warning,
                BuildLogLevel.Error => AutomationBuildLogLevel.Error,
                _ => throw new InvalidOperationException($"未知 build log level '{value.Level}'。"),
            },
            Percent = NormalizeBuildPercent(value.Percent),
            Message = LimitBuildText(value.Message, 8192),
            TimestampUtc = value.Timestamp,
        };
    }

    private static AutomationPlayerProcessSnapshot MapPlayer(EditorPlayerProcessSnapshot value)
    {
        return new AutomationPlayerProcessSnapshot
        {
            PlayerProcessId = value.PlayerProcessId,
            BuildId = value.BuildId,
            ProcessId = value.ProcessId,
            ProcessStartUtc = value.ProcessStartUtc,
            StartedAtUtc = value.StartedAtUtc,
            State = value.State switch
            {
                EditorPlayerProcessState.Running => AutomationPlayerProcessState.Running,
                EditorPlayerProcessState.Exited => AutomationPlayerProcessState.Exited,
                _ => throw new InvalidOperationException($"未知 player process state '{value.State}'。"),
            },
            TerminationRequested = value.TerminationRequested,
            ExitedAtUtc = value.ExitedAtUtc,
            ExitCode = value.ExitCode,
        };
    }

    private static AutomationBuildState MapBuildState(EditorBuildExecutionState state)
    {
        return state switch
        {
            EditorBuildExecutionState.Running => AutomationBuildState.Running,
            EditorBuildExecutionState.Succeeded => AutomationBuildState.Succeeded,
            EditorBuildExecutionState.Failed => AutomationBuildState.Failed,
            EditorBuildExecutionState.Cancelled => AutomationBuildState.Cancelled,
            _ => throw new InvalidOperationException($"未知 build state '{state}'。"),
        };
    }

    private static AutomationBuildPhase MapBuildPhase(BuildPhase phase)
    {
        return phase switch
        {
            BuildPhase.Unknown => AutomationBuildPhase.Unknown,
            BuildPhase.Native => AutomationBuildPhase.Native,
            BuildPhase.Publish => AutomationBuildPhase.Publish,
            BuildPhase.Verify => AutomationBuildPhase.Verify,
            BuildPhase.Package => AutomationBuildPhase.Package,
            BuildPhase.Audit => AutomationBuildPhase.Audit,
            BuildPhase.Done => AutomationBuildPhase.Done,
            _ => throw new InvalidOperationException($"未知 build phase '{phase}'。"),
        };
    }

    private static AutomationBuildRequest DeserializeBuildRequest(JsonElement? payload, string method)
    {
        AutomationBuildRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationBuildRequest,
            method);
        ValidateSchema(request.SchemaVersion, method);
        return request with { BuildId = ValidateExternalId(request.BuildId, "buildId") };
    }

    private static AutomationPlayerProcessRequest DeserializePlayerRequest(
        JsonElement? payload,
        string method)
    {
        AutomationPlayerProcessRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationPlayerProcessRequest,
            method);
        ValidateSchema(request.SchemaVersion, method);
        return request with
        {
            PlayerProcessId = ValidateExternalId(request.PlayerProcessId, "playerProcessId"),
        };
    }

    private static string ValidateExternalId(string value, string field)
    {
        if (value.Length != 32)
        {
            throw Invalid($"{field} 必须是 32 位小写十六进制 ID。");
        }

        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            if (!char.IsAsciiDigit(character) && character is not (>= 'a' and <= 'f'))
            {
                throw Invalid($"{field} 必须是 32 位小写十六进制 ID。");
            }
        }

        return value;
    }

    private static string? ValidateBuildPath(string? path)
    {
        return path is null || path.Length <= 32767
            ? path
            : throw StateUnavailable("build 结果路径超过公共协议上限。");
    }

    private static string? ValidateBuildSha(string? sha256)
    {
        if (sha256 is null)
        {
            return null;
        }

        string normalized = sha256.ToLowerInvariant();
        return normalized.Length == 64 && normalized.All(static character =>
            char.IsAsciiDigit(character) || character is >= 'a' and <= 'f')
            ? normalized
            : throw StateUnavailable("build 结果包含无效 SHA256。");
    }

    private static string NonEmptyBuildText(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : LimitBuildText(value, 128);
    }

    private static string LimitBuildText(string value, int maximumLength)
    {
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }

    private static float NormalizeBuildPercent(float value)
    {
        return float.IsFinite(value)
            ? Math.Clamp(value, 0, 1)
            : throw StateUnavailable("build 结果包含非有限进度值。");
    }

    private static double NormalizeBuildDuration(double value)
    {
        return double.IsFinite(value)
            ? Math.Max(0, value)
            : throw StateUnavailable("build 结果包含非有限阶段耗时。");
    }

    private static bool MatchBuild(AutomationBuildSnapshot item, AutomationFilterClause clause)
    {
        return clause.Field switch
        {
            "buildId" => MatchString(item.BuildId, clause),
            "state" => MatchString(item.State.ToString(), clause),
            "phase" => MatchString(item.Phase.ToString(), clause),
            "launchOnSuccess" => MatchBoolean(item.LaunchOnSuccess, clause),
            "cancellationRequested" => MatchBoolean(item.CancellationRequested, clause),
            _ => throw Invalid($"Build filter field '{clause.Field}' 不受支持。"),
        };
    }

    private static void SortBuilds(AutomationBuildSnapshot[] items, AutomationSortClause[] sort)
    {
        ValidateSort(sort);
        Array.Sort(items, (left, right) =>
        {
            for (int i = 0; i < sort.Length; i++)
            {
                int compared = sort[i].Field switch
                {
                    "buildId" => string.CompareOrdinal(left.BuildId, right.BuildId),
                    "state" => left.State.CompareTo(right.State),
                    "phase" => left.Phase.CompareTo(right.Phase),
                    "percent" => left.Percent.CompareTo(right.Percent),
                    "startedAtUtc" => left.StartedAtUtc.CompareTo(right.StartedAtUtc),
                    "completedAtUtc" => Nullable.Compare(left.CompletedAtUtc, right.CompletedAtUtc),
                    "launchOnSuccess" => left.LaunchOnSuccess.CompareTo(right.LaunchOnSuccess),
                    "cancellationRequested" => left.CancellationRequested.CompareTo(right.CancellationRequested),
                    _ => throw Invalid($"Build sort field '{sort[i].Field}' 不受支持。"),
                };
                if (compared != 0)
                {
                    return ApplyDirection(compared, sort[i].Direction);
                }
            }

            return string.CompareOrdinal(left.BuildId, right.BuildId);
        });
    }

    private static bool MatchPlayer(
        AutomationPlayerProcessSnapshot item,
        AutomationFilterClause clause)
    {
        return clause.Field switch
        {
            "playerProcessId" => MatchString(item.PlayerProcessId, clause),
            "buildId" => MatchString(item.BuildId, clause),
            "processId" => MatchNumber(item.ProcessId, clause),
            "state" => MatchString(item.State.ToString(), clause),
            "terminationRequested" => MatchBoolean(item.TerminationRequested, clause),
            "exitCode" when item.ExitCode.HasValue => MatchNumber(item.ExitCode.Value, clause),
            "exitCode" => false,
            _ => throw Invalid($"Player filter field '{clause.Field}' 不受支持。"),
        };
    }

    private static void SortPlayers(
        AutomationPlayerProcessSnapshot[] items,
        AutomationSortClause[] sort)
    {
        ValidateSort(sort);
        Array.Sort(items, (left, right) =>
        {
            for (int i = 0; i < sort.Length; i++)
            {
                int compared = sort[i].Field switch
                {
                    "playerProcessId" => string.CompareOrdinal(
                        left.PlayerProcessId,
                        right.PlayerProcessId),
                    "buildId" => string.CompareOrdinal(left.BuildId, right.BuildId),
                    "processId" => left.ProcessId.CompareTo(right.ProcessId),
                    "state" => left.State.CompareTo(right.State),
                    "terminationRequested" => left.TerminationRequested.CompareTo(
                        right.TerminationRequested),
                    "startedAtUtc" => left.StartedAtUtc.CompareTo(right.StartedAtUtc),
                    "exitedAtUtc" => Nullable.Compare(left.ExitedAtUtc, right.ExitedAtUtc),
                    "exitCode" => Nullable.Compare(left.ExitCode, right.ExitCode),
                    _ => throw Invalid($"Player sort field '{sort[i].Field}' 不受支持。"),
                };
                if (compared != 0)
                {
                    return ApplyDirection(compared, sort[i].Direction);
                }
            }

            return string.CompareOrdinal(left.PlayerProcessId, right.PlayerProcessId);
        });
    }

    private void EnsureCurrentSession(EditorProjectSession session, string method)
    {
        if (!ReferenceEquals(_app.CurrentSession, session))
        {
            throw StateUnavailable($"{method} 已因工程会话切换而失效。");
        }
    }

    private static void EnsurePreparedId(string prepared, string requested, string method)
    {
        if (!string.Equals(prepared, requested, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{method} prepared ID 与请求不一致。");
        }
    }

    private static string[] BuildResources(string buildId)
    {
        return [BuildResource, $"{BuildResource}:{buildId}"];
    }

    private static string[] PlayerResources(string playerProcessId)
    {
        return [PlayerResource, $"{PlayerResource}:{playerProcessId}"];
    }

    private sealed record BuildPreflightPrepared(
        EditorProjectSession Session,
        BuildPreflight Preflight);

    private sealed record BuildWaitPrepared(EditorProjectSession Session, string BuildId);

    private sealed record PlayerWaitPrepared(
        EditorProjectSession Session,
        string PlayerProcessId);
}
