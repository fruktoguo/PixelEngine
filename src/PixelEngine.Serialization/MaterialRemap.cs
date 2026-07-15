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
        return BuildCore(
            saved,
            new MaterialNameTable(current.BuildIdNameTable()),
            fallbackId);
    }

    /// <summary>
    /// 仅用不可变 name 表构建 saved id 到目标 runtime id 的重映射表，供后台读档使用。
    /// </summary>
    /// <param name="saved">存档内 id 到稳定 name 的映射。</param>
    /// <param name="current">safe phase 冻结的目标 runtime id 到 name 映射。</param>
    /// <param name="fallbackId">目标映射缺失时使用的 material id。</param>
    /// <returns>可在后台线程消费的重映射表。</returns>
    public static MaterialRemap Build(
        MaterialNameTable saved,
        MaterialNameTable current,
        ushort fallbackId)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(current);
        return BuildCore(saved, current, fallbackId);
    }

    private static MaterialRemap BuildCore(
        MaterialNameTable saved,
        MaterialNameTable current,
        ushort fallbackId)
    {
        Dictionary<string, ushort> currentByName = new(StringComparer.Ordinal);
        bool fallbackExists = false;
        foreach ((ushort id, string name) in current.Entries)
        {
            currentByName.Add(name, id);
            fallbackExists |= id == fallbackId;
        }

        if (!fallbackExists)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fallbackId),
                fallbackId,
                "fallback material id 不在目标 material name 表中。");
        }

        int length = Math.Max(saved.MaxSavedId + 1, 0);
        ushort[] lut = new ushort[length];
        bool[] fallbackBecauseMissing = new bool[length];
        Array.Fill(lut, fallbackId);
        Array.Fill(fallbackBecauseMissing, true);
        foreach ((ushort savedId, string name) in saved.Entries)
        {
            bool found = currentByName.TryGetValue(name, out ushort currentId);
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
    /// 原地重映射 material id，并在 saved material 缺失或损坏落到 fallback 时清空对应 Damage。
    /// </summary>
    public void RemapInPlace(Span<ushort> material, Span<byte> damage)
    {
        if (material.Length != damage.Length)
        {
            throw new ArgumentException("material 与 damage span 长度必须一致。", nameof(damage));
        }

        for (int i = 0; i < material.Length; i++)
        {
            ushort savedId = material[i];
            if (savedId >= _lut.Length)
            {
                FallbackHitCount++;
                material[i] = _fallbackId;
                damage[i] = 0;
                continue;
            }

            if (_fallbackBecauseMissing[savedId])
            {
                FallbackHitCount++;
                damage[i] = 0;
            }

            material[i] = _lut[savedId];
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
