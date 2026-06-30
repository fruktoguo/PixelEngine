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
    void OnOwnedCellDamaged(int wx, int wy);

    private sealed class NullRigidDamageSink : IRigidDamageSink
    {
        public void OnOwnedCellDamaged(int wx, int wy)
        {
        }
    }
}
