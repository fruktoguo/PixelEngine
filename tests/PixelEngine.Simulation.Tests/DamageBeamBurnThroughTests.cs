using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// demo-playability 激光烧穿速率与热相变验收测试。
/// </summary>
public sealed class DamageBeamBurnThroughTests
{
    private const ushort Empty = 0;
    private const ushort Wood = 1;
    private const ushort Ice = 2;
    private const ushort Metal = 3;
    private const ushort Water = 4;
    private const ushort MoltenMetal = 5;
    private const ushort Ash = 6;

    /// <summary>
    /// 验证同一束激光结构伤害会快速烧穿低硬度 wood/ice，但只累积高硬度 metal 的 Damage。
    /// </summary>
    [Fact]
    public void DamageBeamBurnsThroughLowHardnessMaterialsBeforeMetal()
    {
        DeterministicSimFixture.TestChunkSource source = DeterministicSimFixture.TestChunkSource.CreateDense(0, 0, 0, 0);
        Chunk chunk = source.GetRequired(new ChunkCoord(0, 0));
        MaterialTable materials = CreateMaterials();
        SimulationKernel kernel = new(source, new MaterialPropsTable(materials.Hot));
        FillBeam(chunk, 8, 10, length: 5, Wood);
        FillBeam(chunk, 8, 12, length: 5, Ice);
        FillBeam(chunk, 8, 14, length: 5, Metal);

        int woodDestroyed = kernel.DamageBeam(8, 10, dirX: 1f, dirY: 0f, length: 5, damagePerCell: 25);
        int iceDestroyed = kernel.DamageBeam(8, 12, dirX: 1f, dirY: 0f, length: 5, damagePerCell: 25);
        int metalDestroyed = kernel.DamageBeam(8, 14, dirX: 1f, dirY: 0f, length: 5, damagePerCell: 25);

        Assert.Equal(6, woodDestroyed);
        Assert.Equal(6, iceDestroyed);
        Assert.Equal(0, metalDestroyed);
        for (int x = 8; x <= 13; x++)
        {
            Assert.Equal(Ash, Get(chunk, x, 10));
            Assert.Equal(Water, Get(chunk, x, 12));
            Assert.Equal(Metal, Get(chunk, x, 14));
            Assert.Equal(5, Damage(chunk, x, 14));
        }
    }

    /// <summary>
    /// 验证 Demo 激光的热量侧可将未被结构伤害烧穿的 metal 按熔点相变为 molten metal。
    /// </summary>
    [Fact]
    public void LaserHeatMeltsMetalThatSurvivesBeamDamage()
    {
        DeterministicSimFixture.TestChunkSource source = DeterministicSimFixture.TestChunkSource.CreateDense(0, 0, 0, 0);
        Chunk chunk = source.GetRequired(new ChunkCoord(0, 0));
        MaterialTable materials = CreateMaterials();
        SimulationKernel kernel = new(source, new MaterialPropsTable(materials.Hot));
        TemperatureField temperature = new(storageKind: TemperatureStorageKind.Float32);
        FillBeam(chunk, 20, 20, length: 5, Metal);

        int destroyed = kernel.DamageBeam(20, 20, dirX: 1f, dirY: 0f, length: 5, damagePerCell: 25);
        for (int x = 20; x <= 25; x++)
        {
            temperature.AddHeat(x, 20, Metal, materials.Hot, deltaC: 80f);
        }

        temperature.ApplyPhaseTransitions(source, materials, CellFlags.Parity);

        Assert.Equal(0, destroyed);
        for (int x = 20; x <= 25; x++)
        {
            Assert.Equal(MoltenMetal, Get(chunk, x, 20));
            Assert.Equal(0, Damage(chunk, x, 20));
            Assert.True(CellFlags.MatchesFrame(Flags(chunk, x, 20), CellFlags.Parity));
        }
    }

    private static MaterialTable CreateMaterials()
    {
        return new MaterialTable(
        [
            Material(Empty, "empty", CellType.Empty),
            Material(Wood, "wood", CellType.Solid) with { Hardness = 1, Integrity = 15, DestroyedTarget = Ash },
            Material(Ice, "ice", CellType.Solid) with { Hardness = 2, Integrity = 16, DestroyedTarget = Water, MeltPoint = 10f, MeltTarget = Water },
            Material(Metal, "metal", CellType.Solid) with { Hardness = 20, Integrity = 80, MeltPoint = 50f, MeltTarget = MoltenMetal, HeatCapacity = 1 },
            Material(Water, "water", CellType.Liquid),
            Material(MoltenMetal, "molten_metal", CellType.Liquid),
            Material(Ash, "ash", CellType.Powder),
        ]);
    }

    private static MaterialDef Material(ushort id, string name, CellType type)
    {
        return new MaterialDef
        {
            Id = id,
            Name = name,
            Type = type,
            Density = type == CellType.Empty ? (byte)0 : (byte)120,
            HeatCapacity = 1,
            HeatConduct = byte.MaxValue,
            TextureId = -1,
            MeltPoint = float.NaN,
            FreezePoint = float.NaN,
            BoilPoint = float.NaN,
        };
    }

    private static void FillBeam(Chunk chunk, int startX, int y, int length, ushort material)
    {
        for (int x = startX; x <= startX + length; x++)
        {
            Set(chunk, x, y, material);
        }
    }

    private static void Set(Chunk chunk, int lx, int ly, ushort material)
    {
        chunk.Material[CellAddressing.LocalIndexFromLocal(lx, ly)] = material;
    }

    private static ushort Get(Chunk chunk, int lx, int ly)
    {
        return chunk.Material[CellAddressing.LocalIndexFromLocal(lx, ly)];
    }

    private static byte Damage(Chunk chunk, int lx, int ly)
    {
        return chunk.Damage[CellAddressing.LocalIndexFromLocal(lx, ly)];
    }

    private static byte Flags(Chunk chunk, int lx, int ly)
    {
        return chunk.Flags[CellAddressing.LocalIndexFromLocal(lx, ly)];
    }
}
