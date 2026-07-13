using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using PixelEngine.UI;
using static PixelEngine.Hosting.EngineProjectSettingsValidation;

namespace PixelEngine.Hosting;

/// <summary>
/// build-player 出包通道。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<BuildProfileChannel>))]
public enum BuildProfileChannel
{
    /// <summary>
    /// ReadyToRun 自包含发行通道。
    /// </summary>
    R2R,

    /// <summary>
    /// NativeAOT 发行通道。
    /// </summary>
    Aot,
}

/// <summary>
/// 玩家包发行通道。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlayerReleaseChannel>))]
public enum PlayerReleaseChannel
{
    /// <summary>
    /// 开发或本机探针发行通道。
    /// </summary>
    Development,

    /// <summary>
    /// 正式发行通道。
    /// </summary>
    Production,
}

/// <summary>
/// 工程资源规则设置。
/// </summary>
public sealed record ProjectResourceRulesDto
{
    /// <summary>
    /// 是否要求材质入盘使用稳定名称。
    /// </summary>
    public bool RequireStableMaterialNames { get; init; } = true;

    /// <summary>
    /// 工程内容文件的默认收集规则。
    /// </summary>
    public string[] ContentFileGlobs { get; init; } = ["materials.json", "reactions.json", "scenes/**/*.scene", "ui/**/*"];
}

/// <summary>
/// 旧版工程内编辑器偏好，仅用于向用户级 Preferences 兼容迁移。
/// </summary>
public sealed record EditorPreferencesDto
{
    /// <summary>
    /// 旧版布局保存选项；新代码不得把它作为运行时权威值。
    /// </summary>
    public bool SaveLayoutOnExit { get; init; } = true;

    /// <summary>
    /// 旧版外部脚本编辑器命令；新代码不得执行项目提供的该值。
    /// </summary>
    public string ExternalScriptEditor { get; init; } = string.Empty;
}

/// <summary>
/// Project Settings 的 Hosting 中性 DTO。
/// </summary>
public sealed record ProjectSettingsDto
{
    /// <summary>
    /// 当前 Project Settings schema 版本。
    /// </summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>
    /// Project Settings 文件版本。
    /// </summary>
    public int FormatVersion { get; init; } = CurrentFormatVersion;

    /// <summary>
    /// 工程显示名称。
    /// </summary>
    public string Name { get; init; } = "PixelEngine Project";

    /// <summary>
    /// 工程内 content 根目录相对路径。
    /// </summary>
    public string ContentRoot { get; init; } = "content";

    /// <summary>
    /// 工程内脚本源码目录相对路径。
    /// </summary>
    public string ScriptSourceDir { get; init; } = "scripts";

    /// <summary>
    /// 默认启动场景相对路径。
    /// </summary>
    public string StartScene { get; init; } = "scenes/main.scene";

    /// <summary>
    /// 工程资源规则设置。
    /// </summary>
    public ProjectResourceRulesDto ResourceRules { get; init; } = new();

    /// <summary>
    /// 旧版工程内编辑器偏好，仅保留反序列化兼容。
    /// </summary>
    public EditorPreferencesDto EditorPreferences { get; init; } = new();

