using System.Diagnostics.CodeAnalysis;
using PixelEngine.Gui;
using PixelEngine.Physics;
using PixelEngine.Rendering;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using PixelEngine.Scripting;
using Silk.NET.OpenGL;
using RuntimeUi = PixelEngine.UI;
using ScriptScene = PixelEngine.Scripting.Scene;

namespace PixelEngine.Hosting;

/// <summary>
/// Hosting 提供给 Demo 窗口探针的受控诊断 API，避免 Demo 直接依赖 Simulation 内部类型。
/// </summary>
public sealed class EngineProbeApi
{
    private readonly CellGrid _grid;
    private readonly SimulationKernel _kernel;
    private readonly TemperatureField _temperature;
    private readonly MaterialTable _materials;
    private readonly ParticleSystem _particles;
    private PhysicsSystem? Physics { get; set; }
    private RenderPipeline? RenderPipeline { get; set; }
    private RenderPhaseDriver? RenderDriver { get; set; }
    private IScriptContext? ScriptContext { get; set; }
    private ScriptInputApi? InputApi { get; set; }
    private ScriptCameraApi? CameraApi { get; set; }
    private ScriptLightingApi? LightingApi { get; set; }
    private ScriptCameraSynchronizer? CameraSync { get; set; }
    private ScriptLightingSynchronizer? LightingSync { get; set; }
    private GameUiCanvasRegistry? GameUiRegistry { get; set; }
    private GameUiBackendSelection? GameUiSelection { get; set; }
    private RuntimeUi.UiInputRouter? GameUiInputRouter { get; set; }
    private GameUiPhaseDriver? GameUiDriver { get; set; }
    private GuiApp? Gui { get; set; }

    internal EngineProbeApi(
        CellGrid grid,
        SimulationKernel kernel,
        TemperatureField temperature,
        MaterialTable materials,
        ParticleSystem particles)
    {
        _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _temperature = temperature ?? throw new ArgumentNullException(nameof(temperature));
        _materials = materials ?? throw new ArgumentNullException(nameof(materials));
        _particles = particles ?? throw new ArgumentNullException(nameof(particles));
    }

    /// <summary>
    /// 当前活跃自由粒子数量。
    /// </summary>
    public int ActiveParticles => _particles.ActiveCount;

    /// <summary>
    /// 当前脚本场景；脚本上下文尚未接入时访问会抛出明确异常。
    /// </summary>
    public ScriptScene ScriptScene => RequireScriptContext().Scene;

    /// <summary>
    /// 尝试读取当前脚本场景；未装配 Scripting 的基础 Engine 返回 false，不以异常作为 probe 控制流。
    /// </summary>
    /// <param name="scene">成功时返回当前脚本场景；未装配时为 null。</param>
    /// <returns>已接入脚本上下文时返回 true。</returns>
    public bool TryGetScriptScene([NotNullWhen(true)] out ScriptScene? scene)
    {
        scene = ScriptContext?.Scene;
        return scene is not null;
    }

    /// <summary>
    /// Hosting 注入的脚本输入 probe；用于窗口 scripted probe 写入确定性输入快照。
    /// </summary>
    public ScriptInputApi Input => InputApi ?? throw MissingBinding("ScriptInputApi");

    /// <summary>
    /// Hosting 注入的脚本相机 probe；用于窗口 scripted probe 做世界/屏幕坐标转换。
    /// </summary>
    public ScriptCameraApi Camera => CameraApi ?? throw MissingBinding("ScriptCameraApi");

    /// <summary>
    /// Hosting 注入的脚本光照 probe；用于窗口 scripted probe 发出测试光源与 fog 请求。
    /// </summary>
    public ScriptLightingApi Lighting => LightingApi ?? throw MissingBinding("ScriptLightingApi");

    /// <summary>
    /// 当前脚本相机同步到 Rendering 后的快照。
    /// </summary>
    public ScriptCameraSynchronizer CameraSynchronizer => CameraSync ?? throw MissingBinding("ScriptCameraSynchronizer");

