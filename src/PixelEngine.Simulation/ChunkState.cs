namespace PixelEngine.Simulation;

/// <summary>
/// chunk 在 CA 调度中的运行状态。
/// </summary>
public enum ChunkState : byte
{
    /// <summary>
    /// chunk 有待处理 dirty 区域，需要进入 CA 调度。
    /// </summary>
    Awake = 0,

    /// <summary>
    /// chunk 当前无 dirty 区域，可跳过 CA 调度。
    /// </summary>
    Sleeping = 1,
}
