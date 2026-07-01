namespace PixelEngine.Hosting;

/// <summary>
/// 默认场景服务，负责注册描述并维护当前场景实例。
/// </summary>
public sealed class SceneService : ISceneService
{
    private readonly Dictionary<string, SceneDescriptor> _scenes = new(StringComparer.Ordinal);

    /// <summary>
    /// 创建默认场景服务。
    /// </summary>
    /// <param name="contentRoot">项目内容根目录。</param>
    public SceneService(string contentRoot = "content")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ContentRoot = contentRoot;
    }

    /// <summary>
    /// 项目内容根目录，用于解析相对存档路径。
    /// </summary>
    public string ContentRoot { get; }

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

        Current = CreateScene(descriptor);
        return Current;
    }

    /// <summary>
    /// 卸载当前场景。
    /// </summary>
    public void UnloadCurrent()
    {
        Current = null;
    }

    private Scene CreateScene(SceneDescriptor descriptor)
    {
        return descriptor.SourceKind switch
        {
            SceneSourceKind.Empty => new Scene(descriptor),
            SceneSourceKind.SaveDirectory => new Scene(
                descriptor,
                ResolveContentPath(descriptor.Source!),
                worldConstructionPending: true),
            SceneSourceKind.SceneFile => new Scene(
                descriptor,
                ResolveContentPath(descriptor.Source!),
                worldConstructionPending: true),
            SceneSourceKind.Procedural => new Scene(
                descriptor,
                descriptor.Source,
                worldConstructionPending: true),
            _ => throw new ArgumentOutOfRangeException(nameof(descriptor), descriptor.SourceKind, "未知场景来源类型。"),
        };
    }

    private string ResolveContentPath(string source)
    {
        string path = Path.IsPathRooted(source)
            ? source
            : Path.Combine(ContentRoot, source);
        return Path.GetFullPath(path);
    }
}
