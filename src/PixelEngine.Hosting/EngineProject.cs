namespace PixelEngine.Hosting;

/// <summary>
/// EngineBuilder 可加载的 Hosting 中性项目模型。
/// </summary>
public sealed class EngineProject
{
    private readonly SceneDescriptor[] _scenes;

    /// <summary>
    /// 创建项目模型。
    /// </summary>
    public EngineProject(string contentRoot, string? startScene, ReadOnlySpan<SceneDescriptor> scenes)
        : this(
            projectRoot: null,
            contentRoot: contentRoot,
            scriptSourceDirectory: null,
            startScene: startScene,
            scenes: scenes,
            projectSettings: null,
            playerSettings: null,
            buildProfile: null,
            startupSettings: null)
    {
    }

    private EngineProject(
        string? projectRoot,
        string contentRoot,
        string? scriptSourceDirectory,
        string? startScene,
        ReadOnlySpan<SceneDescriptor> scenes,
        ProjectSettingsDto? projectSettings,
        PlayerSettingsDto? playerSettings,
        BuildProfileDto? buildProfile,
        EngineProjectStartupSettings? startupSettings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ProjectRoot = string.IsNullOrWhiteSpace(projectRoot) ? null : Path.GetFullPath(projectRoot);
        ContentRoot = contentRoot;
        ScriptSourceDirectory = string.IsNullOrWhiteSpace(scriptSourceDirectory) ? null : scriptSourceDirectory;
        StartScene = string.IsNullOrWhiteSpace(startScene) ? null : startScene.Trim();
        _scenes = scenes.ToArray();
        ProjectSettings = projectSettings;
        PlayerSettings = playerSettings;
        BuildProfile = buildProfile;
        StartupSettings = startupSettings;
    }

    /// <summary>
    /// 工程根目录；玩家包仅有 content 根时为空。
    /// </summary>
    public string? ProjectRoot { get; }

    /// <summary>
    /// 内容根目录。
    /// </summary>
    public string ContentRoot { get; }

    /// <summary>
    /// 脚本源码目录；玩家包或无脚本工程时为空。
    /// </summary>
    public string? ScriptSourceDirectory { get; }

    /// <summary>
    /// 起始场景名称。
    /// </summary>
    public string? StartScene { get; }

    /// <summary>
    /// 已加载的 Project Settings；非工程根加载时为空。
    /// </summary>
    public ProjectSettingsDto? ProjectSettings { get; }

    /// <summary>
    /// 已加载的 Player Settings；非工程根加载时为空。
    /// </summary>
    public PlayerSettingsDto? PlayerSettings { get; }

    /// <summary>
    /// 已加载的 Build Settings；非工程根加载时为空。
    /// </summary>
    public BuildProfileDto? BuildProfile { get; }

    /// <summary>
    /// 已加载的玩家启动设置；无 startup.json 时为回退设置。
    /// </summary>
    public EngineProjectStartupSettings? StartupSettings { get; }

    /// <summary>
    /// 项目声明与扫描得到的场景列表，起始场景稳定排在第一项。
    /// </summary>
    public ReadOnlySpan<SceneDescriptor> Scenes => _scenes;

    /// <summary>
    /// 从工程根目录加载 settings、content、startup 与场景描述。
    /// </summary>
    public static EngineProject Load(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        string root = Path.GetFullPath(projectRoot);
        ProjectSettingsDto projectSettings = EngineProjectSettingsStore.LoadProjectSettings(root);
        PlayerSettingsDto playerSettings = EngineProjectSettingsStore.LoadPlayerSettings(root);
        BuildProfileDto buildProfile = EngineProjectSettingsStore.LoadBuildProfile(
            root,
            BuildProfileDto.CreateDefault(projectSettings.StartScene));
        string contentRoot = Path.GetFullPath(Path.Combine(root, projectSettings.ContentRoot));
        EngineProjectStartupSettings startupSettings = EngineProjectSettingsStore.LoadStartupSettings(
            contentRoot,
            EngineProjectStartupSettings.FromPlayerSettings(playerSettings));
        List<SceneDescriptor> declaredScenes = BuildProfileScenes(contentRoot, buildProfile);
        return FromProjectSettingsCore(
            root,
            projectSettings,
            declaredScenes,
            projectSettings.StartScene,
            playerSettings,
            buildProfile,
            startupSettings);
    }

