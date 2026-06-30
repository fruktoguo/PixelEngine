namespace PixelEngine.Hosting;

/// <summary>
/// 已加载场景实例。
/// </summary>
public sealed class Scene
{
    /// <summary>
    /// 创建场景实例。
    /// </summary>
    public Scene(SceneDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        Descriptor = descriptor;
    }

    /// <summary>
    /// 场景描述。
    /// </summary>
    public SceneDescriptor Descriptor { get; }

    /// <summary>
    /// 场景名称。
    /// </summary>
    public string Name => Descriptor.Name;
}
