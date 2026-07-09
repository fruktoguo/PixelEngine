namespace PixelEngine.Editor.Shell;

/// <summary>
/// GameObject 与 Prefab 资产的链接及 override 列表。
/// </summary>
internal sealed class EditorPrefabLink
{
    public string? AssetId { get; set; }

    public string? AssetPath { get; set; }

    public string? SourceStableId { get; set; }

    public List<EditorPrefabOverride> Overrides { get; } = [];

    public EditorPrefabLink Clone()
    {
        EditorPrefabLink clone = new()
        {
            AssetId = AssetId,
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

/// <summary>Prefab 实例相对源资产的单条属性 override。</summary>
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
