namespace PixelEngine.Simulation;

/// <summary>
/// CA 覆盖或消耗 RigidOwned cell 时通知 physics 层的钩子。
/// </summary>
public interface IRigidDamageSink
{
    /// <summary>
    /// 空实现，表示覆盖 RigidOwned cell 时退化为普通 CA 行为。
    /// </summary>
    static IRigidDamageSink Null { get; } = new NullRigidDamageSink();

    /// <summary>
    /// 通知一个刚体占用 cell 被 CA 覆盖或消耗。
    /// </summary>
    /// <param name="wx">世界 X 坐标。</param>
    /// <param name="wy">世界 Y 坐标。</param>
    void OnOwnedCellDamaged(int wx, int wy);

    /// <summary>
    /// 记录一个 RigidOwned cell 被消费 / 覆盖时的材质来源。
    /// </summary>
    /// <param name="wx">世界 X 坐标。</param>
    /// <param name="wy">世界 Y 坐标。</param>
    /// <param name="consumedMaterial">被消费 / 覆盖前的材质 id；未知时为 0。</param>
    void OnOwnedCellDamaged(int wx, int wy, ushort consumedMaterial)
    {
        OnOwnedCellDamaged(wx, wy);
    }

    private sealed class NullRigidDamageSink : IRigidDamageSink
    {
        public void OnOwnedCellDamaged(int wx, int wy)
        {
        }
    }
}
