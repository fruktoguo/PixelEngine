using PixelEngine.Core;
using PixelEngine.Simulation;

namespace PixelEngine.Serialization;

/// <summary>
/// chunk 序列化视图，持有 Simulation SoA 与粗温度子块的 span。
/// </summary>
public ref struct ChunkSnapshot
{
    private const int TemperatureBlockSize = EngineConstants.ChunkSize / EngineConstants.TempFieldDownscale;

    /// <summary>
    /// 单 chunk 温度子块 cell 数，固定为 16x16。
    /// </summary>
    public const int TemperatureCellCount = TemperatureBlockSize * TemperatureBlockSize;

    /// <summary>
    /// 创建 chunk 序列化视图。
    /// </summary>
    public ChunkSnapshot(
        ChunkCoord coord,
        Span<ushort> material,
        Span<byte> flags,
        Span<byte> lifetime,
        Span<Half> temperature)
    {
        if (material.Length != EngineConstants.ChunkArea)
        {
            throw new ArgumentException("Material span 长度必须等于 ChunkArea。", nameof(material));
        }

        if (flags.Length != EngineConstants.ChunkArea)
        {
            throw new ArgumentException("Flags span 长度必须等于 ChunkArea。", nameof(flags));
        }

        if (lifetime.Length != EngineConstants.ChunkArea)
        {
            throw new ArgumentException("Lifetime span 长度必须等于 ChunkArea。", nameof(lifetime));
        }

        if (temperature.Length != TemperatureCellCount)
        {
            throw new ArgumentException("Temperature span 长度必须等于 16x16。", nameof(temperature));
        }

        Coord = coord;
        Material = material;
        Flags = flags;
        Lifetime = lifetime;
        Temperature = temperature;
    }

    /// <summary>
    /// chunk 坐标。
    /// </summary>
    public ChunkCoord Coord { get; }

    /// <summary>
    /// 运行时 material id 视图。
    /// </summary>
    public Span<ushort> Material { get; }

    /// <summary>
    /// cell flags 视图。
    /// </summary>
    public Span<byte> Flags { get; }

    /// <summary>
    /// cell lifetime 视图。
    /// </summary>
    public Span<byte> Lifetime { get; }

    /// <summary>
    /// 1/4 分辨率温度子块视图，落盘固定为 Half。
    /// </summary>
    public Span<Half> Temperature { get; }
}

/// <summary>
/// cell flags 的持久化规则。
/// </summary>
public static class PersistentCellFlags
{
    /// <summary>
    /// 入盘持久位。parity、settled/sleep、freefalling、rigid-owned 都是运行时瞬时状态，不入盘。
    /// </summary>
    public const byte Mask = CellFlags.Burning;

    /// <summary>
    /// 仅保留持久位。
    /// </summary>
    public static byte StripTransient(byte flags)
    {
        return (byte)(flags & Mask);
    }

    /// <summary>
    /// 读档后重置瞬时位，并把 parity 设置为与当前帧不同，确保下帧可被检视。
    /// </summary>
    public static byte ResetTransient(byte persistedFlags, byte currentParityBit)
    {
        byte parity = (byte)((currentParityBit & CellFlags.Parity) == 0 ? CellFlags.Parity : 0);
        return (byte)(StripTransient(persistedFlags) | parity);
    }

    /// <summary>
    /// 批量剥离瞬时位。
    /// </summary>
    public static void StripTransientInPlace(Span<byte> flags)
    {
        for (int i = 0; i < flags.Length; i++)
        {
            flags[i] = StripTransient(flags[i]);
        }
    }

    /// <summary>
    /// 批量重置瞬时位。
    /// </summary>
    public static void ResetTransientInPlace(Span<byte> flags, byte currentParityBit)
    {
        for (int i = 0; i < flags.Length; i++)
        {
            flags[i] = ResetTransient(flags[i], currentParityBit);
        }
    }
}
