namespace PixelEngine.Scripting;

/// <summary>
/// 基于脚本 Scene 的默认运行时，实现相位 1 生命周期派发。
/// </summary>
public sealed class ScriptRuntime : IScriptRuntime
{
    private readonly HotReloadService? _hotReload;
    private IScriptContext? _context;
    private bool _shutdown;

    /// <summary>
    /// 创建默认脚本运行时。
    /// </summary>
    public ScriptRuntime()
    {
    }

    internal ScriptRuntime(HotReloadService hotReload)
    {
        _hotReload = hotReload ?? throw new ArgumentNullException(nameof(hotReload));
    }

    /// <summary>
    /// 使用脚本上下文初始化运行时；由 Hosting 在主循环启动前调用。
    /// </summary>
    /// <param name="context">脚本访问引擎能力的统一上下文。</param>
    public void Initialize(IScriptContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ThrowIfShutdown();
        _context = context;
    }

    /// <summary>
    /// 相位 1 开始时派发未启动 Behaviour 的 OnStart。
    /// </summary>
    public void BeginFrame()
    {
        IScriptContext context = RequireContext();
        _ = _hotReload?.ApplyPendingReload();
        context.Scene.DispatchStart(context);
    }

    /// <summary>
    /// 每个渲染帧在相位 1 派发 Behaviour OnUpdate 与系统 OnFrame。
    /// </summary>
    /// <param name="dt">本帧 delta time，单位秒。</param>
    public void Update(float dt)
    {
        IScriptContext context = RequireContext();
        if (context.Events is IScriptEventDispatcher dispatcher)
        {
            dispatcher.DrainEvents();
        }

        context.Scene.DispatchUpdate(context, dt);
        context.Scene.DispatchFrameSystems(context, dt);
    }

    /// <summary>
    /// sim tick 帧在相位 1 派发 Behaviour OnFixedSimTick 与系统 OnSimTick。
    /// </summary>
    public void FixedSimTick()
    {
        IScriptContext context = RequireContext();
        context.Scene.DispatchFixedSimTick(context);
        context.Scene.DispatchSimSystems(context);
    }

    /// <summary>
    /// 相位 1 结束时刷新延迟销毁。
    /// </summary>
    public void EndFrame()
    {
        IScriptContext context = RequireContext();
        context.Scene.FlushDestroyed(context);
    }

    /// <summary>
    /// 关闭脚本运行时；由 Hosting 生命周期关闭流程调用。
    /// </summary>
    public void Shutdown()
    {
        _hotReload?.Dispose();
        _shutdown = true;
        _context = null;
    }

    private IScriptContext RequireContext()
    {
        ThrowIfShutdown();
        return _context ?? throw new InvalidOperationException("脚本运行时尚未初始化。");
    }

    private void ThrowIfShutdown()
    {
        ObjectDisposedException.ThrowIf(_shutdown, this);
    }
}
