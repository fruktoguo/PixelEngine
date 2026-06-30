namespace PixelEngine.Hosting;

/// <summary>
/// 已加载场景实例。
/// </summary>
public sealed class Scene
{
    /// <summary>
    /// 创建场景实例。
    /// </summary>
    public Scene(SceneDescriptor descriptor, string? resolvedSource = null, bool worldConstructionPending = false)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        Descriptor = descriptor;
        ResolvedSource = string.IsNullOrWhiteSpace(resolvedSource) ? null : resolvedSource;
        WorldConstructionPending = worldConstructionPending;
    }

    /// <summary>
    /// 场景描述。
    /// </summary>
    public SceneDescriptor Descriptor { get; }

    /// <summary>
    /// 场景名称。
    /// </summary>
    public string Name => Descriptor.Name;

    /// <summary>
    /// 解析后的存档目录路径或程序化生成器键。
    /// </summary>
    public string? ResolvedSource { get; }

    /// <summary>
    /// 当前场景是否仍等待 World/Simulation 后端真正构建起始世界。
    /// </summary>
    public bool WorldConstructionPending { get; }
}