    /// <summary>
    /// 默认游戏 UI 后端。
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<UiBackendKind>))]
    public UiBackendKind DefaultUiBackend { get; init; } = UiBackendKind.ManagedFallback;

    /// <summary>
    /// 创建 Project Settings 默认值。
    /// </summary>
    public static ProjectSettingsDto CreateDefault(string? name = null)
    {
        return new ProjectSettingsDto
        {
            Name = string.IsNullOrWhiteSpace(name) ? "PixelEngine Project" : name.Trim(),
        };
    }

    /// <summary>
    /// 校验 Project Settings 并返回可执行诊断。
    /// </summary>
    public bool TryNormalize(out string error)
    {
        if (FormatVersion != CurrentFormatVersion)
        {
            error = $"不支持的 Project Settings 版本：{FormatVersion}。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            error = "工程名不能为空。";
            return false;
        }

        if (!TryNormalizeRelativeDirectory(ContentRoot, nameof(ContentRoot), out _, out error) ||
            !TryNormalizeRelativeDirectory(ScriptSourceDir, nameof(ScriptSourceDir), out _, out error) ||
            !TryNormalizeRelativePath(StartScene, nameof(StartScene), allowEmpty: false, out _, out error))
        {
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// 返回规范化后的 Project Settings；非法时抛出可执行诊断。
    /// </summary>
    public ProjectSettingsDto Normalize()
    {
        if (!TryNormalize(out string error))
        {
            throw new InvalidOperationException(error);
        }

        ProjectResourceRulesDto resourceRules = ResourceRules ?? new ProjectResourceRulesDto();
        EditorPreferencesDto editorPreferences = EditorPreferences ?? new EditorPreferencesDto();
        return this with
        {
            Name = Name.Trim(),
            ContentRoot = NormalizeRelativeDirectory(ContentRoot, nameof(ContentRoot)),
            ScriptSourceDir = NormalizeRelativeDirectory(ScriptSourceDir, nameof(ScriptSourceDir)),
            StartScene = NormalizeRelativePath(StartScene, nameof(StartScene), allowEmpty: false),
            ResourceRules = resourceRules with
            {
                ContentFileGlobs = resourceRules.ContentFileGlobs ?? [],
            },
            EditorPreferences = editorPreferences,
        };
    }
}

/// <summary>
/// 玩家包 content/startup.json 的 Hosting 中性启动设置。
/// </summary>
public sealed record EngineProjectStartupSettings
{
    /// <summary>
    /// 启动场景、存档目录或程序化入口键。
    /// </summary>
    public string StartScene { get; init; } = "scenes/main.scene";

    /// <summary>
    /// 玩家窗口标题。
    /// </summary>
    public string WindowTitle { get; init; } = EngineOptions.DefaultWindowTitle;

    /// <summary>
    /// 玩家窗口默认宽度。
    /// </summary>
    public int WindowWidth { get; init; } = EngineOptions.DefaultWindowWidth;

    /// <summary>
    /// 玩家窗口默认高度。
    /// </summary>
    public int WindowHeight { get; init; } = EngineOptions.DefaultWindowHeight;

    /// <summary>
    /// 是否启用垂直同步。
    /// </summary>
    public bool VSync { get; init; } = true;

    /// <summary>
    /// 运行时游戏 UI 后端。
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<UiBackendKind>))]
    public UiBackendKind RuntimeUiBackend { get; init; } = UiBackendKind.ManagedFallback;

    /// <summary>
    /// 玩家包发行通道。
    /// </summary>
    public PlayerReleaseChannel ReleaseChannel { get; init; } = PlayerReleaseChannel.Development;

    /// <summary>
    /// 创建启动设置默认值。
    /// </summary>
    public static EngineProjectStartupSettings CreateDefault()
    {
        return new EngineProjectStartupSettings();
    }

    /// <summary>
    /// 从 Player Settings 投影玩家启动设置。
    /// </summary>
    public static EngineProjectStartupSettings FromPlayerSettings(PlayerSettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        PlayerSettingsDto normalized = settings.Normalize();
        return new EngineProjectStartupSettings
        {
            StartScene = normalized.StartupScene,
            WindowTitle = normalized.WindowTitle,
            WindowWidth = normalized.WindowWidth,
            WindowHeight = normalized.WindowHeight,
            VSync = normalized.VSync,
            RuntimeUiBackend = normalized.RuntimeUiBackend,
            ReleaseChannel = normalized.ReleaseChannel,
        };
    }

    /// <summary>
    /// 返回规范化后的启动设置；非法时抛出可执行诊断。
    /// </summary>
    public EngineProjectStartupSettings Normalize()
    {
        return string.IsNullOrWhiteSpace(WindowTitle)
            ? throw new InvalidOperationException("窗口标题不能为空。")
            : WindowWidth <= 0 || WindowHeight <= 0
            ? throw new InvalidOperationException("窗口尺寸必须大于 0。")
            : this with
            {
                StartScene = NormalizeRelativePath(StartScene, nameof(StartScene), allowEmpty: false),
                WindowTitle = WindowTitle.Trim(),
            };
    }
}

