using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// Plan 04 材质定义、热表与 name↔id 注册表测试。
/// </summary>
public sealed class MaterialTableTests
{
    /// <summary>
    /// 验证 MaterialDef 的热路径字段会被派生进 MaterialHotTable，冷字段不参与 movement facade。
    /// </summary>
    [Fact]
    public void MaterialHotTableCopiesRuntimeFieldsFromDefinitions()
    {
        MaterialTable table = new(CreateDefinitions());

        Assert.Equal(3, table.Count);
        Assert.Equal(CellType.Liquid, table.Hot.Type[1]);
        Assert.Equal(100, table.Hot.Density[1]);
        Assert.Equal(5, table.Hot.Dispersion[1]);
        Assert.True(table.Hot.LiquidStatic[1]);
        Assert.True(table.Hot.LiquidSand[1]);
        Assert.Equal(3, table.Hot.Flammability[1]);
        Assert.Equal(250, table.Hot.AutoIgnitionTemp[1]);
        Assert.Equal(7, table.Hot.FireHp[1]);
        Assert.Equal(80, table.Hot.TemperatureOfFire[1]);
        Assert.Equal(2, table.Hot.GeneratesSmoke[1]);
        Assert.Equal(0.5f, table.Hot.MeltPoint[1]);
        Assert.Equal(2, table.Hot.MeltTarget[1]);
        Assert.Equal(-10f, table.Hot.FreezePoint[1]);
        Assert.Equal(2, table.Hot.FreezeTarget[1]);
        Assert.Equal(100f, table.Hot.BoilPoint[1]);
        Assert.Equal(2, table.Hot.BoilTarget[1]);
        Assert.Equal(30, table.Hot.HeatConduct[1]);
        Assert.Equal(4.18f, table.Hot.HeatCapacity[1]);
        Assert.Equal(120, table.Hot.DefaultLifetime[1]);
        Assert.Equal(9, table.Hot.Durability[1]);
        Assert.True((table.Hot.PropertyFlags[1] & MaterialProperty.Corrodible) != 0);
        Assert.Equal(11, table.Hot.ReactionStart[1]);
        Assert.Equal(2, table.Hot.ReactionCount[1]);

        MaterialPropsTable props = new(table.Hot);
        Assert.Equal(CellType.Liquid, props.TypeOf(1));
        Assert.Equal(100, props.DensityOf(1));
        Assert.Equal(5, props.DispersionOf(1));
        Assert.Equal(11, props.ReactionStartOf(1));
        Assert.Equal(2, props.ReactionCountOf(1));
        Assert.Equal(120, props.DefaultLifetimeOf(1));
    }

    /// <summary>
    /// 验证 name↔id 查询与存档 remap LUT 不依赖当前材质顺序。
    /// </summary>
    [Fact]
    public void MaterialTableBuildsNameMappingsAndRemapLut()
    {
        MaterialTable table = new(CreateDefinitions());

        Assert.True(table.TryGetId("water", out ushort waterId));
        Assert.Equal(1, waterId);
        Assert.Equal(0, table.GetIdOrFallback("missing", fallback: 0));
        Assert.Equal("sand", table.GetName(2));
        Assert.Equal([(0, "empty"), (1, "water"), (2, "sand")], table.BuildIdNameTable());

        ushort[] remap = table.BuildRemapLut([(0, "water"), (1, "missing"), (2, "empty")], fallbackId: 2);

        Assert.Equal([1, 2, 0], remap);
    }

    /// <summary>
    /// 验证稳定热重载保留既有 id、追加新材质、删除材质作 tombstone 且输出替换诊断计数。
    /// </summary>
    [Fact]
    public void ReloadStablePreservesIdsAppendsAndTombstones()
    {
        MaterialTable table = new(CreateDefinitions());
        MaterialDef[] reloaded =
        [
            CreateMaterial(0, "empty", CellType.Empty),
            CreateMaterial(0, "water", CellType.Liquid) with { Density = 111 },
            CreateMaterial(0, "fire", CellType.Fire) with { PropertyFlags = MaterialProperty.Fire },
        ];

        MaterialReloadResult result = table.ReloadStable(reloaded, [0, 4, 9], fallbackId: 0);

        Assert.Equal([2], result.TombstoneIds);
        Assert.Equal(1, result.AddedCount);
        Assert.Equal(2, result.PreservedCount);
        Assert.Equal(9, result.FallbackReplacementCount);
        Assert.True(table.TryGetId("water", out ushort waterId));
        Assert.Equal(1, waterId);
        Assert.True(table.TryGetId("fire", out ushort fireId));
        Assert.Equal(3, fireId);
        Assert.False(table.TryGetId("sand", out _));
        Assert.True(table.IsTombstone(2));
        Assert.Equal(111, table.Hot.Density[1]);
        Assert.Equal([(0, "empty"), (1, "water"), (3, "fire")], table.BuildIdNameTable());
    }

