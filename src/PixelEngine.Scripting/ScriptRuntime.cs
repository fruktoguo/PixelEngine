namespace PixelEngine.Scripting;

/// <summary>
/// 基于脚本 Scene 的默认运行时，实现相位 1 生命周期派发。
/// </summary>
public sealed class ScriptRuntime : IScriptRuntime
{
#if !PIXELENGINE_NATIVEAOT
    private readonly HotReloadService? _hotReload;
#endif
    private readonly ScriptHotReloadController? _hotReloadController;
    private readonly IScriptHotReloadDiagnosticSink? _hotReloadDiagnosticSink;
    private readonly Action<System.Reflection.Assembly>? _hotReloadAssemblyLoaded;
    private IScriptContext? _context;
    private Exception? _lastReportedWatcherException;
    private bool _shutdown;

    /// <summary>
    /// 创建默认脚本运行时。
    /// </summary>
    public ScriptRuntime()
    {
    }

    /// <summary>
    /// 创建带热重载控制器的脚本运行时；控制器会在运行时关闭时释放。
    /// </summary>
    /// <param name="hotReloadController">热重载控制器。</param>
    public ScriptRuntime(ScriptHotReloadController hotReloadController)
        : this(hotReloadController, hotReloadDiagnosticSink: null)
    {
    }

    /// <summary>
    /// 创建带热重载控制器与诊断 sink 的脚本运行时；控制器会在运行时关闭时释放。
    /// </summary>
    /// <param name="hotReloadController">热重载控制器。</param>
    /// <param name="hotReloadDiagnosticSink">可选热重载诊断 sink。</param>
    public ScriptRuntime(ScriptHotReloadController hotReloadController, IScriptHotReloadDiagnosticSink? hotReloadDiagnosticSink)
        : this(hotReloadController, hotReloadDiagnosticSink, hotReloadAssemblyLoaded: null)
    {
    }

