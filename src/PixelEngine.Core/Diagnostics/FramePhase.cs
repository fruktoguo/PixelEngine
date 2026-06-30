namespace PixelEngine.Core.Diagnostics;

/// <summary>
/// 表示架构 §3.3 定义的帧主相位。
/// </summary>
public enum FramePhase
{
    /// <summary>
    /// 相位 0：输入、帧时钟与预算监测。
    /// </summary>
    InputAndTime,

    /// <summary>
    /// 相位 1：玩法脚本与命令执行。
    /// </summary>
    GameLogic,

    /// <summary>
    /// 相位 2：chunk 驻留变更应用。
    /// </summary>
    ResidencyApply,

    /// <summary>
    /// 相位 3：自由粒子落定写回 cell。
    /// </summary>
    ParticleToCell,

    /// <summary>
    /// 相位 4：CA checkerboard 模拟。
    /// </summary>
    CaSimulation,

    /// <summary>
    /// 相位 5：温度、火焰与材质反应。
    /// </summary>
    Temperature,

    /// <summary>
    /// 相位 6：dirty rectangle swap。
    /// </summary>
    DirtyRectSwap,

    /// <summary>
    /// 相位 7：cell 抛射为自由粒子。
    /// </summary>
    CellToParticle,

    /// <summary>
    /// 相位 8：物理同步、刚体 erase/step/stamp。
    /// </summary>
    PhysicsSync,

    /// <summary>
    /// 相位 9：构建渲染缓冲。
    /// </summary>
    BuildRenderBuffer,

    /// <summary>
    /// 相位 10：GPU 上传与渲染提交。
    /// </summary>
    GpuUploadRender,

    /// <summary>
    /// 相位 11：世界流式后台与音频派发。
    /// </summary>
    WorldStreaming,
}
