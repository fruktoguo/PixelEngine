using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// demo-playability 结构破坏抗性分档测试。
/// 不变式：结构破坏抗性分档与材质表一致、阈值边界可复现。
/// </summary>
public sealed class CellDamageResistanceTests
{
    private const ushort Empty = 0;
    private const ushort Sand = 1;
    private const ushort Dirt = 2;
    private const ushort Stone = 3;
    private const ushort Metal = 4;
    private const ushort Gravel = 5;

    /// <summary>
    /// 同一 DamageCircle 下低抗性材质即碎，stone 需累计，大硬度 metal 对小当量近免疫。
    /// </summary>
    [Fact]
    public void DamageCircleDifferentiatesMaterialResistanceFromData()
    {
        DeterministicSimFixture.TestChunkSource source = DeterministicSimFixture.TestChunkSource.CreateDense(0, 0, 0, 0);
        Chunk chunk = source.GetRequired(new ChunkCoord(0, 0));
        MaterialTable materials = new(CreateMaterials());
        SimulationKernel kernel = new(source, new MaterialPropsTable(materials.Hot));
        Set(chunk, 20, 20, Sand);
        Set(chunk, 21, 20, Dirt);
        Set(chunk, 22, 20, Stone);
        Set(chunk, 20, 22, Metal);

        int destroyed = kernel.DamageCircle(20, 20, radius: 2, damage: 10, falloff: false);

        Assert.Equal(2, destroyed);
        Assert.Equal(Empty, Get(chunk, 20, 20));
        Assert.Equal(Empty, Get(chunk, 21, 20));
        Assert.Equal(Stone, Get(chunk, 22, 20));
        Assert.Equal(2, Damage(chunk, 22, 20));
        Assert.Equal(Metal, Get(chunk, 20, 22));
        Assert.Equal(0, Damage(chunk, 20, 22));

        Assert.Equal(0, kernel.DamageCircle(22, 20, radius: 0, damage: 17, falloff: false));
        Assert.Equal(Stone, Get(chunk, 22, 20));
        Assert.Equal(11, Damage(chunk, 22, 20));

        Assert.Equal(1, kernel.DamageCircle(22, 20, radius: 0, damage: 17, falloff: false));
        Assert.Equal(Gravel, Get(chunk, 22, 20));
        Assert.Equal(0, Damage(chunk, 22, 20));

        Assert.Equal(1, kernel.DamageCircle(20, 22, radius: 0, damage: 250, falloff: false));
        Assert.Equal(Empty, Get(chunk, 20, 22));
        Assert.Equal(0, Damage(chunk, 20, 22));
    }

    /// <summary>
    /// MaxIntegrity 为 0 时，任何穿过硬度吸收的有效伤害都会即时破坏。
    /// </summary>
    [Fact]
    public void MaxIntegrityZeroDestroysImmediatelyAfterHardness()
    {
        DeterministicSimFixture.TestChunkSource source = DeterministicSimFixture.TestChunkSource.CreateDense(0, 0, 0, 0);
        Chunk chunk = source.GetRequired(new ChunkCoord(0, 0));
        MaterialTable materials = new(CreateMaterials());
        SimulationKernel kernel = new(source, new MaterialPropsTable(materials.Hot));
        Set(chunk, 10, 10, Sand);

        bool destroyed = kernel.ApplyStructuralDamage(10, 10, damage: 1);

        Assert.True(destroyed);
        Assert.Equal(Empty, Get(chunk, 10, 10));
        Assert.Equal(0, Damage(chunk, 10, 10));
        Assert.True(chunk.WorkingDirty.MinX <= 10);
        Assert.True(chunk.WorkingDirty.MinY <= 10);
        Assert.True(chunk.WorkingDirty.MaxX >= 10);
        Assert.True(chunk.WorkingDirty.MaxY >= 10);
    }

    private static MaterialDef[] CreateMaterials()
    {
        return
        [
            Material(Empty, "empty", CellType.Empty),
            Material(Sand, "sand", CellType.Powder) with { Density = 120, Integrity = 0 },
            Material(Dirt, "dirt", CellType.Solid) with { Density = 140, Integrity = 5 },
            Material(Stone, "stone", CellType.Solid) with { Density = 200, Hardness = 8, Integrity = 20, DestroyedTarget = Gravel },
            Material(Metal, "metal", CellType.Solid) with { Density = 240, Hardness = 20, Integrity = 200 },
            Material(Gravel, "gravel", CellType.Powder) with { Density = 150 },
        ];
    }

    private static MaterialDef Material(ushort id, string name, CellType type)
    {
        return new MaterialDef
        {
            Id = id,
            Name = name,
            Type = type,
            Density = type == CellType.Empty ? (byte)0 : (byte)100,
            HeatCapacity = 1,
            TextureId = -1,
            MeltPoint = float.NaN,
            FreezePoint = float.NaN,
            BoilPoint = float.NaN,
        };
    }

    private static void Set(Chunk chunk, int lx, int ly, ushort material)
    {
        chunk.MaterialBuffer[CellAddressing.LocalIndexFromLocal(lx, ly)] = material;
    }

    private static ushort Get(Chunk chunk, int lx, int ly)
    {
        return chunk.MaterialBuffer[CellAddressing.LocalIndexFromLocal(lx, ly)];
    }

    private static byte Damage(Chunk chunk, int lx, int ly)
    {
        return chunk.DamageBuffer[CellAddressing.LocalIndexFromLocal(lx, ly)];
    }
}
