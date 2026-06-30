namespace PixelEngine.Scripting;

/// <summary>
/// 所有用户脚本组件的基类；生命周期回调由 Hosting 在相位 1 驱动。
/// </summary>
public abstract class Behaviour : IComponent
{
    /// <summary>
    /// 所属实体；由脚本运行时在组件挂载时注入。
    /// </summary>
    public Entity Entity { get; internal set; } = null!;

    /// <summary>
    /// 是否启用该脚本；禁用后跳过逐帧与固定步回调。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 访问引擎世界能力的统一上下文；由脚本运行时注入。
    /// </summary>
    protected IScriptContext Context { get; private set; } = null!;

    /// <summary>
    /// 实体激活后、首个 OnUpdate 前调用一次；运行在相位 1。
    /// </summary>
    protected virtual void OnStart()
    {
    }

    /// <summary>
    /// 每个渲染帧调用一次；运行在相位 1，sim 降频时仍调用。
    /// </summary>
    /// <param name="dt">本帧经过时间，单位秒。</param>
    protected virtual void OnUpdate(float dt)
    {
    }

    /// <summary>
    /// 仅在本帧执行 sim step 时调用；运行在相位 1。
    /// </summary>
    protected virtual void OnFixedSimTick()
    {
    }

    /// <summary>
    /// 组件或实体销毁、或热重载卸载前调用一次；运行在相位 1。
    /// </summary>
    protected virtual void OnDestroy()
    {
    }

    internal void Attach(Entity entity, IScriptContext context)
    {
        Entity = entity;
        Context = context;
    }
}
