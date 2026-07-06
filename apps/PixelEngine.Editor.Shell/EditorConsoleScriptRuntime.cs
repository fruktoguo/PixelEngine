using PixelEngine.Scripting;

namespace PixelEngine.Editor.Shell;

internal sealed class EditorConsoleScriptRuntime : IScriptRuntime
{
    private readonly IEditorConsoleSink _console;
    private readonly ScriptHotReloadRuntimeOptions? _hotReloadOptions;
    private readonly ScriptRuntime _inner = new();
    private ScriptHotReloadController? _hotReloadController;
    private Exception? _lastLoggedWatcherException;
    private bool _shutdown;

    public EditorConsoleScriptRuntime(IEditorConsoleSink console, ScriptHotReloadRuntimeOptions? hotReloadOptions = null)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _hotReloadOptions = hotReloadOptions;
    }

    public void Initialize(IScriptContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ThrowIfShutdown();
        _inner.Initialize(context);
        if (_hotReloadOptions is null)
        {
            return;
        }

        _hotReloadController = new ScriptHotReloadController(context.Scene, context);
        try
        {
            _hotReloadController.StartWatching(
                _hotReloadOptions.AssemblyName,
                _hotReloadOptions.SourceDirectory,
                _hotReloadOptions.PreserveState,
                _hotReloadOptions.SearchPattern,
                _hotReloadOptions.IncludeSubdirectories,
                _hotReloadOptions.DebounceInterval);
            _console.Add(new EditorConsoleEntry(
                DateTimeOffset.UtcNow,
                EditorConsoleCategory.Script,
                EditorConsoleSeverity.Info,
                "script-hot-reload",
                $"脚本热重载监听已启动：{_hotReloadOptions.SourceDirectory}"));
        }
        catch (Exception ex) when (!OperatingSystem.IsBrowser())
        {
            _console.Add(new EditorConsoleEntry(
                DateTimeOffset.UtcNow,
                EditorConsoleCategory.Script,
                EditorConsoleSeverity.Error,
                "script-hot-reload",
                $"脚本热重载监听启动失败：{ex.Message}"));
        }
    }

    public void BeginFrame()
    {
        ThrowIfShutdown();
        ApplyPendingReload();
        _inner.BeginFrame();
    }

    public void Update(float dt)
    {
        ThrowIfShutdown();
        _inner.Update(dt);
    }

    public void FixedSimTick()
    {
        ThrowIfShutdown();
        _inner.FixedSimTick();
    }

    public void DrawGui(IGuiContext gui)
    {
        ThrowIfShutdown();
        _inner.DrawGui(gui);
    }

    public void EndFrame()
    {
        ThrowIfShutdown();
        _inner.EndFrame();
    }

    public void EndPlaySession()
    {
        ThrowIfShutdown();
        _inner.EndPlaySession();
    }

    public ScriptPlaySessionSnapshot CapturePlaySessionSnapshot()
    {
        ThrowIfShutdown();
        return _inner.CapturePlaySessionSnapshot();
    }

    public void RestorePlaySessionSnapshot(ScriptPlaySessionSnapshot snapshot)
    {
        ThrowIfShutdown();
        _inner.RestorePlaySessionSnapshot(snapshot);
    }

    public void Shutdown()
    {
        if (_shutdown)
        {
            return;
        }

        _inner.Shutdown();
        _hotReloadController?.Dispose();
        _shutdown = true;
    }

    private void ApplyPendingReload()
    {
        if (_hotReloadController is null)
        {
            return;
        }

        if (_hotReloadController.LastWatcherException is { } watcherException && !ReferenceEquals(watcherException, _lastLoggedWatcherException))
        {
            _lastLoggedWatcherException = watcherException;
            _console.Add(new EditorConsoleEntry(
                DateTimeOffset.UtcNow,
                EditorConsoleCategory.Script,
                EditorConsoleSeverity.Error,
                "script-hot-reload",
                watcherException.ToString()));
        }

        if (!_hotReloadController.HasPendingReload)
        {
            return;
        }

        ScriptHotReloadApplyResult result = _hotReloadController.ApplyPendingReload();
        bool success = result.Status == ScriptHotReloadStatus.Reloaded;
        string message = result.Status switch
        {
            ScriptHotReloadStatus.NoPendingReload => "没有待处理脚本热重载",
            ScriptHotReloadStatus.CompileFailed => "脚本编译失败",
            ScriptHotReloadStatus.ApplyFailed => "脚本热重载应用失败，旧脚本保持运行",
            ScriptHotReloadStatus.Reloaded => result.OldContextUnloaded ? "脚本热重载完成" : "脚本热重载完成，旧 ALC 尚未卸载",
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, "未知热重载状态。"),
        };
        if (result.Status != ScriptHotReloadStatus.NoPendingReload)
        {
            _console.AddScriptDiagnostics("script-hot-reload", message, result.Diagnostics, success);
        }
    }

    private void ThrowIfShutdown()
    {
        ObjectDisposedException.ThrowIf(_shutdown, this);
    }
}
