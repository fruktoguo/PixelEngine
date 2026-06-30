using PixelEngine.Simulation;

namespace PixelEngine.World;

/// <summary>
/// WorldSaveService 读档时需要写入的 World 运行态。
/// </summary>
public sealed class WorldLoadContext
{
    /// <summary>
    /// 创建读档上下文。
    /// </summary>
    public WorldLoadContext(
        ResidentChunkMap chunks,
        ResidencyTable residency,
        TemperatureField temperature,
        MaterialTable materials,
        ushort fallbackMaterialId,
        byte currentParityBit)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(residency);
        ArgumentNullException.ThrowIfNull(temperature);
        ArgumentNullException.ThrowIfNull(materials);
        _ = materials.GetName(fallbackMaterialId);

        Chunks = chunks;
        Residency = residency;
        Temperature = temperature;
        Materials = materials;
        FallbackMaterialId = fallbackMaterialId;
        CurrentParityBit = currentParityBit;
    }

    /// <summary>
    /// live chunk map，只能在相位 2 / 暂停点结构性修改。
    /// </summary>
    public ResidentChunkMap Chunks { get; }

    /// <summary>
    /// World 侧驻留元数据。
    /// </summary>
    public ResidencyTable Residency { get; }

    /// <summary>
    /// 温度场。
    /// </summary>
    public TemperatureField Temperature { get; }

    /// <summary>
    /// 当前运行时材质表。
    /// </summary>
    public MaterialTable Materials { get; }

    /// <summary>
    /// 缺失材质 fallback id。
    /// </summary>
    public ushort FallbackMaterialId { get; }

    /// <summary>
    /// 当前帧 parity bit，用于 ChunkCodec 读档重置瞬时位。
    /// </summary>
    public byte CurrentParityBit { get; }
}
