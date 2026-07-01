namespace PixelEngine.Simulation;

/// <summary>
/// 编辑器读取世界 cell 与 chunk 元数据的只读门面。
/// </summary>
public interface ISimulationInspectApi
{
    /// <summary>
    /// 尝试读取指定世界坐标处的 cell 与 chunk 快照。
    /// </summary>
    bool TryInspectCell(int worldX, int worldY, out SimulationCellInspection inspection);
}
