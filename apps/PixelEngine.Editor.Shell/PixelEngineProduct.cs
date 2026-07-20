namespace PixelEngine.Editor.Shell;

internal static class PixelEngineProduct
{
    public const string Name = "PixelEngine";
    public const string AboutPopupTitle = "About PixelEngine";

    public static string FormatWindowTitle(string? projectName, string? sceneName, bool dirty)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return Name;
        }

        string scene = string.IsNullOrWhiteSpace(sceneName) ? "No Scene" : sceneName;
        return $"{Name} - {projectName} - {scene}{(dirty ? "*" : string.Empty)}";
    }
}
