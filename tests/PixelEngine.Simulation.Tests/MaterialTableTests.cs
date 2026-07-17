using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// Plan 04 材质定义、热表与 name↔id 注册表测试。
/// 不变式：材质定义、热表与 name↔id 双向解析一致。
/// </summary>
public sealed class MaterialTableTests
{
    /// <summary>
    /// 验证 MaterialDef 的热路径字段会被派生进 MaterialHotTable，冷字段不参与 movement facade。
    /// </summary>
    [Fact]
    public void MaterialHotTableCopiesRuntimeFieldsFromDefinitions()
    {
        // Arrange：准备输入与初始状态
        MaterialTable table = new(CreateDefinitions());

        // Assert：验证预期结果
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
        Assert.Equal(6, table.Hot.Hardness[1]);
        Assert.Equal(14, table.Hot.MaxIntegrity[1]);
        Assert.Equal(14, table.Hot.Integrity[1]);
        Assert.Equal(2, table.Hot.RubbleTarget[1]);
        Assert.Equal(2, table.Hot.DestroyedTarget[1]);
        Assert.Equal(3, table.Hot.DebrisCount[1]);
        Assert.Equal(1, table.Hot.MineYield[1]);
        Assert.True((table.Hot.PropertyFlags[1] & MaterialProperty.Corrodible) != 0);
        Assert.True((table.Hot.PropertyFlags[1] & MaterialProperty.Diggable) != 0);
        Assert.Equal(11, table.Hot.ReactionStart[1]);
        Assert.Equal(2, table.Hot.ReactionCount[1]);
        Assert.Equal(MaterialRenderStyle.Liquid, table.Visual.RenderStyle[1]);
        Assert.Equal(MaterialLegendCategory.Liquid, table.Visual.LegendCategory[1]);
        Assert.Equal(0xFF112233u, table.Visual.EdgeColorBGRA[1]);
        Assert.Equal(200, table.Visual.Opacity[1]);
        Assert.Equal(0xFF445566u, table.Visual.HighlightColorBGRA[1]);
        Assert.True(table.Visual.LegendVisible[1]);

        MaterialPropsTable props = new(table.Hot);
        Assert.Equal(CellType.Liquid, props.TypeOf(1));
        Assert.Equal(100, props.DensityOf(1));
        Assert.Equal(5, props.DispersionOf(1));
        Assert.Equal(5, props.FlowRateOf(1));
        Assert.Equal(11, props.ReactionStartOf(1));
        Assert.Equal(2, props.ReactionCountOf(1));
        uint updateProperties = table.Hot.CellUpdatePropertiesOfUnchecked(1);
        Assert.Equal(CellType.Liquid, MaterialHotTable.CellUpdateType(updateProperties));
        Assert.Equal(100, MaterialHotTable.CellUpdateDensity(updateProperties));
        Assert.Equal(5, MaterialHotTable.CellUpdateDispersion(updateProperties));
        Assert.True(MaterialHotTable.CellUpdateHasReaction(updateProperties));
        Assert.False(MaterialHotTable.CellUpdateHasCustomUpdate(updateProperties));
        Assert.Equal(120, props.DefaultLifetimeOf(1));
        Assert.Equal(6, props.HardnessOf(1));
        Assert.Equal(14, props.MaxIntegrityOf(1));
        Assert.Equal(14, props.IntegrityOf(1));
        Assert.Equal(2, props.RubbleTargetOf(1));
        Assert.Equal(2, props.DestroyedTargetOf(1));
        Assert.Equal(3, props.DebrisCountOf(1));
        Assert.Equal(1, props.MineYieldOf(1));
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
        // Arrange：准备输入与初始状态
        MaterialTable table = new(CreateDefinitions());
        MaterialDef[] reloaded =
        [
            CreateMaterial(0, "empty", CellType.Empty),
            CreateMaterial(0, "water", CellType.Liquid) with { Density = 111 },
            CreateMaterial(0, "fire", CellType.Fire) with { PropertyFlags = MaterialProperty.Fire },
        ];

        MaterialReloadResult result = table.ReloadStable(reloaded, [0, 4, 9], fallbackId: 0);

        // Assert：验证预期结果
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

    /// <summary>相同 stable reload 不应被判为变化，且不能清掉既有 custom-update 热路径位。</summary>
    [Fact]
    public void StableReloadNoChangePreservesRegisteredCustomUpdate()
    {
        MaterialDef[] definitions = CreateDefinitions();
        MaterialTable table = new(definitions);
        table.RegisterCustomUpdate("water", NoOpCustomUpdate);

        Assert.False(table.WouldReloadChange(definitions));

        MaterialReloadResult result = table.ReloadStable(definitions, fallbackId: 0);

        Assert.Empty(result.TombstoneIds);
        Assert.Equal(0, result.AddedCount);
        Assert.Equal(definitions.Length, result.PreservedCount);
        Assert.True((table.Get(1).PropertyFlags & MaterialProperty.HasCustomUpdate) != 0);
        Assert.True((table.Hot.PropertyFlags[1] & MaterialProperty.HasCustomUpdate) != 0);
    }

    /// <summary>CA cell-update 派生 lane 必须随稳定热重载及 custom-update 绑定一起替换。</summary>
    [Fact]
    public void CellUpdatePropertiesFollowStableReloadAndCustomUpdateBinding()
    {
        MaterialTable table = new(CreateDefinitions());
        MaterialPropsTable props = new(table.Hot);

        table.RegisterCustomUpdate("water", NoOpCustomUpdate);
        props.Reload(table.Hot);
        uint registered = props.Hot.CellUpdatePropertiesOfUnchecked(1);
        Assert.True(MaterialHotTable.CellUpdateHasReaction(registered));
        Assert.True(MaterialHotTable.CellUpdateHasCustomUpdate(registered));

        MaterialDef[] reloaded = CreateDefinitions();
        reloaded[1] = reloaded[1] with
        {
            Type = CellType.Gas,
            Density = 37,
            Dispersion = 9,
            ReactionCount = 0,
        };

        _ = table.ReloadStable(reloaded, fallbackId: 0);
        props.Reload(table.Hot);

        uint updated = props.Hot.CellUpdatePropertiesOfUnchecked(1);
        Assert.Equal(CellType.Gas, MaterialHotTable.CellUpdateType(updated));
        Assert.Equal(37, MaterialHotTable.CellUpdateDensity(updated));
        Assert.Equal(9, MaterialHotTable.CellUpdateDispersion(updated));
        Assert.False(MaterialHotTable.CellUpdateHasReaction(updated));
        Assert.True(MaterialHotTable.CellUpdateHasCustomUpdate(updated));
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
        Assert.Equal(1u << 11, (uint)MaterialProperty.Indestructible);
        Assert.Equal(1u << 12, (uint)MaterialProperty.Diggable);
    }

    private static void NoOpCustomUpdate(
        ref CellCursor cell,
        ref NeighborWindow window,
        ref ChunkWorkContext context)
    {
        _ = cell;
        _ = window;
        _ = context;
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
                Hardness = 6,
                Integrity = 14,
                DestroyedTarget = 2,
                DebrisCount = 3,
                MineYield = 1,
                TextureId = 4,
                BaseColorBGRA = 0xFFCC6633,
                ColorNoise = 8,
                RenderStyle = MaterialRenderStyle.Liquid,
                LegendCategory = MaterialLegendCategory.Liquid,
                EdgeColorBGRA = 0xFF112233,
                Opacity = 200,
                HighlightColorBGRA = 0xFF445566,
                DisplayName = "Water",
                LegendVisible = true,
                PropertyFlags = MaterialProperty.Corrodible | MaterialProperty.Conductive | MaterialProperty.Diggable,
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
