using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PixelEngine.Editor.Automation.Protocol;
using PixelEngine.Editor.Automation.Server;
using PixelEngine.Hosting;
using PixelEngine.Scripting;
using PixelEngine.UI;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 手动 Editor UI 与外部 automation 共用的 authoring 语义注册表。
/// 所有 delegate 只会由 <see cref="AutomationMainThreadScheduler"/> 在 EditorIngress 调用。
/// </summary>
internal sealed class EditorAutomationAuthoringApi(EditorShellApp app) : IDisposable
{
    internal const long MaximumBrushStrokeCellVisits = 4_000_000;
    private const string WorkspaceResource = "editor:workspace";
    private const string ProjectResource = "editor:project";
    private const string WindowResource = "editor:window";
    private const string LayoutResource = "editor:layout";
    private const string PanelsResource = "editor:panels";
    private const string TransitionResource = "editor:transition";
    private readonly EditorShellApp _app = app ?? throw new ArgumentNullException(nameof(app));
    private readonly byte[] _cursorKey = RandomNumberGenerator.GetBytes(32);
    private int _disposed;

    public AutomationMethodRegistration[] CreateRegistrations()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return
        [
            Read(
                AutomationProtocolConstants.WorkspaceGetMethod,
                "workspace",
                "emptyRequest",
                "workspaceSnapshot",
                ["menu.file", "toolbar.play-controls"],
                GetWorkspace),
            Read(
                AutomationProtocolConstants.WorkspaceTransitionGetMethod,
                "workspace",
                "emptyRequest",
                "transitionResult",
                ["dialog.unsaved-scene"],
                GetTransition),
            Command(
                AutomationProtocolConstants.WorkspaceTransitionResolveMethod,
                "workspace",
                "transitionResolveRequest",
                "transitionResult",
                [AutomationScopes.EditorControl],
                ["edit"],
                ["dialog.unsaved-scene.save", "dialog.unsaved-scene.discard", "dialog.unsaved-scene.cancel"],
                ResolveTransition),
            Read(
                AutomationProtocolConstants.EditorHistoryGetMethod,
                "workspace",
                "emptyRequest",
                "historySnapshot",
                ["menu.edit.undo", "menu.edit.redo"],
                GetHistory),
            Command(
                AutomationProtocolConstants.EditorHistoryUndoMethod,
                "workspace",
                "emptyRequest",
                "historySnapshot",
                [AutomationScopes.ProjectWrite],
                ["edit"],
                ["menu.edit.undo", "shortcut.ctrl-z"],
                Undo),
            Command(
                AutomationProtocolConstants.EditorHistoryRedoMethod,
                "workspace",
                "emptyRequest",
                "historySnapshot",
                [AutomationScopes.ProjectWrite],
                ["edit"],
                ["menu.edit.redo", "shortcut.ctrl-y"],
                Redo),
            Read(
                AutomationProtocolConstants.WindowGetMethod,
                "window",
                "emptyRequest",
                "windowSnapshot",
                [],
                GetWindow),
            Command(
                AutomationProtocolConstants.WindowResizeMethod,
                "window",
                "windowResizeRequest",
                "windowSnapshot",
                [AutomationScopes.EditorControl],
                AllModes,
                ["window.resize"],
                ResizeWindow),
            Command(
                AutomationProtocolConstants.WindowSetMethod,
                "window",
                "windowSetRequest",
                "windowSnapshot",
                [AutomationScopes.EditorControl],
                AllModes,
                ["window.move", "window.resize", "window.minimize", "window.maximize", "window.fullscreen", "window.focus"],
                SetWindow),
            Read(
                AutomationProtocolConstants.WindowPanelListMethod,
                "window",
                "pageRequest",
                "panelListResponse",
                ["menu.window"],
                ListPanels),
            Command(
                AutomationProtocolConstants.WindowPanelSetMethod,
                "window",
                "panelSetRequest",
                "panelInfo",
                [AutomationScopes.EditorControl],
                AllModes,
                ["menu.window", "panel.close", "panel.focus"],
                SetPanel),
            Command(
                AutomationProtocolConstants.WindowPanelDockMethod,
                "window",
                "panelDockRequest",
                "dockLayoutSnapshot",
                [AutomationScopes.EditorControl],
                AllModes,
                ["panel.drag-dock", "panel.undock", "panel.tab-merge"],
                DockPanel),
            Read(
                AutomationProtocolConstants.WindowLayoutGetMethod,
                "window",
                "emptyRequest",
                "dockLayoutSnapshot",
                ["menu.window.layout.export"],
                GetLayout),
            Command(
                AutomationProtocolConstants.WindowLayoutSetMethod,
                "window",
                "dockLayoutSetRequest",
                "dockLayoutSnapshot",
                [AutomationScopes.EditorControl],
                AllModes,
                ["menu.window.layout.import"],
                SetLayout),
            Command(
                AutomationProtocolConstants.WindowLayoutResetMethod,
                "window",
                "emptyRequest",
                "commandResult",
                [AutomationScopes.EditorControl],
                AllModes,
                ["menu.window.reset-layout"],
                ResetLayout),
            Read(
                AutomationProtocolConstants.SceneGetMethod,
                "scene",
                "emptyRequest",
                "sceneSnapshot",
                ["menu.file.scene"],
                GetScene),
            Command(
                AutomationProtocolConstants.SceneSaveMethod,
                "scene",
                "emptyRequest",
                "sceneSnapshot",
                [AutomationScopes.ProjectWrite],
                ["edit"],
                ["menu.file.save", "shortcut.ctrl-s"],
                SaveScene),
            Command(
                AutomationProtocolConstants.SceneSaveAsMethod,
                "scene",
                "sceneSaveAsRequest",
                "sceneSnapshot",
                [AutomationScopes.ProjectWrite],
                ["edit"],
                ["menu.file.save-as"],
                SaveSceneAs),
            Command(
                AutomationProtocolConstants.SceneNewMethod,
                "scene",
                "emptyRequest",
                "transitionResult",
                [AutomationScopes.ProjectWrite],
                ["edit"],
                ["menu.file.new-scene"],
                NewScene),
            Command(
                AutomationProtocolConstants.SceneOpenMethod,
                "scene",
                "scenePathRequest",
                "transitionResult",
                [AutomationScopes.ProjectWrite],
                ["edit"],
                ["menu.file.open-scene", "project.open-scene"],
                OpenScene),
            Read(
                AutomationProtocolConstants.HierarchyListMethod,
                "hierarchy",
                "pageRequest",
                "hierarchyListResponse",
                ["panel.hierarchy"],
                ListHierarchy),
            Read(
                AutomationProtocolConstants.HierarchyGetMethod,
                "hierarchy",
                "gameObjectRequest",
                "gameObjectSnapshot",
                ["panel.hierarchy", "panel.inspector"],
                GetGameObject),
            Read(
                AutomationProtocolConstants.HierarchySelectionGetMethod,
                "hierarchy",
                "emptyRequest",
                "selectionSnapshot",
                ["panel.hierarchy.selection", "panel.scene.selection"],
                GetSelection),
            Command(
                AutomationProtocolConstants.HierarchySelectionSetMethod,
                "hierarchy",
                "selectionSetRequest",
                "selectionSnapshot",
                [AutomationScopes.EditorControl],
                AllModes,
                ["panel.hierarchy.selection", "panel.scene.selection"],
                SetSelection),
            Write(
                AutomationProtocolConstants.GameObjectCreateMethod,
                "hierarchy",
                "gameObjectCreateRequest",
                "gameObjectSnapshot",
                ["menu.game-object.create", "panel.hierarchy.create"],
                CreateGameObject),
            Write(
                AutomationProtocolConstants.GameObjectDeleteMethod,
                "hierarchy",
                "gameObjectRequest",
                "commandResult",
                ["shortcut.delete", "panel.hierarchy.delete"],
                DeleteGameObject),
            Write(
                AutomationProtocolConstants.GameObjectDuplicateMethod,
                "hierarchy",
                "gameObjectRequest",
                "gameObjectSnapshot",
                ["shortcut.ctrl-d", "panel.hierarchy.duplicate"],
                DuplicateGameObject),
            Write(
                AutomationProtocolConstants.GameObjectRenameMethod,
                "hierarchy",
                "gameObjectRenameRequest",
                "gameObjectSnapshot",
                ["shortcut.f2", "panel.hierarchy.rename"],
                RenameGameObject),
            Write(
                AutomationProtocolConstants.GameObjectSetEnabledMethod,
                "hierarchy",
                "gameObjectBoolRequest",
                "gameObjectSnapshot",
                ["panel.inspector.enabled"],
                SetGameObjectEnabled),
            Write(
                AutomationProtocolConstants.GameObjectReparentMethod,
                "hierarchy",
                "gameObjectReparentRequest",
                "gameObjectSnapshot",
                ["panel.hierarchy.drag-drop"],
                ReparentGameObject),
            Write(
                AutomationProtocolConstants.GameObjectSetSceneVisibleMethod,
                "hierarchy",
                "gameObjectBoolRequest",
                "gameObjectSnapshot",
                ["panel.hierarchy.visibility"],
                SetGameObjectSceneVisible),
            Write(
                AutomationProtocolConstants.GameObjectSetScenePickableMethod,
                "hierarchy",
                "gameObjectBoolRequest",
                "gameObjectSnapshot",
                ["panel.hierarchy.pickability"],
                SetGameObjectScenePickable),
            Write(
                AutomationProtocolConstants.HierarchySetAllSceneVisibleMethod,
                "hierarchy",
                "boolValueRequest",
                "commandResult",
                ["panel.hierarchy.visibility-all"],
                SetAllSceneVisible),
            Write(
                AutomationProtocolConstants.HierarchySetAllScenePickableMethod,
                "hierarchy",
                "boolValueRequest",
                "commandResult",
                ["panel.hierarchy.pickability-all"],
                SetAllScenePickable),
            Write(
                AutomationProtocolConstants.PrefabCreateMethod,
                "hierarchy",
                "prefabCreateRequest",
                "prefabCreateResult",
                ["panel.hierarchy.create-prefab"],
                CreatePrefab),
            Write(
                AutomationProtocolConstants.PrefabInstantiateMethod,
                "hierarchy",
                "prefabInstantiateRequest",
                "gameObjectSnapshot",
                ["project.prefab.open", "project.prefab.drag-to-hierarchy"],
                InstantiatePrefab),
            Read(
                AutomationProtocolConstants.InspectorGetMethod,
                "inspector",
                "inspectorGetRequest",
                "gameObjectSnapshot",
                ["panel.inspector"],
                GetInspector),
            Write(
                AutomationProtocolConstants.InspectorTransformSetMethod,
                "inspector",
                "transformSetRequest",
                "gameObjectSnapshot",
                ["panel.inspector.transform", "panel.scene.gizmo"],
                SetTransform),
            Write(
                AutomationProtocolConstants.InspectorComponentAddMethod,
                "inspector",
                "componentAddRequest",
                "gameObjectSnapshot",
                ["panel.inspector.add-component"],
                AddComponent),
            Write(
                AutomationProtocolConstants.InspectorComponentRemoveMethod,
                "inspector",
                "componentRemoveRequest",
                "gameObjectSnapshot",
                ["panel.inspector.component.remove"],
                RemoveComponent),
            Write(
                AutomationProtocolConstants.InspectorComponentSetEnabledMethod,
                "inspector",
                "componentEnabledSetRequest",
                "gameObjectSnapshot",
                ["panel.inspector.component.enabled"],
                SetComponentEnabled),
            Write(
                AutomationProtocolConstants.InspectorComponentMoveMethod,
                "inspector",
                "componentMoveRequest",
                "gameObjectSnapshot",
                ["panel.inspector.component.move"],
                MoveComponent),
            Write(
                AutomationProtocolConstants.InspectorComponentSetFieldMethod,
                "inspector",
                "componentFieldSetRequest",
                "gameObjectSnapshot",
                ["panel.inspector.component.field"],
                SetComponentField),
            Write(
                AutomationProtocolConstants.InspectorCanvasSetMethod,
                "inspector",
                "builtInCanvasSetRequest",
                "gameObjectSnapshot",
                ["panel.inspector.canvas", "panel.inspector.canvas-scaler"],
                SetBuiltInCanvas),
            Write(
                AutomationProtocolConstants.InspectorCanvasSetPrimaryMethod,
                "inspector",
                "canvasPrimarySetRequest",
                "gameObjectSnapshot",
                ["panel.inspector.canvas.primary"],
                SetCanvasPrimary),
            Write(
                AutomationProtocolConstants.PrefabRevertOverridesMethod,
                "inspector",
                "gameObjectRequest",
                "gameObjectSnapshot",
                ["panel.inspector.prefab.revert-overrides"],
                RevertPrefabOverrides),
            Read(
                AutomationProtocolConstants.SceneToolGetMethod,
                "tool",
                "emptyRequest",
                "sceneToolSnapshot",
                ["panel.scene.toolbar"],
                GetSceneTool),
            Command(
                AutomationProtocolConstants.SceneToolSetMethod,
                "tool",
                "sceneToolSetRequest",
                "sceneToolSnapshot",
                [AutomationScopes.EditorControl],
                ["edit"],
                ["panel.scene.toolbar", "panel.scene.camera", "panel.scene.snap", "shortcut.w", "shortcut.e", "shortcut.r"],
                SetSceneTool),
            Command(
                AutomationProtocolConstants.SceneToolFrameMethod,
                "tool",
                "sceneFrameRequest",
                "sceneToolSnapshot",
                [AutomationScopes.EditorControl],
                ["edit"],
                ["shortcut.f", "panel.scene.frame"],
                FrameScene),
            Command(
                AutomationProtocolConstants.BrushApplyMethod,
                "tool",
                "brushApplyRequest",
                "brushApplyResult",
                [AutomationScopes.ProjectWrite],
                ["edit"],
                ["panel.scene.brush"],
                ApplyBrush),
            Command(
                AutomationProtocolConstants.BrushStrokeMethod,
                "tool",
                "brushStrokeRequest",
                "brushStrokeResult",
                [AutomationScopes.ProjectWrite],
                ["edit"],
                ["panel.scene.brush.stroke"],
                ApplyBrushStroke),
        ];
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            CryptographicOperations.ZeroMemory(_cursorKey);
        }
    }

    private static string[] AllModes => ["edit", "play", "paused"];

    private static AutomationMethodRegistration Read(
        string method,
        string domain,
        string requestSchema,
        string responseSchema,
        string[] uiCommandIds,
        AutomationScheduledOperation operation)
    {
        return Registration(
            method,
            domain,
            requestSchema,
            responseSchema,
            [AutomationScopes.EditorRead],
            AllModes,
            AutomationOperationKind.Read,
            AutomationTransactionMode.Forbidden,
            requiresExpectedRevision: false,
            requiresIdempotencyKey: false,
            eventTypes: [],
            uiCommandIds,
            operation);
    }

    private static AutomationMethodRegistration Command(
        string method,
        string domain,
        string requestSchema,
        string responseSchema,
        string[] scopes,
        string[] modes,
        string[] uiCommandIds,
        AutomationScheduledOperation operation,
        bool stateChanging = true)
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
            requiresExpectedRevision: stateChanging,
            requiresIdempotencyKey: stateChanging,
            eventTypes: stateChanging ? [AutomationProtocolConstants.StateChangedEventType] : [],
            uiCommandIds,
            operation);
    }

    private static AutomationMethodRegistration Write(
        string method,
        string domain,
        string requestSchema,
        string responseSchema,
        string[] uiCommandIds,
        AutomationScheduledOperation operation)
    {
        return Registration(
            method,
            domain,
            requestSchema,
            responseSchema,
            [AutomationScopes.ProjectWrite],
            ["edit"],
            AutomationOperationKind.Write,
            AutomationTransactionMode.Optional,
            requiresExpectedRevision: true,
            requiresIdempotencyKey: true,
            eventTypes: [AutomationProtocolConstants.StateChangedEventType],
            uiCommandIds,
            operation);
    }

    private static AutomationMethodRegistration Registration(
        string method,
        string domain,
        string requestSchema,
        string responseSchema,
        string[] scopes,
        string[] modes,
        AutomationOperationKind operationKind,
        AutomationTransactionMode transactionMode,
        bool requiresExpectedRevision,
        bool requiresIdempotencyKey,
        string[] eventTypes,
        string[] uiCommandIds,
        AutomationScheduledOperation operation)
    {
        return new AutomationMethodRegistration
        {
            Descriptor = new AutomationMethodDescriptor
            {
                Method = method,
                Domain = domain,
                RequestSchema = $"#/$defs/{requestSchema}",
                ResponseSchema = $"#/$defs/{responseSchema}",
                RequiredScopes = scopes,
                SupportedModes = modes,
                OperationKind = operationKind,
                ExecutionPhase = AutomationExecutionPhase.EditorIngress,
                TransactionMode = transactionMode,
                RequiresExpectedRevision = requiresExpectedRevision,
                RequiresIdempotencyKey = requiresIdempotencyKey,
                EventTypes = eventTypes,
                ArtifactBehavior = AutomationArtifactBehavior.None,
                UiCommandIds = uiCommandIds,
            },
            Operation = operation,
        };
    }

    private AutomationOperationResult GetWorkspace(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.WorkspaceGetMethod);
        AutomationWorkspaceSnapshot snapshot = CaptureWorkspace();
        string[] resources = _app.CurrentSession is null
            ? [WorkspaceResource, ProjectResource, TransitionResource]
            :
            [
                WorkspaceResource,
                ProjectResource,
                TransitionResource,
                EditorAutomationRuntime.CreateSceneResourceId(_app.CurrentSession),
            ];
        return Result(snapshot, AutomationJsonContext.Default.AutomationWorkspaceSnapshot, resources);
    }

    private AutomationOperationResult GetTransition(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        EnsureEmpty(payload, AutomationProtocolConstants.WorkspaceTransitionGetMethod);
        return Result(
            CaptureTransition(),
            AutomationJsonContext.Default.AutomationTransitionResult,
            [TransitionResource]);
    }

    private AutomationOperationResult ResolveTransition(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationTransitionResolveRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationTransitionResolveRequest,
            AutomationProtocolConstants.WorkspaceTransitionResolveMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.WorkspaceTransitionResolveMethod);
        if (!Enum.IsDefined(request.Decision))
        {
            throw Invalid("transition decision 无效。");
        }

        string[] beforeResources = CurrentWorkspaceResources(includeTransition: true);
        context.Revisions.EnsureCanAdvance(beforeResources);
        long revisionBefore = context.Revisions.GlobalRevision;
        EditorTransitionResult result = _app.ResolveTransition(request.Decision switch
        {
            AutomationTransitionDecision.Save => EditorTransitionDecision.Save,
            AutomationTransitionDecision.Discard => EditorTransitionDecision.Discard,
            AutomationTransitionDecision.Cancel => EditorTransitionDecision.Cancel,
            _ => throw Invalid("transition decision 无效。"),
        });
        string[] resources = [.. CurrentWorkspaceResources(includeTransition: true)
            .Concat(beforeResources)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
        bool changed = result.Status is EditorTransitionStatus.Executed or EditorTransitionStatus.Cancelled;
        bool eventAlreadyPublished = context.Revisions.GlobalRevision != revisionBefore;
        if (changed && !eventAlreadyPublished)
        {
            _ = context.Revisions.Advance([TransitionResource]);
        }

        AutomationRevisionSnapshot revision = context.Revisions.Capture(resources);
        return Result(
            MapTransitionResult(result),
            AutomationJsonContext.Default.AutomationTransitionResult,
            resources,
            changed ? revision : null,
            stateEventAlreadyPublished: eventAlreadyPublished);
    }

    private AutomationOperationResult GetHistory(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        EnsureEmpty(payload, AutomationProtocolConstants.EditorHistoryGetMethod);
        EditorProjectSession session = RequireSession();
        return Result(
            CaptureHistory(session),
            AutomationJsonContext.Default.AutomationHistorySnapshot,
            [EditorAutomationRuntime.CreateSceneResourceId(session)]);
    }

    private AutomationOperationResult Undo(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.EditorHistoryUndoMethod);
        return ApplyHistory(context, undo: true);
    }

    private AutomationOperationResult Redo(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.EditorHistoryRedoMethod);
        return ApplyHistory(context, undo: false);
    }

    private AutomationOperationResult ApplyHistory(
        AutomationScheduledContext context,
        bool undo)
    {
        EditorProjectSession session = RequireEditSession();
        session.FlushPendingAuthoringEdits();
        string[] resources = [EditorAutomationRuntime.CreateSceneResourceId(session)];
        context.Revisions.EnsureCanAdvance(resources);
        long revisionBefore = context.Revisions.GlobalRevision;
        bool applied = undo
            ? session.UndoStack.Undo(session.SceneModel, notifyHistoryApplied: false)
            : session.UndoStack.Redo(session.SceneModel, notifyHistoryApplied: false);
        if (!applied)
        {
            throw StateUnavailable(undo ? "Undo history 为空。" : "Redo history 为空。");
        }

        bool eventAlreadyPublished = context.Revisions.GlobalRevision != revisionBefore;
        AutomationRevisionSnapshot revision = eventAlreadyPublished
            ? context.Revisions.Capture(resources)
            : context.Revisions.Advance(resources);
        return Result(
            CaptureHistory(session),
            AutomationJsonContext.Default.AutomationHistorySnapshot,
            resources,
            revision,
            stateEventAlreadyPublished: eventAlreadyPublished);
    }

    private AutomationOperationResult GetWindow(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        EnsureEmpty(payload, AutomationProtocolConstants.WindowGetMethod);
        return Result(
            _app.CaptureAutomationWindow(),
            AutomationJsonContext.Default.AutomationWindowSnapshot,
            [WindowResource]);
    }

    private AutomationOperationResult ResizeWindow(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationWindowResizeRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationWindowResizeRequest,
            AutomationProtocolConstants.WindowResizeMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.WindowResizeMethod);
        AutomationWindowSetRequest setRequest = new()
        {
            Width = request.Width,
            Height = request.Height,
            State = AutomationWindowState.Normal,
        };
        if (!_app.TryValidateAutomationWindowRequest(setRequest, out string diagnostic))
        {
            throw Invalid(diagnostic);
        }

        string[] resources = [WindowResource, WorkspaceResource];
        AutomationWindowSnapshot before = _app.CaptureAutomationWindow();
        if (!WindowRequestWouldChange(before, setRequest))
        {
            return Result(
                before,
                AutomationJsonContext.Default.AutomationWindowSnapshot,
                resources);
        }

        context.Revisions.EnsureCanAdvance(resources);
        if (!_app.TrySetAutomationWindow(
            setRequest,
            out diagnostic,
            out bool workspaceChanged))
        {
            throw Invalid(diagnostic);
        }

        AutomationWindowSnapshot snapshot = _app.CaptureAutomationWindow();
        if (snapshot == before && !workspaceChanged)
        {
            return Result(
                snapshot,
                AutomationJsonContext.Default.AutomationWindowSnapshot,
                resources);
        }

        AutomationRevisionSnapshot revision = context.Revisions.Advance(resources);
        return Result(
            snapshot,
            AutomationJsonContext.Default.AutomationWindowSnapshot,
            resources,
            revision);
    }

    private AutomationOperationResult SetWindow(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationWindowSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationWindowSetRequest,
            AutomationProtocolConstants.WindowSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.WindowSetMethod);
        if (!_app.TryValidateAutomationWindowRequest(request, out string diagnostic))
        {
            throw Invalid(diagnostic);
        }

        string[] resources = [WindowResource, WorkspaceResource];
        AutomationWindowSnapshot before = _app.CaptureAutomationWindow();
        if (!WindowRequestWouldChange(before, request))
        {
            return Result(
                before,
                AutomationJsonContext.Default.AutomationWindowSnapshot,
                resources);
        }

        context.Revisions.EnsureCanAdvance(resources);
        if (!_app.TrySetAutomationWindow(
            request,
            out diagnostic,
            out bool workspaceChanged))
        {
            throw Invalid(diagnostic);
        }

        AutomationWindowSnapshot snapshot = _app.CaptureAutomationWindow();
        if (snapshot == before && !workspaceChanged)
        {
            return Result(
                snapshot,
                AutomationJsonContext.Default.AutomationWindowSnapshot,
                resources);
        }

        AutomationRevisionSnapshot revision = context.Revisions.Advance(resources);
        return Result(
            snapshot,
            AutomationJsonContext.Default.AutomationWindowSnapshot,
            resources,
            revision);
    }

    private AutomationOperationResult ListPanels(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EditorProjectSession session = RequireSession();
        AutomationPageRequest request = DeserializePage(
            payload,
            AutomationProtocolConstants.WindowPanelListMethod);
        EditorPanelSnapshot[] source = session.CaptureAutomationPanels();
        AutomationPanelInfo[] items = [.. source.Select(MapPanel)];
        string fingerprint = Fingerprint(items, AutomationJsonContext.Default.AutomationPanelInfoArray);
        AutomationPanelInfo[] filtered = FilterPanels(items, request.Filter);
        SortPanels(filtered, request.Sort);
        PageSlice<AutomationPanelInfo> page = SlicePage(
            "panels",
            fingerprint,
            request,
            filtered);
        AutomationPanelListResponse response = new()
        {
            Items = page.Items,
            Page = page.Info,
        };
        return Result(
            response,
            AutomationJsonContext.Default.AutomationPanelListResponse,
            [PanelsResource]);
    }

    private AutomationOperationResult SetPanel(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EditorProjectSession session = RequireSession();
        AutomationPanelSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationPanelSetRequest,
            AutomationProtocolConstants.WindowPanelSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.WindowPanelSetMethod);
        string panelId = ValidateIdentifier(request.PanelId, "panelId", 128);
        string[] resources = [PanelsResource, $"editor:panel:{panelId}"];
        EditorPanelSnapshot[] before = session.CaptureAutomationPanels();
        int panelIndex = Array.FindIndex(
            before,
            item => string.Equals(item.Id, panelId, StringComparison.Ordinal));
        if (panelIndex < 0)
        {
            throw NotFound($"Panel '{panelId}' 不存在。");
        }

        EditorPanelSnapshot current = before[panelIndex];
        bool targetFocusPending = request.Visible && (request.Focus || current.FocusPending);
        if (current.Visible == request.Visible && current.FocusPending == targetFocusPending)
        {
            return Result(
                MapPanel(current),
                AutomationJsonContext.Default.AutomationPanelInfo,
                resources);
        }

        context.Revisions.EnsureCanAdvance(resources);

        EditorPanelSnapshot panel;
        try
        {
            if (!session.TrySetAutomationPanel(panelId, request.Visible, request.Focus))
            {
                throw StateUnavailable($"Panel '{panelId}' 状态无法更新。");
            }

            panel = session.CaptureAutomationPanels()
                .Single(item => string.Equals(item.Id, panelId, StringComparison.Ordinal));
        }
        catch (Exception exception)
        {
            if (!session.TryRestoreAutomationPanels(before))
            {
                throw new AggregateException(
                    $"Panel '{panelId}' 更新失败且无法恢复 before state。",
                    exception);
            }

            throw;
        }

        AutomationRevisionSnapshot revision = context.Revisions.Advance(resources);
        AutomationPanelInfo response = MapPanel(panel);
        return Result(
            response,
            AutomationJsonContext.Default.AutomationPanelInfo,
            resources,
            revision);
    }

    private AutomationOperationResult GetLayout(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        EnsureEmpty(payload, AutomationProtocolConstants.WindowLayoutGetMethod);
        EditorProjectSession session = RequireSession();
        return Result(
            CaptureDockLayout(session),
            AutomationJsonContext.Default.AutomationDockLayoutSnapshot,
            [LayoutResource, PanelsResource]);
    }

    private AutomationOperationResult SetLayout(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EditorProjectSession session = RequireSession();
        AutomationDockLayoutSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationDockLayoutSetRequest,
            AutomationProtocolConstants.WindowLayoutSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.WindowLayoutSetMethod);
        if (request.Layout is null)
        {
            throw Invalid("dockLayoutSetRequest.layout 不得为 null。");
        }

        ValidateSchema(request.Layout.SchemaVersion, "layout.schemaVersion");
        string normalized = ValidateDockLayout(request.Layout);
        EditorPanelSnapshot[] beforePanels = session.CaptureAutomationPanels();
        EditorPanelSnapshot[] nextPanels = ResolveLayoutPanels(beforePanels, request.Layout.Panels);
        string beforeLayout = CaptureDockLayoutText(session);
        string[] resources = [LayoutResource, PanelsResource];
        if (string.Equals(beforeLayout, normalized, StringComparison.Ordinal) &&
            beforePanels.AsSpan().SequenceEqual(nextPanels))
        {
            return Result(
                CaptureDockLayout(session),
                AutomationJsonContext.Default.AutomationDockLayoutSnapshot,
                resources);
        }

        context.Revisions.EnsureCanAdvance(resources);
        try
        {
            session.ApplyAutomationDockLayout(normalized);
            if (!session.TryRestoreAutomationPanels(nextPanels))
            {
                throw StateUnavailable("Dock layout 的 panel registry 与当前 Editor 不匹配。");
            }

            string applied = CaptureDockLayoutText(session);
            if (!_app.TryPersistAutomationLayout(applied, out _, out string diagnostic))
            {
                throw StateUnavailable(diagnostic);
            }
        }
        catch (Exception exception)
        {
            RestoreDockLayoutOrThrow(session, beforeLayout, beforePanels, exception);
            throw;
        }

        AutomationRevisionSnapshot revision = context.Revisions.Advance(resources);
        return Result(
            CaptureDockLayout(session),
            AutomationJsonContext.Default.AutomationDockLayoutSnapshot,
            resources,
            revision);
    }

    private AutomationOperationResult DockPanel(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EditorProjectSession session = RequireSession();
        AutomationPanelDockRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationPanelDockRequest,
            AutomationProtocolConstants.WindowPanelDockMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.WindowPanelDockMethod);
        string panelId = ValidateIdentifier(request.PanelId, "panelId", 128);
        string? targetPanelId = request.TargetPanelId is null
            ? null
            : ValidateIdentifier(request.TargetPanelId, "targetPanelId", 128);
        EditorDockWindowRequest dockRequest = ValidatePanelDockRequest(request);
        EditorPanelSnapshot[] beforePanels = session.CaptureAutomationPanels();
        if (!beforePanels.Any(item => string.Equals(item.Id, panelId, StringComparison.Ordinal)))
        {
            throw NotFound($"Panel '{panelId}' 不存在。");
        }

        if (targetPanelId is not null &&
            !beforePanels.Any(item => string.Equals(item.Id, targetPanelId, StringComparison.Ordinal)))
        {
            throw NotFound($"目标 panel '{targetPanelId}' 不存在。");
        }

        string beforeLayout = CaptureDockLayoutText(session);
        List<string> resourceList = [LayoutResource, PanelsResource, $"editor:panel:{panelId}"];
        if (targetPanelId is not null)
        {
            resourceList.Add($"editor:panel:{targetPanelId}");
        }

        string[] resources = [.. resourceList];
        EditorPanelSnapshot sourcePanel = beforePanels.Single(
            item => string.Equals(item.Id, panelId, StringComparison.Ordinal));
        EditorPanelSnapshot? targetPanel = targetPanelId is null
            ? null
            : beforePanels.Single(item => string.Equals(item.Id, targetPanelId, StringComparison.Ordinal));
        if (PanelDockRequestAlreadyApplied(sourcePanel, targetPanel, request))
        {
            return Result(
                CaptureDockLayout(session),
                AutomationJsonContext.Default.AutomationDockLayoutSnapshot,
                resources);
        }

        context.Revisions.EnsureCanAdvance(resources);
        try
        {
            if (!session.TrySetAutomationPanelDock(
                panelId,
                targetPanelId,
                dockRequest,
                out string diagnostic))
            {
                throw StateUnavailable(diagnostic);
            }

            string applied = CaptureDockLayoutText(session);
            if (!_app.TryPersistAutomationLayout(applied, out _, out diagnostic))
            {
                throw StateUnavailable(diagnostic);
            }
        }
        catch (Exception exception)
        {
            RestoreDockLayoutOrThrow(session, beforeLayout, beforePanels, exception);
            throw;
        }

        string afterLayout = CaptureDockLayoutText(session);
        EditorPanelSnapshot[] afterPanels = session.CaptureAutomationPanels();
        if (string.Equals(beforeLayout, afterLayout, StringComparison.Ordinal) &&
            beforePanels.AsSpan().SequenceEqual(afterPanels))
        {
            return Result(
                CaptureDockLayout(session),
                AutomationJsonContext.Default.AutomationDockLayoutSnapshot,
                resources);
        }

        AutomationRevisionSnapshot revision = context.Revisions.Advance(resources);
        return Result(
            CaptureDockLayout(session),
            AutomationJsonContext.Default.AutomationDockLayoutSnapshot,
            resources,
            revision);
    }

    private AutomationOperationResult ResetLayout(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EditorProjectSession session = RequireSession();
        EnsureEmpty(payload, AutomationProtocolConstants.WindowLayoutResetMethod);
        string beforeLayout = CaptureDockLayoutText(session);
        EditorPanelSnapshot[] beforePanels = session.CaptureAutomationPanels();
        if (!_app.TryCaptureAutomationLayoutPersistence(
            out EditorLayoutPersistenceSnapshot beforePersistence,
            out string persistenceDiagnostic))
        {
            throw StateUnavailable(persistenceDiagnostic);
        }

        string[] resources = [LayoutResource, PanelsResource];
        context.Revisions.EnsureCanAdvance(resources);
        string diagnostic;
        bool resetSucceeded;
        string? afterLayout = null;
        EditorPanelSnapshot[]? afterPanels = null;
        try
        {
            resetSucceeded = _app.TryResetAutomationLayout(out diagnostic);
            if (resetSucceeded)
            {
                afterLayout = CaptureDockLayoutText(session);
                afterPanels = session.CaptureAutomationPanels();
            }
        }
        catch (Exception exception)
        {
            RestoreResetLayoutOrThrow(
                session,
                beforeLayout,
                beforePanels,
                beforePersistence,
                exception);
            throw;
        }

        if (!resetSucceeded)
        {
            throw StateUnavailable(diagnostic);
        }

        if (string.Equals(beforeLayout, afterLayout, StringComparison.Ordinal) &&
            beforePanels.AsSpan().SequenceEqual(afterPanels!))
        {
            AutomationCommandResult unchanged = new()
            {
                Succeeded = true,
                Diagnostic = diagnostic,
                ResourceIds = resources,
            };
            return Result(
                unchanged,
                AutomationJsonContext.Default.AutomationCommandResult,
                resources);
        }

        AutomationRevisionSnapshot revision = context.Revisions.Advance(resources);
        AutomationCommandResult response = new()
        {
            Succeeded = true,
            Diagnostic = diagnostic,
            ResourceIds = resources,
        };
        return Result(
            response,
            AutomationJsonContext.Default.AutomationCommandResult,
            resources,
            revision);
    }

    private AutomationOperationResult GetScene(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        EnsureEmpty(payload, AutomationProtocolConstants.SceneGetMethod);
        EditorProjectSession session = RequireSession();
        AutomationSceneSnapshot snapshot = CaptureScene(session);
        return Result(
            snapshot,
            AutomationJsonContext.Default.AutomationSceneSnapshot,
            [snapshot.ResourceId]);
    }

    private AutomationOperationResult SaveScene(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.SceneSaveMethod);
        EditorProjectSession session = RequireEditSession();
        string resource = EditorAutomationRuntime.CreateSceneResourceId(session);
        string[] resources = [resource, ProjectResource];
        context.Revisions.EnsureCanAdvance(resources);
        if (!_app.SaveScene())
        {
            throw StateUnavailable(_app.LastProjectError ?? "保存 Scene 失败。");
        }

        AutomationRevisionSnapshot revision = context.Revisions.Advance(resources);
        return Result(
            CaptureScene(session),
            AutomationJsonContext.Default.AutomationSceneSnapshot,
            resources,
            revision);
    }

    private AutomationOperationResult SaveSceneAs(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationSceneSaveAsRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationSceneSaveAsRequest,
            AutomationProtocolConstants.SceneSaveAsMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.SceneSaveAsMethod);
        EditorProjectSession session = RequireEditSession();
        string oldSceneResource = EditorAutomationRuntime.CreateSceneResourceId(session);
        string normalized = session.Project.ResolveSceneRelativePath(request.Path);
        string newSceneResource =
            $"scene:{EditorAutomationRuntime.StableProjectId(session.Project.ProjectRoot)}:" +
            EditorAutomationRuntime.StableSceneId(session.Project.ProjectRoot, normalized);
        string[] resources = [oldSceneResource, newSceneResource, ProjectResource, WorkspaceResource];
        context.Revisions.EnsureCanAdvance(resources);
        try
        {
            session.SaveSceneAs(normalized, request.MakeStartScene);
        }
        catch (Exception exception) when (IsRecoverableSceneFailure(exception))
        {
            throw StateUnavailable($"另存 Scene 失败：{exception.Message}");
        }

        AutomationRevisionSnapshot revision = context.Revisions.Capture(resources);
        return Result(
            CaptureScene(session),
            AutomationJsonContext.Default.AutomationSceneSnapshot,
            resources,
            revision,
            stateEventAlreadyPublished: true);
    }

    private AutomationOperationResult NewScene(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.SceneNewMethod);
        _ = RequireEditSession();
        string[] before = CurrentWorkspaceResources(includeTransition: true);
        context.Revisions.EnsureCanAdvance(before);
        if (!_app.NewScene())
        {
            throw StateUnavailable(_app.LastProjectError ?? "新建 Scene 请求未被接受。");
        }

        bool executed = _app.PendingTransition is null;
        string[] resources = [.. CurrentWorkspaceResources(includeTransition: true)
            .Concat(before)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
        AutomationRevisionSnapshot revision = executed
            ? context.Revisions.Capture(resources)
            : AdvanceAndCapture(context.Revisions, [TransitionResource], resources);
        return Result(
            CaptureTransition(),
            AutomationJsonContext.Default.AutomationTransitionResult,
            resources,
            revision,
            stateEventAlreadyPublished: executed);
    }

    private AutomationOperationResult OpenScene(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationScenePathRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationScenePathRequest,
            AutomationProtocolConstants.SceneOpenMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.SceneOpenMethod);
        EditorProjectSession session = RequireEditSession();
        string normalized = session.Project.ResolveSceneRelativePath(request.Path);
        _ = EditorProjectSession.LoadSceneModel(session.Project, normalized);
        string[] before = CurrentWorkspaceResources(includeTransition: true);
        context.Revisions.EnsureCanAdvance(before);
        if (!_app.OpenScene(normalized))
        {
            throw StateUnavailable(_app.LastProjectError ?? "打开 Scene 请求未被接受。");
        }

        bool executed = _app.PendingTransition is null;
        string[] resources = [.. CurrentWorkspaceResources(includeTransition: true)
            .Concat(before)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
        AutomationRevisionSnapshot revision = executed
            ? context.Revisions.Capture(resources)
            : AdvanceAndCapture(context.Revisions, [TransitionResource], resources);
        return Result(
            CaptureTransition(),
            AutomationJsonContext.Default.AutomationTransitionResult,
            resources,
            revision,
            stateEventAlreadyPublished: executed);
    }

    private AutomationOperationResult ListHierarchy(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        EditorProjectSession session = RequireSession();
        AutomationPageRequest request = DeserializePage(
            payload,
            AutomationProtocolConstants.HierarchyListMethod);
        AutomationHierarchyItem[] items =
        [
            .. session.SceneModel.EnumerateDepthFirst()
                .Select(gameObject => CaptureHierarchyItem(session, gameObject)),
        ];
        string fingerprint = Fingerprint(
            items,
            AutomationJsonContext.Default.AutomationHierarchyItemArray);
        AutomationHierarchyItem[] filtered = FilterHierarchy(items, request.Filter);
        SortHierarchy(filtered, request.Sort);
        PageSlice<AutomationHierarchyItem> page = SlicePage(
            "hierarchy",
            fingerprint,
            request,
            filtered);
        AutomationHierarchyListResponse response = new()
        {
            Items = page.Items,
            Page = page.Info,
        };
        return Result(
            response,
            AutomationJsonContext.Default.AutomationHierarchyListResponse,
            [EditorAutomationRuntime.CreateSceneResourceId(session)]);
    }

    private AutomationOperationResult GetGameObject(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        AutomationGameObjectRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationGameObjectRequest,
            context.Request.RequestId);
        ValidateSchema(request.SchemaVersion, context.Request.RequestId);
        EditorProjectSession session = RequireSession();
        EditorGameObject gameObject = RequireGameObject(session.SceneModel, request.StableId);
        string resource = GameObjectResource(session, gameObject.StableId);
        return Result(
            CaptureGameObject(session, gameObject),
            AutomationJsonContext.Default.AutomationGameObjectSnapshot,
            [EditorAutomationRuntime.CreateSceneResourceId(session), resource]);
    }

    private AutomationOperationResult GetInspector(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        AutomationInspectorGetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationInspectorGetRequest,
            AutomationProtocolConstants.InspectorGetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.InspectorGetMethod);
        EditorProjectSession session = RequireSession();
        int stableId = request.StableId ?? session.SceneModel.SelectedStableId ??
            throw StateUnavailable(
                "Inspector 没有选中 GameObject；请提供 stableId 或先设置 selection。");
        EditorGameObject gameObject = RequireGameObject(session.SceneModel, stableId);
        return Result(
            CaptureGameObject(session, gameObject),
            AutomationJsonContext.Default.AutomationGameObjectSnapshot,
            [
                EditorAutomationRuntime.CreateSceneResourceId(session),
                GameObjectResource(session, stableId),
            ]);
    }

    private AutomationOperationResult GetSelection(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        EnsureEmpty(payload, AutomationProtocolConstants.HierarchySelectionGetMethod);
        EditorProjectSession session = RequireSession();
        AutomationSelectionSnapshot snapshot = CaptureSelection(session);
        return Result(
            snapshot,
            AutomationJsonContext.Default.AutomationSelectionSnapshot,
            [SelectionResource(session)]);
    }

    private AutomationOperationResult SetSelection(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationSelectionSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationSelectionSetRequest,
            AutomationProtocolConstants.HierarchySelectionSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.HierarchySelectionSetMethod);
        EditorProjectSession session = RequireSession();
        if (request.StableId is { } stableId)
        {
            _ = RequireGameObject(session.SceneModel, stableId);
        }

        int? before = session.SceneModel.SelectedStableId;
        string[] resources =
        [
            SelectionResource(session),
            .. new[] { before, request.StableId }
                .Where(static stableId => stableId.HasValue)
                .Select(stableId => GameObjectResource(session, stableId!.Value))
                .Distinct(StringComparer.Ordinal),
        ];
        if (before == request.StableId)
        {
            return Result(
                CaptureSelection(session),
                AutomationJsonContext.Default.AutomationSelectionSnapshot,
                resources);
        }

        context.Revisions.EnsureCanAdvance(resources);
        session.SetAutomationGameObjectSelection(request.StableId);
        AutomationRevisionSnapshot revision = context.Revisions.Advance(resources);
        return Result(
            CaptureSelection(session),
            AutomationJsonContext.Default.AutomationSelectionSnapshot,
            resources,
            revision);
    }

    private AutomationOperationResult CreateGameObject(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationGameObjectCreateRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationGameObjectCreateRequest,
            AutomationProtocolConstants.GameObjectCreateMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.GameObjectCreateMethod);
        EditorProjectSession session = RequireEditSession();
        string name = ValidateDisplayName(request.Name, "name");
        ValidateParentAndIndex(
            session.SceneModel,
            request.ParentStableId,
            request.SiblingIndex,
            allowEnd: true);
        session.FlushPendingAuthoringEdits();
        CreateGameObjectCommand command = new(name, request.ParentStableId, request.SiblingIndex);
        return ExecuteWrite(
            session,
            command,
            () =>
            {
                int stableId = command.CreatedStableId
                    ?? throw new InvalidOperationException("CreateGameObject 未返回新 stable ID。");
                string[] resources =
                [
                    EditorAutomationRuntime.CreateSceneResourceId(session),
                    GameObjectResource(session, stableId),
                    SelectionResource(session),
                ];
                return (
                    CaptureGameObject(session, session.SceneModel.Get(stableId)),
                    resources);
            },
            AutomationJsonContext.Default.AutomationGameObjectSnapshot);
    }

    private AutomationOperationResult DeleteGameObject(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationGameObjectRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationGameObjectRequest,
            AutomationProtocolConstants.GameObjectDeleteMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.GameObjectDeleteMethod);
        EditorProjectSession session = RequireEditSession();
        _ = RequireGameObject(session.SceneModel, request.StableId);
        session.FlushPendingAuthoringEdits();
        DeleteGameObjectCommand command = new(request.StableId);
        return ExecuteWrite(
            session,
            command,
            () =>
            {
                string[] resources =
                [
                    EditorAutomationRuntime.CreateSceneResourceId(session),
                    GameObjectResource(session, request.StableId),
                    SelectionResource(session),
                ];
                return (
                    new AutomationCommandResult
                    {
                        Succeeded = true,
                        Diagnostic = $"GameObject {request.StableId} subtree 已删除。",
                        ResourceIds = resources,
                    },
                    resources);
            },
            AutomationJsonContext.Default.AutomationCommandResult);
    }

    private AutomationOperationResult DuplicateGameObject(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationGameObjectRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationGameObjectRequest,
            AutomationProtocolConstants.GameObjectDuplicateMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.GameObjectDuplicateMethod);
        EditorProjectSession session = RequireEditSession();
        _ = RequireGameObject(session.SceneModel, request.StableId);
        session.FlushPendingAuthoringEdits();
        DuplicateGameObjectCommand command = new(request.StableId);
        return ExecuteWrite(
            session,
            command,
            () =>
            {
                int duplicateId = command.DuplicateStableId
                    ?? throw new InvalidOperationException("DuplicateGameObject 未返回新 stable ID。");
                string[] resources =
                [
                    EditorAutomationRuntime.CreateSceneResourceId(session),
                    GameObjectResource(session, request.StableId),
                    GameObjectResource(session, duplicateId),
                    SelectionResource(session),
                ];
                return (
                    CaptureGameObject(session, session.SceneModel.Get(duplicateId)),
                    resources);
            },
            AutomationJsonContext.Default.AutomationGameObjectSnapshot);
    }

    private AutomationOperationResult RenameGameObject(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationGameObjectRenameRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationGameObjectRenameRequest,
            AutomationProtocolConstants.GameObjectRenameMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.GameObjectRenameMethod);
        EditorProjectSession session = RequireEditSession();
        EditorGameObject gameObject = RequireGameObject(session.SceneModel, request.StableId);
        string name = ValidateDisplayName(request.Name, "name");
        session.FlushPendingAuthoringEdits();
        string[] resources = SceneAndGameObjectResources(session, request.StableId);
        if (string.Equals(gameObject.Name, name, StringComparison.Ordinal))
        {
            return NoChange(
                CaptureGameObject(session, gameObject),
                AutomationJsonContext.Default.AutomationGameObjectSnapshot,
                resources);
        }

        RenameGameObjectCommand command = new(request.StableId, name);
        return ExecuteWrite(
            session,
            command,
            () => (
                CaptureGameObject(session, gameObject),
                resources),
            AutomationJsonContext.Default.AutomationGameObjectSnapshot);
    }

    private AutomationOperationResult SetGameObjectEnabled(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        return SetGameObjectBoolean(
            payload,
            AutomationProtocolConstants.GameObjectSetEnabledMethod,
            static gameObject => gameObject.Enabled,
            static request => new SetGameObjectEnabledCommand(request.StableId, request.Value));
    }

    private AutomationOperationResult ReparentGameObject(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationGameObjectReparentRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationGameObjectReparentRequest,
            AutomationProtocolConstants.GameObjectReparentMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.GameObjectReparentMethod);
        EditorProjectSession session = RequireEditSession();
        EditorGameObject gameObject = RequireGameObject(session.SceneModel, request.StableId);
        ValidateParentAndIndex(
            session.SceneModel,
            request.ParentStableId,
            request.SiblingIndex,
            allowEnd: true);
        EnsureAcyclicReparent(
            session.SceneModel,
            request.StableId,
            request.ParentStableId);

        session.FlushPendingAuthoringEdits();
        int currentIndex = session.SceneModel.IndexInParent(request.StableId);
        int currentSiblingCount = gameObject.ParentId is { } currentParentId
            ? session.SceneModel.Get(currentParentId).Children.Count
            : session.SceneModel.RootIds.Count;
        int targetIndex = request.SiblingIndex.HasValue
            ? Math.Clamp(request.SiblingIndex.Value, 0, currentSiblingCount - 1)
            : currentSiblingCount - 1;
        List<int> affectedStableIds = [request.StableId];
        if (gameObject.ParentId is { } oldParentId)
        {
            affectedStableIds.Add(oldParentId);
        }

        if (request.ParentStableId is { } newParentId)
        {
            affectedStableIds.Add(newParentId);
        }

        string[] resources =
        [
            .. SceneAndGameObjectResources(
                session,
                [.. affectedStableIds.Distinct()]),
            SelectionResource(session),
        ];
        if (gameObject.ParentId == request.ParentStableId && currentIndex == targetIndex)
        {
            return NoChange(
                CaptureGameObject(session, gameObject),
                AutomationJsonContext.Default.AutomationGameObjectSnapshot,
                resources);
        }

        ReparentGameObjectCommand command =
            new(request.StableId, request.ParentStableId, request.SiblingIndex);
        try
        {
            return ExecuteWrite(
                session,
                command,
                () => (
                    CaptureGameObject(session, gameObject),
                    resources),
                AutomationJsonContext.Default.AutomationGameObjectSnapshot);
        }
        catch (InvalidOperationException exception)
        {
            throw Invalid(exception.Message);
        }
    }

    private AutomationOperationResult SetGameObjectSceneVisible(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        return SetGameObjectBoolean(
            payload,
            AutomationProtocolConstants.GameObjectSetSceneVisibleMethod,
            static gameObject => gameObject.SceneVisible,
            static request => new SetSceneVisibilityCommand(request.StableId, request.Value));
    }

    private AutomationOperationResult SetGameObjectScenePickable(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        return SetGameObjectBoolean(
            payload,
            AutomationProtocolConstants.GameObjectSetScenePickableMethod,
            static gameObject => gameObject.ScenePickable,
            static request => new SetScenePickabilityCommand(request.StableId, request.Value));
    }

    private AutomationOperationResult SetGameObjectBoolean(
        JsonElement? payload,
        string method,
        Func<EditorGameObject, bool> getValue,
        Func<AutomationGameObjectBoolRequest, IEditorCommand> createCommand)
    {
        AutomationGameObjectBoolRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationGameObjectBoolRequest,
            method);
        ValidateSchema(request.SchemaVersion, method);
        EditorProjectSession session = RequireEditSession();
        EditorGameObject gameObject = RequireGameObject(session.SceneModel, request.StableId);
        session.FlushPendingAuthoringEdits();
        string[] resources = SceneAndGameObjectResources(session, request.StableId);
        if (getValue(gameObject) == request.Value)
        {
            return NoChange(
                CaptureGameObject(session, gameObject),
                AutomationJsonContext.Default.AutomationGameObjectSnapshot,
                resources);
        }

        IEditorCommand command = createCommand(request);
        return ExecuteWrite(
            session,
            command,
            () => (
                CaptureGameObject(session, gameObject),
                resources),
            AutomationJsonContext.Default.AutomationGameObjectSnapshot);
    }

    private AutomationOperationResult SetAllSceneVisible(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        return SetAllSceneBoolean(
            payload,
            AutomationProtocolConstants.HierarchySetAllSceneVisibleMethod,
            static gameObject => gameObject.SceneVisible,
            static value => new SetAllSceneVisibilityCommand(value),
            "Scene visibility");
    }

    private AutomationOperationResult SetAllScenePickable(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        return SetAllSceneBoolean(
            payload,
            AutomationProtocolConstants.HierarchySetAllScenePickableMethod,
            static gameObject => gameObject.ScenePickable,
            static value => new SetAllScenePickabilityCommand(value),
            "Scene pickability");
    }

    private AutomationOperationResult SetAllSceneBoolean(
        JsonElement? payload,
        string method,
        Func<EditorGameObject, bool> getValue,
        Func<bool, IEditorCommand> createCommand,
        string displayName)
    {
        AutomationBoolValueRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationBoolValueRequest,
            method);
        ValidateSchema(request.SchemaVersion, method);
        EditorProjectSession session = RequireEditSession();
        session.FlushPendingAuthoringEdits();
        string[] resources = [EditorAutomationRuntime.CreateSceneResourceId(session)];
        if (session.SceneModel.EnumerateDepthFirst().All(gameObject => getValue(gameObject) == request.Value))
        {
            return NoChange(
                new AutomationCommandResult
                {
                    Succeeded = true,
                    Diagnostic = $"{displayName} 已经为 {request.Value}。",
                    ResourceIds = resources,
                },
                AutomationJsonContext.Default.AutomationCommandResult,
                resources);
        }

        IEditorCommand command = createCommand(request.Value);
        return ExecuteWrite(
            session,
            command,
            () =>
            {
                return (
                    new AutomationCommandResult
                    {
                        Succeeded = true,
                        Diagnostic = $"{displayName} 已批量设为 {request.Value}。",
                        ResourceIds = resources,
                    },
                    resources);
            },
            AutomationJsonContext.Default.AutomationCommandResult);
    }

    private AutomationOperationResult CreatePrefab(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationPrefabCreateRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationPrefabCreateRequest,
            AutomationProtocolConstants.PrefabCreateMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.PrefabCreateMethod);
        EditorProjectSession session = RequireEditSession();
        EditorGameObject gameObject = RequireGameObject(session.SceneModel, request.StableId);
        string assetPath = request.AssetPath is null
            ? session.Prefabs.AllocatePrefabPath(gameObject.Name)
            : NormalizePrefabPath(session, request.AssetPath, requireExisting: false);
        assetPath = NormalizePrefabPath(session, assetPath, requireExisting: false);
        session.FlushPendingAuthoringEdits();
        CreatePrefabAssetCommand command = new(
            session.Prefabs,
            request.StableId,
            assetPath);
        return ExecuteWrite(
            session,
            command,
            () =>
            {
                AutomationPrefabCreateResult result = new()
                {
                    AssetPath = assetPath,
                    GameObject = CaptureGameObject(session, gameObject),
                };
                return (
                    result,
                    [
                        .. SceneAndGameObjectResources(session, request.StableId),
                        AssetResource(session, assetPath),
                    ]);
            },
            AutomationJsonContext.Default.AutomationPrefabCreateResult);
    }

    private AutomationOperationResult InstantiatePrefab(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationPrefabInstantiateRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationPrefabInstantiateRequest,
            AutomationProtocolConstants.PrefabInstantiateMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.PrefabInstantiateMethod);
        EditorProjectSession session = RequireEditSession();
        string assetPath = NormalizePrefabPath(session, request.AssetPath, requireExisting: true);
        if (request.ParentStableId is { } parentStableId)
        {
            _ = RequireGameObject(session.SceneModel, parentStableId);
        }

        try
        {
            _ = session.Prefabs.LoadPrefabModel(assetPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException or JsonException)
        {
            throw Invalid($"Prefab '{assetPath}' 无法实例化：{exception.Message}");
        }

        EditorSceneTransform? transform = request.Transform is null
            ? null
            : ToEditorTransform(request.Transform);
        session.FlushPendingAuthoringEdits();
        InstantiatePrefabCommand command = new(
            session.Prefabs,
            assetPath,
            request.ParentStableId,
            transform);
        return ExecuteWrite(
            session,
            command,
            () =>
            {
                int stableId = command.CreatedStableId ??
                    throw new InvalidOperationException("InstantiatePrefab 未返回新 stable ID。");
                return (
                    CaptureGameObject(session, session.SceneModel.Get(stableId)),
                    [
                        EditorAutomationRuntime.CreateSceneResourceId(session),
                        GameObjectResource(session, stableId),
                        SelectionResource(session),
                        AssetResource(session, assetPath),
                    ]);
            },
            AutomationJsonContext.Default.AutomationGameObjectSnapshot);
    }

    private AutomationOperationResult SetTransform(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationTransformSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationTransformSetRequest,
            AutomationProtocolConstants.InspectorTransformSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.InspectorTransformSetMethod);
        EditorProjectSession session = RequireEditSession();
        EditorGameObject gameObject = RequireGameObject(session.SceneModel, request.StableId);
        EditorSceneTransform transform = ToEditorTransform(request.Transform);
        session.FlushPendingAuthoringEdits();
        string[] resources = SceneAndGameObjectResources(session, request.StableId);
        if (TransformContentEquals(gameObject.Transform, transform))
        {
            return NoChange(
                CaptureGameObject(session, gameObject),
                AutomationJsonContext.Default.AutomationGameObjectSnapshot,
                resources);
        }

        SetTransformCommand command = new(request.StableId, transform);
        return ExecuteWrite(
            session,
            command,
            () => (
                CaptureGameObject(session, gameObject),
                resources),
            AutomationJsonContext.Default.AutomationGameObjectSnapshot);
    }

    private AutomationOperationResult AddComponent(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationComponentAddRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationComponentAddRequest,
            AutomationProtocolConstants.InspectorComponentAddMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.InspectorComponentAddMethod);
        EditorProjectSession session = RequireEditSession();
        EditorGameObject gameObject = RequireGameObject(session.SceneModel, request.StableId);
        string typeName = ValidateIdentifier(request.TypeName, "typeName", 1024, allowDotsAndNestedType: true);
        _ = RequireBehaviour(session, typeName);
        if (request.Index is < 0 || request.Index > gameObject.Components.Count)
        {
            throw Invalid($"component index 必须在 0..{gameObject.Components.Count}。");
        }

        session.FlushPendingAuthoringEdits();
        AddComponentCommand command =
            new(request.StableId, new EditorComponentModel(typeName), request.Index);
        return ExecuteWrite(
            session,
            command,
            () => (
                CaptureGameObject(session, gameObject),
                SceneAndGameObjectResources(session, request.StableId)),
            AutomationJsonContext.Default.AutomationGameObjectSnapshot);
    }

    private AutomationOperationResult RemoveComponent(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationComponentRemoveRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationComponentRemoveRequest,
            AutomationProtocolConstants.InspectorComponentRemoveMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.InspectorComponentRemoveMethod);
        EditorProjectSession session = RequireEditSession();
        EditorGameObject gameObject = RequireGameObject(session.SceneModel, request.StableId);
        ValidateComponentIndex(gameObject, request.Index, "index");
        session.FlushPendingAuthoringEdits();
        RemoveComponentCommand command = new(request.StableId, request.Index);
        return ExecuteWrite(
            session,
            command,
            () => (
                CaptureGameObject(session, gameObject),
                SceneAndGameObjectResources(session, request.StableId)),
            AutomationJsonContext.Default.AutomationGameObjectSnapshot);
    }

    private AutomationOperationResult MoveComponent(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationComponentMoveRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationComponentMoveRequest,
            AutomationProtocolConstants.InspectorComponentMoveMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.InspectorComponentMoveMethod);
        EditorProjectSession session = RequireEditSession();
        EditorGameObject gameObject = RequireGameObject(session.SceneModel, request.StableId);
        ValidateComponentIndex(gameObject, request.FromIndex, "fromIndex");
        ValidateComponentIndex(gameObject, request.ToIndex, "toIndex");
        session.FlushPendingAuthoringEdits();
        string[] resources = SceneAndGameObjectResources(session, request.StableId);
        if (request.FromIndex == request.ToIndex)
        {
            return NoChange(
                CaptureGameObject(session, gameObject),
                AutomationJsonContext.Default.AutomationGameObjectSnapshot,
                resources);
        }

        MoveComponentCommand command =
            new(request.StableId, request.FromIndex, request.ToIndex);
        return ExecuteWrite(
            session,
            command,
            () => (
                CaptureGameObject(session, gameObject),
                resources),
            AutomationJsonContext.Default.AutomationGameObjectSnapshot);
    }

    private AutomationOperationResult SetComponentEnabled(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationComponentEnabledSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationComponentEnabledSetRequest,
            AutomationProtocolConstants.InspectorComponentSetEnabledMethod);
        ValidateSchema(
            request.SchemaVersion,
            AutomationProtocolConstants.InspectorComponentSetEnabledMethod);
        EditorProjectSession session = RequireEditSession();
        EditorGameObject gameObject = RequireGameObject(session.SceneModel, request.StableId);
        ValidateComponentIndex(gameObject, request.Index, "index");
        session.FlushPendingAuthoringEdits();
        string normalizedEnabled = request.Enabled.ToString(CultureInfo.InvariantCulture);
        string[] resources = SceneAndGameObjectResources(session, request.StableId);
        if (gameObject.Components[request.Index].SerializedFields.TryGetValue(
            nameof(Behaviour.Enabled),
            out string? currentEnabled) &&
            string.Equals(currentEnabled, normalizedEnabled, StringComparison.Ordinal))
        {
            return NoChange(
                CaptureGameObject(session, gameObject),
                AutomationJsonContext.Default.AutomationGameObjectSnapshot,
                resources);
        }

        SetComponentFieldCommand command = new(
            request.StableId,
            request.Index,
            nameof(Behaviour.Enabled),
            normalizedEnabled);
        return ExecuteWrite(
            session,
            command,
            () => (
                CaptureGameObject(session, gameObject),
                resources),
            AutomationJsonContext.Default.AutomationGameObjectSnapshot);
    }

    private AutomationOperationResult SetComponentField(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationComponentFieldSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationComponentFieldSetRequest,
            AutomationProtocolConstants.InspectorComponentSetFieldMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.InspectorComponentSetFieldMethod);
        EditorProjectSession session = RequireEditSession();
        EditorGameObject gameObject = RequireGameObject(session.SceneModel, request.StableId);
        ValidateComponentIndex(gameObject, request.ComponentIndex, "componentIndex");
        string fieldName = ValidateIdentifier(
            request.FieldName,
            "fieldName",
            512,
            allowDotsAndNestedType: true);
        EditorComponentModel component = gameObject.Components[request.ComponentIndex];
        Behaviour behaviour = BindBehaviour(session, component);
        ScriptFieldDescriptor descriptor = RequireInspectorField(behaviour, fieldName);
        if (!descriptor.CanWrite || descriptor.Kind == ScriptFieldKind.Unsupported)
        {
            throw Invalid($"字段 '{fieldName}' 不可写或不受 Inspector 支持。");
        }

        string? normalizedValue = null;
        if (!request.RemoveOverride)
        {
            if (request.Value is null)
            {
                throw Invalid("removeOverride=false 时 value 不得为 null。");
            }

            Dictionary<string, string> proposed =
                new(component.SerializedFields, StringComparer.Ordinal)
                {
                    [fieldName] = request.Value,
                };
            Behaviour proposedBehaviour = RequireBehaviour(session, component.TypeName);
            try
            {
                SerializedFieldBinder.Bind(proposedBehaviour, proposed);
            }
            catch (Exception exception) when (
                exception is FormatException or OverflowException or
                ArgumentException or InvalidOperationException or NotSupportedException)
            {
                throw Invalid($"字段 '{fieldName}' 值无效：{exception.Message}");
            }

            ScriptFieldDescriptor proposedField = RequireInspectorField(proposedBehaviour, fieldName);
            ValidateRange(proposedField);
            normalizedValue = EncodeFieldValue(proposedField);
            if (proposedField.Kind == ScriptFieldKind.AssetReference)
            {
                normalizedValue = NormalizeAssetReferenceValue(
                    proposedField.Name,
                    proposedField.AssetKind,
                    normalizedValue);
            }
        }

        session.FlushPendingAuthoringEdits();
        string? desiredValue = request.RemoveOverride ? null : normalizedValue;
        bool hasCurrentValue = component.SerializedFields.TryGetValue(fieldName, out string? currentValue);
        bool noChange = desiredValue is null
            ? !hasCurrentValue
            : hasCurrentValue && string.Equals(currentValue, desiredValue, StringComparison.Ordinal);
        string[] resources = SceneAndGameObjectResources(session, request.StableId);
        if (noChange)
        {
            return NoChange(
                CaptureGameObject(session, gameObject),
                AutomationJsonContext.Default.AutomationGameObjectSnapshot,
                resources);
        }

        SetComponentFieldCommand command = new(
            request.StableId,
            request.ComponentIndex,
            fieldName,
            desiredValue);
        return ExecuteWrite(
            session,
            command,
            () => (
                CaptureGameObject(session, gameObject),
                resources),
            AutomationJsonContext.Default.AutomationGameObjectSnapshot);
    }

    private AutomationOperationResult SetBuiltInCanvas(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationBuiltInCanvasSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationBuiltInCanvasSetRequest,
            AutomationProtocolConstants.InspectorCanvasSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.InspectorCanvasSetMethod);
        EditorProjectSession session = RequireEditSession();
        EditorGameObject gameObject = RequireGameObject(session.SceneModel, request.StableId);
        EditorWebCanvasComponent? webCanvas = request.WebCanvas is null
            ? null
            : new EditorWebCanvasComponent
            {
                ManifestAssetId = NormalizeOptionalText(
                    request.WebCanvas.ManifestAssetId,
                    "webCanvas.manifestAssetId",
                    512),
                ManifestPath = NormalizeOptionalText(
                    request.WebCanvas.ManifestPath,
                    "webCanvas.manifestPath",
                    32767),
                InitialScreenId = NormalizeOptionalText(
                    request.WebCanvas.InitialScreenId,
                    "webCanvas.initialScreenId",
                    256),
                Enabled = request.WebCanvas.Enabled,
                SortingOrder = request.WebCanvas.SortingOrder,
                Primary = gameObject.WebCanvas?.Primary ?? false,
            };
        EditorCanvasScalerComponent? canvasScaler = request.CanvasScaler is null
            ? null
            : new EditorCanvasScalerComponent
            {
                Settings = ToCanvasScalerSettings(request.CanvasScaler),
            };

        session.FlushPendingAuthoringEdits();
        string[] resources = SceneAndGameObjectResources(session, request.StableId);
        if (EditorWebCanvasComponent.ContentEquals(gameObject.WebCanvas, webCanvas) &&
            EditorCanvasScalerComponent.ContentEquals(gameObject.CanvasScaler, canvasScaler))
        {
            return NoChange(
                CaptureGameObject(session, gameObject),
                AutomationJsonContext.Default.AutomationGameObjectSnapshot,
                resources);
        }

        SetBuiltInCanvasComponentsCommand command = new(
            request.StableId,
            webCanvas,
            canvasScaler);
        return ExecuteWrite(
            session,
            command,
            () => (
                CaptureGameObject(session, gameObject),
                resources),
            AutomationJsonContext.Default.AutomationGameObjectSnapshot);
    }

    private AutomationOperationResult SetCanvasPrimary(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationCanvasPrimarySetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationCanvasPrimarySetRequest,
            AutomationProtocolConstants.InspectorCanvasSetPrimaryMethod);
        ValidateSchema(
            request.SchemaVersion,
            AutomationProtocolConstants.InspectorCanvasSetPrimaryMethod);
        EditorProjectSession session = RequireEditSession();
        EditorGameObject gameObject = RequireGameObject(session.SceneModel, request.StableId);
        if (gameObject.WebCanvas is null)
        {
            throw Invalid($"GameObject {request.StableId} 没有 Canvas (Web)。");
        }

        session.FlushPendingAuthoringEdits();
        string[] resources = request.Primary
            ? CanvasResources(session)
            : SceneAndGameObjectResources(session, request.StableId);
        bool primaryAlreadyApplied = request.Primary
            ? session.SceneModel.EnumerateDepthFirst().All(candidate =>
                candidate.WebCanvas is null ||
                candidate.WebCanvas.Primary == (candidate.StableId == request.StableId))
            : !gameObject.WebCanvas.Primary;
        if (primaryAlreadyApplied)
        {
            return NoChange(
                CaptureGameObject(session, gameObject),
                AutomationJsonContext.Default.AutomationGameObjectSnapshot,
                resources);
        }

        IEditorCommand command;
        if (request.Primary)
        {
            command = new SetPrimaryWebCanvasCommand(request.StableId);
        }
        else
        {
            EditorWebCanvasComponent webCanvas = gameObject.WebCanvas.Clone();
            webCanvas.Primary = false;
            command = new SetBuiltInCanvasComponentsCommand(
                request.StableId,
                webCanvas,
                gameObject.CanvasScaler);
        }

        return ExecuteWrite(
            session,
            command,
            () => (
                CaptureGameObject(session, gameObject),
                resources),
            AutomationJsonContext.Default.AutomationGameObjectSnapshot);
    }

    private AutomationOperationResult RevertPrefabOverrides(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationGameObjectRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationGameObjectRequest,
            AutomationProtocolConstants.PrefabRevertOverridesMethod);
        ValidateSchema(
            request.SchemaVersion,
            AutomationProtocolConstants.PrefabRevertOverridesMethod);
        EditorProjectSession session = RequireEditSession();
        EditorGameObject gameObject = RequireGameObject(session.SceneModel, request.StableId);
        if (gameObject.PrefabLink is null)
        {
            throw Invalid($"GameObject {request.StableId} 不是 Prefab instance。");
        }

        if (gameObject.PrefabLink.Overrides.Count == 0)
        {
            throw StateUnavailable($"GameObject {request.StableId} 没有 Prefab overrides。");
        }

        session.FlushPendingAuthoringEdits();
        RevertPrefabOverridesCommand command = new(request.StableId);
        return ExecuteWrite(
            session,
            command,
            () => (
                CaptureGameObject(session, gameObject),
                SceneAndGameObjectResources(session, request.StableId)),
            AutomationJsonContext.Default.AutomationGameObjectSnapshot);
    }

    private AutomationOperationResult GetSceneTool(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        EnsureEmpty(payload, AutomationProtocolConstants.SceneToolGetMethod);
        EditorProjectSession session = RequireSession();
        return !session.TryCaptureAutomationSceneTool(out AutomationSceneToolSnapshot snapshot)
            ? throw StateUnavailable("Scene View 尚未注册。")
            : Result(
            snapshot,
            AutomationJsonContext.Default.AutomationSceneToolSnapshot,
            [SceneViewResource(session)]);
    }

    private AutomationOperationResult SetSceneTool(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationSceneToolSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationSceneToolSetRequest,
            AutomationProtocolConstants.SceneToolSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.SceneToolSetMethod);
        if ((request.Tool is { } tool && !Enum.IsDefined(tool)) ||
            (request.GizmoSpace is { } gizmoSpace && !Enum.IsDefined(gizmoSpace)))
        {
            throw Invalid("Scene tool 或 gizmo space 无效。");
        }

        EditorProjectSession session = RequireEditSession();
        string[] resources = [SceneViewResource(session), PanelsResource];
        if (!session.TryCaptureAutomationSceneTool(out AutomationSceneToolSnapshot before))
        {
            throw StateUnavailable("Scene View 尚未注册。");
        }

        if (!SceneToolRequestWouldChange(before, request))
        {
            return Result(
                before,
                AutomationJsonContext.Default.AutomationSceneToolSnapshot,
                resources);
        }

        context.Revisions.EnsureCanAdvance(resources);

        EditorPanelSnapshot[] panelsBefore = session.CaptureAutomationPanels();
        AutomationSceneToolSnapshot snapshot;
        try
        {
            if (!session.TrySetAutomationSceneTool(request, out string diagnostic))
            {
                throw StateUnavailable(diagnostic);
            }

            if (!session.TryCaptureAutomationSceneTool(out snapshot))
            {
                throw StateUnavailable("Scene View 状态无法读取。");
            }
        }
        catch (Exception exception)
        {
            AutomationSceneToolSetRequest restore = CreateSceneToolSetRequest(before);
            bool toolRestored = session.TrySetAutomationSceneTool(restore, out string restoreDiagnostic);
            bool panelsRestored = session.TryRestoreAutomationPanels(panelsBefore);
            if (!toolRestored || !panelsRestored)
            {
                throw new AggregateException(
                    "Scene tool 更新失败且无法完整恢复 before state。" +
                    (toolRestored ? string.Empty : $" Tool: {restoreDiagnostic}"),
                    exception);
            }

            throw;
        }

        if (snapshot == before)
        {
            return Result(
                snapshot,
                AutomationJsonContext.Default.AutomationSceneToolSnapshot,
                resources);
        }

        AutomationRevisionSnapshot revision = context.Revisions.Advance(resources);
        return Result(
            snapshot,
            AutomationJsonContext.Default.AutomationSceneToolSnapshot,
            resources,
            revision);
    }

    private AutomationOperationResult FrameScene(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationSceneFrameRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationSceneFrameRequest,
            AutomationProtocolConstants.SceneToolFrameMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.SceneToolFrameMethod);
        if (!Enum.IsDefined(request.Target))
        {
            throw Invalid("Scene frame target 无效。");
        }

        EditorProjectSession session = RequireEditSession();
        string[] resources = [SceneViewResource(session)];
        if (!session.TryCaptureAutomationSceneTool(out AutomationSceneToolSnapshot before))
        {
            throw StateUnavailable("Scene View 状态无法读取。");
        }

        context.Revisions.EnsureCanAdvance(resources);
        if (!session.TryFrameAutomationScene(request.Target, out string diagnostic))
        {
            throw StateUnavailable(diagnostic);
        }

        if (!session.TryCaptureAutomationSceneTool(out AutomationSceneToolSnapshot snapshot))
        {
            throw StateUnavailable("Scene View 状态无法读取。");
        }

        if (snapshot == before)
        {
            return Result(
                snapshot,
                AutomationJsonContext.Default.AutomationSceneToolSnapshot,
                resources);
        }

        AutomationRevisionSnapshot revision = context.Revisions.Advance(resources);
        return Result(
            snapshot,
            AutomationJsonContext.Default.AutomationSceneToolSnapshot,
            resources,
            revision);
    }

    private AutomationOperationResult ApplyBrush(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationBrushApplyRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationBrushApplyRequest,
            AutomationProtocolConstants.BrushApplyMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.BrushApplyMethod);
        EditorProjectSession session = RequireEditSession();
        string[] resources = [AuthoringWorldResource(session), SceneViewResource(session)];
        context.Revisions.EnsureCanAdvance(resources);
        if (!session.TryApplyAutomationBrush(
            request.X,
            request.Y,
            out AutomationBrushApplyResult result,
            out string diagnostic))
        {
            throw StateUnavailable(diagnostic);
        }

        if (result.WrittenCells == 0)
        {
            return Result(
                result,
                AutomationJsonContext.Default.AutomationBrushApplyResult,
                resources);
        }

        AutomationRevisionSnapshot revision = context.Revisions.Advance(resources);
        return Result(
            result,
            AutomationJsonContext.Default.AutomationBrushApplyResult,
            resources,
            revision);
    }

    private AutomationOperationResult ApplyBrushStroke(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationBrushStrokeRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationBrushStrokeRequest,
            AutomationProtocolConstants.BrushStrokeMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.BrushStrokeMethod);
        if (request.Points is null || request.Points.Length is < 1 or > 256 ||
            request.Points.Any(static point => point is null))
        {
            throw Invalid("brush stroke 必须提供 1..256 个非 null 控制点。");
        }

        long sampleCount = 1;
        for (int i = 1; i < request.Points.Length; i++)
        {
            long deltaX = Math.Abs((long)request.Points[i].X - request.Points[i - 1].X);
            long deltaY = Math.Abs((long)request.Points[i].Y - request.Points[i - 1].Y);
            sampleCount += Math.Max(deltaX, deltaY);
            if (sampleCount > 8192)
            {
                throw Invalid("brush stroke 栅格化后不得超过 8192 个采样点。");
            }
        }

        EditorProjectSession session = RequireEditSession();
        if (!session.TryCaptureAutomationSceneTool(out AutomationSceneToolSnapshot tool) ||
            tool.Brush is null)
        {
            throw StateUnavailable("当前工程没有可读取设置的世界画刷。");
        }

        long estimatedCellVisits = EstimateBrushStrokeCellVisits(sampleCount, tool.Brush.Radius);
        if (estimatedCellVisits > MaximumBrushStrokeCellVisits)
        {
            throw Invalid(
                $"brush stroke 预计访问 {estimatedCellVisits} 个 cell，超过单请求上限 " +
                $"{MaximumBrushStrokeCellVisits}；请拆分 stroke。");
        }

        context.CancellationToken.ThrowIfCancellationRequested();
        string[] resources = [AuthoringWorldResource(session), SceneViewResource(session)];
        context.Revisions.EnsureCanAdvance(resources);
        if (!session.TryApplyAutomationBrushStroke(
            request.Points,
            out AutomationBrushStrokeResult result,
            out string diagnostic))
        {
            throw StateUnavailable(diagnostic);
        }

        if (result.WrittenCells == 0)
        {
            return Result(
                result,
                AutomationJsonContext.Default.AutomationBrushStrokeResult,
                resources);
        }

        AutomationRevisionSnapshot revision = context.Revisions.Advance(resources);
        return Result(
            result,
            AutomationJsonContext.Default.AutomationBrushStrokeResult,
            resources,
            revision);
    }

    internal static long EstimateBrushStrokeCellVisits(long sampleCount, int radius)
    {
        if (sampleCount < 1 || radius is < 0 or > 128)
        {
            throw new ArgumentOutOfRangeException(
                sampleCount < 1 ? nameof(sampleCount) : nameof(radius));
        }

        long diameter = (radius * 2L) + 1L;
        return checked(sampleCount * diameter * diameter);
    }

    private AutomationWorkspaceSnapshot CaptureWorkspace()
    {
        EditorProjectSession? session = _app.CurrentSession;
        EditorTransitionPrompt? transition = _app.PendingTransition;
        return new AutomationWorkspaceSnapshot
        {
            ProjectOpen = session is not null,
            ProjectId = session is null
                ? null
                : EditorAutomationRuntime.StableProjectId(session.Project.ProjectRoot),
            ProjectName = session?.Project.Name,
            ProjectRoot = session?.Project.ProjectRoot,
            SceneId = session is null
                ? null
                : EditorAutomationRuntime.StableSceneId(
                    session.Project.ProjectRoot,
                    session.CurrentSceneRelativePath),
            ScenePath = session?.CurrentSceneRelativePath,
            SceneDirty = session?.SceneModel.IsDirty == true,
            Mode = CaptureMode(session),
            TransitionPending = transition.HasValue,
            TransitionKind = transition?.Kind.ToString(),
            TransitionTarget = transition?.Target,
        };
    }

    private AutomationTransitionResult CaptureTransition()
    {
        EditorTransitionPrompt? pending = _app.PendingTransition;
        return new AutomationTransitionResult
        {
            Status = pending.HasValue ? "confirmationRequired" : "none",
            Kind = pending?.Kind.ToString(),
            Target = pending?.Target,
            Diagnostic = pending.HasValue
                ? "当前场景有未保存修改，请选择 Save、Discard 或 Cancel。"
                : null,
        };
    }

    private static AutomationTransitionResult MapTransitionResult(EditorTransitionResult result)
    {
        return new AutomationTransitionResult
        {
            Status = result.Status switch
            {
                EditorTransitionStatus.Executed => "executed",
                EditorTransitionStatus.ConfirmationRequired => "confirmationRequired",
                EditorTransitionStatus.PendingTransitionExists => "pendingTransitionExists",
                EditorTransitionStatus.Cancelled => "cancelled",
                EditorTransitionStatus.SaveFailed => "saveFailed",
                EditorTransitionStatus.NoPendingTransition => "noPendingTransition",
                _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, null),
            },
            Diagnostic = result.Diagnostic,
        };
    }

    private static AutomationSceneSnapshot CaptureScene(EditorProjectSession session)
    {
        EditorSceneModel scene = session.SceneModel;
        return new AutomationSceneSnapshot
        {
            SceneId = EditorAutomationRuntime.StableSceneId(
                session.Project.ProjectRoot,
                session.CurrentSceneRelativePath),
            ResourceId = EditorAutomationRuntime.CreateSceneResourceId(session),
            Path = session.CurrentSceneRelativePath,
            Name = scene.Name,
            Dirty = scene.IsDirty,
            ContentVersion = scene.Version,
            SceneViewVersion = scene.SceneViewVersion,
            Generation = scene.SceneGeneration,
            GameObjectCount = scene.Count,
            RootStableIds = [.. scene.RootIds],
            SelectedStableId = scene.SelectedStableId,
        };
    }

    private static AutomationHierarchyItem CaptureHierarchyItem(
        EditorProjectSession session,
        EditorGameObject gameObject)
    {
        EditorSceneModel scene = session.SceneModel;
        List<int> path = [gameObject.StableId];
        int? parentId = gameObject.ParentId;
        while (parentId is { } current)
        {
            path.Add(current);
            parentId = scene.Get(current).ParentId;
        }

        path.Reverse();
        return new AutomationHierarchyItem
        {
            StableId = gameObject.StableId,
            ResourceId = GameObjectResource(session, gameObject.StableId),
            ParentStableId = gameObject.ParentId,
            SiblingIndex = scene.IndexInParent(gameObject.StableId),
            Depth = path.Count - 1,
            HierarchyPath = string.Join('/', path),
            Name = gameObject.Name,
            Enabled = gameObject.Enabled,
            SceneVisible = gameObject.SceneVisible,
            ScenePickable = gameObject.ScenePickable,
            Selected = scene.SelectedStableId == gameObject.StableId,
            ChildCount = gameObject.Children.Count,
            ComponentCount = gameObject.Components.Count,
            HasPrefabLink = gameObject.PrefabLink is not null,
            HasWebCanvas = gameObject.WebCanvas is not null,
            HasCanvasScaler = gameObject.CanvasScaler is not null,
        };
    }

    private static AutomationGameObjectSnapshot CaptureGameObject(
        EditorProjectSession session,
        EditorGameObject gameObject)
    {
        EditorSceneTransform world = session.SceneModel.ComputeWorldTransform(gameObject.StableId);
        AutomationComponentSnapshot[] components = new AutomationComponentSnapshot[gameObject.Components.Count];
        for (int i = 0; i < components.Length; i++)
        {
            components[i] = CaptureComponent(session, gameObject.Components[i], i);
        }

        return new AutomationGameObjectSnapshot
        {
            Hierarchy = CaptureHierarchyItem(session, gameObject),
            LocalTransform = ToAutomationTransform(gameObject.Transform),
            WorldTransform = ToAutomationTransform(world),
            PrefabAssetPath = gameObject.PrefabLink?.AssetPath,
            PrefabOverridePaths = gameObject.PrefabLink is null
                ? []
                :
                [
                    .. gameObject.PrefabLink.Overrides
                        .Select(static item => item.PropertyPath)
                        .Where(static path => !string.IsNullOrWhiteSpace(path))
                        .Select(static path => path!)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal),
                ],
            WebCanvas = gameObject.WebCanvas is null
                ? null
                : new AutomationWebCanvasSnapshot
                {
                    ManifestAssetId = gameObject.WebCanvas.ManifestAssetId,
                    ManifestPath = gameObject.WebCanvas.ManifestPath,
                    InitialScreenId = gameObject.WebCanvas.InitialScreenId,
                    Enabled = gameObject.WebCanvas.Enabled,
                    SortingOrder = gameObject.WebCanvas.SortingOrder,
                    Primary = gameObject.WebCanvas.Primary,
                },
            CanvasScaler = gameObject.CanvasScaler is null
                ? null
                : ToAutomationCanvasScaler(gameObject.CanvasScaler.Settings),
            Components = components,
        };
    }

    private static AutomationComponentSnapshot CaptureComponent(
        EditorProjectSession session,
        EditorComponentModel component,
        int index)
    {
        if (!TryCreateBehaviour(session, component.TypeName, out Behaviour? behaviour))
        {
            return new AutomationComponentSnapshot
            {
                Index = index,
                TypeName = component.TypeName,
                TypeAvailable = false,
                Enabled = false,
                Fields = [],
            };
        }

        try
        {
            SerializedFieldBinder.Bind(behaviour, component.SerializedFields);
        }
        catch (Exception exception) when (
            exception is FormatException or OverflowException or ArgumentException or
            InvalidOperationException or NotSupportedException)
        {
            throw StateUnavailable(
                $"Behaviour '{component.TypeName}' 的 SerializedFields 无法绑定：{exception.Message}");
        }

        ScriptFieldDescriptor[] fields = ScriptInspector.InspectFields(behaviour);
        AutomationInspectorField[] snapshots = new AutomationInspectorField[fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            ScriptFieldDescriptor field = fields[i];
            bool overridden = component.SerializedFields.TryGetValue(field.Name, out string? serialized);
            Type normalizedType = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
            snapshots[i] = new AutomationInspectorField
            {
                Name = field.Name,
                ValueType = field.FieldType.FullName ?? field.FieldType.Name,
                Kind = field.Kind.ToString(),
                CanWrite = field.CanWrite,
                Public = field.IsPublic,
                SerializedPrivate = field.IsSerializedPrivate,
                Nullable = !field.FieldType.IsValueType ||
                    Nullable.GetUnderlyingType(field.FieldType) is not null,
                RangeMinimum = field.RangeMinimum,
                RangeMaximum = field.RangeMaximum,
                EnumNames = normalizedType.IsEnum ? Enum.GetNames(normalizedType) : [],
                AssetKind = field.AssetKind?.ToString(),
                Value = overridden ? serialized! : EncodeFieldValue(field),
                Overridden = overridden,
            };
        }

        return new AutomationComponentSnapshot
        {
            Index = index,
            TypeName = component.TypeName,
            TypeAvailable = true,
            Enabled = GameObjectInspectorPanel.IsComponentEnabled(component),
            Fields = snapshots,
        };
    }

    private static AutomationSelectionSnapshot CaptureSelection(EditorProjectSession session)
    {
        int? stableId = session.SceneModel.SelectedStableId;
        return new AutomationSelectionSnapshot
        {
            StableId = stableId,
            ResourceId = stableId is { } value ? GameObjectResource(session, value) : null,
        };
    }

    private static AutomationPanelInfo MapPanel(EditorPanelSnapshot panel)
    {
        return new AutomationPanelInfo
        {
            Id = panel.Id,
            Title = panel.Title,
            Visible = panel.Visible,
            Chrome = panel.Chrome,
            Maximized = panel.Maximized,
            FocusPending = panel.FocusPending,
            DockStateKnown = panel.DockStateKnown,
            Docked = panel.Docked,
            DockGroupId = panel.DockGroupId,
            X = panel.DockStateKnown ? panel.X : null,
            Y = panel.DockStateKnown ? panel.Y : null,
            Width = panel.DockStateKnown ? panel.Width : null,
            Height = panel.DockStateKnown ? panel.Height : null,
        };
    }

    private static AutomationDockLayoutSnapshot CaptureDockLayout(EditorProjectSession session)
    {
        string layout = CaptureDockLayoutText(session);
        byte[] bytes = Encoding.UTF8.GetBytes(layout);
        try
        {
            return new AutomationDockLayoutSnapshot
            {
                LayoutVersion = EditorShellLayout.CurrentLayoutVersion,
                Utf8ByteLength = bytes.Length,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                LayoutText = layout,
                Panels =
                [
                    .. session.CaptureAutomationPanels().Select(static panel =>
                        new AutomationPanelLayoutEntry
                        {
                            PanelId = panel.Id,
                            Visible = panel.Visible,
                        }),
                ],
            };
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    private static string CaptureDockLayoutText(EditorProjectSession session)
    {
        string raw = session.CaptureAutomationDockLayout();
        return EditorDockLayoutValidator.TryValidate(raw, out string normalized, out string diagnostic)
            ? normalized
            : throw StateUnavailable($"当前 ImGui dock layout 尚不可导出：{diagnostic}");
    }

    private static string ValidateDockLayout(AutomationDockLayoutSnapshot layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (layout.LayoutVersion != EditorShellLayout.CurrentLayoutVersion)
        {
            throw Invalid(
                $"layoutVersion 必须为 {EditorShellLayout.CurrentLayoutVersion}，实际为 {layout.LayoutVersion}。");
        }

        if (layout.LayoutText is null || layout.Sha256 is null)
        {
            throw Invalid("Dock layout layoutText/sha256 不得为 null。");
        }

        if (!EditorDockLayoutValidator.TryValidate(
            layout.LayoutText,
            out string normalized,
            out string diagnostic))
        {
            throw Invalid(diagnostic);
        }

        if (!string.Equals(layout.LayoutText, normalized, StringComparison.Ordinal))
        {
            throw Invalid("layoutText 必须使用 LF、单个结尾换行的 canonical 格式。");
        }

        byte[] bytes = Encoding.UTF8.GetBytes(normalized);
        byte[] expectedHash;
        try
        {
            expectedHash = Convert.FromHexString(layout.Sha256);
        }
        catch (FormatException)
        {
            Array.Clear(bytes);
            throw Invalid("Dock layout sha256 必须是 64 位十六进制。 ");
        }

        try
        {
            byte[] actualHash = SHA256.HashData(bytes);
            try
            {
                if (layout.Utf8ByteLength != bytes.Length ||
                    expectedHash.Length != actualHash.Length ||
                    !CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
                {
                    throw Invalid("Dock layout utf8ByteLength/SHA256 与 layoutText 不一致。");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(actualHash);
            }
        }
        finally
        {
            Array.Clear(bytes);
            CryptographicOperations.ZeroMemory(expectedHash);
        }

        return normalized;
    }

    private static EditorPanelSnapshot[] ResolveLayoutPanels(
        EditorPanelSnapshot[] current,
        AutomationPanelLayoutEntry[] entries)
    {
        if (entries is null || entries.Length != current.Length)
        {
            throw Invalid("Dock layout panels 必须与当前完整 panel registry 一一对应。");
        }

        Dictionary<string, bool> visibility = new(entries.Length, StringComparer.Ordinal);
        for (int i = 0; i < entries.Length; i++)
        {
            AutomationPanelLayoutEntry entry = entries[i]
                ?? throw Invalid("Dock layout panels 不得包含 null。");

            string panelId = ValidateIdentifier(entry.PanelId, $"panels[{i}].panelId", 128);
            if (!visibility.TryAdd(panelId, entry.Visible))
            {
                throw Invalid($"Dock layout panel ID '{panelId}' 重复。");
            }
        }

        EditorPanelSnapshot[] resolved = new EditorPanelSnapshot[current.Length];
        for (int i = 0; i < current.Length; i++)
        {
            if (!visibility.TryGetValue(current[i].Id, out bool visible))
            {
                throw Invalid($"Dock layout 缺少 panel '{current[i].Id}'。");
            }

            resolved[i] = current[i] with
            {
                Visible = visible,
                FocusPending = false,
            };
        }

        return resolved;
    }

    private static EditorDockWindowRequest ValidatePanelDockRequest(AutomationPanelDockRequest request)
    {
        if (!Enum.IsDefined(request.Placement))
        {
            throw Invalid("placement 是未知停靠枚举值。");
        }

        bool floating = request.Placement == AutomationPanelDockPlacement.Floating;
        if (floating == !string.IsNullOrWhiteSpace(request.TargetPanelId))
        {
            throw Invalid("Floating 不得提供 targetPanelId；其它 placement 必须提供 targetPanelId。");
        }

        if (!float.IsFinite(request.SplitRatio) || request.SplitRatio is < 0.05f or > 0.95f)
        {
            throw Invalid("splitRatio 必须在 0.05..0.95。 ");
        }

        if (request.X.HasValue != request.Y.HasValue || request.Width.HasValue != request.Height.HasValue)
        {
            throw Invalid("Floating X/Y 与 width/height 必须分别成对提供。");
        }

        if (!floating && (request.X.HasValue || request.Width.HasValue))
        {
            throw Invalid("只有 Floating placement 可提供 window rect。");
        }

        if (request.X is { } x)
        {
            _ = float.IsFinite(x) && float.IsFinite(request.Y!.Value) &&
                MathF.Abs(x) <= 1_000_000f && MathF.Abs(request.Y.Value) <= 1_000_000f
                    ? x
                    : throw Invalid("Floating 坐标必须是 -1000000..1000000 的有限数。");
        }

        if (request.Width is { } width)
        {
            _ = float.IsFinite(width) && float.IsFinite(request.Height!.Value) &&
                width is >= 100f and <= 32768f && request.Height.Value is >= 100f and <= 32768f
                    ? width
                    : throw Invalid("Floating 尺寸必须是 100..32768 的有限数。");
        }

        return new EditorDockWindowRequest
        {
            WindowTitle = "resolved-by-panel-registry",
            Placement = request.Placement switch
            {
                AutomationPanelDockPlacement.Tab => EditorDockPlacement.Tab,
                AutomationPanelDockPlacement.Left => EditorDockPlacement.Left,
                AutomationPanelDockPlacement.Right => EditorDockPlacement.Right,
                AutomationPanelDockPlacement.Top => EditorDockPlacement.Top,
                AutomationPanelDockPlacement.Bottom => EditorDockPlacement.Bottom,
                AutomationPanelDockPlacement.Floating => EditorDockPlacement.Floating,
                _ => throw Invalid("placement 是未知停靠枚举值。"),
            },
            SplitRatio = request.SplitRatio,
            X = request.X,
            Y = request.Y,
            Width = request.Width,
            Height = request.Height,
        };
    }

    private static void RestoreDockLayoutOrThrow(
        EditorProjectSession session,
        string beforeLayout,
        EditorPanelSnapshot[] beforePanels,
        Exception original)
    {
        try
        {
            session.ApplyAutomationDockLayout(beforeLayout);
            if (!session.TryRestoreAutomationPanels(beforePanels))
            {
                throw new InvalidOperationException("panel registry before state 不再匹配。");
            }
        }
        catch (Exception rollback)
        {
            throw new AggregateException("Dock layout 变更失败且无法恢复内存 before state。", original, rollback);
        }
    }

    private void RestoreResetLayoutOrThrow(
        EditorProjectSession session,
        string beforeLayout,
        EditorPanelSnapshot[] beforePanels,
        EditorLayoutPersistenceSnapshot beforePersistence,
        Exception original)
    {
        List<Exception> failures = [original];
        try
        {
            session.ApplyAutomationDockLayout(beforeLayout);
            if (!session.TryRestoreAutomationPanels(beforePanels))
            {
                throw new InvalidOperationException("panel registry before state 不再匹配。");
            }
        }
        catch (Exception rollback)
        {
            failures.Add(rollback);
        }

        try
        {
            if (!_app.TryRestoreAutomationLayoutPersistence(
                beforePersistence,
                out string diagnostic))
            {
                throw new InvalidOperationException(diagnostic);
            }
        }
        catch (Exception rollback)
        {
            failures.Add(rollback);
        }

        if (failures.Count > 1)
        {
            throw new AggregateException(
                "Reset Layout 失败，且至少一个内存或磁盘 before state 无法恢复。",
                failures);
        }
    }

    private static AutomationHistorySnapshot CaptureHistory(EditorProjectSession session)
    {
        EditorUndoStack history = session.UndoStack;
        return new AutomationHistorySnapshot
        {
            CanUndo = history.CanUndo,
            CanRedo = history.CanRedo,
            UndoCount = history.UndoCount,
            RedoCount = history.RedoCount,
            UndoName = history.UndoName,
            RedoName = history.RedoName,
        };
    }

    private static AutomationTransformValue ToAutomationTransform(EditorSceneTransform transform)
    {
        return new AutomationTransformValue
        {
            X = transform.X,
            Y = transform.Y,
            RotationRadians = transform.RotationRadians,
            ScaleX = transform.ScaleX,
            ScaleY = transform.ScaleY,
        };
    }

    private static EditorSceneTransform ToEditorTransform(AutomationTransformValue transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        return !float.IsFinite(transform.X) || !float.IsFinite(transform.Y) ||
            !float.IsFinite(transform.RotationRadians) ||
            !float.IsFinite(transform.ScaleX) || !float.IsFinite(transform.ScaleY) ||
            MathF.Abs(transform.X) > 1_000_000_000f ||
            MathF.Abs(transform.Y) > 1_000_000_000f ||
            MathF.Abs(transform.RotationRadians) > 1_000_000_000f ||
            MathF.Abs(transform.ScaleX) > 1_000_000f ||
            MathF.Abs(transform.ScaleY) > 1_000_000f
            ? throw Invalid("Transform 必须由有限且在公共安全范围内的数值组成。")
            : new EditorSceneTransform
            {
                X = transform.X,
                Y = transform.Y,
                RotationRadians = transform.RotationRadians,
                ScaleX = transform.ScaleX,
                ScaleY = transform.ScaleY,
            };
    }

    private static AutomationEditorMode CaptureMode(EditorProjectSession? session)
    {
        return session?.Engine.Mode switch
        {
            null or EngineExecutionMode.Edit => AutomationEditorMode.Edit,
            EngineExecutionMode.Play => AutomationEditorMode.Play,
            EngineExecutionMode.Paused or EngineExecutionMode.Step => AutomationEditorMode.Paused,
            _ => throw new InvalidOperationException($"未知 Editor mode：{session.Engine.Mode}。"),
        };
    }

    private static EditorGameObject RequireGameObject(EditorSceneModel scene, int stableId)
    {
        return stableId <= 0 || !scene.TryGet(stableId, out EditorGameObject? gameObject)
            ? throw NotFound($"GameObject {stableId} 不存在。")
            : gameObject;
    }

    private EditorProjectSession RequireSession()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _app.CurrentSession
            ?? throw StateUnavailable("当前没有打开的 Editor project session。");
    }

    private EditorProjectSession RequireEditSession()
    {
        EditorProjectSession session = RequireSession();
        return session.Engine.Mode != EngineExecutionMode.Edit ? throw StateUnavailable("Authoring 写操作只允许在 Edit mode 执行。") : session;
    }

    private static string GameObjectResource(EditorProjectSession session, int stableId)
    {
        return $"{EditorAutomationRuntime.CreateSceneResourceId(session)}:game-object:{stableId}";
    }

    private static string SelectionResource(EditorProjectSession session)
    {
        return $"{EditorAutomationRuntime.CreateSceneResourceId(session)}:selection";
    }

    private static string SceneViewResource(EditorProjectSession session)
    {
        return $"{EditorAutomationRuntime.CreateSceneResourceId(session)}:scene-view";
    }

    private static string AuthoringWorldResource(EditorProjectSession session)
    {
        return $"{EditorAutomationRuntime.CreateSceneResourceId(session)}:authoring-world";
    }

    private static string AssetResource(EditorProjectSession session, string assetPath)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(assetPath);
        try
        {
            string token = Convert.ToHexStringLower(SHA256.HashData(pathBytes));
            return $"project:{EditorAutomationRuntime.StableProjectId(session.Project.ProjectRoot)}:asset:{token}";
        }
        finally
        {
            Array.Clear(pathBytes);
        }
    }

    private static string[] SceneAndGameObjectResources(
        EditorProjectSession session,
        int stableId)
    {
        return SceneAndGameObjectResources(session, [stableId]);
    }

    private static string[] SceneAndGameObjectResources(
        EditorProjectSession session,
        IReadOnlyList<int> stableIds)
    {
        return
        [
            EditorAutomationRuntime.CreateSceneResourceId(session),
            .. stableIds.Select(stableId => GameObjectResource(session, stableId)),
        ];
    }

    private static string[] CanvasResources(EditorProjectSession session)
    {
        return
        [
            EditorAutomationRuntime.CreateSceneResourceId(session),
            .. session.SceneModel
                .EnumerateDepthFirst()
                .Where(static gameObject => gameObject.WebCanvas is not null)
                .Select(gameObject => GameObjectResource(session, gameObject.StableId)),
        ];
    }

    private string[] CurrentWorkspaceResources(bool includeTransition)
    {
        List<string> resources = [WorkspaceResource, ProjectResource];
        if (includeTransition)
        {
            resources.Add(TransitionResource);
        }

        if (_app.CurrentSession is { } session)
        {
            resources.Add(EditorAutomationRuntime.CreateSceneResourceId(session));
        }

        return [.. resources.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    private static AutomationOperationResult ExecuteWrite<T>(
        EditorProjectSession session,
        IEditorCommand command,
        Func<(T Response, string[] Resources)> complete,
        JsonTypeInfo<T> typeInfo)
    {
        EditorAutomationTransactionState before = session.CaptureAutomationTransactionState();
        try
        {
            command.Execute(session.SceneModel);
            (T response, string[] resources) = complete();
            EditorAutomationTransactionState after = session.CaptureAutomationTransactionState();
            JsonElement payload = JsonSerializer.SerializeToElement(response, typeInfo);
            return new AutomationOperationResult
            {
                Payload = payload,
                UndoAction = new EditorCommandUndoAction(
                    session,
                    command,
                    before,
                    after),
                ResourceIds = resources,
                WriteStateChanged = true,
            };
        }
        catch (Exception exception)
        {
            try
            {
                command.Undo(session.SceneModel);
                session.RestoreAutomationTransactionState(before);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    $"Automation authoring command '{command.Name}' 失败且无法恢复 before state。",
                    exception,
                    rollbackException);
            }

            throw;
        }
    }

    private static AutomationOperationResult NoChange<T>(
        T response,
        JsonTypeInfo<T> typeInfo,
        string[] resources)
    {
        return new AutomationOperationResult
        {
            Payload = JsonSerializer.SerializeToElement(response, typeInfo),
            ResourceIds = resources,
            WriteStateChanged = false,
        };
    }

    internal static void EnsureAcyclicReparent(
        EditorSceneModel scene,
        int stableId,
        int? parentStableId)
    {
        ArgumentNullException.ThrowIfNull(scene);
        int? ancestorId = parentStableId;
        while (ancestorId is { } currentAncestorId)
        {
            if (currentAncestorId == stableId)
            {
                throw Invalid(parentStableId == stableId
                    ? "GameObject 不能成为自己的父节点。"
                    : "GameObject 不能移动到自己的后代节点下。");
            }

            ancestorId = scene.Get(currentAncestorId).ParentId;
        }
    }

    internal static bool TransformContentEquals(
        EditorSceneTransform left,
        EditorSceneTransform right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.X == right.X &&
            left.Y == right.Y &&
            left.RotationRadians == right.RotationRadians &&
            left.ScaleX == right.ScaleX &&
            left.ScaleY == right.ScaleY;
    }

    internal static bool WindowRequestWouldChange(
        AutomationWindowSnapshot current,
        AutomationWindowSetRequest request)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(request);
        return (request.X.HasValue &&
                (current.LogicalX != request.X.Value || current.LogicalY != request.Y!.Value)) ||
            (request.Width.HasValue &&
             (current.LogicalWidth != request.Width.Value ||
              current.LogicalHeight != request.Height!.Value)) ||
            (request.State.HasValue && current.State != request.State.Value) ||
            (request.Activate && !current.Focused);
    }

    internal static bool SceneToolRequestWouldChange(
        AutomationSceneToolSnapshot current,
        AutomationSceneToolSetRequest request)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(request);
        bool cameraPairInvalid = request.CameraCenterX.HasValue != request.CameraCenterY.HasValue;
        bool overlayPairInvalid = request.OverlayOffsetX.HasValue != request.OverlayOffsetY.HasValue;
        return cameraPairInvalid ||
            overlayPairInvalid ||
            (request.Tool.HasValue && current.Tool != request.Tool.Value) ||
            (request.GizmoSpace.HasValue && current.GizmoSpace != request.GizmoSpace.Value) ||
            (request.GridVisible.HasValue && current.GridVisible != request.GridVisible.Value) ||
            (request.SnapEnabled.HasValue && current.SnapEnabled != request.SnapEnabled.Value) ||
            (request.MoveSnap.HasValue && current.MoveSnap != request.MoveSnap.Value) ||
            (request.RotationSnapDegrees.HasValue &&
             current.RotationSnapDegrees != request.RotationSnapDegrees.Value) ||
            (request.ScaleSnap.HasValue && current.ScaleSnap != request.ScaleSnap.Value) ||
            (request.CameraCenterX.HasValue &&
             (current.CameraCenterX != request.CameraCenterX.Value ||
              current.CameraCenterY != request.CameraCenterY!.Value)) ||
            (request.CameraCellsPerPixel.HasValue &&
             current.CameraCellsPerPixel != request.CameraCellsPerPixel.Value) ||
            (request.Brush is not null && request.Brush != current.Brush) ||
            (request.BrushPanelVisible.HasValue &&
             current.BrushPanelVisible != request.BrushPanelVisible.Value) ||
            (request.OverlayDock.HasValue && current.OverlayDock != request.OverlayDock.Value) ||
            (request.OverlayOffsetX.HasValue &&
             (current.OverlayOffsetX != request.OverlayOffsetX.Value ||
              current.OverlayOffsetY != request.OverlayOffsetY!.Value));
    }

    internal static bool PanelDockRequestAlreadyApplied(
        EditorPanelSnapshot source,
        EditorPanelSnapshot? target,
        AutomationPanelDockRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!source.DockStateKnown)
        {
            return false;
        }

        if (request.Placement == AutomationPanelDockPlacement.Tab)
        {
            return source.Docked &&
                target is { DockStateKnown: true, Docked: true } targetPanel &&
                !string.IsNullOrWhiteSpace(source.DockGroupId) &&
                string.Equals(source.DockGroupId, targetPanel.DockGroupId, StringComparison.Ordinal);
        }

        if (request.Placement != AutomationPanelDockPlacement.Floating || source.Docked)
        {
            return false;
        }

        bool positionMatches = !request.X.HasValue ||
            (source.X == request.X.Value && source.Y == request.Y!.Value);
        bool sizeMatches = !request.Width.HasValue ||
            (source.Width == request.Width.Value && source.Height == request.Height!.Value);
        return positionMatches && sizeMatches;
    }

    private static AutomationOperationResult Result<T>(
        T response,
        JsonTypeInfo<T> typeInfo,
        string[] resources,
        AutomationRevisionSnapshot? revision = null,
        bool stateEventAlreadyPublished = false)
    {
        return new AutomationOperationResult
        {
            Payload = JsonSerializer.SerializeToElement(response, typeInfo),
            ResourceIds = resources,
            RevisionOverride = revision,
            StateChanged = revision is not null,
            StateEventAlreadyPublished = stateEventAlreadyPublished,
        };
    }

    private static AutomationRevisionSnapshot AdvanceAndCapture(
        AutomationRevisionStore revisions,
        string[] changedResources,
        string[] responseResources)
    {
        _ = revisions.Advance(changedResources);
        return revisions.Capture(responseResources);
    }

    private static T Deserialize<T>(
        JsonElement? payload,
        JsonTypeInfo<T> typeInfo,
        string method)
        where T : class
    {
        try
        {
            return payload?.Deserialize(typeInfo)
                ?? throw Invalid($"Automation method '{method}' 缺少 payload。");
        }
        catch (JsonException exception)
        {
            throw Invalid($"Automation method '{method}' payload schema 无效：{exception.Message}");
        }
    }

    private static AutomationPageRequest DeserializePage(JsonElement? payload, string method)
    {
        AutomationPageRequest request = payload is null
            ? new AutomationPageRequest()
            : Deserialize(payload, AutomationJsonContext.Default.AutomationPageRequest, method);
        ValidateSchema(request.SchemaVersion, method);
        return request.PageSize is < 1 or > 500 ||
            request.Sort is null ||
            request.Sort.Length > 8 ||
            (request.Filter is not null && request.Filter.Clauses is null) ||
            request.Filter?.Clauses.Length > 32 ||
            request.Cursor is { Length: > 4096 }
            ? throw Invalid(
                $"Automation method '{method}' 的 filter/sort/cursor/pageSize 无效或超过上限。")
            : request;
    }

    private static void EnsureEmpty(JsonElement? payload, string method)
    {
        if (payload is null || payload.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        if (payload.Value.ValueKind == JsonValueKind.Object &&
            !payload.Value.EnumerateObject().MoveNext())
        {
            return;
        }

        throw Invalid($"Automation method '{method}' 不接受非空 payload。");
    }

    private static void ValidateSchema(int schemaVersion, string method)
    {
        if (schemaVersion != AutomationProtocolConstants.WireSchemaVersion)
        {
            throw Invalid(
                $"Automation method '{method}' schemaVersion 必须为 " +
                $"{AutomationProtocolConstants.WireSchemaVersion}。");
        }
    }

    private static string ValidateDisplayName(string value, string field)
    {
        ArgumentNullException.ThrowIfNull(value);
        string normalized = value.Trim();
        return normalized.Length is < 1 or > 256 || normalized.Any(char.IsControl)
            ? throw Invalid($"{field} 必须是 1..256 字符且不得包含控制字符。")
            : normalized;
    }

    private static string? NormalizeOptionalText(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();
        return normalized.Length > maxLength || normalized.Any(char.IsControl)
            ? throw Invalid($"{field} 必须不超过 {maxLength} 字符且不得包含控制字符。")
            : normalized;
    }

    private static string NormalizePrefabPath(
        EditorProjectSession session,
        string value,
        bool requireExisting)
    {
        string logicalPath;
        try
        {
            logicalPath = EditorPrefabAssetStore.NormalizeAssetPath(value);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw Invalid(exception.Message);
        }

        string[] segments = logicalPath.Split('/');
        string root = Path.GetFullPath(session.Project.ContentRootPath);
        string fullPath = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
        string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("assetPath 越过 Content root。");
        }

        string current = root;
        for (int i = -1; i < segments.Length; i++)
        {
            if (i >= 0)
            {
                current = Path.Combine(current, segments[i]);
            }

            if (!File.Exists(current) && !Directory.Exists(current))
            {
                continue;
            }

            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw Invalid("assetPath 不允许经过 reparse point。");
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                throw Invalid($"assetPath 无法安全检查：{exception.Message}");
            }
        }

        return requireExisting && !File.Exists(fullPath)
            ? throw NotFound($"Prefab asset '{logicalPath}' 不存在。")
            : string.Join('/', segments);
    }

    private static UiCanvasScalerSettings ToCanvasScalerSettings(
        AutomationCanvasScalerValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        UiScaleMode scaleMode = value.ScaleMode switch
        {
            AutomationCanvasScaleMode.ConstantPixelSize => UiScaleMode.ConstantPixelSize,
            AutomationCanvasScaleMode.ScaleWithScreenSize => UiScaleMode.ScaleWithScreenSize,
            AutomationCanvasScaleMode.ConstantPhysicalSize => UiScaleMode.ConstantPhysicalSize,
            _ => throw Invalid("canvasScaler.scaleMode 无效。"),
        };
        UiScreenMatchMode screenMatchMode = value.ScreenMatchMode switch
        {
            AutomationCanvasScreenMatchMode.MatchWidthOrHeight =>
                UiScreenMatchMode.MatchWidthOrHeight,
            AutomationCanvasScreenMatchMode.Expand => UiScreenMatchMode.Expand,
            AutomationCanvasScreenMatchMode.Shrink => UiScreenMatchMode.Shrink,
            _ => throw Invalid("canvasScaler.screenMatchMode 无效。"),
        };
        UiPhysicalUnit physicalUnit = value.PhysicalUnit switch
        {
            AutomationCanvasPhysicalUnit.Centimeters => UiPhysicalUnit.Centimeters,
            AutomationCanvasPhysicalUnit.Millimeters => UiPhysicalUnit.Millimeters,
            AutomationCanvasPhysicalUnit.Inches => UiPhysicalUnit.Inches,
            AutomationCanvasPhysicalUnit.Points => UiPhysicalUnit.Points,
            AutomationCanvasPhysicalUnit.Picas => UiPhysicalUnit.Picas,
            _ => throw Invalid("canvasScaler.physicalUnit 无效。"),
        };

        ValidateFinitePositive(value.ScaleFactor, "canvasScaler.scaleFactor");
        ValidateFinitePositive(value.ReferenceWidth, "canvasScaler.referenceWidth");
        ValidateFinitePositive(value.ReferenceHeight, "canvasScaler.referenceHeight");
        ValidateFinitePositive(value.FallbackScreenDpi, "canvasScaler.fallbackScreenDpi");
        ValidateFinitePositive(value.DefaultSpriteDpi, "canvasScaler.defaultSpriteDpi");
        ValidateFinitePositive(
            value.ReferencePixelsPerUnit,
            "canvasScaler.referencePixelsPerUnit");
        return !float.IsFinite(value.MatchWidthOrHeight) ||
            value.MatchWidthOrHeight is < 0f or > 1f
            ? throw Invalid("canvasScaler.matchWidthOrHeight 必须是 0..1 的有限数值。")
            : new UiCanvasScalerSettings(
            scaleMode,
            value.ScaleFactor,
            value.ReferenceWidth,
            value.ReferenceHeight,
            screenMatchMode,
            value.MatchWidthOrHeight,
            physicalUnit,
            value.FallbackScreenDpi,
            value.DefaultSpriteDpi,
            value.ReferencePixelsPerUnit);
    }

    private static AutomationCanvasScalerValue ToAutomationCanvasScaler(
        in UiCanvasScalerSettings settings)
    {
        return new AutomationCanvasScalerValue
        {
            ScaleMode = settings.ScaleMode switch
            {
                UiScaleMode.ConstantPixelSize => AutomationCanvasScaleMode.ConstantPixelSize,
                UiScaleMode.ScaleWithScreenSize => AutomationCanvasScaleMode.ScaleWithScreenSize,
                UiScaleMode.ConstantPhysicalSize => AutomationCanvasScaleMode.ConstantPhysicalSize,
                _ => throw StateUnavailable($"未知 CanvasScaler ScaleMode '{settings.ScaleMode}'。"),
            },
            ScaleFactor = settings.ScaleFactor,
            ReferenceWidth = settings.ReferenceWidth,
            ReferenceHeight = settings.ReferenceHeight,
            ScreenMatchMode = settings.ScreenMatchMode switch
            {
                UiScreenMatchMode.MatchWidthOrHeight =>
                    AutomationCanvasScreenMatchMode.MatchWidthOrHeight,
                UiScreenMatchMode.Expand => AutomationCanvasScreenMatchMode.Expand,
                UiScreenMatchMode.Shrink => AutomationCanvasScreenMatchMode.Shrink,
                _ => throw StateUnavailable(
                    $"未知 CanvasScaler ScreenMatchMode '{settings.ScreenMatchMode}'。"),
            },
            MatchWidthOrHeight = settings.MatchWidthOrHeight,
            PhysicalUnit = settings.PhysicalUnit switch
            {
                UiPhysicalUnit.Centimeters => AutomationCanvasPhysicalUnit.Centimeters,
                UiPhysicalUnit.Millimeters => AutomationCanvasPhysicalUnit.Millimeters,
                UiPhysicalUnit.Inches => AutomationCanvasPhysicalUnit.Inches,
                UiPhysicalUnit.Points => AutomationCanvasPhysicalUnit.Points,
                UiPhysicalUnit.Picas => AutomationCanvasPhysicalUnit.Picas,
                _ => throw StateUnavailable(
                    $"未知 CanvasScaler PhysicalUnit '{settings.PhysicalUnit}'。"),
            },
            FallbackScreenDpi = settings.FallbackScreenDpi,
            DefaultSpriteDpi = settings.DefaultSpriteDpi,
            ReferencePixelsPerUnit = settings.ReferencePixelsPerUnit,
        };
    }

    private static AutomationSceneToolSetRequest CreateSceneToolSetRequest(
        AutomationSceneToolSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new AutomationSceneToolSetRequest
        {
            Tool = snapshot.Tool,
            GizmoSpace = snapshot.GizmoSpace,
            GridVisible = snapshot.GridVisible,
            SnapEnabled = snapshot.SnapEnabled,
            MoveSnap = snapshot.MoveSnap,
            RotationSnapDegrees = snapshot.RotationSnapDegrees,
            ScaleSnap = snapshot.ScaleSnap,
            CameraCenterX = snapshot.CameraCenterX,
            CameraCenterY = snapshot.CameraCenterY,
            CameraCellsPerPixel = snapshot.CameraCellsPerPixel,
            Brush = snapshot.Brush,
            BrushPanelVisible = snapshot.BrushPanelVisible,
            OverlayDock = snapshot.OverlayDock,
            OverlayOffsetX = snapshot.OverlayOffsetX,
            OverlayOffsetY = snapshot.OverlayOffsetY,
        };
    }

    private static void ValidateFinitePositive(float value, string field)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw Invalid($"{field} 必须是有限正数。");
        }
    }

    private static string ValidateIdentifier(
        string value,
        string field,
        int maxLength,
        bool allowDotsAndNestedType = false)
    {
        ArgumentNullException.ThrowIfNull(value);
        string normalized = value.Trim();
        bool valid = normalized.Length is >= 1 &&
            normalized.Length <= maxLength &&
            !normalized.Any(char.IsControl) &&
            (allowDotsAndNestedType ||
             normalized.All(static character =>
                 char.IsAsciiLetterOrDigit(character) ||
                 character is '.' or '_' or '-'));
        return !valid ? throw Invalid($"{field} 的长度或字符无效。") : normalized;
    }

    private static void ValidateParentAndIndex(
        EditorSceneModel scene,
        int? parentStableId,
        int? siblingIndex,
        bool allowEnd)
    {
        int count = parentStableId is { } parentId
            ? RequireGameObject(scene, parentId).Children.Count
            : scene.RootIds.Count;
        int maximum = allowEnd ? count : Math.Max(0, count - 1);
        if (siblingIndex is < 0 || siblingIndex > maximum)
        {
            throw Invalid($"siblingIndex 必须在 0..{maximum}。");
        }
    }

    private static void ValidateComponentIndex(
        EditorGameObject gameObject,
        int index,
        string field)
    {
        if ((uint)index >= (uint)gameObject.Components.Count)
        {
            throw Invalid(
                $"{field}={index} 越界；GameObject {gameObject.StableId} 有 " +
                $"{gameObject.Components.Count} 个 Behaviour components。");
        }
    }

    private static Behaviour RequireBehaviour(EditorProjectSession session, string typeName)
    {
        return TryCreateBehaviour(session, typeName, out Behaviour? behaviour)
            ? behaviour
            : throw Invalid(
                $"Behaviour type '{typeName}' 不存在、不是 concrete Behaviour 或没有无参构造函数。");
    }

    private static Behaviour BindBehaviour(
        EditorProjectSession session,
        EditorComponentModel component)
    {
        Behaviour behaviour = RequireBehaviour(session, component.TypeName);
        try
        {
            SerializedFieldBinder.Bind(behaviour, component.SerializedFields);
            return behaviour;
        }
        catch (Exception exception) when (
            exception is FormatException or OverflowException or ArgumentException or
            InvalidOperationException or NotSupportedException)
        {
            throw Invalid(
                $"Behaviour '{component.TypeName}' 的 SerializedFields 无效：{exception.Message}");
        }
    }

    private static bool TryCreateBehaviour(
        EditorProjectSession session,
        string typeName,
        out Behaviour behaviour)
    {
        ScriptAssemblyRegistry scripts =
            session.Engine.Context.GetService<ScriptAssemblyRegistry>();
        for (int i = 0; i < scripts.Assemblies.Count; i++)
        {
            Type? type = scripts.Assemblies[i].GetType(typeName, throwOnError: false);
            if (type is not null &&
                !type.IsAbstract &&
                typeof(Behaviour).IsAssignableFrom(type) &&
                type.GetConstructor(Type.EmptyTypes) is not null)
            {
                behaviour = (Behaviour)Activator.CreateInstance(type)!;
                return true;
            }
        }

        behaviour = null!;
        return false;
    }

    private static ScriptFieldDescriptor RequireInspectorField(
        Behaviour behaviour,
        string fieldName)
    {
        ScriptFieldDescriptor[] fields = ScriptInspector.InspectFields(behaviour);
        for (int i = 0; i < fields.Length; i++)
        {
            if (string.Equals(fields[i].Name, fieldName, StringComparison.Ordinal))
            {
                return fields[i];
            }
        }

        throw Invalid(
            $"Behaviour '{behaviour.GetType().FullName}' 没有 Inspector 字段 '{fieldName}'。");
    }

    private static void ValidateRange(in ScriptFieldDescriptor field)
    {
        if (!field.RangeMinimum.HasValue && !field.RangeMaximum.HasValue)
        {
            return;
        }

        double numeric;
        try
        {
            numeric = Convert.ToDouble(field.Value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (
            exception is InvalidCastException or FormatException or OverflowException)
        {
            throw Invalid($"字段 '{field.Name}' 声明了 Range，但值无法转换为有限数值。");
        }

        if (!double.IsFinite(numeric) ||
            (field.RangeMinimum is { } minimum && numeric < minimum) ||
            (field.RangeMaximum is { } maximum && numeric > maximum))
        {
            throw Invalid(
                $"字段 '{field.Name}' 必须在 {field.RangeMinimum?.ToString(CultureInfo.InvariantCulture) ?? "-∞"}.." +
                $"{field.RangeMaximum?.ToString(CultureInfo.InvariantCulture) ?? "+∞"}。");
        }
    }

    private static string EncodeFieldValue(in ScriptFieldDescriptor field)
    {
        object? value = field.Value;
        return value switch
        {
            null => string.Empty,
            string text => text,
            System.Numerics.Vector2 vector => SerializedFieldValueCodec.Format(vector),
            System.Numerics.Vector3 vector => SerializedFieldValueCodec.Format(vector),
            System.Numerics.Vector4 vector => SerializedFieldValueCodec.Format(vector),
            ScriptAssetReference reference => reference.ToString(),
            Enum enumValue => enumValue.ToString(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    internal static string NormalizeAssetReferenceValue(
        string fieldName,
        ScriptAssetKind? expectedKind,
        string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        if (!expectedKind.HasValue ||
            !ScriptAssetReference.TryDecode(value, out ScriptAssetReference reference) ||
            !reference.IsValid ||
            reference.AssetType != expectedKind.Value ||
            reference.AssetId.Length > 256 ||
            reference.AssetId.Any(static character => char.IsControl(character) || character == '|'))
        {
            throw Invalid(
                $"字段 '{fieldName}' 必须使用匹配声明类型的有效 stable asset reference 编码。");
        }

        string logicalPath = reference.LogicalPath.Trim().Replace('\\', '/');
        if (logicalPath.Length is < 1 or > 32767 ||
            logicalPath.StartsWith("/", StringComparison.Ordinal) ||
            Path.IsPathFullyQualified(logicalPath) ||
            logicalPath.Any(static character => char.IsControl(character) || character == '|'))
        {
            throw Invalid($"字段 '{fieldName}' 的 asset logical path 无效或越过 root。");
        }

        string[] segments = logicalPath.Split('/', StringSplitOptions.None);
        return segments.Any(static segment =>
            segment.Length == 0 ||
            segment is "." or ".." ||
            segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            ? throw Invalid($"字段 '{fieldName}' 的 asset logical path 包含非法 segment。")
            : ScriptAssetReference.Encode(
                reference.AssetId,
                string.Join('/', segments),
                reference.AssetType);
    }

    private static bool IsRecoverableSceneFailure(Exception exception)
    {
        return exception is ArgumentException or InvalidOperationException or
            IOException or UnauthorizedAccessException or NotSupportedException;
    }

    private static AutomationPanelInfo[] FilterPanels(
        AutomationPanelInfo[] source,
        AutomationQueryFilter? filter)
    {
        return ApplyFilter(source, filter, static (item, clause) => clause.Field switch
        {
            "id" => MatchString(item.Id, clause),
            "title" => MatchString(item.Title, clause),
            "visible" => MatchBoolean(item.Visible, clause),
            "chrome" => MatchBoolean(item.Chrome, clause),
            "maximized" => MatchBoolean(item.Maximized, clause),
            "focusPending" => MatchBoolean(item.FocusPending, clause),
            _ => throw Invalid($"Panel filter field '{clause.Field}' 不受支持。"),
        });
    }

    private static void SortPanels(
        AutomationPanelInfo[] items,
        AutomationSortClause[] sort)
    {
        ValidateSort(sort);
        Array.Sort(items, (left, right) =>
        {
            for (int i = 0; i < sort.Length; i++)
            {
                int compared = sort[i].Field switch
                {
                    "id" => string.CompareOrdinal(left.Id, right.Id),
                    "title" => CompareText(left.Title, right.Title),
                    "visible" => left.Visible.CompareTo(right.Visible),
                    "chrome" => left.Chrome.CompareTo(right.Chrome),
                    "maximized" => left.Maximized.CompareTo(right.Maximized),
                    "focusPending" => left.FocusPending.CompareTo(right.FocusPending),
                    _ => throw Invalid($"Panel sort field '{sort[i].Field}' 不受支持。"),
                };
                if (compared != 0)
                {
                    return ApplyDirection(compared, sort[i].Direction);
                }
            }

            return string.CompareOrdinal(left.Id, right.Id);
        });
    }

    private static AutomationHierarchyItem[] FilterHierarchy(
        AutomationHierarchyItem[] source,
        AutomationQueryFilter? filter)
    {
        return ApplyFilter(source, filter, static (item, clause) => clause.Field switch
        {
            "stableId" => MatchNumber(item.StableId, clause),
            "parentStableId" => MatchNullableNumber(item.ParentStableId, clause),
            "siblingIndex" => MatchNumber(item.SiblingIndex, clause),
            "depth" => MatchNumber(item.Depth, clause),
            "hierarchyPath" => MatchString(item.HierarchyPath, clause),
            "name" => MatchString(item.Name, clause),
            "enabled" => MatchBoolean(item.Enabled, clause),
            "sceneVisible" => MatchBoolean(item.SceneVisible, clause),
            "scenePickable" => MatchBoolean(item.ScenePickable, clause),
            "selected" => MatchBoolean(item.Selected, clause),
            "childCount" => MatchNumber(item.ChildCount, clause),
            "componentCount" => MatchNumber(item.ComponentCount, clause),
            "hasPrefabLink" => MatchBoolean(item.HasPrefabLink, clause),
            "hasWebCanvas" => MatchBoolean(item.HasWebCanvas, clause),
            "hasCanvasScaler" => MatchBoolean(item.HasCanvasScaler, clause),
            _ => throw Invalid($"Hierarchy filter field '{clause.Field}' 不受支持。"),
        });
    }

    private static void SortHierarchy(
        AutomationHierarchyItem[] items,
        AutomationSortClause[] sort)
    {
        ValidateSort(sort);
        Array.Sort(items, (left, right) =>
        {
            for (int i = 0; i < sort.Length; i++)
            {
                int compared = sort[i].Field switch
                {
                    "stableId" => left.StableId.CompareTo(right.StableId),
                    "parentStableId" => Nullable.Compare(left.ParentStableId, right.ParentStableId),
                    "siblingIndex" => left.SiblingIndex.CompareTo(right.SiblingIndex),
                    "depth" => left.Depth.CompareTo(right.Depth),
                    "hierarchyPath" => string.CompareOrdinal(left.HierarchyPath, right.HierarchyPath),
                    "name" => CompareText(left.Name, right.Name),
                    "enabled" => left.Enabled.CompareTo(right.Enabled),
                    "sceneVisible" => left.SceneVisible.CompareTo(right.SceneVisible),
                    "scenePickable" => left.ScenePickable.CompareTo(right.ScenePickable),
                    "selected" => left.Selected.CompareTo(right.Selected),
                    "childCount" => left.ChildCount.CompareTo(right.ChildCount),
                    "componentCount" => left.ComponentCount.CompareTo(right.ComponentCount),
                    "hasPrefabLink" => left.HasPrefabLink.CompareTo(right.HasPrefabLink),
                    "hasWebCanvas" => left.HasWebCanvas.CompareTo(right.HasWebCanvas),
                    "hasCanvasScaler" => left.HasCanvasScaler.CompareTo(right.HasCanvasScaler),
                    _ => throw Invalid($"Hierarchy sort field '{sort[i].Field}' 不受支持。"),
                };
                if (compared != 0)
                {
                    return ApplyDirection(compared, sort[i].Direction);
                }
            }

            return left.StableId.CompareTo(right.StableId);
        });
    }

    private static T[] ApplyFilter<T>(
        T[] source,
        AutomationQueryFilter? filter,
        Func<T, AutomationFilterClause, bool> predicate)
    {
        if (filter is null)
        {
            return [.. source];
        }

        if (!Enum.IsDefined(filter.Match) ||
            filter.Clauses is null ||
            filter.Clauses.Length > 32)
        {
            throw Invalid("filter match/clauses 无效或超过 32 条。");
        }

        if (filter.Clauses.Length == 0)
        {
            return [.. source];
        }

        List<T> result = new(source.Length);
        for (int i = 0; i < source.Length; i++)
        {
            bool matched = filter.Match == AutomationFilterMatch.All;
            for (int j = 0; j < filter.Clauses.Length; j++)
            {
                AutomationFilterClause clause = filter.Clauses[j]
                    ?? throw Invalid("filter clause 不得为 null。");
                if (string.IsNullOrWhiteSpace(clause.Field) ||
                    !Enum.IsDefined(clause.Operator))
                {
                    throw Invalid("filter clause field/operator 无效。");
                }

                bool current = predicate(source[i], clause);
                if (filter.Match == AutomationFilterMatch.All)
                {
                    matched &= current;
                    if (!matched)
                    {
                        break;
                    }
                }
                else
                {
                    matched |= current;
                    if (matched)
                    {
                        break;
                    }
                }
            }

            if (matched)
            {
                result.Add(source[i]);
            }
        }

        return [.. result];
    }

    private static bool MatchString(string actual, AutomationFilterClause clause)
    {
        if (clause.Value.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"Filter field '{clause.Field}' 要求 string value。");
        }

        string expected = clause.Value.GetString() ?? string.Empty;
        return clause.Operator switch
        {
            AutomationFilterOperator.Equals =>
                string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            AutomationFilterOperator.NotEquals =>
                !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            AutomationFilterOperator.Contains =>
                actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            AutomationFilterOperator.StartsWith =>
                actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase),
            AutomationFilterOperator.LessThan or
            AutomationFilterOperator.LessThanOrEqual or
            AutomationFilterOperator.GreaterThan or
            AutomationFilterOperator.GreaterThanOrEqual => throw Invalid(
                $"String filter field '{clause.Field}' 不支持数值比较 {clause.Operator}。"),
            _ => throw Invalid(
                            $"String filter field '{clause.Field}' 不支持 {clause.Operator}。"),
        };
    }

    private static bool MatchBoolean(bool actual, AutomationFilterClause clause)
    {
        if (clause.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw Invalid($"Filter field '{clause.Field}' 要求 boolean value。");
        }

        bool expected = clause.Value.GetBoolean();
        return clause.Operator switch
        {
            AutomationFilterOperator.Equals => actual == expected,
            AutomationFilterOperator.NotEquals => actual != expected,
            AutomationFilterOperator.Contains or
            AutomationFilterOperator.StartsWith or
            AutomationFilterOperator.LessThan or
            AutomationFilterOperator.LessThanOrEqual or
            AutomationFilterOperator.GreaterThan or
            AutomationFilterOperator.GreaterThanOrEqual => throw Invalid(
                $"Boolean filter field '{clause.Field}' 只支持 Equals/NotEquals。"),
            _ => throw Invalid(
                            $"Boolean filter field '{clause.Field}' 不支持 {clause.Operator}。"),
        };
    }

    private static bool MatchNumber(long actual, AutomationFilterClause clause)
    {
        return !clause.Value.TryGetInt64(out long expected)
            ? throw Invalid($"Filter field '{clause.Field}' 要求 Int64 value。")
            : clause.Operator switch
            {
                AutomationFilterOperator.Equals => actual == expected,
                AutomationFilterOperator.NotEquals => actual != expected,
                AutomationFilterOperator.LessThan => actual < expected,
                AutomationFilterOperator.LessThanOrEqual => actual <= expected,
                AutomationFilterOperator.GreaterThan => actual > expected,
                AutomationFilterOperator.GreaterThanOrEqual => actual >= expected,
                AutomationFilterOperator.Contains or
                AutomationFilterOperator.StartsWith => throw Invalid(
                    $"Number filter field '{clause.Field}' 不支持文本运算 {clause.Operator}。"),
                _ => throw Invalid(
                                $"Number filter field '{clause.Field}' 不支持 {clause.Operator}。"),
            };
    }

    private static bool MatchNullableNumber(int? actual, AutomationFilterClause clause)
    {
        return clause.Value.ValueKind == JsonValueKind.Null
            ? clause.Operator switch
            {
                AutomationFilterOperator.Equals => !actual.HasValue,
                AutomationFilterOperator.NotEquals => actual.HasValue,
                AutomationFilterOperator.Contains or
                AutomationFilterOperator.StartsWith or
                AutomationFilterOperator.LessThan or
                AutomationFilterOperator.LessThanOrEqual or
                AutomationFilterOperator.GreaterThan or
                AutomationFilterOperator.GreaterThanOrEqual => throw Invalid(
                    $"Nullable filter field '{clause.Field}' 对 null 只支持 Equals/NotEquals。"),
                _ => throw Invalid(
                                    $"Nullable filter field '{clause.Field}' 对 null 只支持 Equals/NotEquals。"),
            }
            : actual.HasValue && MatchNumber(actual.Value, clause);
    }

    private static void ValidateSort(AutomationSortClause[] sort)
    {
        if (sort is null || sort.Length > 8)
        {
            throw Invalid("sort 不得为 null 且最多 8 条。");
        }

        HashSet<string> fields = new(StringComparer.Ordinal);
        for (int i = 0; i < sort.Length; i++)
        {
            AutomationSortClause clause = sort[i]
                ?? throw Invalid("sort clause 不得为 null。");
            if (string.IsNullOrWhiteSpace(clause.Field) ||
                !Enum.IsDefined(clause.Direction) ||
                !fields.Add(clause.Field))
            {
                throw Invalid("sort field/direction 无效或字段重复。");
            }
        }
    }

    private static int CompareText(string left, string right)
    {
        int compared = string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        return compared != 0 ? compared : string.CompareOrdinal(left, right);
    }

    private static int ApplyDirection(int value, AutomationSortDirection direction)
    {
        return direction switch
        {
            AutomationSortDirection.Ascending => value,
            AutomationSortDirection.Descending => -Math.Sign(value),
            _ => throw Invalid($"Sort direction '{direction}' 无效。"),
        };
    }

    private PageSlice<T> SlicePage<T>(
        string kind,
        string sourceFingerprint,
        AutomationPageRequest request,
        T[] filtered)
    {
        string queryDigest = Fingerprint(
            request with { Cursor = null },
            AutomationJsonContext.Default.AutomationPageRequest);
        int offset = DecodeCursor(
            request.Cursor,
            kind,
            sourceFingerprint,
            queryDigest,
            filtered.Length);
        int count = Math.Min(request.PageSize, filtered.Length - offset);
        T[] page = new T[count];
        Array.Copy(filtered, offset, page, 0, count);
        int nextOffset = checked(offset + count);
        return new PageSlice<T>(
            page,
            new AutomationPageInfo
            {
                Returned = count,
                Total = filtered.Length,
                NextCursor = nextOffset < filtered.Length
                    ? EncodeCursor(kind, sourceFingerprint, queryDigest, nextOffset)
                    : null,
            });
    }

    private string EncodeCursor(
        string kind,
        string sourceFingerprint,
        string queryDigest,
        int offset)
    {
        byte[] body = Encoding.UTF8.GetBytes(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{kind}\n{sourceFingerprint}\n{queryDigest}\n{offset}"));
        try
        {
            byte[] signature = HMACSHA256.HashData(_cursorKey, body);
            try
            {
                return $"{Base64UrlEncode(body)}.{Base64UrlEncode(signature)}";
            }
            finally
            {
                CryptographicOperations.ZeroMemory(signature);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(body);
        }
    }

    private int DecodeCursor(
        string? cursor,
        string kind,
        string sourceFingerprint,
        string queryDigest,
        int total)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return 0;
        }

        try
        {
            string[] parts = cursor.Split('.');
            if (parts.Length != 2)
            {
                throw new FormatException();
            }

            byte[] body = Base64UrlDecode(parts[0]);
            byte[] suppliedSignature = Base64UrlDecode(parts[1]);
            try
            {
                byte[] expectedSignature = HMACSHA256.HashData(_cursorKey, body);
                try
                {
                    if (!CryptographicOperations.FixedTimeEquals(
                        suppliedSignature,
                        expectedSignature))
                    {
                        throw new FormatException();
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(expectedSignature);
                }

                string[] fields = Encoding.UTF8.GetString(body).Split('\n');
                return fields.Length != 4 ||
                    !string.Equals(fields[0], kind, StringComparison.Ordinal) ||
                    !string.Equals(fields[1], sourceFingerprint, StringComparison.Ordinal) ||
                    !string.Equals(fields[2], queryDigest, StringComparison.Ordinal) ||
                    !int.TryParse(
                        fields[3],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int offset) ||
                    offset < 0 ||
                    offset > total
                    ? throw new FormatException()
                    : offset;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(body);
                CryptographicOperations.ZeroMemory(suppliedSignature);
            }
        }
        catch (Exception exception) when (
            exception is FormatException or ArgumentException)
        {
            throw CursorConflict(
                "分页 cursor 已失效、被篡改，或不属于当前查询；请从首屏重新同步。");
        }
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string value)
    {
        string normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = (normalized.Length % 4) switch
        {
            0 => normalized,
            2 => normalized + "==",
            3 => normalized + "=",
            _ => throw new FormatException(),
        };
        return Convert.FromBase64String(normalized);
    }

    private static string Fingerprint<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        byte[] utf8 = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
        try
        {
            return Convert.ToHexStringLower(SHA256.HashData(utf8));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(utf8);
        }
    }

    private static AutomationRequestException Invalid(string message)
    {
        return RequestError(
            AutomationErrorCodes.InvalidRequest,
            AutomationErrorCategory.Validation,
            message);
    }

    private static AutomationRequestException NotFound(string message)
    {
        return RequestError(
            AutomationErrorCodes.ResourceNotFound,
            AutomationErrorCategory.Availability,
            message);
    }

    private static AutomationRequestException StateUnavailable(string message)
    {
        return RequestError(
            AutomationErrorCodes.StateUnavailable,
            AutomationErrorCategory.Availability,
            message);
    }

    private static AutomationRequestException CursorConflict(string message)
    {
        return RequestError(
            AutomationErrorCodes.RevisionConflict,
            AutomationErrorCategory.Conflict,
            message);
    }

    private static AutomationRequestException RequestError(
        string code,
        AutomationErrorCategory category,
        string message)
    {
        return new AutomationRequestException(new AutomationError
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Code = code,
            Category = category,
            Message = message,
            Transient = false,
        });
    }

    private readonly record struct PageSlice<T>(T[] Items, AutomationPageInfo Info);

    private sealed class EditorCommandUndoAction(
        EditorProjectSession session,
        IEditorCommand command,
        EditorAutomationTransactionState before,
        EditorAutomationTransactionState after) : IAutomationUndoAction
    {
        public string Name => command.Name;

        public void Undo()
        {
            command.Undo(session.SceneModel);
            session.RestoreAutomationTransactionState(before);
        }

        public void Redo()
        {
            command.Execute(session.SceneModel);
            session.RestoreAutomationTransactionState(after);
        }
    }
}
