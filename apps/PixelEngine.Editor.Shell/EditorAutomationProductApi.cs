using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Editor.Automation.Protocol;
using PixelEngine.Editor.Automation.Server;
using PixelEngine.Gui;
using PixelEngine.Editor.Shell.Settings;
using PixelEngine.Hosting;
using PixelEngine.Physics;
using PixelEngine.Scripting;
using PixelEngine.Rendering;
using PixelEngine.Simulation;
using PixelEngine.UI;
using PixelEngine.World;

namespace PixelEngine.Editor.Shell;

internal sealed partial class EditorAutomationAuthoringApi
{
    private const string AssetsResource = "editor:project:assets";
    private const string ProjectWindowResource = "editor:project:window";
    private const string UiManifestResource = "editor:project:ui-manifest";
    private const string ConsoleResource = "editor:console";
    private const string ConsoleOptionsResource = "editor:console:options";
    private const string ConsoleSelectionResource = "editor:console:selection";
    private const string PlayResource = "editor:play";
    private const string SimulationResource = "editor:simulation";
    private const string RuntimeWorldResource = "editor:runtime:world";
    private const string WorldInspectorResource = "editor:runtime:world-inspector";
    private const string RuntimeMaterialsResource = "editor:runtime:materials";
    private const string PhysicsResource = "editor:runtime:physics";
    private const string ParticlesResource = "editor:runtime:particles";
    private const string LightingResource = "editor:runtime:lighting";
    private const string RuntimeSavesResource = "editor:runtime:saves";
    private const string GamePresentationResource = "editor:game:presentation";
    private const string ProfilerResource = "editor:profiler";
    private const string DebugOverlayResource = "editor:debug-overlays";
    private const string PreferencesResource = "editor:preferences";
    private const string ProjectSettingsResource = "editor:project-settings";
    private const string PlayerSettingsResource = "editor:player-settings";
    private const string BuildSettingsResource = "editor:build-settings";

    private AutomationMethodRegistration[] CreateProductRegistrations()
    {
        return
        [
            ProductRead(
                AutomationProtocolConstants.WorkspaceRecentListMethod,
                "workspace",
                "pageRequest",
                "recentProjectListResponse",
                ["project-picker.recent"],
                ListRecentProjects,
                preparation: PrepareRecentProjectsList),
            ProductCommand(
                AutomationProtocolConstants.WorkspaceRecentFavoriteSetMethod,
                "workspace",
                "recentProjectFavoriteSetRequest",
                "recentProjectMutationResult",
                [AutomationScopes.SettingsWrite],
                AllModes,
                ["project-picker.recent.favorite"],
                CommitPreparedRecentProjectsMutation,
                preparation: PrepareRecentProjectFavorite),
            ProductCommand(
                AutomationProtocolConstants.WorkspaceRecentRemoveMethod,
                "workspace",
                "recentProjectRemoveRequest",
                "recentProjectMutationResult",
                [AutomationScopes.SettingsWrite],
                AllModes,
                ["project-picker.recent.remove"],
                CommitPreparedRecentProjectsMutation,
                preparation: PrepareRecentProjectRemove),
            ProductRead(
                AutomationProtocolConstants.ProjectGetMethod,
                "project",
                "emptyRequest",
                "projectSnapshot",
                ["panel.project", "menu.file.project"],
                GetProject),
            ProductRead(
                AutomationProtocolConstants.ProjectAssetListMethod,
                "project",
                "pageRequest",
                "assetListResponse",
                ["panel.project.assets"],
                ListAssets),
            ProductCommand(
                AutomationProtocolConstants.ProjectAssetRefreshMethod,
                "project",
                "emptyRequest",
                "assetRefreshResult",
                [AutomationScopes.ProjectWrite],
                ["edit"],
                ["panel.project.refresh"],
                CommitPreparedAssetRefresh,
                preparation: PrepareAssetRefresh),
            ProductRead(
                AutomationProtocolConstants.ProjectFolderListMethod,
                "project",
                "pageRequest",
                "folderListResponse",
                ["panel.project.folders"],
                ListFolders),
            ProductRead(
                AutomationProtocolConstants.ProjectAssetGetMethod,
                "project",
                "assetRequest",
                "assetInfo",
                ["panel.project.selection", "panel.inspector.asset"],
                GetAsset),
            ProductRead(
                AutomationProtocolConstants.ProjectSelectionGetMethod,
                "project",
                "emptyRequest",
                "projectSelectionSnapshot",
                ["panel.project.selection", "panel.inspector.asset"],
                GetProjectSelection),
            ProductCommand(
                AutomationProtocolConstants.ProjectSelectionSetMethod,
                "project",
                "projectSelectionSetRequest",
                "projectSelectionSnapshot",
                [AutomationScopes.EditorControl],
                AllModes,
                ["panel.project.selection", "panel.project.folder", "panel.inspector.asset"],
                SetProjectSelection),
            ProductRead(
                AutomationProtocolConstants.ProjectWindowGetMethod,
                "project",
                "emptyRequest",
                "projectWindowSnapshot",
                [
                    "panel.project.search",
                    "panel.project.kind-filter",
                    "panel.project.sort",
                    "panel.project.grid-view",
                    "panel.project.list-view",
                    "panel.project.thumbnail-size",
                ],
                GetProjectWindow),
            ProductCommand(
                AutomationProtocolConstants.ProjectWindowSetMethod,
                "project",
                "projectWindowSetRequest",
                "projectWindowSnapshot",
                [AutomationScopes.EditorControl],
                AllModes,
                [
                    "panel.project.search",
                    "panel.project.kind-filter",
                    "panel.project.sort",
                    "panel.project.grid-view",
                    "panel.project.list-view",
                    "panel.project.thumbnail-size",
                ],
                SetProjectWindow),
            ProductRead(
                AutomationProtocolConstants.ProjectAssetPreviewMethod,
                "project",
                "assetRequest",
                "assetPreviewResult",
                ["panel.inspector.asset.preview"],
                GetAssetPreview,
                AutomationArtifactBehavior.Required),
            ProductCommand(
                AutomationProtocolConstants.ProjectAssetScriptOpenMethod,
                "project",
                "scriptAssetOpenRequest",
                "assetActionResult",
                [AutomationScopes.EditorControl],
                AllModes,
                ["panel.project.open-script", "panel.inspector.asset.open-script"],
                OpenScriptAsset),
            ProductCommand(
                AutomationProtocolConstants.ProjectCodeOpenMethod,
                "project",
                "emptyRequest",
                "codeProjectOpenResult",
                [AutomationScopes.EditorControl, AutomationScopes.ProjectWrite],
                AllModes,
                ["menu.assets.open-csharp-project"],
                CommitPreparedCodeProjectOpen,
                preparation: PrepareCodeProjectOpen),
            ProductCommand(
                AutomationProtocolConstants.ProjectAssetAudioPreviewMethod,
                "project",
                "assetRequest",
                "assetActionResult",
                [AutomationScopes.EditorControl],
                AllModes,
                ["panel.project.preview-audio", "panel.inspector.asset.preview-audio"],
                PreviewAudioAsset),
            ProductRead(
                AutomationProtocolConstants.ProjectAssetReferencesMethod,
                "project",
                "assetReferenceListRequest",
                "assetReferenceListResponse",
                ["panel.project.delete-preflight", "panel.inspector.asset.references"],
                ListAssetReferences),
            ProductAssetWrite(
                AutomationProtocolConstants.ProjectAssetCreateMethod,
                "assetCreateRequest",
                "assetMutationResult",
                ["panel.project.create"],
                CommitPreparedCreateAsset,
                PrepareCreateAsset),
            ProductAssetWrite(
                AutomationProtocolConstants.ProjectAssetImportMethod,
                "assetImportRequest",
                "assetMutationResult",
                ["panel.project.import", "window.file-drop.import"],
                CommitPreparedImportAsset,
                PrepareImportAsset),
            ProductAssetWrite(
                AutomationProtocolConstants.ProjectAssetReplaceMethod,
                "assetReplaceRequest",
                "assetMutationResult",
                ["panel.project.open-external", "panel.inspector.asset.edit"],
                CommitPreparedReplaceAsset,
                PrepareReplaceAsset),
            ProductAssetWrite(
                AutomationProtocolConstants.ProjectAssetMoveMethod,
                "assetMoveRequest",
                "assetMutationResult",
                ["panel.project.move", "panel.project.rename", "panel.project.drag-drop"],
                CommitPreparedMoveAsset,
                PrepareMoveAsset),
            ProductAssetWrite(
                AutomationProtocolConstants.ProjectAssetDeleteMethod,
                "assetDeleteRequest",
                "assetMutationResult",
                ["panel.project.delete"],
                CommitPreparedDeleteAsset,
                PrepareDeleteAsset),
            ProductAssetWrite(
                AutomationProtocolConstants.ProjectFolderMoveMethod,
                "folderMoveRequest",
                "assetMutationResult",
                ["panel.project.folder.move", "panel.project.folder.rename"],
                CommitPreparedMoveFolder,
                PrepareMoveFolder),
            ProductAssetWrite(
                AutomationProtocolConstants.ProjectFolderDeleteMethod,
                "folderDeleteRequest",
                "assetMutationResult",
                ["panel.project.folder.delete"],
                CommitPreparedDeleteFolder,
                PrepareDeleteFolder),
            ProductRead(
                AutomationProtocolConstants.ProjectUiManifestGetMethod,
                "project",
                "emptyRequest",
                "uiManifestSnapshot",
                ["panel.ui-manifest"],
                GetUiManifest,
                preparation: PrepareUiManifestRead),
            ProductAssetWrite(
                AutomationProtocolConstants.ProjectUiManifestSyncMethod,
                "emptyRequest",
                "uiManifestSnapshot",
                ["panel.ui-manifest.sync"],
                CommitPreparedUiManifestSync,
                PrepareUiManifestSync),
            ProductAssetWrite(
                AutomationProtocolConstants.ProjectUiManifestPreloadSetMethod,
                "uiManifestPreloadSetRequest",
                "uiManifestSnapshot",
                ["panel.ui-manifest.preload"],
                CommitPreparedUiManifestPreload,
                PrepareUiManifestPreload),
            ProductRead(
                AutomationProtocolConstants.ConsoleListMethod,
                "console",
                "pageRequest",
                "consoleListResponse",
                ["panel.console"],
                ListConsole),
            ProductRead(
                AutomationProtocolConstants.ConsoleCountsGetMethod,
                "console",
                "emptyRequest",
                "consoleCounts",
                ["panel.console.toolbar"],
                GetConsoleCounts),
            ProductRead(
                AutomationProtocolConstants.ConsoleOptionsGetMethod,
                "console",
                "emptyRequest",
                "consoleOptions",
                ["panel.console.toolbar", "panel.console.options"],
                GetConsoleOptions),
            ProductCommand(
                AutomationProtocolConstants.ConsoleOptionsSetMethod,
                "console",
                "consoleOptions",
                "consoleOptions",
                [AutomationScopes.EditorControl],
                AllModes,
                ["panel.console.search", "panel.console.filters", "panel.console.options"],
                SetConsoleOptions),
            ProductCommand(
                AutomationProtocolConstants.ConsoleClearMethod,
                "console",
                "emptyRequest",
                "consoleCounts",
                [AutomationScopes.EditorControl],
                AllModes,
                ["panel.console.clear"],
                ClearConsole),
            ProductRead(
                AutomationProtocolConstants.ConsoleExportMethod,
                "console",
                "emptyRequest",
                "artifactReference",
                ["panel.console.export"],
                ExportConsole,
                AutomationArtifactBehavior.Required),
            ProductRead(
                AutomationProtocolConstants.ConsoleSelectionGetMethod,
                "console",
                "emptyRequest",
                "consoleSelectionSnapshot",
                ["panel.console.selection", "panel.console.details"],
                GetConsoleSelection),
            ProductCommand(
                AutomationProtocolConstants.ConsoleSelectionSetMethod,
                "console",
                "consoleSelectionSetRequest",
                "consoleSelectionSnapshot",
                [AutomationScopes.EditorControl],
                AllModes,
                ["panel.console.selection", "panel.console.details"],
                SetConsoleSelection),
            ProductRead(
                AutomationProtocolConstants.ConsoleEntryCopyMethod,
                "console",
                "consoleEntryRequest",
                "consoleCopyResult",
                ["panel.console.copy"],
                CopyConsoleEntry),
            ProductCommand(
                AutomationProtocolConstants.ConsoleEntryOpenSourceMethod,
                "console",
                "consoleEntryRequest",
                "consoleOpenSourceResult",
                [AutomationScopes.EditorControl],
                AllModes,
                ["panel.console.open-source", "panel.console.double-click"],
                OpenConsoleEntrySource),
            ProductRead(
                AutomationProtocolConstants.PlayGetMethod,
                "play",
                "emptyRequest",
                "playSnapshot",
                ["toolbar.play-controls", "panel.play-mode"],
                GetPlay),
            ProductCommand(
                AutomationProtocolConstants.PlayEnterMethod,
                "play",
                "playEnterRequest",
                "playCommandResult",
                [AutomationScopes.EditorControl],
                ["edit"],
                ["menu.play.play", "toolbar.play", "shortcut.ctrl-p"],
                EnterPlay),
            ProductCommand(
                AutomationProtocolConstants.PlayPauseMethod,
                "play",
                "emptyRequest",
                "playCommandResult",
                [AutomationScopes.EditorControl],
                ["play"],
                ["menu.play.pause", "toolbar.pause"],
                PausePlay),
            ProductCommand(
                AutomationProtocolConstants.PlayResumeMethod,
                "play",
                "emptyRequest",
                "playCommandResult",
                [AutomationScopes.EditorControl],
                ["paused"],
                ["menu.play.resume", "toolbar.pause"],
                ResumePlay),
            ProductCommand(
                AutomationProtocolConstants.PlayStepMethod,
                "play",
                "emptyRequest",
                "playCommandResult",
                [AutomationScopes.EditorControl],
                ["play", "paused"],
                ["menu.play.step", "toolbar.step"],
                StepPlay),
            ProductCommand(
                AutomationProtocolConstants.PlayStopMethod,
                "play",
                "emptyRequest",
                "playCommandResult",
                [AutomationScopes.EditorControl],
                ["play", "paused"],
                ["menu.play.stop", "toolbar.stop", "shortcut.ctrl-p"],
                StopPlay),
            ProductRead(
                AutomationProtocolConstants.RuntimeWorldGetMethod,
                "runtime",
                "emptyRequest",
                "runtimeWorldSnapshot",
                ["panel.game", "panel.profiler"],
                GetRuntimeWorld,
                phase: AutomationExecutionPhase.EngineInputAndTime,
                modes: ["play", "paused"]),
            ProductRead(
                AutomationProtocolConstants.RuntimeEntityListMethod,
                "runtime",
                "pageRequest",
                "runtimeEntityListResponse",
                ["panel.hierarchy.runtime"],
                ListRuntimeEntities,
                phase: AutomationExecutionPhase.EngineInputAndTime,
                modes: ["play", "paused"]),
            ProductRead(
                AutomationProtocolConstants.RuntimeEntityGetMethod,
                "runtime",
                "runtimeEntityRequest",
                "runtimeEntity",
                ["panel.inspector.runtime"],
                GetRuntimeEntity,
                phase: AutomationExecutionPhase.EngineInputAndTime,
                modes: ["play", "paused"]),
            ProductRead(
                AutomationProtocolConstants.RuntimeBodyListMethod,
                "runtime",
                "pageRequest",
                "runtimeBodyListResponse",
                ["panel.hierarchy.runtime-bodies"],
                ListRuntimeBodies,
                phase: AutomationExecutionPhase.EnginePhysicsSync,
                modes: ["play", "paused"]),
            ProductRead(
                AutomationProtocolConstants.RuntimeBodyGetMethod,
                "runtime",
                "runtimeBodyRequest",
                "runtimeBody",
                ["panel.inspector.runtime-body"],
                GetRuntimeBody,
                phase: AutomationExecutionPhase.EnginePhysicsSync,
                modes: ["play", "paused"]),
            ProductCommand(
                AutomationProtocolConstants.RuntimeEntityTransformSetMethod,
                "runtime",
                "runtimeTransformSetRequest",
                "runtimeEntity",
                [AutomationScopes.EditorControl],
                ["play", "paused"],
                ["panel.inspector.runtime.transform"],
                SetRuntimeTransform,
                AutomationExecutionPhase.EngineInputAndTime),
            ProductCommand(
                AutomationProtocolConstants.RuntimeComponentFieldSetMethod,
                "runtime",
                "runtimeComponentFieldSetRequest",
                "runtimeEntity",
                [AutomationScopes.EditorControl],
                ["play", "paused"],
                ["panel.inspector.runtime.field"],
                SetRuntimeComponentField,
                AutomationExecutionPhase.EngineInputAndTime),
            ProductRead(
                AutomationProtocolConstants.RuntimeSimulationGetMethod,
                "runtime",
                "emptyRequest",
                "runtimeSimulationSnapshot",
                ["panel.simulation"],
                GetRuntimeSimulation,
                phase: AutomationExecutionPhase.EngineInputAndTime),
            ProductCommand(
                AutomationProtocolConstants.RuntimeSimulationSetMethod,
                "runtime",
                "runtimeSimulationSetRequest",
                "runtimeSimulationSnapshot",
                [AutomationScopes.EditorControl],
                AllModes,
                ["panel.simulation.rate-30", "panel.simulation.rate-60"],
                SetRuntimeSimulation,
                AutomationExecutionPhase.EngineInputAndTime),
            ProductRead(
                AutomationProtocolConstants.RuntimeMaterialListMethod,
                "runtime",
                "pageRequest",
                "materialListResponse",
                ["panel.brush.material", "panel.materials.list"],
                ListRuntimeMaterials,
                phase: AutomationExecutionPhase.EngineInputAndTime),
            ProductRead(
                AutomationProtocolConstants.RuntimeMaterialGetMethod,
                "runtime",
                "materialRequest",
                "materialDefinition",
                ["panel.brush.material", "panel.materials.definition"],
                GetRuntimeMaterial,
                phase: AutomationExecutionPhase.EngineInputAndTime),
            ProductRead(
                AutomationProtocolConstants.MaterialEditorGetMethod,
                "materials",
                "emptyRequest",
                "materialEditorSnapshot",
                ["panel.materials"],
                GetMaterialEditor),
            ProductWrite(
                AutomationProtocolConstants.MaterialEditorSetMethod,
                "materials",
                "materialEditorSetRequest",
                "materialEditorSnapshot",
                ["panel.materials.edit", "panel.materials.add", "panel.materials.remove"],
                SetMaterialEditor,
                [AutomationScopes.EditorControl],
                ["edit"]),
            ProductWrite(
                AutomationProtocolConstants.MaterialEditorReloadMethod,
                "materials",
                "emptyRequest",
                "materialEditorSnapshot",
                ["panel.materials.reload"],
                ReloadMaterialEditor,
                [AutomationScopes.EditorControl],
                ["edit"],
                PrepareMaterialEditorReload),
            ProductWrite(
                AutomationProtocolConstants.MaterialEditorPreviewMethod,
                "materials",
                "emptyRequest",
                "materialEditorPreviewResult",
                ["panel.materials.preview"],
                PreviewMaterialEditor,
                [AutomationScopes.EditorControl],
                ["edit"]),
            ProductEngineWrite(
                AutomationProtocolConstants.MaterialEditorApplyMethod,
                "materials",
                "emptyRequest",
                "materialEditorApplyResult",
                ["panel.materials.apply"],
                ApplyMaterialEditor,
                PrepareMaterialEditorApply,
                ["edit"],
                [AutomationScopes.EditorControl, AutomationScopes.ProjectWrite]),
            ProductRead(
                AutomationProtocolConstants.RuntimeCellInspectMethod,
                "runtime",
                "runtimeCellInspectRequest",
                "runtimeCellInspection",
                ["panel.world-inspector.inspect"],
                InspectRuntimeCell,
                phase: AutomationExecutionPhase.EngineInputAndTime),
            ProductRead(
                AutomationProtocolConstants.RuntimeWorldInspectorGetMethod,
                "runtime",
                "emptyRequest",
                "worldInspectorSnapshot",
                ["panel.world-inspector"],
                GetWorldInspector),
            ProductWrite(
                AutomationProtocolConstants.RuntimeWorldInspectorSetMethod,
                "runtime",
                "worldInspectorSetRequest",
                "worldInspectorSnapshot",
                ["panel.world-inspector.follow", "panel.world-inspector.lock", "panel.world-inspector.coordinates"],
                SetWorldInspector,
                [AutomationScopes.EditorControl],
                AllModes),
            ProductRead(
                AutomationProtocolConstants.RuntimePhysicsGetMethod,
                "runtime",
                "emptyRequest",
                "runtimePhysicsSnapshot",
                ["panel.physics"],
                GetRuntimePhysics),
            ProductCommand(
                AutomationProtocolConstants.RuntimePhysicsSetMethod,
                "runtime",
                "runtimePhysicsSetRequest",
                "runtimePhysicsSnapshot",
                [AutomationScopes.EditorControl],
                AllModes,
                ["panel.physics.apply"],
                SetRuntimePhysics),
            ProductRead(
                AutomationProtocolConstants.RuntimeParticlesGetMethod,
                "runtime",
                "emptyRequest",
                "runtimeParticlesSnapshot",
                ["panel.particles"],
                GetRuntimeParticles),
            ProductCommand(
                AutomationProtocolConstants.RuntimeParticlesSetMethod,
                "runtime",
                "runtimeParticlesSetRequest",
                "runtimeParticlesSnapshot",
                [AutomationScopes.EditorControl],
                AllModes,
                ["panel.particles.apply"],
                SetRuntimeParticles),
            ProductRead(
                AutomationProtocolConstants.RuntimeLightingGetMethod,
                "runtime",
                "emptyRequest",
                "runtimeLightingSnapshot",
                ["panel.lighting"],
                GetRuntimeLighting),
            ProductCommand(
                AutomationProtocolConstants.RuntimeLightingSetMethod,
                "runtime",
                "runtimeLightingSetRequest",
                "runtimeLightingSnapshot",
                [AutomationScopes.EditorControl],
                AllModes,
                ["panel.lighting.apply"],
                SetRuntimeLighting),
            ProductRead(
                AutomationProtocolConstants.RuntimeSaveSlotListMethod,
                "runtime",
                "pageRequest",
                "saveSlotListResponse",
                ["panel.save-load.refresh", "panel.save-load.slots"],
                ListRuntimeSaveSlots,
                preparation: PrepareRuntimeSaveSlots),
            ProductEngineWrite(
                AutomationProtocolConstants.RuntimeSaveSlotSaveMethod,
                "runtime",
                "saveSlotRequest",
                "saveSlotOperationResult",
                ["panel.save-load.save"],
                CommitRuntimeSave,
                PrepareRuntimeSave,
                AllModes),
            ProductEngineWrite(
                AutomationProtocolConstants.RuntimeSaveSlotLoadMethod,
                "runtime",
                "saveSlotRequest",
                "saveSlotOperationResult",
                ["panel.save-load.load"],
                CommitRuntimeLoad,
                PrepareRuntimeLoad,
                ["edit"]),
            ProductRead(
                AutomationProtocolConstants.GamePresentationGetMethod,
                "game",
                "emptyRequest",
                "gamePresentationSnapshot",
                ["panel.game.toolbar", "panel.game.presentation"],
                GetGamePresentation),
            ProductWrite(
                AutomationProtocolConstants.GamePresentationSetMethod,
                "game",
                "gamePresentationSetRequest",
                "gamePresentationSnapshot",
                ["panel.game.toolbar", "panel.game.maximize"],
                SetGamePresentation,
                [AutomationScopes.EditorControl],
                AllModes),
            ProductRead(
                AutomationProtocolConstants.SceneCaptureMethod,
                "scene",
                "emptyRequest",
                "artifactReference",
                ["panel.scene.capture"],
                CaptureScene,
                AutomationArtifactBehavior.Required),
            ProductRead(
                AutomationProtocolConstants.GameCaptureMethod,
                "game",
                "emptyRequest",
                "artifactReference",
                ["panel.game.capture"],
                CaptureGame,
                AutomationArtifactBehavior.Required),
            ProductRead(
                AutomationProtocolConstants.ProfilerGetMethod,
                "profiler",
                "emptyRequest",
                "profilerSnapshot",
                ["panel.profiler"],
                GetProfiler,
                phase: AutomationExecutionPhase.EngineInputAndTime),
            ProductRead(
                AutomationProtocolConstants.ProfilerExportMethod,
                "profiler",
                "emptyRequest",
                "artifactReference",
                ["panel.profiler.export"],
                ExportProfiler,
                AutomationArtifactBehavior.Required,
                AutomationExecutionPhase.EngineInputAndTime),
            ProductCommand(
                AutomationProtocolConstants.ProfilerVSyncSetMethod,
                "profiler",
                "profilerVSyncSetRequest",
                "profilerSnapshot",
                [AutomationScopes.EditorControl],
                AllModes,
                ["panel.profiler.vsync"],
                SetProfilerVSync,
                AutomationExecutionPhase.EngineInputAndTime),
            ProductRead(
                AutomationProtocolConstants.DebugOverlayGetMethod,
                "debug",
                "emptyRequest",
                "debugOverlaySnapshot",
                ["panel.overlays"],
                GetDebugOverlays),
            ProductCommand(
                AutomationProtocolConstants.DebugOverlaySetMethod,
                "debug",
                "debugOverlaySetRequest",
                "debugOverlaySnapshot",
                [AutomationScopes.EditorControl],
                AllModes,
                ["panel.overlays.toggle"],
                SetDebugOverlay),
            ProductRead(
                AutomationProtocolConstants.PreferencesGetMethod,
                "settings",
                "emptyRequest",
                "editorPreferences",
                ["menu.edit.preferences", "panel.preferences", "shortcut.ctrl-comma"],
                GetPreferences),
            ProductRead(
                AutomationProtocolConstants.ShortcutListMethod,
                "settings",
                "pageRequest",
                "shortcutListResponse",
                ["menu.help.shortcuts", "panel.preferences.shortcuts"],
                ListShortcuts),
            ProductWrite(
                AutomationProtocolConstants.PreferencesSetMethod,
                "settings",
                "editorPreferences",
                "editorPreferences",
                ["panel.preferences.apply"],
                CommitPreparedPreferences,
                preparation: PreparePreferences),
            ProductRead(
                AutomationProtocolConstants.ProjectSettingsGetMethod,
                "settings",
                "emptyRequest",
                "projectSettings",
                ["menu.file.project-settings", "panel.project-settings"],
                GetProjectSettings),
            ProductWrite(
                AutomationProtocolConstants.ProjectSettingsSetMethod,
                "settings",
                "projectSettingsSetRequest",
                "projectSettings",
                ["panel.project-settings.apply"],
                CommitPreparedProjectSettings,
                preparation: PrepareProjectSettings),
            ProductRead(
                AutomationProtocolConstants.PlayerSettingsGetMethod,
                "settings",
                "emptyRequest",
                "playerSettings",
                ["menu.file.player-settings", "panel.player-settings"],
                GetPlayerSettings),
            ProductWrite(
                AutomationProtocolConstants.PlayerSettingsSetMethod,
                "settings",
                "playerSettings",
                "playerSettings",
                ["panel.player-settings.apply"],
                CommitPreparedPlayerSettings,
                preparation: PreparePlayerSettings),
            ProductRead(
                AutomationProtocolConstants.BuildSettingsGetMethod,
                "settings",
                "emptyRequest",
                "buildSettings",
                ["menu.file.build-settings", "panel.build-settings"],
                GetBuildSettings),
            ProductWrite(
                AutomationProtocolConstants.BuildSettingsSetMethod,
                "settings",
                "buildSettings",
                "buildSettings",
                ["panel.build-settings.save"],
                CommitPreparedBuildSettings,
                preparation: PrepareBuildSettings),
            ProductRead(
                AutomationProtocolConstants.ArtifactListMethod,
                "artifact",
                "pageRequest",
                "artifactListResponse",
                ["automation.artifacts.list"],
                ListArtifacts),
            ProductRead(
                AutomationProtocolConstants.ArtifactVerifyMethod,
                "artifact",
                "artifactRequest",
                "artifactVerifyResult",
                ["automation.artifacts.verify"],
                VerifyArtifact),
            ProductArtifactCommand(
                AutomationProtocolConstants.ArtifactDeleteMethod,
                "artifactRequest",
                "artifactDeleteResult",
                ["automation.artifacts.delete"],
                DeleteArtifact),
        ];
    }

