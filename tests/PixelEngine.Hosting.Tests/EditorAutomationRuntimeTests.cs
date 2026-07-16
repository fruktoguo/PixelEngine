using System.Buffers;
using System.Text.Json;
using PixelEngine.Editor;
using PixelEngine.Editor.Automation.Client;
using PixelEngine.Editor.Automation.Protocol;
using PixelEngine.Editor.Automation.Server;
using PixelEngine.Editor.Shell;
using PixelEngine.Serialization;
using PixelEngine.Simulation;
using PixelEngine.Scripting;
using PixelEngine.World;
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
                    RequestedScopes =
                    [
                        AutomationScopes.EditorRead,
                        AutomationScopes.EditorControl,
                        AutomationScopes.SettingsWrite,
                        AutomationScopes.ProjectWrite,
                    ],
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
                Task<AutomationTypedInvocationResult<AutomationCapabilityMatrixSnapshot>> matrixPending =
                    client.GetCapabilityMatrixAsync().AsTask();
                Assert.True(SpinWait.SpinUntil(
                    () => runtime.Scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
                    TimeSpan.FromSeconds(5)));
                runtime.DrainEditorIngress();
                AutomationCapabilityMatrixSnapshot matrix = matrixPending.GetAwaiter().GetResult().Response;
                Assert.Equal(runtime.Scheduler.CapabilityDigest, matrix.CapabilityDigest);
                Assert.Equal(runtime.Scheduler.UiCommandDigest, matrix.UiCommandDigest);
                Assert.Equal(runtime.Scheduler.MatrixDigest, matrix.MatrixDigest);
                Assert.Equal(capabilities.Items.Length, matrix.Capabilities.Length);
                Assert.True(matrix.UiCommands.Length >= 200);
                Assert.Contains(matrix.UiCommands, command =>
                    command.Id == "context.hierarchy.delete" &&
                    command.SurfaceId == "editor.panel.hierarchy" &&
                    command.CapabilityIds.Contains(
                        AutomationProtocolConstants.GameObjectDeleteMethod,
                        StringComparer.Ordinal));
                Assert.Contains(matrix.UiCommands, command =>
                    command.Id == "shortcut.delete" &&
                    command.HandlerId.Contains(
                        "EditorMainMenuBar.DispatchShortcuts",
                        StringComparison.Ordinal));
                Assert.Contains(matrix.UiCommands, command =>
                    command.Id == "menu.file.new-project" &&
                    command.CapabilityIds.SequenceEqual(
                        [AutomationProtocolConstants.WorkspaceProjectPickerSetMethod]));
                Assert.Contains(matrix.UiCommands, command =>
                    command.Id == "menu.edit.preferences" &&
                    command.CapabilityIds.SequenceEqual(
                        [AutomationProtocolConstants.WindowPanelSetMethod]));
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
                    descriptor.Id == AutomationProtocolConstants.WorkspaceProjectCreateMethod &&
                    descriptor.RequestSchema == "#/$defs/projectCreateRequest" &&
                    descriptor.ResponseSchema == "#/$defs/transitionResult" &&
                    descriptor.OperationKind == AutomationOperationKind.Command &&
                    descriptor.TransactionMode == AutomationTransactionMode.Forbidden &&
                    descriptor.UsesBackgroundPreparation &&
                    descriptor.RequiredScopes.Contains(AutomationScopes.ProjectWrite, StringComparer.Ordinal) &&
                    descriptor.UiCommandIds.Contains("project-picker.create", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.WorkspaceProjectOpenMethod &&
                    descriptor.RequestSchema == "#/$defs/projectOpenRequest" &&
                    descriptor.UsesBackgroundPreparation &&
                    descriptor.UiCommandIds.Contains("menu.file.open-recent", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.WorkspaceProjectCloseMethod &&
                    descriptor.RequestSchema == "#/$defs/emptyRequest" &&
                    descriptor.TransactionMode == AutomationTransactionMode.Forbidden &&
                    descriptor.UiCommandIds.Contains("menu.file.close-project", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.WorkspaceExitMethod &&
                    descriptor.SupportedModes.SequenceEqual(["edit", "play", "paused"]) &&
                    descriptor.UiCommandIds.Contains("window.close", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.WorkspaceRecentListMethod &&
                    descriptor.RequestSchema == "#/$defs/pageRequest" &&
                    descriptor.ResponseSchema == "#/$defs/recentProjectListResponse" &&
                    descriptor.OperationKind == AutomationOperationKind.Read &&
                    descriptor.UsesBackgroundPreparation &&
                    descriptor.UiCommandIds.Contains("project-picker.recent", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.WorkspaceRecentFavoriteSetMethod &&
                    descriptor.RequestSchema == "#/$defs/recentProjectFavoriteSetRequest" &&
                    descriptor.ResponseSchema == "#/$defs/recentProjectMutationResult" &&
                    descriptor.OperationKind == AutomationOperationKind.Command &&
                    descriptor.TransactionMode == AutomationTransactionMode.Forbidden &&
                    descriptor.UsesBackgroundPreparation &&
                    descriptor.RequiredScopes.SequenceEqual([AutomationScopes.SettingsWrite]) &&
                    descriptor.UiCommandIds.Contains(
                        "project-picker.recent.favorite",
                        StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.WorkspaceRecentRemoveMethod &&
                    descriptor.RequestSchema == "#/$defs/recentProjectRemoveRequest" &&
                    descriptor.OperationKind == AutomationOperationKind.Command &&
                    descriptor.UsesBackgroundPreparation &&
                    descriptor.UiCommandIds.Contains(
                        "project-picker.recent.remove",
                        StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.ShortcutListMethod &&
                    descriptor.RequestSchema == "#/$defs/pageRequest" &&
                    descriptor.ResponseSchema == "#/$defs/shortcutListResponse" &&
                    descriptor.OperationKind == AutomationOperationKind.Read &&
                    descriptor.UiCommandIds.Contains("menu.help.shortcuts", StringComparer.Ordinal));
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
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.RuntimeEntityTransformSetMethod &&
                    descriptor.RequestSchema == "#/$defs/runtimeTransformSetRequest" &&
                    descriptor.ResponseSchema == "#/$defs/runtimeEntity" &&
                    descriptor.ExecutionPhase == AutomationExecutionPhase.EngineInputAndTime &&
                    descriptor.TransactionMode == AutomationTransactionMode.Forbidden &&
                    descriptor.SupportedModes.SequenceEqual(["play", "paused"]) &&
                    descriptor.UiCommandIds.Contains(
                        "panel.inspector.runtime.transform",
                        StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.RuntimeComponentFieldSetMethod &&
                    descriptor.RequestSchema == "#/$defs/runtimeComponentFieldSetRequest" &&
                    descriptor.ExecutionPhase == AutomationExecutionPhase.EngineInputAndTime &&
                    descriptor.RequiresExpectedRevision &&
                    descriptor.RequiresIdempotencyKey &&
                    descriptor.UiCommandIds.Contains(
                        "panel.inspector.runtime.field",
                        StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.RuntimeBodyListMethod &&
                    descriptor.ResponseSchema == "#/$defs/runtimeBodyListResponse" &&
                    descriptor.ExecutionPhase == AutomationExecutionPhase.EnginePhysicsSync &&
                    descriptor.SupportedModes.SequenceEqual(["play", "paused"]));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.RuntimeBodyGetMethod &&
                    descriptor.RequestSchema == "#/$defs/runtimeBodyRequest" &&
                    descriptor.ResponseSchema == "#/$defs/runtimeBody" &&
                    descriptor.UiCommandIds.Contains(
                        "panel.inspector.runtime-body",
                        StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.RuntimeSimulationSetMethod &&
                    descriptor.RequestSchema == "#/$defs/runtimeSimulationSetRequest" &&
                    descriptor.ExecutionPhase == AutomationExecutionPhase.EngineInputAndTime &&
                    descriptor.EventTypes.Contains(
                        AutomationProtocolConstants.RuntimeChangedEventType,
                        StringComparer.Ordinal) &&
                    descriptor.EventTypes.Contains(
                        AutomationProtocolConstants.ProfilerChangedEventType,
                        StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.RuntimeCellInspectMethod &&
                    descriptor.RequestSchema == "#/$defs/runtimeCellInspectRequest" &&
                    descriptor.ResponseSchema == "#/$defs/runtimeCellInspection" &&
                    descriptor.ExecutionPhase == AutomationExecutionPhase.EngineInputAndTime &&
                    descriptor.OperationKind == AutomationOperationKind.Read);
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.RuntimeWorldInspectorGetMethod &&
                    descriptor.RequestSchema == "#/$defs/emptyRequest" &&
                    descriptor.ResponseSchema == "#/$defs/worldInspectorSnapshot" &&
                    descriptor.ExecutionPhase == AutomationExecutionPhase.EditorIngress &&
                    descriptor.OperationKind == AutomationOperationKind.Read &&
                    descriptor.UiCommandIds.Contains("panel.world-inspector", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.RuntimeWorldInspectorSetMethod &&
                    descriptor.RequestSchema == "#/$defs/worldInspectorSetRequest" &&
                    descriptor.ResponseSchema == "#/$defs/worldInspectorSnapshot" &&
                    descriptor.ExecutionPhase == AutomationExecutionPhase.EditorIngress &&
                    descriptor.OperationKind == AutomationOperationKind.Write &&
                    descriptor.TransactionMode == AutomationTransactionMode.Optional &&
                    descriptor.RequiresExpectedRevision &&
                    descriptor.RequiresIdempotencyKey &&
                    descriptor.RequiredScopes.Contains(AutomationScopes.EditorControl, StringComparer.Ordinal) &&
                    descriptor.SupportedModes.SequenceEqual(["edit", "play", "paused"]) &&
                    descriptor.UiCommandIds.Contains("panel.world-inspector.follow", StringComparer.Ordinal) &&
                    descriptor.UiCommandIds.Contains("panel.world-inspector.lock", StringComparer.Ordinal) &&
                    descriptor.UiCommandIds.Contains("panel.world-inspector.coordinates", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.RuntimeMaterialListMethod &&
                    descriptor.RequestSchema == "#/$defs/pageRequest" &&
                    descriptor.ResponseSchema == "#/$defs/materialListResponse" &&
                    descriptor.ExecutionPhase == AutomationExecutionPhase.EngineInputAndTime &&
                    descriptor.OperationKind == AutomationOperationKind.Read &&
                    descriptor.UiCommandIds.Contains("panel.brush.material", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.RuntimeMaterialGetMethod &&
                    descriptor.RequestSchema == "#/$defs/materialRequest" &&
                    descriptor.ResponseSchema == "#/$defs/materialDefinition" &&
                    descriptor.ExecutionPhase == AutomationExecutionPhase.EngineInputAndTime &&
                    descriptor.OperationKind == AutomationOperationKind.Read);
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.MaterialEditorGetMethod &&
                    descriptor.Domain == "materials" &&
                    descriptor.ResponseSchema == "#/$defs/materialEditorSnapshot" &&
                    descriptor.ExecutionPhase == AutomationExecutionPhase.EditorIngress &&
                    descriptor.OperationKind == AutomationOperationKind.Read);
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.MaterialEditorSetMethod &&
                    descriptor.RequestSchema == "#/$defs/materialEditorSetRequest" &&
                    descriptor.TransactionMode == AutomationTransactionMode.Optional &&
                    descriptor.SupportedModes.SequenceEqual(["edit"]));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.MaterialEditorReloadMethod &&
                    descriptor.UsesBackgroundPreparation &&
                    descriptor.TransactionMode == AutomationTransactionMode.Optional &&
                    descriptor.UiCommandIds.Contains("panel.materials.reload", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.MaterialEditorPreviewMethod &&
                    descriptor.ResponseSchema == "#/$defs/materialEditorPreviewResult" &&
                    descriptor.OperationKind == AutomationOperationKind.Write &&
                    descriptor.TransactionMode == AutomationTransactionMode.Optional);
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.MaterialEditorApplyMethod &&
                    descriptor.ResponseSchema == "#/$defs/materialEditorApplyResult" &&
                    descriptor.OperationKind == AutomationOperationKind.Write &&
                    descriptor.TransactionMode == AutomationTransactionMode.Forbidden &&
                    descriptor.ExecutionPhase == AutomationExecutionPhase.EngineWorldStreaming &&
                    descriptor.UsesBackgroundPreparation &&
                    descriptor.RequiredScopes.Contains(AutomationScopes.EditorControl, StringComparer.Ordinal) &&
                    descriptor.RequiredScopes.Contains(AutomationScopes.ProjectWrite, StringComparer.Ordinal) &&
                    descriptor.UiCommandIds.Contains("panel.materials.apply", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.BrushApplyMethod &&
                    descriptor.RequestSchema == "#/$defs/brushApplyRequest" &&
                    descriptor.ResponseSchema == "#/$defs/brushApplyResult" &&
                    descriptor.ExecutionPhase == AutomationExecutionPhase.EngineInputAndTime &&
                    descriptor.OperationKind == AutomationOperationKind.Command &&
                    descriptor.TransactionMode == AutomationTransactionMode.Forbidden);
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.BrushStrokeMethod &&
                    descriptor.RequestSchema == "#/$defs/brushStrokeRequest" &&
                    descriptor.ResponseSchema == "#/$defs/brushStrokeResult" &&
                    descriptor.ExecutionPhase == AutomationExecutionPhase.EngineInputAndTime &&
                    descriptor.OperationKind == AutomationOperationKind.Command &&
                    descriptor.TransactionMode == AutomationTransactionMode.Forbidden);
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.RuntimePhysicsSetMethod &&
                    descriptor.ExecutionPhase == AutomationExecutionPhase.EditorIngress &&
                    descriptor.RequiresExpectedRevision &&
                    descriptor.RequiresIdempotencyKey &&
                    descriptor.UiCommandIds.Contains("panel.physics.apply", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.RuntimeParticlesSetMethod &&
                    descriptor.ExecutionPhase == AutomationExecutionPhase.EditorIngress &&
                    descriptor.EventTypes.Contains(
                        AutomationProtocolConstants.RuntimeChangedEventType,
                        StringComparer.Ordinal) &&
                    descriptor.EventTypes.Contains(
                        AutomationProtocolConstants.ProfilerChangedEventType,
                        StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.RuntimeLightingSetMethod &&
                    descriptor.ExecutionPhase == AutomationExecutionPhase.EditorIngress &&
                    descriptor.RequestSchema == "#/$defs/runtimeLightingSetRequest" &&
                    descriptor.UiCommandIds.Contains("panel.lighting.apply", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.RuntimeSaveSlotListMethod &&
                    descriptor.RequestSchema == "#/$defs/pageRequest" &&
                    descriptor.ResponseSchema == "#/$defs/saveSlotListResponse" &&
                    descriptor.OperationKind == AutomationOperationKind.Read &&
                    descriptor.UsesBackgroundPreparation &&
                    descriptor.UiCommandIds.Contains("panel.save-load.refresh", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.RuntimeSaveSlotSaveMethod &&
                    descriptor.RequestSchema == "#/$defs/saveSlotRequest" &&
                    descriptor.ResponseSchema == "#/$defs/saveSlotOperationResult" &&
                    descriptor.OperationKind == AutomationOperationKind.Write &&
                    descriptor.TransactionMode == AutomationTransactionMode.Forbidden &&
                    descriptor.ExecutionPhase == AutomationExecutionPhase.EngineWorldStreaming &&
                    descriptor.UsesBackgroundPreparation &&
                    descriptor.SupportedModes.SequenceEqual(["edit", "play", "paused"]) &&
                    descriptor.UiCommandIds.Contains("panel.save-load.save", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.RuntimeSaveSlotLoadMethod &&
                    descriptor.RequestSchema == "#/$defs/saveSlotRequest" &&
                    descriptor.ResponseSchema == "#/$defs/saveSlotOperationResult" &&
                    descriptor.OperationKind == AutomationOperationKind.Write &&
                    descriptor.TransactionMode == AutomationTransactionMode.Forbidden &&
                    descriptor.ExecutionPhase == AutomationExecutionPhase.EngineWorldStreaming &&
                    descriptor.UsesBackgroundPreparation &&
                    descriptor.SupportedModes.SequenceEqual(["edit"]) &&
                    descriptor.UiCommandIds.Contains("panel.save-load.load", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.ProjectAssetRefreshMethod &&
                    descriptor.RequestSchema == "#/$defs/emptyRequest" &&
                    descriptor.ResponseSchema == "#/$defs/assetRefreshResult" &&
                    descriptor.OperationKind == AutomationOperationKind.Command &&
                    descriptor.TransactionMode == AutomationTransactionMode.Forbidden &&
                    descriptor.UsesBackgroundPreparation &&
                    descriptor.RequiredScopes.Contains(AutomationScopes.ProjectWrite, StringComparer.Ordinal) &&
                    descriptor.SupportedModes.SequenceEqual(["edit"]) &&
                    descriptor.UiCommandIds.Contains("panel.project.refresh", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.ProjectWindowGetMethod &&
                    descriptor.RequestSchema == "#/$defs/emptyRequest" &&
                    descriptor.ResponseSchema == "#/$defs/projectWindowSnapshot" &&
                    descriptor.OperationKind == AutomationOperationKind.Read &&
                    descriptor.UiCommandIds.Contains("panel.project.search", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.ProjectWindowSetMethod &&
                    descriptor.RequestSchema == "#/$defs/projectWindowSetRequest" &&
                    descriptor.ResponseSchema == "#/$defs/projectWindowSnapshot" &&
                    descriptor.OperationKind == AutomationOperationKind.Command &&
                    descriptor.TransactionMode == AutomationTransactionMode.Forbidden &&
                    descriptor.RequiredScopes.Contains(AutomationScopes.EditorControl, StringComparer.Ordinal) &&
                    descriptor.SupportedModes.SequenceEqual(["edit", "play", "paused"]) &&
                    descriptor.UiCommandIds.Contains("panel.project.thumbnail-size", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.ProjectCodeOpenMethod &&
                    descriptor.RequestSchema == "#/$defs/emptyRequest" &&
                    descriptor.ResponseSchema == "#/$defs/codeProjectOpenResult" &&
                    descriptor.OperationKind == AutomationOperationKind.Command &&
                    descriptor.TransactionMode == AutomationTransactionMode.Forbidden &&
                    descriptor.UsesBackgroundPreparation &&
                    descriptor.RequiredScopes.Contains(AutomationScopes.EditorControl, StringComparer.Ordinal) &&
                    descriptor.RequiredScopes.Contains(AutomationScopes.ProjectWrite, StringComparer.Ordinal) &&
                    descriptor.UiCommandIds.Contains("menu.assets.open-csharp-project", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.ProjectAssetReplaceMethod &&
                    descriptor.RequestSchema == "#/$defs/assetReplaceRequest" &&
                    descriptor.OperationKind == AutomationOperationKind.Write &&
                    descriptor.TransactionMode == AutomationTransactionMode.Optional &&
                    descriptor.UsesBackgroundPreparation &&
                    !descriptor.UiCommandIds.Contains("panel.inspector.asset.edit", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.ProjectUiManifestGetMethod &&
                    descriptor.ResponseSchema == "#/$defs/uiManifestSnapshot" &&
                    descriptor.OperationKind == AutomationOperationKind.Read &&
                    descriptor.UsesBackgroundPreparation);
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.ProjectUiManifestSyncMethod &&
                    descriptor.RequestSchema == "#/$defs/emptyRequest" &&
                    descriptor.TransactionMode == AutomationTransactionMode.Optional &&
                    descriptor.UsesBackgroundPreparation &&
                    descriptor.UiCommandIds.Contains("panel.ui-manifest.sync", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.ProjectUiManifestPreloadSetMethod &&
                    descriptor.RequestSchema == "#/$defs/uiManifestPreloadSetRequest" &&
                    descriptor.UiCommandIds.Contains("panel.ui-manifest.preload", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.ConsoleOptionsSetMethod &&
                    descriptor.RequestSchema == "#/$defs/consoleOptions" &&
                    descriptor.ResponseSchema == "#/$defs/consoleOptions" &&
                    descriptor.TransactionMode == AutomationTransactionMode.Forbidden &&
                    descriptor.SupportedModes.SequenceEqual(["edit", "play", "paused"]) &&
                    descriptor.UiCommandIds.Contains("panel.console.options", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.ConsoleSelectionGetMethod &&
                    descriptor.RequestSchema == "#/$defs/emptyRequest" &&
                    descriptor.ResponseSchema == "#/$defs/consoleSelectionSnapshot" &&
                    descriptor.OperationKind == AutomationOperationKind.Read);
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.ConsoleSelectionSetMethod &&
                    descriptor.RequestSchema == "#/$defs/consoleSelectionSetRequest" &&
                    descriptor.ResponseSchema == "#/$defs/consoleSelectionSnapshot" &&
                    descriptor.OperationKind == AutomationOperationKind.Command &&
                    descriptor.TransactionMode == AutomationTransactionMode.Forbidden &&
                    descriptor.RequiredScopes.Contains(AutomationScopes.EditorControl, StringComparer.Ordinal) &&
                    descriptor.UiCommandIds.Contains("panel.console.selection", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.ConsoleEntryCopyMethod &&
                    descriptor.RequestSchema == "#/$defs/consoleEntryRequest" &&
                    descriptor.ResponseSchema == "#/$defs/consoleCopyResult" &&
                    descriptor.OperationKind == AutomationOperationKind.Read &&
                    descriptor.UiCommandIds.Contains("panel.console.copy", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.ConsoleEntryOpenSourceMethod &&
                    descriptor.ResponseSchema == "#/$defs/consoleOpenSourceResult" &&
                    descriptor.OperationKind == AutomationOperationKind.Command &&
                    descriptor.UiCommandIds.Contains("panel.console.open-source", StringComparer.Ordinal) &&
                    descriptor.UiCommandIds.Contains("panel.console.double-click", StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.ProfilerVSyncSetMethod &&
                    descriptor.RequestSchema == "#/$defs/profilerVSyncSetRequest" &&
                    descriptor.ResponseSchema == "#/$defs/profilerSnapshot" &&
                    descriptor.ExecutionPhase == AutomationExecutionPhase.EngineInputAndTime &&
                    descriptor.UiCommandIds.Contains("panel.profiler.vsync", StringComparer.Ordinal));
                string[] preparedSettingsMethods =
                [
                    AutomationProtocolConstants.PreferencesSetMethod,
                    AutomationProtocolConstants.ProjectSettingsSetMethod,
                    AutomationProtocolConstants.PlayerSettingsSetMethod,
                    AutomationProtocolConstants.BuildSettingsSetMethod,
                ];
                Assert.All(preparedSettingsMethods, method =>
                    Assert.Contains(capabilities.Items, descriptor =>
                        descriptor.Id == method &&
                        descriptor.OperationKind == AutomationOperationKind.Write &&
                        descriptor.TransactionMode == AutomationTransactionMode.Optional &&
                        descriptor.UsesBackgroundPreparation));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.BuildStartMethod &&
                    descriptor.Domain == "build" &&
                    descriptor.RequestSchema == "#/$defs/buildStartRequest" &&
                    descriptor.ResponseSchema == "#/$defs/buildSnapshot" &&
                    descriptor.RequiredScopes.SequenceEqual([AutomationScopes.ProcessBuild]) &&
                    descriptor.SupportedModes.SequenceEqual(["edit"]) &&
                    descriptor.OperationKind == AutomationOperationKind.Command &&
                    descriptor.TransactionMode == AutomationTransactionMode.Forbidden &&
                    descriptor.RequiresExpectedRevision &&
                    descriptor.RequiresIdempotencyKey &&
                    descriptor.EventTypes.Contains(
                        AutomationProtocolConstants.BuildChangedEventType,
                        StringComparer.Ordinal) &&
                    descriptor.UiCommandIds.Contains(
                        "panel.build-settings.build-and-run",
                        StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.BuildWaitMethod &&
                    descriptor.OperationKind == AutomationOperationKind.Read &&
                    descriptor.UsesBackgroundPreparation &&
                    !descriptor.RequiresExpectedRevision &&
                    descriptor.RequiredScopes.SequenceEqual([AutomationScopes.ProcessBuild]));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.BuildLogExportMethod &&
                    descriptor.ResponseSchema == "#/$defs/artifactReference" &&
                    descriptor.ArtifactBehavior == AutomationArtifactBehavior.Required);
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.PlayerLaunchMethod &&
                    descriptor.Domain == "player" &&
                    descriptor.RequiredScopes.SequenceEqual([AutomationScopes.ProcessLaunch]) &&
                    descriptor.OperationKind == AutomationOperationKind.Command &&
                    descriptor.TransactionMode == AutomationTransactionMode.Forbidden &&
                    descriptor.EventTypes.Contains(
                        AutomationProtocolConstants.PlayerChangedEventType,
                        StringComparer.Ordinal));
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.PlayerWaitMethod &&
                    descriptor.OperationKind == AutomationOperationKind.Read &&
                    descriptor.UsesBackgroundPreparation);
                Assert.Contains(capabilities.Items, descriptor =>
                    descriptor.Id == AutomationProtocolConstants.PlayerTerminateMethod &&
                    descriptor.ResponseSchema == "#/$defs/playerProcessSnapshot" &&
                    descriptor.RequiresExpectedRevision &&
                    descriptor.RequiresIdempotencyKey);

                Task<AutomationInvocationResult> pickerGetPending = client.InvokeDetailedAsync(
                    AutomationProtocolConstants.WorkspaceProjectPickerGetMethod).AsTask();
                Assert.True(SpinWait.SpinUntil(
                    () => runtime.Scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
                    TimeSpan.FromSeconds(5)));
                runtime.DrainEditorIngress();
                AutomationInvocationResult pickerGet = pickerGetPending.GetAwaiter().GetResult();
                AutomationProjectPickerSnapshot pickerBefore = pickerGet.Payload?.Deserialize(
                    AutomationJsonContext.Default.AutomationProjectPickerSnapshot)
                    ?? throw new InvalidOperationException("workspace.project-picker.get 未返回 DTO。");
                AutomationProjectPickerSetRequest pickerTarget = new()
                {
                    Visible = false,
                    Mode = AutomationProjectPickerMode.OpenProject,
                };
                Task<AutomationInvocationResult> pickerSetPending = client.InvokeDetailedAsync(
                    AutomationProtocolConstants.WorkspaceProjectPickerSetMethod,
                    JsonSerializer.SerializeToElement(
                        pickerTarget,
                        AutomationJsonContext.Default.AutomationProjectPickerSetRequest),
                    new AutomationInvocationOptions
                    {
                        ExpectedRevision = ToPrecondition(pickerGet.Revision),
                        IdempotencyKey = "set-project-picker",
                    }).AsTask();
                Assert.True(SpinWait.SpinUntil(
                    () => runtime.Scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
                    TimeSpan.FromSeconds(5)));
                runtime.DrainEditorIngress();
                AutomationInvocationResult pickerSet = pickerSetPending.GetAwaiter().GetResult();
                Assert.Equal(
                    new AutomationProjectPickerSnapshot
                    {
                        Visible = false,
                        Mode = AutomationProjectPickerMode.OpenProject,
                    },
                    pickerSet.Payload?.Deserialize(AutomationJsonContext.Default.AutomationProjectPickerSnapshot));

                Task<AutomationInvocationResult> pickerNoChangePending = client.InvokeDetailedAsync(
                    AutomationProtocolConstants.WorkspaceProjectPickerSetMethod,
                    JsonSerializer.SerializeToElement(
                        pickerTarget,
                        AutomationJsonContext.Default.AutomationProjectPickerSetRequest),
                    new AutomationInvocationOptions
                    {
                        ExpectedRevision = ToPrecondition(pickerSet.Revision),
                        IdempotencyKey = "set-project-picker-no-change",
                    }).AsTask();
                Assert.True(SpinWait.SpinUntil(
                    () => runtime.Scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
                    TimeSpan.FromSeconds(5)));
                runtime.DrainEditorIngress();
                AutomationInvocationResult pickerNoChange = pickerNoChangePending.GetAwaiter().GetResult();
                Assert.Equal(pickerSet.Revision?.GlobalRevision, pickerNoChange.Revision?.GlobalRevision);

                Task<AutomationInvocationResult> pickerRestorePending = client.InvokeDetailedAsync(
                    AutomationProtocolConstants.WorkspaceProjectPickerSetMethod,
                    JsonSerializer.SerializeToElement(
                        new AutomationProjectPickerSetRequest
                        {
                            Visible = pickerBefore.Visible,
                            Mode = pickerBefore.Mode,
                        },
                        AutomationJsonContext.Default.AutomationProjectPickerSetRequest),
                    new AutomationInvocationOptions
                    {
                        ExpectedRevision = ToPrecondition(pickerNoChange.Revision),
                        IdempotencyKey = "restore-project-picker",
                    }).AsTask();
                Assert.True(SpinWait.SpinUntil(
                    () => runtime.Scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
                    TimeSpan.FromSeconds(5)));
                runtime.DrainEditorIngress();
                _ = pickerRestorePending.GetAwaiter().GetResult();

                Task<AutomationInvocationResult> consoleOptionsGetPending = client.InvokeDetailedAsync(
                    AutomationProtocolConstants.ConsoleOptionsGetMethod).AsTask();
                Assert.True(SpinWait.SpinUntil(
                    () => runtime.Scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
                    TimeSpan.FromSeconds(5)));
                runtime.DrainEditorIngress();
                AutomationInvocationResult consoleOptionsGet = consoleOptionsGetPending.GetAwaiter().GetResult();
                AutomationConsoleOptions originalConsoleOptions = consoleOptionsGet.Payload?.Deserialize(
                    AutomationJsonContext.Default.AutomationConsoleOptions)
                    ?? throw new InvalidOperationException("console.options.get 未返回 DTO。");
                AutomationConsoleOptions targetConsoleOptions = originalConsoleOptions with
                {
                    Search = "runtime",
                    Collapse = !originalConsoleOptions.Collapse,
                    ErrorPause = !originalConsoleOptions.ErrorPause,
                };
                Task<AutomationInvocationResult> consoleOptionsSetPending = client.InvokeDetailedAsync(
                    AutomationProtocolConstants.ConsoleOptionsSetMethod,
                    JsonSerializer.SerializeToElement(
                        targetConsoleOptions,
                        AutomationJsonContext.Default.AutomationConsoleOptions),
                    new AutomationInvocationOptions
                    {
                        ExpectedRevision = ToPrecondition(consoleOptionsGet.Revision),
                        IdempotencyKey = "set-console-options",
                    }).AsTask();
                Assert.True(SpinWait.SpinUntil(
                    () => runtime.Scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
                    TimeSpan.FromSeconds(5)));
                runtime.DrainEditorIngress();
                AutomationInvocationResult consoleOptionsSet = consoleOptionsSetPending.GetAwaiter().GetResult();
                Assert.Equal("runtime", app.ConsoleOptions.Capture().Search);
                Assert.Equal(
                    targetConsoleOptions,
                    consoleOptionsSet.Payload?.Deserialize(AutomationJsonContext.Default.AutomationConsoleOptions));

                Task<AutomationInvocationResult> consoleOptionsNoChangePending = client.InvokeDetailedAsync(
                    AutomationProtocolConstants.ConsoleOptionsSetMethod,
                    JsonSerializer.SerializeToElement(
                        targetConsoleOptions,
                        AutomationJsonContext.Default.AutomationConsoleOptions),
                    new AutomationInvocationOptions
                    {
                        ExpectedRevision = ToPrecondition(consoleOptionsSet.Revision),
                        IdempotencyKey = "set-console-options-no-change",
                    }).AsTask();
                Assert.True(SpinWait.SpinUntil(
                    () => runtime.Scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
                    TimeSpan.FromSeconds(5)));
                runtime.DrainEditorIngress();
                AutomationInvocationResult consoleOptionsNoChange = consoleOptionsNoChangePending
                    .GetAwaiter().GetResult();
                Assert.Equal(
                    consoleOptionsSet.Revision?.GlobalRevision,
                    consoleOptionsNoChange.Revision?.GlobalRevision);

                app.ConsoleStore.Add(new EditorConsoleEntry(
                    DateTimeOffset.UtcNow,
                    EditorConsoleCategory.Script,
                    EditorConsoleSeverity.Error,
                    "runtime-selection-test",
                    "script failed",
                    "stack line",
                    "ScriptSource/Test.cs",
                    Line: 12,
                    Column: 3));
                Task<AutomationInvocationResult> consoleListPending = client.InvokeDetailedAsync(
                    AutomationProtocolConstants.ConsoleListMethod,
                    JsonSerializer.SerializeToElement(
                        new AutomationPageRequest { PageSize = 100 },
                        AutomationJsonContext.Default.AutomationPageRequest)).AsTask();
                Assert.True(SpinWait.SpinUntil(
                    () => runtime.Scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
                    TimeSpan.FromSeconds(5)));
                runtime.DrainEditorIngress();
                AutomationInvocationResult consoleListResult = consoleListPending.GetAwaiter().GetResult();
                AutomationConsoleEntry consoleEntry = Assert.Single(
                    consoleListResult.Payload?.Deserialize(
                        AutomationJsonContext.Default.AutomationConsoleListResponse)?.Items ?? [],
                    entry => string.Equals(entry.Source, "runtime-selection-test", StringComparison.Ordinal));
                AutomationConsoleSelectionSetRequest selectRequest = new() { EntryId = consoleEntry.EntryId };
                Task<AutomationInvocationResult> selectPending = client.InvokeDetailedAsync(
                    AutomationProtocolConstants.ConsoleSelectionSetMethod,
                    JsonSerializer.SerializeToElement(
                        selectRequest,
                        AutomationJsonContext.Default.AutomationConsoleSelectionSetRequest),
                    new AutomationInvocationOptions
                    {
                        ExpectedRevision = ToPrecondition(consoleListResult.Revision),
                        IdempotencyKey = "console-select-entry",
                    }).AsTask();
                Assert.True(SpinWait.SpinUntil(
                    () => runtime.Scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
                    TimeSpan.FromSeconds(5)));
                runtime.DrainEditorIngress();
                AutomationInvocationResult selectedResult = selectPending.GetAwaiter().GetResult();
                AutomationConsoleSelectionSnapshot selected = selectedResult.Payload?.Deserialize(
                    AutomationJsonContext.Default.AutomationConsoleSelectionSnapshot)
                    ?? throw new InvalidOperationException("console.selection.set 未返回 DTO。");
                Assert.Equal(consoleEntry.EntryId, selected.EntryId);
                Assert.Equal(consoleEntry, selected.Entry);

                Task<AutomationInvocationResult> copyPending = client.InvokeDetailedAsync(
                    AutomationProtocolConstants.ConsoleEntryCopyMethod,
                    JsonSerializer.SerializeToElement(
                        new AutomationConsoleEntryRequest { EntryId = consoleEntry.EntryId },
                        AutomationJsonContext.Default.AutomationConsoleEntryRequest)).AsTask();
                Assert.True(SpinWait.SpinUntil(
                    () => runtime.Scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
                    TimeSpan.FromSeconds(5)));
                runtime.DrainEditorIngress();
                AutomationConsoleCopyResult copied = copyPending.GetAwaiter().GetResult().Payload?.Deserialize(
                    AutomationJsonContext.Default.AutomationConsoleCopyResult)
                    ?? throw new InvalidOperationException("console.entry.copy 未返回 DTO。");
                Assert.Contains("script failed", copied.Text, StringComparison.Ordinal);
                Assert.Contains("ScriptSource/Test.cs:12:3", copied.Text, StringComparison.Ordinal);

                Task<AutomationInvocationResult> selectNoChangePending = client.InvokeDetailedAsync(
                    AutomationProtocolConstants.ConsoleSelectionSetMethod,
                    JsonSerializer.SerializeToElement(
                        selectRequest,
                        AutomationJsonContext.Default.AutomationConsoleSelectionSetRequest),
                    new AutomationInvocationOptions
                    {
                        ExpectedRevision = ToPrecondition(selectedResult.Revision),
                        IdempotencyKey = "console-select-entry-no-change",
                    }).AsTask();
                Assert.True(SpinWait.SpinUntil(
                    () => runtime.Scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
                    TimeSpan.FromSeconds(5)));
                runtime.DrainEditorIngress();
                AutomationInvocationResult selectionNoChange = selectNoChangePending.GetAwaiter().GetResult();
                Assert.Equal(
                    selectedResult.Revision?.GlobalRevision,
                    selectionNoChange.Revision?.GlobalRevision);

                Task<AutomationSubscriptionInfo> artifactSubscriptionPending = client.SubscribeEventsAsync(
                    new AutomationEventSubscribeRequest
                    {
                        SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                        SubscriptionKey = "shell-artifact-events",
                        EventTypes =
                        [
                            AutomationProtocolConstants.ConsoleChangedEventType,
                            AutomationProtocolConstants.ArtifactChangedEventType,
                        ],
                        BacklogLimit = 8,
                    }).AsTask();
                Assert.True(SpinWait.SpinUntil(
                    () => runtime.Scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
                    TimeSpan.FromSeconds(5)));
                runtime.DrainEditorIngress();
                _ = artifactSubscriptionPending.GetAwaiter().GetResult();

                using CancellationTokenSource eventTimeout = new(TimeSpan.FromSeconds(5));
                IAsyncEnumerator<AutomationEventRecord> eventReader = client
                    .ReadEventsAsync(eventTimeout.Token)
                    .GetAsyncEnumerator(eventTimeout.Token);
                try
                {
                    app.ConsoleStore.Add(new EditorConsoleEntry(
                        DateTimeOffset.UtcNow,
                        EditorConsoleCategory.General,
                        EditorConsoleSeverity.Info,
                        "automation-test",
                        "console event"));
                    Assert.True(eventReader.MoveNextAsync().AsTask().GetAwaiter().GetResult());
                    AutomationEventRecord consoleEvent = eventReader.Current;
                    Assert.Equal(AutomationProtocolConstants.ConsoleChangedEventType, consoleEvent.EventType);
                    AutomationStateChangedEvent consoleChanged = consoleEvent.Payload?.Deserialize(
                        AutomationJsonContext.Default.AutomationStateChangedEvent)
                        ?? throw new InvalidOperationException("console changed event 缺少 payload。");
                    Assert.Equal("console.entry.added", consoleChanged.Method);

                    EditorConsoleOptionsSnapshot manualOptions = app.ConsoleOptions.Capture() with
                    {
                        AutoScroll = !app.ConsoleOptions.Capture().AutoScroll,
                    };
                    Assert.True(app.ConsoleOptions.Apply(manualOptions));
                    Assert.True(eventReader.MoveNextAsync().AsTask().GetAwaiter().GetResult());
                    AutomationEventRecord optionsEvent = eventReader.Current;
                    Assert.Equal(AutomationProtocolConstants.ConsoleChangedEventType, optionsEvent.EventType);
                    AutomationStateChangedEvent optionsChanged = optionsEvent.Payload?.Deserialize(
                        AutomationJsonContext.Default.AutomationStateChangedEvent)
                        ?? throw new InvalidOperationException("console options changed event 缺少 payload。");
                    Assert.Equal("console.options.changed", optionsChanged.Method);
                    Assert.Contains("editor:console:options", optionsChanged.ResourceIds, StringComparer.Ordinal);

                    Task<AutomationInvocationResult> exportPending = client.InvokeDetailedAsync(
                        AutomationProtocolConstants.ConsoleExportMethod).AsTask();
                    Assert.True(SpinWait.SpinUntil(
                        () => runtime.Scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
                        TimeSpan.FromSeconds(5)));
                    runtime.DrainEditorIngress();
                    AutomationInvocationResult exportResult = exportPending.GetAwaiter().GetResult();
                    AutomationArtifactReference artifact = exportResult.Payload?.Deserialize(
                        AutomationJsonContext.Default.AutomationArtifactReference)
                        ?? throw new InvalidOperationException("console.export 未返回 artifactReference。");
                    Assert.True(eventReader.MoveNextAsync().AsTask().GetAwaiter().GetResult());
                    AutomationEventRecord createdEvent = eventReader.Current;
                    Assert.Equal(AutomationProtocolConstants.ArtifactChangedEventType, createdEvent.EventType);
                    Assert.Equal(exportResult.Revision?.GlobalRevision, createdEvent.StateRevision.GlobalRevision);
                    AutomationStateChangedEvent created = createdEvent.Payload?.Deserialize(
                        AutomationJsonContext.Default.AutomationStateChangedEvent)
                        ?? throw new InvalidOperationException("artifact created event 缺少 payload。");
                    Assert.Equal(AutomationProtocolConstants.ConsoleExportMethod, created.Method);
                    Assert.Equal("created", created.ChangeKind);
                    Assert.Contains(created.ResourceIds, resource =>
                        resource.EndsWith(':' + artifact.ArtifactId, StringComparison.Ordinal));

                    Task<AutomationInvocationResult> deletePending = client.InvokeDetailedAsync(
                        AutomationProtocolConstants.ArtifactDeleteMethod,
                        JsonSerializer.SerializeToElement(
                            new AutomationArtifactRequest
                            {
                                SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                                ArtifactId = artifact.ArtifactId,
                            },
                            AutomationJsonContext.Default.AutomationArtifactRequest),
                        new AutomationInvocationOptions
                        {
                            IdempotencyKey = "delete-shell-artifact",
                        }).AsTask();
                    Assert.True(SpinWait.SpinUntil(
                        () => runtime.Scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
                        TimeSpan.FromSeconds(5)));
                    runtime.DrainEditorIngress();
                    AutomationArtifactDeleteResult deleted = deletePending.GetAwaiter().GetResult()
                        .Payload?.Deserialize(AutomationJsonContext.Default.AutomationArtifactDeleteResult)
                        ?? throw new InvalidOperationException("artifact.delete 未返回结果。");
                    Assert.True(deleted.Deleted);
                    Assert.True(eventReader.MoveNextAsync().AsTask().GetAwaiter().GetResult());
                    AutomationStateChangedEvent deletedEvent = eventReader.Current.Payload?.Deserialize(
                        AutomationJsonContext.Default.AutomationStateChangedEvent)
                        ?? throw new InvalidOperationException("artifact deleted event 缺少 payload。");
                    Assert.Equal(AutomationProtocolConstants.ArtifactDeleteMethod, deletedEvent.Method);
                    Assert.Equal("deleted", deletedEvent.ChangeKind);
                    Assert.False(File.Exists(artifact.Path));
                }
                finally
                {
                    eventReader.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }

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

                Task<AutomationInvocationResult> preferencesGetPending = client.InvokeDetailedAsync(
                    AutomationProtocolConstants.PreferencesGetMethod).AsTask();
                Assert.True(SpinWait.SpinUntil(
                    () => runtime.Scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
                    TimeSpan.FromSeconds(5)));
                runtime.DrainEditorIngress();
                AutomationInvocationResult preferencesGet = preferencesGetPending.GetAwaiter().GetResult();
                AutomationEditorPreferences beforePreferences = preferencesGet.Payload?.Deserialize(
                    AutomationJsonContext.Default.AutomationEditorPreferences)
                    ?? throw new InvalidOperationException("preferences.get 未返回 DTO。");
                AutomationEditorPreferences targetPreferences = beforePreferences with
                {
                    UiScale = beforePreferences.UiScale == 1.25f ? 1.5f : 1.25f,
                };
                Task<AutomationInvocationResult> preferencesSetPending = client.InvokeDetailedAsync(
                    AutomationProtocolConstants.PreferencesSetMethod,
                    JsonSerializer.SerializeToElement(
                        targetPreferences,
                        AutomationJsonContext.Default.AutomationEditorPreferences),
                    new AutomationInvocationOptions
                    {
                        ExpectedRevision = ToPrecondition(preferencesGet.Revision),
                        IdempotencyKey = "preferences-without-project",
                    }).AsTask();
                Assert.True(SpinWait.SpinUntil(
                    () =>
                    {
                        if (runtime.Scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress))
                        {
                            runtime.DrainEditorIngress();
                        }

                        return preferencesSetPending.IsCompleted;
                    },
                    TimeSpan.FromSeconds(5)));
                AutomationInvocationResult preferencesSet = preferencesSetPending.GetAwaiter().GetResult();
                Assert.Equal(targetPreferences.UiScale, app.Preferences.Current.UiScale);
                Task<AutomationInvocationResult> preferencesNoChangePending = client.InvokeDetailedAsync(
                    AutomationProtocolConstants.PreferencesSetMethod,
                    JsonSerializer.SerializeToElement(
                        targetPreferences,
                        AutomationJsonContext.Default.AutomationEditorPreferences),
                    new AutomationInvocationOptions
                    {
                        ExpectedRevision = ToPrecondition(preferencesSet.Revision),
                        IdempotencyKey = "preferences-without-project-no-change",
                    }).AsTask();
                Assert.True(SpinWait.SpinUntil(
                    () =>
                    {
                        if (runtime.Scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress))
                        {
                            runtime.DrainEditorIngress();
                        }

                        return preferencesNoChangePending.IsCompleted;
                    },
                    TimeSpan.FromSeconds(5)));
                AutomationInvocationResult preferencesNoChange =
                    preferencesNoChangePending.GetAwaiter().GetResult();
                Assert.Equal(
                    preferencesSet.Revision?.GlobalRevision,
                    preferencesNoChange.Revision?.GlobalRevision);

                Task<AutomationInvocationResult> undoPending = client.InvokeDetailedAsync(
                    AutomationProtocolConstants.EditorHistoryUndoMethod,
                    payload: null,
                    new AutomationInvocationOptions
                    {
                        ExpectedRevision = ToPrecondition(preferencesNoChange.Revision),
                        IdempotencyKey = "undo-preferences-without-project",
                    }).AsTask();
                Assert.True(SpinWait.SpinUntil(
                    () => runtime.Scheduler.HasPendingWork(AutomationExecutionPhase.EditorIngress),
                    TimeSpan.FromSeconds(5)));
                runtime.DrainEditorIngress();
                _ = undoPending.GetAwaiter().GetResult();
                Assert.Equal(beforePreferences.UiScale, app.Preferences.Current.UiScale);
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

    /// <summary>Runtime save slot 扫描在后台只接受稳定 manifest，并完整映射其摘要。</summary>
    [Fact]
    public void RuntimeSaveSlotListingReadsStableManifestAndHonorsCancellation()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "PixelEngine",
            "AutomationSaveSlotTests",
            Guid.NewGuid().ToString("N"));
        string slotDirectory = Path.Combine(root, "slot-a");
        _ = Directory.CreateDirectory(slotDirectory);
        try
        {
            WorldManifest manifest = new(
                SaveFormatVersions.WorldManifest,
                0x1234UL,
                5678,
                [],
                new MaterialNameTable([(0, "empty")]),
                [],
                [],
                [new ChunkCoord(-2, 3), new ChunkCoord(4, 5)]);
            ArrayBufferWriter<byte> buffer = new();
            new ManifestCodec().Encode(manifest, buffer);
            string manifestPath = Path.Combine(slotDirectory, "manifest.bin");
            File.WriteAllBytes(manifestPath, buffer.WrittenSpan);

            SaveSlotInfo slot = Assert.Single(
                EditorWorldSaveLoadService.ListSaveSlots(root, CancellationToken.None));

            Assert.Equal("slot-a", slot.Id);
            Assert.Equal(slotDirectory, slot.Path);
            Assert.Equal(File.GetLastWriteTimeUtc(manifestPath), slot.TimestampUtc.UtcDateTime);
            Assert.Equal(SaveFormatVersions.WorldManifest, slot.FormatVersion);
            Assert.Equal(0x1234UL, slot.WorldSeed);
            Assert.Equal(5678, slot.GameTimeTicks);
            Assert.Equal(2, slot.ChunkCount);

            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            _ = Assert.Throws<OperationCanceledException>(() =>
                EditorWorldSaveLoadService.ListSaveSlots(root, cancellation.Token));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Runtime component ID 只绑定 play entity 与 Behaviour type，不依赖快照数组顺序。</summary>
    [Fact]
    public void RuntimeComponentIdsAreTypeDerivedAndOrderIndependent()
    {
        const string EntityId = "play:session-1:entity:42";
        string first = EditorAutomationAuthoringApi.CreateRuntimeComponentId(
            EntityId,
            "Game.PlayerMovementBehaviour");
        string repeated = EditorAutomationAuthoringApi.CreateRuntimeComponentId(
            EntityId,
            "Game.PlayerMovementBehaviour");
        string otherType = EditorAutomationAuthoringApi.CreateRuntimeComponentId(
            EntityId,
            "Game.WeaponBehaviour");
        string otherEntity = EditorAutomationAuthoringApi.CreateRuntimeComponentId(
            "play:session-1:entity:43",
            "Game.PlayerMovementBehaviour");

        Assert.Equal(first, repeated);
        Assert.NotEqual(first, otherType);
        Assert.NotEqual(first, otherEntity);
        Assert.StartsWith(EntityId + ":component:", first, StringComparison.Ordinal);
        Assert.Equal(64, first[(first.LastIndexOf(':') + 1)..].Length);
    }

    /// <summary>手动与 API Play 路径必须对同一 runtime resource ID 和 domain event 达成一致。</summary>
    [Fact]
    public void RuntimeResourceIdentityAndManualRoutingAreCanonical()
    {
        const string PlaySessionId = "session-1";
        string resource = EditorAutomationRuntime.CreateRuntimeResourceId(PlaySessionId);
        Assert.Equal("play:session-1:runtime", resource);
        Assert.Contains(
            AutomationProtocolConstants.RuntimeChangedEventType,
            EditorAutomationEventRouting.ForManualMutation(
                "runtime.simulation.changed",
                ["editor:simulation", resource]),
            StringComparer.Ordinal);
        Assert.Contains(
            AutomationProtocolConstants.ProfilerChangedEventType,
            EditorAutomationEventRouting.ForCapability(
                AutomationProtocolConstants.RuntimeSimulationSetMethod,
                "runtime"),
            StringComparer.Ordinal);
        Assert.Contains(
            AutomationProtocolConstants.RuntimeChangedEventType,
            EditorAutomationEventRouting.ForManualMutation(
                "runtime.physics.changed",
                ["editor:runtime:physics", resource]),
            StringComparer.Ordinal);
        Assert.Contains(
            AutomationProtocolConstants.ProfilerChangedEventType,
            EditorAutomationEventRouting.ForCapability(
                AutomationProtocolConstants.RuntimeLightingSetMethod,
                "runtime"),
            StringComparer.Ordinal);
    }

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

    /// <summary>checked-in capability matrix 必须与当前 production semantic/UI registry 同源。</summary>
    [Fact]
    public async Task PublishedCapabilityMatrixMatchesProductionRegistry()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "PixelEngine",
            "AutomationMatrixTests",
            Guid.NewGuid().ToString("N"));
        try
        {
            EditorShellApp app = EditorShellApp.CreateForTests();
            await using AutomationArtifactStore artifacts = new(new AutomationArtifactStoreOptions
            {
                RootPath = root,
            });
            using EditorAutomationAuthoringApi api = new(app, artifacts, []);
            using AutomationMainThreadScheduler scheduler = new(
                api.CreateRegistrations(),
                new AutomationRevisionStore(),
                new NoopUndoSink(),
                new NoopTransactionParticipant(),
                uiCommands: EditorUiCommandCatalog.CreateRegistrations());
            AutomationCapabilityMatrixSnapshot production = scheduler.CaptureCapabilityMatrix();
            AutomationCapabilityMatrixSnapshot published = JsonSerializer.Deserialize(
                File.ReadAllBytes(FindRepositoryFile(
                    "schema/editor-automation-capabilities.v1.json")),
                AutomationJsonContext.Default.AutomationCapabilityMatrixSnapshot)
                ?? throw new InvalidOperationException("发布 capability matrix 返回 null。");

            Assert.Equal(production.CapabilityDigest, published.CapabilityDigest);
            Assert.Equal(production.UiCommandDigest, published.UiCommandDigest);
            Assert.Equal(production.MatrixDigest, published.MatrixDigest);
            Assert.Equal(production.Capabilities.Length, published.Capabilities.Length);
            Assert.Equal(production.UiCommands.Length, published.UiCommands.Length);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>空闲 phase hook 只做原子 pending 读取，十万次调用保持当前线程零托管分配。</summary>
    [Fact]
    public void IdlePhaseDriverHasZeroManagedAllocation()
    {
        using AutomationMainThreadScheduler scheduler = new(
            [],
            new AutomationRevisionStore(),
            new NoopUndoSink(),
            new NoopTransactionParticipant());
        EditorAutomationPhaseDriver driver = new(scheduler);
        EngineTickContext context = default;
        for (int i = 0; i < 1024; i++)
        {
            driver.Drain(context);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100_000; i++)
        {
            driver.Drain(context);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.False(scheduler.HasPendingWork(AutomationExecutionPhase.EngineInputAndTime));
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

    private static AutomationRevisionPrecondition ToPrecondition(AutomationRevisionSnapshot? revision)
    {
        AutomationRevisionSnapshot actual = revision ??
            throw new InvalidOperationException("Automation response 缺少 revision。");
        return new AutomationRevisionPrecondition
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            GlobalRevision = actual.GlobalRevision,
            Resources =
            [
                .. actual.Resources.Select(static resource => new AutomationExpectedResourceRevision
                {
                    SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
                    ResourceId = resource.ResourceId,
                    Revision = resource.Revision,
                }),
            ],
        };
    }
}