/// <summary>
/// 玩家输入默认值设置。
/// </summary>
public sealed record PlayerInputDefaultsDto
{
    /// <summary>
    /// 是否启用键盘鼠标输入。
    /// </summary>
    public bool EnableKeyboardMouse { get; init; } = true;

    /// <summary>
    /// 是否启用手柄输入。
    /// </summary>
    public bool EnableGamepad { get; init; } = true;
}

/// <summary>
/// Player Settings 的 Hosting 中性 DTO。
/// </summary>
public sealed record PlayerSettingsDto
{
    /// <summary>
    /// 当前 Player Settings schema 版本。
    /// </summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>
    /// Player Settings 文件版本。
    /// </summary>
    public int FormatVersion { get; init; } = CurrentFormatVersion;

    /// <summary>
    /// 玩家窗口标题。
    /// </summary>
    public string WindowTitle { get; init; } = "PixelEngine Demo";

    /// <summary>
    /// 玩家窗口默认宽度。
    /// </summary>
    public int WindowWidth { get; init; } = 1280;

    /// <summary>
    /// 玩家窗口默认高度。
    /// </summary>
    public int WindowHeight { get; init; } = 720;

    /// <summary>
    /// 是否启用垂直同步。
    /// </summary>
    public bool VSync { get; init; } = true;

    /// <summary>
    /// 玩家窗口图标相对路径。
    /// </summary>
    public string? IconPath { get; init; }

    /// <summary>
    /// 玩家包版本号。
    /// </summary>
    public string Version { get; init; } = "0.1.0";

    /// <summary>
    /// 玩家启动场景相对路径。
    /// </summary>
    public string StartupScene { get; init; } = "scenes/main.scene";

    /// <summary>
    /// 玩家输入默认值。
    /// </summary>
    public PlayerInputDefaultsDto InputDefaults { get; init; } = new();