    private static AutomationMethodRegistration ProductRead(
        string method,
        string domain,
        string requestSchema,
        string responseSchema,
        string[] uiCommandIds,
        AutomationScheduledOperation operation,
        AutomationArtifactBehavior artifactBehavior = AutomationArtifactBehavior.None,
        AutomationExecutionPhase phase = AutomationExecutionPhase.EditorIngress,
        string[]? modes = null,
        AutomationScheduledPreparation? preparation = null)
    {
        return Registration(
            method,
            domain,
            requestSchema,
            responseSchema,
            [AutomationScopes.EditorRead],
            modes ?? AllModes,
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
            phase,
            preparation: preparation);
    }

    private static AutomationMethodRegistration ProductCommand(
        string method,
        string domain,
        string requestSchema,
        string responseSchema,
        string[] scopes,
        string[] modes,
        string[] uiCommandIds,
        AutomationScheduledOperation operation,
        AutomationExecutionPhase phase = AutomationExecutionPhase.EditorIngress,
        AutomationScheduledPreparation? preparation = null)
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
            operation,
            executionPhase: phase,
            preparation: preparation);
    }

    private static AutomationMethodRegistration ProductWrite(
        string method,
        string domain,
        string requestSchema,
        string responseSchema,
        string[] uiCommandIds,
        AutomationScheduledOperation operation,
        string[]? scopes = null,
        string[]? modes = null,
        AutomationScheduledPreparation? preparation = null,
        AutomationExecutionPhase phase = AutomationExecutionPhase.EditorIngress)
    {
        return Registration(
            method,
            domain,
            requestSchema,
            responseSchema,
            scopes ?? [AutomationScopes.SettingsWrite],
            modes ?? ["edit"],
            AutomationOperationKind.Write,
            AutomationTransactionMode.Optional,
            requiresExpectedRevision: true,
            requiresIdempotencyKey: true,
            eventTypes: EditorAutomationEventRouting.ForCapability(method, domain),
            uiCommandIds,
            operation,
            executionPhase: phase,
            preparation: preparation);
    }

    private static AutomationMethodRegistration ProductAssetWrite(
        string method,
        string requestSchema,
        string responseSchema,
        string[] uiCommandIds,
        AutomationScheduledOperation operation,
        AutomationScheduledPreparation preparation)
    {
        return Registration(
            method,
            "project",
            requestSchema,
            responseSchema,
            [AutomationScopes.ProjectWrite],
            ["edit"],
            AutomationOperationKind.Write,
            AutomationTransactionMode.Optional,
            requiresExpectedRevision: true,
            requiresIdempotencyKey: true,
            eventTypes: EditorAutomationEventRouting.ForCapability(method, "project"),
            uiCommandIds,
            operation,
            preparation: preparation);
    }

    private static AutomationMethodRegistration ProductEngineWrite(
        string method,
        string domain,
        string requestSchema,
        string responseSchema,
        string[] uiCommandIds,
        AutomationScheduledOperation operation,
        AutomationScheduledPreparation preparation,
        string[] modes,
        string[]? scopes = null)
    {
        return Registration(
            method,
            domain,
            requestSchema,
            responseSchema,
            scopes ?? [AutomationScopes.EditorControl],
            modes,
            AutomationOperationKind.Write,
            AutomationTransactionMode.Forbidden,
            requiresExpectedRevision: true,
            requiresIdempotencyKey: true,
            eventTypes: EditorAutomationEventRouting.ForCapability(method, domain),
            uiCommandIds,
            operation,
            executionPhase: AutomationExecutionPhase.EngineWorldStreaming,
            preparation: preparation);
    }

    private static AutomationMethodRegistration ProductArtifactCommand(
        string method,
        string requestSchema,
        string responseSchema,
        string[] uiCommandIds,
        AutomationScheduledOperation operation)
    {
        return Registration(
            method,
            "artifact",
            requestSchema,
            responseSchema,
            [AutomationScopes.EditorControl],
            AllModes,
            AutomationOperationKind.Command,
            AutomationTransactionMode.Forbidden,
            requiresExpectedRevision: false,
            requiresIdempotencyKey: true,
            eventTypes: [AutomationProtocolConstants.ArtifactChangedEventType],
            uiCommandIds,
            operation);
    }

    private AutomationOperationResult GetProject(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.ProjectGetMethod);
        EditorProjectSession session = RequireSession();
        EditorAssetBrowserDataSource assets = session.AutomationAssetDatabase;
        string activeContentRootPath = Path.GetFullPath(Path.Combine(
            session.Project.ProjectRoot,
            session.AutomationActiveContentRoot));
        string activeScriptSourcePath = Path.GetFullPath(Path.Combine(
            session.Project.ProjectRoot,
            session.AutomationActiveScriptSourceDir));
        AutomationProjectSnapshot snapshot = new()
        {
            ProjectId = EditorAutomationRuntime.StableProjectId(session.Project.ProjectRoot),
            Name = session.Project.Name,
            RootPath = session.Project.ProjectRoot,
            ContentRootPath = activeContentRootPath,
            ScriptSourcePath = activeScriptSourcePath,
            ConfiguredContentRootPath = session.Project.ContentRootPath,
            ConfiguredScriptSourcePath = session.Project.ScriptSourcePath,
            RequiresReload = !string.Equals(
                    activeContentRootPath,
                    session.Project.ContentRootPath,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    activeScriptSourcePath,
                    session.Project.ScriptSourcePath,
                    StringComparison.OrdinalIgnoreCase),
            CurrentScenePath = session.CurrentSceneRelativePath,
            AssetCount = assets.ListAssets().Count,
            FolderCount = assets.ListFolders().Count,
            AssetDatabaseDiagnostic = assets.LastDiagnostic,
        };
        return Result(
            snapshot,
            AutomationJsonContext.Default.AutomationProjectSnapshot,
            [ProjectResource, AssetsResource]);
    }

    private AutomationOperationResult ListAssets(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationPageRequest request = DeserializePage(
            payload,
            AutomationProtocolConstants.ProjectAssetListMethod);
        EditorProjectSession session = RequireSession();
        IReadOnlyList<AssetBrowserItem> source = session.AutomationAssetDatabase.ListAssets();
        AutomationAssetInfo[] items = new AutomationAssetInfo[source.Count];
        for (int i = 0; i < source.Count; i++)
        {
            items[i] = MapAsset(source[i]);
        }

        AutomationAssetInfo[] filtered = ApplyFilter(items, request.Filter, MatchAsset);
        SortAssets(filtered, request.Sort);
        string fingerprint = Fingerprint(items, AutomationJsonContext.Default.AutomationAssetInfoArray);
        PageSlice<AutomationAssetInfo> page = SlicePage("project.assets", fingerprint, request, filtered);
        AutomationAssetListResponse response = new() { Items = page.Items, Page = page.Info };
        return Result(
            response,
            AutomationJsonContext.Default.AutomationAssetListResponse,
            [ProjectResource, AssetsResource]);
    }

    private AutomationOperationResult ListFolders(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationPageRequest request = DeserializePage(
            payload,
            AutomationProtocolConstants.ProjectFolderListMethod);
        EditorProjectSession session = RequireSession();
        string projectId = EditorAutomationRuntime.StableProjectId(session.Project.ProjectRoot);
        IReadOnlyList<AssetBrowserFolderItem> source = session.AutomationAssetDatabase.ListFolders();
        AutomationFolderInfo[] items = new AutomationFolderInfo[source.Count];
        for (int i = 0; i < source.Count; i++)
        {
            AssetBrowserFolderItem folder = source[i];
            items[i] = new AutomationFolderInfo
            {
                FolderId = CreateFolderId(projectId, folder.Path),
                Path = folder.Path,
                DisplayName = folder.DisplayName,
                AssetCount = folder.AssetCount,
            };
        }

        AutomationFolderInfo[] filtered = ApplyFilter(items, request.Filter, MatchFolder);
        SortFolders(filtered, request.Sort);
        string fingerprint = Fingerprint(items, AutomationJsonContext.Default.AutomationFolderInfoArray);
        PageSlice<AutomationFolderInfo> page = SlicePage("project.folders", fingerprint, request, filtered);
        AutomationFolderListResponse response = new() { Items = page.Items, Page = page.Info };
        return Result(
            response,
            AutomationJsonContext.Default.AutomationFolderListResponse,
            [ProjectResource, AssetsResource]);
    }

    private AutomationOperationResult GetAsset(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationAssetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationAssetRequest,
            AutomationProtocolConstants.ProjectAssetGetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.ProjectAssetGetMethod);
        EditorProjectSession session = RequireSession();
        AssetBrowserItem item = RequireAsset(session.AutomationAssetDatabase, request.AssetId);
        return Result(
            MapAsset(item),
            AutomationJsonContext.Default.AutomationAssetInfo,
            [ProjectResource, AssetsResource, StableAssetResource(session, item.AssetId!)]);
    }

    private AutomationOperationResult GetProjectSelection(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        EnsureEmpty(payload, AutomationProtocolConstants.ProjectSelectionGetMethod);
        EditorProjectSession session = RequireSession();
        return Result(
            CaptureProjectSelection(session),
            AutomationJsonContext.Default.AutomationProjectSelectionSnapshot,
            [ProjectSelectionResource(session)]);
    }

    private AutomationOperationResult SetProjectSelection(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationProjectSelectionSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationProjectSelectionSetRequest,
            AutomationProtocolConstants.ProjectSelectionSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.ProjectSelectionSetMethod);
        string? requestedAssetId = string.IsNullOrWhiteSpace(request.AssetId)
            ? null
            : ValidateAssetId(request.AssetId);
        int selectorCount = (requestedAssetId is null ? 0 : 1) +
            (request.FolderPath is null ? 0 : 1) +
            (request.Clear ? 1 : 0);
        if (selectorCount != 1)
        {
            throw Invalid("assetId、folderPath、clear 必须且只能指定一个。");
        }

        EditorProjectSession session = RequireSession();
        AutomationProjectSelectionSnapshot before = CaptureProjectSelection(session);
        AssetBrowserItem? targetAsset = requestedAssetId is null
            ? null
            : RequireAsset(session.AutomationAssetDatabase, requestedAssetId);
        string? targetFolder = request.FolderPath is null
            ? null
            : ValidateProjectFolder(session, request.FolderPath);
        string[] resources = ProjectSelectionResources(session, before, targetAsset, targetFolder);
        bool alreadySelected = targetAsset is { } asset
            ? string.Equals(before.AssetId, asset.AssetId, StringComparison.OrdinalIgnoreCase)
            : targetFolder is not null
                ? before.FolderPath is not null && string.Equals(
                    before.FolderPath,
                    targetFolder,
                    StringComparison.OrdinalIgnoreCase)
                : before.AssetId is null && before.FolderPath is null;
        if (alreadySelected)
        {
            return Result(
                before,
                AutomationJsonContext.Default.AutomationProjectSelectionSnapshot,
                resources);
        }

        context.Revisions.EnsureCanAdvance(resources);
        bool applied = targetAsset is { } selectedAsset
            ? session.TrySetAutomationProjectAssetSelection(selectedAsset.Path)
            : targetFolder is not null
                ? session.TrySetAutomationProjectFolderSelection(targetFolder)
                : ClearProjectSelection(session);
        if (!applied)
        {
            throw StateUnavailable("Project Window 选择目标在提交前已失效。");
        }

        try
        {
            AutomationProjectSelectionSnapshot after = CaptureProjectSelection(session);
            AutomationOperationResult result = Result(
                after,
                AutomationJsonContext.Default.AutomationProjectSelectionSnapshot,
                resources);
            AutomationRevisionSnapshot revision = context.Revisions.Advance(resources);
            return result with
            {
                RevisionOverride = revision,
                StateChanged = true,
            };
        }
        catch
        {
            RestoreProjectSelection(session, before);
            throw;
        }
    }

    private AutomationOperationResult GetProjectWindow(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        EnsureEmpty(payload, AutomationProtocolConstants.ProjectWindowGetMethod);
        EditorProjectSession session = RequireSession();
        return Result(
            CaptureProjectWindow(session),
            AutomationJsonContext.Default.AutomationProjectWindowSnapshot,
            [ProjectWindowResource]);
    }

    private AutomationOperationResult SetProjectWindow(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationProjectWindowSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationProjectWindowSetRequest,
            AutomationProtocolConstants.ProjectWindowSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.ProjectWindowSetMethod);
        if (request.KindFilter.HasValue && request.ClearKindFilter)
        {
            throw Invalid("kindFilter 与 clearKindFilter 不得同时指定。");
        }

        if (request.Search is { Length: > AssetBrowserPanel.MaximumSearchLength })
        {
            throw Invalid($"Project Window search 最多 {AssetBrowserPanel.MaximumSearchLength} 个字符。");
        }

        if (request.ThumbnailSize is { } thumbnailSize &&
            (!float.IsFinite(thumbnailSize) ||
             thumbnailSize < AssetBrowserPanel.MinimumThumbnailSize ||
             thumbnailSize > AssetBrowserPanel.MaximumThumbnailSize))
        {
            throw Invalid(
                $"thumbnailSize 必须位于 {AssetBrowserPanel.MinimumThumbnailSize}.." +
                $"{AssetBrowserPanel.MaximumThumbnailSize}。 ");
        }

        bool hasTarget = request.Search is not null ||
            request.KindFilter.HasValue ||
            request.ClearKindFilter ||
            request.SortMode.HasValue ||
            request.ViewMode.HasValue ||
            request.ThumbnailSize.HasValue;
        if (!hasTarget)
        {
            throw Invalid("project.window.set 至少需要一个目标字段。");
        }

        EditorProjectSession session = RequireSession();
        AssetBrowserViewState before = session.CaptureAutomationProjectWindowViewState();
        AssetBrowserViewState target = before with
        {
            Search = request.Search ?? before.Search,
            KindFilter = request.ClearKindFilter
                ? null
                : request.KindFilter.HasValue
                    ? ToAssetKind(request.KindFilter.Value)
                    : before.KindFilter,
            SortMode = request.SortMode.HasValue
                ? ToAssetSortMode(request.SortMode.Value)
                : before.SortMode,
            ViewMode = request.ViewMode.HasValue
                ? ToAssetViewMode(request.ViewMode.Value)
                : before.ViewMode,
            ThumbnailSize = request.ThumbnailSize ?? before.ThumbnailSize,
        };
        AutomationProjectWindowSnapshot response = CaptureProjectWindow(session, target);
        if (before == target)
        {
            return NoChange(
                response,
                AutomationJsonContext.Default.AutomationProjectWindowSnapshot,
                [ProjectWindowResource]);
        }

        JsonElement serialized = JsonSerializer.SerializeToElement(
            response,
            AutomationJsonContext.Default.AutomationProjectWindowSnapshot);
        context.Revisions.EnsureCanAdvance([ProjectWindowResource]);
        bool applied = session.ApplyAutomationProjectWindowViewState(target, notifyChanged: false);
        if (!applied)
        {
            throw new InvalidOperationException("Project Window target state 在提交时未形成预期变化。");
        }

        try
        {
            AutomationRevisionSnapshot revision = context.Revisions.Advance([ProjectWindowResource]);
            return new AutomationOperationResult
            {
                Payload = serialized,
                ResourceIds = [ProjectWindowResource],
                RevisionOverride = revision,
                StateChanged = true,
            };
        }
        catch (Exception exception)
        {
            RestoreProjectWindowStateOrThrow(session, before, exception);
            throw;
        }
    }

    private AutomationOperationResult GetAssetPreview(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationAssetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationAssetRequest,
            AutomationProtocolConstants.ProjectAssetPreviewMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.ProjectAssetPreviewMethod);
        EditorProjectSession session = RequireSession();
        string assetId = ValidateAssetId(request.AssetId);
        if (!session.AutomationAssetDatabase.TryCreateAutomationPreviewPlan(assetId, out EditorAssetAutomationPreviewPlan plan))
        {
            throw NotFound($"Asset '{assetId}' 不存在。");
        }

        string sessionId = context.Request.SessionId;
        string requestId = context.Request.RequestId;
        return new AutomationOperationResult
        {
            ResourceIds =
            [
                ProjectResource,
                AssetsResource,
                StableAssetResource(session, assetId),
                ArtifactResource(sessionId),
            ],
            DeferredPayloadFactory = (revision, cancellationToken) =>
                BuildAssetPreviewAsync(sessionId, requestId, plan, revision, cancellationToken),
        };
    }

    private AutomationOperationResult OpenScriptAsset(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        AutomationScriptAssetOpenRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationScriptAssetOpenRequest,
            AutomationProtocolConstants.ProjectAssetScriptOpenMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.ProjectAssetScriptOpenMethod);
        if (request.Line <= 0 || request.Line > 10_000_000 ||
            request.Column <= 0 || request.Column > 1_000_000)
        {
            throw Invalid("line/column 必须是受支持的一基正整数。");
        }

        EditorProjectSession session = RequireSession();
        AssetBrowserItem item = RequireAsset(
            session.AutomationAssetDatabase,
            ValidateAssetId(request.AssetId));
        if (item.Kind != AssetBrowserItemKind.Script)
        {
            throw Invalid($"Asset '{item.AssetId}' 不是 Script，不能由外部脚本编辑器打开。");
        }

        bool succeeded = _app.OpenScriptAsset(
            item.Path,
            request.Line,
            request.Column,
            out string diagnostic);
        AutomationAssetActionResult response = new()
        {
            Succeeded = succeeded,
            Diagnostic = diagnostic,
            Asset = MapAsset(item),
        };
        return Result(
            response,
            AutomationJsonContext.Default.AutomationAssetActionResult,
            [ProjectResource, AssetsResource, StableAssetResource(session, item.AssetId!)]);
    }

    private AutomationBackgroundPreparation PrepareCodeProjectOpen(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        EnsureEmpty(payload, AutomationProtocolConstants.ProjectCodeOpenMethod);
        EditorProjectSession session = RequireSession();
        EditorCodeWorkspaceAutomationWorkspace workspace =
            session.CaptureAutomationCodeWorkspacePreparation();
        EditorCodeWorkspaceAutomationPrepared? prepared = null;
        return new AutomationBackgroundPreparation
        {
            PrepareAsync = cancellationToken =>
            {
                EditorCodeWorkspacePreparedOpen open = workspace.Prepare(cancellationToken);
                EditorCodeWorkspaceAutomationPrepared result = new(workspace, open);
                Volatile.Write(ref prepared, result);
                return ValueTask.FromResult<object?>(result);
            },
            AbortAtEditorIngress = () => Volatile.Read(ref prepared)?.Dispose(),
        };
    }

    private AutomationOperationResult CommitPreparedCodeProjectOpen(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.ProjectCodeOpenMethod);
        EditorCodeWorkspaceAutomationPrepared prepared =
            context.RequirePreparedState<EditorCodeWorkspaceAutomationPrepared>();
        using (prepared)
        {
            if (!prepared.Workspace.IsCurrent(_app.CurrentSession))
            {
                throw StateUnavailable(
                    "project.code.open preparation 已因工程切换或 IDE 设置变化而失效。");
            }

            string[] advancingResources = prepared.Open.FilesChanged
                ? [ProjectResource, ConsoleResource]
                : [ConsoleResource];
            context.Revisions.EnsureCanAdvance(advancingResources);
            bool filesChanged = prepared.Open.FilesChanged;
            EditorCodeWorkspaceOpenResult result = prepared.Workspace.Commit(prepared.Open);
            _app.RecordCodeWorkspaceOpenResult(result);
            AutomationCodeProjectOpenResult response = MapCodeProjectOpen(result, filesChanged);
            JsonElement serialized = JsonSerializer.SerializeToElement(
                response,
                AutomationJsonContext.Default.AutomationCodeProjectOpenResult);
            if (!filesChanged)
            {
                return new AutomationOperationResult
                {
                    Payload = serialized,
                    ResourceIds = [ProjectResource, ConsoleResource],
                };
            }

            AutomationRevisionSnapshot revision = AdvanceAndCapture(
                context.Revisions,
                [ProjectResource],
                [ProjectResource, ConsoleResource]);
            return new AutomationOperationResult
            {
                Payload = serialized,
                ResourceIds = [ProjectResource],
                RevisionOverride = revision,
                StateChanged = true,
            };
        }
    }

    private AutomationOperationResult PreviewAudioAsset(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        AutomationAssetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationAssetRequest,
            AutomationProtocolConstants.ProjectAssetAudioPreviewMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.ProjectAssetAudioPreviewMethod);
        EditorProjectSession session = RequireSession();
        AssetBrowserItem item = RequireAsset(
            session.AutomationAssetDatabase,
            ValidateAssetId(request.AssetId));
        if (item.Kind != AssetBrowserItemKind.Audio)
        {
            throw Invalid($"Asset '{item.AssetId}' 不是 Audio，不能试听。");
        }

        bool succeeded = session.TryPreviewAutomationAudio(item.Path, out string diagnostic);
        AutomationAssetActionResult response = new()
        {
            Succeeded = succeeded,
            Diagnostic = diagnostic,
            Asset = MapAsset(item),
        };
        return Result(
            response,
            AutomationJsonContext.Default.AutomationAssetActionResult,
            [ProjectResource, AssetsResource, StableAssetResource(session, item.AssetId!)]);
    }

    private AutomationOperationResult ListAssetReferences(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationAssetReferenceListRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationAssetReferenceListRequest,
            AutomationProtocolConstants.ProjectAssetReferencesMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.ProjectAssetReferencesMethod);
        EditorProjectSession session = RequireSession();
        string assetId = ValidateAssetId(request.AssetId);
        EditorAssetAutomationReferencePlan plan =
            session.AutomationAssetDatabase.TryCreateAutomationAssetReferencePlan(
                assetId,
                session.SceneModel,
                out EditorAssetAutomationReferencePlan candidate)
                ? candidate
                : throw NotFound($"Asset '{assetId}' 不存在。");

        return new AutomationOperationResult
        {
            ResourceIds = [ProjectResource, AssetsResource, StableAssetResource(session, assetId)],
            DeferredPayloadFactory = (_, cancellationToken) =>
                BuildAssetReferenceListAsync(assetId, plan, request, cancellationToken),
        };
    }

    private async ValueTask<JsonElement?> BuildAssetReferenceListAsync(
        string assetId,
        EditorAssetAutomationReferencePlan plan,
        AutomationAssetReferenceListRequest request,
        CancellationToken cancellationToken)
    {
        EditorAssetDeletePreflight preflight;
        try
        {
            preflight = await Task.Run(
                () => EditorAssetAutomationReferenceScanner.Scan(plan, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            throw StateUnavailable(exception.Message);
        }

        AutomationAssetReferenceInfo[] source = new AutomationAssetReferenceInfo[
            preflight.ReferenceLocations.Count];
        for (int i = 0; i < source.Length; i++)
        {
            string location = preflight.ReferenceLocations[i];
            source[i] = new AutomationAssetReferenceInfo
            {
                ReferenceId = CreateReferenceId(assetId, location),
                Location = location,
                ActiveScene = location.StartsWith("active scene:", StringComparison.Ordinal),
            };
        }

        AutomationAssetReferenceInfo[] filtered = ApplyFilter(
            source,
            request.Filter,
            MatchAssetReference);
        SortAssetReferences(filtered, request.Sort);
        AutomationPageRequest pageRequest = new()
        {
            SchemaVersion = request.SchemaVersion,
            Filter = request.Filter,
            Sort = request.Sort,
            PageSize = request.PageSize,
            Cursor = request.Cursor,
        };
        string fingerprint = Fingerprint(
            source,
            AutomationJsonContext.Default.AutomationAssetReferenceInfoArray);
        PageSlice<AutomationAssetReferenceInfo> page = SlicePage(
            $"project.asset.references:{assetId}",
            fingerprint,
            pageRequest,
            filtered);
        AutomationAssetReferenceListResponse response = new()
        {
            AssetId = assetId,
            ReferenceCount = preflight.ReferenceCount,
            ReferenceDocuments = preflight.ReferenceDocuments,
            ActiveSceneHasReferences = preflight.ActiveSceneHasReferences,
            Items = page.Items,
            Page = page.Info,
        };
        return JsonSerializer.SerializeToElement(
            response,
            AutomationJsonContext.Default.AutomationAssetReferenceListResponse);
    }

    private AutomationBackgroundPreparation PrepareAssetRefresh(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.ProjectAssetRefreshMethod);
        EditorProjectSession session = RequireEditSession();
        AutomationPreparationScope scope = context.PreparationScope ??
            throw new InvalidOperationException("project.assets.refresh preparation 缺少 workspace scope。");
        string workspaceKey = $"pixelengine.asset:{Path.GetFullPath(session.Project.ProjectRoot)}";
        EditorAssetAutomationPreparationWorkspace workspace = scope.GetOrAdd(
            workspaceKey,
            () => EditorAssetAutomationPreparationWorkspace.Freeze(session, _importRoots));
        EditorAssetAutomationMutationRequest request = new(
            AutomationProtocolConstants.ProjectAssetRefreshMethod,
            Payload: null,
            session.AutomationAssetDatabase.CaptureAutomationBrowserSnapshot());
        EditorAssetAutomationPreparedMutation? prepared = null;
        return new AutomationBackgroundPreparation
        {
            PrepareAsync = async cancellationToken =>
            {
                EditorAssetAutomationPreparedMutation result =
                    (EditorAssetAutomationPreparedMutation?)await workspace.PrepareRefreshAsync(
                        request,
                        cancellationToken).ConfigureAwait(false) ??
                    throw new InvalidOperationException("project.assets.refresh preparation 返回 null。");
                Volatile.Write(ref prepared, result);
                return result;
            },
            AbortAtEditorIngress = () =>
                Volatile.Read(ref prepared)?.DisposeUncommittedStaging(),
        };
    }

    private AutomationBackgroundPreparation PrepareCreateAsset(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        return PrepareAssetMutation(context, payload, AutomationProtocolConstants.ProjectAssetCreateMethod);
    }

    private AutomationBackgroundPreparation PrepareImportAsset(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        return PrepareAssetMutation(context, payload, AutomationProtocolConstants.ProjectAssetImportMethod);
    }

    private AutomationBackgroundPreparation PrepareReplaceAsset(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        return PrepareAssetMutation(context, payload, AutomationProtocolConstants.ProjectAssetReplaceMethod);
    }

    private AutomationBackgroundPreparation PrepareMoveAsset(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        return PrepareAssetMutation(context, payload, AutomationProtocolConstants.ProjectAssetMoveMethod);
    }

    private AutomationBackgroundPreparation PrepareDeleteAsset(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        return PrepareAssetMutation(context, payload, AutomationProtocolConstants.ProjectAssetDeleteMethod);
    }

    private AutomationBackgroundPreparation PrepareMoveFolder(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        return PrepareAssetMutation(context, payload, AutomationProtocolConstants.ProjectFolderMoveMethod);
    }

    private AutomationBackgroundPreparation PrepareDeleteFolder(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        return PrepareAssetMutation(context, payload, AutomationProtocolConstants.ProjectFolderDeleteMethod);
    }

    private AutomationBackgroundPreparation PrepareUiManifestRead(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        EnsureEmpty(payload, AutomationProtocolConstants.ProjectUiManifestGetMethod);
        EditorProjectSession session = RequireSession();
        string contentRoot = session.Project.ContentRootPath;
        AssetBrowserItem[] assets = [.. session.AutomationAssetDatabase.ListAssets()];
        return new AutomationBackgroundPreparation
        {
            PrepareAsync = cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult<object?>(
                    EditorUiManifestAutomation.Capture(contentRoot, assets));
            },
        };
    }

    private AutomationBackgroundPreparation PrepareUiManifestSync(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.ProjectUiManifestSyncMethod);
        return PrepareAssetMutation(
            context,
            payload,
            AutomationProtocolConstants.ProjectUiManifestSyncMethod);
    }

    private AutomationBackgroundPreparation PrepareUiManifestPreload(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        return PrepareAssetMutation(
            context,
            payload,
            AutomationProtocolConstants.ProjectUiManifestPreloadSetMethod);
    }

    private AutomationBackgroundPreparation PrepareAssetMutation(
        AutomationScheduledContext context,
        JsonElement? payload,
        string method)
    {
        EditorProjectSession session = RequireEditSession();
        AutomationPreparationScope scope = context.PreparationScope ??
            throw new InvalidOperationException($"{method} preparation 缺少共享 workspace scope。");
        string workspaceKey = $"pixelengine.asset:{Path.GetFullPath(session.Project.ProjectRoot)}";
        EditorAssetAutomationPreparationWorkspace workspace = scope.GetOrAdd(
            workspaceKey,
            () => EditorAssetAutomationPreparationWorkspace.Freeze(session, _importRoots));
        EditorAssetAutomationMutationRequest request = new(method, payload?.Clone());
        EditorAssetAutomationPreparedMutation? prepared = null;
        return new AutomationBackgroundPreparation
        {
            PrepareAsync = async cancellationToken =>
            {
                EditorAssetAutomationPreparedMutation result =
                    (EditorAssetAutomationPreparedMutation?)await workspace.PrepareAsync(
                        request,
                        cancellationToken).ConfigureAwait(false) ??
                    throw new InvalidOperationException($"{method} preparation 返回了 null result。");
                Volatile.Write(ref prepared, result);
                return result;
            },
            AbortAtEditorIngress = () =>
                Volatile.Read(ref prepared)?.DisposeUncommittedStaging(),
        };
    }

    private AutomationOperationResult CommitPreparedAssetRefresh(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = payload;
        EditorAssetAutomationPreparedMutation prepared =
            context.RequirePreparedState<EditorAssetAutomationPreparedMutation>();
        if (!string.Equals(
                prepared.Method,
                AutomationProtocolConstants.ProjectAssetRefreshMethod,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Prepared asset refresh method 不匹配：{prepared.Method}。");
        }

        AutomationAssetRefreshResult response = prepared.SemanticResult as AutomationAssetRefreshResult ??
            throw new InvalidOperationException("Prepared asset refresh 缺少 semantic response。");
        EditorProjectSession session = RequireEditSession();
        string[] resources =
        [
            ProjectResource,
            AssetsResource,
            ProjectSelectionResource(session),
            EditorAutomationRuntime.CreateSceneResourceId(session),
        ];
        if (!prepared.StateChanged)
        {
            prepared.CommitNoChange(session.AutomationAssetDatabase);
            return NoChange(
                response,
                AutomationJsonContext.Default.AutomationAssetRefreshResult,
                resources);
        }

        JsonElement serialized = JsonSerializer.SerializeToElement(
            response,
            AutomationJsonContext.Default.AutomationAssetRefreshResult);
        context.Revisions.EnsureCanAdvance(resources);
        EditorAutomationAssetUndoAction action = prepared.Apply(
            session,
            session.AutomationAssetDatabase);
        AutomationRevisionSnapshot revision;
        try
        {
            revision = context.Revisions.Advance(resources);
        }
        catch (Exception operationException)
        {
            List<Exception> failures = [operationException];
            try
            {
                action.Undo();
            }
            catch (Exception rollbackException)
            {
                failures.Add(rollbackException);
            }

            action.Dispose();
            throw new AggregateException(
                "Asset refresh revision 提交失败；已尝试恢复 before-image。",
                failures);
        }

        action.Dispose();
        return new AutomationOperationResult
        {
            Payload = serialized,
            ResourceIds = resources,
            RevisionOverride = revision,
            StateChanged = true,
        };
    }

    private AutomationOperationResult CommitPreparedCreateAsset(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        return CommitPreparedAssetMutation(
            context,
            payload,
            AutomationProtocolConstants.ProjectAssetCreateMethod);
    }

    private AutomationOperationResult CommitPreparedImportAsset(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        return CommitPreparedAssetMutation(
            context,
            payload,
            AutomationProtocolConstants.ProjectAssetImportMethod);
    }

    private AutomationOperationResult CommitPreparedReplaceAsset(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        return CommitPreparedAssetMutation(
            context,
            payload,
            AutomationProtocolConstants.ProjectAssetReplaceMethod);
    }

    private AutomationOperationResult CommitPreparedMoveAsset(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        return CommitPreparedAssetMutation(
            context,
            payload,
            AutomationProtocolConstants.ProjectAssetMoveMethod);
    }

    private AutomationOperationResult CommitPreparedDeleteAsset(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        return CommitPreparedAssetMutation(
            context,
            payload,
            AutomationProtocolConstants.ProjectAssetDeleteMethod);
    }

    private AutomationOperationResult CommitPreparedMoveFolder(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        return CommitPreparedAssetMutation(
            context,
            payload,
            AutomationProtocolConstants.ProjectFolderMoveMethod);
    }

    private AutomationOperationResult CommitPreparedDeleteFolder(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        return CommitPreparedAssetMutation(
            context,
            payload,
            AutomationProtocolConstants.ProjectFolderDeleteMethod);
    }

    private AutomationOperationResult GetUiManifest(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = payload;
        AutomationUiManifestSnapshot snapshot =
            context.RequirePreparedState<AutomationUiManifestSnapshot>();
        return Result(
            snapshot,
            AutomationJsonContext.Default.AutomationUiManifestSnapshot,
            [ProjectResource, AssetsResource, UiManifestResource]);
    }

    private AutomationOperationResult CommitPreparedUiManifestSync(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        return CommitPreparedUiManifest(
            context,
            payload,
            AutomationProtocolConstants.ProjectUiManifestSyncMethod);
    }

    private AutomationOperationResult CommitPreparedUiManifestPreload(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        return CommitPreparedUiManifest(
            context,
            payload,
            AutomationProtocolConstants.ProjectUiManifestPreloadSetMethod);
    }

    private AutomationOperationResult CommitPreparedUiManifest(
        AutomationScheduledContext context,
        JsonElement? payload,
        string method)
    {
        _ = payload;
        EditorAssetAutomationPreparedMutation prepared =
            context.RequirePreparedState<EditorAssetAutomationPreparedMutation>();
        if (!string.Equals(prepared.Method, method, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Prepared UI manifest method 不匹配：expected={method}, actual={prepared.Method}。");
        }

        AutomationUiManifestSnapshot response = prepared.SemanticResult as AutomationUiManifestSnapshot ??
            throw new InvalidOperationException("Prepared UI manifest 缺少 semantic response。");
        EditorProjectSession session = RequireEditSession();
        string[] resources = PreparedUiManifestResources(session, prepared);
        if (!prepared.StateChanged)
        {
            prepared.CommitNoChange(session.AutomationAssetDatabase);
            return NoChange(
                response,
                AutomationJsonContext.Default.AutomationUiManifestSnapshot,
                resources);
        }

        EditorAutomationAssetUndoAction action = prepared.Apply(
            session,
            session.AutomationAssetDatabase);
        return CompleteAssetWrite(
            action,
            response,
            AutomationJsonContext.Default.AutomationUiManifestSnapshot,
            resources);
    }

    private AutomationOperationResult CommitPreparedAssetMutation(
        AutomationScheduledContext context,
        JsonElement? payload,
        string method)
    {
        _ = payload;
        EditorAssetAutomationPreparedMutation prepared =
            context.RequirePreparedState<EditorAssetAutomationPreparedMutation>();
        if (!string.Equals(prepared.Method, method, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Prepared asset method 不匹配：expected={method}, actual={prepared.Method}。");
        }

        EditorProjectSession session = RequireEditSession();
        AutomationAssetMutationResult response = new()
        {
            Succeeded = prepared.Succeeded,
            RequiresConfirmation = prepared.RequiresConfirmation,
            Diagnostic = prepared.Diagnostic,
            Asset = prepared.Asset is { } asset ? MapAsset(asset) : null,
        };
        string[] resources = PreparedAssetResources(session, prepared);
        if (!prepared.StateChanged)
        {
            prepared.CommitNoChange(session.AutomationAssetDatabase);
            return NoChange(
                response,
                AutomationJsonContext.Default.AutomationAssetMutationResult,
                resources);
        }

        EditorAutomationAssetUndoAction action = prepared.Apply(
            session,
            session.AutomationAssetDatabase);
        return CompleteAssetWrite(action, response, resources);
    }

    private static string[] PreparedAssetResources(
        EditorProjectSession session,
        EditorAssetAutomationPreparedMutation prepared)
    {
        HashSet<string> resources = new(StringComparer.Ordinal)
        {
            ProjectResource,
            AssetsResource,
        };
        for (int i = 0; i < prepared.AffectedAssetIds.Length; i++)
        {
            _ = resources.Add(StableAssetResource(session, prepared.AffectedAssetIds[i]));
        }

        for (int i = 0; i < prepared.AffectedFolderPaths.Length; i++)
        {
            _ = resources.Add(FolderResource(session, prepared.AffectedFolderPaths[i]));
        }

        if (prepared.Method is AutomationProtocolConstants.ProjectAssetMoveMethod or
            AutomationProtocolConstants.ProjectFolderMoveMethod)
        {
            _ = resources.Add(EditorAutomationRuntime.CreateSceneResourceId(session));
        }

        return [.. resources.Order(StringComparer.Ordinal)];
    }

    private static AutomationOperationResult CompleteAssetWrite(
        EditorAutomationAssetUndoAction action,
        AutomationAssetMutationResult response,
        string[] resources)
    {
        return CompleteAssetWrite(
            action,
            response,
            AutomationJsonContext.Default.AutomationAssetMutationResult,
            resources);
    }

    private static AutomationOperationResult CompleteAssetWrite<T>(
        EditorAutomationAssetUndoAction action,
        T response,
        JsonTypeInfo<T> typeInfo,
        string[] resources)
        where T : class
    {
        try
        {
            return new AutomationOperationResult
            {
                Payload = JsonSerializer.SerializeToElement(response, typeInfo),
                UndoAction = action,
                ResourceIds = resources,
                WriteStateChanged = true,
            };
        }
        catch (Exception operationException)
        {
            try
            {
                action.Undo();
                action.Dispose();
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    $"Automation asset command '{action.Name}' 序列化响应失败且无法恢复 before-image。",
                    operationException,
                    rollbackException);
            }

            throw;
        }
    }

    private static string[] PreparedUiManifestResources(
        EditorProjectSession session,
        EditorAssetAutomationPreparedMutation prepared)
    {
        HashSet<string> resources = new(StringComparer.Ordinal)
        {
            ProjectResource,
            AssetsResource,
            UiManifestResource,
        };
        for (int i = 0; i < prepared.AffectedAssetIds.Length; i++)
        {
            _ = resources.Add(StableAssetResource(session, prepared.AffectedAssetIds[i]));
        }

        return [.. resources.Order(StringComparer.Ordinal)];
    }

    private AutomationOperationResult ListConsole(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationPageRequest request = DeserializePage(payload, AutomationProtocolConstants.ConsoleListMethod);
        EditorConsoleRow[] source = _app.ConsoleStore.SnapshotRows();
        AutomationConsoleEntry[] items = new AutomationConsoleEntry[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            items[i] = MapConsole(source[i]);
        }

        AutomationConsoleEntry[] filtered = ApplyFilter(items, request.Filter, MatchConsole);
        SortConsole(filtered, request.Sort);
        string fingerprint = Fingerprint(items, AutomationJsonContext.Default.AutomationConsoleEntryArray);
        PageSlice<AutomationConsoleEntry> page = SlicePage("console.entries", fingerprint, request, filtered);
        AutomationConsoleListResponse response = new() { Items = page.Items, Page = page.Info };
        return Result(response, AutomationJsonContext.Default.AutomationConsoleListResponse, [ConsoleResource]);
    }

    private AutomationOperationResult GetConsoleCounts(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.ConsoleCountsGetMethod);
        return Result(
            CaptureConsoleCounts(),
            AutomationJsonContext.Default.AutomationConsoleCounts,
            [ConsoleResource]);
    }

    private AutomationOperationResult GetConsoleOptions(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.ConsoleOptionsGetMethod);
        return Result(
            MapConsoleOptions(_app.ConsoleOptions.Capture()),
            AutomationJsonContext.Default.AutomationConsoleOptions,
            [ConsoleOptionsResource]);
    }

    private AutomationOperationResult SetConsoleOptions(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationConsoleOptions request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationConsoleOptions,
            AutomationProtocolConstants.ConsoleOptionsSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.ConsoleOptionsSetMethod);
        if (request.Search.Length > 256)
        {
            throw Invalid("Console search 最多 256 字符。");
        }

        EditorConsoleOptionsSnapshot target = new(
            request.Search,
            request.Collapse,
            request.ClearOnPlay,
            request.ErrorPause,
            request.ShowLogs,
            request.ShowWarnings,
            request.ShowErrors,
            request.AutoScroll);
        EditorConsoleOptionsSnapshot before = _app.ConsoleOptions.Capture();
        AutomationConsoleOptions response = MapConsoleOptions(target);
        if (before == target)
        {
            return NoChange(
                response,
                AutomationJsonContext.Default.AutomationConsoleOptions,
                [ConsoleOptionsResource]);
        }

        JsonElement serialized = JsonSerializer.SerializeToElement(
            response,
            AutomationJsonContext.Default.AutomationConsoleOptions);
        _ = _app.ConsoleOptions.Apply(target, notifyChanged: false);
        AutomationRevisionSnapshot revision = AdvanceAndCapture(
            context.Revisions,
            [ConsoleOptionsResource],
            [ConsoleOptionsResource]);
        return new AutomationOperationResult
        {
            Payload = serialized,
            ResourceIds = [ConsoleOptionsResource],
            RevisionOverride = revision,
            StateChanged = true,
        };
    }

    private AutomationOperationResult ClearConsole(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.ConsoleClearMethod);
        EditorConsoleStoreState storeBefore = _app.ConsoleStore.CaptureState();
        EditorConsoleSelectionSnapshot selectionBefore = _app.ConsoleSelection.Capture();
        bool selectionChanged = selectionBefore.Sequence.HasValue;
        bool changed = storeBefore.Rows.Length != 0 || selectionChanged;
        string[] resources = selectionChanged
            ? [ConsoleResource, ConsoleSelectionResource]
            : [ConsoleResource];
        if (!changed)
        {
            return Result(
                CaptureConsoleCounts(),
                AutomationJsonContext.Default.AutomationConsoleCounts,
                resources);
        }

        context.Revisions.EnsureCanAdvance(resources);
        _ = _app.ClearConsole(notifyAutomation: false);
        try
        {
            AutomationConsoleCounts response = CaptureConsoleCounts();
            JsonElement serialized = JsonSerializer.SerializeToElement(
                response,
                AutomationJsonContext.Default.AutomationConsoleCounts);
            AutomationRevisionSnapshot revision = context.Revisions.Advance(resources);
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
            _app.ConsoleStore.RestoreState(storeBefore);
            _ = _app.ConsoleSelection.Restore(selectionBefore);
            throw;
        }
    }

    private AutomationOperationResult ExportConsole(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.ConsoleExportMethod);
        EditorConsoleRow[] source = _app.ConsoleStore.SnapshotRows();
        AutomationConsoleEntry[] snapshot = new AutomationConsoleEntry[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            snapshot[i] = MapConsole(source[i]);
        }

        string sessionId = context.Request.SessionId;
        string requestId = context.Request.RequestId;
        return new AutomationOperationResult
        {
            ResourceIds = [ConsoleResource, ArtifactResource(sessionId)],
            DeferredPayloadFactory = (revision, cancellationToken) =>
                ExportConsoleAsync(sessionId, requestId, snapshot, revision, cancellationToken),
        };
    }

    private AutomationOperationResult GetConsoleSelection(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        EnsureEmpty(payload, AutomationProtocolConstants.ConsoleSelectionGetMethod);
        return Result(
            CaptureConsoleSelection(),
            AutomationJsonContext.Default.AutomationConsoleSelectionSnapshot,
            [ConsoleSelectionResource]);
    }

    private AutomationOperationResult SetConsoleSelection(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationConsoleSelectionSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationConsoleSelectionSetRequest,
            AutomationProtocolConstants.ConsoleSelectionSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.ConsoleSelectionSetMethod);
        bool hasEntry = !string.IsNullOrWhiteSpace(request.EntryId);
        if ((hasEntry ? 1 : 0) + (request.Clear ? 1 : 0) != 1)
        {
            throw Invalid("entryId 与 clear 必须且只能指定一个。");
        }

        EditorConsoleSelectionSnapshot before = _app.ConsoleSelection.Capture();
        EditorConsoleSelectionSnapshot target = hasEntry
            ? ToSelection(RequireConsoleRow(request.EntryId!))
            : default;
        if (before == target)
        {
            return Result(
                CaptureConsoleSelection(),
                AutomationJsonContext.Default.AutomationConsoleSelectionSnapshot,
                [ConsoleSelectionResource]);
        }

        context.Revisions.EnsureCanAdvance([ConsoleSelectionResource]);
        _ = _app.ConsoleSelection.Restore(target);
        try
        {
            AutomationConsoleSelectionSnapshot response = CaptureConsoleSelection();
            JsonElement serialized = JsonSerializer.SerializeToElement(
                response,
                AutomationJsonContext.Default.AutomationConsoleSelectionSnapshot);
            AutomationRevisionSnapshot revision = context.Revisions.Advance([ConsoleSelectionResource]);
            return new AutomationOperationResult
            {
                Payload = serialized,
                ResourceIds = [ConsoleSelectionResource],
                RevisionOverride = revision,
                StateChanged = true,
            };
        }
        catch
        {
            _ = _app.ConsoleSelection.Restore(before);
            throw;
        }
    }

    private AutomationOperationResult CopyConsoleEntry(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        AutomationConsoleEntryRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationConsoleEntryRequest,
            AutomationProtocolConstants.ConsoleEntryCopyMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.ConsoleEntryCopyMethod);
        EditorConsoleRow row = RequireConsoleRow(request.EntryId);
        AutomationConsoleCopyResult response = new()
        {
            Entry = MapConsole(row),
            Text = EditorConsoleActions.BuildClipboardText(row.Entry),
        };
        return Result(
            response,
            AutomationJsonContext.Default.AutomationConsoleCopyResult,
            [ConsoleResource]);
    }

    private AutomationOperationResult OpenConsoleEntrySource(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        AutomationConsoleEntryRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationConsoleEntryRequest,
            AutomationProtocolConstants.ConsoleEntryOpenSourceMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.ConsoleEntryOpenSourceMethod);
        EditorConsoleRow row = RequireConsoleRow(request.EntryId);
        bool succeeded = _app.TryOpenConsoleSource(row.Entry, out string diagnostic);
        AutomationConsoleOpenSourceResult response = new()
        {
            Entry = MapConsole(row),
            Succeeded = succeeded,
            Diagnostic = diagnostic,
        };
        return Result(
            response,
            AutomationJsonContext.Default.AutomationConsoleOpenSourceResult,
            [ConsoleResource]);
    }

    private AutomationOperationResult GetPlay(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.PlayGetMethod);
        EditorProjectSession session = RequireSession();
        return Result(
            CapturePlay(session),
            AutomationJsonContext.Default.AutomationPlaySnapshot,
            PlayResources(session));
    }

    private AutomationOperationResult EnterPlay(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationPlayEnterRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationPlayEnterRequest,
            AutomationProtocolConstants.PlayEnterMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.PlayEnterMethod);
        if (!Enum.IsDefined(request.Source))
        {
            throw Invalid($"未知 Play source：{request.Source}。");
        }

        EditorProjectSession session = RequireEditSession();
        Hosting.EditorPlaySessionResult result = request.Source == AutomationPlaySource.CurrentState
            ? session.EnterPlayCurrent()
            : session.EnterPlayTemporary();
        return CompletePlayCommand(context, session, result, changed: result.Succeeded);
    }

    private AutomationOperationResult PausePlay(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.PlayPauseMethod);
        EditorProjectSession session = RequireSession();
        Hosting.EditorPlaySessionSnapshot before = session.CaptureEditorPlaySession();
        Hosting.EditorPlaySessionResult result = session.PauseEditorPlay();
        bool changed = result.Succeeded && before.Mode != result.Snapshot.Mode;
        return CompletePlayCommand(context, session, result, changed);
    }

    private AutomationOperationResult ResumePlay(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.PlayResumeMethod);
        EditorProjectSession session = RequireSession();
        Hosting.EditorPlaySessionSnapshot before = session.CaptureEditorPlaySession();
        Hosting.EditorPlaySessionResult result = session.ResumeEditorPlay();
        bool changed = result.Succeeded && before.Mode != result.Snapshot.Mode;
        return CompletePlayCommand(context, session, result, changed);
    }

    private AutomationOperationResult StepPlay(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.PlayStepMethod);
        EditorProjectSession session = RequireSession();
        Hosting.EditorPlaySessionResult result = session.StepEditorPlay();
        return CompletePlayCommand(context, session, result, changed: result.Succeeded);
    }

    private AutomationOperationResult StopPlay(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.PlayStopMethod);
        EditorProjectSession session = RequireSession();
        Hosting.EditorPlaySessionSnapshot before = session.CaptureEditorPlaySession();
        string? previousPlaySessionId = session.AutomationPlaySessionId;
        Hosting.EditorPlaySessionResult result = session.ExitEditorPlay();
        bool changed = result.Succeeded && before.Mode != result.Snapshot.Mode;
        return CompletePlayCommand(
            context,
            session,
            result,
            changed,
            previousPlaySessionId);
    }

    private AutomationOperationResult GetRuntimeWorld(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.RuntimeWorldGetMethod);
        EditorProjectSession session = RequireRuntimeSession();
        ScriptEntityInspection[] entities = CaptureScriptEntities(session);
        EngineDiagnosticsSnapshot diagnostics = session.Engine.Probe.CaptureDiagnostics();
        AutomationRuntimeWorldSnapshot snapshot = new()
        {
            PlaySessionId = session.AutomationPlaySessionId!,
            FrameIndex = diagnostics.FrameCount,
            EntityCount = entities.Length,
            FramesPerSecond = diagnostics.FramesPerSecond,
            FrameMilliseconds = diagnostics.FrameLastMilliseconds,
            P99FrameMilliseconds = diagnostics.FrameP99Milliseconds,
            OnePercentLowFps = diagnostics.FrameLow1PercentFps,
            SimulationHz = checked((int)MathF.Round(diagnostics.SimHz)),
            ActiveChunks = checked((int)Math.Min(int.MaxValue, diagnostics.ActiveChunks)),
            ActiveParticles = checked((int)Math.Min(int.MaxValue, diagnostics.FreeParticles)),
            ActiveBodies = checked((int)Math.Min(int.MaxValue, diagnostics.RigidBodies)),
            ActiveLights = checked((int)Math.Min(int.MaxValue, diagnostics.PointLights)),
        };
        return Result(
            snapshot,
            AutomationJsonContext.Default.AutomationRuntimeWorldSnapshot,
            RuntimeResources(session));
    }

    private AutomationOperationResult ListRuntimeEntities(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationPageRequest request = DeserializePage(
            payload,
            AutomationProtocolConstants.RuntimeEntityListMethod);
        EditorProjectSession session = RequireRuntimeSession();
        AutomationRuntimeEntity[] items = MapRuntimeEntities(session, CaptureScriptEntities(session));
        AutomationRuntimeEntity[] filtered = ApplyFilter(items, request.Filter, MatchRuntimeEntity);
        SortRuntimeEntities(filtered, request.Sort);
        string fingerprint = Fingerprint(items, AutomationJsonContext.Default.AutomationRuntimeEntityArray);
        PageSlice<AutomationRuntimeEntity> page = SlicePage("runtime.entities", fingerprint, request, filtered);
        AutomationRuntimeEntityListResponse response = new()
        {
            PlaySessionId = session.AutomationPlaySessionId!,
            Items = page.Items,
            Page = page.Info,
        };
        return Result(
            response,
            AutomationJsonContext.Default.AutomationRuntimeEntityListResponse,
            RuntimeResources(session));
    }

    private AutomationOperationResult GetRuntimeEntity(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationRuntimeEntityRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationRuntimeEntityRequest,
            AutomationProtocolConstants.RuntimeEntityGetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.RuntimeEntityGetMethod);
        EditorProjectSession session = RequireRuntimeSession();
        string id = ValidateRuntimeEntityId(session, request.EntityId);
        AutomationRuntimeEntity[] entities = MapRuntimeEntities(session, CaptureScriptEntities(session));
        AutomationRuntimeEntity entity = entities.FirstOrDefault(candidate =>
            string.Equals(candidate.EntityId, id, StringComparison.Ordinal)) ??
            throw NotFound($"Runtime entity '{id}' 不存在于当前 Play session。");

        return Result(
            entity,
            AutomationJsonContext.Default.AutomationRuntimeEntity,
            [.. RuntimeResources(session), entity.EntityId]);
    }

    private AutomationOperationResult ListRuntimeBodies(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationPageRequest request = DeserializePage(
            payload,
            AutomationProtocolConstants.RuntimeBodyListMethod);
        EditorProjectSession session = RequireRuntimeSession();
        AutomationRuntimeBody[] items = CaptureRuntimeBodies(session);
        AutomationRuntimeBody[] filtered = ApplyFilter(items, request.Filter, MatchRuntimeBody);
        SortRuntimeBodies(filtered, request.Sort);
        string fingerprint = Fingerprint(items, AutomationJsonContext.Default.AutomationRuntimeBodyArray);
        PageSlice<AutomationRuntimeBody> page = SlicePage("runtime.bodies", fingerprint, request, filtered);
        AutomationRuntimeBodyListResponse response = new()
        {
            PlaySessionId = session.AutomationPlaySessionId!,
            Items = page.Items,
            Page = page.Info,
        };
        return Result(
            response,
            AutomationJsonContext.Default.AutomationRuntimeBodyListResponse,
            RuntimeResources(session));
    }

    private AutomationOperationResult GetRuntimeBody(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationRuntimeBodyRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationRuntimeBodyRequest,
            AutomationProtocolConstants.RuntimeBodyGetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.RuntimeBodyGetMethod);
        EditorProjectSession session = RequireRuntimeSession();
        (string bodyId, int bodyKey) = ValidateRuntimeBodyId(session, request.BodyId);
        return session.TryGetAutomationRuntimeBody(bodyKey, out RigidBodySnapshot body)
            ? Result(
                MapRuntimeBody(session, body),
                AutomationJsonContext.Default.AutomationRuntimeBody,
                [.. RuntimeResources(session), bodyId])
            : throw NotFound($"Runtime body '{bodyId}' 不存在于当前 Play session。");
    }

    private AutomationOperationResult SetRuntimeTransform(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationRuntimeTransformSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationRuntimeTransformSetRequest,
            AutomationProtocolConstants.RuntimeEntityTransformSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.RuntimeEntityTransformSetMethod);
        if (!float.IsFinite(request.X) || !float.IsFinite(request.Y) ||
            !float.IsFinite(request.RotationRadians) ||
            !float.IsFinite(request.ScaleX) || !float.IsFinite(request.ScaleY))
        {
            throw Invalid("Runtime Transform 只接受有限数值。");
        }

        EditorProjectSession session = RequireRuntimeSession();
        string entityId = ValidateRuntimeEntityId(session, request.EntityId);
        ScriptEntityInspection beforeEntity = RequireRuntimeEntityInspection(session, entityId);
        Transform transform = beforeEntity.Transform ??
            throw StateUnavailable($"Runtime entity '{entityId}' 没有 Transform。");
        AutomationRuntimeEntity beforeResponse = MapRuntimeEntity(session, beforeEntity);
        bool changed = transform.X != request.X || transform.Y != request.Y ||
            transform.RotationRadians != request.RotationRadians ||
            transform.ScaleX != request.ScaleX || transform.ScaleY != request.ScaleY;
        string[] resources = [.. RuntimeResources(session), entityId];
        if (!changed)
        {
            return Result(
                beforeResponse,
                AutomationJsonContext.Default.AutomationRuntimeEntity,
                resources);
        }

        float beforeX = transform.X;
        float beforeY = transform.Y;
        float beforeRotation = transform.RotationRadians;
        float beforeScaleX = transform.ScaleX;
        float beforeScaleY = transform.ScaleY;
        if (!session.TryApplyAutomationRuntimeTransform(
            beforeEntity.Handle,
            request.X,
            request.Y,
            request.RotationRadians,
            request.ScaleX,
            request.ScaleY,
            out string diagnostic))
        {
            throw StateUnavailable(diagnostic);
        }

        AutomationRuntimeEntity response = MapRuntimeEntity(
            session,
            RequireRuntimeEntityInspection(session, entityId));
        JsonElement serialized;
        try
        {
            serialized = JsonSerializer.SerializeToElement(
                response,
                AutomationJsonContext.Default.AutomationRuntimeEntity);
        }
        catch (Exception serializationException)
        {
            if (!session.TryApplyAutomationRuntimeTransform(
                beforeEntity.Handle,
                beforeX,
                beforeY,
                beforeRotation,
                beforeScaleX,
                beforeScaleY,
                out string rollbackDiagnostic))
            {
                throw new AggregateException(
                    $"Runtime Transform 响应序列化失败，且 before-image 回滚失败：{rollbackDiagnostic}",
                    serializationException);
            }

            throw;
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

    private AutomationOperationResult SetRuntimeComponentField(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationRuntimeComponentFieldSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationRuntimeComponentFieldSetRequest,
            AutomationProtocolConstants.RuntimeComponentFieldSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.RuntimeComponentFieldSetMethod);
        if (request.Value.Length > 32768)
        {
            throw Invalid("Runtime field value 最多 32768 字符。");
        }

        string fieldName = ValidateIdentifier(
            request.FieldName,
            "fieldName",
            512,
            allowDotsAndNestedType: true);
        EditorProjectSession session = RequireRuntimeSession();
        string entityId = ValidateRuntimeEntityId(session, request.EntityId);
        ScriptEntityInspection beforeEntity = RequireRuntimeEntityInspection(session, entityId);
        int componentIndex = RequireRuntimeComponentIndex(beforeEntity, entityId, request.ComponentId);
        ScriptComponentInspection component = beforeEntity.Components[componentIndex];
        ScriptFieldDescriptor field = RequireRuntimeField(component, fieldName);
        if (!field.CanWrite || field.Kind is ScriptFieldKind.Material or
            ScriptFieldKind.AssetReference or ScriptFieldKind.Unsupported)
        {
            throw Invalid($"Runtime field '{fieldName}' 不可由 Inspector 写入。");
        }

        object? converted;
        try
        {
            converted = SerializedFieldBinder.ConvertValue(request.Value, field.FieldType);
            ValidateRuntimeFieldValue(field, converted, request.Value);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or
            ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw Invalid($"Runtime field '{fieldName}' 值无效：{exception.Message}");
        }

        string currentValue = EncodeRuntimeValue(field.Value);
        string desiredValue = EncodeRuntimeValue(converted);
        string componentId = CreateRuntimeComponentId(entityId, component.TypeName);
        string[] resources = [.. RuntimeResources(session), entityId, componentId];
        AutomationRuntimeEntity beforeResponse = MapRuntimeEntity(session, beforeEntity);
        if (string.Equals(currentValue, desiredValue, StringComparison.Ordinal))
        {
            return Result(
                beforeResponse,
                AutomationJsonContext.Default.AutomationRuntimeEntity,
                resources);
        }

        if (!session.TryApplyAutomationRuntimeField(
            beforeEntity.Handle,
            componentIndex,
            fieldName,
            converted,
            out string diagnostic))
        {
            throw StateUnavailable(diagnostic);
        }

        ScriptEntityInspection afterEntity = RequireRuntimeEntityInspection(session, entityId);
        ScriptFieldDescriptor afterField = RequireRuntimeField(
            afterEntity.Components[RequireRuntimeComponentIndex(afterEntity, entityId, componentId)],
            fieldName);
        AutomationRuntimeEntity response = MapRuntimeEntity(session, afterEntity);
        bool changed = !string.Equals(
            currentValue,
            EncodeRuntimeValue(afterField.Value),
            StringComparison.Ordinal);
        JsonElement serialized;
        try
        {
            serialized = JsonSerializer.SerializeToElement(
                response,
                AutomationJsonContext.Default.AutomationRuntimeEntity);
        }
        catch (Exception serializationException)
        {
            if (!session.TryApplyAutomationRuntimeField(
                beforeEntity.Handle,
                componentIndex,
                fieldName,
                field.Value,
                out string rollbackDiagnostic))
            {
                throw new AggregateException(
                    $"Runtime field 响应序列化失败，且 before-image 回滚失败：{rollbackDiagnostic}",
                    serializationException);
            }

            throw;
        }

        AutomationRevisionSnapshot? revision = changed
            ? AdvanceAndCapture(context.Revisions, resources, resources)
            : null;
        return new AutomationOperationResult
        {
            Payload = serialized,
            ResourceIds = resources,
            RevisionOverride = revision,
            StateChanged = changed,
        };
    }

    private AutomationOperationResult GetRuntimeSimulation(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.RuntimeSimulationGetMethod);
        EditorProjectSession session = RequireSession();
        return Result(
            MapRuntimeSimulation(session),
            AutomationJsonContext.Default.AutomationRuntimeSimulationSnapshot,
            SimulationResources(session));
    }

    private AutomationOperationResult SetRuntimeSimulation(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationRuntimeSimulationSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationRuntimeSimulationSetRequest,
            AutomationProtocolConstants.RuntimeSimulationSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.RuntimeSimulationSetMethod);
        if (request.SimulationHz is not (30.0 or 60.0))
        {
            throw Invalid("simulationHz 在 wire v1 只能是 30 或 60。");
        }

        EditorProjectSession session = RequireSession();
        Hosting.SimulationControlSnapshot before = session.CaptureSimulationControl();
        string[] resources = SimulationResources(session);
        if (Math.Abs(before.SimHz - request.SimulationHz) < 0.001)
        {
            return NoChange(
                MapRuntimeSimulation(session, before),
                AutomationJsonContext.Default.AutomationRuntimeSimulationSnapshot,
                resources);
        }

        session.SetSimHz(request.SimulationHz);
        JsonElement serialized;
        try
        {
            serialized = JsonSerializer.SerializeToElement(
                MapRuntimeSimulation(session),
                AutomationJsonContext.Default.AutomationRuntimeSimulationSnapshot);
        }
        catch (Exception operationException)
        {
            try
            {
                session.SetSimHz(before.SimHz);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "Simulation 频率响应失败，且 before-image 回滚失败。",
                    operationException,
                    rollbackException);
            }

            throw;
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

    private AutomationOperationResult InspectRuntimeCell(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationRuntimeCellInspectRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationRuntimeCellInspectRequest,
            AutomationProtocolConstants.RuntimeCellInspectMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.RuntimeCellInspectMethod);
        EditorProjectSession session = RequireSession();
        return session.TryInspectAutomationCell(
            request.WorldX,
            request.WorldY,
            out SimulationCellInspection inspection)
            ? Result(
                MapRuntimeCell(inspection),
                AutomationJsonContext.Default.AutomationRuntimeCellInspection,
                RuntimeWorldResources(session))
            : throw NotFound(
                $"World cell ({request.WorldX}, {request.WorldY}) 所属 chunk 当前未驻留。");
    }

    private AutomationOperationResult ListRuntimeMaterials(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        AutomationPageRequest request = DeserializePage(
            payload,
            AutomationProtocolConstants.RuntimeMaterialListMethod);
        EditorProjectSession session = RequireSession();
        MaterialTable materials = session.Engine.Context.GetService<MaterialTable>();
        AutomationMaterialDefinition[] source = CaptureRuntimeMaterials(materials);
        AutomationMaterialDefinition[] filtered = ApplyFilter(
            source,
            request.Filter,
            MatchRuntimeMaterial);
        SortRuntimeMaterials(filtered, request.Sort);
        string fingerprint = Fingerprint(
            source,
            AutomationJsonContext.Default.AutomationMaterialDefinitionArray);
        PageSlice<AutomationMaterialDefinition> page = SlicePage(
            "runtime.materials",
            fingerprint,
            request,
            filtered);
        return Result(
            new AutomationMaterialListResponse
            {
                Items = page.Items,
                Page = page.Info,
            },
            AutomationJsonContext.Default.AutomationMaterialListResponse,
            [RuntimeMaterialsResource]);
    }

    private AutomationOperationResult GetRuntimeMaterial(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        AutomationMaterialRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationMaterialRequest,
            AutomationProtocolConstants.RuntimeMaterialGetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.RuntimeMaterialGetMethod);
        string name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 256)
        {
            throw Invalid("material name 长度必须为 1..256。");
        }

        EditorProjectSession session = RequireSession();
        MaterialTable materials = session.Engine.Context.GetService<MaterialTable>();
        if (!materials.TryGetId(name, out ushort id) || materials.IsTombstone(id))
        {
            throw NotFound($"Runtime material 不存在：{name}");
        }

        AutomationMaterialDefinition definition = MapRuntimeMaterial(materials, id);
        return Result(
            definition,
            AutomationJsonContext.Default.AutomationMaterialDefinition,
            [RuntimeMaterialsResource, definition.ResourceId]);
    }

    private static AutomationMaterialDefinition[] CaptureRuntimeMaterials(MaterialTable materials)
    {
        List<AutomationMaterialDefinition> result = new(materials.Count);
        for (int i = 0; i < materials.Count; i++)
        {
            ushort id = checked((ushort)i);
            if (!materials.IsTombstone(id))
            {
                result.Add(MapRuntimeMaterial(materials, id));
            }
        }

        return [.. result];
    }

    private static AutomationMaterialDefinition MapRuntimeMaterial(
        MaterialTable materials,
        ushort id)
    {
        ref readonly MaterialDef value = ref materials.Get(id);
        MaterialProperty flags = value.PropertyFlags;
        bool emissive = (flags & MaterialProperty.Emissive) != 0 ||
            value.RenderStyle == MaterialRenderStyle.Emissive;
        bool destructible = id != 0 &&
            value.Type is CellType.Solid or CellType.Powder &&
            (flags & MaterialProperty.Indestructible) == 0;
        AudioCueSet cues = value.AudioCues;
        return new AutomationMaterialDefinition
        {
            ResourceId = RuntimeMaterialResource(value.Name),
            Name = value.Name,
            RuntimeId = id,
            DisplayName = string.IsNullOrWhiteSpace(value.DisplayName)
                ? value.Name
                : value.DisplayName,
            CellType = value.Type.ToString(),
            Density = value.Density,
            Dispersion = value.Dispersion,
            LiquidStatic = value.LiquidStatic,
            LiquidSand = value.LiquidSand,
            Flammability = value.Flammability,
            AutoIgnitionTemp = value.AutoIgnitionTemp,
            FireHp = value.FireHp,
            TemperatureOfFire = value.TemperatureOfFire,
            GeneratesSmoke = value.GeneratesSmoke,
            MeltPoint = FiniteOrNull(value.MeltPoint),
            MeltTargetName = TransitionTargetName(materials, value.MeltPoint, value.MeltTarget),
            FreezePoint = FiniteOrNull(value.FreezePoint),
            FreezeTargetName = TransitionTargetName(materials, value.FreezePoint, value.FreezeTarget),
            BoilPoint = FiniteOrNull(value.BoilPoint),
            BoilTargetName = TransitionTargetName(materials, value.BoilPoint, value.BoilTarget),
            HeatConduct = value.HeatConduct,
            HeatCapacity = value.HeatCapacity,
            DefaultLifetime = value.DefaultLifetime,
            Durability = value.Durability,
            Hardness = value.Hardness,
            MaxIntegrity = value.MaxIntegrity,
            DestroyedTargetName = TryGetLiveMaterialName(materials, value.DestroyedTarget),
            DebrisCount = value.DebrisCount,
            MineYield = value.MineYield,
            TextureId = value.TextureId,
            BaseColorBgra = value.BaseColorBGRA,
            ColorNoise = value.ColorNoise,
            RenderStyle = value.RenderStyle.ToString(),
            LegendCategory = value.LegendCategory.ToString(),
            EdgeColorBgra = value.EdgeColorBGRA,
            Opacity = value.Opacity,
            HighlightColorBgra = value.HighlightColorBGRA,
            LegendVisible = value.LegendVisible,
            PropertyFlags = flags.ToString(),
            Emissive = emissive,
            Destructible = destructible,
            BlocksCharacter = value.Type is CellType.Solid or CellType.Powder,
            ReactionCount = value.ReactionCount,
            ImpactCue = cues.ImpactCue,
            FireCue = cues.FireCue,
            SplashCue = cues.SplashCue,
            ExplosionCue = cues.ExplosionCue,
            ShatterCue = cues.ShatterCue,
            AmbientCue = cues.AmbientCue,
        };
    }

    private static float? FiniteOrNull(float value)
    {
        return float.IsFinite(value) ? value : null;
    }

    private static string? TransitionTargetName(
        MaterialTable materials,
        float threshold,
        ushort targetId)
    {
        return float.IsFinite(threshold) ? TryGetLiveMaterialName(materials, targetId) : null;
    }

    private static string? TryGetLiveMaterialName(MaterialTable materials, ushort id)
    {
        return id < materials.Count && !materials.IsTombstone(id)
            ? materials.GetName(id)
            : null;
    }

    private AutomationOperationResult GetRuntimePhysics(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.RuntimePhysicsGetMethod);
        EditorProjectSession session = RequireSession();
        return Result(
            MapRuntimePhysics(session.CaptureAutomationPhysicsTuning()),
            AutomationJsonContext.Default.AutomationRuntimePhysicsSnapshot,
            RuntimeTuningResources(session, PhysicsResource));
    }

    private AutomationOperationResult SetRuntimePhysics(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationRuntimePhysicsSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationRuntimePhysicsSetRequest,
            AutomationProtocolConstants.RuntimePhysicsSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.RuntimePhysicsSetMethod);
        if (request.SubStepCount is < 1 or > 64 ||
            request.FragmentPixelThreshold is < 0 or > 1_048_576 ||
            !float.IsFinite(request.GravityX) || !float.IsFinite(request.GravityY) ||
            MathF.Abs(request.GravityX) > 1_000_000f || MathF.Abs(request.GravityY) > 1_000_000f)
        {
            throw Invalid("Physics tuning 超出 wire v1 的有限安全范围。");
        }

        EditorProjectSession session = RequireSession();
        PhysicsTuningState before = session.CaptureAutomationPhysicsTuning();
        string[] resources = RuntimeTuningResources(session, PhysicsResource);
        if (before.SubStepCount == request.SubStepCount &&
            before.FragmentPixelThreshold == request.FragmentPixelThreshold &&
            before.GravityX == request.GravityX && before.GravityY == request.GravityY)
        {
            return NoChange(
                MapRuntimePhysics(before),
                AutomationJsonContext.Default.AutomationRuntimePhysicsSnapshot,
                resources);
        }

        PhysicsTuningState desired = before with
        {
            SubStepCount = request.SubStepCount,
            FragmentPixelThreshold = request.FragmentPixelThreshold,
            GravityX = request.GravityX,
            GravityY = request.GravityY,
        };
        JsonElement serialized;
        try
        {
            session.ApplyAutomationPhysicsTuning(desired);
            serialized = JsonSerializer.SerializeToElement(
                MapRuntimePhysics(session.CaptureAutomationPhysicsTuning()),
                AutomationJsonContext.Default.AutomationRuntimePhysicsSnapshot);
        }
        catch (Exception operationException)
        {
            RollbackRuntimeTuning(
                () => session.ApplyAutomationPhysicsTuning(before),
                "Physics tuning",
                operationException);
            throw;
        }

        return ChangedCommand(context, serialized, resources);
    }

    private AutomationOperationResult GetRuntimeParticles(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.RuntimeParticlesGetMethod);
        EditorProjectSession session = RequireSession();
        return Result(
            MapRuntimeParticles(session.CaptureAutomationParticleTuning()),
            AutomationJsonContext.Default.AutomationRuntimeParticlesSnapshot,
            RuntimeTuningResources(session, ParticlesResource));
    }

    private AutomationOperationResult SetRuntimeParticles(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationRuntimeParticlesSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationRuntimeParticlesSetRequest,
            AutomationProtocolConstants.RuntimeParticlesSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.RuntimeParticlesSetMethod);
        EditorProjectSession session = RequireSession();
        ParticleTuningState before = session.CaptureAutomationParticleTuning();
        if (request.MaxCount is < 1 || request.MaxCount > before.Stats.Capacity ||
            request.MaxLifetimeTicks is < 1 or > byte.MaxValue ||
            request.MaxEjectionPerTick is < 0 or > 1_048_576 ||
            !float.IsFinite(request.GravityPerTick) ||
            !float.IsFinite(request.DepositSpeedEpsilon) || request.DepositSpeedEpsilon < 0f ||
            !float.IsFinite(request.EjectionImpulseScale) || request.EjectionImpulseScale < 0f)
        {
            throw Invalid("Particles tuning 超出固定池或 wire v1 的有限安全范围。");
        }

        string[] resources = RuntimeTuningResources(session, ParticlesResource);
        if (before.MaxCount == request.MaxCount &&
            before.GravityPerTick == request.GravityPerTick &&
            before.MaxLifetimeTicks == request.MaxLifetimeTicks &&
            before.DepositSpeedEpsilon == request.DepositSpeedEpsilon &&
            before.EjectionImpulseScale == request.EjectionImpulseScale &&
            before.MaxEjectionPerTick == request.MaxEjectionPerTick)
        {
            return NoChange(
                MapRuntimeParticles(before),
                AutomationJsonContext.Default.AutomationRuntimeParticlesSnapshot,
                resources);
        }

        ParticleTuningState desired = before with
        {
            MaxCount = request.MaxCount,
            GravityPerTick = request.GravityPerTick,
            MaxLifetimeTicks = request.MaxLifetimeTicks,
            DepositSpeedEpsilon = request.DepositSpeedEpsilon,
            EjectionImpulseScale = request.EjectionImpulseScale,
            MaxEjectionPerTick = request.MaxEjectionPerTick,
        };
        JsonElement serialized;
        try
        {
            session.ApplyAutomationParticleTuning(desired);
            ParticleTuningState after = session.CaptureAutomationParticleTuning();
            if (after.MaxCount != desired.MaxCount ||
                after.GravityPerTick != desired.GravityPerTick ||
                after.MaxLifetimeTicks != desired.MaxLifetimeTicks ||
                after.DepositSpeedEpsilon != desired.DepositSpeedEpsilon ||
                after.EjectionImpulseScale != desired.EjectionImpulseScale ||
                after.MaxEjectionPerTick != desired.MaxEjectionPerTick)
            {
                throw Invalid("Particles tuning 被运行时钳制；请求必须使用当前 capacity 与公开范围内的值。");
            }

            serialized = JsonSerializer.SerializeToElement(
                MapRuntimeParticles(after),
                AutomationJsonContext.Default.AutomationRuntimeParticlesSnapshot);
        }
        catch (Exception operationException)
        {
            RollbackRuntimeTuning(
                () => session.ApplyAutomationParticleTuning(before),
                "Particles tuning",
                operationException);
            throw;
        }

        return ChangedCommand(context, serialized, resources);
    }

    private AutomationOperationResult GetRuntimeLighting(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.RuntimeLightingGetMethod);
        EditorProjectSession session = RequireSession();
        return Result(
            MapRuntimeLighting(session.CaptureAutomationLightingTuning()),
            AutomationJsonContext.Default.AutomationRuntimeLightingSnapshot,
            RuntimeTuningResources(session, LightingResource));
    }

    private AutomationOperationResult SetRuntimeLighting(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationRuntimeLightingSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationRuntimeLightingSetRequest,
            AutomationProtocolConstants.RuntimeLightingSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.RuntimeLightingSetMethod);
        if (!Enum.IsDefined(request.Quality) ||
            !float.IsFinite(request.BloomThreshold) || request.BloomThreshold is < 0f or > 8f ||
            !float.IsFinite(request.BloomIntensity) || request.BloomIntensity is < 0f or > 100f ||
            !float.IsFinite(request.Gamma) || request.Gamma is < 0.01f or > 10f)
        {
            throw Invalid("Lighting tuning 超出 wire v1 的有限安全范围。");
        }

        EditorProjectSession session = RequireSession();
        LightingTuningState before = session.CaptureAutomationLightingTuning();
        LightingQualityLevel quality = ToLightingQuality(request.Quality);
        string[] resources = RuntimeTuningResources(session, LightingResource);
        if (before.QualityLevel == quality && before.BloomEnabled == request.BloomEnabled &&
            before.BloomThreshold == request.BloomThreshold &&
            before.BloomIntensity == request.BloomIntensity &&
            before.FogOfWarEnabled == request.FogOfWarEnabled &&
            before.DitherEnabled == request.DitherEnabled && before.Gamma == request.Gamma &&
            before.RadianceCascadesEnabled == request.RadianceCascadesEnabled)
        {
            return NoChange(
                MapRuntimeLighting(before),
                AutomationJsonContext.Default.AutomationRuntimeLightingSnapshot,
                resources);
        }

        LightingTuningState desired = before with
        {
            QualityLevel = quality,
            BloomEnabled = request.BloomEnabled,
            BloomThreshold = request.BloomThreshold,
            BloomIntensity = request.BloomIntensity,
            FogOfWarEnabled = request.FogOfWarEnabled,
            DitherEnabled = request.DitherEnabled,
            Gamma = request.Gamma,
            RadianceCascadesEnabled = request.RadianceCascadesEnabled,
        };
        JsonElement serialized;
        try
        {
            session.ApplyAutomationLightingTuning(desired);
            serialized = JsonSerializer.SerializeToElement(
                MapRuntimeLighting(session.CaptureAutomationLightingTuning()),
                AutomationJsonContext.Default.AutomationRuntimeLightingSnapshot);
        }
        catch (Exception operationException)
        {
            RollbackRuntimeTuning(
                () => session.ApplyAutomationLightingTuning(before),
                "Lighting tuning",
                operationException);
            throw;
        }

        return ChangedCommand(context, serialized, resources);
    }

    private AutomationBackgroundPreparation PrepareRuntimeSaveSlots(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        _ = DeserializePage(payload, AutomationProtocolConstants.RuntimeSaveSlotListMethod);
        EditorProjectSession session = RequireSession();
        string saveRoot = session.CaptureAutomationSaveRoot();
        return new AutomationBackgroundPreparation
        {
            PrepareAsync = cancellationToken => ValueTask.FromResult<object?>(
                EditorWorldSaveLoadService.ListSaveSlots(saveRoot, cancellationToken)),
        };
    }

    private AutomationOperationResult ListRuntimeSaveSlots(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationPageRequest request = DeserializePage(
            payload,
            AutomationProtocolConstants.RuntimeSaveSlotListMethod);
        SaveSlotInfo[] source = context.RequirePreparedState<SaveSlotInfo[]>();
        AutomationSaveSlotInfo[] items = new AutomationSaveSlotInfo[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            SaveSlotInfo slot = source[i];
            items[i] = new AutomationSaveSlotInfo
            {
                SlotId = slot.Id,
                Path = slot.Path,
                LastWriteUtc = slot.TimestampUtc,
                FormatVersion = slot.FormatVersion,
                WorldSeed = slot.WorldSeed,
                GameTimeTicks = slot.GameTimeTicks,
                ChunkCount = slot.ChunkCount,
            };
        }

        AutomationSaveSlotInfo[] filtered = ApplyFilter(items, request.Filter, MatchSaveSlot);
        SortSaveSlots(filtered, request.Sort);
        string fingerprint = Fingerprint(
            items,
            AutomationJsonContext.Default.AutomationSaveSlotInfoArray);
        PageSlice<AutomationSaveSlotInfo> page = SlicePage(
            "runtime.saves",
            fingerprint,
            request,
            filtered);
        AutomationSaveSlotListResponse response = new()
        {
            Items = page.Items,
            Page = page.Info,
        };
        return Result(
            response,
            AutomationJsonContext.Default.AutomationSaveSlotListResponse,
            [RuntimeSavesResource]);
    }

    private AutomationOperationResult GetGamePresentation(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.GamePresentationGetMethod);
        EditorProjectSession session = RequireSession();
        AutomationGamePresentationSnapshot snapshot = MapGamePresentation(
            session.CaptureAutomationGameViewState(),
            session.CaptureScriptedGameViewPresentation());
        return Result(
            snapshot,
            AutomationJsonContext.Default.AutomationGamePresentationSnapshot,
            [GamePresentationResource]);
    }

    private AutomationOperationResult SetGamePresentation(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationGamePresentationSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationGamePresentationSetRequest,
            AutomationProtocolConstants.GamePresentationSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.GamePresentationSetMethod);
        EditorProjectSession session = RequireSession();
        EditorGameViewAutomationState before = session.CaptureAutomationGameViewState();
        EditorGameViewAutomationState target = ToGameViewState(request, before.Diagnostic);
        if (GameViewStatesEqual(before, target))
        {
            AutomationGamePresentationSnapshot current = MapGamePresentation(
                before,
                session.CaptureScriptedGameViewPresentation());
            return NoChange(
                current,
                AutomationJsonContext.Default.AutomationGamePresentationSnapshot,
                [GamePresentationResource]);
        }

        ApplyGameViewStateOrThrow(session, target);
        EditorGameViewAutomationState after = session.CaptureAutomationGameViewState();
        AutomationGamePresentationSnapshot response = MapGamePresentation(
            after,
            session.CaptureScriptedGameViewPresentation());
        return CompleteSettingsWrite(
            "Set Game View Presentation",
            before,
            after,
            value => ApplyGameViewStateOrThrow(session, value),
            response,
            AutomationJsonContext.Default.AutomationGamePresentationSnapshot,
            [GamePresentationResource]);
    }

    private AutomationOperationResult CaptureScene(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.SceneCaptureMethod);
        EditorProjectSession session = RequireSession();
        EditorAutomationFrameCapture capture = session.TryBeginAutomationSceneCapture(
            out EditorAutomationFrameCapture pending,
            out string diagnostic)
                ? pending
                : throw StateUnavailable(diagnostic);

        return new AutomationOperationResult
        {
            ResourceIds = [
                EditorAutomationRuntime.CreateSceneResourceId(session),
                SceneViewResource(session),
                ArtifactResource(context.Request.SessionId),
            ],
            DeferredPayloadFactory = (sourceRevision, cancellationToken) =>
                BuildFrameCaptureArtifactAsync(
                    context.Request.SessionId,
                    context.Request.RequestId,
                    AutomationProtocolConstants.SceneCaptureMethod,
                    capture,
                    sourceRevision,
                    cancellationToken),
        };
    }

    private AutomationOperationResult CaptureGame(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.GameCaptureMethod);
        EditorProjectSession session = RequireSession();
        EditorAutomationFrameCapture capture = session.TryBeginAutomationGameCapture(
            out EditorAutomationFrameCapture pending,
            out string diagnostic)
                ? pending
                : throw StateUnavailable(diagnostic);

        return new AutomationOperationResult
        {
            ResourceIds = [GamePresentationResource, ArtifactResource(context.Request.SessionId)],
            DeferredPayloadFactory = (sourceRevision, cancellationToken) =>
                BuildFrameCaptureArtifactAsync(
                    context.Request.SessionId,
                    context.Request.RequestId,
                    AutomationProtocolConstants.GameCaptureMethod,
                    capture,
                    sourceRevision,
                    cancellationToken),
        };
    }

    private AutomationOperationResult GetProfiler(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.ProfilerGetMethod);
        AutomationProfilerSnapshot snapshot = CaptureProfiler(RequireSession());
        return Result(
            snapshot,
            AutomationJsonContext.Default.AutomationProfilerSnapshot,
            [ProfilerResource]);
    }

    private AutomationOperationResult ExportProfiler(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.ProfilerExportMethod);
        AutomationProfilerSnapshot snapshot = CaptureProfiler(RequireSession());
        string sessionId = context.Request.SessionId;
        string requestId = context.Request.RequestId;
        return new AutomationOperationResult
        {
            ResourceIds = [ProfilerResource, ArtifactResource(sessionId)],
            DeferredPayloadFactory = (revision, cancellationToken) =>
                BuildProfilerExportAsync(sessionId, requestId, snapshot, revision, cancellationToken),
        };
    }

    private AutomationOperationResult SetProfilerVSync(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationProfilerVSyncSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationProfilerVSyncSetRequest,
            AutomationProtocolConstants.ProfilerVSyncSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.ProfilerVSyncSetMethod);
        EditorProjectSession session = RequireSession();
        RuntimeSettingsSnapshot before = session.CaptureAutomationRuntimeSettings();
        AutomationProfilerSnapshot beforeResponse = CaptureProfiler(session);
        if (!before.CanToggleVSync)
        {
            throw StateUnavailable("当前渲染后端不支持运行时切换 VSync。");
        }

        if (before.VSyncEnabled == request.Enabled)
        {
            return NoChange(
                beforeResponse,
                AutomationJsonContext.Default.AutomationProfilerSnapshot,
                [ProfilerResource]);
        }

        RuntimeControlResult applied = session.SetAutomationVSyncEnabled(request.Enabled);
        if (!applied.Success)
        {
            throw StateUnavailable(applied.Message);
        }

        JsonElement serialized;
        try
        {
            AutomationProfilerSnapshot after = CaptureProfiler(session);
            serialized = JsonSerializer.SerializeToElement(
                after,
                AutomationJsonContext.Default.AutomationProfilerSnapshot);
        }
        catch (Exception operationException)
        {
            RuntimeControlResult rollback = session.SetAutomationVSyncEnabled(before.VSyncEnabled);
            if (!rollback.Success)
            {
                throw new AggregateException(
                    $"Profiler VSync 响应失败，且 before-image 回滚失败：{rollback.Message}",
                    operationException);
            }

            throw;
        }

        AutomationRevisionSnapshot revision = AdvanceAndCapture(
            context.Revisions,
            [ProfilerResource],
            [ProfilerResource]);
        return new AutomationOperationResult
        {
            Payload = serialized,
            ResourceIds = [ProfilerResource],
            RevisionOverride = revision,
            StateChanged = true,
        };
    }

    private static AutomationProfilerSnapshot CaptureProfiler(EditorProjectSession session)
    {
        FrameProfiler profiler = session.Engine.Context.Profiler;
        PerformanceHudSample hud = session.CaptureAutomationProfilerSample();
        PerformanceHudHistorySnapshot history = session.CaptureAutomationProfilerHistory();
        RuntimeSettingsSnapshot settings = session.CaptureAutomationRuntimeSettings();
        AutomationProfilerSample[] main = new AutomationProfilerSample[FrameStats.PhaseCount];
        AutomationProfilerSample[] sub = new AutomationProfilerSample[FrameStats.SubPhaseCount];
        ReadOnlySpan<double> lastMain = profiler.LastFrame;
        ReadOnlySpan<double> lastSub = profiler.LastSubFrame;
        FramePhase[] mainPhases = Enum.GetValues<FramePhase>();
        FrameSubPhase[] subPhases = Enum.GetValues<FrameSubPhase>();
        for (int i = 0; i < main.Length; i++)
        {
            main[i] = new AutomationProfilerSample
            {
                Phase = mainPhases[i].ToString(),
                Milliseconds = lastMain[i],
                Average60Milliseconds = profiler.Average(mainPhases[i], 60),
            };
        }

        for (int i = 0; i < sub.Length; i++)
        {
            sub[i] = new AutomationProfilerSample
            {
                Phase = subPhases[i].ToString(),
                Milliseconds = lastSub[i],
            };
        }

        AutomationProfilerHistorySample[] historySamples =
            new AutomationProfilerHistorySample[history.Samples.Length];
        for (int i = 0; i < historySamples.Length; i++)
        {
            PerformanceHudFrameSample frame = history.Samples[i];
            PerformanceHudSample sample = frame.Sample;
            historySamples[i] = new AutomationProfilerHistorySample
            {
                FrameIndex = frame.FrameIndex,
                FrameMilliseconds = sample.TotalFrameMs,
                CaMilliseconds = sample.CaMs,
                PhysicsMilliseconds = sample.PhysicsMs + sample.ShapeRebuildMs,
                RenderMilliseconds = sample.RenderMs + sample.UploadMs,
                AudioMilliseconds = sample.AudioMs,
                CpuMilliseconds = sample.CpuWorkMs,
                GpuMilliseconds = sample.GpuWorkMs,
                WaitMilliseconds = sample.WaitMs,
                EffectiveMilliseconds = sample.EffectiveFrameMs,
                VariableWorkMilliseconds = sample.VariableWorkMs,
                FixedOverheadMilliseconds = sample.FixedOverheadMs,
                ActiveChunks = sample.ActiveChunks,
                ActiveCells = sample.ActiveCells,
                FreeParticles = sample.FreeParticles,
                RigidBodies = sample.RigidBodies,
                DestructionEvents = sample.CellDestructionEvents +
                    sample.RigidBodiesDestroyed +
                    sample.RigidBodiesCreated,
                CustomMetricValue = sample.CustomMetricValue,
                SimulationHz = sample.SimHz,
            };
        }

        return new AutomationProfilerSnapshot
        {
            FrameIndex = session.Engine.Probe.FrameCount,
            WallMilliseconds = hud.TotalFrameMs,
            CpuWorkMilliseconds = hud.CpuWorkMs,
            GpuWorkMilliseconds = hud.GpuWorkMs,
            GpuTimerAvailable = hud.GpuTimerAvailable,
            WaitMilliseconds = hud.WaitMs,
            EffectiveFrameMilliseconds = hud.EffectiveFrameMs,
            EffectiveFramesPerSecond = hud.EffectiveFps,
            BoundType = hud.BoundType,
            VSyncEnabled = settings.VSyncEnabled,
            CanToggleVSync = settings.CanToggleVSync,
            TimeScale = hud.TimeScale,
            DegradationLevel = hud.DegradationLevel,
            DegradationName = hud.DegradationName,
            ConsecutiveOverBudgetFrames = hud.ConsecutiveOverBudgetFrames,
            MainPhases = main,
            SubPhases = sub,
            HistoryCapacity = history.Capacity,
            CapturedSampleCount = history.CapturedSampleCount,
            History = historySamples,
            FrameStatistics = MapProfilerStatistics(history.FrameStatistics),
            CpuStatistics = MapProfilerStatistics(history.CpuStatistics),
            GpuStatistics = MapProfilerStatistics(history.GpuStatistics),
            WaitStatistics = MapProfilerStatistics(history.WaitStatistics),
            EffectiveStatistics = MapProfilerStatistics(history.EffectiveStatistics),
            VariableWorkStatistics = MapProfilerStatistics(history.VariableWorkStatistics),
            FixedOverheadStatistics = MapProfilerStatistics(history.FixedOverheadStatistics),
        };
    }

    private static AutomationProfilerStatistics MapProfilerStatistics(
        PerformanceHudStatistics statistics)
    {
        return new AutomationProfilerStatistics
        {
            SampleCount = statistics.SampleCount,
            AverageMilliseconds = statistics.AverageMs,
            P50Milliseconds = statistics.P50Ms,
            P95Milliseconds = statistics.P95Ms,
            P99Milliseconds = statistics.P99Ms,
            MaxMilliseconds = statistics.MaxMs,
            IsSteady = statistics.IsSteady,
            IsSpike = statistics.IsSpike,
        };
    }

    private AutomationOperationResult GetDebugOverlays(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.DebugOverlayGetMethod);
        EditorProjectSession session = RequireSession();
        DebugOverlaySettings settings = session.Engine.Context.GetService<DebugOverlaySettings>();
        return Result(
            CaptureDebugOverlays(settings),
            AutomationJsonContext.Default.AutomationDebugOverlaySnapshot,
            [DebugOverlayResource]);
    }

    private AutomationOperationResult SetDebugOverlay(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationDebugOverlaySetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationDebugOverlaySetRequest,
            AutomationProtocolConstants.DebugOverlaySetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.DebugOverlaySetMethod);
        if (!Enum.TryParse(request.Flag, ignoreCase: false, out DebugOverlayFlags flag) ||
            flag == DebugOverlayFlags.None ||
            !IsSingleFlag(flag))
        {
            throw Invalid($"未知或非单一 debug overlay flag：'{request.Flag}'。");
        }

        EditorProjectSession session = RequireSession();
        DebugOverlaySettings settings = session.Engine.Context.GetService<DebugOverlaySettings>();
        bool changed = settings.IsEnabled(flag) != request.Enabled;
        if (changed)
        {
            settings.Set(flag, request.Enabled);
        }

        AutomationRevisionSnapshot? revision = changed
            ? AdvanceAndCapture(context.Revisions, [DebugOverlayResource], [DebugOverlayResource])
            : null;
        return Result(
            CaptureDebugOverlays(settings),
            AutomationJsonContext.Default.AutomationDebugOverlaySnapshot,
            [DebugOverlayResource],
            revision);
    }

    private AutomationOperationResult GetPreferences(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.PreferencesGetMethod);
        return Result(
            MapPreferences(_app.Preferences.Current),
            AutomationJsonContext.Default.AutomationEditorPreferences,
            [PreferencesResource]);
    }

    private AutomationOperationResult GetProjectSettings(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.ProjectSettingsGetMethod);
        EditorProjectSession session = RequireSession();
        return Result(
            MapProjectSettings(session, session.CaptureAutomationProjectSettings()),
            AutomationJsonContext.Default.AutomationProjectSettings,
            [ProjectResource, ProjectSettingsResource]);
    }

    private AutomationOperationResult GetPlayerSettings(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.PlayerSettingsGetMethod);
        EditorProjectSession session = RequireSession();
        return Result(
            MapPlayerSettings(session.CaptureAutomationPlayerSettings()),
            AutomationJsonContext.Default.AutomationPlayerSettings,
            [ProjectResource, PlayerSettingsResource]);
    }

    private AutomationOperationResult GetBuildSettings(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        EnsureEmpty(payload, AutomationProtocolConstants.BuildSettingsGetMethod);
        EditorProjectSession session = RequireSession();
        return Result(
            MapBuildSettings(session.CaptureAutomationBuildSettings()),
            AutomationJsonContext.Default.AutomationBuildSettings,
            [ProjectResource, BuildSettingsResource]);
    }

    private AutomationOperationResult ListArtifacts(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationPageRequest request = DeserializePage(
            payload,
            AutomationProtocolConstants.ArtifactListMethod);
        string sessionId = context.Request.SessionId;
        return new AutomationOperationResult
        {
            ResourceIds = [ArtifactResource(sessionId)],
            DeferredPayloadFactory = (revision, cancellationToken) =>
                BuildArtifactListAsync(sessionId, request, revision, cancellationToken),
        };
    }

    private AutomationOperationResult VerifyArtifact(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationArtifactRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationArtifactRequest,
            AutomationProtocolConstants.ArtifactVerifyMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.ArtifactVerifyMethod);
        string artifactId = ValidateOpaqueId(request.ArtifactId, "artifactId");
        string sessionId = context.Request.SessionId;
        return new AutomationOperationResult
        {
            ResourceIds = [ArtifactResource(sessionId), ArtifactResource(sessionId, artifactId)],
            DeferredPayloadFactory = (_, cancellationToken) =>
                VerifyArtifactAsync(sessionId, artifactId, cancellationToken),
        };
    }

    private AutomationOperationResult DeleteArtifact(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        AutomationArtifactRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationArtifactRequest,
            AutomationProtocolConstants.ArtifactDeleteMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.ArtifactDeleteMethod);
        string artifactId = ValidateOpaqueId(request.ArtifactId, "artifactId");
        string sessionId = context.Request.SessionId;
        return new AutomationOperationResult
        {
            ResourceIds = [ArtifactResource(sessionId), ArtifactResource(sessionId, artifactId)],
            DeferredPayloadFactory = (sourceRevision, cancellationToken) =>
                DeleteArtifactAsync(
                    sessionId,
                    artifactId,
                    context.Request.RequestId,
                    sourceRevision,
                    cancellationToken),
        };
    }

    private AutomationOperationResult CompletePlayCommand(
        AutomationScheduledContext context,
        EditorProjectSession session,
        Hosting.EditorPlaySessionResult result,
        bool changed,
        string? previousPlaySessionId = null)
    {
        string[] resources = string.IsNullOrWhiteSpace(previousPlaySessionId)
            ? PlayResources(session)
            :
            [
                .. PlayResources(session)
                    .Append(EditorAutomationRuntime.CreateRuntimeResourceId(previousPlaySessionId))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal),
            ];
        AutomationRevisionSnapshot? revision = changed
            ? AdvanceAndCapture(context.Revisions, resources, resources)
            : null;
        AutomationPlayCommandResult response = new()
        {
            Succeeded = result.Succeeded,
            Diagnostic = result.Message,
            Snapshot = CapturePlay(session),
        };
        return Result(
            response,
            AutomationJsonContext.Default.AutomationPlayCommandResult,
            resources,
            revision);
    }

    private async ValueTask<JsonElement?> BuildAssetPreviewAsync(
        string sessionId,
        string requestId,
        EditorAssetAutomationPreviewPlan plan,
        AutomationRevisionSnapshot sourceRevision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileInfo before = new(plan.FullPath);
        if (!before.Exists || before.Length != plan.Item.SizeBytes || before.LastWriteTimeUtc != plan.Item.LastModifiedUtc.UtcDateTime)
        {
            throw StateUnavailable("资产已在 preview safe point 后变化；请刷新 Asset Database 并重试。");
        }

        AssetBrowserItem previewItem = plan.Item;
        AssetBrowserDetailedPreview preview = EditorAssetPreviewBuilder.Build(in previewItem, plan.FullPath);
        FileInfo afterPreview = new(plan.FullPath);
        if (!afterPreview.Exists || afterPreview.Length != before.Length || afterPreview.LastWriteTimeUtc != before.LastWriteTimeUtc)
        {
            throw StateUnavailable("资产在生成 preview 时变化；请重试。");
        }

        string extension = ResolveArtifactExtension(plan.FullPath);
        string mediaType = ResolveMediaType(plan.FullPath, plan.Item.Kind);
        AutomationArtifactReference artifact = await _artifacts.WriteAsync(
            sessionId,
            extension,
            mediaType,
            sourceRevision,
            async (destination, token) =>
            {
                await using FileStream source = new(
                    plan.FullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await source.CopyToAsync(destination, token).ConfigureAwait(false);
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        PublishArtifactChanged(
            AutomationProtocolConstants.ProjectAssetPreviewMethod,
            sessionId,
            requestId,
            artifact,
            "created");
        AutomationAssetPreviewResult response = new()
        {
            Asset = MapAsset(plan.Item),
            Title = preview.Title,
            ContentKind = MapPreviewKind(preview.ContentKind),
            Summary = preview.Summary,
            Properties =
            [
                .. preview.Properties.Select(static property => new AutomationAssetPreviewProperty
                {
                    Label = property.Label,
                    Value = property.Value,
                }),
            ],
            Diagnostic = preview.Diagnostic,
            Artifact = artifact,
        };
        return JsonSerializer.SerializeToElement(
            response,
            AutomationJsonContext.Default.AutomationAssetPreviewResult);
    }

    private async ValueTask<JsonElement?> ExportConsoleAsync(
        string sessionId,
        string requestId,
        AutomationConsoleEntry[] snapshot,
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
                snapshot,
                AutomationJsonContext.Default.AutomationConsoleEntryArray,
                token)),
            new AutomationArtifactMetadata
            {
                Encoding = "utf-8",
                Data = JsonSerializer.SerializeToElement(new { entryCount = snapshot.Length }),
            },
            cancellationToken).ConfigureAwait(false);
        PublishArtifactChanged(
            AutomationProtocolConstants.ConsoleExportMethod,
            sessionId,
            requestId,
            artifact,
            "created");
        return JsonSerializer.SerializeToElement(
            artifact,
            AutomationJsonContext.Default.AutomationArtifactReference);
    }

    private async ValueTask<JsonElement?> BuildProfilerExportAsync(
        string sessionId,
        string requestId,
        AutomationProfilerSnapshot snapshot,
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
                snapshot,
                AutomationJsonContext.Default.AutomationProfilerSnapshot,
                token)),
            new AutomationArtifactMetadata
            {
                Encoding = "utf-8",
                Data = JsonSerializer.SerializeToElement(new
                {
                    snapshot.FrameIndex,
                    mainPhaseCount = snapshot.MainPhases.Length,
                    subPhaseCount = snapshot.SubPhases.Length,
                }),
            },
            cancellationToken).ConfigureAwait(false);
        PublishArtifactChanged(
            AutomationProtocolConstants.ProfilerExportMethod,
            sessionId,
            requestId,
            artifact,
            "created");
        return JsonSerializer.SerializeToElement(
            artifact,
            AutomationJsonContext.Default.AutomationArtifactReference);
    }

    private async ValueTask<JsonElement?> BuildFrameCaptureArtifactAsync(
        string sessionId,
        string requestId,
        string method,
        EditorAutomationFrameCapture pending,
        AutomationRevisionSnapshot sourceRevision,
        CancellationToken cancellationToken)
    {
        EditorAutomationRawCapture capture;
        try
        {
            capture = await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            throw StateUnavailable(exception.Message);
        }

        AutomationArtifactReference artifact = await _artifacts.WriteAsync(
            sessionId,
            "bmp",
            "image/bmp",
            sourceRevision,
            (stream, token) => EditorAutomationBmpEncoder.WriteAsync(stream, capture, token),
            new AutomationArtifactMetadata
            {
                Width = capture.Width,
                Height = capture.Height,
                Encoding = "bgra8-bottom-up",
                Data = JsonSerializer.SerializeToElement(new
                {
                    kind = capture.Kind,
                    contentRevision = capture.ContentRevision,
                    sourcePixelFormat = "rgba8",
                    imageFormat = "bmp",
                }),
            },
            cancellationToken).ConfigureAwait(false);
        PublishArtifactChanged(method, sessionId, requestId, artifact, "created");
        return JsonSerializer.SerializeToElement(
            artifact,
            AutomationJsonContext.Default.AutomationArtifactReference);
    }

    private async ValueTask<JsonElement?> BuildArtifactListAsync(
        string sessionId,
        AutomationPageRequest request,
        AutomationRevisionSnapshot sourceRevision,
        CancellationToken cancellationToken)
    {
        _ = sourceRevision;
        AutomationArtifactReference[] source = await _artifacts.ListAsync(
            sessionId,
            cancellationToken).ConfigureAwait(false);
        AutomationArtifactReference[] filtered = ApplyFilter(
            source,
            request.Filter,
            MatchArtifact);
        SortArtifacts(filtered, request.Sort);
        string fingerprint = Fingerprint(
            source,
            AutomationJsonContext.Default.AutomationArtifactReferenceArray);
        PageSlice<AutomationArtifactReference> page = SlicePage(
            "artifacts",
            fingerprint,
            request,
            filtered);
        AutomationArtifactListResponse response = new()
        {
            Items = page.Items,
            Page = page.Info,
        };
        return JsonSerializer.SerializeToElement(
            response,
            AutomationJsonContext.Default.AutomationArtifactListResponse);
    }

    private async ValueTask<JsonElement?> VerifyArtifactAsync(
        string sessionId,
        string artifactId,
        CancellationToken cancellationToken)
    {
        bool verified = await _artifacts.VerifyAsync(
            sessionId,
            artifactId,
            cancellationToken).ConfigureAwait(false);
        return JsonSerializer.SerializeToElement(
            new AutomationArtifactVerifyResult
            {
                ArtifactId = artifactId,
                Verified = verified,
            },
            AutomationJsonContext.Default.AutomationArtifactVerifyResult);
    }

    private async ValueTask<JsonElement?> DeleteArtifactAsync(
        string sessionId,
        string artifactId,
        string requestId,
        AutomationRevisionSnapshot sourceRevision,
        CancellationToken cancellationToken)
    {
        bool deleted = await _artifacts.DeleteAsync(
            sessionId,
            artifactId,
            cancellationToken).ConfigureAwait(false);
        if (deleted)
        {
            PublishArtifactChanged(
                AutomationProtocolConstants.ArtifactDeleteMethod,
                sessionId,
                requestId,
                artifactId,
                sourceRevision,
                "deleted");
        }

        return JsonSerializer.SerializeToElement(
            new AutomationArtifactDeleteResult
            {
                ArtifactId = artifactId,
                Deleted = deleted,
            },
            AutomationJsonContext.Default.AutomationArtifactDeleteResult);
    }

    private void PublishArtifactChanged(
        string method,
        string sessionId,
        string requestId,
        AutomationArtifactReference artifact,
        string changeKind)
    {
        PublishArtifactChanged(
            method,
            sessionId,
            requestId,
            artifact.ArtifactId,
            artifact.SourceRevision,
            changeKind);
    }

    private void PublishArtifactChanged(
        string method,
        string sessionId,
        string requestId,
        string artifactId,
        AutomationRevisionSnapshot revision,
        string changeKind)
    {
        AutomationEventHub eventHub = Volatile.Read(ref _eventHub)
            ?? throw new InvalidOperationException("Automation artifact event hub 尚未连接。");
        AutomationStateChangedEvent payload = new()
        {
            SchemaVersion = AutomationProtocolConstants.WireSchemaVersion,
            Method = method,
            ResourceIds = [ArtifactResource(sessionId), ArtifactResource(sessionId, artifactId)],
            ChangeKind = changeKind,
        };
        _ = eventHub.Publish(
            AutomationProtocolConstants.ArtifactChangedEventType,
            revision,
            requestId,
            JsonSerializer.SerializeToElement(
                payload,
                AutomationJsonContext.Default.AutomationStateChangedEvent));
    }

    private EditorProjectSession RequireRuntimeSession()
    {
        EditorProjectSession session = RequireSession();
        return session.CaptureEditorPlaySession().Mode is Hosting.EditorMode.Play or Hosting.EditorMode.Paused &&
            !string.IsNullOrWhiteSpace(session.AutomationPlaySessionId)
                ? session
                : throw StateUnavailable("Runtime 数据只在有效 Play session 中可用。");
    }

    private static ScriptEntityInspection[] CaptureScriptEntities(EditorProjectSession session)
    {
        return session.Engine.Probe.TryGetScriptScene(out Scripting.Scene? scene)
            ? scene.CaptureInspectionSnapshot()
            : [];
    }

    private static EditorGameViewAutomationState ToGameViewState(
        AutomationGamePresentationSetRequest request,
        string currentDiagnostic)
    {
        if (request.CustomPresets is null || request.CustomPresets.Length > 32)
        {
            throw Invalid("Game View customPresets 不得为 null 且最多包含 32 项。");
        }

        EditorGameViewCustomPreset[] custom = new EditorGameViewCustomPreset[request.CustomPresets.Length];
        for (int i = 0; i < custom.Length; i++)
        {
            AutomationGameViewPreset preset = request.CustomPresets[i] ??
                throw Invalid($"Game View customPresets[{i}] 不得为 null。");
            if (!Enum.IsDefined(preset.Kind) ||
                preset.Kind != AutomationGameViewPresetKind.FixedResolution ||
                preset.BuiltIn)
            {
                throw Invalid(
                    $"Game View customPresets[{i}] 必须声明 builtIn=false、kind=FixedResolution。");
            }

            string id = ValidateIdentifier(preset.PresetId, $"customPresets[{i}].presetId", 128);
            string name = ValidateIdentifier(preset.Name, $"customPresets[{i}].name", 128);
            if (!string.Equals(id, preset.PresetId, StringComparison.Ordinal) ||
                !string.Equals(name, preset.Name, StringComparison.Ordinal))
            {
                throw Invalid($"Game View customPresets[{i}] 的 ID 与名称必须是 canonical 未裁剪文本。");
            }

            custom[i] = new EditorGameViewCustomPreset
            {
                Id = id,
                Name = name,
                Width = preset.ValueA,
                Height = preset.ValueB,
            };
        }

        string selectedPresetId = ValidateIdentifier(
            request.SelectedPresetId,
            "selectedPresetId",
            128);
        selectedPresetId = string.Equals(
            selectedPresetId,
            request.SelectedPresetId,
            StringComparison.Ordinal)
                ? selectedPresetId
                : throw Invalid("Game View selectedPresetId 必须是 canonical 未裁剪文本。");

        return new EditorGameViewAutomationState
        {
            PresetId = selectedPresetId,
            ScalePercent = request.ScalePercent,
            PanX = request.PanX,
            PanY = request.PanY,
            MaximizeOnPlay = request.MaximizeOnPlay,
            IsMaximized = request.Maximized,
            CustomPresets = custom,
            Diagnostic = currentDiagnostic,
        };
    }

    private static AutomationGamePresentationSnapshot MapGamePresentation(
        EditorGameViewAutomationState state,
        in ScriptedGameViewPresentationSnapshot presentation)
    {
        int builtInCount = GameViewPresentationPreset.BuiltIns.Length;
        AutomationGameViewPreset[] presets =
            new AutomationGameViewPreset[builtInCount + state.CustomPresets.Length];
        for (int i = 0; i < builtInCount; i++)
        {
            GameViewPresentationPreset preset = GameViewPresentationPreset.BuiltIns[i];
            presets[i] = new AutomationGameViewPreset
            {
                PresetId = preset.Id,
                Name = preset.Label,
                Kind = MapGameViewPresetKind(preset.Kind),
                BuiltIn = true,
                ValueA = preset.ValueA,
                ValueB = preset.ValueB,
            };
        }

        for (int i = 0; i < state.CustomPresets.Length; i++)
        {
            EditorGameViewCustomPreset preset = state.CustomPresets[i];
            presets[builtInCount + i] = new AutomationGameViewPreset
            {
                PresetId = preset.Id,
                Name = preset.Name,
                Kind = AutomationGameViewPresetKind.FixedResolution,
                BuiltIn = false,
                ValueA = preset.Width,
                ValueB = preset.Height,
            };
        }

        bool hasCommittedPresentation = presentation.PresentationWidth > 0 &&
            presentation.PresentationHeight > 0 &&
            presentation.PresentationRevision >= 0;
        PresentationViewport viewport = presentation.WorldContentRect;
        return new AutomationGamePresentationSnapshot
        {
            SelectedPresetId = state.PresetId,
            ScalePercent = state.ScalePercent,
            PanX = state.PanX,
            PanY = state.PanY,
            MaximizeOnPlay = state.MaximizeOnPlay,
            Maximized = state.IsMaximized,
            Presets = presets,
            HasCommittedPresentation = hasCommittedPresentation,
            PresentationWidth = hasCommittedPresentation ? presentation.PresentationWidth : 0,
            PresentationHeight = hasCommittedPresentation ? presentation.PresentationHeight : 0,
            PresentationRevision = hasCommittedPresentation ? presentation.PresentationRevision : 0,
            RequestRevision = hasCommittedPresentation ? presentation.RequestRevision : 0,
            Source = hasCommittedPresentation ? MapGamePresentationSource(presentation.Source) : null,
            WorldContentRect = hasCommittedPresentation
                ? new AutomationPixelRect
                {
                    X = viewport.X,
                    Y = viewport.Y,
                    Width = viewport.Width,
                    Height = viewport.Height,
                }
                : null,
            Diagnostic = state.Diagnostic,
        };
    }

    private static AutomationGameViewPresetKind MapGameViewPresetKind(
        GameViewPresentationPresetKind kind)
    {
        return kind switch
        {
            GameViewPresentationPresetKind.PlayerDefault => AutomationGameViewPresetKind.PlayerDefault,
            GameViewPresentationPresetKind.FreeAspect => AutomationGameViewPresetKind.FreeAspect,
            GameViewPresentationPresetKind.AspectRatio => AutomationGameViewPresetKind.AspectRatio,
            GameViewPresentationPresetKind.FixedResolution => AutomationGameViewPresetKind.FixedResolution,
            _ => throw new InvalidOperationException($"未知 Game View preset kind：{kind}。"),
        };
    }

    private static AutomationGamePresentationSource MapGamePresentationSource(
        GamePresentationSource source)
    {
        return source switch
        {
            GamePresentationSource.PlayerDefault => AutomationGamePresentationSource.PlayerDefault,
            GamePresentationSource.EditorFreeAspect => AutomationGamePresentationSource.EditorFreeAspect,
            GamePresentationSource.EditorAspectRatio => AutomationGamePresentationSource.EditorAspectRatio,
            GamePresentationSource.EditorFixedResolution => AutomationGamePresentationSource.EditorFixedResolution,
            _ => throw new InvalidOperationException($"未知 Game presentation source：{source}。"),
        };
    }

    private static bool GameViewStatesEqual(
        EditorGameViewAutomationState left,
        EditorGameViewAutomationState right)
    {
        if (!string.Equals(left.PresetId, right.PresetId, StringComparison.Ordinal) ||
            left.ScalePercent != right.ScalePercent ||
            left.PanX != right.PanX ||
            left.PanY != right.PanY ||
            left.MaximizeOnPlay != right.MaximizeOnPlay ||
            left.IsMaximized != right.IsMaximized ||
            left.CustomPresets.Length != right.CustomPresets.Length)
        {
            return false;
        }

        for (int i = 0; i < left.CustomPresets.Length; i++)
        {
            if (left.CustomPresets[i] != right.CustomPresets[i])
            {
                return false;
            }
        }

        return true;
    }

    private static AutomationRuntimeEntity[] MapRuntimeEntities(
        EditorProjectSession session,
        ScriptEntityInspection[] source)
    {
        AutomationRuntimeEntity[] result = new AutomationRuntimeEntity[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            result[i] = MapRuntimeEntity(session, source[i]);
        }

        return result;
    }

    private static AutomationRuntimeBody[] CaptureRuntimeBodies(EditorProjectSession session)
    {
        SceneHierarchySnapshot hierarchy = session.CaptureAutomationRuntimeHierarchy();
        List<AutomationRuntimeBody> bodies = new(hierarchy.Bodies.Count);
        for (int i = 0; i < hierarchy.Bodies.Count; i++)
        {
            int bodyKey = hierarchy.Bodies[i].BodyKey;
            if (session.TryGetAutomationRuntimeBody(bodyKey, out RigidBodySnapshot body))
            {
                bodies.Add(MapRuntimeBody(session, body));
            }
        }

        bodies.Sort(static (left, right) => left.BodyKey.CompareTo(right.BodyKey));
        return [.. bodies];
    }

    private static AutomationRuntimeBody MapRuntimeBody(
        EditorProjectSession session,
        in RigidBodySnapshot body)
    {
        return new AutomationRuntimeBody
        {
            BodyId = CreateRuntimeBodyId(session, body.BodyKey),
            BodyKey = body.BodyKey,
            PositionX = body.Transform.Position.X,
            PositionY = body.Transform.Position.Y,
            RotationSin = body.Transform.Sin,
            RotationCos = body.Transform.Cos,
            LinearVelocityX = body.LinearVelocityPixelsPerSecond.X,
            LinearVelocityY = body.LinearVelocityPixelsPerSecond.Y,
            AngularVelocityRadiansPerSecond = body.AngularVelocityRadiansPerSecond,
            MaskWidth = body.Mask.Width,
            MaskHeight = body.Mask.Height,
            SolidPixelCount = body.Mask.SolidPixelCount,
        };
    }

    private static AutomationRuntimeSimulationSnapshot MapRuntimeSimulation(
        EditorProjectSession session)
    {
        return MapRuntimeSimulation(session, session.CaptureSimulationControl());
    }

    private static AutomationRuntimeSimulationSnapshot MapRuntimeSimulation(
        EditorProjectSession session,
        in Hosting.SimulationControlSnapshot snapshot)
    {
        return new AutomationRuntimeSimulationSnapshot
        {
            PlaySessionId = session.AutomationPlaySessionId,
            IsPlaying = snapshot.IsPlaying,
            SimulationHz = snapshot.SimHz,
            FrameIndex = snapshot.FrameIndex,
            SimulationTickIndex = snapshot.SimTickIndex,
            RanSimulationThisFrame = snapshot.RunSimThisFrame,
        };
    }

    private static AutomationRuntimeCellInspection MapRuntimeCell(
        in SimulationCellInspection inspection)
    {
        return new AutomationRuntimeCellInspection
        {
            WorldX = inspection.WorldX,
            WorldY = inspection.WorldY,
            ChunkX = inspection.ChunkCoord.X,
            ChunkY = inspection.ChunkCoord.Y,
            LocalX = inspection.LocalX,
            LocalY = inspection.LocalY,
            MaterialId = inspection.MaterialId,
            MaterialName = inspection.MaterialName,
            TemperatureAvailable = inspection.TemperatureAvailable,
            TemperatureCelsius = inspection.TemperatureCelsius,
            RawFlags = inspection.Flags.Raw,
            Parity = inspection.Flags.Parity,
            Settled = inspection.Flags.Settled,
            Burning = inspection.Flags.Burning,
            FreeFalling = inspection.Flags.FreeFalling,
            RigidOwned = inspection.Flags.RigidOwned,
            BodyKey = inspection.BodyId,
            CurrentDirty = MapDirtyRect(inspection.CurrentDirty),
            WorkingDirty = MapDirtyRect(inspection.WorkingDirty),
            ChunkState = inspection.ChunkState.ToString(),
            ChunkParity = inspection.ChunkParity,
        };
    }

    private static AutomationDirtyRectSnapshot MapDirtyRect(in DirtyRect rect)
    {
        return new AutomationDirtyRectSnapshot
        {
            IsEmpty = rect.IsEmpty,
            MinX = rect.MinX,
            MinY = rect.MinY,
            MaxX = rect.MaxX,
            MaxY = rect.MaxY,
        };
    }

    private static AutomationRuntimePhysicsSnapshot MapRuntimePhysics(PhysicsTuningState state)
    {
        PhysicsSystemStats stats = state.Stats;
        RigidDestructionResult destruction = stats.LastDestructionResult;
        return new AutomationRuntimePhysicsSnapshot
        {
            PixelsPerMeter = state.PixelsPerMeter,
            SubStepCount = state.SubStepCount,
            WorkerCount = state.WorkerCount,
            FragmentPixelThreshold = state.FragmentPixelThreshold,
            RebuildThrottleTicks = state.RebuildThrottleTicks,
            GravityX = state.GravityX,
            GravityY = state.GravityY,
            ActiveBodyCount = stats.ActiveBodyCount,
            PendingDamageCount = stats.PendingDamageCount,
            LastErasedCellCount = stats.LastErasedCellCount,
            LastStampedCellCount = stats.LastStampedCellCount,
            DamagedBodyCount = destruction.DamagedBodies,
            DestroyedBodyCount = destruction.DestroyedBodies,
            CreatedBodyCount = destruction.CreatedBodies,
            FragmentPixelCount = destruction.FragmentPixels,
            SkippedSleepingBodyCount = destruction.SkippedSleepingBodies,
            TaskBridgeWorkerCount = stats.TaskBridgeWorkerCount,
            TaskBridgeFaultedCallbackCount = stats.TaskBridgeFaultedCallbackCount,
        };
    }

    private static AutomationRuntimeParticlesSnapshot MapRuntimeParticles(ParticleTuningState state)
    {
        return new AutomationRuntimeParticlesSnapshot
        {
            MaxCount = state.MaxCount,
            GravityPerTick = state.GravityPerTick,
            MaxLifetimeTicks = state.MaxLifetimeTicks,
            DepositSpeedEpsilon = state.DepositSpeedEpsilon,
            EjectionImpulseScale = state.EjectionImpulseScale,
            MaxEjectionPerTick = state.MaxEjectionPerTick,
            ActiveCount = state.Stats.ActiveCount,
            Capacity = state.Stats.Capacity,
            SpawnedThisTick = state.Stats.SpawnedThisTick,
            DepositedThisTick = state.Stats.DepositedThisTick,
            KilledByLifetimeThisTick = state.Stats.KilledByLifetimeThisTick,
            DroppedThisTick = state.Stats.DroppedThisTick,
            AudioEventsDroppedThisTick = state.Stats.AudioEventsDroppedThisTick,
            CellDestructionEventsThisTick = state.Stats.CellDestructionEventsThisTick,
        };
    }

    private static AutomationRuntimeLightingSnapshot MapRuntimeLighting(LightingTuningState state)
    {
        return new AutomationRuntimeLightingSnapshot
        {
            Quality = state.QualityLevel switch
            {
                LightingQualityLevel.Full => AutomationLightingQuality.Full,
                LightingQualityLevel.BloomDisabled => AutomationLightingQuality.BloomDisabled,
                LightingQualityLevel.FogOfWarEmissiveOnly => AutomationLightingQuality.FogOfWarEmissiveOnly,
                _ => throw new InvalidOperationException("未知 Lighting quality。"),
            },
            BloomEnabled = state.BloomEnabled,
            BloomThreshold = state.BloomThreshold,
            BloomIntensity = state.BloomIntensity,
            FogOfWarEnabled = state.FogOfWarEnabled,
            DitherEnabled = state.DitherEnabled,
            Gamma = state.Gamma,
            RadianceCascadesEnabled = state.RadianceCascadesEnabled,
        };
    }

    private static LightingQualityLevel ToLightingQuality(AutomationLightingQuality quality)
    {
        return quality switch
        {
            AutomationLightingQuality.Full => LightingQualityLevel.Full,
            AutomationLightingQuality.BloomDisabled => LightingQualityLevel.BloomDisabled,
            AutomationLightingQuality.FogOfWarEmissiveOnly => LightingQualityLevel.FogOfWarEmissiveOnly,
            _ => throw Invalid("未知 Lighting quality。"),
        };
    }

    private static AutomationRuntimeEntity MapRuntimeEntity(
        EditorProjectSession session,
        in ScriptEntityInspection entity)
    {
        string entityId =
            $"play:{session.AutomationPlaySessionId}:entity:{entity.EntityId.ToString(CultureInfo.InvariantCulture)}";
        ScriptComponentInspection[] orderedComponents =
        [
            .. entity.Components.OrderBy(static component => component.TypeName, StringComparer.Ordinal),
        ];
        AutomationRuntimeComponent[] components = new AutomationRuntimeComponent[orderedComponents.Length];
        for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
        {
            ScriptComponentInspection component = orderedComponents[componentIndex];
            components[componentIndex] = new AutomationRuntimeComponent
            {
                ComponentId = CreateRuntimeComponentId(entityId, component.TypeName),
                TypeName = component.TypeName,
                Enabled = component.Enabled,
                Faulted = component.Faulted,
                Fields = CaptureRuntimeFields(component.Behaviour),
            };
        }

        return new AutomationRuntimeEntity
        {
            EntityId = entityId,
            NumericId = entity.EntityId,
            Handle = entity.Handle,
            Transform = entity.Transform is null
                ? null
                : new AutomationRuntimeTransform
                {
                    X = entity.Transform.X,
                    Y = entity.Transform.Y,
                    RotationRadians = entity.Transform.RotationRadians,
                    ScaleX = entity.Transform.ScaleX,
                    ScaleY = entity.Transform.ScaleY,
                },
            Components = components,
        };
    }

    private static AutomationInspectorField[] CaptureRuntimeFields(Behaviour behaviour)
    {
        ScriptFieldDescriptor[] fields = ScriptInspector.InspectFields(behaviour);
        AutomationInspectorField[] result = new AutomationInspectorField[fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            ScriptFieldDescriptor field = fields[i];
            Type normalizedType = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
            result[i] = new AutomationInspectorField
            {
                Name = field.Name,
                ValueType = field.FieldType.FullName ?? field.FieldType.Name,
                Kind = field.Kind.ToString(),
                CanWrite = IsRuntimeFieldWritable(field),
                Public = field.IsPublic,
                SerializedPrivate = field.IsSerializedPrivate,
                Nullable = !field.FieldType.IsValueType || Nullable.GetUnderlyingType(field.FieldType) is not null,
                RangeMinimum = field.RangeMinimum,
                RangeMaximum = field.RangeMaximum,
                EnumNames = normalizedType.IsEnum ? Enum.GetNames(normalizedType) : [],
                AssetKind = field.AssetKind?.ToString(),
                Value = EncodeFieldValue(field),
                Overridden = false,
            };
        }

        return result;
    }

    private static ScriptEntityInspection RequireRuntimeEntityInspection(
        EditorProjectSession session,
        string entityId)
    {
        ScriptEntityInspection[] entities = CaptureScriptEntities(session);
        for (int i = 0; i < entities.Length; i++)
        {
            string candidate =
                $"play:{session.AutomationPlaySessionId}:entity:{entities[i].EntityId.ToString(CultureInfo.InvariantCulture)}";
            if (string.Equals(candidate, entityId, StringComparison.Ordinal))
            {
                return entities[i];
            }
        }

        throw NotFound($"Runtime entity '{entityId}' 不存在于当前 Play session。");
    }

    private static int RequireRuntimeComponentIndex(
        in ScriptEntityInspection entity,
        string entityId,
        string componentId)
    {
        ArgumentNullException.ThrowIfNull(componentId);
        string normalized = componentId.Trim();
        if (normalized.Length is < 1 or > 1024 || normalized.Any(char.IsControl))
        {
            throw Invalid("componentId 必须是 1..1024 字符且不得包含控制字符。");
        }

        for (int i = 0; i < entity.Components.Length; i++)
        {
            if (string.Equals(
                CreateRuntimeComponentId(entityId, entity.Components[i].TypeName),
                normalized,
                StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw NotFound($"Runtime component '{normalized}' 不存在于 entity '{entityId}'。");
    }

    private static ScriptFieldDescriptor RequireRuntimeField(
        in ScriptComponentInspection component,
        string fieldName)
    {
        ScriptFieldDescriptor[] fields = ScriptInspector.InspectFields(component.Behaviour);
        for (int i = 0; i < fields.Length; i++)
        {
            if (string.Equals(fields[i].Name, fieldName, StringComparison.Ordinal))
            {
                return fields[i];
            }
        }

        throw NotFound($"Runtime component '{component.TypeName}' 不存在字段 '{fieldName}'。");
    }

    private static bool IsRuntimeFieldWritable(in ScriptFieldDescriptor field)
    {
        return field.CanWrite && field.Kind is ScriptFieldKind.Boolean or
            ScriptFieldKind.Number or ScriptFieldKind.String or
            ScriptFieldKind.Enum or ScriptFieldKind.Vector;
    }

    private static void ValidateRuntimeFieldValue(
        in ScriptFieldDescriptor field,
        object? value,
        string serialized)
    {
        if (field.Kind == ScriptFieldKind.String && serialized.Length > 256)
        {
            throw new InvalidOperationException("Runtime string field 最多 256 字符，与 Inspector 输入上限一致。");
        }

        if (field.Kind != ScriptFieldKind.Number || value is null)
        {
            return;
        }

        double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        if (!double.IsFinite(number) ||
            (field.RangeMinimum is { } minimum && number < minimum) ||
            (field.RangeMaximum is { } maximum && number > maximum))
        {
            throw new InvalidOperationException(
                $"Runtime number field 超出有限范围 [{field.RangeMinimum}, {field.RangeMaximum}]。");
        }
    }

    private static string EncodeRuntimeValue(object? value)
    {
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

    internal static string CreateRuntimeComponentId(string entityId, string typeName)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(typeName);
        try
        {
            return $"{entityId}:component:{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))}";
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static AutomationAssetInfo MapAsset(in AssetBrowserItem item)
    {
        if (string.IsNullOrWhiteSpace(item.AssetId))
        {
            throw StateUnavailable($"Asset '{item.Path}' 缺少 stable asset id。");
        }

        AssetBrowserDescriptor descriptor = item.Descriptor ?? new AssetBrowserDescriptor(
            item.Kind.ToString(),
            item.PreviewSummary ?? string.Empty);
        return new AutomationAssetInfo
        {
            AssetId = item.AssetId,
            Path = item.Path,
            Kind = MapAssetKind(item.Kind),
            SizeBytes = item.SizeBytes,
            LastModifiedUtc = item.LastModifiedUtc,
            DisplayName = item.DisplayName,
            PreviewSummary = item.PreviewSummary ?? string.Empty,
            TypeLabel = descriptor.TypeLabel,
            Purpose = descriptor.Purpose,
            Badges = MapBadges(descriptor.Badges),
        };
    }

    private static AssetBrowserItem RequireAsset(EditorAssetBrowserDataSource assets, string assetId)
    {
        string normalized = ValidateAssetId(assetId);
        IReadOnlyList<AssetBrowserItem> items = assets.ListAssets();
        for (int i = 0; i < items.Count; i++)
        {
            if (string.Equals(items[i].AssetId, normalized, StringComparison.Ordinal))
            {
                return items[i];
            }
        }

        throw NotFound($"Asset '{normalized}' 不存在。");
    }

    private static string ValidateAssetId(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string normalized = value.Trim();
        return normalized.Length is < 1 or > 512 || normalized.Any(char.IsControl)
            ? throw Invalid("assetId 必须是 1..512 字符且不得包含控制字符。")
            : normalized;
    }

    private static string ValidateOpaqueId(string value, string field)
    {
        ArgumentNullException.ThrowIfNull(value);
        string normalized = value.Trim();
        return normalized.Length is < 1 or > 128 || normalized.Any(static character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')
            ? throw Invalid($"{field} 必须是 1..128 个 ASCII 字母、数字、'-' 或 '_'。")
            : normalized;
    }

    private static AutomationConsoleEntry MapConsole(in EditorConsoleRow row)
    {
        return new AutomationConsoleEntry
        {
            EntryId = $"console:{row.Sequence.ToString(CultureInfo.InvariantCulture)}",
            Sequence = row.Sequence,
            Timestamp = row.Entry.Timestamp,
            Category = (AutomationConsoleCategory)row.Entry.Category,
            Severity = (AutomationConsoleSeverity)row.Entry.Severity,
            Source = row.Entry.Source,
            Text = row.Entry.Text,
            Details = row.Entry.Details,
            FilePath = row.Entry.FilePath,
            Line = row.Entry.Line,
            Column = row.Entry.Column,
            FrameIndex = row.Entry.FrameIndex,
        };
    }

    private AutomationConsoleSelectionSnapshot CaptureConsoleSelection()
    {
        EditorConsoleSelectionSnapshot selection = _app.ConsoleSelection.Capture();
        if (!selection.Sequence.HasValue)
        {
            return new AutomationConsoleSelectionSnapshot();
        }

        EditorConsoleOptionsSnapshot options = _app.ConsoleOptions.Capture();
        EditorConsoleFilter? filter = string.IsNullOrWhiteSpace(options.Search)
            ? null
            : new EditorConsoleFilter { SearchContains = options.Search };
        EditorConsoleRow[] rows = _app.ConsoleStore.SnapshotRows(
            filter,
            newestFirst: false,
            collapse: options.Collapse);
        for (int i = 0; i < rows.Length; i++)
        {
            EditorConsoleRow row = rows[i];
            if (EditorConsoleActions.IsSeverityVisible(row.Entry.Severity, options) &&
                EditorConsolePanel.RowMatchesSelection(
                    row,
                    options.Collapse,
                    selection.Sequence,
                    selection.CollapseKey))
            {
                AutomationConsoleEntry entry = MapConsole(row);
                return new AutomationConsoleSelectionSnapshot
                {
                    EntryId = entry.EntryId,
                    Entry = entry,
                };
            }
        }

        return new AutomationConsoleSelectionSnapshot();
    }

    private EditorConsoleRow RequireConsoleRow(string entryId)
    {
        long sequence = ParseConsoleEntryId(entryId);
        EditorConsoleRow[] rows = _app.ConsoleStore.SnapshotRows();
        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i].Sequence == sequence)
            {
                return rows[i];
            }
        }

        throw NotFound($"Console entry '{entryId}' 不存在或已从环形缓冲淘汰。");
    }

    private static EditorConsoleSelectionSnapshot ToSelection(in EditorConsoleRow row)
    {
        return new EditorConsoleSelectionSnapshot(row.Sequence, row.Entry.CollapseKey);
    }

    private static long ParseConsoleEntryId(string entryId)
    {
        const string Prefix = "console:";
        string value = entryId?.Trim() ?? string.Empty;
        return value.StartsWith(Prefix, StringComparison.Ordinal) &&
            long.TryParse(
                value.AsSpan(Prefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long sequence) &&
            sequence >= 0
                ? sequence
                : throw Invalid("entryId 必须使用稳定格式 'console:<non-negative-sequence>'。");
    }

    private AutomationConsoleCounts CaptureConsoleCounts()
    {
        EditorConsoleCounts counts = _app.ConsoleStore.CaptureCounts();
        return new AutomationConsoleCounts
        {
            Logs = counts.Logs,
            Warnings = counts.Warnings,
            Errors = counts.Errors,
            LastSequence = _app.ConsoleStore.LastSequence,
        };
    }

    private static AutomationConsoleOptions MapConsoleOptions(
        in EditorConsoleOptionsSnapshot options)
    {
        return new AutomationConsoleOptions
        {
            Search = options.Search,
            Collapse = options.Collapse,
            ClearOnPlay = options.ClearOnPlay,
            ErrorPause = options.ErrorPause,
            ShowLogs = options.ShowLogs,
            ShowWarnings = options.ShowWarnings,
            ShowErrors = options.ShowErrors,
            AutoScroll = options.AutoScroll,
        };
    }

    private static AutomationPlaySnapshot CapturePlay(EditorProjectSession session)
    {
        Hosting.EditorPlaySessionSnapshot snapshot = session.CaptureEditorPlaySession();
        return new AutomationPlaySnapshot
        {
            PlaySessionId = session.AutomationPlaySessionId,
            Mode = snapshot.Mode switch
            {
                Hosting.EditorMode.Edit => AutomationEditorMode.Edit,
                Hosting.EditorMode.Play => AutomationEditorMode.Play,
                Hosting.EditorMode.Paused => AutomationEditorMode.Paused,
                _ => throw new InvalidOperationException($"未知 Editor mode：{snapshot.Mode}。"),
            },
            Source = snapshot.Mode == Hosting.EditorMode.Edit
                ? null
                : snapshot.Source == Hosting.EditorPlaySource.CurrentState
                    ? AutomationPlaySource.CurrentState
                    : AutomationPlaySource.TemporarySnapshot,
            TemporarySnapshotActive = snapshot.TemporarySnapshotActive,
            FrameIndex = session.Engine.Probe.FrameCount,
            Status = snapshot.StatusMessage,
        };
    }

    private static AutomationDebugOverlaySnapshot CaptureDebugOverlays(DebugOverlaySettings settings)
    {
        DebugOverlayFlags[] flags = Enum.GetValues<DebugOverlayFlags>();
        string[] available =
        [
            .. flags.Where(static flag => flag != DebugOverlayFlags.None).Select(static flag => flag.ToString()),
        ];
        string[] enabled =
        [
            .. flags.Where(flag => flag != DebugOverlayFlags.None && settings.IsEnabled(flag))
                .Select(static flag => flag.ToString()),
        ];
        return new AutomationDebugOverlaySnapshot
        {
            AvailableFlags = available,
            EnabledFlags = enabled,
        };
    }

    private static bool IsSingleFlag(DebugOverlayFlags flag)
    {
        ushort value = (ushort)flag;
        return (value & (value - 1)) == 0;
    }

    private static string[] PlayResources(EditorProjectSession session)
    {
        return
        [
            PlayResource,
            RuntimeResource(session),
            EditorAutomationRuntime.CreateSceneResourceId(session),
        ];
    }

    private static string[] RuntimeResources(EditorProjectSession session)
    {
        return [PlayResource, RuntimeResource(session), ProfilerResource];
    }

    private static string[] SimulationResources(EditorProjectSession session)
    {
        return string.IsNullOrWhiteSpace(session.AutomationPlaySessionId)
            ? [SimulationResource, ProfilerResource]
            : [SimulationResource, ProfilerResource, RuntimeResource(session)];
    }

    private static string[] RuntimeWorldResources(EditorProjectSession session)
    {
        return string.IsNullOrWhiteSpace(session.AutomationPlaySessionId)
            ? [RuntimeWorldResource, SimulationResource]
            : [RuntimeWorldResource, SimulationResource, RuntimeResource(session)];
    }

    private static string[] RuntimeTuningResources(
        EditorProjectSession session,
        string resourceId)
    {
        return string.IsNullOrWhiteSpace(session.AutomationPlaySessionId)
            ? [resourceId, ProfilerResource]
            : [resourceId, ProfilerResource, RuntimeResource(session)];
    }

    private static AutomationOperationResult ChangedCommand(
        AutomationScheduledContext context,
        JsonElement payload,
        string[] resources)
    {
        AutomationRevisionSnapshot revision = AdvanceAndCapture(
            context.Revisions,
            resources,
            resources);
        return new AutomationOperationResult
        {
            Payload = payload,
            ResourceIds = resources,
            RevisionOverride = revision,
            StateChanged = true,
        };
    }

    private static void RollbackRuntimeTuning(
        Action rollback,
        string operation,
        Exception operationException)
    {
        try
        {
            rollback();
        }
        catch (Exception rollbackException)
        {
            throw new AggregateException(
                $"{operation} 失败，且 before-image 回滚失败。",
                operationException,
                rollbackException);
        }
    }

    private static string RuntimeResource(EditorProjectSession session)
    {
        return EditorAutomationRuntime.CreateRuntimeResourceId(session.AutomationPlaySessionId);
    }

    private static string RuntimeMaterialResource(string name)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(name);
        try
        {
            return RuntimeMaterialsResource + ":" + Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(bytes));
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    private static string ValidateRuntimeEntityId(EditorProjectSession session, string entityId)
    {
        ArgumentNullException.ThrowIfNull(entityId);
        string normalized = entityId.Trim();
        string prefix = $"play:{session.AutomationPlaySessionId}:entity:";
        return normalized.Length <= prefix.Length || normalized.Length > 256 ||
            !normalized.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(normalized.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int value) ||
            value <= 0
                ? throw Invalid("entityId 不属于当前 Play session 或格式无效。")
                : normalized;
    }

    private static (string BodyId, int BodyKey) ValidateRuntimeBodyId(
        EditorProjectSession session,
        string bodyId)
    {
        ArgumentNullException.ThrowIfNull(bodyId);
        string normalized = bodyId.Trim();
        string prefix = $"play:{session.AutomationPlaySessionId}:body:";
        return normalized.Length <= prefix.Length || normalized.Length > 256 ||
            !normalized.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(
                normalized.AsSpan(prefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int bodyKey) ||
            bodyKey < 0
                ? throw Invalid("bodyId 不属于当前 Play session 或格式无效。")
                : (normalized, bodyKey);
    }

    private static string CreateRuntimeBodyId(EditorProjectSession session, int bodyKey)
    {
        return $"play:{session.AutomationPlaySessionId}:body:{bodyKey.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string CreateFolderId(string projectId, string path)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(path.ToUpperInvariant());
        try
        {
            return $"project:{projectId}:folder:{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))}";
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    private static AutomationProjectSelectionSnapshot CaptureProjectSelection(
        EditorProjectSession session)
    {
        EditorAutomationSelectionSnapshot selection = session.CaptureAutomationTransactionState().Selection;
        string? folderPath = selection.FolderPath;
        return new AutomationProjectSelectionSnapshot
        {
            AssetId = selection.AssetId,
            AssetPath = selection.AssetPath,
            FolderId = folderPath is null
                ? null
                : CreateFolderId(
                    EditorAutomationRuntime.StableProjectId(session.Project.ProjectRoot),
                    folderPath),
            FolderPath = folderPath,
        };
    }

    private static AutomationProjectWindowSnapshot CaptureProjectWindow(
        EditorProjectSession session,
        AssetBrowserViewState? viewState = null)
    {
        AssetBrowserViewState state = viewState ?? session.CaptureAutomationProjectWindowViewState();
        return new AutomationProjectWindowSnapshot
        {
            Search = state.Search,
            KindFilter = state.KindFilter.HasValue ? MapAssetKind(state.KindFilter.Value) : null,
            SortMode = MapAssetSortMode(state.SortMode),
            ViewMode = MapAssetViewMode(state.ViewMode),
            ThumbnailSize = state.ThumbnailSize,
            MinimumThumbnailSize = AssetBrowserPanel.MinimumThumbnailSize,
            MaximumThumbnailSize = AssetBrowserPanel.MaximumThumbnailSize,
            ActiveFolderPath = session.CaptureAutomationProjectWindowActiveFolderPath(),
        };
    }

    private static void RestoreProjectWindowStateOrThrow(
        EditorProjectSession session,
        in AssetBrowserViewState before,
        Exception original)
    {
        try
        {
            if (!session.ApplyAutomationProjectWindowViewState(before, notifyChanged: false))
            {
                throw new InvalidOperationException("Project Window before state 未形成恢复变化。");
            }
        }
        catch (Exception rollback)
        {
            throw new AggregateException("Project Window 变更失败且无法恢复 before state。", original, rollback);
        }
    }

    private static string ValidateProjectFolder(EditorProjectSession session, string path)
    {
        string normalized = path.Trim().Replace('\\', '/');
        IReadOnlyList<AssetBrowserFolderItem> folders = session.AutomationAssetDatabase.ListFolders();
        for (int i = 0; i < folders.Count; i++)
        {
            if (string.Equals(folders[i].Path, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return folders[i].Path;
            }
        }

        throw NotFound($"Project folder '{normalized}' 不存在。");
    }

    private static string[] ProjectSelectionResources(
        EditorProjectSession session,
        AutomationProjectSelectionSnapshot before,
        AssetBrowserItem? targetAsset,
        string? targetFolder)
    {
        HashSet<string> resources = new(StringComparer.Ordinal)
        {
            ProjectSelectionResource(session),
        };
        if (!string.IsNullOrWhiteSpace(before.AssetId))
        {
            _ = resources.Add(StableAssetResource(session, before.AssetId));
        }

        if (before.FolderPath is not null)
        {
            _ = resources.Add(FolderResource(session, before.FolderPath));
        }

        if (targetAsset is { AssetId: not null } asset)
        {
            _ = resources.Add(StableAssetResource(session, asset.AssetId));
        }

        if (targetFolder is not null)
        {
            _ = resources.Add(FolderResource(session, targetFolder));
        }

        return [.. resources.Order(StringComparer.Ordinal)];
    }

    private static string ProjectSelectionResource(EditorProjectSession session)
    {
        return $"project:{EditorAutomationRuntime.StableProjectId(session.Project.ProjectRoot)}:selection";
    }

    private static bool ClearProjectSelection(EditorProjectSession session)
    {
        session.ClearAutomationProjectSelection();
        return true;
    }

    private static void RestoreProjectSelection(
        EditorProjectSession session,
        AutomationProjectSelectionSnapshot snapshot)
    {
        bool restored = !string.IsNullOrWhiteSpace(snapshot.AssetPath)
            ? session.TrySetAutomationProjectAssetSelection(snapshot.AssetPath)
            : snapshot.FolderPath is not null
                ? session.TrySetAutomationProjectFolderSelection(snapshot.FolderPath)
                : ClearProjectSelection(session);
        if (!restored)
        {
            throw new InvalidOperationException("Project selection before-image 恢复失败。");
        }
    }

    private static AutomationAssetKind MapAssetKind(AssetBrowserItemKind kind)
    {
        return kind switch
        {
            AssetBrowserItemKind.Folder => AutomationAssetKind.Folder,
            AssetBrowserItemKind.Material => AutomationAssetKind.Material,
            AssetBrowserItemKind.Texture => AutomationAssetKind.Texture,
            AssetBrowserItemKind.Audio => AutomationAssetKind.Audio,
            AssetBrowserItemKind.Scene => AutomationAssetKind.Scene,
            AssetBrowserItemKind.Prefab => AutomationAssetKind.Prefab,
            AssetBrowserItemKind.Script => AutomationAssetKind.Script,
            AssetBrowserItemKind.UiScreen => AutomationAssetKind.UiScreen,
            AssetBrowserItemKind.Json => AutomationAssetKind.Json,
            AssetBrowserItemKind.Other => AutomationAssetKind.Other,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知 asset kind。"),
        };
    }

    private static AutomationCodeProjectOpenResult MapCodeProjectOpen(
        in EditorCodeWorkspaceOpenResult result,
        bool filesChanged)
    {
        return new AutomationCodeProjectOpenResult
        {
            Succeeded = result.Success,
            EditorKind = result.ProjectPath is null && result.SolutionPath is null
                ? null
                : result.EditorKind switch
                {
                    ExternalCodeEditorKind.VsCode => AutomationCodeEditorKind.VsCode,
                    ExternalCodeEditorKind.VisualStudio => AutomationCodeEditorKind.VisualStudio,
                    ExternalCodeEditorKind.Rider => AutomationCodeEditorKind.Rider,
                    ExternalCodeEditorKind.SystemDefault => AutomationCodeEditorKind.SystemDefault,
                    ExternalCodeEditorKind.Custom => AutomationCodeEditorKind.Custom,
                    _ => throw new InvalidOperationException($"未知外部代码编辑器类型：{result.EditorKind}。"),
                },
            ProjectPath = result.ProjectPath,
            SolutionPath = result.SolutionPath,
            OpenedTarget = result.OpenedTarget,
            ProjectGenerated = result.ProjectGenerated,
            SolutionGenerated = result.SolutionGenerated,
            FilesChanged = filesChanged,
            Diagnostic = result.Diagnostic,
        };
    }

    private static AssetBrowserItemKind ToAssetKind(AutomationAssetKind kind)
    {
        return kind switch
        {
            AutomationAssetKind.Folder => AssetBrowserItemKind.Folder,
            AutomationAssetKind.Material => AssetBrowserItemKind.Material,
            AutomationAssetKind.Texture => AssetBrowserItemKind.Texture,
            AutomationAssetKind.Audio => AssetBrowserItemKind.Audio,
            AutomationAssetKind.Scene => AssetBrowserItemKind.Scene,
            AutomationAssetKind.Prefab => AssetBrowserItemKind.Prefab,
            AutomationAssetKind.Script => AssetBrowserItemKind.Script,
            AutomationAssetKind.UiScreen => AssetBrowserItemKind.UiScreen,
            AutomationAssetKind.Json => AssetBrowserItemKind.Json,
            AutomationAssetKind.Other => AssetBrowserItemKind.Other,
            _ => throw Invalid("kindFilter 是未知资产类型。"),
        };
    }

    private static AutomationProjectSortMode MapAssetSortMode(AssetBrowserSortMode mode)
    {
        return mode switch
        {
            AssetBrowserSortMode.PathAscending => AutomationProjectSortMode.PathAscending,
            AssetBrowserSortMode.KindThenPath => AutomationProjectSortMode.KindThenPath,
            AssetBrowserSortMode.LastModifiedDescending => AutomationProjectSortMode.LastModifiedDescending,
            AssetBrowserSortMode.SizeDescending => AutomationProjectSortMode.SizeDescending,
            _ => throw new InvalidOperationException($"未知 Project Window 排序模式：{mode}。"),
        };
    }

    private static AssetBrowserSortMode ToAssetSortMode(AutomationProjectSortMode mode)
    {
        return mode switch
        {
            AutomationProjectSortMode.PathAscending => AssetBrowserSortMode.PathAscending,
            AutomationProjectSortMode.KindThenPath => AssetBrowserSortMode.KindThenPath,
            AutomationProjectSortMode.LastModifiedDescending => AssetBrowserSortMode.LastModifiedDescending,
            AutomationProjectSortMode.SizeDescending => AssetBrowserSortMode.SizeDescending,
            _ => throw Invalid("sortMode 是未知排序模式。"),
        };
    }

    private static AutomationProjectViewMode MapAssetViewMode(AssetBrowserViewMode mode)
    {
        return mode switch
        {
            AssetBrowserViewMode.Grid => AutomationProjectViewMode.Grid,
            AssetBrowserViewMode.List => AutomationProjectViewMode.List,
            _ => throw new InvalidOperationException($"未知 Project Window 展示模式：{mode}。"),
        };
    }

    private static AssetBrowserViewMode ToAssetViewMode(AutomationProjectViewMode mode)
    {
        return mode switch
        {
            AutomationProjectViewMode.Grid => AssetBrowserViewMode.Grid,
            AutomationProjectViewMode.List => AssetBrowserViewMode.List,
            _ => throw Invalid("viewMode 是未知展示模式。"),
        };
    }

    private static string StableAssetResource(EditorProjectSession session, string assetId)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(assetId);
        try
        {
            string token = Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(bytes));
            return $"project:{EditorAutomationRuntime.StableProjectId(session.Project.ProjectRoot)}:asset:{token}";
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    private static string FolderResource(EditorProjectSession session, string path)
    {
        string projectId = EditorAutomationRuntime.StableProjectId(session.Project.ProjectRoot);
        return CreateFolderId(projectId, path.Trim().Replace('\\', '/'));
    }

    private static string ArtifactResource(string sessionId)
    {
        return $"automation:session:{sessionId}:artifacts";
    }

    private static string ArtifactResource(string sessionId, string artifactId)
    {
        return $"{ArtifactResource(sessionId)}:{artifactId}";
    }

    private static string[] MapBadges(AssetBrowserBadge badges)
    {
        List<string> result = [];
        foreach (AssetBrowserBadge value in Enum.GetValues<AssetBrowserBadge>())
        {
            if (value != AssetBrowserBadge.None && (badges & value) != 0)
            {
                result.Add(value.ToString());
            }
        }

        return [.. result];
    }

    private static AutomationAssetPreviewKind MapPreviewKind(AssetBrowserPreviewContentKind kind)
    {
        return kind switch
        {
            AssetBrowserPreviewContentKind.Summary => AutomationAssetPreviewKind.Metadata,
            AssetBrowserPreviewContentKind.Image => AutomationAssetPreviewKind.Image,
            AssetBrowserPreviewContentKind.Audio => AutomationAssetPreviewKind.Audio,
            AssetBrowserPreviewContentKind.Text => AutomationAssetPreviewKind.Text,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知 preview kind。"),
        };
    }

    private static string ResolveArtifactExtension(string path)
    {
        string extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(extension) ? "bin" : extension;
    }

    private static string ResolveMediaType(string path, AssetBrowserItemKind kind)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            ".mp3" => "audio/mpeg",
            ".json" => "application/json",
            ".xhtml" => "application/xhtml+xml",
            ".html" => "text/html",
            ".cs" => "text/x-csharp",
            ".scene" or ".prefab" => "application/json",
            _ when kind is AssetBrowserItemKind.Script or AssetBrowserItemKind.UiScreen => "text/plain",
            _ => "application/octet-stream",
        };
    }

    private static bool MatchAsset(AutomationAssetInfo item, AutomationFilterClause clause)
    {
        return clause.Field switch
        {
            "assetId" => MatchString(item.AssetId, clause),
            "path" => MatchString(item.Path, clause),
            "displayName" => MatchString(item.DisplayName, clause),
            "kind" => MatchString(item.Kind.ToString(), clause),
            "sizeBytes" => MatchNumber(item.SizeBytes, clause),
            "typeLabel" => MatchString(item.TypeLabel, clause),
            "purpose" => MatchString(item.Purpose, clause),
            _ => throw Invalid($"Asset filter field '{clause.Field}' 不受支持。"),
        };
    }

    private static void SortAssets(AutomationAssetInfo[] items, AutomationSortClause[] sort)
    {
        ValidateSort(sort);
        Array.Sort(items, (left, right) =>
        {
            for (int i = 0; i < sort.Length; i++)
            {
                int compared = sort[i].Field switch
                {
                    "assetId" => string.CompareOrdinal(left.AssetId, right.AssetId),
                    "path" => CompareText(left.Path, right.Path),
                    "displayName" => CompareText(left.DisplayName, right.DisplayName),
                    "kind" => left.Kind.CompareTo(right.Kind),
                    "sizeBytes" => left.SizeBytes.CompareTo(right.SizeBytes),
                    "lastModifiedUtc" => left.LastModifiedUtc.CompareTo(right.LastModifiedUtc),
                    _ => throw Invalid($"Asset sort field '{sort[i].Field}' 不受支持。"),
                };
                if (compared != 0)
                {
                    return ApplyDirection(compared, sort[i].Direction);
                }
            }

            return string.CompareOrdinal(left.AssetId, right.AssetId);
        });
    }

    private static bool MatchAssetReference(
        AutomationAssetReferenceInfo item,
        AutomationFilterClause clause)
    {
        return clause.Field switch
        {
            "referenceId" => MatchString(item.ReferenceId, clause),
            "location" => MatchString(item.Location, clause),
            "activeScene" => MatchBoolean(item.ActiveScene, clause),
            _ => throw Invalid($"Asset reference filter field '{clause.Field}' 不受支持。"),
        };
    }

    private static void SortAssetReferences(
        AutomationAssetReferenceInfo[] items,
        AutomationSortClause[] sort)
    {
        ValidateSort(sort);
        Array.Sort(items, (left, right) =>
        {
            for (int i = 0; i < sort.Length; i++)
            {
                int compared = sort[i].Field switch
                {
                    "referenceId" => string.CompareOrdinal(left.ReferenceId, right.ReferenceId),
                    "location" => CompareText(left.Location, right.Location),
                    "activeScene" => left.ActiveScene.CompareTo(right.ActiveScene),
                    _ => throw Invalid(
                        $"Asset reference sort field '{sort[i].Field}' 不受支持。"),
                };
                if (compared != 0)
                {
                    return ApplyDirection(compared, sort[i].Direction);
                }
            }

            return string.CompareOrdinal(left.ReferenceId, right.ReferenceId);
        });
    }

    private static string CreateReferenceId(string assetId, string location)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(assetId + "\n" + location);
        try
        {
            return "asset-reference:" + Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(bytes));
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    private static bool MatchFolder(AutomationFolderInfo item, AutomationFilterClause clause)
    {
        return clause.Field switch
        {
            "folderId" => MatchString(item.FolderId, clause),
            "path" => MatchString(item.Path, clause),
            "displayName" => MatchString(item.DisplayName, clause),
            "assetCount" => MatchNumber(item.AssetCount, clause),
            _ => throw Invalid($"Folder filter field '{clause.Field}' 不受支持。"),
        };
    }

    private static void SortFolders(AutomationFolderInfo[] items, AutomationSortClause[] sort)
    {
        ValidateSort(sort);
        Array.Sort(items, (left, right) =>
        {
            for (int i = 0; i < sort.Length; i++)
            {
                int compared = sort[i].Field switch
                {
                    "folderId" => string.CompareOrdinal(left.FolderId, right.FolderId),
                    "path" => CompareText(left.Path, right.Path),
                    "displayName" => CompareText(left.DisplayName, right.DisplayName),
                    "assetCount" => left.AssetCount.CompareTo(right.AssetCount),
                    _ => throw Invalid($"Folder sort field '{sort[i].Field}' 不受支持。"),
                };
                if (compared != 0)
                {
                    return ApplyDirection(compared, sort[i].Direction);
                }
            }

            return string.CompareOrdinal(left.FolderId, right.FolderId);
        });
    }

    private static bool MatchConsole(AutomationConsoleEntry item, AutomationFilterClause clause)
    {
        return clause.Field switch
        {
            "entryId" => MatchString(item.EntryId, clause),
            "sequence" => MatchNumber(item.Sequence, clause),
            "category" => MatchString(item.Category.ToString(), clause),
            "severity" => MatchString(item.Severity.ToString(), clause),
            "source" => MatchString(item.Source, clause),
            "text" => MatchString(item.Text, clause),
            "details" => MatchString(item.Details, clause),
            "filePath" => MatchString(item.FilePath ?? string.Empty, clause),
            "frameIndex" => MatchNumber(item.FrameIndex, clause),
            _ => throw Invalid($"Console filter field '{clause.Field}' 不受支持。"),
        };
    }

    private static void SortConsole(AutomationConsoleEntry[] items, AutomationSortClause[] sort)
    {
        ValidateSort(sort);
        Array.Sort(items, (left, right) =>
        {
            for (int i = 0; i < sort.Length; i++)
            {
                int compared = sort[i].Field switch
                {
                    "sequence" => left.Sequence.CompareTo(right.Sequence),
                    "timestamp" => left.Timestamp.CompareTo(right.Timestamp),
                    "category" => left.Category.CompareTo(right.Category),
                    "severity" => left.Severity.CompareTo(right.Severity),
                    "source" => CompareText(left.Source, right.Source),
                    _ => throw Invalid($"Console sort field '{sort[i].Field}' 不受支持。"),
                };
                if (compared != 0)
                {
                    return ApplyDirection(compared, sort[i].Direction);
                }
            }

            return left.Sequence.CompareTo(right.Sequence);
        });
    }

    private static bool MatchArtifact(
        AutomationArtifactReference item,
        AutomationFilterClause clause)
    {
        return clause.Field switch
        {
            "artifactId" => MatchString(item.ArtifactId, clause),
            "mediaType" => MatchString(item.MediaType, clause),
            "relativePath" => MatchString(item.RelativePath, clause),
            "byteLength" => MatchNumber(item.ByteLength, clause),
            "createdAtUtc" => MatchString(item.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture), clause),
            "encoding" => MatchString(item.Encoding ?? string.Empty, clause),
            _ => throw Invalid($"Artifact filter field '{clause.Field}' 不受支持。"),
        };
    }

    private static void SortArtifacts(
        AutomationArtifactReference[] items,
        AutomationSortClause[] sort)
    {
        ValidateSort(sort);
        Array.Sort(items, (left, right) =>
        {
            for (int i = 0; i < sort.Length; i++)
            {
                int compared = sort[i].Field switch
                {
                    "artifactId" => string.CompareOrdinal(left.ArtifactId, right.ArtifactId),
                    "mediaType" => CompareText(left.MediaType, right.MediaType),
                    "relativePath" => CompareText(left.RelativePath, right.RelativePath),
                    "byteLength" => left.ByteLength.CompareTo(right.ByteLength),
                    "createdAtUtc" => left.CreatedAtUtc.CompareTo(right.CreatedAtUtc),
                    _ => throw Invalid($"Artifact sort field '{sort[i].Field}' 不受支持。"),
                };
                if (compared != 0)
                {
                    return ApplyDirection(compared, sort[i].Direction);
                }
            }

            return string.CompareOrdinal(left.ArtifactId, right.ArtifactId);
        });
    }

    private static bool MatchRuntimeMaterial(
        AutomationMaterialDefinition item,
        AutomationFilterClause clause)
    {
        return clause.Field switch
        {
            "name" => MatchString(item.Name, clause),
            "displayName" => MatchString(item.DisplayName, clause),
            "runtimeId" => MatchNumber(item.RuntimeId, clause),
            "cellType" => MatchString(item.CellType, clause),
            "legendCategory" => MatchString(item.LegendCategory, clause),
            "renderStyle" => MatchString(item.RenderStyle, clause),
            "propertyFlags" => MatchString(item.PropertyFlags, clause),
            "legendVisible" => MatchString(item.LegendVisible.ToString(), clause),
            "emissive" => MatchString(item.Emissive.ToString(), clause),
            "destructible" => MatchString(item.Destructible.ToString(), clause),
            "density" => MatchNumber(item.Density, clause),
            "hardness" => MatchNumber(item.Hardness, clause),
            "reactionCount" => MatchNumber(item.ReactionCount, clause),
            _ => throw Invalid($"Runtime material filter field '{clause.Field}' 不受支持。"),
        };
    }

    private static void SortRuntimeMaterials(
        AutomationMaterialDefinition[] items,
        AutomationSortClause[] sort)
    {
        ValidateSort(sort);
        Array.Sort(items, (left, right) =>
        {
            for (int i = 0; i < sort.Length; i++)
            {
                int compared = sort[i].Field switch
                {
                    "name" => string.CompareOrdinal(left.Name, right.Name),
                    "displayName" => CompareText(left.DisplayName, right.DisplayName),
                    "runtimeId" => left.RuntimeId.CompareTo(right.RuntimeId),
                    "cellType" => string.CompareOrdinal(left.CellType, right.CellType),
                    "legendCategory" => string.CompareOrdinal(left.LegendCategory, right.LegendCategory),
                    "density" => left.Density.CompareTo(right.Density),
                    "hardness" => left.Hardness.CompareTo(right.Hardness),
                    "reactionCount" => left.ReactionCount.CompareTo(right.ReactionCount),
                    _ => throw Invalid($"Runtime material sort field '{sort[i].Field}' 不受支持。"),
                };
                if (compared != 0)
                {
                    return ApplyDirection(compared, sort[i].Direction);
                }
            }

            return string.CompareOrdinal(left.Name, right.Name);
        });
    }

    private static bool MatchRuntimeEntity(AutomationRuntimeEntity item, AutomationFilterClause clause)
    {
        return clause.Field switch
        {
            "entityId" => MatchString(item.EntityId, clause),
            "numericId" => MatchNumber(item.NumericId, clause),
            "handle" => MatchString(item.Handle, clause),
            "componentCount" => MatchNumber(item.Components.Length, clause),
            "hasTransform" => MatchBoolean(item.Transform is not null, clause),
            _ => throw Invalid($"Runtime entity filter field '{clause.Field}' 不受支持。"),
        };
    }

    private static bool MatchRuntimeBody(AutomationRuntimeBody item, AutomationFilterClause clause)
    {
        return clause.Field switch
        {
            "bodyId" => MatchString(item.BodyId, clause),
            "bodyKey" => MatchNumber(item.BodyKey, clause),
            "positionX" => MatchNumber(item.PositionX, clause),
            "positionY" => MatchNumber(item.PositionY, clause),
            "solidPixelCount" => MatchNumber(item.SolidPixelCount, clause),
            _ => throw Invalid($"Runtime body filter field '{clause.Field}' 不受支持。"),
        };
    }

    private static void SortRuntimeBodies(
        AutomationRuntimeBody[] items,
        AutomationSortClause[] sort)
    {
        ValidateSort(sort);
        Array.Sort(items, (left, right) =>
        {
            for (int i = 0; i < sort.Length; i++)
            {
                int compared = sort[i].Field switch
                {
                    "bodyId" => string.CompareOrdinal(left.BodyId, right.BodyId),
                    "bodyKey" => left.BodyKey.CompareTo(right.BodyKey),
                    "positionX" => left.PositionX.CompareTo(right.PositionX),
                    "positionY" => left.PositionY.CompareTo(right.PositionY),
                    "solidPixelCount" => left.SolidPixelCount.CompareTo(right.SolidPixelCount),
                    _ => throw Invalid($"Runtime body sort field '{sort[i].Field}' 不受支持。"),
                };
                if (compared != 0)
                {
                    return ApplyDirection(compared, sort[i].Direction);
                }
            }

            return left.BodyKey.CompareTo(right.BodyKey);
        });
    }

    private static void SortRuntimeEntities(
        AutomationRuntimeEntity[] items,
        AutomationSortClause[] sort)
    {
        ValidateSort(sort);
        Array.Sort(items, (left, right) =>
        {
            for (int i = 0; i < sort.Length; i++)
            {
                int compared = sort[i].Field switch
                {
                    "entityId" => string.CompareOrdinal(left.EntityId, right.EntityId),
                    "numericId" => left.NumericId.CompareTo(right.NumericId),
                    "handle" => CompareText(left.Handle, right.Handle),
                    "componentCount" => left.Components.Length.CompareTo(right.Components.Length),
                    _ => throw Invalid($"Runtime entity sort field '{sort[i].Field}' 不受支持。"),
                };
                if (compared != 0)
                {
                    return ApplyDirection(compared, sort[i].Direction);
                }
            }

            return string.CompareOrdinal(left.EntityId, right.EntityId);
        });
    }

    private static bool MatchSaveSlot(AutomationSaveSlotInfo item, AutomationFilterClause clause)
    {
        return clause.Field switch
        {
            "slotId" => MatchString(item.SlotId, clause),
            "path" => MatchString(item.Path, clause),
            "formatVersion" => MatchNumber(item.FormatVersion, clause),
            "worldSeed" => MatchNumber(item.WorldSeed, clause),
            "gameTimeTicks" => MatchNumber(item.GameTimeTicks, clause),
            "chunkCount" => MatchNumber(item.ChunkCount, clause),
            _ => throw Invalid($"Save slot filter field '{clause.Field}' 不受支持。"),
        };
    }

    private static void SortSaveSlots(
        AutomationSaveSlotInfo[] items,
        AutomationSortClause[] sort)
    {
        ValidateSort(sort);
        Array.Sort(items, (left, right) =>
        {
            for (int i = 0; i < sort.Length; i++)
            {
                int compared = sort[i].Field switch
                {
                    "slotId" => string.CompareOrdinal(left.SlotId, right.SlotId),
                    "path" => string.CompareOrdinal(left.Path, right.Path),
                    "lastWriteUtc" => left.LastWriteUtc.CompareTo(right.LastWriteUtc),
                    "formatVersion" => left.FormatVersion.CompareTo(right.FormatVersion),
                    "worldSeed" => left.WorldSeed.CompareTo(right.WorldSeed),
                    "gameTimeTicks" => left.GameTimeTicks.CompareTo(right.GameTimeTicks),
                    "chunkCount" => left.ChunkCount.CompareTo(right.ChunkCount),
                    _ => throw Invalid($"Save slot sort field '{sort[i].Field}' 不受支持。"),
                };
                if (compared != 0)
                {
                    return ApplyDirection(compared, sort[i].Direction);
                }
            }

            return string.CompareOrdinal(left.SlotId, right.SlotId);
        });
    }

    private AutomationOperationResult CompleteSettingsWrite<TState, TResponse>(
        string name,
        TState before,
        TState after,
        Action<TState> apply,
        TResponse response,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResponse> typeInfo,
        string[] resources)
    {
        JsonElement serialized;
        try
        {
            serialized = JsonSerializer.SerializeToElement(response, typeInfo);
        }
        catch (Exception serializationException)
        {
            try
            {
                apply(before);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    $"{name} 响应序列化失败，且 settings before-image 回滚也失败。",
                    serializationException,
                    rollbackException);
            }

            throw;
        }

        return new AutomationOperationResult
        {
            Payload = serialized,
            ResourceIds = resources,
            UndoAction = new SettingsUndoAction<TState>(name, before, after, apply),
        };
    }

    private static void ApplyGameViewStateOrThrow(
        EditorProjectSession session,
        EditorGameViewAutomationState value)
    {
        if (!session.TryApplyAutomationGameViewState(value, out string diagnostic))
        {
            throw new InvalidOperationException(diagnostic);
        }
    }

    private static AutomationEditorPreferences MapPreferences(EditorPreferencesDocument value)
    {
        return new AutomationEditorPreferences
        {
            UiScale = value.UiScale,
            SaveLayoutOnExit = value.SaveLayoutOnExit,
            ReopenLastProject = value.ReopenLastProject,
            RestoreLastScene = value.RestoreLastScene,
            ExternalScriptEditor = value.ExternalScriptEditor,
            Language = value.Language,
        };
    }

    private static AutomationProjectSettings MapProjectSettings(
        EditorProjectSession session,
        ProjectSettingsDto value)
    {
        List<string> reloadReasons = [];
        if (!string.Equals(
                value.ContentRoot,
                session.AutomationActiveContentRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            reloadReasons.Add("contentRoot");
        }

        if (!string.Equals(
                value.ScriptSourceDir,
                session.AutomationActiveScriptSourceDir,
                StringComparison.OrdinalIgnoreCase))
        {
            reloadReasons.Add("scriptSourceDir");
        }

        return new AutomationProjectSettings
        {
            Name = value.Name,
            ContentRoot = value.ContentRoot,
            ScriptSourceDir = value.ScriptSourceDir,
            ActiveContentRoot = session.AutomationActiveContentRoot,
            ActiveScriptSourceDir = session.AutomationActiveScriptSourceDir,
            StartScene = value.StartScene,
            RequireStableMaterialNames = value.ResourceRules.RequireStableMaterialNames,
            ContentFileGlobs = [.. value.ResourceRules.ContentFileGlobs],
            DefaultUiBackend = MapUiBackend(value.DefaultUiBackend),
            RequiresReload = reloadReasons.Count != 0,
            ReloadReasons = [.. reloadReasons],
        };
    }

    private static ProjectSettingsDto ToProjectSettings(
        AutomationProjectSettingsSetRequest value,
        ProjectSettingsDto current)
    {
        if (value.ContentFileGlobs is null || value.ContentFileGlobs.Length > 1024)
        {
            throw Invalid("contentFileGlobs 不得为 null 且最多 1024 条。");
        }

        try
        {
            return new ProjectSettingsDto
            {
                FormatVersion = ProjectSettingsDto.CurrentFormatVersion,
                Name = value.Name,
                ContentRoot = value.ContentRoot,
                ScriptSourceDir = value.ScriptSourceDir,
                StartScene = value.StartScene,
                ResourceRules = new ProjectResourceRulesDto
                {
                    RequireStableMaterialNames = value.RequireStableMaterialNames,
                    ContentFileGlobs = [.. value.ContentFileGlobs],
                },
                EditorPreferences = current.EditorPreferences,
                DefaultUiBackend = MapUiBackend(value.DefaultUiBackend),
            }.Normalize();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            throw Invalid($"Project Settings 无效：{exception.Message}");
        }
    }

    private static AutomationPlayerSettings MapPlayerSettings(PlayerSettingsDto value)
    {
        return new AutomationPlayerSettings
        {
            WindowTitle = value.WindowTitle,
            WindowWidth = value.WindowWidth,
            WindowHeight = value.WindowHeight,
            WindowMode = value.WindowMode switch
            {
                PlayerWindowMode.Windowed => AutomationPlayerWindowMode.Windowed,
                PlayerWindowMode.MaximizedWindow => AutomationPlayerWindowMode.MaximizedWindow,
                PlayerWindowMode.BorderlessFullscreen => AutomationPlayerWindowMode.BorderlessFullscreen,
                _ => throw new ArgumentOutOfRangeException(nameof(value), value.WindowMode, "未知 Player window mode。"),
            },
            VSync = value.VSync,
            IconPath = value.IconPath,
            Version = value.Version,
            StartupScene = value.StartupScene,
            EnableKeyboardMouse = value.InputDefaults.EnableKeyboardMouse,
            EnableGamepad = value.InputDefaults.EnableGamepad,
            RuntimeUiBackend = MapUiBackend(value.RuntimeUiBackend),
            ReleaseChannel = value.ReleaseChannel switch
            {
                PlayerReleaseChannel.Development => AutomationPlayerReleaseChannel.Development,
                PlayerReleaseChannel.Production => AutomationPlayerReleaseChannel.Production,
                _ => throw new ArgumentOutOfRangeException(nameof(value), value.ReleaseChannel, "未知 Player release channel。"),
            },
        };
    }

    private static PlayerSettingsDto ToPlayerSettings(AutomationPlayerSettings value)
    {
        try
        {
            return new PlayerSettingsDto
            {
                FormatVersion = PlayerSettingsDto.CurrentFormatVersion,
                WindowTitle = value.WindowTitle,
                WindowWidth = value.WindowWidth,
                WindowHeight = value.WindowHeight,
                WindowMode = value.WindowMode switch
                {
                    AutomationPlayerWindowMode.Windowed => PlayerWindowMode.Windowed,
                    AutomationPlayerWindowMode.MaximizedWindow => PlayerWindowMode.MaximizedWindow,
                    AutomationPlayerWindowMode.BorderlessFullscreen => PlayerWindowMode.BorderlessFullscreen,
                    _ => throw new InvalidOperationException($"未知 Player window mode：{value.WindowMode}。"),
                },
                VSync = value.VSync,
                IconPath = value.IconPath,
                Version = value.Version,
                StartupScene = value.StartupScene,
                InputDefaults = new PlayerInputDefaultsDto
                {
                    EnableKeyboardMouse = value.EnableKeyboardMouse,
                    EnableGamepad = value.EnableGamepad,
                },
                RuntimeUiBackend = MapUiBackend(value.RuntimeUiBackend),
                ReleaseChannel = value.ReleaseChannel switch
                {
                    AutomationPlayerReleaseChannel.Development => PlayerReleaseChannel.Development,
                    AutomationPlayerReleaseChannel.Production => PlayerReleaseChannel.Production,
                    _ => throw new InvalidOperationException($"未知 Player release channel：{value.ReleaseChannel}。"),
                },
            }.Normalize();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            throw Invalid($"Player Settings 无效：{exception.Message}");
        }
    }

    private static AutomationBuildSettings MapBuildSettings(BuildProfileDto value)
    {
        return new AutomationBuildSettings
        {
            Rid = value.Rid,
            Channel = value.Channel == BuildProfileChannel.R2R
                ? AutomationBuildChannel.R2R
                : AutomationBuildChannel.Aot,
            Configuration = value.Configuration,
            OutputDirectory = value.OutputDirectory,
            ProductName = value.ProductName,
            Version = value.Version,
            InformationalVersion = value.InformationalVersion,
            IconPath = value.IconPath,
            IncludeSymbols = value.IncludeSymbols,
            PackageWholeContent = value.PackageWholeContent,
            RunAfterBuild = value.RunAfterBuild,
            Scenes =
            [
                .. value.Scenes.Select(static scene => new AutomationBuildScene
                {
                    SceneName = scene.SceneName,
                    Included = scene.Included,
                    IsStartup = scene.IsStartup,
                    SourceKind = scene.SourceKind switch
                    {
                        SceneSourceKind.Empty => AutomationSceneSourceKind.Empty,
                        SceneSourceKind.SaveDirectory => AutomationSceneSourceKind.SaveDirectory,
                        SceneSourceKind.SceneFile => AutomationSceneSourceKind.SceneFile,
                        SceneSourceKind.Procedural => AutomationSceneSourceKind.Procedural,
                        _ => throw new ArgumentOutOfRangeException(nameof(scene), scene.SourceKind, "未知 Scene source kind。"),
                    },
                    Source = scene.Source,
                }),
            ],
        };
    }

    private static BuildProfileDto ToBuildSettings(AutomationBuildSettings value)
    {
        if (value.Scenes is null || value.Scenes.Length > 4096)
        {
            throw Invalid("Build Settings scenes 不得为 null 且最多 4096 条。");
        }

        try
        {
            return new BuildProfileDto
            {
                FormatVersion = BuildProfileDto.CurrentFormatVersion,
                Rid = value.Rid,
                Channel = value.Channel switch
                {
                    AutomationBuildChannel.R2R => BuildProfileChannel.R2R,
                    AutomationBuildChannel.Aot => BuildProfileChannel.Aot,
                    _ => throw new InvalidOperationException($"未知 build channel：{value.Channel}。"),
                },
                Configuration = value.Configuration,
                OutputDirectory = value.OutputDirectory,
                ProductName = value.ProductName,
                Version = value.Version,
                InformationalVersion = value.InformationalVersion,
                IconPath = value.IconPath,
                IncludeSymbols = value.IncludeSymbols,
                PackageWholeContent = value.PackageWholeContent,
                RunAfterBuild = value.RunAfterBuild,
                Scenes =
                [
                    .. value.Scenes.Select(static scene => new BuildProfileSceneDto
                    {
                        SceneName = scene.SceneName,
                        Included = scene.Included,
                        IsStartup = scene.IsStartup,
                        SourceKind = scene.SourceKind switch
                        {
                            AutomationSceneSourceKind.Empty => SceneSourceKind.Empty,
                            AutomationSceneSourceKind.SaveDirectory => SceneSourceKind.SaveDirectory,
                            AutomationSceneSourceKind.SceneFile => SceneSourceKind.SceneFile,
                            AutomationSceneSourceKind.Procedural => SceneSourceKind.Procedural,
                            _ => throw new InvalidOperationException($"未知 Scene source kind：{scene.SourceKind}。"),
                        },
                        Source = scene.Source,
                    }),
                ],
            }.Normalize();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            throw Invalid($"Build Settings 无效：{exception.Message}");
        }
    }

    private static AutomationUiBackend MapUiBackend(UiBackendKind value)
    {
        return value switch
        {
            UiBackendKind.ManagedFallback => AutomationUiBackend.ManagedFallback,
            UiBackendKind.RmlUi => AutomationUiBackend.RmlUi,
            UiBackendKind.Ultralight => AutomationUiBackend.Ultralight,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "未知 UI backend。"),
        };
    }

    private static UiBackendKind MapUiBackend(AutomationUiBackend value)
    {
        return value switch
        {
            AutomationUiBackend.ManagedFallback => UiBackendKind.ManagedFallback,
            AutomationUiBackend.RmlUi => UiBackendKind.RmlUi,
            AutomationUiBackend.Ultralight => UiBackendKind.Ultralight,
            _ => throw new InvalidOperationException($"未知 UI backend：{value}。"),
        };
    }

    private static bool SettingsEqual<T>(
        T left,
        T right,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        JsonElement leftJson = JsonSerializer.SerializeToElement(left, typeInfo);
        JsonElement rightJson = JsonSerializer.SerializeToElement(right, typeInfo);
        return JsonElement.DeepEquals(leftJson, rightJson);
    }

    private static bool ProjectSettingsEqual(ProjectSettingsDto left, ProjectSettingsDto right)
    {
        return left.FormatVersion == right.FormatVersion &&
            string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
            string.Equals(left.ContentRoot, right.ContentRoot, StringComparison.Ordinal) &&
            string.Equals(left.ScriptSourceDir, right.ScriptSourceDir, StringComparison.Ordinal) &&
            string.Equals(left.StartScene, right.StartScene, StringComparison.Ordinal) &&
            left.DefaultUiBackend == right.DefaultUiBackend &&
            left.ResourceRules.RequireStableMaterialNames == right.ResourceRules.RequireStableMaterialNames &&
            left.ResourceRules.ContentFileGlobs.SequenceEqual(
                right.ResourceRules.ContentFileGlobs,
                StringComparer.Ordinal);
    }

    private sealed class SettingsUndoAction<TState>(
        string name,
        TState before,
        TState after,
        Action<TState> apply) : IAutomationUndoAction
    {
        public string Name => name;

        public void Undo()
        {
            apply(before);
        }

        public void Redo()
        {
            apply(after);
        }
    }
}
