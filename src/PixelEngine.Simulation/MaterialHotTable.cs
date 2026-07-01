using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PixelEngine.Simulation;

/// <summary>
/// 材质热路径 SoA 表。内层循环只读这些并列数组，避免把 name、音效等冷字段拉入 cache line。
/// </summary>
public sealed class MaterialHotTable
{
    private readonly CellType[] _type;
    private readonly byte[] _density;
    private readonly byte[] _dispersion;
    private readonly bool[] _liquidStatic;
    private readonly bool[] _liquidSand;
    private readonly byte[] _flammability;
    private readonly ushort[] _autoIgnitionTemp;
    private readonly int[] _fireHp;
    private readonly byte[] _temperatureOfFire;
    private readonly byte[] _generatesSmoke;
    private readonly float[] _meltPoint;
    private readonly ushort[] _meltTarget;
    private readonly float[] _freezePoint;
    private readonly ushort[] _freezeTarget;
    private readonly float[] _boilPoint;
    private readonly ushort[] _boilTarget;
    private readonly byte[] _heatConduct;
    private readonly float[] _heatCapacity;
    private readonly ushort[] _defaultLifetime;
    private readonly byte[] _durability;
    private readonly int[] _textureId;
    private readonly uint[] _baseColorBgra;
    private readonly byte[] _colorNoise;
    private readonly MaterialProperty[] _propertyFlags;
    private readonly int[] _reactionStart;
    private readonly byte[] _reactionCount;

