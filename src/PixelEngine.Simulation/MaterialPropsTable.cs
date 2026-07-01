using System.Runtime.CompilerServices;

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
    public MaterialHotTable Hot { get; private set; }

    /// <summary>
    /// 刷新完整材质热表，供材质热重载后让持有同一 props 视图的系统立即看到新列。
    /// </summary>
    public void Reload(MaterialHotTable hot)
    {
        Hot = hot ?? throw new ArgumentNullException(nameof(hot));
    }

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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CellType TypeOf(ushort materialId)
    {
        return Hot.TypeOfUnchecked(materialId);
    }

    /// <summary>
    /// 返回材质密度。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte DensityOf(ushort materialId)
    {
        return Hot.DensityOfUnchecked(materialId);
    }

    /// <summary>
    /// 返回材质扩散距离。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte DispersionOf(ushort materialId)
    {
        return Hot.DispersionOfUnchecked(materialId);
    }

    /// <summary>
    /// 返回材质反应表起始索引。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReactionStartOf(ushort materialId)
    {
        return Hot.ReactionStartOfUnchecked(materialId);
    }

    /// <summary>
    /// 返回材质可触发反应数量。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReactionCountOf(ushort materialId)
    {
        return Hot.ReactionCountOfUnchecked(materialId);
    }

    /// <summary>
    /// 返回材质默认 lifetime。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort DefaultLifetimeOf(ushort materialId)
    {
        return Hot.DefaultLifetimeOfUnchecked(materialId);
    }

    /// <summary>
    /// 返回材质属性位。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MaterialProperty PropertyFlagsOf(ushort materialId)
    {
        return Hot.PropertyFlagsOfUnchecked(materialId);
    }
}
