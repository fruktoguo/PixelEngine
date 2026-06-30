namespace PixelEngine.Core.Diagnostics;

/// <summary>
/// 表示架构 §3.3 定义的帧主相位。
/// </summary>
public enum FramePhase
{
    /// <summary>
    /// 相位 0：帧时钟与预算监测。
    /// </summary>
    FrameClock,

    /// <summary>
    /// 相位 1：玩法脚本输入与命令采集。
    /// </summary>
    Gameplay,

    /// <summary>
    /// 相位 2：世界流式装卸。
    /// </summary>
    Streaming,

    /// <summary>
    /// 相位 3：刚体像素擦除。
    /// </summary>
    RigidBodyErase,

    /// <summary>
    /// 相位 4：CA checkerboard 模拟。
    /// </summary>
    CellularAutomata,

    /// <summary>
    /// 相位 5：温度、火焰与反应。
    /// </summary>
    TemperatureReaction,

    /// <summary>
    /// 相位 6：自由粒子积分。
    /// </summary>
    Particles,

    /// <summary>
    /// 相位 7：刚体形状重建。
    /// </summary>
    RigidBodyRebuild,

    /// <summary>
    /// 相位 8：物理步进。
    /// </summary>
    Physics,

    /// <summary>
    /// 相位 9：刚体像素回写。
    /// </summary>
    RigidBodyStamp,

    /// <summary>
    /// 相位 10：渲染缓冲构建。
    /// </summary>
    RenderBuild,

    /// <summary>
    /// 相位 11：GPU 提交与音频消费。
    /// </summary>
    PresentAudio,
}
