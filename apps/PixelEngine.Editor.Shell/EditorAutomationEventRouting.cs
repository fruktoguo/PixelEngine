using PixelEngine.Editor.Automation.Protocol;

namespace PixelEngine.Editor.Shell;

internal static class EditorAutomationEventRouting
{
    internal static string[] ForCapability(string method, string domain)
    {
        List<string> eventTypes = [AutomationProtocolConstants.StateChangedEventType];
        switch (domain)
        {
            case "workspace":
                eventTypes.Add(AutomationProtocolConstants.WorkspaceChangedEventType);
                break;
            case "window":
                eventTypes.Add(AutomationProtocolConstants.WindowChangedEventType);
                if (method.Contains("layout", StringComparison.Ordinal) ||
                    method.Contains("panel", StringComparison.Ordinal))
                {
                    eventTypes.Add(AutomationProtocolConstants.LayoutChangedEventType);
                }

                break;
            case "scene":
                eventTypes.Add(AutomationProtocolConstants.SceneChangedEventType);
                break;
            case "hierarchy":
                if (method.Contains("selection", StringComparison.Ordinal))
                {
                    eventTypes.Add(AutomationProtocolConstants.SelectionChangedEventType);
                }
                else
                {
                    eventTypes.Add(AutomationProtocolConstants.HierarchyChangedEventType);
                    eventTypes.Add(AutomationProtocolConstants.InspectorChangedEventType);
                    eventTypes.Add(AutomationProtocolConstants.SceneChangedEventType);
                }

                break;
            case "inspector":
                eventTypes.Add(AutomationProtocolConstants.InspectorChangedEventType);
                eventTypes.Add(AutomationProtocolConstants.SceneChangedEventType);
                break;
            case "tool":
                eventTypes.Add(AutomationProtocolConstants.ToolChangedEventType);
                if (method.Contains("brush", StringComparison.Ordinal))
                {
                    eventTypes.Add(AutomationProtocolConstants.SceneChangedEventType);
                }

                break;
            case "project":
                eventTypes.Add(AutomationProtocolConstants.AssetsChangedEventType);
                break;
            case "console":
                eventTypes.Add(AutomationProtocolConstants.ConsoleChangedEventType);
                break;
            case "play":
                eventTypes.Add(AutomationProtocolConstants.PlayChangedEventType);
                eventTypes.Add(AutomationProtocolConstants.RuntimeChangedEventType);
                break;
            case "runtime":
                eventTypes.Add(AutomationProtocolConstants.RuntimeChangedEventType);
                if (method.Contains("simulation", StringComparison.Ordinal) ||
                    method.Contains("physics", StringComparison.Ordinal) ||
                    method.Contains("particles", StringComparison.Ordinal) ||
                    method.Contains("lighting", StringComparison.Ordinal))
                {
                    eventTypes.Add(AutomationProtocolConstants.ProfilerChangedEventType);
                }

                break;
            case "game":
                eventTypes.Add(AutomationProtocolConstants.GameChangedEventType);
                if (method.Contains(".ui.", StringComparison.Ordinal))
                {
                    eventTypes.Add(AutomationProtocolConstants.RuntimeChangedEventType);
                }

                break;
            case "settings":
                eventTypes.Add(AutomationProtocolConstants.SettingsChangedEventType);
                if (method.Contains("build", StringComparison.Ordinal))
                {
                    eventTypes.Add(AutomationProtocolConstants.BuildChangedEventType);
                }

                break;
            case "profiler":
                eventTypes.Add(AutomationProtocolConstants.ProfilerChangedEventType);
                break;
            case "debug":
                eventTypes.Add(AutomationProtocolConstants.DebugChangedEventType);
                break;
            case "artifact":
                eventTypes.Add(AutomationProtocolConstants.ArtifactChangedEventType);
                break;
            case "build":
                eventTypes.Add(AutomationProtocolConstants.BuildChangedEventType);
                break;
            case "player":
                eventTypes.Add(AutomationProtocolConstants.PlayerChangedEventType);
                break;
            default:
                break;
        }

        return [.. eventTypes.Distinct(StringComparer.Ordinal)];
    }

    internal static string[] ForManualMutation(string method, IReadOnlyList<string> resourceIds)
    {
        HashSet<string> eventTypes = new(StringComparer.Ordinal);
        if (method.StartsWith("project.", StringComparison.Ordinal) ||
            resourceIds.Contains("editor:workspace", StringComparer.Ordinal))
        {
            _ = eventTypes.Add(AutomationProtocolConstants.WorkspaceChangedEventType);
        }

        for (int i = 0; i < resourceIds.Count; i++)
        {
            string resource = resourceIds[i];
            if (resource is "editor:window")
            {
                _ = eventTypes.Add(AutomationProtocolConstants.WindowChangedEventType);
            }

            if (resource is "editor:layout" or "editor:panels")
            {
                _ = eventTypes.Add(AutomationProtocolConstants.LayoutChangedEventType);
            }

            if (resource.Contains(":selection", StringComparison.Ordinal))
            {
                _ = eventTypes.Add(AutomationProtocolConstants.SelectionChangedEventType);
            }

            if (resource.StartsWith("scene:", StringComparison.Ordinal) ||
                resource.Contains(":game-object:", StringComparison.Ordinal))
            {
                _ = eventTypes.Add(AutomationProtocolConstants.SceneChangedEventType);
                _ = eventTypes.Add(AutomationProtocolConstants.HierarchyChangedEventType);
                _ = eventTypes.Add(AutomationProtocolConstants.InspectorChangedEventType);
            }

            if (resource.Contains("assets", StringComparison.Ordinal) ||
                resource.Contains(":asset:", StringComparison.Ordinal) ||
                resource.Contains(":folder:", StringComparison.Ordinal))
            {
                _ = eventTypes.Add(AutomationProtocolConstants.AssetsChangedEventType);
            }

            if (resource.StartsWith("editor:console", StringComparison.Ordinal))
            {
                _ = eventTypes.Add(AutomationProtocolConstants.ConsoleChangedEventType);
            }

            if (resource is "editor:play")
            {
                _ = eventTypes.Add(AutomationProtocolConstants.PlayChangedEventType);
                _ = eventTypes.Add(AutomationProtocolConstants.RuntimeChangedEventType);
            }

            if (resource is "editor:simulation" ||
                resource.StartsWith("editor:runtime:", StringComparison.Ordinal) ||
                (resource.StartsWith("play:", StringComparison.Ordinal) &&
                 resource.EndsWith(":runtime", StringComparison.Ordinal)))
            {
                _ = eventTypes.Add(AutomationProtocolConstants.RuntimeChangedEventType);
            }

            if (resource is "editor:profiler")
            {
                _ = eventTypes.Add(AutomationProtocolConstants.ProfilerChangedEventType);
            }

            if (resource.Contains("settings", StringComparison.Ordinal) ||
                resource is "editor:preferences")
            {
                _ = eventTypes.Add(AutomationProtocolConstants.SettingsChangedEventType);
            }

            if (resource.StartsWith("editor:build", StringComparison.Ordinal))
            {
                _ = eventTypes.Add(AutomationProtocolConstants.BuildChangedEventType);
            }

            if (resource.StartsWith("editor:player", StringComparison.Ordinal))
            {
                _ = eventTypes.Add(AutomationProtocolConstants.PlayerChangedEventType);
            }
        }

        return [.. eventTypes.Order(StringComparer.Ordinal)];
    }
}
