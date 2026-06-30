using PixelEngine.Core.Diagnostics;
using PixelEngine.Simulation;

namespace PixelEngine.Serialization;

/// <summary>
/// saved material id 到当前 runtime id 的重映射表。
/// </summary>
public sealed class MaterialRemap
{
    private readonly ushort[] _lut;
    private readonly bool[] _fallbackBecauseMissing;
    private readonly ushort _fallbackId;

    private MaterialRemap(ushort[] lut, bool[] fallbackBecauseMissing, ushort fallbackId)
    {
        _lut = lut;
        _fallbackBecauseMissing = fallbackBecauseMissing;
        _fallbackId = fallbackId;
    }

    /// <summary>
    /// fallback 命中次数，包含缺失 name 与超出 LUT 的损坏 id。
    /// </summary>
    public long FallbackHitCount { get; private set; }

    /// <summary>
    /// 构建 saved id 到当前 runtime id 的重映射表。
    /// </summary>
    public static MaterialRemap Build(MaterialNameTable saved, MaterialTable current, ushort fallbackId)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(current);

        _ = current.GetName(fallbackId);
        int length = Math.Max(saved.MaxSavedId + 1, 0);
        ushort[] lut = new ushort[length];
        bool[] fallbackBecauseMissing = new bool[length];
        Array.Fill(lut, fallbackId);
        Array.Fill(fallbackBecauseMissing, true);
        foreach ((ushort savedId, string name) in saved.Entries)
        {
            bool found = current.TryGetId(name, out ushort currentId);
            lut[savedId] = found ? currentId : fallbackId;
            fallbackBecauseMissing[savedId] = !found;
        }

        return new MaterialRemap(lut, fallbackBecauseMissing, fallbackId);
    }

    /// <summary>
    /// 映射单个 saved material id。
    /// </summary>
    public ushort Map(ushort savedId)
    {
        if (savedId >= _lut.Length)
        {
            FallbackHitCount++;
            return _fallbackId;
        }

        if (_fallbackBecauseMissing[savedId])
        {
            FallbackHitCount++;
        }

        return _lut[savedId];
    }

    /// <summary>
    /// 原地重映射 material id span。
    /// </summary>
    public void RemapInPlace(Span<ushort> material)
    {
        for (int i = 0; i < material.Length; i++)
        {
            material[i] = Map(material[i]);
        }
    }

    /// <summary>
    /// 将 material 重映射 fallback 命中次数发布到 Core 计数器。
    /// </summary>
    public void PublishDiagnostics(EngineCounters counters)
    {
        ArgumentNullException.ThrowIfNull(counters);
        counters.MaterialRemapFallbackHits = FallbackHitCount;
    }
}
