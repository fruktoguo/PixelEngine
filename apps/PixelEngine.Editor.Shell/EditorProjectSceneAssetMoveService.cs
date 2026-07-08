using PixelEngine.Editor.Shell.Build;
using PixelEngine.Editor.Shell.Settings;
using PixelEngine.Hosting;

namespace PixelEngine.Editor.Shell;

internal sealed class EditorProjectSceneAssetMoveService(EditorProject project, EditorAssetManifestStore manifest)
{
    private readonly EditorProject _project = project ?? throw new ArgumentNullException(nameof(project));
    private readonly EditorAssetManifestStore _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));

    public EditorSceneAssetMoveResult MoveSceneAsset(string currentLogicalPath, string newLogicalPath, EditorSceneModel? activeScene = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentLogicalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(newLogicalPath);
        if (EditorAssetManifestStore.Classify(currentLogicalPath) != EditorAssetType.Scene ||
            EditorAssetManifestStore.Classify(newLogicalPath) != EditorAssetType.Scene)
        {
            throw new InvalidOperationException("Scene 资产移动同步服务只接受 .scene / .world 资产。");
        }

        EditorAssetMoveResult move = _manifest.MoveAsset(currentLogicalPath, newLogicalPath, activeScene);
        SceneSettingsSyncCounts counts = SyncSceneReferences(currentLogicalPath, newLogicalPath);
        return new EditorSceneAssetMoveResult(move, counts);
    }

    private SceneSettingsSyncCounts SyncSceneReferences(string currentLogicalPath, string newLogicalPath)
    {
        int projectUpdates = _project.ReplaceScenePath(currentLogicalPath, newLogicalPath) ? 1 : 0;
        int projectSettingsUpdates = SyncProjectSettings(currentLogicalPath, newLogicalPath);
        int playerSettingsUpdates = SyncPlayerSettings(currentLogicalPath, newLogicalPath);
        int startupSettingsUpdates = SyncStartupSettings(currentLogicalPath, newLogicalPath);
        int buildSettingsUpdates = SyncBuildSettings(currentLogicalPath, newLogicalPath);
        return new SceneSettingsSyncCounts(
            projectUpdates,
            projectSettingsUpdates,
            playerSettingsUpdates,
            buildSettingsUpdates,
            startupSettingsUpdates);
    }

    private int SyncProjectSettings(string currentLogicalPath, string newLogicalPath)
    {
        ProjectSettingsStore store = new(_project);
        ProjectSettingsDto settings = store.Load();
        if (!PathEquals(settings.StartScene, currentLogicalPath))
        {
            return 0;
        }

        store.Save(settings with { StartScene = newLogicalPath });
        return 1;
    }

    private int SyncPlayerSettings(string currentLogicalPath, string newLogicalPath)
    {
        PlayerSettingsStore store = new(_project);
        PlayerSettingsDto settings = store.Load();
        if (!PathEquals(settings.StartupScene, currentLogicalPath))
        {
            return 0;
        }

        store.Save(settings with { StartupScene = newLogicalPath });
        return 1;
    }

    private int SyncStartupSettings(string currentLogicalPath, string newLogicalPath)
    {
        string startupPath = Path.Combine(_project.ContentRootPath, EngineProjectSettingsStore.StartupSettingsFileName);
        if (!File.Exists(startupPath))
        {
            return 0;
        }

        EngineProjectStartupSettings settings = EngineProjectSettingsStore.LoadStartupSettings(_project.ContentRootPath);
        if (!PathEquals(settings.StartScene, currentLogicalPath))
        {
            return 0;
        }

        EngineProjectSettingsStore.SaveStartupSettings(_project.ContentRootPath, settings with { StartScene = newLogicalPath });
        return 1;
    }

    private int SyncBuildSettings(string currentLogicalPath, string newLogicalPath)
    {
        BuildSettingsStore store = new(_project);
        BuildProfileDto settings = store.Load();
        int updates = 0;
        for (int i = 0; i < settings.Scenes.Count; i++)
        {
            BuildProfileSceneDto scene = settings.Scenes[i];
            string source = scene.Source ?? scene.SceneName;
            if (!PathEquals(source, currentLogicalPath))
            {
                continue;
            }

            scene.Source = newLogicalPath;
            if (string.Equals(scene.SceneName, Path.GetFileNameWithoutExtension(currentLogicalPath), StringComparison.OrdinalIgnoreCase))
            {
                scene.SceneName = Path.GetFileNameWithoutExtension(newLogicalPath) ?? newLogicalPath;
            }

            updates++;
        }

        if (updates == 0)
        {
            return 0;
        }

        CollapseDuplicateBuildScenes(settings);
        settings.RefreshScenes(_project);
        store.Save(settings);
        return updates;
    }

    private static void CollapseDuplicateBuildScenes(BuildProfileDto settings)
    {
        Dictionary<string, BuildProfileSceneDto> bySource = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < settings.Scenes.Count; i++)
        {
            BuildProfileSceneDto scene = settings.Scenes[i];
            string source = scene.Source ?? scene.SceneName;
            if (!bySource.TryGetValue(source, out BuildProfileSceneDto? existing))
            {
                bySource[source] = scene;
                continue;
            }

            existing.Included |= scene.Included;
            existing.IsStartup |= scene.IsStartup;
            if (string.IsNullOrWhiteSpace(existing.SceneName))
            {
                existing.SceneName = scene.SceneName;
            }
        }

        settings.Scenes = [.. bySource.Values];
    }

    private static bool PathEquals(string? left, string right)
    {
        return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string? value)
    {
        return (value ?? string.Empty).Replace('\\', '/').Trim();
    }
}

internal sealed record EditorSceneAssetMoveResult(EditorAssetMoveResult AssetMove, SceneSettingsSyncCounts SettingsUpdates);

internal sealed record SceneSettingsSyncCounts(
    int ProjectFile,
    int ProjectSettings,
    int PlayerSettings,
    int BuildSettings,
    int StartupSettings)
{
    public string FormatDiagnostic()
    {
        return $"project={ProjectFile.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"projectSettings={ProjectSettings.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"playerSettings={PlayerSettings.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"buildSettings={BuildSettings.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"startup={StartupSettings.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }
}
