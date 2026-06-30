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
    /// 向实体添加指定组件类型；结构性变更由 Scene 统一管理。
    /// </summary>
    public T AddComponent<T>()
        where T : class, IComponent, new()
    {
        return Scene.AddComponent<T>(this);
    }

    /// <summary>
    /// 尝试读取指定组件类型。
    /// </summary>
    public bool TryGetComponent<T>(out T component)
        where T : class, IComponent
    {
        return Scene.TryGetComponent(this, out component);
    }

    /// <summary>
    /// 移除指定组件类型。
    /// </summary>
    public void RemoveComponent<T>()
        where T : class, IComponent
    {
        Scene.RemoveComponent<T>(this);
    }

    /// <summary>
    /// 请求销毁实体；实际移除由 Scene 在安全窗口统一执行。
    /// </summary>
    public void Destroy()
    {
        Scene.Destroy(this);
    }
}
