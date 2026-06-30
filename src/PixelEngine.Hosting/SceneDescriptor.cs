namespace PixelEngine.Hosting;

/// <summary>
/// 项目中的场景描述。
/// </summary>
public sealed class SceneDescriptor
{
    /// <summary>
    /// 创建场景描述。
    /// </summary>
    public SceneDescriptor(string name, SceneSourceKind sourceKind = SceneSourceKind.Empty, string? source = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        SourceKind = sourceKind;
        Source = string.IsNullOrWhiteSpace(source) ? null : source;
    }

    /// <summary>
    /// 场景稳定名称。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 场景初始世界来源。
    /// </summary>
    public SceneSourceKind SourceKind { get; }

    /// <summary>
    /// 来源路径或生成器键。
    /// </summary>
    public string? Source { get; }
}
