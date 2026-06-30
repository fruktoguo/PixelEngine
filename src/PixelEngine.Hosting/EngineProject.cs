namespace PixelEngine.Hosting;

/// <summary>
/// EngineBuilder 可加载的项目模型。
/// </summary>
public sealed class EngineProject
{
    private readonly SceneDescriptor[] _scenes;

    /// <summary>
    /// 创建项目模型。
    /// </summary>
    public EngineProject(string contentRoot, string? startScene, ReadOnlySpan<SceneDescriptor> scenes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ContentRoot = contentRoot;
        StartScene = string.IsNullOrWhiteSpace(startScene) ? null : startScene;
        _scenes = scenes.ToArray();
    }

    /// <summary>
    /// 内容根目录。
    /// </summary>
    public string ContentRoot { get; }

    /// <summary>
    /// 起始场景名称。
    /// </summary>
    public string? StartScene { get; }

    /// <summary>
    /// 项目声明的场景列表。
    /// </summary>
    public ReadOnlySpan<SceneDescriptor> Scenes => _scenes;
}