    /// <summary>
    /// 运行时游戏 UI 后端。
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<UiBackendKind>))]
    public UiBackendKind RuntimeUiBackend { get; init; } = UiBackendKind.ManagedFallback;

    /// <summary>
    /// 玩家包发行通道。
    /// </summary>
    public PlayerReleaseChannel ReleaseChannel { get; init; } = PlayerReleaseChannel.Development;

    /// <summary>
    /// 创建 Player Settings 默认值。
    /// </summary>
    public static PlayerSettingsDto CreateDefault(string? title = null)
    {
        return new PlayerSettingsDto
        {
            WindowTitle = string.IsNullOrWhiteSpace(title) ? "PixelEngine Demo" : title.Trim(),
        };
    }

    /// <summary>
    /// 校验 Player Settings 并返回可执行诊断。
    /// </summary>
    public bool TryNormalize(out string error)
    {
        if (FormatVersion != CurrentFormatVersion)
        {
            error = $"不支持的 Player Settings 版本：{FormatVersion}。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(WindowTitle))
        {
            error = "窗口标题不能为空。";
            return false;
        }

        if (WindowWidth <= 0 || WindowHeight <= 0)
        {
            error = "窗口尺寸必须大于 0。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Version))
        {
            error = "版本号不能为空。";
            return false;
        }

        if (!TryNormalizeRelativePath(StartupScene, nameof(StartupScene), allowEmpty: false, out _, out error))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(IconPath) && !TryNormalizeRelativePath(IconPath, nameof(IconPath), allowEmpty: false, out _, out error))
        {
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// 返回规范化后的 Player Settings；非法时抛出可执行诊断。
    /// </summary>
    public PlayerSettingsDto Normalize()
    {
        return TryNormalize(out string error)
            ? this with
            {
                WindowTitle = WindowTitle.Trim(),
                Version = Version.Trim(),
                StartupScene = NormalizeRelativePath(StartupScene, nameof(StartupScene), allowEmpty: false),
                IconPath = string.IsNullOrWhiteSpace(IconPath) ? null : NormalizeRelativePath(IconPath, nameof(IconPath), allowEmpty: false),
                InputDefaults = InputDefaults ?? new PlayerInputDefaultsDto(),
            }
            : throw new InvalidOperationException(error);
    }
}

/// <summary>
/// build-player 场景入包设置。
/// </summary>
public sealed record BuildProfileSceneDto
{
    /// <summary>
    /// 场景显示名称。
    /// </summary>
    public string SceneName { get; set; } = string.Empty;

    /// <summary>
    /// 是否把该场景纳入玩家包。
    /// </summary>
    public bool Included { get; set; } = true;

    /// <summary>
    /// 是否作为启动场景。
    /// </summary>
    public bool IsStartup { get; set; }

    /// <summary>
    /// 场景来源类型。
    /// </summary>
    public SceneSourceKind SourceKind { get; set; } = SceneSourceKind.SceneFile;

    /// <summary>
    /// 场景来源相对路径。
    /// </summary>
    public string? Source { get; set; }
}

/// <summary>
/// Build Settings 的 Hosting 中性 DTO。
/// </summary>
public sealed record BuildProfileDto
{
    /// <summary>
    /// 当前 Build Profile schema 版本。
    /// </summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>
    /// Build Profile 文件版本。
    /// </summary>
    public int FormatVersion { get; init; } = CurrentFormatVersion;

    /// <summary>
    /// 目标 RID。
    /// </summary>
    public string Rid { get; set; } = "win-x64";

    /// <summary>
    /// build-player 出包通道。
    /// </summary>
    public BuildProfileChannel Channel { get; set; } = BuildProfileChannel.R2R;

    /// <summary>
    /// 构建配置。
    /// </summary>
    public string Configuration { get; set; } = "Release";

    /// <summary>
    /// 输出目录。
    /// </summary>
    public string OutputDirectory { get; set; } = "artifacts/player";

    /// <summary>
    /// 玩家可见产物名。
    /// </summary>
    public string ProductName { get; set; } = "PixelEngine Demo";

    /// <summary>
    /// 玩家包版本号。
    /// </summary>
    public string Version { get; set; } = "0.1.0";

    /// <summary>
    /// 信息版本号。
    /// </summary>
    public string InformationalVersion { get; set; } = string.Empty;

    /// <summary>
    /// 玩家图标相对路径或绝对工具路径。
    /// </summary>
    public string? IconPath { get; set; }

    /// <summary>
    /// 是否包含调试符号。
    /// </summary>
    public bool IncludeSymbols { get; set; }

    /// <summary>
    /// 是否完整打包 content 目录。
    /// </summary>
    public bool PackageWholeContent { get; set; } = true;

    /// <summary>
    /// 构建完成后是否启动玩家包。
    /// </summary>
    public bool RunAfterBuild { get; set; }

    /// <summary>
    /// 场景入包与启动场景配置。
    /// </summary>
    public List<BuildProfileSceneDto> Scenes { get; set; } = [];

    /// <summary>
    /// 创建 Build Profile 默认值。
    /// </summary>
    public static BuildProfileDto CreateDefault(string? startupScene = "scenes/main.scene")
    {
        BuildProfileDto profile = new();
        if (!string.IsNullOrWhiteSpace(startupScene))
        {
            string source = NormalizeRelativePath(startupScene, nameof(startupScene), allowEmpty: false);
            profile.Scenes.Add(new BuildProfileSceneDto
            {
                SceneName = Path.GetFileNameWithoutExtension(source) ?? source,
                Included = true,
                IsStartup = true,
                SourceKind = SceneSourceKind.SceneFile,
                Source = source,
            });
        }

        return profile;
    }

    /// <summary>
    /// 校验 Build Profile 并返回可执行诊断。
    /// </summary>
    public bool TryNormalize(out string error)
    {
        if (FormatVersion != CurrentFormatVersion)
        {
            error = $"不支持的 Build Profile 版本：{FormatVersion}。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Rid))
        {
            error = "RID 不能为空。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Configuration))
        {
            error = "Configuration 不能为空。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            error = "输出目录不能为空。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(ProductName))
        {
            error = "产物名不能为空。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Version))
        {
            error = "版本号不能为空。";
            return false;
        }

        if (OutputDirectory.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            error = "输出目录包含非法路径字符。";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(IconPath) && IconPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            error = "图标路径包含非法路径字符。";
            return false;
        }

        if (Scenes is null)
        {
            error = "场景列表不能为空。";
            return false;
        }

        int included = 0;
        int startup = 0;
        for (int i = 0; i < Scenes.Count; i++)
        {
            BuildProfileSceneDto scene = Scenes[i];
            if (scene is null)
            {
                error = "场景条目不能为空。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(scene.SceneName) && string.IsNullOrWhiteSpace(scene.Source))
            {
                error = "场景名称或来源不能为空。";
                return false;
            }

            string scenePath = string.IsNullOrWhiteSpace(scene.Source) ? scene.SceneName : scene.Source;
            if (!TryNormalizeRelativePath(scenePath, nameof(BuildProfileSceneDto.Source), allowEmpty: false, out _, out error))
            {
                return false;
            }

            if (scene.Included)
            {
                included++;
            }

            if (scene.IsStartup)
            {
                startup++;
                if (!scene.Included)
                {
                    error = "启动场景必须入包。";
                    return false;
                }
            }
        }

        if (included == 0)
        {
            error = "至少需要一个入包场景。";
            return false;
        }

        if (startup != 1)
        {
            error = "必须且只能选择一个启动场景。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// 返回规范化后的 Build Profile；非法时抛出可执行诊断。
    /// </summary>
    public BuildProfileDto Normalize()
    {
        return TryNormalize(out string error)
            ? this with
            {
                Rid = Rid.Trim(),
                Configuration = Configuration.Trim(),
                OutputDirectory = OutputDirectory.Trim(),
                ProductName = ProductName.Trim(),
                Version = Version.Trim(),
                InformationalVersion = InformationalVersion.Trim(),
                IconPath = string.IsNullOrWhiteSpace(IconPath) ? null : IconPath.Trim(),
                Scenes = [.. Scenes.Select(static scene =>
                {
                    string scenePath = string.IsNullOrWhiteSpace(scene.Source) ? scene.SceneName : scene.Source;
                    string normalizedPath = NormalizeRelativePath(scenePath, nameof(BuildProfileSceneDto.Source), allowEmpty: false);
                    string? normalizedSource = string.IsNullOrWhiteSpace(scene.Source) ? null : normalizedPath;
                    string sceneName = string.IsNullOrWhiteSpace(scene.SceneName)
                        ? Path.GetFileNameWithoutExtension(normalizedPath) ?? normalizedPath
                        : scene.SceneName.Trim();
                    return scene with
                    {
                        SceneName = sceneName,
                        Source = normalizedSource,
                    };
                })],
            }
            : throw new InvalidOperationException(error);
    }
}

/// <summary>
/// Hosting 中性 settings DTO 的 JSON 读写入口。
/// </summary>
public static class EngineProjectSettingsStore
{
    /// <summary>
    /// Project Settings 文件名。
    /// </summary>
    public const string ProjectSettingsFileName = "ProjectSettings.json";

    /// <summary>
    /// Player Settings 文件名。
    /// </summary>
    public const string PlayerSettingsFileName = "PlayerSettings.json";

    /// <summary>
    /// Build Settings 文件名。
    /// </summary>
    public const string BuildSettingsFileName = "BuildSettings.json";

    /// <summary>
    /// 玩家包启动设置文件名。
    /// </summary>
    public const string StartupSettingsFileName = "startup.json";

    /// <summary>
    /// 从工程根目录加载 Project Settings。
    /// </summary>
    public static ProjectSettingsDto LoadProjectSettings(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        return LoadSettings(
            Path.Combine(projectRoot, ProjectSettingsFileName),
            ProjectSettingsDto.CreateDefault(),
            EngineProjectSettingsJsonContext.Default.ProjectSettingsDto).Normalize();
    }

    /// <summary>
    /// 从工程根目录加载 Player Settings。
    /// </summary>
    public static PlayerSettingsDto LoadPlayerSettings(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        return LoadSettings(
            Path.Combine(projectRoot, PlayerSettingsFileName),
            PlayerSettingsDto.CreateDefault(),
            EngineProjectSettingsJsonContext.Default.PlayerSettingsDto).Normalize();
    }

    /// <summary>
    /// 从工程根目录加载 Build Profile。
    /// </summary>
    public static BuildProfileDto LoadBuildProfile(string projectRoot, BuildProfileDto? fallback = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        return LoadBuildProfileFromFile(Path.Combine(projectRoot, BuildSettingsFileName), fallback);
    }

    /// <summary>
    /// 从指定文件加载 Build Profile。
    /// </summary>
    public static BuildProfileDto LoadBuildProfileFromFile(string filePath, BuildProfileDto? fallback = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return LoadSettings(
            filePath,
            fallback ?? BuildProfileDto.CreateDefault(),
            EngineProjectSettingsJsonContext.Default.BuildProfileDto).Normalize();
    }

    /// <summary>
    /// 保存 Project Settings 到工程根目录。
    /// </summary>
    public static void SaveProjectSettings(string projectRoot, ProjectSettingsDto settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        SaveSettings(Path.Combine(projectRoot, ProjectSettingsFileName), settings.Normalize(), EngineProjectSettingsJsonContext.Default.ProjectSettingsDto);
    }

    /// <summary>
    /// 保存 Player Settings 到工程根目录。
    /// </summary>
    public static void SavePlayerSettings(string projectRoot, PlayerSettingsDto settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        SaveSettings(Path.Combine(projectRoot, PlayerSettingsFileName), settings.Normalize(), EngineProjectSettingsJsonContext.Default.PlayerSettingsDto);
    }

    /// <summary>
    /// 保存 Build Profile 到工程根目录。
    /// </summary>
    public static void SaveBuildProfile(string projectRoot, BuildProfileDto settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        SaveBuildProfileToFile(Path.Combine(projectRoot, BuildSettingsFileName), settings);
    }

    /// <summary>
    /// 从 content 根目录加载玩家包启动设置。
    /// </summary>
    public static EngineProjectStartupSettings LoadStartupSettings(string contentRoot, EngineProjectStartupSettings? fallback = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        EngineProjectStartupSettings defaults = (fallback ?? EngineProjectStartupSettings.CreateDefault()).Normalize();
        string filePath = Path.Combine(contentRoot, StartupSettingsFileName);
        if (!File.Exists(filePath))
        {
            return defaults;
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(filePath));
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("startup.json 根节点必须是 JSON 对象。");
        }

        string startScene = ReadOptionalString(root, "startScene", defaults.StartScene);
        string windowTitle = ReadOptionalString(root, "windowTitle", defaults.WindowTitle);
        int windowWidth = ReadPositiveInt(root, "windowWidth", defaults.WindowWidth);
        int windowHeight = ReadPositiveInt(root, "windowHeight", defaults.WindowHeight);
        bool vSync = ReadOptionalBool(root, "vSync", defaults.VSync);
        UiBackendKind runtimeUiBackend = ReadOptionalEnum(root, "runtimeUiBackend", defaults.RuntimeUiBackend);
        PlayerReleaseChannel releaseChannel = ReadOptionalEnum(root, "releaseChannel", defaults.ReleaseChannel);

        return new EngineProjectStartupSettings
        {
            StartScene = startScene,
            WindowTitle = windowTitle,
            WindowWidth = windowWidth,
            WindowHeight = windowHeight,
            VSync = vSync,
            RuntimeUiBackend = runtimeUiBackend,
            ReleaseChannel = releaseChannel,
        }.Normalize();
    }

    /// <summary>
    /// 保存玩家包启动设置到 content 根目录。
    /// </summary>
    public static void SaveStartupSettings(string contentRoot, EngineProjectStartupSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentNullException.ThrowIfNull(settings);
        SaveSettings(Path.Combine(contentRoot, StartupSettingsFileName), settings.Normalize(), EngineProjectSettingsJsonContext.Default.EngineProjectStartupSettings);
    }

    /// <summary>
    /// 保存 Build Profile 到指定文件。
    /// </summary>
    public static void SaveBuildProfileToFile(string filePath, BuildProfileDto settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        SaveSettings(filePath, settings.Normalize(), EngineProjectSettingsJsonContext.Default.BuildProfileDto);
    }

    private static T LoadSettings<T>(string filePath, T fallback, JsonTypeInfo<T> typeInfo)
        where T : class
    {
        if (!File.Exists(filePath))
        {
            return fallback;
        }

        string json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize(json, typeInfo) ?? fallback;
    }

    private static string ReadOptionalString(JsonElement root, string propertyName, string fallback)
    {
        return root.TryGetProperty(propertyName, out JsonElement element) &&
            element.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(element.GetString())
                ? element.GetString()!.Trim()
                : fallback;
    }

    private static int ReadPositiveInt(JsonElement root, string propertyName, int fallback)
    {
        return root.TryGetProperty(propertyName, out JsonElement element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out int value) &&
            value > 0
                ? value
                : fallback;
    }

    private static bool ReadOptionalBool(JsonElement root, string propertyName, bool fallback)
    {
        return root.TryGetProperty(propertyName, out JsonElement element) &&
            (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
                ? element.GetBoolean()
                : fallback;
    }

    private static TEnum ReadOptionalEnum<TEnum>(JsonElement root, string propertyName, TEnum fallback)
        where TEnum : struct
    {
        return root.TryGetProperty(propertyName, out JsonElement element) &&
            element.ValueKind == JsonValueKind.String &&
            Enum.TryParse(element.GetString(), ignoreCase: true, out TEnum value)
                ? value
                : fallback;
    }

    private static void SaveSettings<T>(string filePath, T settings, JsonTypeInfo<T> typeInfo)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(settings, typeInfo);
        AtomicTextFile.WriteAllText(filePath, json);
    }
}

/// <summary>
/// 工程/玩家设置路径规范化与校验辅助；禁止绝对路径与 <c>..</c> 越界。
/// </summary>
internal static class EngineProjectSettingsValidation
{
    internal static string NormalizeRelativeDirectory(string value, string fieldName)
    {
        string normalized = NormalizeRelativePath(value, fieldName, allowEmpty: false);
        return normalized.TrimEnd('/');
    }

    internal static string NormalizeRelativePath(string value, string fieldName, bool allowEmpty)
    {
        return TryNormalizeRelativePath(value, fieldName, allowEmpty, out string normalized, out string error)
            ? normalized
            : throw new InvalidOperationException(error);
    }

    internal static bool TryNormalizeRelativeDirectory(string value, string fieldName, out string normalized, out string error)
    {
        if (!TryNormalizeRelativePath(value, fieldName, allowEmpty: false, out normalized, out error))
        {
            return false;
        }

        normalized = normalized.TrimEnd('/');
        if (normalized.Length == 0)
        {
            error = $"{fieldName} 不能解析为空目录。";
            return false;
        }

        return true;
    }

    internal static bool TryNormalizeRelativePath(string value, string fieldName, bool allowEmpty, out string normalized, out string error)
    {
        normalized = string.Empty;
        string candidate = value?.Trim().Replace('\\', '/') ?? string.Empty;
        if (candidate.Length == 0)
        {
            if (allowEmpty)
            {
                error = string.Empty;
                return true;
            }

            error = $"{fieldName} 不能为空。";
            return false;
        }

        if (Path.IsPathRooted(candidate) || candidate.StartsWith('/'))
        {
            error = $"{fieldName} 必须是工程内相对路径：{candidate}";
            return false;
        }

        if (candidate.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            error = $"{fieldName} 包含非法路径字符。";
            return false;
        }

        string[] parts = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        List<string> cleaned = new(parts.Length);
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            if (part.Length == 0 || part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                error = $"{fieldName} 不能越过工程或 content 根目录：{candidate}";
                return false;
            }

            cleaned.Add(part);
        }

        normalized = string.Join('/', cleaned);
        if (!allowEmpty && normalized.Length == 0)
        {
            error = $"{fieldName} 不能解析为空路径。";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true)]
[JsonSerializable(typeof(ProjectSettingsDto))]
[JsonSerializable(typeof(ProjectResourceRulesDto))]
[JsonSerializable(typeof(EditorPreferencesDto))]
[JsonSerializable(typeof(EngineProjectStartupSettings))]
[JsonSerializable(typeof(PlayerSettingsDto))]
[JsonSerializable(typeof(PlayerInputDefaultsDto))]
[JsonSerializable(typeof(BuildProfileDto))]
[JsonSerializable(typeof(BuildProfileSceneDto))]
internal sealed partial class EngineProjectSettingsJsonContext : JsonSerializerContext
{
}
