namespace PixelEngine.Editor.Shell;

/// <summary>
/// Editor 用户级状态文件的统一路径集合。
/// </summary>
internal sealed record EditorUserDataPaths
{
    public const string UserDataDirectoryEnvironmentVariable = "PIXELENGINE_EDITOR_USER_DATA_DIR";

    private EditorUserDataPaths(string rootDirectory, bool isEphemeral)
    {
        RootDirectory = rootDirectory;
        IsEphemeral = isEphemeral;
        PreferencesPath = Path.Combine(rootDirectory, "editor-preferences.json");
        RecentProjectsPath = Path.Combine(rootDirectory, "recent-projects.json");
        WorkspacePath = Path.Combine(rootDirectory, "editor-workspace.json");
        LayoutPath = Path.Combine(rootDirectory, "editor-shell-imgui.ini");
        LocalizationDirectory = Path.Combine(rootDirectory, "Localization");
        RecoveryRoot = Path.Combine(rootDirectory, "recovery");
    }

    public string RootDirectory { get; }

    public string PreferencesPath { get; }

    public string RecentProjectsPath { get; }

    public string WorkspacePath { get; }

    public string LayoutPath { get; }

    public string LocalizationDirectory { get; }

    public string RecoveryRoot { get; }

    public bool IsEphemeral { get; }

    /// <summary>
    /// 按 CLI、环境变量、临时隔离、平台默认值的优先级解析用户数据路径。
    /// </summary>
    public static EditorUserDataPaths Resolve(EditorShellOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Resolve(
            options,
            Environment.GetEnvironmentVariable(UserDataDirectoryEnvironmentVariable),
            ephemeralDirectory: null);
    }

    internal static EditorUserDataPaths Resolve(
        EditorShellOptions options,
        string? environmentUserDataDirectory,
        string? ephemeralDirectory)
    {
        ArgumentNullException.ThrowIfNull(options);
        string? configuredDirectory = string.IsNullOrWhiteSpace(options.UserDataDirectory)
            ? environmentUserDataDirectory
            : options.UserDataDirectory;
        string rootDirectory = !string.IsNullOrWhiteSpace(configuredDirectory)
            ? Path.GetFullPath(configuredDirectory.Trim())
            : options.EphemeralUserState
                ? Path.GetFullPath(string.IsNullOrWhiteSpace(ephemeralDirectory)
                ? Path.Combine(
                    Path.GetTempPath(),
                    "PixelEngine",
                    "EditorUserData",
                    $"{Environment.ProcessId}-{Guid.NewGuid():N}")
                    : ephemeralDirectory.Trim())
                : Path.GetFullPath(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PixelEngine"));

        return new EditorUserDataPaths(rootDirectory, options.EphemeralUserState);
    }
}
