namespace PixelEngine.Scripting;

/// <summary>
/// 脚本层游戏实体；持有轻量 id，并通过 Scene 管理组件。
/// </summary>
public sealed class Entity
{
    internal Entity(int id, Scene scene)
    {
        Id = id;
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
    }

    /// <summary>
    /// 实体在当前脚本场景内的稳定 id。
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// 所属脚本场景。
    /// </summary>
    public Scene Scene { get; }

    /// <summary>
    /// 向实体添加指定组件类型；脚本可在相位 1 调用，结构性变更由 Scene 统一管理。
    /// </summary>
    /// <typeparam name="T">要添加的组件类型。</typeparam>
    /// <returns>新创建并挂载到该实体的组件实例。</returns>
    public T AddComponent<T>()
        where T : class, IComponent, new()
    {
        return Scene.AddComponent<T>(this);
    }

    /// <summary>
    /// 向实体添加运行时确定的组件类型；供场景文件加载与脚本热重载装配使用。
    /// </summary>
    /// <param name="componentType">要添加的组件类型，必须实现 IComponent 且提供无参构造。</param>
    /// <returns>新创建并挂载到该实体的组件实例。</returns>
    public IComponent AddComponent(Type componentType)
    {
        return Scene.AddComponent(this, componentType);
    }

    /// <summary>
    /// 尝试读取指定组件类型；脚本可在相位 1 调用。
    /// </summary>
    /// <typeparam name="T">要读取的组件类型。</typeparam>
    /// <param name="component">找到时返回组件实例；未找到时为默认值。</param>
    /// <returns>若实体上存在该组件则返回 true，否则返回 false。</returns>
    public bool TryGetComponent<T>(out T component)
        where T : class, IComponent
    {
        return Scene.TryGetComponent(this, out component);
    }

    /// <summary>
    /// 移除指定组件类型；脚本可在相位 1 调用。
    /// </summary>
    /// <typeparam name="T">要移除的组件类型。</typeparam>
    public void RemoveComponent<T>()
        where T : class, IComponent
    {
        Scene.RemoveComponent<T>(this);
    }

    /// <summary>
    /// 请求销毁实体；脚本可在相位 1 调用，实际移除由 Scene 在帧末安全窗口统一执行。
    /// </summary>
    public void Destroy()
    {
        Scene.Destroy(this);
    }
}
