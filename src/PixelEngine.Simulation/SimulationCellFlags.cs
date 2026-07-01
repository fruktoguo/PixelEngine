namespace PixelEngine.Simulation;

/// <summary>
/// 单 cell flag 位解码结果。
/// </summary>
/// <param name="Raw">原始 flag 字节。</param>
/// <param name="Parity">当前 cell parity 位。</param>
/// <param name="Settled">settled/sleep 标记。</param>
/// <param name="Burning">燃烧标记。</param>
/// <param name="FreeFalling">自由下落标记。</param>
/// <param name="RigidOwned">是否由刚体像素占用。</param>
public readonly record struct SimulationCellFlags(
    byte Raw,
    bool Parity,
    bool Settled,
    bool Burning,
    bool FreeFalling,
    bool RigidOwned)
{
    /// <summary>
    /// 从原始 flag 字节解码。
    /// </summary>
    public static SimulationCellFlags FromRaw(byte flags)
    {
        return new SimulationCellFlags(
            flags,
            CellFlags.Has(flags, CellFlags.Parity),
            CellFlags.Has(flags, CellFlags.Settled),
            CellFlags.Has(flags, CellFlags.Burning),
            CellFlags.Has(flags, CellFlags.FreeFalling),
            CellFlags.Has(flags, CellFlags.RigidOwned));
    }
}
