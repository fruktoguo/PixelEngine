using System.Buffers;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Simulation;
using Xunit;

namespace PixelEngine.Serialization.Tests;

/// <summary>
/// material name↔id 表与重映射测试。
/// </summary>
public sealed class MaterialRemapTests
{
    /// <summary>
    /// 验证 MaterialNameTable 二进制读写往返并按 id 排序。
    /// </summary>
    [Fact]
    public void MaterialNameTableRoundTripsAndSortsBySavedId()
    {
        MaterialNameTable table = new([(2, "sand"), (0, "empty"), (1, "water")]);
        ArrayBufferWriter<byte> writer = new();

        table.Write(writer);
        MaterialNameTable decoded = MaterialNameTable.Read(writer.WrittenSpan, out int consumed);

        Assert.Equal(writer.WrittenCount, consumed);
        Assert.Equal([(0, "empty"), (1, "water"), (2, "sand")], decoded.Entries.ToArray());
    }

    /// <summary>
    /// 验证 name 表拒绝重复 id 与重复 name。
    /// </summary>
    [Fact]
    public void MaterialNameTableRejectsDuplicateIdsAndNames()
    {
        ArgumentException duplicateId = Assert.Throws<ArgumentException>(() => new MaterialNameTable([(1, "a"), (1, "b")]));
        ArgumentException duplicateName = Assert.Throws<ArgumentException>(() => new MaterialNameTable([(1, "a"), (2, "a")]));

        Assert.Contains("id", duplicateId.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name", duplicateName.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 验证 remap 能处理顺序变化、缺失材质、空洞 saved id 与损坏的超范围 id。
    /// </summary>
    [Fact]
    public void MaterialRemapMapsByNameAndCountsOnlyMissingFallbacks()
    {
        MaterialTable current = new(
        [
            Material(0, "empty"),
            Material(1, "sand"),
            Material(2, "water"),
        ]);
        MaterialNameTable saved = new(
        [
            (0, "empty"),
            (1, "water"),
            (2, "missing"),
            (4, "sand"),
        ]);
        MaterialRemap remap = MaterialRemap.Build(saved, current, fallbackId: 0);
        ushort[] material = [0, 1, 2, 3, 4, 99];

        remap.RemapInPlace(material);

        Assert.Equal([0, 2, 0, 0, 1, 0], material);
        Assert.Equal(3, remap.FallbackHitCount);
    }

    /// <summary>
    /// 验证 remap fallback 命中能发布到 Core 诊断计数器。
    /// </summary>
    [Fact]
    public void MaterialRemapPublishesFallbackDiagnostics()
    {
        MaterialTable current = new([Material(0, "empty")]);
        MaterialNameTable saved = new([(0, "empty"), (1, "missing")]);
        MaterialRemap remap = MaterialRemap.Build(saved, current, fallbackId: 0);
        EngineCounters counters = new();

        _ = remap.Map(1);
        remap.PublishDiagnostics(counters);

        Assert.Equal(1, counters.MaterialRemapFallbackHits);
    }

    /// <summary>
    /// 验证 fallback id 必须是当前材质表中的 live id。
    /// </summary>
    [Fact]
    public void MaterialRemapRejectsInvalidFallbackId()
    {
        MaterialTable current = new([Material(0, "empty")]);
        MaterialNameTable saved = new([(0, "empty")]);

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MaterialRemap.Build(saved, current, fallbackId: 7));

        Assert.Equal("id", exception.ParamName);
    }

    private static MaterialDef Material(ushort id, string name)
    {
        return new MaterialDef
        {
            Id = id,
            Name = name,
            Type = id == 0 ? CellType.Empty : CellType.Solid,
            HeatCapacity = 1,
            TextureId = -1,
            MeltPoint = float.NaN,
            FreezePoint = float.NaN,
            BoilPoint = float.NaN,
        };
    }
}
