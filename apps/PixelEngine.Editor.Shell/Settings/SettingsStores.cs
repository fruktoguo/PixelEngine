using PixelEngine.Editor.Shell.Build;
using PixelEngine.Hosting;
using PixelEngine.UI;

namespace PixelEngine.Editor.Shell.Settings;

internal sealed class ProjectSettingsStore(EditorProject project)
{
    private readonly EditorProject _project = project ?? throw new ArgumentNullException(nameof(project));

    public string SettingsPath => Path.Combine(_project.ProjectRoot, EngineProjectSettingsStore.ProjectSettingsFileName);

    public ProjectSettingsDto Load()
    {
        if (File.Exists(SettingsPath))
        {
            return EngineProjectSettingsStore.LoadProjectSettings(_project.ProjectRoot);
        }

        return ProjectSettingsDto.CreateDefault(_project.Name) with
        {
            ContentRoot = _project.ContentRoot,
            ScriptSourceDir = _project.ScriptSourceDir,
            StartScene = _project.ResolveSceneRelativePath(null),
        };
    }

    public void Save(ProjectSettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EngineProjectSettingsStore.SaveProjectSettings(_project.ProjectRoot, settings);
        _project.ApplyProjectSettings(settings);
    }
}

internal sealed class PlayerSettingsStore(EditorProject project)
{
    private readonly EditorProject _project = project ?? throw new ArgumentNullException(nameof(project));

    public string SettingsPath => Path.Combine(_project.ProjectRoot, EngineProjectSettingsStore.PlayerSettingsFileName);

    public PlayerSettingsDto Load()
    {
        if (File.Exists(SettingsPath))
        {
            return EngineProjectSettingsStore.LoadPlayerSettings(_project.ProjectRoot);
        }

        return PlayerSettingsDto.CreateDefault(_project.Name) with
        {
            StartupScene = _project.ResolveSceneRelativePath(null),
        };
    }

    public void Save(PlayerSettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EngineProjectSettingsStore.SavePlayerSettings(_project.ProjectRoot, settings);
    }
}

internal static class PlayerSettingsEditorAdapter
{
    public static EngineBuilder ApplyRuntimeDefaults(this EngineBuilder builder, PlayerSettingsDto settings, bool applyStartupScene = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        PlayerSettingsDto normalized = Normalize(settings);
        _ = builder
            .WithWindow(normalized.WindowWidth, normalized.WindowHeight)
            .WithWindowTitle(normalized.WindowTitle)
            .UseVSync(normalized.VSync)
            .UseGuiRuntime()
            .EnableGameUi()
            .UseUiBackend(normalized.RuntimeUiBackend);
        if (applyStartupScene)
        {
            _ = builder.WithStartScene(normalized.StartupScene);
        }

        return builder;
    }

    public static BuildRequest ApplyToBuildRequest(BuildRequest request, PlayerSettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(request);
        PlayerSettingsDto normalized = Normalize(settings);
        string[] includedScenes = EnsureIncluded(request.IncludedScenes, normalized.StartupScene);
        return request with
        {
            ProductName = normalized.WindowTitle,
            Version = normalized.Version,
            IconPath = normalized.IconPath,
            StartScene = normalized.StartupScene,
            IncludedScenes = includedScenes,
            PlayerWindowWidth = normalized.WindowWidth,
            PlayerWindowHeight = normalized.WindowHeight,
            PlayerVSync = normalized.VSync,
            RuntimeUiBackend = normalized.RuntimeUiBackend,
            ReleaseChannel = normalized.ReleaseChannel,
        };
    }

    public static PlayerSettingsRuntimeProjectionSnapshot CaptureRuntimeProjection(PlayerSettingsDto settings)
    {
        PlayerSettingsDto normalized = Normalize(settings);
        return new PlayerSettingsRuntimeProjectionSnapshot(
            normalized.WindowTitle,
            normalized.WindowWidth,
            normalized.WindowHeight,
            normalized.VSync,
            normalized.StartupScene,
            normalized.RuntimeUiBackend,
            normalized.ReleaseChannel);
    }

    private static PlayerSettingsDto Normalize(PlayerSettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Normalize();
    }

    private static string[] EnsureIncluded(string[] scenes, string startupScene)
    {
        if (scenes.Length == 0 || scenes.Any(scene => string.Equals(scene, startupScene, StringComparison.OrdinalIgnoreCase)))
        {
            return scenes;
        }

        return [.. scenes, startupScene];
    }
}

internal sealed record PlayerSettingsRuntimeProjectionSnapshot(
    string WindowTitle,
    int WindowWidth,
    int WindowHeight,
    bool VSync,
    string StartupScene,
    UiBackendKind RuntimeUiBackend,
    PlayerReleaseChannel ReleaseChannel);