    /// <summary>
    /// 当前脚本光照同步到 Rendering 后的快照。
    /// </summary>
    public ScriptLightingSynchronizer LightingSynchronizer => LightingSync ?? throw MissingBinding("ScriptLightingSynchronizer");

    /// <summary>
    /// 当前同步后的点光源只读快照。
    /// </summary>
    public ReadOnlySpan<LightSource> PointLights => LightingSynchronizer.PointLights;

    /// <summary>
    /// 当前同步后的 fog-of-war buffer。
    /// </summary>
    public FogOfWarBuffer FogOfWar => LightingSynchronizer.FogOfWar;

    /// <summary>
    /// 当前物理系统的稳定统计快照；未接入 Physics 时返回默认空快照。
    /// </summary>
    public PhysicsSystemStats PhysicsStats => Physics?.Stats ?? default;

    /// <summary>
    /// 最近一次 Rendering phase 提交的 overlay 数量；未接入 Rendering 时返回 -1。
    /// </summary>
    public int RenderOverlayCount => RenderDriver?.LastOverlayCount ?? -1;

    /// <summary>
    /// 当前粒子渲染请求是否由 GPU point-sprite 路径接管。
    /// </summary>
    public bool CanRenderParticlesOnGpu => RenderPipeline?.CanRenderParticlesOnGpu ?? false;

    /// <summary>
    /// 当前 GPU frame timer 是否可用；未接入 Rendering 时返回 false。
    /// </summary>
    public bool GpuFrameTimerAvailable => RenderPipeline?.GpuFrameTimerAvailable ?? false;

    /// <summary>
    /// 捕获当前游戏 Web Canvas 数量与 UI 后端选择；窗口 UI 尚未接入时返回未附加快照。
    /// </summary>
    /// <returns>不暴露 Canvas registry 或 backend 实例的稳定诊断快照。</returns>
    public GameUiProbeSnapshot CaptureGameUi()
    {
        GameUiCanvasRegistry? registry = GameUiRegistry;
        GameUiBackendSelection? selection = GameUiSelection;
        return registry is null || selection is null
            ? default
            : new GameUiProbeSnapshot(
                IsAttached: true,
                CanvasCount: registry.Count,
                RequestedBackend: selection.Value.RequestedBackend,
                ActiveBackend: selection.Value.ActiveBackend,
                UsedFallback: selection.Value.UsedFallback,
                FallbackReason: selection.Value.FallbackReason,
                ActiveNativeProfile: selection.Value.ActiveNativeProfile);
    }

    /// <summary>
    /// 显式启用 shared/runtime Gui 按钮只读诊断；普通运行默认关闭。
    /// </summary>
    public void EnablePhysicalUiInputDiagnostics()
    {
        (Gui ?? throw MissingBinding("GuiApp")).SetButtonInputDiagnosticsEnabled(enabled: true);
    }

    /// <summary>
    /// 捕获物理 UI 输入的稳定只读快照，不向 Demo 暴露 Context、registry、router 或 Gui 实例。
    /// </summary>
    /// <returns>Canvas 目标、输入 capture、Gui 按钮和累计事件诊断。</returns>
    public PhysicalUiInputProbeSnapshot CapturePhysicalUiInput()
    {
        GameUiCanvasRegistry registry = GameUiRegistry ?? throw MissingBinding("GameUiCanvasRegistry");
        RuntimeUi.UiInputRouter inputRouter = GameUiInputRouter ?? throw MissingBinding("UiInputRouter");
        GameUiPhaseDriver driver = GameUiDriver ?? throw MissingBinding("GameUiPhaseDriver");
        GuiApp gui = Gui ?? throw MissingBinding("GuiApp");
        return new PhysicalUiInputProbeSnapshot(
            registry.CaptureInputDiagnostics(),
            inputRouter.Capture,
            gui.Input.Capture,
            gui.CaptureButtonInputDiagnostics(),
            driver.TotalDrainedEventCount);
    }

