namespace PixelEngine.Simulation;

/// <summary>
/// CA movement 使用的基础 cell 类型。具体材质字段语义由 Content/Material 层定义。
/// </summary>
public enum CellType : byte
{
    /// <summary>
    /// 空 cell。
    /// </summary>
    Empty = 0,

    /// <summary>
    /// 静态固体。
    /// </summary>
    Solid = 1,

    /// <summary>
    /// 粉末类材质。
    /// </summary>
    Powder = 2,

    /// <summary>
    /// 液体类材质。
    /// </summary>
    Liquid = 3,

    /// <summary>
    /// 气体类材质。
    /// </summary>
    Gas = 4,

    /// <summary>
    /// 火焰或能量类瞬态材质。
    /// </summary>
    Fire = 5,
}
