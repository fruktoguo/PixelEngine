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

    public void Save(BuildProfileDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EngineProjectSettingsStore.SaveBuildProfileToFile(SettingsPath, settings);
    }
}
