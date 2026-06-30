namespace PixelEngine.Scripting;

/// <summary>
/// 脚本运行时接口；由 Hosting 在相位 1 驱动。
/// </summary>
public interface IScriptRuntime
{
    /// <summary>
    /// 使用脚本上下文初始化运行时。
    /// </summary>
    void Initialize(IScriptContext context);

    /// <summary>
    /// 相位 1 开始时调用，用于处理待启动脚本。
    /// </summary>
    void BeginFrame();

    /// <summary>
    /// 每个渲染帧调用一次，用于派发 OnUpdate 与系统 OnFrame。
    /// </summary>
    /// <param name="dt">本帧 delta time，单位秒。</param>
    void Update(float dt);

    /// <summary>
    /// 仅在执行 sim tick 的帧调用，用于派发 OnFixedSimTick 与系统 OnSimTick。
    /// </summary>
    void FixedSimTick();

    /// <summary>
    /// 相位 1 结束时调用，用于刷新延迟销毁队列。
    /// </summary>
    void EndFrame();

    /// <summary>
    /// 关闭运行时并释放脚本侧资源。
    /// </summary>
    void Shutdown();
}
