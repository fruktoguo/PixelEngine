using System.Text.Json;
using PixelEngine.Editor.Shell.Build;
using PixelEngine.Hosting;
using PixelEngine.Rendering;
using PixelEngine.UI;

namespace PixelEngine.Editor.Shell.Settings;

/// <summary>
/// 项目级 Project Settings JSON 读写。
/// </summary>
internal sealed class ProjectSettingsStore(EditorProject project)
{
    private readonly EditorProject _project = project ?? throw new ArgumentNullException(nameof(project));

    public string SettingsPath => Path.Combine(_project.ProjectRoot, EngineProjectSettingsStore.ProjectSettingsFileName);

    public ProjectSettingsDto Load()
    {
        return File.Exists(SettingsPath)
            ? EngineProjectSettingsStore.LoadProjectSettings(_project.ProjectRoot)
            : CreateFallback();
    }

    public ProjectSettingsDto LoadRecoverable(out string diagnostic)
    {
        try
        {
            ProjectSettingsDto settings = Load();
            diagnostic = string.Empty;
            return settings;
        }
        catch (Exception exception) when (IsRecoverableSettingsException(exception))
        {
            diagnostic =
                $"读取 Project Settings 失败，已使用 project.pixelproj 中的有效值：{exception.Message} " +
                $"请在 File > Project Settings... 中检查并点击 Apply 修复 {SettingsPath}。";
            return CreateFallback();
        }
    }

    public void Save(ProjectSettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ProjectSettingsDto normalized = settings.Normalize();
        bool hadSettingsFile = File.Exists(SettingsPath);
        string? previousSettings = hadSettingsFile ? File.ReadAllText(SettingsPath) : null;
        EngineProjectSettingsStore.SaveProjectSettings(_project.ProjectRoot, normalized);
        try
        {
            _project.ApplyProjectSettings(normalized);
        }
        catch (Exception applyException)
        {
            try
            {
                if (hadSettingsFile)
                {
                    EditorAtomicTextFile.WriteAllText(SettingsPath, previousSettings!);
                }
                else
                {
                    File.Delete(SettingsPath);
                }
            }
            catch (Exception rollbackException)
            {
                throw new InvalidOperationException(
                    $"同步 project.pixelproj 失败，且无法回滚 {SettingsPath}。",
                    new AggregateException(applyException, rollbackException));
            }

            throw;
        }
    }

    private ProjectSettingsDto CreateFallback()
    {
        return ProjectSettingsDto.CreateDefault(_project.Name) with
        {
            ContentRoot = _project.ContentRoot,
            ScriptSourceDir = _project.ScriptSourceDir,
            StartScene = _project.ResolveSceneRelativePath(null),
        };
    }

    private static bool IsRecoverableSettingsException(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or NotSupportedException;
    }
}

/// <summary>
/// 项目级 Player Settings JSON 读写。
/// </summary>
internal sealed class PlayerSettingsStore(EditorProject project)
{
    private readonly EditorProject _project = project ?? throw new ArgumentNullException(nameof(project));

    public string SettingsPath => Path.Combine(_project.ProjectRoot, EngineProjectSettingsStore.PlayerSettingsFileName);

    public PlayerSettingsDto Load()
    {
        return File.Exists(SettingsPath)
            ? EngineProjectSettingsStore.LoadPlayerSettings(_project.ProjectRoot)
            : CreateFallback();
    }

    public PlayerSettingsDto LoadRecoverable(out string diagnostic)
    {
        try
        {
            PlayerSettingsDto settings = Load();
            diagnostic = string.Empty;
            return settings;
        }
        catch (Exception exception) when (IsRecoverableSettingsException(exception))
        {
            diagnostic =
                $"读取 Player Settings 失败，已使用工程默认玩家设置：{exception.Message} " +
                $"请在 File > Player Settings... 中检查并点击 Apply 修复 {SettingsPath}。";
            return CreateFallback();
        }
    }

    public void Save(PlayerSettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EngineProjectSettingsStore.SavePlayerSettings(_project.ProjectRoot, settings);
    }

    private PlayerSettingsDto CreateFallback()
    {
        return PlayerSettingsDto.CreateDefault(_project.Name) with
        {
            StartupScene = _project.ResolveSceneRelativePath(null),
        };
    }

    private static bool IsRecoverableSettingsException(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or NotSupportedException;
    }
}

/// <summary>
/// PlayerSettings 与运行时投影之间的适配器。
/// </summary>
internal static class PlayerSettingsEditorAdapter
{
    public static EngineBuilder ApplyRuntimeDefaults(this EngineBuilder builder, PlayerSettingsDto settings, bool applyStartupScene = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        PlayerSettingsDto normalized = Normalize(settings);
        _ = builder
            .WithWindow(normalized.WindowWidth, normalized.WindowHeight)
            .WithWindowTitle(normalized.WindowTitle)
            .WithWindowMode(normalized.WindowMode)
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
            PlayerWindowMode = normalized.WindowMode,
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
            normalized.WindowMode,
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
        return scenes.Length == 0 || scenes.Any(scene => string.Equals(scene, startupScene, StringComparison.OrdinalIgnoreCase))
            ? scenes
            : [.. scenes, startupScene];
    }
}

/// <summary>
/// PlayerSettingsRuntimeProjectionSnapshot 数据结构。
/// </summary>
internal sealed record PlayerSettingsRuntimeProjectionSnapshot(
    string WindowTitle,
    int WindowWidth,
    int WindowHeight,
    PlayerWindowMode WindowMode,
    bool VSync,
    string StartupScene,
    UiBackendKind RuntimeUiBackend,
    PlayerReleaseChannel ReleaseChannel);
