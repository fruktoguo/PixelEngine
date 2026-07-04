namespace PixelEngine.Editor.Shell;

internal sealed class EditorPrefabLink
{
    public string? AssetPath { get; set; }

    public string? SourceStableId { get; set; }

    public EditorPrefabLink Clone()
    {
        return new EditorPrefabLink
        {
            AssetPath = AssetPath,
            SourceStableId = SourceStableId,
        };
    }
}
