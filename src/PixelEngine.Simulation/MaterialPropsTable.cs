namespace PixelEngine.Simulation;

/// <summary>
/// Simulation 内核消费的材质属性 SoA 只读视图。
/// </summary>
public sealed class MaterialPropsTable
{
    private readonly CellType[] _type;
    private readonly byte[] _density;
    private readonly byte[] _dispersion;
    private readonly int[] _reactionStart;
    private readonly byte[] _reactionCount;
    private readonly ushort[] _defaultLifetime;

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
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(density);
        ArgumentNullException.ThrowIfNull(dispersion);
        ArgumentNullException.ThrowIfNull(reactionStart);
        ArgumentNullException.ThrowIfNull(reactionCount);
        ArgumentNullException.ThrowIfNull(defaultLifetime);

        int length = type.Length;
        if (density.Length != length ||
            dispersion.Length != length ||
            reactionStart.Length != length ||
            reactionCount.Length != length ||
            defaultLifetime.Length != length)
        {
            throw new ArgumentException("所有材质属性列长度必须一致。");
        }

        _type = type;
        _density = density;
        _dispersion = dispersion;
        _reactionStart = reactionStart;
        _reactionCount = reactionCount;
        _defaultLifetime = defaultLifetime;
    }

    /// <summary>
    /// material id 的可用数量。
    /// </summary>
    public int Count => _type.Length;

    /// <summary>
    /// 材质类型列。
    /// </summary>
    public ReadOnlySpan<CellType> Type => _type;

    /// <summary>
    /// 材质密度列。
    /// </summary>
    public ReadOnlySpan<byte> Density => _density;

    /// <summary>
    /// 液体/气体横向扩散列。
    /// </summary>
    public ReadOnlySpan<byte> Dispersion => _dispersion;

    /// <summary>
    /// 反应表起始索引列。
    /// </summary>
    public ReadOnlySpan<int> ReactionStart => _reactionStart;

    /// <summary>
    /// 反应数量列。
    /// </summary>
    public ReadOnlySpan<byte> ReactionCount => _reactionCount;

    /// <summary>
    /// 默认 lifetime 列。
    /// </summary>
    public ReadOnlySpan<ushort> DefaultLifetime => _defaultLifetime;

    /// <summary>
    /// 返回材质类型。
    /// </summary>
    public CellType TypeOf(ushort materialId)
    {
        return Type[materialId];
    }
}
