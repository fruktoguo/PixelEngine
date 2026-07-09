namespace PixelEngine.Editor.Shell;

/// <summary>
/// 编辑器侧 Behaviour 组件的序列化模型，字段以字符串键值对存储。
/// </summary>
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
