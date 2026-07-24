using PixelEngine.Scripting;

namespace PixelEngine.Hosting;

/// <summary>
/// 非权威世界视觉层的组合位置。视觉层不进入 cell 网格、碰撞、攻击或存档。
/// </summary>
public enum WorldVisualLayerKind : byte
{
    /// <summary>在权威 cell 世界后方绘制。</summary>
    Background,

    /// <summary>在权威 cell 世界上方、光照合成前绘制。</summary>
    Decoration,
}

/// <summary>
/// 一张非权威世界图片的稳定描述；目标矩形使用 world cell 坐标。
/// </summary>
/// <param name="Asset">ContentRoot 内的稳定 Texture 资产引用。</param>
/// <param name="WorldX">目标矩形左上角世界 X，单位 cell。</param>
/// <param name="WorldY">目标矩形左上角世界 Y，单位 cell。</param>
/// <param name="WidthCells">目标宽度，单位 cell。</param>
/// <param name="HeightCells">目标高度，单位 cell。</param>
/// <param name="Layer">视觉组合层。</param>
/// <param name="TintBgra">BGRA8 非预乘 tint；白色不改色。</param>
public readonly record struct WorldVisualLayerDescriptor(
    ScriptAssetReference Asset,
    float WorldX,
    float WorldY,
    float WidthCells,
    float HeightCells,
    WorldVisualLayerKind Layer,
    uint TintBgra = 0xFFFFFFFFu)
{
    /// <summary>校验资产、世界矩形与组合层。</summary>
    /// <returns>当前描述。</returns>
    public WorldVisualLayerDescriptor Validate()
    {
        return !Asset.IsValid || Asset.AssetType != ScriptAssetKind.Texture
            ? throw new ArgumentException("世界视觉层必须引用有效 Texture 资产。", nameof(Asset))
            : !float.IsFinite(WorldX) || !float.IsFinite(WorldY)
            ? throw new ArgumentOutOfRangeException(nameof(WorldX), "世界视觉层坐标必须为有限数。")
            : !float.IsFinite(WidthCells) || !float.IsFinite(HeightCells) ||
            WidthCells <= 0f || HeightCells <= 0f
            ? throw new ArgumentOutOfRangeException(nameof(WidthCells), "世界视觉层尺寸必须为正有限数。")
            : Enum.IsDefined(Layer)
            ? this
            : throw new ArgumentOutOfRangeException(nameof(Layer), Layer, "未知世界视觉组合层。");
    }
}

/// <summary>
/// 程序化世界可选实现的非权威视觉层来源。调用方按索引读取，不分配逐帧集合。
/// </summary>
public interface IWorldVisualLayerProvider
{
    /// <summary>当前世界视觉层数量；必须位于 0..128。</summary>
    int WorldVisualLayerCount { get; }

    /// <summary>
    /// 按稳定顺序读取一层世界视觉描述。
    /// </summary>
    /// <param name="index">范围为 0..WorldVisualLayerCount-1 的索引。</param>
    /// <returns>视觉层描述。</returns>
    WorldVisualLayerDescriptor GetWorldVisualLayer(int index);
}

/// <summary>
/// 为流式世界按当前视口提供确定性动态视觉层；用于由 seed/marker 决定、无法预枚举全世界实例的场景。
/// </summary>
public interface IViewportWorldVisualLayerProvider
{
    /// <summary>
    /// 收集与给定世界视口相交的视觉层。调用方提供固定容量目标，返回值不得超过目标长度。
    /// </summary>
    /// <param name="minimumWorldX">视口最小世界 X。</param>
    /// <param name="minimumWorldY">视口最小世界 Y。</param>
    /// <param name="maximumWorldX">视口最大世界 X。</param>
    /// <param name="maximumWorldY">视口最大世界 Y。</param>
    /// <param name="destination">固定容量输出。</param>
    /// <returns>写入的视觉层数量。</returns>
    int CollectWorldVisualLayers(
        float minimumWorldX,
        float minimumWorldY,
        float maximumWorldX,
        float maximumWorldY,
        Span<WorldVisualLayerDescriptor> destination);
}
