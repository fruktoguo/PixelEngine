namespace PixelEngine.Editor.Shell;

internal sealed class EditorPrefabLink
{
    public string? AssetPath { get; set; }

    public string? SourceStableId { get; set; }

    public List<EditorPrefabOverride> Overrides { get; } = [];

    public EditorPrefabLink Clone()
    {
        EditorPrefabLink clone = new()
        {
            AssetPath = AssetPath,
            SourceStableId = SourceStableId,
        };
        for (int i = 0; i < Overrides.Count; i++)
        {
            clone.Overrides.Add(Overrides[i].Clone());
        }

        return clone;
    }
}

internal sealed class EditorPrefabOverride
{
    public string? SourceStableId { get; set; }

    public string? PropertyPath { get; set; }

    public string? Value { get; set; }

    public EditorPrefabOverride Clone()
    {
        return new EditorPrefabOverride
        {
            SourceStableId = SourceStableId,
            PropertyPath = PropertyPath,
            Value = Value,
        };
    }
}
