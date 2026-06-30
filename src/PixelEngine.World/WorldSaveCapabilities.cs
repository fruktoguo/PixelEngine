namespace PixelEngine.World;

/// <summary>
/// 描述 v1 世界持久化入口明确支持与不支持的存档能力。
/// </summary>
public static class WorldSaveCapabilities
{
    /// <summary>
    /// v1 支持显式或周期触发的粗粒度世界快照存档。
    /// </summary>
    public const bool SupportsCoarseSnapshotSaves = true;

    /// <summary>
    /// v1 不支持逐帧 rewind；需要回退时只能加载已写入的粗粒度快照。
    /// </summary>
    public const bool SupportsFrameRewind = false;

    /// <summary>
    /// v1 不支持通用 undo 栈；编辑器或 Demo 不应依赖世界层提供撤销历史。
    /// </summary>
    public const bool SupportsUndo = false;
}
