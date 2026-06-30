namespace PixelEngine.Scripting;

internal sealed class ScriptInvoker(IScriptDiagnosticSink? diagnostics = null)
{
    [ThreadStatic]
    private static Behaviour? currentBehaviour;

    [ThreadStatic]
    private static ScriptInvoker? currentInvoker;

    private readonly IScriptDiagnosticSink? _diagnostics = diagnostics;
    private readonly List<ScriptExceptionRecord> _exceptions = [];

    public IReadOnlyList<ScriptExceptionRecord> Exceptions => _exceptions;

    internal static bool TryGetCurrentOwner(out Behaviour behaviour, out ScriptInvoker invoker)
    {
        behaviour = currentBehaviour!;
        invoker = currentInvoker!;
        return behaviour is not null && invoker is not null;
    }

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

    private static bool ShouldSkip(Behaviour behaviour)
    {
        return behaviour.Faulted || !behaviour.Enabled;
    }

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

    private readonly struct InvocationScope(Behaviour? previousBehaviour, ScriptInvoker? previousInvoker) : IDisposable
    {
        public void Dispose()
        {
            currentBehaviour = previousBehaviour;
            currentInvoker = previousInvoker;
        }
    }
}
