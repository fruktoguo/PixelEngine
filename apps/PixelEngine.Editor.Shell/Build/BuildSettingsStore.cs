using System.Text.Json;
using PixelEngine.Hosting;

namespace PixelEngine.Editor.Shell.Build;

/// <summary>
/// 项目级 Build Settings JSON 读写。
/// </summary>
internal sealed class BuildSettingsStore(EditorProject project)
{
    private readonly EditorProject _project = project ?? throw new ArgumentNullException(nameof(project));

    public string SettingsPath => Path.Combine(_project.ProjectRoot, EngineProjectSettingsStore.BuildSettingsFileName);

    public BuildProfileDto Load()
    {
        BuildProfileDto settings = EngineProjectSettingsStore.LoadBuildProfileFromFile(SettingsPath, BuildProfileEditorAdapter.CreateDefault(_project));
        settings.RefreshScenes(_project);
        return settings;
    }

    public BuildProfileDto LoadRecoverable(out string diagnostic)
    {
        try
        {
            BuildProfileDto settings = Load();
            diagnostic = string.Empty;
            return settings;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or NotSupportedException)
        {
            BuildProfileDto fallback = BuildProfileEditorAdapter.CreateDefault(_project);
            fallback.RefreshScenes(_project);
            diagnostic =
                $"读取 Build Settings 失败，已使用工程默认构建 profile：{exception.Message} " +
                $"请在 File > Build Settings... 中检查并重新保存以修复 {SettingsPath}。";
            return fallback;
        }
    }

    public void Save(BuildProfileDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EngineProjectSettingsStore.SaveBuildProfileToFile(SettingsPath, settings);
    }
}
