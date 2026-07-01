using System.Diagnostics;
using System.Reflection;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Core.Time;
using PixelEngine.Scripting;

namespace PixelEngine.Hosting;

/// <summary>
/// PixelEngine 运行时门面，拥有 EngineContext 并控制生命周期。
/// </summary>
public sealed class Engine : IDisposable
{
    private readonly EngineLifecycle _lifecycle;
    private bool _disposed;

    internal Engine(EngineContext context, EnginePhasePipeline phases, EngineLifecycle lifecycle)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(phases);
        ArgumentNullException.ThrowIfNull(lifecycle);
        Context = context;
        Phases = phases;
        _lifecycle = lifecycle;
        State = EngineRunState.Created;
        Mode = EngineExecutionMode.Play;
        RequestedSimHz = context.Options.SimHz;
    }

    /// <summary>
    /// 当前运行上下文。
    /// </summary>
    public EngineContext Context { get; }

    /// <summary>
    /// 12 相位同步调度管线。
    /// </summary>
    public EnginePhasePipeline Phases { get; }

    /// <summary>
    /// 当前生命周期状态。
    /// </summary>
    public EngineRunState State { get; private set; }

    /// <summary>
    /// 当前 Play/Edit/Step 执行模式。
    /// </summary>
    public EngineExecutionMode Mode { get; private set; }

    /// <summary>
    /// 当前由用户或工具请求的基础 sim 频率；自动降级可临时覆盖到更低档。
    /// </summary>
    public double RequestedSimHz { get; private set; }

    /// <summary>
    /// 切换到运行模式，后续 tick 会推进 sim/physics。
    /// </summary>
    public void EnterPlayMode()
    {
        ThrowIfShutdown();
        Mode = EngineExecutionMode.Play;
    }

    /// <summary>
    /// 切换到编辑模式，后续普通 tick 只推进渲染与后台流式相位。
    /// </summary>
    public void EnterEditMode()
    {
        ThrowIfShutdown();
        Mode = EngineExecutionMode.Edit;
    }

    /// <summary>
    /// 设置基础 sim 频率；后续普通 tick 由 FrameClock 使用该频率，自动过载降级仍可临时降到 30Hz。
    /// </summary>
    /// <param name="simHz">目标 sim 频率，目前支持 60Hz 与 30Hz。</param>
    public void SetRequestedSimHz(double simHz)
    {
        ThrowIfShutdown();
        Context.Clock.SimHz = simHz;
        Context.Counters.SimHz = simHz;
        RequestedSimHz = simHz;
    }

    /// <summary>
    /// 在编辑模式下执行恰好一个 sim tick，随后回到编辑模式。
    /// </summary>
    public FrameTiming StepOnce(double realDeltaSeconds = 0)
    {
        ThrowIfShutdown();
        if (Mode != EngineExecutionMode.Edit)
        {
            throw new InvalidOperationException("StepOnce 只能从编辑模式触发。");
        }

        Mode = EngineExecutionMode.Step;
        try
        {
            return RunOneTick(realDeltaSeconds);
        }
        finally
        {
            Mode = EngineExecutionMode.Edit;
        }
    }

    /// <summary>
    /// 注册包含 Demo Behaviour 的脚本程序集，供脚本宿主在装配期发现类型。
    /// </summary>
    /// <param name="assembly">脚本程序集。</param>
    public void RegisterScriptAssembly(Assembly assembly)
    {
        ThrowIfShutdown();
        Context.GetService<ScriptAssemblyRegistry>().Register(assembly);
    }

    /// <summary>
    /// 从当前 ContentRoot 加载材质与反应内容包，并注册材质/反应运行时服务。
    /// </summary>
    /// <returns>加载后的内容包。</returns>
    public EngineContentPackage LoadContentPackage()
    {
        ThrowIfShutdown();
        EngineContentPackage package = EngineContentLoader.LoadMaterialPackage(Context.Options.ContentRoot);
        Context.RegisterService(package);
        Context.RegisterService<IMaterialQuery>(EngineServiceRole.MaterialRegistry, package.MaterialRegistry);
        Context.RegisterService(package.MaterialRegistry);
        Context.RegisterService(package.MaterialTable);
        Context.RegisterService(package.ReactionTable);
        return package;
    }

    /// <summary>
    /// 判断当前 ContentRoot 是否存在可加载的材质/反应内容包。
    /// </summary>
    /// <returns>materials.json 与 reactions.json 都存在时返回 true。</returns>
    public bool HasContentPackage()
    {
        ThrowIfShutdown();
        return EngineContentLoader.HasMaterialPackage(Context.Options.ContentRoot);
    }

    /// <summary>
    /// 切换到已注册场景描述；实际世界构建由对应场景后端在后续装配中完成。
    /// </summary>
    /// <param name="name">场景稳定名称。</param>
    /// <returns>当前场景实例。</returns>
    public Scene LoadScene(string name)
    {
        ThrowIfShutdown();
        Scene scene = Context.GetService<ISceneService>().SwitchTo(name);
        if (scene.Descriptor.SourceKind == SceneSourceKind.SceneFile && scene.ResolvedSource is not null)
        {
            PixelEngine.Scripting.Scene scriptScene = EngineSceneDocumentLoader.Load(
                scene.ResolvedSource,
                Context.GetService<ScriptAssemblyRegistry>());
            scene.AttachScriptScene(scriptScene);
            Context.RegisterService(scriptScene);
        }

        return scene;
    }

    /// <summary>
    /// 持续运行直到收到取消请求或 Engine 被关闭。
    /// </summary>
    public void Run(CancellationToken cancellationToken = default)
    {
        ThrowIfShutdown();
        Stopwatch stopwatch = Stopwatch.StartNew();
        double previousSeconds = stopwatch.Elapsed.TotalSeconds;
        while (!cancellationToken.IsCancellationRequested && State != EngineRunState.Shutdown)
        {
            double now = stopwatch.Elapsed.TotalSeconds;
            _ = RunOneTick(now - previousSeconds);
            previousSeconds = now;
        }
    }

    /// <summary>
    /// 执行一个运行时 tick 并推进固定步长帧时钟。
    /// </summary>
    public FrameTiming RunOneTick(double realDeltaSeconds = 0)
    {
        ThrowIfShutdown();
        State = EngineRunState.Running;
        FrameProfiler profiler = Context.Profiler;
        profiler.BeginFrame();
        FrameTiming timing;
        try
        {
            using (profiler.Measure(FramePhase.InputAndTime))
            {
                ApplyOverloadPolicy(realDeltaSeconds);
                timing = BeginRuntimeFrame(realDeltaSeconds);
            }

            Phases.Execute(this, timing);
            Context.Counters.SimHz = Context.Clock.SimHz;
        }
        finally
        {
            profiler.EndFrame();
        }

        return timing;
    }

    /// <summary>
    /// headless 模式下按固定次数驱动 tick，供测试与基准使用。
    /// </summary>
    public void RunHeadlessTicks(int tickCount, double realDeltaSeconds = 0)
    {
        ThrowIfShutdown();
        ArgumentOutOfRangeException.ThrowIfNegative(tickCount);
        if (!Context.Options.Headless)
        {
            throw new InvalidOperationException("RunHeadlessTicks 只能在 headless 模式下调用。");
        }

        for (int i = 0; i < tickCount; i++)
        {
            _ = RunOneTick(realDeltaSeconds);
        }
    }

    /// <summary>
    /// 关闭引擎并按生命周期顺序释放已装配资源。
    /// </summary>
    public void Shutdown()
    {
        if (State == EngineRunState.Shutdown)
        {
            return;
        }

        Exception? lifecycleFailure = null;
        try
        {
            _lifecycle.Shutdown();
        }
        catch (Exception exception)
        {
            lifecycleFailure = exception;
        }
        finally
        {
            Context.Jobs.Dispose();
            State = EngineRunState.Shutdown;
        }

        if (lifecycleFailure is not null)
        {
            throw lifecycleFailure;
        }
    }

    /// <summary>
    /// 释放引擎资源。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Shutdown();
        _disposed = true;
    }

    private void ThrowIfShutdown()
    {
        ObjectDisposedException.ThrowIf(State == EngineRunState.Shutdown, this);
    }

    private void ApplyOverloadPolicy(double realDeltaSeconds)
    {
        EngineOverloadController overload = Context.GetService<EngineOverloadController>();
        EngineQualityTier previousTier = Context.QualityTier;
        EngineQualityTier tier = overload.SubmitFrame(realDeltaSeconds * 1000.0);
        Context.SetQualityTier(tier);
        if (tier != previousTier && tier >= EngineQualityTier.ReducedLighting)
        {
            ApplyGpuComputeDegradation();
        }

        Context.Clock.SimHz = tier >= EngineQualityTier.Sim30Hz
            ? PixelEngine.Core.EngineConstants.SimHzDownscaled
            : RequestedSimHz;
    }

    private void ApplyGpuComputeDegradation()
    {
        if (Context.TryGetService(out IGpuComputeQualityDegrader degrader))
        {
            _ = degrader.DegradeGpuComputeOneStep();
        }
    }

    private FrameTiming BeginRuntimeFrame(double realDeltaSeconds)
    {
        return Mode switch
        {
            EngineExecutionMode.Edit => Context.Clock.BeginRenderOnlyFrame(realDeltaSeconds),
            EngineExecutionMode.Step => Context.Clock.BeginForcedSimFrame(realDeltaSeconds),
            EngineExecutionMode.Play => Context.Clock.BeginFrame(realDeltaSeconds),
            _ => throw new InvalidOperationException($"未知 Engine 执行模式：{Mode}。"),
        };
    }
}
