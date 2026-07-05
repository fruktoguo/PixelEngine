namespace PixelEngine.Simulation;

/// <summary>
/// 单个 cell 被结构破坏动作销毁后的副作用描述。
/// </summary>
/// <param name="WorldX">世界 X 坐标。</param>
/// <param name="WorldY">世界 Y 坐标。</param>
/// <param name="SourceMaterial">被破坏前的材质 id。</param>
/// <param name="TargetMaterial">破坏后写入的材质 id；0 表示 Empty。</param>
/// <param name="DebrisMaterial">碎屑粒子材质；优先 TargetMaterial，Empty 时回退 SourceMaterial。</param>
/// <param name="DebrisCount">请求抛射的碎屑数量；0 表示不抛。</param>
/// <param name="MineYield">可采集产量；非 Diggable 或非矿物为 0。</param>
public readonly record struct CellDestructionEvent(
    int WorldX,
    int WorldY,
    ushort SourceMaterial,
    ushort TargetMaterial,
    ushort DebrisMaterial,
    byte DebrisCount,
    byte MineYield);