    /// <summary>
    /// 验证构建期拒绝 HeatCapacity 为 0 的材质，避免温度场除零。
    /// </summary>
    [Fact]
    public void MaterialTableRejectsZeroHeatCapacity()
    {
        MaterialDef[] definitions =
        [
            CreateMaterial(0, "empty", CellType.Empty) with { HeatCapacity = 0 },
        ];

        ArgumentException exception = Assert.Throws<ArgumentException>(() => new MaterialTable(definitions));
        Assert.Contains("HeatCapacity", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 验证 tag 到 PropertyFlags 的固定映射。
    /// </summary>
    [Fact]
    public void MaterialTagMapReturnsFixedPropertyBits()
    {
        Assert.Equal(MaterialProperty.Meltable, MaterialTagMap.ToProperty(MaterialTag.Meltable));
        Assert.Equal(MaterialProperty.Acid, MaterialTagMap.ToProperty(MaterialTag.Acid));
        Assert.Equal(MaterialProperty.Fire, MaterialTagMap.ToProperty(MaterialTag.Fire));
        Assert.Equal(MaterialProperty.Corrodible, MaterialTagMap.ToProperty(MaterialTag.Corrodible));
        Assert.Equal(MaterialProperty.Cold, MaterialTagMap.ToProperty(MaterialTag.Cold));
        Assert.Equal(MaterialProperty.MoltenMetal, MaterialTagMap.ToProperty(MaterialTag.MoltenMetal));
        Assert.Equal(MaterialProperty.Static, MaterialTagMap.ToProperty(MaterialTag.Static));
        Assert.Equal(MaterialProperty.BurnableFast, MaterialTagMap.ToProperty(MaterialTag.BurnableFast));
    }

    private static MaterialDef[] CreateDefinitions()
    {
        return
        [
            CreateMaterial(0, "empty", CellType.Empty),
            CreateMaterial(1, "water", CellType.Liquid) with
            {
                Density = 100,
                Dispersion = 5,
                LiquidStatic = true,
                LiquidSand = true,
                Flammability = 3,
                AutoIgnitionTemp = 250,
                FireHp = 7,
                TemperatureOfFire = 80,
                GeneratesSmoke = 2,
                MeltPoint = 0.5f,
                MeltTarget = 2,
                FreezePoint = -10f,
                FreezeTarget = 2,
                BoilPoint = 100f,
                BoilTarget = 2,
                HeatConduct = 30,
                HeatCapacity = 4.18f,
                DefaultLifetime = 120,
                Durability = 9,
                TextureId = 4,
                BaseColorBGRA = 0xFFCC6633,
                ColorNoise = 8,
                PropertyFlags = MaterialProperty.Corrodible | MaterialProperty.Conductive,
                ReactionStart = 11,
                ReactionCount = 2,
                AudioCues = new AudioCueSet
                {
                    ImpactCue = 1,
                    FireCue = 2,
                    SplashCue = 3,
                    ExplosionCue = 4,
                    ShatterCue = 5,
                    AmbientCue = 6,
                },
            },
            CreateMaterial(2, "sand", CellType.Powder) with { Density = 120 },
        ];
    }

    private static MaterialDef CreateMaterial(ushort id, string name, CellType type)
    {
        return new MaterialDef
        {
            Id = id,
            Name = name,
            Type = type,
            HeatCapacity = 1f,
            TextureId = -1,
            MeltPoint = float.NaN,
            FreezePoint = float.NaN,
            BoilPoint = float.NaN,
        };
    }
}
