using System.Text.Json;
using PixelEngine.Editor;
using PixelEngine.Editor.Automation.Client;
using PixelEngine.Editor.Automation.Protocol;
using PixelEngine.Editor.Automation.Server;
using PixelEngine.Editor.Shell;
using PixelEngine.Scripting;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>
/// Editor Shell automation 组合根、phase wiring 与统一 Undo history 回归。
/// </summary>
public sealed class EditorAutomationRuntimeTests
{
    /// <summary>验证 Shell runtime 发布真实实例，并在 EditorIngress 返回无项目结构化错误。</summary>
    [Fact]
#pragma warning disable xUnit1031 // Scheduler contract pins the runtime to this exact OS thread.
    public void ShellRuntimeIsDiscoverableAndDrainsRequestsOnOwnerThread()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), "PixelEngine", "AutomationShellTests", Guid.NewGuid().ToString("N"));
        string discoveryRoot = Path.Combine(root, "discovery");
        string artifactRoot = Path.Combine(root, "artifacts");
        try
        {
            EditorShellOptions options = EditorShellOptions.Parse(
            [
                "--automation-discovery-root",
                discoveryRoot,
                "--automation-artifact-root",
                artifactRoot,
                "--ephemeral-user-state",
            ]);
            EditorUserDataPaths paths = EditorUserDataPaths.Resolve(
                options,
                environmentUserDataDirectory: null,
                ephemeralDirectory: Path.Combine(root, "user-data"));
            EditorShellApp app = EditorShellApp.CreateForTests();
            using EditorAutomationRuntime runtime = EditorAutomationRuntime.Start(app, options, paths)
                ?? throw new InvalidOperationException("Windows Shell automation runtime 未启动。");
            AutomationDiscoverySnapshot discovery = AutomationDiscovery.DiscoverAsync(discoveryRoot)
                .AsTask().GetAwaiter().GetResult();
            AutomationDiscoveredInstance instance = Assert.Single(discovery.Instances);
            Assert.Empty(discovery.Diagnostics);
            Assert.Equal(runtime.InstanceId, instance.Descriptor.InstanceId);
            Assert.Null(instance.Descriptor.Project);
            Assert.True(Directory.Exists(Path.Combine(artifactRoot, runtime.InstanceId)));

            EditorAutomationClient client = EditorAutomationClient.ConnectAsync(
                instance,
                new AutomationClientOptions
                {
                    ClientInstanceId = "shell-runtime-test",
                    ClientName = "hosting-tests",
                    ClientVersion = "1.0",
                    RequestedScopes = [AutomationScopes.EditorRead, AutomationScopes.EditorControl],
                    ConnectTimeout = TimeSpan.FromSeconds(5),
                    RequestTimeout = TimeSpan.FromSeconds(5),
                }).AsTask().GetAwaiter().GetResult();
            try
            {
                AutomationPingResponse ping = client.PingAsync().AsTask().GetAwaiter().GetResult();
                Assert.Equal(runtime.InstanceId, ping.InstanceId);
                Task<AutomationInvocationResult> capabilitiesPending = client.InvokeDetailedAsync(
                    AutomationProtocolConstants.CapabilityListMethod,
                    JsonSerializer.SerializeToElement(
                        new AutomationPageRequest { PageSize = 500 },
                        AutomationJsonContext.Default.AutomationPageRequest))
                    .AsTask();
                Assert.True(SpinWait.SpinUntil(
                    () => runtime.Scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
                    TimeSpan.FromSeconds(5)));
                runtime.DrainEditorIngress();
                AutomationCapabilityListResponse capabilities = capabilitiesPending
                    .GetAwaiter().GetResult()
                    .Payload?.Deserialize(AutomationJsonContext.Default.AutomationCapabilityListResponse)
                    ?? throw new InvalidOperationException("Shell capability registry 未返回响应。");
                Assert.Equal(runtime.Scheduler.CapabilityDigest, instance.Descriptor.CapabilityDigest);
                Assert.Equal(runtime.Scheduler.CapabilityDigest, capabilities.CapabilityDigest);
                Assert.Equal(capabilities.Page.Total, capabilities.Items.Length);
                using (JsonDocument schema = JsonDocument.Parse(
                    File.ReadAllBytes(FindRepositoryFile(
                        "schema/editor-automation-protocol.v1.schema.json"))))
                {
                    JsonElement definitions = schema.RootElement.GetProperty("$defs");
                    Assert.All(capabilities.Items, descriptor =>
                    {
                        Assert.True(
                            definitions.TryGetProperty(
                                descriptor.RequestSchema["#/$defs/".Length..],
                                out _),
                            $"{descriptor.Id} request schema {descriptor.RequestSchema}");
                        Assert.True(
                            definitions.TryGetProperty(
                                descriptor.ResponseSchema["#/$defs/".Length..],
                                out _),
                            $"{descriptor.Id} response schema {descriptor.ResponseSchema}");
                    });
                }

                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.GameObjectCreateMethod &&
                    descriptor.Domain == "hierarchy" &&
                    descriptor.OperationKind == AutomationOperationKind.Write &&
                    descriptor.RequiresExpectedRevision &&
                    descriptor.TransactionMode == AutomationTransactionMode.Optional &&
                    descriptor.UiCommandIds.Contains("menu.game-object.create", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.InspectorCanvasSetMethod &&
                    descriptor.RequestSchema == "#/$defs/builtInCanvasSetRequest" &&
                    descriptor.OperationKind == AutomationOperationKind.Write &&
                    descriptor.TransactionMode == AutomationTransactionMode.Optional);
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.InspectorCanvasSetPrimaryMethod &&
                    descriptor.RequestSchema == "#/$defs/canvasPrimarySetRequest" &&
                    descriptor.UiCommandIds.Contains(
                        "panel.inspector.canvas.primary",
                        StringComparer.Ordinal));

                AutomationTransactionBeginRequest request = new()
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    Name = "No Project Transaction",
                    LeaseMilliseconds = 1000,
                };
                Task<AutomationInvocationResult> pending = client.InvokeDetailedAsync(
                    AutomationProtocolConstants.TransactionBeginMethod,
                    JsonSerializer.SerializeToElement(
                        request,
                        AutomationJsonContext.Default.AutomationTransactionBeginRequest),
                    new AutomationInvocationOptions { IdempotencyKey = "no-project-transaction" })
                    .AsTask();
                Assert.True(SpinWait.SpinUntil(
                    () => runtime.Scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
                    TimeSpan.FromSeconds(5)));
                runtime.DrainEditorIngress();

                AutomationRemoteException exception = Assert.Throws<AutomationRemoteException>(
                    () => pending.GetAwaiter().GetResult());
                Assert.Equal(AutomationErrorCodes.StateUnavailable, exception.Error.Code);
            }
            finally
            {
                client.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
#pragma warning restore xUnit1031

    /// <summary>验证 phase driver 为 12 个固定 Engine phase 各登记一个零轮询 drain hook。</summary>
    [Fact]
    public void PhaseDriverRegistersEveryEngineSafePhaseExactlyOnce()
    {
        using AutomationMainThreadScheduler scheduler = new(
            [],
            new AutomationRevisionStore(),
            new NoopUndoSink(),
            new NoopTransactionParticipant());
        EnginePhasePipeline phases = new(new EngineCommandQueue());
        new EditorAutomationPhaseDriver(scheduler).RegisterPhases(phases);

        for (int raw = 0; raw <= (int)EnginePhase.WorldStreaming; raw++)
        {
            Assert.Equal(1, phases.Count((EnginePhase)raw));
        }
    }

    /// <summary>验证手动 Execute/Undo/Redo 发通知，而 scheduler 的 RecordExecuted 不被重复计数。</summary>
    [Fact]
    public void UnifiedUndoHistoryDistinguishesManualApplyFromAutomationRecord()
    {
        EditorSceneModel scene = EditorSceneModel.Empty();
        EditorUndoStack undo = new();
        List<EditorUndoMutationKind> mutations = [];
        undo.HistoryApplied = (_, mutation) => mutations.Add(mutation);
        TestEditorCommand manual = new();

        undo.Execute(scene, manual);
        Assert.True(undo.Undo(scene));
        Assert.True(undo.Redo(scene));
        undo.RecordExecuted(new TestEditorCommand());

        Assert.Equal(
            [EditorUndoMutationKind.Execute, EditorUndoMutationKind.Undo, EditorUndoMutationKind.Redo],
            mutations);
    }

    /// <summary>automation Undo/Redo 由 scheduler 发布带 request 因果的事件，不重复触发手动回调。</summary>
    [Fact]
    public void AutomationHistoryMutationCanSuppressManualEventCallback()
    {
        EditorSceneModel scene = EditorSceneModel.Empty();
        EditorUndoStack undo = new();
        List<EditorUndoMutationKind> mutations = [];
        undo.HistoryApplied = (_, mutation) => mutations.Add(mutation);
        undo.Execute(scene, new TestEditorCommand());

        Assert.True(undo.Undo(scene, notifyHistoryApplied: false));
        Assert.True(undo.Redo(scene, notifyHistoryApplied: false));

        Assert.Equal([EditorUndoMutationKind.Execute], mutations);
    }

    /// <summary>Undo/Redo action 抛错时 history 归属不得先行丢失，修复后可重试同一 action。</summary>
    [Fact]
    public void UndoRedoHistoryRetainsActionWhenApplyFails()
    {
        EditorSceneModel scene = EditorSceneModel.Empty("undo-failure-retry");
        EditorUndoStack undo = new();
        RetryableEditorCommand command = new() { Value = 1, ThrowOnUndo = true };
        undo.RecordExecuted(command);

        _ = Assert.Throws<InvalidOperationException>(() => undo.Undo(scene));
        Assert.Equal(1, undo.UndoCount);
        Assert.Equal(0, undo.RedoCount);
        Assert.Equal(1, command.Value);

        command.ThrowOnUndo = false;
        Assert.True(undo.Undo(scene));
        Assert.Equal(0, command.Value);
        command.ThrowOnExecute = true;

        _ = Assert.Throws<InvalidOperationException>(() => undo.Redo(scene));
        Assert.Equal(0, undo.UndoCount);
        Assert.Equal(1, undo.RedoCount);
        Assert.Equal(0, command.Value);

        command.ThrowOnExecute = false;
        Assert.True(undo.Redo(scene));
        Assert.Equal(1, command.Value);
    }

    /// <summary>
    /// Hierarchy 手动入口与 automation 共用的 visibility/pickability commands
    /// 必须精确保存单对象与批量 before image，并通过唯一 Undo/Redo 栈往返。
    /// </summary>
    [Fact]
    public void SceneVisibilityCommandsRoundTripThroughUnifiedUndoHistory()
    {
        EditorSceneModel scene = EditorSceneModel.Empty("visibility-undo");
        EditorGameObject root = scene.Create("Root");
        EditorGameObject child = scene.Create("Child", root.StableId);
        scene.SetSceneVisible(child.StableId, visible: false);
        scene.SetScenePickable(root.StableId, pickable: false);
        EditorUndoStack undo = new();

        undo.Execute(scene, new SetAllSceneVisibilityCommand(visible: true));
        Assert.All(scene.EnumerateDepthFirst(), item => Assert.True(item.SceneVisible));
        Assert.True(undo.Undo(scene));
        Assert.True(root.SceneVisible);
        Assert.False(child.SceneVisible);
        Assert.True(undo.Redo(scene));
        Assert.All(scene.EnumerateDepthFirst(), item => Assert.True(item.SceneVisible));

        undo.Execute(scene, new SetScenePickabilityCommand(root.StableId, pickable: true));
        Assert.True(root.ScenePickable);
        Assert.True(undo.Undo(scene));
        Assert.False(root.ScenePickable);
    }

    /// <summary>连续画刷必须按最坏 footprint 限制主线程单请求工作量，避免大半径长 stroke 卡死一帧。</summary>
    [Fact]
    public void BrushStrokeBudgetAccountsForRadiusSquared()
    {
        Assert.Equal(1, EditorAutomationAuthoringApi.EstimateBrushStrokeCellVisits(1, 0));
        Assert.Equal(66_049, EditorAutomationAuthoringApi.EstimateBrushStrokeCellVisits(1, 128));
        Assert.True(
            EditorAutomationAuthoringApi.EstimateBrushStrokeCellVisits(8192, 128) >
            EditorAutomationAuthoringApi.MaximumBrushStrokeCellVisits);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            EditorAutomationAuthoringApi.EstimateBrushStrokeCellVisits(0, 1));
    }

    /// <summary>Inspector typed asset 字段拒绝类型冒充和 root escape，并返回规范编码。</summary>
    [Fact]
    public void AutomationAssetReferenceValidationIsTypedAndRootConfined()
    {
        string valid = ScriptAssetReference.Encode(
            "asset-123",
            "textures\\hero.png",
            ScriptAssetKind.Texture);

        Assert.Equal(
            "assetref|Texture|asset-123|textures/hero.png",
            EditorAutomationAuthoringApi.NormalizeAssetReferenceValue(
                "Portrait",
                ScriptAssetKind.Texture,
                valid));
        _ = Assert.Throws<AutomationRequestException>(() =>
            EditorAutomationAuthoringApi.NormalizeAssetReferenceValue(
                "Portrait",
                ScriptAssetKind.Audio,
                valid));
        _ = Assert.Throws<AutomationRequestException>(() =>
            EditorAutomationAuthoringApi.NormalizeAssetReferenceValue(
                "Portrait",
                ScriptAssetKind.Texture,
                "assetref|Texture|asset-123|../outside.png"));
    }

    /// <summary>幂等检测器必须按 semantic state 比较，而不是按请求对象身份或屏幕坐标猜测。</summary>
    [Fact]
    public void AutomationNoChangeDetectorsCoverTransformWindowToolAndDockState()
    {
        EditorSceneTransform transform = new()
        {
            X = 1f,
            Y = 2f,
            RotationRadians = 0.5f,
            ScaleX = 3f,
            ScaleY = 4f,
        };
        Assert.True(EditorAutomationAuthoringApi.TransformContentEquals(transform, transform.Clone()));
        EditorSceneTransform changedTransform = transform.Clone();
        changedTransform.X = 9f;
        Assert.False(EditorAutomationAuthoringApi.TransformContentEquals(
            transform,
            changedTransform));

        AutomationWindowSnapshot window = new()
        {
            LogicalWidth = 1280,
            LogicalHeight = 720,
            LogicalX = 20,
            LogicalY = 30,
            FramebufferWidth = 1280,
            FramebufferHeight = 720,
            FramebufferScaleX = 1f,
            FramebufferScaleY = 1f,
            State = AutomationWindowState.Normal,
            Focused = true,
            Title = "PixelEngine Editor",
        };
        Assert.False(EditorAutomationAuthoringApi.WindowRequestWouldChange(
            window,
            new AutomationWindowSetRequest
            {
                X = 20,
                Y = 30,
                Width = 1280,
                Height = 720,
                State = AutomationWindowState.Normal,
                Activate = true,
            }));
        Assert.True(EditorAutomationAuthoringApi.WindowRequestWouldChange(
            window,
            new AutomationWindowSetRequest { State = AutomationWindowState.Maximized }));

        AutomationSceneToolSnapshot tool = new()
        {
            Tool = AutomationSceneTool.Move,
            GizmoSpace = AutomationGizmoSpace.Local,
            GridVisible = true,
            SnapEnabled = false,
            MoveSnap = 1f,
            RotationSnapDegrees = 15f,
            ScaleSnap = 0.1f,
            CameraCenterX = 12f,
            CameraCenterY = -4f,
            CameraCellsPerPixel = 0.5f,
            BrushPanelVisible = false,
            OverlayDock = AutomationSceneToolOverlayDock.Left,
            OverlayOffsetX = 8f,
            OverlayOffsetY = 8f,
        };
        Assert.False(EditorAutomationAuthoringApi.SceneToolRequestWouldChange(
            tool,
            new AutomationSceneToolSetRequest
            {
                CameraCenterX = 12f,
                CameraCenterY = -4f,
                CameraCellsPerPixel = 0.5f,
            }));
        Assert.True(EditorAutomationAuthoringApi.SceneToolRequestWouldChange(
            tool,
            new AutomationSceneToolSetRequest { CameraCenterX = 12f }));
        Assert.True(EditorAutomationAuthoringApi.SceneToolRequestWouldChange(
            tool,
            new AutomationSceneToolSetRequest { SnapEnabled = true }));

        EditorPanelSnapshot source = new(
            "panel.scene", "Scene", true, false, false, false,
            true, true, "dock:main", 0f, 0f, 640f, 720f);
        EditorPanelSnapshot target = new(
            "panel.inspector", "Inspector", true, false, false, false,
            true, true, "dock:main", 640f, 0f, 320f, 720f);
        Assert.True(EditorAutomationAuthoringApi.PanelDockRequestAlreadyApplied(
            source,
            target,
            new AutomationPanelDockRequest
            {
                PanelId = source.Id,
                TargetPanelId = target.Id,
                Placement = AutomationPanelDockPlacement.Tab,
            }));
        Assert.False(EditorAutomationAuthoringApi.PanelDockRequestAlreadyApplied(
            source,
            target with { DockGroupId = "dock:other" },
            new AutomationPanelDockRequest
            {
                PanelId = source.Id,
                TargetPanelId = target.Id,
                Placement = AutomationPanelDockPlacement.Tab,
            }));
    }

    private sealed class TestEditorCommand : IEditorCommand
    {
        public string Name => "Test";

        public void Execute(EditorSceneModel scene)
        {
            ArgumentNullException.ThrowIfNull(scene);
        }

        public void Undo(EditorSceneModel scene)
        {
            ArgumentNullException.ThrowIfNull(scene);
        }
    }

    private sealed class RetryableEditorCommand : IEditorCommand
    {
        public string Name => "Retryable";

        public int Value { get; set; }

        public bool ThrowOnExecute { get; set; }

        public bool ThrowOnUndo { get; set; }

        public void Execute(EditorSceneModel scene)
        {
            ArgumentNullException.ThrowIfNull(scene);
            if (ThrowOnExecute)
            {
                throw new InvalidOperationException("simulated redo failure");
            }

            Value = 1;
        }

        public void Undo(EditorSceneModel scene)
        {
            ArgumentNullException.ThrowIfNull(scene);
            if (ThrowOnUndo)
            {
                throw new InvalidOperationException("simulated undo failure");
            }

            Value = 0;
        }
    }

    private sealed class NoopUndoSink : IAutomationUndoSink
    {
        public void RecordExecuted(IAutomationUndoAction action)
        {
            ArgumentNullException.ThrowIfNull(action);
        }
    }

    private sealed class NoopTransactionParticipant : IAutomationTransactionParticipant
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

    private static string FindRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"找不到仓库文件 {relativePath}。");
    }
}
