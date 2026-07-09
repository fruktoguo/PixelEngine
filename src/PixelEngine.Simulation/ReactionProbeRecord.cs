namespace PixelEngine.Simulation;

/// <summary>
/// 单次成功化学反应的内部探测记录，用于诊断叠层。
/// </summary>
/// <param name="X1">反应参与格 1 的世界 X。</param>
/// <param name="Y1">反应参与格 1 的世界 Y。</param>
/// <param name="MaterialA">格 1 的材质 ID。</param>
/// <param name="X2">反应参与格 2 的世界 X。</param>
/// <param name="Y2">反应参与格 2 的世界 Y。</param>
/// <param name="MaterialB">格 2 的材质 ID。</param>
/// <param name="CrossesChunkBoundary">两格是否分属不同 chunk。</param>
internal readonly record struct ReactionProbeRecord(
    int X1,
    int Y1,
    ushort MaterialA,
    int X2,
    int Y2,
    ushort MaterialB,
    bool CrossesChunkBoundary);