    /// <summary>
    /// 从 Project Settings 与壳侧声明场景构造中性工程模型。
    /// </summary>
    public static EngineProject FromProjectSettings(
        string projectRoot,
        ProjectSettingsDto settings,
        ReadOnlySpan<SceneDescriptor> declaredScenes,
        string? startSceneOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(settings);
        return FromProjectSettingsCore(
            Path.GetFullPath(projectRoot),
            settings.Normalize(),
            declaredScenes.ToArray(),
            string.IsNullOrWhiteSpace(startSceneOverride) ? settings.Normalize().StartScene : startSceneOverride,
            playerSettings: null,
            buildProfile: null,
            startupSettings: null);
    }

    /// <summary>
    /// 从玩家包 content 根目录构造中性工程模型，自动扫描 .scene 并按需回退到程序化来源。
    /// </summary>
    public static EngineProject FromContentRoot(string contentRoot, string startScene, string? proceduralFallbackKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(startScene);
        string root = Path.GetFullPath(contentRoot);
        SceneDescriptor start = ResolveSceneDescriptor(root, startScene, proceduralFallbackKey, declaredKind: null);
        SceneDescriptor[] scenes = MergeScenes(start, ScanSceneFiles(root));
        return new EngineProject(
            projectRoot: null,
            contentRoot: root,
            scriptSourceDirectory: null,
            startScene: start.Name,
            scenes: scenes,
            projectSettings: null,
            playerSettings: null,
            buildProfile: null,
            startupSettings: EngineProjectSettingsStore.LoadStartupSettings(
                root,
                EngineProjectStartupSettings.CreateDefault() with { StartScene = startScene }));
    }

    /// <summary>
    /// 解析一个场景来源为中性场景描述，供 Demo、Editor 与工具共享同一套规则。
    /// </summary>
    public static SceneDescriptor ResolveSceneDescriptor(string contentRoot, string scene, string? proceduralFallbackKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(scene);
        return ResolveSceneDescriptor(Path.GetFullPath(contentRoot), scene, proceduralFallbackKey, declaredKind: null);
    }

    private static EngineProject FromProjectSettingsCore(
        string projectRoot,
        ProjectSettingsDto settings,
        IReadOnlyCollection<SceneDescriptor> declaredScenes,
        string? startSceneOverride,
        PlayerSettingsDto? playerSettings,
        BuildProfileDto? buildProfile,
        EngineProjectStartupSettings? startupSettings)
    {
        ProjectSettingsDto normalized = settings.Normalize();
        string contentRoot = Path.GetFullPath(Path.Combine(projectRoot, normalized.ContentRoot));
        string scriptSourceDirectory = Path.GetFullPath(Path.Combine(projectRoot, normalized.ScriptSourceDir));
        string startSource = string.IsNullOrWhiteSpace(startSceneOverride) ? normalized.StartScene : startSceneOverride.Trim();
        SceneDescriptor start = ResolveSceneDescriptor(contentRoot, startSource, proceduralFallbackKey: null, declaredKind: SceneSourceKind.SceneFile);
        SceneDescriptor[] scenes = MergeScenes(start, declaredScenes.Concat(ScanSceneFiles(contentRoot)));
        return new EngineProject(
            projectRoot,
            contentRoot,
            scriptSourceDirectory,
            start.Name,
            scenes,
            normalized,
            playerSettings,
            buildProfile,
            startupSettings);
    }

