namespace PixelEngine.Hosting;

/// <summary>
/// 架构 §3.3 定义的 12 个运行时相位。
/// </summary>
public enum EnginePhase
{
    /// <summary>
    /// 相位 0：输入与时间。
    /// </summary>
    InputAndTime = 0,

    /// <summary>
    /// 相位 1：玩法逻辑与脚本。
    /// </summary>
    GameLogicAndScripts = 1,

    /// <summary>
    /// 相位 2：世界驻留结构性增删。
    /// </summary>
    ResidencyApply = 2,

    /// <summary>
    /// 相位 3：自由粒子沉积到 cell。
    /// </summary>
    ParticleToCell = 3,

    /// <summary>
    /// 相位 4：CA 模拟。
    /// </summary>
    CaSimulation = 4,

    /// <summary>
    /// 相位 5：温度场。
    /// </summary>
    Temperature = 5,

    /// <summary>
    /// 相位 6：dirty rectangle 交换。
    /// </summary>
    DirtyRectSwap = 6,

    /// <summary>
    /// 相位 7：cell 抛射为自由粒子。
    /// </summary>
    CellToParticle = 7,

    /// <summary>
    /// 相位 8：物理同步。
    /// </summary>
    PhysicsSync = 8,

    /// <summary>
    /// 相位 9：构建渲染缓冲。
    /// </summary>
    BuildRenderBuffer = 9,

    /// <summary>
    /// 相位 10：GPU 上传、渲染与 Editor 绘制。
    /// </summary>
    GpuUploadAndRender = 10,

    /// <summary>
    /// 相位 11：世界后台流式 I/O。
    /// </summary>
    WorldStreaming = 11,
}
