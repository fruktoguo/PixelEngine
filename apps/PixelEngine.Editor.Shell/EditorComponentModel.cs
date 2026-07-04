namespace PixelEngine.Editor.Shell;

internal sealed class EditorComponentModel
{
    public EditorComponentModel(string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        TypeName = typeName.Trim();
    }

    public string TypeName { get; set; }

    public SortedDictionary<string, string> SerializedFields { get; } = new(StringComparer.Ordinal);

    public EditorComponentModel Clone()
    {
        EditorComponentModel clone = new(TypeName);
        foreach (KeyValuePair<string, string> field in SerializedFields)
        {
            clone.SerializedFields.Add(field.Key, field.Value);
        }

        return clone;
    }
}
