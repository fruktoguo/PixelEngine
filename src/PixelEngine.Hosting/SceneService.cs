namespace PixelEngine.Hosting;

/// <summary>
/// 默认场景服务，负责注册描述并维护当前场景实例。
/// </summary>
public sealed class SceneService : ISceneService
{
    private readonly Dictionary<string, SceneDescriptor> _scenes = new(StringComparer.Ordinal);

    /// <summary>
    /// 当前已加载场景。
    /// </summary>
    public Scene? Current { get; private set; }

    /// <summary>
    /// 注册或覆盖指定场景描述。
    /// </summary>
    public void Register(SceneDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _scenes[descriptor.Name] = descriptor;
    }

    /// <summary>
    /// 尝试按名称读取已注册场景描述。
    /// </summary>
    public bool TryGet(string name, out SceneDescriptor descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _scenes.TryGetValue(name, out descriptor!);
    }

    /// <summary>
    /// 切换到指定场景。
    /// </summary>
    public Scene SwitchTo(string name)
    {
        if (!TryGet(name, out SceneDescriptor descriptor))
        {
            throw new InvalidOperationException($"场景 {name} 未注册。");
        }

        Current = new Scene(descriptor);
        return Current;
    }

    /// <summary>
    /// 卸载当前场景。
    /// </summary>
    public void UnloadCurrent()
    {
        Current = null;
    }
}
