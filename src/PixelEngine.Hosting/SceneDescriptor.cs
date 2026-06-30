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
        if (sourceKind == SceneSourceKind.Empty && !string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("空场景不能配置来源。", nameof(source));
        }

        if (sourceKind != SceneSourceKind.Empty && string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("非空场景必须配置存档路径或生成器键。", nameof(source));
        }

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
