using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 把 production UI method declarations 联结到真实 semantic capability IDs。
/// 旧 registration 上不存在实际 UI handler 的历史标签会在此移除；最终双向闭包仍由 scheduler 验证。
/// </summary>
internal static class EditorAutomationUiCommandMappings
{
    public static string[] Resolve(string method, IReadOnlyList<string> declared)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(declared);
        string[] additions = Additions(method);
        List<string> resolved = new(declared.Count + additions.Length);
        for (int i = 0; i < declared.Count; i++)
        {
            if (!IsHistoricalNonUiLabel(declared[i]) && !IsSupersededMapping(method, declared[i]))
            {
                resolved.Add(declared[i]);
            }
        }

        resolved.AddRange(additions);
        return
        [
            .. resolved.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
        ];
    }

    private static string[] Additions(string method)
    {
        return method switch
        {
            AutomationProtocolConstants.WorkspaceGetMethod =>
            [
                "menu.help.about",
            ],
            AutomationProtocolConstants.WorkspaceProjectCreateMethod =>
            [
                "project-picker.create.browse-location",
            ],
            AutomationProtocolConstants.WorkspaceProjectOpenMethod =>
            [
                "project-picker.add-from-disk",
                "project-picker.open.browse-location",
                "project-picker.recent.add-from-disk",
                "project-picker.recent.open",
            ],
            AutomationProtocolConstants.WorkspaceProjectPickerSetMethod =>
            [
                "menu.file.new-project",
                "menu.file.open-project",
            ],
            AutomationProtocolConstants.WindowPanelSetMethod =>
            [
                "menu.edit.preferences",
                "menu.file.build-settings",
                "menu.file.player-settings",
                "menu.file.project-settings",
                "menu.window.panel.open",
                "project-picker.settings",
                "shortcut.ctrl-shift-b",
                "shortcut.ctrl-comma",
            ],
            AutomationProtocolConstants.WindowLayoutResetMethod =>
            [
                "panel.preferences.reset-layout",
                "toolbar.layout.reset",
            ],
            AutomationProtocolConstants.SceneGetMethod => ["panel.scene"],
            AutomationProtocolConstants.SceneOpenMethod =>
            [
                "context.project.asset.open-scene",
                "panel.inspector.asset.primary-action",
            ],
            AutomationProtocolConstants.GameObjectCreateMethod =>
            [
                "context.hierarchy.create-child",
                "context.hierarchy.create-root",
                "menu.game-object.create-child",
                "menu.game-object.create-empty",
                "menu.game-object.create-with-component",
            ],
            AutomationProtocolConstants.GameObjectDeleteMethod =>
            [
                "context.hierarchy.delete",
                "menu.edit.delete",
                "menu.game-object.delete",
            ],
            AutomationProtocolConstants.GameObjectDuplicateMethod =>
            [
                "context.hierarchy.duplicate",
                "menu.edit.duplicate",
            ],
            AutomationProtocolConstants.GameObjectRenameMethod =>
            [
                "context.hierarchy.rename",
                "menu.game-object.rename",
                "panel.inspector.rename",
            ],
            AutomationProtocolConstants.GameObjectReparentMethod => ["panel.hierarchy.drag-drop-root"],
            AutomationProtocolConstants.InspectorTransformSetMethod => ["context.inspector.transform.reset"],
            AutomationProtocolConstants.InspectorComponentAddMethod =>
            [
                "menu.component.add",
                "menu.game-object.create-with-component",
            ],
            AutomationProtocolConstants.InspectorComponentRemoveMethod =>
            [
                "context.inspector.canvas-scaler.remove",
                "context.inspector.canvas.remove",
                "context.inspector.component.remove",
            ],
            AutomationProtocolConstants.InspectorComponentMoveMethod =>
            [
                "context.inspector.component.move-down",
                "context.inspector.component.move-up",
            ],
            AutomationProtocolConstants.InspectorComponentSetFieldMethod =>
            [
                "panel.inspector.asset-reference.clear",
                "panel.inspector.asset-reference.drop",
            ],
            AutomationProtocolConstants.InspectorCanvasSetMethod =>
            [
                "context.inspector.canvas-scaler.reset",
                "context.inspector.canvas.reset",
            ],
            AutomationProtocolConstants.PrefabCreateMethod => ["context.hierarchy.create-prefab"],
            AutomationProtocolConstants.PrefabInstantiateMethod =>
            [
                "context.project.asset.instantiate-prefab",
                "panel.inspector.asset.primary-action",
            ],
            AutomationProtocolConstants.SceneToolGetMethod => ["panel.brush"],
            AutomationProtocolConstants.SceneToolSetMethod =>
            [
                "panel.brush.active",
                "panel.brush.material",
                "panel.brush.radius",
                "panel.brush.shape",
                "panel.brush.strength",
                "panel.brush.temperature",
                "panel.brush.tool",
                "panel.scene.brush-overlay.close",
                "panel.scene.brush-overlay.dock-left",
                "panel.scene.brush-overlay.dock-right",
                "panel.scene.brush-overlay.drag",
                "panel.scene.brush-overlay.float",
                "panel.scene.toolbar.gizmo",
                "shortcut.b",
            ],
            AutomationProtocolConstants.MaterialEditorSetMethod =>
            [
                "panel.materials.reactions",
                "panel.materials.reactions.add",
                "panel.materials.tag-representatives",
                "panel.materials.tag-representatives.add",
            ],
            AutomationProtocolConstants.ProjectAssetScriptOpenMethod =>
            [
                "context.project.asset.open-script",
                "panel.inspector.asset.primary-action",
            ],
            AutomationProtocolConstants.ProjectAssetAudioPreviewMethod =>
            [
                "context.project.asset.preview-audio",
                "panel.inspector.asset.primary-action",
            ],
            AutomationProtocolConstants.ProjectAssetCreateMethod =>
            [
                "context.project.folder.create",
                "panel.project.create.commit",
            ],
            AutomationProtocolConstants.ProjectAssetImportMethod =>
            [
                "context.project.folder.import",
                "panel.project.import.browse",
                "panel.project.import.commit",
            ],
            AutomationProtocolConstants.ProjectAssetMoveMethod =>
            [
                "context.project.asset.move",
                "panel.project.move.cancel",
                "panel.project.move.commit",
            ],
            AutomationProtocolConstants.ProjectAssetDeleteMethod =>
            [
                "context.project.asset.delete",
                "panel.project.delete.cancel",
                "panel.project.delete.commit",
            ],
            AutomationProtocolConstants.ProjectFolderMoveMethod =>
            [
                "context.project.folder.move",
                "panel.project.folder.move.cancel",
                "panel.project.folder.move.commit",
            ],
            AutomationProtocolConstants.ProjectFolderDeleteMethod =>
            [
                "context.project.folder.delete",
                "panel.project.folder.delete.cancel",
                "panel.project.folder.delete.commit",
            ],
            AutomationProtocolConstants.ProjectUiManifestGetMethod => ["panel.ui-manifest.refresh"],
            AutomationProtocolConstants.ConsoleEntryCopyMethod => ["context.console.copy"],
            AutomationProtocolConstants.ConsoleEntryOpenSourceMethod => ["context.console.open-source"],
            AutomationProtocolConstants.PlayEnterMethod =>
            [
                "panel.play-mode.play-current",
                "panel.play-mode.play-temp",
            ],
            AutomationProtocolConstants.PlayPauseMethod => ["panel.simulation.pause"],
            AutomationProtocolConstants.PlayResumeMethod => ["panel.simulation.play"],
            AutomationProtocolConstants.PlayStepMethod => ["panel.simulation.step"],
            AutomationProtocolConstants.PlayStopMethod => ["panel.play-mode.exit"],
            AutomationProtocolConstants.RuntimeSimulationSetMethod =>
            [
                "panel.simulation.rate-30",
                "panel.simulation.rate-60",
            ],
            AutomationProtocolConstants.GamePresentationGetMethod => ["panel.game.custom-resolution.cancel"],
            AutomationProtocolConstants.GamePresentationSetMethod =>
            [
                "panel.game.custom-resolution.add",
                "panel.game.custom-resolution.commit",
                "panel.game.custom-resolution.delete",
                "panel.game.custom-resolution.edit",
                "panel.game.overflow.maximize",
                "panel.game.overflow.maximize-on-play",
                "panel.game.overflow.scale",
                "panel.game.presentation.preset",
                "panel.game.scale.fit",
                "panel.game.scale.pixel-100",
                "shortcut.shift-space",
            ],
            AutomationProtocolConstants.PreferencesGetMethod => ["panel.preferences.revert"],
            AutomationProtocolConstants.PreferencesSetMethod =>
            [
                "panel.preferences.appearance",
                "panel.preferences.external-tools",
                "panel.preferences.general",
                "panel.preferences.language",
            ],
            AutomationProtocolConstants.ProjectSettingsGetMethod => ["panel.project-settings.revert"],
            AutomationProtocolConstants.ProjectSettingsSetMethod => ["panel.project-settings.fields"],
            AutomationProtocolConstants.PlayerSettingsGetMethod => ["panel.player-settings.revert"],
            AutomationProtocolConstants.PlayerSettingsSetMethod => ["panel.player-settings.fields"],
            AutomationProtocolConstants.BuildSettingsSetMethod =>
            [
                "panel.build-settings.repair",
                "panel.build-settings.scenes",
                "panel.build-settings.settings",
            ],
            AutomationProtocolConstants.BuildPreflightMethod => ["panel.build-settings.overflow.preflight"],
            AutomationProtocolConstants.BuildStartMethod =>
            [
                "menu.file.build-and-run",
                "panel.build-settings.overflow.build",
                "panel.build-settings.overflow.build-and-run",
                "shortcut.ctrl-b",
            ],
            AutomationProtocolConstants.BuildCancelMethod => ["panel.build-settings.overflow.cancel"],
            AutomationProtocolConstants.BuildGetMethod => ["panel.build-settings.open-output"],
            _ => [],
        };
    }

    private static bool IsHistoricalNonUiLabel(string id)
    {
        return id is
            "automation.artifacts.delete" or
            "automation.artifacts.list" or
            "automation.artifacts.verify" or
            "menu.window.layout.export" or
            "menu.window.layout.import" or
            "panel.console.export" or
            "panel.game.capture" or
            "panel.inspector.asset.edit" or
            "panel.materials.remove" or
            "panel.profiler.export" or
            "panel.scene.capture";
    }

    private static bool IsSupersededMapping(string method, string id)
    {
        return (method, id) is
            (AutomationProtocolConstants.WorkspaceProjectCreateMethod, "menu.file.new-project") or
            (AutomationProtocolConstants.WorkspaceProjectOpenMethod, "menu.file.open-project") or
            (AutomationProtocolConstants.PreferencesGetMethod, "menu.edit.preferences") or
            (AutomationProtocolConstants.PreferencesGetMethod, "shortcut.ctrl-comma") or
            (AutomationProtocolConstants.ProjectSettingsGetMethod, "menu.file.project-settings") or
            (AutomationProtocolConstants.PlayerSettingsGetMethod, "menu.file.player-settings") or
            (AutomationProtocolConstants.BuildSettingsGetMethod, "menu.file.build-settings");
    }
}
