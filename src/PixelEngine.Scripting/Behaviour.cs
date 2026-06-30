namespace PixelEngine.Scripting;

/// <summary>
/// 所有用户脚本组件的基类；生命周期回调由 Hosting 在相位 1 驱动。
/// </summary>
public abstract class Behaviour : IComponent
{
    private List<IDisposable>? _subscriptions;

    internal bool Started { get; private set; }

    /// <summary>
    /// 所属实体；由脚本运行时在组件挂载时注入。
    /// </summary>
    public Entity Entity { get; internal set; } = null!;

    /// <summary>
    /// 是否启用该脚本；禁用后跳过逐帧与固定步回调。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 获取该脚本是否因回调异常被运行时隔离。
    /// </summary>
    public bool Faulted { get; private set; }

    /// <summary>
    /// 最近一次导致脚本被隔离的异常。
    /// </summary>
    public Exception? LastException { get; private set; }

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

    /// <summary>
    /// 清除异常隔离状态，并重新启用该脚本；由热重载或编辑器修复流程在相位 1 调用。
    /// </summary>
    public void ResetFault()
    {
        Faulted = false;
        LastException = null;
        Enabled = true;
    }

    internal void Attach(Entity entity, IScriptContext context)
    {
        Entity = entity;
        Context = context;
    }

    internal void InvokeStart(IScriptContext context)
    {
        Context = context;
        OnStart();
        Started = true;
    }

    internal void InvokeUpdate(IScriptContext context, float dt)
    {
        Context = context;
        OnUpdate(dt);
    }

    internal void InvokeFixedSimTick(IScriptContext context)
    {
        Context = context;
        OnFixedSimTick();
    }

    internal void InvokeDestroy(IScriptContext context)
    {
        Context = context;
        OnDestroy();
    }

    internal void TrackSubscription(IDisposable subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        _subscriptions ??= [];
        _subscriptions.Add(subscription);
    }

    internal void DisposeTrackedSubscriptions()
    {
        if (_subscriptions is null)
        {
            return;
        }

        for (int i = 0; i < _subscriptions.Count; i++)
        {
            _subscriptions[i].Dispose();
        }

        _subscriptions.Clear();
    }

    internal long TryGetFrameCount()
    {
        try
        {
            return Context?.Time.FrameCount ?? -1;
        }
        catch (NotSupportedException)
        {
            return -1;
        }
        catch (ObjectDisposedException)
        {
            return -1;
        }
    }

    internal void MarkFaulted(Exception exception)
    {
        LastException = exception;
        Faulted = true;
        Enabled = false;
    }
}