    private MaterialHotTable(
        CellType[] type,
        byte[] density,
        byte[] dispersion,
        bool[] liquidStatic,
        bool[] liquidSand,
        byte[] flammability,
        ushort[] autoIgnitionTemp,
        int[] fireHp,
        byte[] temperatureOfFire,
        byte[] generatesSmoke,
        float[] meltPoint,
        ushort[] meltTarget,
        float[] freezePoint,
        ushort[] freezeTarget,
        float[] boilPoint,
        ushort[] boilTarget,
        byte[] heatConduct,
        float[] heatCapacity,
        ushort[] defaultLifetime,
        byte[] durability,
        int[] textureId,
        uint[] baseColorBgra,
        byte[] colorNoise,
        MaterialProperty[] propertyFlags,
        int[] reactionStart,
        byte[] reactionCount)
    {
        _type = type;
        _density = density;
        _dispersion = dispersion;
        _liquidStatic = liquidStatic;
        _liquidSand = liquidSand;
        _flammability = flammability;
        _autoIgnitionTemp = autoIgnitionTemp;
        _fireHp = fireHp;
        _temperatureOfFire = temperatureOfFire;
        _generatesSmoke = generatesSmoke;
        _meltPoint = meltPoint;
        _meltTarget = meltTarget;
        _freezePoint = freezePoint;
        _freezeTarget = freezeTarget;
        _boilPoint = boilPoint;
        _boilTarget = boilTarget;
        _heatConduct = heatConduct;
        _heatCapacity = heatCapacity;
        _defaultLifetime = defaultLifetime;
        _durability = durability;
        _textureId = textureId;
        _baseColorBgra = baseColorBgra;
        _colorNoise = colorNoise;
        _propertyFlags = propertyFlags;
        _reactionStart = reactionStart;
        _reactionCount = reactionCount;
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
    /// 不流动液体标记列。
    /// </summary>
    public ReadOnlySpan<bool> LiquidStatic => _liquidStatic;

    /// <summary>
    /// 粉末式液体标记列。
    /// </summary>
    public ReadOnlySpan<bool> LiquidSand => _liquidSand;

    /// <summary>
    /// 可燃性列。
    /// </summary>
    public ReadOnlySpan<byte> Flammability => _flammability;

    /// <summary>
    /// 自燃阈值列。
    /// </summary>
    public ReadOnlySpan<ushort> AutoIgnitionTemp => _autoIgnitionTemp;

    /// <summary>
    /// 燃烧耐久列。
    /// </summary>
    public ReadOnlySpan<int> FireHp => _fireHp;

    /// <summary>
    /// 燃烧注热列。
    /// </summary>
    public ReadOnlySpan<byte> TemperatureOfFire => _temperatureOfFire;

    /// <summary>
    /// 产烟倾向列。
    /// </summary>
    public ReadOnlySpan<byte> GeneratesSmoke => _generatesSmoke;

    /// <summary>
    /// 熔化阈值列。
    /// </summary>
    public ReadOnlySpan<float> MeltPoint => _meltPoint;

    /// <summary>
    /// 熔化目标列。
    /// </summary>
    public ReadOnlySpan<ushort> MeltTarget => _meltTarget;

    /// <summary>
    /// 凝固阈值列。
    /// </summary>
    public ReadOnlySpan<float> FreezePoint => _freezePoint;

    /// <summary>
    /// 凝固目标列。
    /// </summary>
    public ReadOnlySpan<ushort> FreezeTarget => _freezeTarget;

    /// <summary>
    /// 沸腾阈值列。
    /// </summary>
    public ReadOnlySpan<float> BoilPoint => _boilPoint;

    /// <summary>
    /// 沸腾目标列。
    /// </summary>
    public ReadOnlySpan<ushort> BoilTarget => _boilTarget;

    /// <summary>
    /// 热传导概率列。
    /// </summary>
    public ReadOnlySpan<byte> HeatConduct => _heatConduct;

    /// <summary>
    /// 热容量列。
    /// </summary>
    public ReadOnlySpan<float> HeatCapacity => _heatCapacity;

    /// <summary>
    /// 默认 lifetime 列。
    /// </summary>
    public ReadOnlySpan<ushort> DefaultLifetime => _defaultLifetime;

    /// <summary>
    /// 耐久列。
    /// </summary>
    public ReadOnlySpan<byte> Durability => _durability;

    /// <summary>
    /// 材质纹理 id 列；-1 表示纯色。
    /// </summary>
    public ReadOnlySpan<int> TextureId => _textureId;

    /// <summary>
    /// BGRA8 基色 palette 列，供 rendering 相位 SIMD 转色。
    /// </summary>
    public ReadOnlySpan<uint> BaseColorBGRA => _baseColorBgra;

    /// <summary>
    /// 颜色噪声幅度列。
    /// </summary>
    public ReadOnlySpan<byte> ColorNoise => _colorNoise;

    /// <summary>
    /// 是否存在需要材质纹理采样的材质。
    /// </summary>
    public bool HasTexturedMaterials { get; private init; }

    /// <summary>
    /// 是否存在需要坐标噪声调色的材质。
    /// </summary>
    public bool HasColorNoise { get; private init; }

    /// <summary>
    /// 材质属性位列。
    /// </summary>
    public ReadOnlySpan<MaterialProperty> PropertyFlags => _propertyFlags;

    /// <summary>
    /// 反应表起始索引列。
    /// </summary>
    public ReadOnlySpan<int> ReactionStart => _reactionStart;

    /// <summary>
    /// 反应数量列。
    /// </summary>
    public ReadOnlySpan<byte> ReactionCount => _reactionCount;

    /// <summary>
    /// 热路径 unchecked 读取材质类型；调用方保证 material id 来自有效网格数据。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal CellType TypeOfUnchecked(ushort materialId)
    {
        return Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_type), materialId);
    }

    /// <summary>
    /// 热路径 unchecked 读取材质密度；调用方保证 material id 来自有效网格数据。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal byte DensityOfUnchecked(ushort materialId)
    {
        return Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_density), materialId);
    }

    /// <summary>
    /// 热路径 unchecked 读取扩散距离；调用方保证 material id 来自有效网格数据。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal byte DispersionOfUnchecked(ushort materialId)
    {
        return Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_dispersion), materialId);
    }

    /// <summary>
    /// 热路径 unchecked 读取反应起始索引；调用方保证 material id 来自有效网格数据。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int ReactionStartOfUnchecked(ushort materialId)
    {
        return Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_reactionStart), materialId);
    }

    /// <summary>
    /// 热路径 unchecked 读取反应数量；调用方保证 material id 来自有效网格数据。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal byte ReactionCountOfUnchecked(ushort materialId)
    {
        return Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_reactionCount), materialId);
    }

    /// <summary>
    /// 热路径 unchecked 读取默认 lifetime；调用方保证 material id 来自有效网格数据。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ushort DefaultLifetimeOfUnchecked(ushort materialId)
    {
        return Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_defaultLifetime), materialId);
    }

    /// <summary>
    /// 热路径 unchecked 读取材质属性位；调用方保证 material id 来自有效网格数据。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal MaterialProperty PropertyFlagsOfUnchecked(ushort materialId)
    {
        return Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_propertyFlags), materialId);
    }

    /// <summary>
    /// 从完整材质定义构建热路径 SoA 表。
    /// </summary>
    public static MaterialHotTable FromDefinitions(ReadOnlySpan<MaterialDef> definitions)
    {
        int count = definitions.Length;
        CellType[] type = new CellType[count];
        byte[] density = new byte[count];
        byte[] dispersion = new byte[count];
        bool[] liquidStatic = new bool[count];
        bool[] liquidSand = new bool[count];
        byte[] flammability = new byte[count];
        ushort[] autoIgnitionTemp = new ushort[count];
        int[] fireHp = new int[count];
        byte[] temperatureOfFire = new byte[count];
        byte[] generatesSmoke = new byte[count];
        float[] meltPoint = new float[count];
        ushort[] meltTarget = new ushort[count];
        float[] freezePoint = new float[count];
        ushort[] freezeTarget = new ushort[count];
        float[] boilPoint = new float[count];
        ushort[] boilTarget = new ushort[count];
        byte[] heatConduct = new byte[count];
        float[] heatCapacity = new float[count];
        ushort[] defaultLifetime = new ushort[count];
        byte[] durability = new byte[count];
        int[] textureId = new int[count];
        uint[] baseColorBgra = new uint[count];
        byte[] colorNoise = new byte[count];
        MaterialProperty[] propertyFlags = new MaterialProperty[count];
        int[] reactionStart = new int[count];
        byte[] reactionCount = new byte[count];
        bool hasTexturedMaterials = false;
        bool hasColorNoise = false;

        for (int i = 0; i < count; i++)
        {
            MaterialDef def = definitions[i];
            type[i] = def.Type;
            density[i] = def.Density;
            dispersion[i] = def.Dispersion;
            liquidStatic[i] = def.LiquidStatic;
            liquidSand[i] = def.LiquidSand;
            flammability[i] = def.Flammability;
            autoIgnitionTemp[i] = def.AutoIgnitionTemp;
            fireHp[i] = def.FireHp;
            temperatureOfFire[i] = def.TemperatureOfFire;
            generatesSmoke[i] = def.GeneratesSmoke;
            meltPoint[i] = def.MeltPoint;
            meltTarget[i] = def.MeltTarget;
            freezePoint[i] = def.FreezePoint;
            freezeTarget[i] = def.FreezeTarget;
            boilPoint[i] = def.BoilPoint;
            boilTarget[i] = def.BoilTarget;
            heatConduct[i] = def.HeatConduct;
            heatCapacity[i] = def.HeatCapacity;
            defaultLifetime[i] = def.DefaultLifetime;
            durability[i] = def.Durability;
            textureId[i] = def.TextureId;
            baseColorBgra[i] = def.BaseColorBGRA;
            colorNoise[i] = def.ColorNoise;
            propertyFlags[i] = def.PropertyFlags;
            reactionStart[i] = def.ReactionStart;
            reactionCount[i] = def.ReactionCount;
            hasTexturedMaterials |= def.TextureId >= 0;
            hasColorNoise |= def.ColorNoise != 0;
        }

        return new MaterialHotTable(
            type,
            density,
            dispersion,
            liquidStatic,
            liquidSand,
            flammability,
            autoIgnitionTemp,
            fireHp,
            temperatureOfFire,
            generatesSmoke,
            meltPoint,
            meltTarget,
            freezePoint,
            freezeTarget,
            boilPoint,
            boilTarget,
            heatConduct,
            heatCapacity,
            defaultLifetime,
            durability,
            textureId,
            baseColorBgra,
            colorNoise,
            propertyFlags,
            reactionStart,
            reactionCount)
        {
            HasTexturedMaterials = hasTexturedMaterials,
            HasColorNoise = hasColorNoise,
        };
    }

    /// <summary>
    /// 从旧 movement 所需列构建热表。缺省热学字段使用惰性安全值，供已有 plan/03/05 测试夹具兼容。
    /// </summary>
    public static MaterialHotTable FromColumns(
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
        ValidateColumnLengths(type.Length, density, dispersion, reactionStart, reactionCount, defaultLifetime);

        int count = type.Length;
        float[] noPhase = new float[count];
        Array.Fill(noPhase, float.NaN);
        float[] heatCapacity = new float[count];
        Array.Fill(heatCapacity, 1f);

        return new MaterialHotTable(
            type,
            density,
            dispersion,
            new bool[count],
            new bool[count],
            new byte[count],
            new ushort[count],
            new int[count],
            new byte[count],
            new byte[count],
            (float[])noPhase.Clone(),
            new ushort[count],
            (float[])noPhase.Clone(),
            new ushort[count],
            (float[])noPhase.Clone(),
            new ushort[count],
            new byte[count],
            heatCapacity,
            defaultLifetime,
            new byte[count],
            CreateFilled(count, -1),
            new uint[count],
            new byte[count],
            new MaterialProperty[count],
            reactionStart,
            reactionCount);
    }

    private static int[] CreateFilled(int count, int value)
    {
        int[] result = new int[count];
        Array.Fill(result, value);
        return result;
    }

    private static void ValidateColumnLengths(
        int length,
        byte[] density,
        byte[] dispersion,
        int[] reactionStart,
        byte[] reactionCount,
        ushort[] defaultLifetime)
    {
        if (density.Length != length ||
            dispersion.Length != length ||
            reactionStart.Length != length ||
            reactionCount.Length != length ||
            defaultLifetime.Length != length)
        {
            throw new ArgumentException("所有材质属性列长度必须一致。");
        }
    }
}
