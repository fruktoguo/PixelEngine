namespace PixelEngine.World;

/// <summary>
/// 根据相机与流式配置计算可见区、激活区和 border ring。
/// </summary>
public sealed class ActivationPolicy
{
    /// <summary>
    /// 计算视口覆盖的 chunk 矩形。
    /// </summary>
    public ChunkRect ComputeVisible(WorldCamera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        long minX = camera.FocusX - (camera.ViewportCellsX / 2L);
        long minY = camera.FocusY - (camera.ViewportCellsY / 2L);
        long maxX = minX + camera.ViewportCellsX - 1L;
        long maxY = minY + camera.ViewportCellsY - 1L;
        return new ChunkRect(
            WorldCamera.CellToChunk(minX),
            WorldCamera.CellToChunk(minY),
            WorldCamera.CellToChunk(maxX),
            WorldCamera.CellToChunk(maxY));
    }

    /// <summary>
    /// 计算会参与模拟的激活区。
    /// </summary>
    public ChunkRect ComputeActive(WorldCamera camera, WorldStreamingConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _ = config.Validate();
        return ComputeVisible(camera).Expand(config.ActivationMarginChunks);
    }

    /// <summary>
    /// 计算激活区外扩后的常驻边界矩形。
    /// </summary>
    public ChunkRect ComputeBorder(ChunkRect active, WorldStreamingConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _ = config.Validate();
        return active.Expand(config.BorderRingWidth);
    }
}
