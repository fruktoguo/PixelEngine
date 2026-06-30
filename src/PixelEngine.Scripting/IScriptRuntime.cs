namespace PixelEngine.Scripting;

/// <summary>
/// 脚本运行时接口；由 Hosting 在相位 1 驱动。
/// </summary>
public interface IScriptRuntime
{
    /// <summary>
    /// 使用脚本上下文初始化运行时；由 Hosting 在主循环启动前调用。
    /// </summary>
    /// <param name="context">脚本访问引擎能力的统一上下文。</param>
    void Initialize(IScriptContext context);

    /// <summary>
    /// 相位 1 开始时调用，用于处理待启动脚本。
    /// </summary>
    void BeginFrame();

    /// <summary>
    /// 每个渲染帧在相位 1 调用一次，用于派发 OnUpdate 与系统 OnFrame。
    /// </summary>
    /// <param name="dt">本帧 delta time，单位秒。</param>
    void Update(float dt);

    /// <summary>
    /// 仅在执行 sim tick 的帧于相位 1 调用，用于派发 OnFixedSimTick 与系统 OnSimTick。
    /// </summary>
    void FixedSimTick();

    /// <summary>
    /// 相位 1 结束时调用，用于刷新延迟销毁队列。
    /// </summary>
    void EndFrame();

    /// <summary>
    /// 关闭运行时并释放脚本侧资源；由 Hosting 生命周期关闭流程调用。
    /// </summary>
    void Shutdown();
}
