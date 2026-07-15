using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;
using PixelEngine.Editor.Automation.Server;
using PixelEngine.Hosting;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// Editor Shell 中 automation Server、主线程 scheduler、revision/event 与 artifact 的唯一组合根。
/// </summary>
internal sealed class EditorAutomationRuntime : IDisposable
{
    private const string DiscoveryRootEnvironmentVariable = "PIXELENGINE_AUTOMATION_DISCOVERY_ROOT";
    private const string ArtifactRootEnvironmentVariable = "PIXELENGINE_AUTOMATION_ARTIFACT_ROOT";
    private readonly EditorShellApp _app;
    private readonly EditorAutomationAuthoringApi _authoringApi;
    private readonly EditorAutomationServer _server;
    private string[] _publishedProjectResourceIds = [];
    private int _disposed;

    private EditorAutomationRuntime(
        EditorShellApp app,
        EditorAutomationAuthoringApi authoringApi,
        AutomationMainThreadScheduler scheduler,
        AutomationArtifactStore artifacts,
        EditorAutomationServer server)
    {
        _app = app;
        _authoringApi = authoringApi;
        Scheduler = scheduler;
        Artifacts = artifacts;
        _server = server;
        _app.ConsoleStore.Changed += OnConsoleChanged;
        _app.ConsoleOptions.Changed += OnConsoleOptionsChanged;
    }

    public AutomationMainThreadScheduler Scheduler { get; }

    public AutomationArtifactStore Artifacts { get; }

    public string InstanceId => _server.InstanceId;

    /// <summary>在 Editor 主线程创建并启动 current-user Named Pipe automation runtime。</summary>
    public static EditorAutomationRuntime? Start(
        EditorShellApp app,
        EditorShellOptions options,
        EditorUserDataPaths userDataPaths)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(userDataPaths);
        if (!options.AutomationEnabled || !OperatingSystem.IsWindows())
        {
            return null;
        }