    /// <summary>
    /// 创建带热重载控制器、诊断 sink 与程序集注册回调的脚本运行时；控制器会在运行时关闭时释放。
    /// </summary>
    /// <param name="hotReloadController">热重载控制器。</param>
    /// <param name="hotReloadDiagnosticSink">可选热重载诊断 sink。</param>
    /// <param name="hotReloadAssemblyLoaded">热重载成功后接收最新动态脚本程序集的回调。</param>
    public ScriptRuntime(
        ScriptHotReloadController hotReloadController,
        IScriptHotReloadDiagnosticSink? hotReloadDiagnosticSink,
        Action<System.Reflection.Assembly>? hotReloadAssemblyLoaded)
    {
        _hotReloadController = hotReloadController ?? throw new ArgumentNullException(nameof(hotReloadController));
        _hotReloadDiagnosticSink = hotReloadDiagnosticSink;
        _hotReloadAssemblyLoaded = hotReloadAssemblyLoaded;
    }

#if !PIXELENGINE_NATIVEAOT
    internal ScriptRuntime(HotReloadService hotReload)
    {
        _hotReload = hotReload ?? throw new ArgumentNullException(nameof(hotReload));
    }
#endif

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
        context.ClearFrameTransientRequests();
#if !PIXELENGINE_NATIVEAOT
        _ = _hotReload?.ApplyPendingReload();
#endif
        ReportWatcherException();
        _ = ApplyPendingReload();

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
    /// GUI 绘制相位在相位 1 派发 Behaviour OnGui。
    /// </summary>
    /// <param name="gui">本次 GUI 绘制相位的上下文。</param>
    public void DrawGui(IGuiContext gui)
    {
        ArgumentNullException.ThrowIfNull(gui);
        IScriptContext context = RequireContext();
        context.Scene.DispatchGui(context, gui);
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
    /// 结束 Play Session，对仍存活且已启动的 Behaviour 派发 OnDestroy 并重置启动状态。
    /// </summary>
    public void EndPlaySession()
    {
        IScriptContext context = RequireContext();
        context.Scene.EndPlaySession(context);
    }

    /// <summary>
    /// 将热重载目标切换到新的脚本 Scene，供编辑态 authoring projection 刷新复用同一脚本运行时。
    /// </summary>
    /// <param name="scene">新的脚本 Scene。</param>
    public void ReplaceScene(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ThrowIfShutdown();
        if (_context is ScriptSimulationContext simulationContext)
        {
            simulationContext.ReplaceScene(scene);
        }
        else if (_context is not null && !ReferenceEquals(_context.Scene, scene))
        {
            throw new InvalidOperationException("当前脚本上下文不支持替换脚本 Scene。");
        }

        _hotReloadController?.ReplaceScene(scene);
    }

    /// <summary>
    /// 在编辑态显式应用待处理热重载，复用运行时的程序集注册与诊断路径。
    /// </summary>
    /// <returns>热重载应用结果。</returns>
    public ScriptHotReloadApplyResult ApplyPendingReload()
    {
        ThrowIfShutdown();
        if (_hotReloadController is null)
        {
            return new ScriptHotReloadApplyResult(
                ScriptHotReloadStatus.NoPendingReload,
                [],
                OldContextUnloaded: true,
                LoadedAssembly: null);
        }

        ScriptHotReloadApplyResult result = _hotReloadController.ApplyPendingReload();
        ProcessHotReloadResult(result);
        return result;
    }

    /// <summary>
    /// 捕获当前脚本字段状态，供 Editor 临时 Play 回滚。
    /// </summary>
    /// <returns>脚本字段快照。</returns>
    public ScriptPlaySessionSnapshot CapturePlaySessionSnapshot()
    {
        IScriptContext context = RequireContext();
        return context.Scene.CapturePlaySessionSnapshot();
    }

    /// <summary>
    /// 恢复先前捕获的脚本字段状态。
    /// </summary>
    /// <param name="snapshot">脚本字段快照。</param>
    public void RestorePlaySessionSnapshot(ScriptPlaySessionSnapshot snapshot)
    {
        IScriptContext context = RequireContext();
        context.Scene.RestorePlaySessionSnapshot(snapshot);
    }

    /// <summary>
    /// 关闭脚本运行时；由 Hosting 生命周期关闭流程调用。
    /// </summary>
    public void Shutdown()
    {
        if (!_shutdown && _context is not null)
        {
            _context.Scene.EndPlaySession(_context);
        }

#if !PIXELENGINE_NATIVEAOT
        _hotReload?.Dispose();
#endif
        _hotReloadController?.Dispose();
        _shutdown = true;
        _context = null;
    }

    private void ProcessHotReloadResult(ScriptHotReloadApplyResult result)
    {
        if (result.Status == ScriptHotReloadStatus.Reloaded && result.LoadedAssembly is not null)
        {
            _hotReloadAssemblyLoaded?.Invoke(result.LoadedAssembly);
        }

        ReportHotReloadResult(result);
    }

    private void ReportWatcherException()
    {
        if (_hotReloadController?.LastWatcherException is not { } watcherException ||
            ReferenceEquals(watcherException, _lastReportedWatcherException))
        {
            return;
        }

        _lastReportedWatcherException = watcherException;
        _hotReloadDiagnosticSink?.Report(new ScriptHotReloadDiagnostic(
            DateTimeOffset.UtcNow,
            ScriptHotReloadDiagnosticKind.WatcherException,
            ScriptHotReloadStatus.NoPendingReload,
            $"脚本热重载监听异常：{watcherException.Message}",
            [watcherException.ToString()]));
    }

    private void ReportHotReloadResult(ScriptHotReloadApplyResult result)
    {
        if (result.Status == ScriptHotReloadStatus.NoPendingReload)
        {
            return;
        }

        _hotReloadDiagnosticSink?.Report(new ScriptHotReloadDiagnostic(
            DateTimeOffset.UtcNow,
            ScriptHotReloadDiagnosticKind.ReloadResult,
            result.Status,
            FormatHotReloadMessage(result),
            result.Diagnostics));
    }

    private static string FormatHotReloadMessage(ScriptHotReloadApplyResult result)
    {
        return result.Status switch
        {
            ScriptHotReloadStatus.CompileFailed => "脚本编译失败",
            ScriptHotReloadStatus.ApplyFailed => "脚本热重载应用失败，旧脚本保持运行",
            ScriptHotReloadStatus.Reloaded => result.OldContextUnloaded ? "脚本热重载完成" : "脚本热重载完成，旧 ALC 尚未卸载",
            ScriptHotReloadStatus.NoPendingReload => "没有待处理脚本热重载",
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, "未知热重载状态。"),
        };
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
