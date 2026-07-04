using System.Text.Json;

namespace PixelEngine.Editor.Shell.Build;

internal sealed class BuildSettingsStore(EditorProject project)
{
    private readonly EditorProject _project = project ?? throw new ArgumentNullException(nameof(project));

    public string SettingsPath => Path.Combine(_project.ProjectRoot, "BuildSettings.json");

    public BuildTargetSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return BuildTargetSettings.CreateDefault(_project);
        }

        string json = File.ReadAllText(SettingsPath);
        BuildTargetSettings settings = JsonSerializer.Deserialize(
                json,
                PixelEngineEditorShellBuildJsonContext.Default.BuildTargetSettings) ??
            BuildTargetSettings.CreateDefault(_project);
        settings.RefreshScenes(_project);
        return settings;
    }

    public void Save(BuildTargetSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(
            settings,
            PixelEngineEditorShellBuildJsonContext.Default.BuildTargetSettings);
        File.WriteAllText(SettingsPath, json);
    }
}
