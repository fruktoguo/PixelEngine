namespace PixelEngine.Scripting;

/// <summary>
/// 脚本生命周期与系统回调的统一调用入口；捕获异常并隔离故障脚本/系统。
/// </summary>
internal sealed class ScriptInvoker(IScriptDiagnosticSink? diagnostics = null)
{
    [ThreadStatic]
    private static Behaviour? currentBehaviour;

    [ThreadStatic]
    private static ScriptInvoker? currentInvoker;

    private readonly IScriptDiagnosticSink? _diagnostics = diagnostics;
    private readonly List<ScriptExceptionRecord> _exceptions = [];
    private readonly HashSet<ISystem> _faultedSystems = [];

    /// <summary>
    /// 本帧累积的脚本异常记录（只读视图）。
    /// </summary>
    public IReadOnlyList<ScriptExceptionRecord> Exceptions => _exceptions;

    /// <summary>
    /// 获取当前线程正在执行的 Behaviour 与 Invoker；用于嵌套 API 上下文查询。
    /// </summary>
    internal static bool TryGetCurrentOwner(out Behaviour behaviour, out ScriptInvoker invoker)
    {
        behaviour = currentBehaviour!;
        invoker = currentInvoker!;
        return behaviour is not null && invoker is not null;
    }

    /// <summary>
    /// 调用 <see cref="Behaviour"/> 的 OnStart；故障或禁用脚本被跳过。
    /// </summary>
    public void InvokeStart(Behaviour behaviour, IScriptContext context)
    {
        if (ShouldSkip(behaviour))
        {
            return;
        }

        try
        {
            using InvocationScope scope = Enter(behaviour);
            behaviour.InvokeStart(context);
        }
        catch (Exception exception)
        {
            MarkFaulted(behaviour, "OnStart", exception);
        }
    }

    /// <summary>
    /// 调用 OnUpdate；运行在相位 1。
    /// </summary>
    public void InvokeUpdate(Behaviour behaviour, IScriptContext context, float dt)
    {
        if (ShouldSkip(behaviour))
        {
            return;
        }

        try
        {
            using InvocationScope scope = Enter(behaviour);
            behaviour.InvokeUpdate(context, dt);
        }
        catch (Exception exception)
        {
            MarkFaulted(behaviour, "OnUpdate", exception);
        }
    }

    /// <summary>
    /// 调用 OnFixedSimTick；仅在本帧执行 sim step 时触发。
    /// </summary>
    public void InvokeFixedSimTick(Behaviour behaviour, IScriptContext context)
    {
        if (ShouldSkip(behaviour))
        {
            return;
        }

        try
        {
            using InvocationScope scope = Enter(behaviour);
            behaviour.InvokeFixedSimTick(context);
        }
        catch (Exception exception)
        {
            MarkFaulted(behaviour, "OnFixedSimTick", exception);
        }
    }

    /// <summary>
    /// 调用 OnGui；用于脚本侧即时模式 UI。
    /// </summary>
    public void InvokeGui(Behaviour behaviour, IScriptContext context, IGuiContext gui)
    {
        if (ShouldSkip(behaviour))
        {
            return;
        }

        try
        {
            using InvocationScope scope = Enter(behaviour);
            behaviour.InvokeGui(context, gui);
        }
        catch (Exception exception)
        {
            MarkFaulted(behaviour, "OnGui", exception);
        }
    }

    /// <summary>
    /// 调用 OnDestroy；无论故障状态如何都会尝试执行，并在 finally 中释放事件订阅。
    /// </summary>
    public void InvokeDestroy(Behaviour behaviour, IScriptContext context)
    {
        try
        {
            using InvocationScope scope = Enter(behaviour);
            behaviour.InvokeDestroy(context);
        }
        catch (Exception exception)
        {
            MarkFaulted(behaviour, "OnDestroy", exception);
        }
        finally
        {
            behaviour.DisposeTrackedSubscriptions();
        }
    }

    /// <summary>
    /// 调用事件总线分发的处理器；异常时隔离对应 Behaviour。
    /// </summary>
    public void InvokeEvent<TEvent>(Behaviour behaviour, Action<TEvent> handler, TEvent item)
        where TEvent : unmanaged
    {
        if (ShouldSkip(behaviour))
        {
            return;
        }

        try
        {
            using InvocationScope scope = Enter(behaviour);
            handler(item);
        }
        catch (Exception exception)
        {
            MarkFaulted(behaviour, $"Event:{typeof(TEvent).FullName}", exception);
        }
    }

    /// <summary>
    /// 调用 <see cref="ISystem.OnFrame"/>；故障系统后续帧被跳过。
    /// </summary>
    public void InvokeFrameSystem(ISystem system, IScriptContext context, float dt)
    {
        if (_faultedSystems.Contains(system))
        {
            return;
        }

        try
        {
            system.OnFrame(context, dt);
        }
        catch (Exception exception)
        {
            MarkFaulted(system, "ISystem.OnFrame", exception, context);
        }
    }

    /// <summary>
    /// 调用 <see cref="ISystem.OnSimTick"/>；故障系统后续 sim tick 被跳过。
    /// </summary>
    public void InvokeSimSystem(ISystem system, IScriptContext context)
    {
        if (_faultedSystems.Contains(system))
        {
            return;
        }

        try
        {
            system.OnSimTick(context);
        }
        catch (Exception exception)
        {
            MarkFaulted(system, "ISystem.OnSimTick", exception, context);
        }
    }

    private static bool ShouldSkip(Behaviour behaviour)
    {
        return behaviour.Faulted || !behaviour.Enabled;
    }

    /// <summary>
    /// 压入当前 Behaviour 到线程静态栈，供嵌套调用与上下文 API 识别所有者。
    /// </summary>
    private InvocationScope Enter(Behaviour behaviour)
    {
        InvocationScope scope = new(currentBehaviour, currentInvoker);
        currentBehaviour = behaviour;
        currentInvoker = this;
        return scope;
    }

    private void MarkFaulted(Behaviour behaviour, string callback, Exception exception)
    {
        behaviour.MarkFaulted(exception);
        ScriptExceptionRecord record = new(
            behaviour.GetType().FullName ?? behaviour.GetType().Name,
            callback,
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            behaviour.TryGetFrameCount());
        _exceptions.Add(record);
        _diagnostics?.ReportScriptException(record);
    }

    private void MarkFaulted(ISystem system, string callback, Exception exception, IScriptContext context)
    {
        _ = _faultedSystems.Add(system);
        ScriptExceptionRecord record = new(
            system.GetType().FullName ?? system.GetType().Name,
            callback,
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            TryGetFrameCount(context));
        _exceptions.Add(record);
        _diagnostics?.ReportScriptException(record);
    }

    private static long TryGetFrameCount(IScriptContext context)
    {
        try
        {
            return context.Time.FrameCount;
        }
        catch (NotSupportedException)
        {
            return -1;
        }
    }

    /// <summary>
    /// 恢复进入作用域前的线程静态调用上下文。
    /// </summary>
    private readonly struct InvocationScope(Behaviour? previousBehaviour, ScriptInvoker? previousInvoker) : IDisposable
    {
        public void Dispose()
        {
            currentBehaviour = previousBehaviour;
            currentInvoker = previousInvoker;
        }
    }
}