    /// <summary>
    /// 捕获当前 Hosting/Scripting 诊断快照。
    /// </summary>
    /// <returns>脚本可消费的诊断快照。</returns>
    public EngineDiagnosticsSnapshot CaptureDiagnostics()
    {
        return RequireScriptContext().Diagnostics.Capture();
    }

    /// <summary>
    /// 当前脚本/渲染帧序号。
    /// </summary>
    public long FrameCount => ScriptContext?.Time.FrameCount ?? _kernel.FrameIndex;

    /// <summary>
    /// 设置粒子渲染请求并返回实际可用的 probe 结果。
    /// </summary>
    /// <param name="requested">请求的粒子渲染模式。</param>
    /// <returns>请求、实际生效模式和 GPU 可用性。</returns>
    public ParticleRenderProbeResult SetParticleRenderMode(ParticleRenderMode requested)
    {
        Rendering.RenderPipeline pipeline = RenderPipeline ?? throw MissingBinding("RenderPipeline");
        pipeline.Settings.ParticleRenderMode = requested;
        bool gpuAvailable = pipeline.CanRenderParticlesOnGpu;
        ParticleRenderMode effective = gpuAvailable
            ? ParticleRenderMode.GpuPointSprite
            : ParticleRenderMode.CpuStamp;
        return new ParticleRenderProbeResult(requested, effective, gpuAvailable);
    }

    /// <summary>
    /// 注册一次 present 前 probe 回调；回调不会接触 RenderPipeline 或 OpenGL 事件签名。
    /// </summary>
    /// <param name="callback">交换缓冲前执行的回调。</param>
    /// <returns>解除注册的订阅。</returns>
    public IDisposable RegisterBeforeSwapBuffers(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Rendering.RenderPipeline pipeline = RenderPipeline ?? throw MissingBinding("RenderPipeline");
        return new SwapCallbackSubscription(pipeline, callback);
    }

