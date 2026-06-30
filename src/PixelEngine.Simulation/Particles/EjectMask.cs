namespace PixelEngine.Simulation.Particles;

/// <summary>
/// cell→particle 抛射可影响的 cell 类型掩码。
/// </summary>
[Flags]
public enum EjectMask : byte
{
    /// <summary>
    /// 不抛射任何 cell。
    /// </summary>
    None = 0,

    /// <summary>
    /// 抛射粉末。
    /// </summary>
    Powder = 1 << 0,

    /// <summary>
    /// 抛射液体。
    /// </summary>
    Liquid = 1 << 1,

    /// <summary>
    /// 抛射气体。
    /// </summary>
    Gas = 1 << 2,

    /// <summary>
    /// 抛射火焰/能量 cell。
    /// </summary>
    Fire = 1 << 3,

    /// <summary>
    /// 抛射固体。
    /// </summary>
    Solid = 1 << 4,
}
