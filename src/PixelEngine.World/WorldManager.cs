using PixelEngine.Serialization;
using PixelEngine.Simulation;

namespace PixelEngine.World;

/// <summary>
/// World 子系统 façade，串联相机、激活区、驻留规划、内存预算与流式装卸。
/// </summary>
public sealed class WorldManager
{
    private readonly ActivationPolicy _activationPolicy;
    private readonly ResidencyPlanner _residencyPlanner;

    /// <summary>
    /// 创建 World 管理器。
    /// </summary>
    public WorldManager(
        WorldCamera camera,
        TemperatureField temperature,
        MaterialTable materials,
        string worldPath,
        ushort fallbackMaterialId,
        WorldStreamingConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(temperature);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldPath);
        _ = materials.GetName(fallbackMaterialId);

        Config = (config ?? new WorldStreamingConfig()).Validate();
        Camera = camera;
        Chunks = new ResidentChunkMap();
        Residency = new ResidencyTable();
        MemoryBudget = new ChunkMemoryBudget(Config.ResidentMemoryCapBytes, Config.EvictionTargetBytes);
        _activationPolicy = new ActivationPolicy();
        _residencyPlanner = new ResidencyPlanner(Config);

        MaterialNameTable currentNames = new(materials.BuildIdNameTable());
        MaterialRemap identityRemap = MaterialRemap.Build(currentNames, materials, fallbackMaterialId);
        Streamer = new WorldStreamer(
            Chunks,
            Residency,
            MemoryBudget,
            temperature,
            new RegionFileStore(worldPath),
            identityRemap);
    }

    /// <summary>
    /// 世界流式配置。
    /// </summary>
    public WorldStreamingConfig Config { get; }

    /// <summary>
    /// 驱动激活区的世界相机。
    /// </summary>
    public WorldCamera Camera { get; }

    /// <summary>
    /// 当前 live chunk 驻留表。
    /// </summary>
    public ResidentChunkMap Chunks { get; }

    /// <summary>
    /// 驻留元数据。
    /// </summary>
    public ResidencyTable Residency { get; }

    /// <summary>
    /// 常驻内存预算。
    /// </summary>
    public ChunkMemoryBudget MemoryBudget { get; }

    /// <summary>
    /// 流式装卸器。
    /// </summary>
    public WorldStreamer Streamer { get; }

    /// <summary>
    /// 更新相机焦点。
    /// </summary>
    public void UpdateCamera(long focusX, long focusY)
    {
        Camera.SetFocus(focusX, focusY);
    }

    /// <summary>
    /// 相位 2：应用后台完成项，计算 active/border，并提交新的装卸计划。
    /// </summary>
    public void ApplyResidency(long frame)
    {
        _ = Streamer.ApplyPrepared(frame);
        ChunkRect active = _activationPolicy.ComputeActive(Camera, Config);
        ChunkRect border = _activationPolicy.ComputeBorder(active, Config);
        ClassifyResidents(active, border, frame);
        ResidencyPlan plan = _residencyPlanner.Plan(active, border, Residency, MemoryBudget);
        Streamer.SubmitPlan(plan);
    }

    /// <summary>
    /// 相位 11：驱动后台流式 I/O 循环。
    /// </summary>
    public void RunStreaming(CancellationToken cancellationToken)
    {
        Streamer.ProcessIo(cancellationToken);
    }

    private void ClassifyResidents(ChunkRect active, ChunkRect border, long frame)
    {
        foreach (Chunk chunk in Chunks.ResidentChunks)
        {
            ChunkResidencyState state = active.Contains(chunk.Coord)
                ? ChunkResidencyState.Active
                : border.Contains(chunk.Coord)
                    ? ChunkResidencyState.Border
                    : ChunkResidencyState.Cached;

            if (state == ChunkResidencyState.Border)
            {
                chunk.ClearDirty();
            }

            Residency.Set(
                chunk.Coord,
                new ChunkResidencyInfo(
                    state,
                    frame,
                    ChunkMemoryBudget.EstimatedResidentChunkBytes,
                    DirtySinceLoad: false));
        }
    }
}
