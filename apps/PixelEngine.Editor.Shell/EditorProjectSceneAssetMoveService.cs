using PixelEngine.Editor.Shell.Build;
using PixelEngine.Editor.Shell.Settings;
using PixelEngine.Hosting;

namespace PixelEngine.Editor.Shell;

/// <summary>
/// 移动场景资产时同步 project 与 manifest 引用。
/// </summary>
internal sealed class EditorProjectSceneAssetMoveService(EditorProject project, EditorAssetManifestStore manifest)
{
    private readonly EditorProject _project = project ?? throw new ArgumentNullException(nameof(project));
    private readonly EditorAssetManifestStore _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));

    public EditorSceneAssetMoveResult MoveSceneAsset(string currentLogicalPath, string newLogicalPath, EditorSceneModel? activeScene = null)
    {
        EditorScenePathRewrite rewrite = NormalizeRewrite(new EditorScenePathRewrite(currentLogicalPath, newLogicalPath));

        EditorAssetMoveResult move = _manifest.MoveAsset(rewrite.CurrentLogicalPath, rewrite.NewLogicalPath, activeScene);
        SceneSettingsSyncCounts counts = SynchronizeMovedScenePaths([rewrite]);
        return new EditorSceneAssetMoveResult(move, counts);
    }

    /// <summary>
    /// 对已完成磁盘移动的一组 Scene 路径批量同步工程与启动配置引用。
    /// </summary>
    /// <remarks>
    /// 该方法只重写配置，不再移动资产或修改 manifest，因此可用于文件夹移动后的多 Scene 收口。
    /// 所有输入会在任何持久化前完成归一化和类型校验。
    /// </remarks>
    public SceneSettingsSyncCounts SynchronizeMovedScenePaths(IReadOnlyList<EditorScenePathRewrite> rewrites)
    {
        ArgumentNullException.ThrowIfNull(rewrites);
        EditorScenePathRewrite[] normalized = NormalizeRewrites(rewrites);
        SceneSettingsSyncCounts total = SceneSettingsSyncCounts.Empty;
        for (int i = 0; i < normalized.Length; i++)
        {
            EditorScenePathRewrite rewrite = normalized[i];
            if (PathEquals(rewrite.CurrentLogicalPath, rewrite.NewLogicalPath))
            {
                continue;
            }

            total = total.Add(SyncSceneReferences(rewrite.CurrentLogicalPath, rewrite.NewLogicalPath));
        }

        return total;
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

    private static EditorScenePathRewrite[] NormalizeRewrites(IReadOnlyList<EditorScenePathRewrite> rewrites)
    {
        List<EditorScenePathRewrite> normalized = new(rewrites.Count);
        Dictionary<string, string> destinationsBySource = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < rewrites.Count; i++)
        {
            EditorScenePathRewrite rewrite = NormalizeRewrite(rewrites[i]);
            if (destinationsBySource.TryGetValue(rewrite.CurrentLogicalPath, out string? existingDestination))
            {
                if (!PathEquals(existingDestination, rewrite.NewLogicalPath))
                {
                    throw new InvalidOperationException(
                        $"Scene 路径 {rewrite.CurrentLogicalPath} 不能在同一批次中重写到多个目标。");
                }

                continue;
            }

            destinationsBySource.Add(rewrite.CurrentLogicalPath, rewrite.NewLogicalPath);
            normalized.Add(rewrite);
        }

        return [.. normalized];
    }

    private static EditorScenePathRewrite NormalizeRewrite(EditorScenePathRewrite rewrite)
    {
        string current = NormalizeScenePath(rewrite.CurrentLogicalPath, nameof(rewrite.CurrentLogicalPath));
        string next = NormalizeScenePath(rewrite.NewLogicalPath, nameof(rewrite.NewLogicalPath));
        return EditorAssetManifestStore.Classify(current) == EditorAssetType.Scene &&
            EditorAssetManifestStore.Classify(next) == EditorAssetType.Scene
                ? new EditorScenePathRewrite(current, next)
                : throw new InvalidOperationException("Scene 资产移动同步服务只接受 .scene / .world 资产。");
    }

    private static string NormalizeScenePath(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string candidate = value.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(candidate) || candidate.StartsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Scene 路径必须是 content 根目录内的相对路径：{candidate}");
        }

        string[] segments = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        List<string> normalized = new(segments.Length);
        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i].Trim();
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                throw new InvalidOperationException($"Scene 路径不能越过 content 根目录：{candidate}");
            }

            normalized.Add(segment);
        }

        return normalized.Count > 0
            ? string.Join('/', normalized)
            : throw new InvalidOperationException("Scene 路径不能为空。");
    }

    private static string NormalizePath(string? value)
    {
        return (value ?? string.Empty).Replace('\\', '/').Trim();
    }
}

/// <summary>
/// EditorSceneAssetMoveResult 数据结构。
/// </summary>
internal sealed record EditorSceneAssetMoveResult(EditorAssetMoveResult AssetMove, SceneSettingsSyncCounts SettingsUpdates);

/// <summary>
/// 已完成磁盘移动的 Scene 旧路径与新路径映射。
/// </summary>
internal readonly record struct EditorScenePathRewrite(string CurrentLogicalPath, string NewLogicalPath);

/// <summary>
/// SceneSettingsSyncCounts。
/// </summary>
internal sealed record SceneSettingsSyncCounts(
    int ProjectFile,
    int ProjectSettings,
    int PlayerSettings,
    int BuildSettings,
    int StartupSettings)
{
    public static SceneSettingsSyncCounts Empty { get; } = new(0, 0, 0, 0, 0);

    public int Total => ProjectFile + ProjectSettings + PlayerSettings + BuildSettings + StartupSettings;

    public SceneSettingsSyncCounts Add(SceneSettingsSyncCounts other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new SceneSettingsSyncCounts(
            ProjectFile + other.ProjectFile,
            ProjectSettings + other.ProjectSettings,
            PlayerSettings + other.PlayerSettings,
            BuildSettings + other.BuildSettings,
            StartupSettings + other.StartupSettings);
    }

    public string FormatDiagnostic()
    {
        return $"project={ProjectFile.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"projectSettings={ProjectSettings.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"playerSettings={PlayerSettings.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"buildSettings={BuildSettings.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
            $"startup={StartupSettings.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }
}