    /// <summary>
    /// 按材质与颜色变体统计当前活跃自由粒子数量；只读扫描活跃前缀，不分配。
    /// </summary>
    /// <param name="material">运行时材质 id。</param>
    /// <param name="colorVariant">粒子颜色变体。</param>
    /// <returns>匹配条件的活跃自由粒子数量。</returns>
    public int CountActiveParticles(ushort material, byte colorVariant)
    {
        ReadOnlySpan<Particle> active = _particles.ActiveReadOnly;
        int count = 0;
        for (int i = 0; i < active.Length; i++)
        {
            Particle particle = active[i];
            if (particle.Material == material && particle.ColorVariant == colorVariant)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 按稳定材质名解析运行时材质 id。
    /// </summary>
    /// <param name="name">材质稳定名称。</param>
    /// <param name="id">解析成功时返回运行时材质 id。</param>
    /// <returns>若材质存在则返回 true。</returns>
    public bool TryResolveMaterial(string name, out ushort id)
    {
        return _materials.TryGetId(name, out id);
    }

    /// <summary>
    /// 按稳定材质名解析运行时材质 id；缺失时抛出明确异常。
    /// </summary>
    /// <param name="name">材质稳定名称。</param>
    /// <returns>运行时材质 id。</returns>
    public ushort ResolveMaterial(string name)
    {
        return TryResolveMaterial(name, out ushort id)
            ? id
            : throw new InvalidOperationException($"缺少材质：{name}。");
    }

    /// <summary>
    /// 读取指定世界坐标的材质 id。
    /// </summary>
    /// <param name="x">世界 X 坐标。</param>
    /// <param name="y">世界 Y 坐标。</param>
    /// <returns>当前材质 id。</returns>
    public ushort MaterialAt(int x, int y)
    {
        return _grid.GetMaterial(x, y);
    }

    /// <summary>
    /// 在输入相位写入 cell 并唤醒对应 dirty 区域。
    /// </summary>
    /// <param name="x">世界 X 坐标。</param>
    /// <param name="y">世界 Y 坐标。</param>
    /// <param name="material">运行时材质 id。</param>
    public void EditCellAtInputPhase(int x, int y, ushort material)
    {
        _kernel.EditCellAtInputPhase(x, y, material, persistentFlags: 0);
    }

    /// <summary>
    /// 把粗温度场指定坐标调整到目标温度。
    /// </summary>
    /// <param name="x">世界 X 坐标。</param>
    /// <param name="y">世界 Y 坐标。</param>
    /// <param name="targetTemperature">目标摄氏温度。</param>
    public void SetTemperature(int x, int y, float targetTemperature)
    {
        float current = _temperature.GetTemperature(x, y);
        _temperature.AddHeat(x, y, targetTemperature - current);
    }

    /// <summary>
    /// 确保自由粒子池容量至少达到指定活跃粒子数。
    /// </summary>
    /// <param name="maxActiveCount">最小活跃粒子容量。</param>
    public void EnsureParticleCapacity(int maxActiveCount)
    {
        if (_particles.Settings.MaxActiveCount < maxActiveCount)
        {
            _particles.ApplySettings(_particles.Settings with { MaxActiveCount = maxActiveCount });
        }
    }

    /// <summary>
    /// 清空当前所有自由粒子。
    /// </summary>
    public void ClearParticles()
    {
        _particles.Clear();
    }

    /// <summary>
    /// 尝试生成一个自由粒子。
    /// </summary>
    /// <param name="x">起始 X 坐标。</param>
    /// <param name="y">起始 Y 坐标。</param>
    /// <param name="velocityX">初始 X 速度。</param>
    /// <param name="velocityY">初始 Y 速度。</param>
    /// <param name="material">运行时材质 id。</param>
    /// <param name="colorVariant">颜色变体。</param>
    /// <param name="life">粒子 lifetime。</param>
    /// <returns>若粒子已生成则返回 true。</returns>
    public bool TrySpawnParticle(
        float x,
        float y,
        float velocityX,
        float velocityY,
        ushort material,
        byte colorVariant,
        ushort life)
    {
        ParticleSpawn spawn = new(x, y, velocityX, velocityY, material, colorVariant, (byte)Math.Min(byte.MaxValue, life));
        return _particles.TrySpawn(in spawn);
    }

    internal void AttachPhysics(PhysicsSystem physics)
    {
        Physics = physics ?? throw new ArgumentNullException(nameof(physics));
    }

    internal void AttachRendering(RenderPipeline pipeline, RenderPhaseDriver driver)
    {
        RenderPipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        RenderDriver = driver ?? throw new ArgumentNullException(nameof(driver));
    }

    internal void AttachScriptContext(IScriptContext context)
    {
        ScriptContext = context ?? throw new ArgumentNullException(nameof(context));
        InputApi ??= context.Input as ScriptInputApi;
        CameraApi ??= context.Camera as ScriptCameraApi;
        LightingApi ??= context.Lighting as ScriptLightingApi;
    }

    internal void AttachRegisteredScriptBindings(
        IScriptContext? context,
        ScriptInputApi? input,
        ScriptCameraApi? camera,
        ScriptLightingApi? lighting,
        ScriptCameraSynchronizer? cameraSynchronizer,
        ScriptLightingSynchronizer? lightingSynchronizer)
    {
        if (context is not null)
        {
            AttachScriptContext(context);
        }

        InputApi ??= input;
        CameraApi ??= camera;
        LightingApi ??= lighting;
        CameraSync ??= cameraSynchronizer;
        LightingSync ??= lightingSynchronizer;
    }

    internal void AttachCameraSynchronizer(ScriptCameraSynchronizer synchronizer)
    {
        CameraSync = synchronizer ?? throw new ArgumentNullException(nameof(synchronizer));
    }

    internal void AttachLightingSynchronizer(ScriptLightingSynchronizer synchronizer)
    {
        LightingSync = synchronizer ?? throw new ArgumentNullException(nameof(synchronizer));
    }

    internal void AttachGameUi(
        GameUiCanvasRegistry registry,
        in GameUiBackendSelection selection,
        RuntimeUi.UiInputRouter inputRouter,
        GameUiPhaseDriver driver)
    {
        GameUiRegistry = registry ?? throw new ArgumentNullException(nameof(registry));
        GameUiSelection = selection;
        GameUiInputRouter = inputRouter ?? throw new ArgumentNullException(nameof(inputRouter));
        GameUiDriver = driver ?? throw new ArgumentNullException(nameof(driver));
    }

    internal void AttachGui(GuiApp gui)
    {
        Gui = gui ?? throw new ArgumentNullException(nameof(gui));
    }

    private IScriptContext RequireScriptContext()
    {
        return ScriptContext ?? throw MissingBinding("IScriptContext");
    }

    private static InvalidOperationException MissingBinding(string name)
    {
        return new InvalidOperationException($"EngineProbeApi 尚未接入 {name}，请先完成对应 Engine 装配阶段。");
    }

    private sealed class SwapCallbackSubscription : IDisposable
    {
        private readonly Action _callback;
        private readonly Action<GL> _handler;
        private RenderPipeline? _pipeline;

        public SwapCallbackSubscription(RenderPipeline pipeline, Action callback)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            _handler = Handle;
            pipeline.BeforeSwapBuffers += _handler;
        }

        private void Handle(GL _)
        {
            _callback();
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _pipeline, null)?.BeforeSwapBuffers -= _handler;
        }
    }
}