    private static List<SceneDescriptor> BuildProfileScenes(string contentRoot, BuildProfileDto buildProfile)
    {
        List<SceneDescriptor> scenes = new(buildProfile.Scenes.Count);
        for (int i = 0; i < buildProfile.Scenes.Count; i++)
        {
            BuildProfileSceneDto scene = buildProfile.Scenes[i];
            if (!scene.Included)
            {
                continue;
            }

            string source = string.IsNullOrWhiteSpace(scene.Source) ? scene.SceneName : scene.Source;
            scenes.Add(ResolveSceneDescriptor(contentRoot, source, proceduralFallbackKey: null, scene.SourceKind));
        }

        return scenes;
    }

    private static SceneDescriptor[] ScanSceneFiles(string contentRoot)
    {
        string scenesRoot = Path.Combine(contentRoot, "scenes");
        return Directory.Exists(scenesRoot)
            ?
            [
                .. Directory
                    .EnumerateFiles(scenesRoot, "*.scene", SearchOption.AllDirectories)
                    .Order(StringComparer.Ordinal)
                    .Select(path =>
                    {
                        string fullPath = Path.GetFullPath(path);
                        string name = ReadSceneStableName(fullPath);
                        return new SceneDescriptor(name, SceneSourceKind.SceneFile, fullPath);
                    }),
            ]
            : [];
    }

    private static SceneDescriptor ResolveSceneDescriptor(
        string contentRoot,
        string scene,
        string? proceduralFallbackKey,
        SceneSourceKind? declaredKind)
    {
        string source = scene.Trim().Replace('\\', Path.DirectorySeparatorChar);
        string fullSource = Path.IsPathRooted(source)
            ? Path.GetFullPath(source)
            : Path.GetFullPath(Path.Combine(contentRoot, source));
        string sceneName = Path.GetFileNameWithoutExtension(fullSource) ?? source;

        if (declaredKind == SceneSourceKind.Procedural)
        {
            return new SceneDescriptor(sceneName, SceneSourceKind.Procedural, source);
        }

        if (declaredKind == SceneSourceKind.SaveDirectory)
        {
            return new SceneDescriptor(sceneName, SceneSourceKind.SaveDirectory, fullSource);
        }

        if (declaredKind == SceneSourceKind.SceneFile)
        {
            string name = File.Exists(fullSource) ? ReadSceneStableName(fullSource) : sceneName;
            return new SceneDescriptor(name, SceneSourceKind.SceneFile, fullSource);
        }

        return Directory.Exists(fullSource)
            ? new SceneDescriptor(sceneName, SceneSourceKind.SaveDirectory, fullSource)
            : File.Exists(fullSource)
            ? new SceneDescriptor(ReadSceneStableName(fullSource), SceneSourceKind.SceneFile, fullSource)
            : !string.IsNullOrWhiteSpace(proceduralFallbackKey)
            ? new SceneDescriptor(sceneName, SceneSourceKind.Procedural, proceduralFallbackKey.Trim())
            : new SceneDescriptor(sceneName);
    }

    private static string ReadSceneStableName(string scenePath)
    {
        EngineSceneDocument document = EngineSceneDocumentLoader.LoadDocument(scenePath);
        return string.IsNullOrWhiteSpace(document.Name)
            ? Path.GetFileNameWithoutExtension(scenePath) ?? scenePath
            : document.Name.Trim();
    }

    private static SceneDescriptor[] MergeScenes(SceneDescriptor start, IEnumerable<SceneDescriptor> scenes)
    {
        List<SceneDescriptor> merged = [start];
        foreach (SceneDescriptor scene in scenes)
        {
            if (ContainsSceneName(merged, scene.Name))
            {
                continue;
            }

            merged.Add(scene);
        }

        return [.. merged];
    }

    private static bool ContainsSceneName(List<SceneDescriptor> scenes, string name)
    {
        for (int i = 0; i < scenes.Count; i++)
        {
            if (string.Equals(scenes[i].Name, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