        string instanceId = Guid.NewGuid().ToString("N");
        string discoveryRoot = ResolveRoot(
            options.AutomationDiscoveryRoot,
            DiscoveryRootEnvironmentVariable,
            Path.Combine(userDataPaths.RootDirectory, "automation"));
        string artifactRoot = ResolveRoot(
            options.AutomationArtifactRoot,
            ArtifactRootEnvironmentVariable,
            Path.Combine(userDataPaths.RootDirectory, "automation-artifacts"));
        AutomationArtifactStore? artifacts = null;
        EditorAutomationAuthoringApi? authoringApi = null;
        AutomationMainThreadScheduler? scheduler = null;
        EditorAutomationServer? server = null;
        try
        {
            artifacts = new AutomationArtifactStore(new AutomationArtifactStoreOptions
            {
                RootPath = Path.Combine(artifactRoot, instanceId),
            });
            authoringApi = new EditorAutomationAuthoringApi(
                app,
                artifacts,
                options.AutomationImportRoots);
            scheduler = new AutomationMainThreadScheduler(
                authoringApi.CreateRegistrations(),
                new AutomationRevisionStore(),
                new EditorAutomationUndoSink(app),
                new EditorAutomationTransactionParticipant(app));
            authoringApi.AttachEventHub(scheduler.Events);
            server = new EditorAutomationServer(
                new AutomationServerOptions
                {
                    DiscoveryRoot = discoveryRoot,
                    EditorVersion = ResolveEditorVersion(),
                    InstanceId = instanceId,
                    CredentialInputPath = options.AutomationCredentialPath,
                    SupportedScopes = AutomationScopes.All,
                    CapabilityDigest = scheduler.CapabilityDigest,
                },
                scheduler);
            EditorAutomationRuntime runtime = new(app, authoringApi, scheduler, artifacts, server);
            runtime.ConfigureUndoStack(app.SharedUndoStack);
            server.StartAsync().AsTask().GetAwaiter().GetResult();
            return runtime;
        }
        catch (Exception startException)
        {
            List<Exception> failures = [startException];
            try
            {
                server?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception cleanupException)
            {
                failures.Add(cleanupException);
            }

            try
            {
                artifacts?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception cleanupException)
            {
                failures.Add(cleanupException);
            }

            try
            {
                scheduler?.Dispose();
            }
            catch (Exception cleanupException)
            {
                failures.Add(cleanupException);
            }

            authoringApi?.Dispose();

            if (failures.Count > 1)
            {
                throw new AggregateException("Automation runtime 启动失败，且至少一个资源清理也失败。", failures);
            }

            throw;
        }
    }

    /// <summary>Editor 主循环在任何手动 UI command 前排空 ingress。</summary>
    public void DrainEditorIngress()
    {
        if (Scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress))
        {
            _ = Scheduler.Drain(AutomationExecutionPhase.EditorIngress);
        }
    }

    /// <summary>把手动 Undo history 接到同一 revision/event 轨道。</summary>
    public void ConfigureUndoStack(EditorUndoStack undoStack)
    {
        ArgumentNullException.ThrowIfNull(undoStack);
        undoStack.HistoryApplied = OnManualHistoryApplied;
    }

    internal void NotifyPlayChanged(
        string method,
        EditorProjectSession session,
        string? previousPlaySessionId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(session);
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        List<string> resources =
        [
            "editor:play",
            "editor:profiler",
            CreateSceneResourceId(session),
        ];
        if (!string.IsNullOrWhiteSpace(previousPlaySessionId))
        {
            resources.Add(CreateRuntimeResourceId(previousPlaySessionId));
        }

        if (!string.IsNullOrWhiteSpace(session.AutomationPlaySessionId))
        {
            resources.Add(CreateRuntimeResourceId(session.AutomationPlaySessionId));
        }

        string[] normalized =
        [
            .. resources.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
        ];
        AutomationRevisionSnapshot revision = Scheduler.Revisions.Advance(normalized);
        PublishStateChanged(method, normalized, "execute", revision);
    }

    internal void NotifyAssetsChanged(EditorProjectSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        string[] resources =
        [
            "editor:project",
            "editor:project:assets",
            CreateSceneResourceId(session),
        ];
        AutomationRevisionSnapshot revision = Scheduler.Revisions.Advance(resources);
        PublishStateChanged("project.assets.external.changed", resources, "execute", revision);
    }

    internal void NotifyUiManifestChanged(EditorProjectSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        string[] resources =
        [
            "editor:project",
            "editor:project:assets",
            "editor:project:ui-manifest",
        ];
        AutomationRevisionSnapshot revision = Scheduler.Revisions.Advance(resources);
        PublishStateChanged("project.ui-manifest.changed", resources, "execute", revision);
    }

    internal void NotifyProfilerChanged()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        string[] resources = ["editor:profiler"];
        AutomationRevisionSnapshot revision = Scheduler.Revisions.Advance(resources);
        PublishStateChanged("profiler.vsync.changed", resources, "execute", revision);
    }

    internal void NotifyConsoleSelectionChanged()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        string[] resources = ["editor:console:selection"];
        AutomationRevisionSnapshot revision = Scheduler.Revisions.Advance(resources);
        PublishStateChanged("console.selection.changed", resources, "execute", revision);
    }

    internal void NotifyConsoleCleared(EditorConsoleClearResult result)
    {
        if (!result.StateChanged || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        string[] resources = result.SelectionChanged
            ? ["editor:console", "editor:console:selection"]
            : ["editor:console"];
        AutomationRevisionSnapshot revision = Scheduler.Revisions.Advance(resources);
        PublishStateChanged("console.cleared", resources, "execute", revision);
    }

    internal void NotifySettingsChanged(string method, IReadOnlyList<string> resources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(resources);
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        string[] normalized =
        [
            .. resources
                .Where(static resource => !string.IsNullOrWhiteSpace(resource))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Settings mutation 至少需要一个稳定 resource ID。", nameof(resources));
        }

        AutomationRevisionSnapshot revision = Scheduler.Revisions.Advance(normalized);
        PublishStateChanged(method, normalized, "execute", revision);
    }

    internal void NotifySimulationChanged(EditorProjectSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        List<string> resources = ["editor:simulation", "editor:profiler"];
        if (!string.IsNullOrWhiteSpace(session.AutomationPlaySessionId))
        {
            resources.Add(CreateRuntimeResourceId(session.AutomationPlaySessionId));
        }

        string[] normalized = [.. resources.Order(StringComparer.Ordinal)];
        AutomationRevisionSnapshot revision = Scheduler.Revisions.Advance(normalized);
        PublishStateChanged("runtime.simulation.changed", normalized, "execute", revision);
    }

    internal void NotifyRuntimeTuningChanged(
        EditorProjectSession session,
        string method,
        string resourceId)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        List<string> resources = [resourceId, "editor:profiler"];
        if (!string.IsNullOrWhiteSpace(session.AutomationPlaySessionId))
        {
            resources.Add(CreateRuntimeResourceId(session.AutomationPlaySessionId));
        }

        string[] normalized = [.. resources.Order(StringComparer.Ordinal)];
        AutomationRevisionSnapshot revision = Scheduler.Revisions.Advance(normalized);
        PublishStateChanged(method, normalized, "execute", revision);
    }

    /// <summary>项目/场景切换后原子更新 discovery，并推进对应 stable resources。</summary>
    public void UpdateProject(EditorProjectSession? session)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        AutomationProjectSummary? summary = session is null ? null : CreateProjectSummary(session);
        _server.UpdateDescriptorAsync(
            summary,
            _server.Descriptor.CapabilityDigest).AsTask().GetAwaiter().GetResult();

        string[] nextResources = summary is null
            ? ["editor:workspace", "editor:transition"]
            :
            [
                "editor:project",
                "editor:workspace",
                "editor:transition",
                $"scene:{summary.ProjectId}:{summary.SceneId}",
            ];
        string[] affectedResources =
        [
            .. _publishedProjectResourceIds
                .Concat(nextResources)
                .Append("editor:project")
                .Append("editor:workspace")
                .Append("editor:transition")
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        _publishedProjectResourceIds = nextResources;
        AutomationRevisionSnapshot revision = Scheduler.Revisions.Advance(affectedResources);
        PublishStateChanged(
            session is null ? "project.close" : "project.open",
            affectedResources,
            "execute",
            revision);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _app.ConsoleStore.Changed -= OnConsoleChanged;
        _app.ConsoleOptions.Changed -= OnConsoleOptionsChanged;

        List<Exception> failures = [];
        try
        {
            _server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            Scheduler.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            Artifacts.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            _authoringApi.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (failures.Count == 1)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures.Count > 1)
        {
            throw new AggregateException("Automation runtime 关闭时多个资源失败。", failures);
        }
    }

    private void OnManualHistoryApplied(IEditorCommand command, EditorUndoMutationKind mutation)
    {
        if (Volatile.Read(ref _disposed) != 0 || _app.CurrentSession is not { } session)
        {
            return;
        }

        string[] resources = ResolveHistoryResources(session, command);
        AutomationRevisionSnapshot revision = command is IEditorResourceScopedCommand
            ? Scheduler.Revisions.Capture(resources)
            : Scheduler.Revisions.Advance(resources);
        PublishStateChanged(
            $"editor.history.{mutation.ToString().ToLowerInvariant()}",
            resources,
            mutation switch
            {
                EditorUndoMutationKind.Execute => "execute",
                EditorUndoMutationKind.Undo => "undo",
                EditorUndoMutationKind.Redo => "redo",
                _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null),
            },
            revision);
    }

    private void OnConsoleChanged(EditorConsoleStoreChange change)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        string[] resources = ["editor:console"];
        AutomationRevisionSnapshot revision = Scheduler.Revisions.Advance(resources);
        PublishStateChanged(
            change.Kind == EditorConsoleStoreChangeKind.Added
                ? "console.entry.added"
                : "console.cleared",
            resources,
            "execute",
            revision);
    }

    private void OnConsoleOptionsChanged(EditorConsoleOptionsSnapshot options)
    {
        _ = options;
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        string[] resources = ["editor:console:options"];
        AutomationRevisionSnapshot revision = Scheduler.Revisions.Advance(resources);
        PublishStateChanged("console.options.changed", resources, "execute", revision);
    }

    internal static string[] ResolveHistoryResources(
        EditorProjectSession session,
        IEditorCommand command)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(command);
        return command is IEditorResourceScopedCommand { ResourceIds.Count: > 0 } scoped
            ? [.. scoped.ResourceIds]
            : [CreateSceneResourceId(session)];
    }

    private void PublishStateChanged(
        string method,
        string[] resourceIds,
        string changeKind,
        AutomationRevisionSnapshot revision)
    {
        AutomationStateChangedEvent stateChanged = new()
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Method = method,
            ResourceIds = [.. resourceIds],
            ChangeKind = changeKind,
        };
        _ = Scheduler.Events.Publish(
            AutomationProtocolConstants.StateChangedEventType,
            revision,
            payload: JsonSerializer.SerializeToElement(
                stateChanged,
                AutomationJsonContext.Default.AutomationStateChangedEvent));
        string[] domainEvents = EditorAutomationEventRouting.ForManualMutation(method, resourceIds);
        for (int i = 0; i < domainEvents.Length; i++)
        {
            _ = Scheduler.Events.Publish(
                domainEvents[i],
                revision,
                payload: JsonSerializer.SerializeToElement(
                    stateChanged,
                    AutomationJsonContext.Default.AutomationStateChangedEvent));
        }
    }

    private static AutomationProjectSummary CreateProjectSummary(EditorProjectSession session)
    {
        string projectId = StableProjectId(session.Project.ProjectRoot);
        string sceneId = StableSceneId(session.Project.ProjectRoot, session.CurrentSceneRelativePath);
        return new AutomationProjectSummary
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            ProjectId = projectId,
            Name = session.Project.Name,
            RootPath = session.Project.ProjectRoot,
            SceneId = sceneId,
        };
    }

    internal static string CreateSceneResourceId(EditorProjectSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return $"scene:{StableProjectId(session.Project.ProjectRoot)}:{StableSceneId(session.Project.ProjectRoot, session.CurrentSceneRelativePath)}";
    }

    internal static string CreateRuntimeResourceId(string? playSessionId)
    {
        string id = string.IsNullOrWhiteSpace(playSessionId) ? "edit" : playSessionId;
        return $"play:{id}:runtime";
    }

    internal static string StableProjectId(string projectRoot)
    {
        string canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot))
            .Replace('\\', '/');
        return StableTextId(canonicalRoot);
    }

    internal static string StableSceneId(string projectRoot, string sceneRelativePath)
    {
        string canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot))
            .Replace('\\', '/');
        string normalizedScene = sceneRelativePath.Trim().Replace('\\', '/');
        return StableTextId($"{canonicalRoot}\n{normalizedScene}");
    }

    private static string StableTextId(string value)
    {
        string normalized = OperatingSystem.IsWindows() ? value.ToUpperInvariant() : value;
        byte[] utf8 = Encoding.UTF8.GetBytes(normalized);
        try
        {
            return Convert.ToHexStringLower(SHA256.HashData(utf8));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(utf8);
        }
    }

    private static string ResolveRoot(string? explicitPath, string environmentVariable, string fallback)
    {
        string? configured = string.IsNullOrWhiteSpace(explicitPath)
            ? Environment.GetEnvironmentVariable(environmentVariable)
            : explicitPath;
        return Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim());
    }

    private static string ResolveEditorVersion()
    {
        return typeof(EditorAutomationRuntime).Assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    private sealed class EditorAutomationUndoSink(EditorShellApp app) : IAutomationUndoSink
    {
        public void RecordExecuted(IAutomationUndoAction action)
        {
            ArgumentNullException.ThrowIfNull(action);
            app.SharedUndoStack.RecordExecuted(new AutomationEditorCommand(action));
        }
    }

    private sealed class EditorAutomationTransactionParticipant(EditorShellApp app)
        : IAutomationTransactionParticipant
    {
        public object CaptureState()
        {
            EditorProjectSession session = app.CurrentSession
                ?? throw StateUnavailable("当前没有打开的 Editor project session，无法开始 transaction。");
            session.FlushPendingAuthoringEdits();
            return session.CaptureAutomationTransactionState();
        }

        public void RestoreState(object state)
        {
            EditorAutomationTransactionState transactionState = state as EditorAutomationTransactionState
                ?? throw new ArgumentException("Automation transaction participant state 类型无效。", nameof(state));
            if (!ReferenceEquals(app.CurrentSession, transactionState.Session))
            {
                throw new InvalidOperationException("Automation transaction 期间 project session 被替换，无法安全恢复。");
            }

            transactionState.Session.RestoreAutomationTransactionState(transactionState);
        }
    }

    private sealed class AutomationEditorCommand(IAutomationUndoAction action)
        : IEditorResourceScopedCommand, IDisposable
    {
        private readonly string[] _resourceIds = action is IAutomationResourceScopedUndoAction scoped
            ? [.. scoped.ResourceIds]
            : [];

        public string Name => action.Name;

        public IReadOnlyList<string> ResourceIds => _resourceIds;

        public void Execute(EditorSceneModel scene)
        {
            ArgumentNullException.ThrowIfNull(scene);
            action.Redo();
        }

        public void Undo(EditorSceneModel scene)
        {
            ArgumentNullException.ThrowIfNull(scene);
            action.Undo();
        }

        public void Dispose()
        {
            (action as IDisposable)?.Dispose();
        }
    }

    private static AutomationRequestException StateUnavailable(string message)
    {
        return new AutomationRequestException(new AutomationError
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Code = AutomationErrorCodes.StateUnavailable,
            Category = AutomationErrorCategory.Availability,
            Message = message,
            Transient = false,
        });
    }
}

