namespace PixelEngine.Scripting;

internal readonly record struct ScriptExceptionRecord(string ScriptType, string Callback, string Message);

internal sealed class ScriptInvoker
{
    private readonly List<ScriptExceptionRecord> _exceptions = [];

    public IReadOnlyList<ScriptExceptionRecord> Exceptions => _exceptions;

    public void InvokeStart(Behaviour behaviour, IScriptContext context)
    {
        if (ShouldSkip(behaviour))
        {
            return;
        }

        try
        {
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
            behaviour.InvokeDestroy(context);
        }
        catch (Exception exception)
        {
            MarkFaulted(behaviour, "OnDestroy", exception);
        }
    }

    private static bool ShouldSkip(Behaviour behaviour)
    {
        return behaviour.Faulted || !behaviour.Enabled;
    }

    private void MarkFaulted(Behaviour behaviour, string callback, Exception exception)
    {
        behaviour.MarkFaulted(exception);
        _exceptions.Add(new ScriptExceptionRecord(
            behaviour.GetType().FullName ?? behaviour.GetType().Name,
            callback,
            exception.Message));
    }
}
