namespace PixelEngine.Simulation;

/// <summary>
/// Simulation 内核消费的材质属性 SoA 只读视图。
/// </summary>
public sealed class MaterialPropsTable
{
    /// <summary>
    /// 仅包含 Empty 材质的空属性表。
    /// </summary>
    public static MaterialPropsTable Empty { get; } = new(
        [CellType.Empty],
        [0],
        [0],
        [0],
        [0],
        [0]);

    /// <summary>
    /// 创建材质属性表。所有数组均按 runtime material id 索引且长度必须一致。
    /// </summary>
    public MaterialPropsTable(
        CellType[] type,
        byte[] density,
        byte[] dispersion,
        int[] reactionStart,
        byte[] reactionCount,
        ushort[] defaultLifetime)
    {
        Hot = MaterialHotTable.FromColumns(type, density, dispersion, reactionStart, reactionCount, defaultLifetime);
    }

    /// <summary>
    /// 从完整材质热表创建 movement 兼容视图。
    /// </summary>
    public MaterialPropsTable(MaterialHotTable hot)
    {
        Hot = hot ?? throw new ArgumentNullException(nameof(hot));
    }

    /// <summary>
    /// material id 的可用数量。
    /// </summary>
    public int Count => Hot.Count;

    /// <summary>
    /// 完整材质热表。
    /// </summary>
    public MaterialHotTable Hot { get; }

    /// <summary>
    /// 材质类型列。
    /// </summary>
    public ReadOnlySpan<CellType> Type => Hot.Type;

    /// <summary>
    /// 材质密度列。
    /// </summary>
    public ReadOnlySpan<byte> Density => Hot.Density;

    /// <summary>
    /// 液体/气体横向扩散列。
    /// </summary>
    public ReadOnlySpan<byte> Dispersion => Hot.Dispersion;

    /// <summary>
    /// 反应表起始索引列。
    /// </summary>
    public ReadOnlySpan<int> ReactionStart => Hot.ReactionStart;

    /// <summary>
    /// 反应数量列。
    /// </summary>
    public ReadOnlySpan<byte> ReactionCount => Hot.ReactionCount;

    /// <summary>
    /// 默认 lifetime 列。
    /// </summary>
    public ReadOnlySpan<ushort> DefaultLifetime => Hot.DefaultLifetime;

    /// <summary>
    /// 返回材质类型。
    /// </summary>
    public CellType TypeOf(ushort materialId)
    {
        return Type[materialId];
    }

    /// <summary>
    /// 返回材质密度。
    /// </summary>
    public byte DensityOf(ushort materialId)
    {
        return Density[materialId];
    }

    /// <summary>
    /// 返回材质扩散距离。
    /// </summary>
    public byte DispersionOf(ushort materialId)
    {
        return Dispersion[materialId];
    }

    /// <summary>
    /// 返回材质反应表起始索引。
    /// </summary>
    public int ReactionStartOf(ushort materialId)
    {
        return ReactionStart[materialId];
    }

    /// <summary>
    /// 返回材质可触发反应数量。
    /// </summary>
    public byte ReactionCountOf(ushort materialId)
    {
        return ReactionCount[materialId];
    }

    /// <summary>
    /// 返回材质默认 lifetime。
    /// </summary>
    public ushort DefaultLifetimeOf(ushort materialId)
    {
        return DefaultLifetime[materialId];
    }
}
