using System.Text.Json;
using PixelEngine.Editor.Automation.Protocol;
using PixelEngine.Editor.Automation.Server;

namespace PixelEngine.Editor.Shell;

internal sealed partial class EditorAutomationAuthoringApi
{
    private AutomationOperationResult GetWorldInspector(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        EnsureEmpty(payload, AutomationProtocolConstants.RuntimeWorldInspectorGetMethod);
        WorldInspectorPanel panel = RequireWorldInspector();
        return Result(
            MapWorldInspector(panel.CaptureState()),
            AutomationJsonContext.Default.AutomationWorldInspectorSnapshot,
            [WorldInspectorResource]);
    }

    private AutomationOperationResult SetWorldInspector(
        AutomationScheduledContext context,
        JsonElement? payload)
    {
        _ = context;
        AutomationWorldInspectorSetRequest request = Deserialize(
            payload,
            AutomationJsonContext.Default.AutomationWorldInspectorSetRequest,
            AutomationProtocolConstants.RuntimeWorldInspectorSetMethod);
        ValidateSchema(request.SchemaVersion, AutomationProtocolConstants.RuntimeWorldInspectorSetMethod);
        EditorProjectSession session = RequireSession();
        WorldInspectorPanel panel = RequireWorldInspector();
        WorldInspectorPanelState before = panel.CaptureState();
        session.ApplyAutomationWorldInspectorState(
            request.FollowSelection,
            request.LockedWorldX,
            request.LockedWorldY);
        WorldInspectorPanelState after = panel.CaptureState();
        AutomationWorldInspectorSnapshot response = MapWorldInspector(after);
        string[] resources = [WorldInspectorResource];
        return panel.StateEquals(before)
            ? NoChange(
                response,
                AutomationJsonContext.Default.AutomationWorldInspectorSnapshot,
                resources)
            : CompleteSettingsWrite(
                "Set World Inspector",
                before,
                after,
                panel.RestoreState,
                response,
                AutomationJsonContext.Default.AutomationWorldInspectorSnapshot,
                resources);
    }

    private WorldInspectorPanel RequireWorldInspector()
    {
        EditorProjectSession session = RequireSession();
        return session.TryGetAutomationWorldInspector(out WorldInspectorPanel panel)
            ? panel
            : throw StateUnavailable("当前 Engine 未提供 World Inspector simulation inspect API。");
    }

    private static AutomationWorldInspectorSnapshot MapWorldInspector(
        WorldInspectorPanelState state)
    {
        return new AutomationWorldInspectorSnapshot
        {
            FollowSelection = state.FollowSelection,
            LockedWorldX = state.WorldX,
            LockedWorldY = state.WorldY,
            Inspection = state.LastInspection is { } inspection
                ? MapRuntimeCell(inspection)
                : null,
        };
    }
}