/// <summary>transaction 需要随 Undo/Redo 恢复的非 command 状态。</summary>
internal sealed record EditorAutomationTransactionState(
    EditorProjectSession Session,
    int? SceneSelectedStableId,
    bool SceneWasDirty,
    EditorAutomationSelectionSnapshot Selection);

/// <summary>跨面板 EditorSelection 的完整快照。</summary>
internal readonly record struct EditorAutomationSelectionSnapshot(
    int? CellX,
    int? CellY,
    int? MaterialId,
    string? AssetId,
    string? AssetPath,
    string? FolderPath,
    string? EntityHandle,
    int? GameObjectStableId,
    int? BodyId);

/// <summary>把 scheduler 的 12 个 Engine safe phase 零轮询开销接到固定 phase pipeline。</summary>
internal sealed class EditorAutomationPhaseDriver(AutomationMainThreadScheduler scheduler) : IEnginePhaseDriver
{
    public void RegisterPhases(EnginePhasePipeline phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        foreach (EnginePhase phase in Enum.GetValues<EnginePhase>())
        {
            phases.Register(phase, Drain);
        }
    }

    private void Drain(EngineTickContext context)
    {
        AutomationExecutionPhase phase = context.Phase switch
        {
            EnginePhase.InputAndTime => AutomationExecutionPhase.EngineInputAndTime,
            EnginePhase.GameLogicAndScripts => AutomationExecutionPhase.EngineGameLogicAndScripts,
            EnginePhase.ResidencyApply => AutomationExecutionPhase.EngineResidencyApply,
            EnginePhase.ParticleToCell => AutomationExecutionPhase.EngineParticleToCell,
            EnginePhase.CaSimulation => AutomationExecutionPhase.EngineCaSimulation,
            EnginePhase.Temperature => AutomationExecutionPhase.EngineTemperature,
            EnginePhase.DirtyRectSwap => AutomationExecutionPhase.EngineDirtyRectSwap,
            EnginePhase.CellToParticle => AutomationExecutionPhase.EngineCellToParticle,
            EnginePhase.PhysicsSync => AutomationExecutionPhase.EnginePhysicsSync,
            EnginePhase.BuildRenderBuffer => AutomationExecutionPhase.EngineBuildRenderBuffer,
            EnginePhase.GpuUploadAndRender => AutomationExecutionPhase.EngineGpuUploadAndRender,
            EnginePhase.WorldStreaming => AutomationExecutionPhase.EngineWorldStreaming,
            _ => throw new ArgumentOutOfRangeException(nameof(context), context.Phase, "未知 Engine phase。"),
        };
        if (scheduler.HasPendingWork(phase))
        {
            _ = scheduler.Drain(phase);
        }
    }
}
