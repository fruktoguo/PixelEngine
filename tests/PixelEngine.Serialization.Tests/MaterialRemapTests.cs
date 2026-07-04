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
    /// 验证 materials.json 仅重排时，旧档 saved id 仍按 name 映射到当前 runtime id。
    /// </summary>
    [Fact]
    public void MaterialRemapPreservesSemanticsWhenMaterialsJsonIsReordered()
    {
        MaterialNameTable saved = new(
        [
            (0, "empty"),
            (1, "sand"),
            (2, "water"),
            (3, "stone"),
        ]);
        MaterialTable current = new(
        [
            Material(0, "empty"),
            Material(1, "water"),
            Material(2, "stone"),
            Material(3, "sand"),
        ]);
        MaterialRemap remap = MaterialRemap.Build(saved, current, fallbackId: 0);
        ushort[] material = [0, 1, 2, 3, 1, 2, 3];

        remap.RemapInPlace(material);

        Assert.Equal([0, 3, 1, 2, 3, 1, 2], material);
        Assert.Equal(0, remap.FallbackHitCount);
    }

    /// <summary>
    /// 验证 materials.json 中间插入新材质导致 runtime id 后移时，旧档仍按 name 重映射。
    /// </summary>
    [Fact]
    public void MaterialRemapMapsByNameWhenMiddleInsertionShiftsRuntimeIds()
    {
        MaterialNameTable saved = new(
        [
            (0, "empty"),
            (1, "sand"),
            (2, "water"),
            (3, "lava"),
        ]);
        MaterialTable current = new(
        [
            Material(0, "empty"),
            Material(1, "sand"),
            Material(2, "oil"),
            Material(3, "water"),
            Material(4, "lava"),
        ]);
        MaterialRemap remap = MaterialRemap.Build(saved, current, fallbackId: 0);
        ushort[] material = [2, 3, 1, 0, 2, 3];

        remap.RemapInPlace(material);

        Assert.Equal([3, 4, 1, 0, 3, 4], material);
        Assert.Equal(0, remap.FallbackHitCount);
    }

    /// <summary>
    /// 验证 materials.json 删除旧材质时，该 saved id 映射 fallback，命中计数等于实际 cell 数。
    /// </summary>
    [Fact]
    public void MaterialRemapMapsDeletedMaterialsToFallbackAndCountsHits()
    {
        MaterialNameTable saved = new(
        [
            (0, "empty"),
            (1, "sand"),
            (2, "water"),
            (3, "acid"),
            (4, "stone"),
        ]);
        MaterialTable current = new(
        [
            Material(0, "empty"),
            Material(1, "sand"),
            Material(2, "stone"),
            Material(3, "water"),
        ]);
        MaterialRemap remap = MaterialRemap.Build(saved, current, fallbackId: 0);
        ushort[] material = [3, 1, 4, 2, 3, 0, 3];

        remap.RemapInPlace(material);

        Assert.Equal([0, 1, 2, 3, 0, 0, 0], material);
        Assert.Equal(3, remap.FallbackHitCount);
    }

    /// <summary>
    /// 验证 remap 联动 Damage：name 重排保留 Damage，缺失或损坏 saved id 落 fallback 时清 Damage。
    /// </summary>
    [Fact]
    public void MaterialRemapWithDamageClearsOnlyFallbackCells()
    {
        MaterialNameTable saved = new(
        [
            (0, "empty"),
            (1, "sand"),
            (2, "acid"),
            (4, "stone"),
        ]);
        MaterialTable current = new(
        [
            Material(0, "empty"),
            Material(1, "stone"),
            Material(2, "sand"),
        ]);
        MaterialRemap remap = MaterialRemap.Build(saved, current, fallbackId: 0);
        ushort[] material = [1, 2, 4, 99, 0];
        byte[] damage = [7, 8, 9, 10, 11];

        remap.RemapInPlace(material, damage);

        Assert.Equal([2, 0, 1, 0, 0], material);
        Assert.Equal([7, 0, 9, 0, 11], damage);
        Assert.Equal(2, remap.FallbackHitCount);
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
