namespace PixelEngine.Simulation;

/// <summary>
/// 世界检视器使用的单 cell 只读快照。
/// </summary>
public readonly record struct SimulationCellInspection
{
    /// <summary>
    /// 创建单 cell 检视快照。
    /// </summary>
    public SimulationCellInspection(
        int worldX,
        int worldY,
        ChunkCoord chunkCoord,
        int localX,
        int localY,
        ushort materialId,
        string materialName,
        float temperatureCelsius,
        bool temperatureAvailable,
        SimulationCellFlags flags,
        int? bodyId,
        DirtyRect currentDirty,
        DirtyRect workingDirty,
        ChunkState chunkState,
        byte chunkParity)
    {
        WorldX = worldX;
        WorldY = worldY;
        ChunkCoord = chunkCoord;
        LocalX = localX;
        LocalY = localY;
        MaterialId = materialId;
        MaterialName = materialName;
        TemperatureCelsius = temperatureCelsius;
        TemperatureAvailable = temperatureAvailable;
        Flags = flags;
        BodyId = bodyId;
        CurrentDirty = currentDirty;
        WorkingDirty = workingDirty;
        ChunkState = chunkState;
        ChunkParity = chunkParity;
    }

    /// <summary>
    /// 世界 X。
    /// </summary>
    public int WorldX { get; }

    /// <summary>
    /// 世界 Y。
    /// </summary>
    public int WorldY { get; }

    /// <summary>
    /// chunk 坐标。
    /// </summary>
    public ChunkCoord ChunkCoord { get; }

    /// <summary>
    /// chunk 本地 X。
    /// </summary>
    public int LocalX { get; }

    /// <summary>
    /// chunk 本地 Y。
    /// </summary>
    public int LocalY { get; }

    /// <summary>
    /// runtime 材质 id。
    /// </summary>
    public ushort MaterialId { get; }

    /// <summary>
    /// 材质稳定 name。
    /// </summary>
    public string MaterialName { get; }

    /// <summary>
    /// 温度，单位摄氏度。
    /// </summary>
    public float TemperatureCelsius { get; }

    /// <summary>
    /// 是否接入了粗温度场。
    /// </summary>
    public bool TemperatureAvailable { get; }

    /// <summary>
    /// cell flag 解码。
    /// </summary>
    public SimulationCellFlags Flags { get; }

    /// <summary>
    /// owning rigid body id；当前物理层只暴露 RigidOwned 位，尚无 cell 到 body id 的映射时为 null。
    /// </summary>
    public int? BodyId { get; }

    /// <summary>
    /// 本帧 CA 读取的 dirty rectangle。
    /// </summary>
    public DirtyRect CurrentDirty { get; }

    /// <summary>
    /// 累积给下一帧的 dirty rectangle。
    /// </summary>
    public DirtyRect WorkingDirty { get; }

    /// <summary>
    /// chunk 调度状态。
    /// </summary>
    public ChunkState ChunkState { get; }

    /// <summary>
    /// chunk 最近一次处理 parity。
    /// </summary>
    public byte ChunkParity { get; }
}
