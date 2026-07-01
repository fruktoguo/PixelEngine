namespace PixelEngine.Simulation;

/// <summary>
/// 查询 RigidOwned cell 所属刚体 key 的只读接口。
/// </summary>
public interface IRigidCellOwnershipLookup
{
    /// <summary>
    /// 尝试读取指定世界 cell 所属的刚体 key。
    /// </summary>
    bool TryGetBodyAtCell(int worldX, int worldY, out int bodyKey);
}
