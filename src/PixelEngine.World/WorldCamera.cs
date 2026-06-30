using PixelEngine.Core;
using PixelEngine.Simulation;

namespace PixelEngine.World;

/// <summary>
/// 驱动世界激活区计算的世界空间相机状态。
/// </summary>
public sealed class WorldCamera
{
    /// <summary>
    /// 创建世界相机。
    /// </summary>
    public WorldCamera(long focusX, long focusY, int viewportCellsX, int viewportCellsY)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewportCellsX);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewportCellsY);
        FocusX = focusX;
        FocusY = focusY;
        ViewportCellsX = viewportCellsX;
        ViewportCellsY = viewportCellsY;
    }

    /// <summary>
    /// 焦点世界 X 坐标，单位 cell。
    /// </summary>
    public long FocusX { get; private set; }

    /// <summary>
    /// 焦点世界 Y 坐标，单位 cell。
    /// </summary>
    public long FocusY { get; private set; }

    /// <summary>
    /// 视口宽度，单位 cell。
    /// </summary>
    public int ViewportCellsX { get; private set; }

    /// <summary>
    /// 视口高度，单位 cell。
    /// </summary>
    public int ViewportCellsY { get; private set; }

    /// <summary>
    /// 焦点所在 chunk。
    /// </summary>
    public ChunkCoord FocusChunk => new(CellToChunk(FocusX), CellToChunk(FocusY));

    /// <summary>
    /// 更新焦点坐标。
    /// </summary>
    public void SetFocus(long focusX, long focusY)
    {
        FocusX = focusX;
        FocusY = focusY;
    }

    /// <summary>
    /// 更新视口尺寸。
    /// </summary>
    public void SetViewport(int viewportCellsX, int viewportCellsY)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewportCellsX);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewportCellsY);
        ViewportCellsX = viewportCellsX;
        ViewportCellsY = viewportCellsY;
    }

    internal static int CellToChunk(long cell)
    {
        long chunk = cell >> EngineConstants.ChunkSizeLog2;
        return chunk is < int.MinValue or > int.MaxValue
            ? throw new OverflowException("世界坐标超出当前 ChunkCoord int 范围。")
            : (int)chunk;
    }
}