/// <summary>
/// 粒子渲染 probe 对请求模式、实际模式和 GPU 能力的稳定快照。
/// </summary>
/// <param name="Requested">调用方请求的模式。</param>
/// <param name="Effective">当前机器实际可执行的模式。</param>
/// <param name="GpuAvailable">GPU point-sprite 路径是否可执行。</param>
public readonly record struct ParticleRenderProbeResult(
    ParticleRenderMode Requested,
    ParticleRenderMode Effective,
    bool GpuAvailable);

/// <summary>
/// 游戏 Web Canvas probe 的稳定快照，不向 Demo 暴露 Hosting registry 或具体 UI backend。
/// </summary>
/// <param name="IsAttached">窗口 UI runtime 是否已经附加。</param>
/// <param name="CanvasCount">当前场景已物化的 Canvas 数量。</param>
/// <param name="RequestedBackend">启动配置请求的后端。</param>
/// <param name="ActiveBackend">primary Canvas 实际使用的后端。</param>
/// <param name="UsedFallback">请求后端是否发生显式降级。</param>
/// <param name="FallbackReason">发生降级时的稳定诊断原因；未降级时为 null。</param>
/// <param name="ActiveNativeProfile">实际启用的 native renderer profile；无 native 后端时为 null。</param>
public readonly record struct GameUiProbeSnapshot(
    bool IsAttached,
    int CanvasCount,
    RuntimeUi.UiBackendKind RequestedBackend,
    RuntimeUi.UiBackendKind ActiveBackend,
    bool UsedFallback,
    string? FallbackReason,
    string? ActiveNativeProfile);

/// <summary>
/// 物理 UI 输入 probe 的稳定只读快照，不暴露 Hosting 服务实例。
/// </summary>
/// <param name="Canvas">多 Canvas 指针目标与按钮转发诊断。</param>
/// <param name="Capture">Game UI 当前输入 capture。</param>
/// <param name="GuiCapture">shared/runtime Gui 当前输入 capture。</param>
/// <param name="GuiButtons">shared/runtime Gui 按钮诊断。</param>
/// <param name="TotalDrainedEventCount">Game UI 累计 drain 事件数。</param>
public readonly record struct PhysicalUiInputProbeSnapshot(
    GameUiCanvasInputDiagnostics Canvas,
    RuntimeUi.UiInputCapture Capture,
    GuiInputSnapshot GuiCapture,
    GuiButtonInputDiagnostics GuiButtons,
    long TotalDrainedEventCount);
