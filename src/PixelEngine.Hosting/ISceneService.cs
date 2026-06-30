namespace PixelEngine.Hosting;

/// <summary>
/// 场景加载、卸载与切换服务。
/// </summary>
public interface ISceneService
{
    /// <summary>
    /// 当前场景；尚未加载时为空。
    /// </summary>
    Scene? Current { get; }

    /// <summary>
    /// 注册项目可用场景。
    /// </summary>
    void Register(SceneDescriptor descriptor);

    /// <summary>
    /// 尝试读取已注册场景。
    /// </summary>
    bool TryGet(string name, out SceneDescriptor descriptor);

    /// <summary>
    /// 切换到指定场景并返回加载后的场景实例。
    /// </summary>
    Scene SwitchTo(string name);

    /// <summary>
    /// 卸载当前场景。
    /// </summary>
    void UnloadCurrent();
}
