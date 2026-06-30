namespace PixelEngine.Scripting;

/// <summary>
/// 基于脚本 Scene 的默认运行时，实现相位 1 生命周期派发。
/// </summary>
public sealed class ScriptRuntime : IScriptRuntime
{
    private IScriptContext? _context;
    private bool _shutdown;

    /// <summary>
    /// 使用脚本上下文初始化运行时。
    /// </summary>
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
        context.Scene.DispatchStart(context);
    }

    /// <summary>
    /// 每个渲染帧派发 Behaviour OnUpdate 与系统 OnFrame。
    /// </summary>
    /// <param name="dt">本帧 delta time，单位秒。</param>
    public void Update(float dt)
    {
        IScriptContext context = RequireContext();
        context.Scene.DispatchUpdate(context, dt);
        context.Scene.DispatchFrameSystems(context, dt);
    }

    /// <summary>
    /// sim tick 帧派发 Behaviour OnFixedSimTick 与系统 OnSimTick。
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
    /// 关闭脚本运行时。
    /// </summary>
    public void Shutdown()
    {
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
