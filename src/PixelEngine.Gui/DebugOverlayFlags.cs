namespace PixelEngine.Gui;

/// <summary>
/// Editor 调试叠层开关集合。
/// </summary>
[Flags]
public enum DebugOverlayFlags : ushort
{
    /// <summary>
    /// 无叠层。
    /// </summary>
    None = 0,

    /// <summary>
    /// dirty rectangle 边框。
    /// </summary>
    DirtyRects = 1 << 0,

    /// <summary>
    /// chunk 网格与 4-pass parity 着色边框。
    /// </summary>
    ChunkGridParity = 1 << 1,

    /// <summary>
    /// KeepAlive 唤醒热点。
    /// </summary>
    KeepAliveHotspots = 1 << 2,

    /// <summary>
    /// cell parity 位着色。
    /// </summary>
    CellParity = 1 << 3,

    /// <summary>
    /// 温度热力图。
    /// </summary>
    TemperatureHeatmap = 1 << 4,

    /// <summary>
    /// owned-by-body-K 着色。
    /// </summary>
    OwnedByBody = 1 << 5,

    /// <summary>
    /// 自由粒子轨迹。
    /// </summary>
    ParticleTrails = 1 << 6,

    /// <summary>
    /// CCL 连通块着色区域。
    /// </summary>
    ConnectedComponents = 1 << 7,

    /// <summary>
    /// 本帧 CA 实际迭代的 dirty rectangle。
    /// </summary>
    CaIterationRects = 1 << 8,
}
