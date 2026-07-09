using PixelEngine.Simulation.Particles;
using Xunit;

namespace PixelEngine.Simulation.Tests;

/// <summary>
/// 区域 / 光束世界效果跨 chunk 边界的破坏质量守恒测试。
/// 不变式：区域/光束效果跨 chunk 边界破坏质量守恒。
/// </summary>
public sealed class WorldEffectBoundaryConservationTests
{
    private const ushort Empty = 0;
    private const ushort Stone = 1;
    private const ushort Gravel = 2;

    /// <summary>
    /// 验证 DamageCircle 跨 2×2 chunk 角点时破坏数、rubble 数与碎屑粒子数一致。
    /// </summary>
    [Fact]
    public void DamageCircleAcrossFourChunkCornerConservesDestroyedRubbleAndDebris()
    {
        // Arrange：准备输入与初始状态
        DeterministicSimFixture.TestChunkSource source = DeterministicSimFixture.TestChunkSource.CreateDense(0, 0, 1, 1);
        MaterialTable materials = CreateMaterials();
        ParticleSystem particles = new(capacity: 64);
        SimulationKernel kernel = new(source, new MaterialPropsTable(materials.Hot), cellDestructionSink: particles);
        const int centerX = 64;
        const int centerY = 64;
        const int radius = 2;
        int expected = FillCircle(source, centerX, centerY, radius, Stone);

        int destroyed = kernel.DamageCircle(centerX, centerY, radius, damage: 10, falloff: false);

        // Assert：验证预期结果
        Assert.Equal(expected, destroyed);
        Assert.Equal(expected, CountMaterial(source, Gravel));
        Assert.Equal(0, CountMaterial(source, Stone));
        Assert.Equal(expected, particles.ActiveCount);
        Assert.All(particles.ActiveReadOnly.ToArray(), static particle => Assert.Equal(Gravel, particle.Material));
    }

    /// <summary>
    /// 验证 DamageBeam 跨水平 chunk 边界时不会重复或漏掉破坏样本。
    /// </summary>
    [Fact]
    public void DamageBeamAcrossHorizontalChunkBoundaryConservesDestroyedRubbleAndDebris()
    {
        // Arrange：准备输入与初始状态
        DeterministicSimFixture.TestChunkSource source = DeterministicSimFixture.TestChunkSource.CreateDense(0, 0, 1, 0);
        MaterialTable materials = CreateMaterials();
        ParticleSystem particles = new(capacity: 16);
        SimulationKernel kernel = new(source, new MaterialPropsTable(materials.Hot), cellDestructionSink: particles);
        const int startX = 61;
        const int y = 32;
        const int length = 6;
        int expected = FillBeamSamples(source, startX, y, dirX: 1f, dirY: 0f, length, Stone);

        int destroyed = kernel.DamageBeam(startX, y, dirX: 1f, dirY: 0f, length, damagePerCell: 10);

        // Assert：验证预期结果
        Assert.Equal(expected, destroyed);
        Assert.Equal(expected, CountMaterial(source, Gravel));
        Assert.Equal(0, CountMaterial(source, Stone));
        Assert.Equal(expected, particles.ActiveCount);
        Assert.All(particles.ActiveReadOnly.ToArray(), static particle => Assert.Equal(Gravel, particle.Material));
    }

    /// <summary>
    /// 验证结构破坏入口预热后不产生托管堆分配。
    /// </summary>
    [Fact]
    public void StructuralDamageEntriesDoNotAllocateAfterWarmup()
    {
        // Arrange：准备输入与初始状态
        DeterministicSimFixture.TestChunkSource source = DeterministicSimFixture.TestChunkSource.CreateDense(0, 0, 0, 0);
        MaterialTable materials = CreateMaterials();
        SimulationKernel kernel = new(source, new MaterialPropsTable(materials.Hot));
        Chunk chunk = source.GetRequired(new ChunkCoord(0, 0));
        const int Iterations = 128;

        ResetDamageArena(chunk);
        _ = kernel.ApplyStructuralDamage(10, 10, damage: 10);
        _ = kernel.DamageCircle(20, 20, radius: 2, damage: 10, falloff: false);
        _ = kernel.DamageBeam(32, 32, dirX: 1f, dirY: 0f, length: 5, damagePerCell: 10);

        long before = GC.GetAllocatedBytesForCurrentThread();
        int destroyed = 0;
        for (int i = 0; i < Iterations; i++)
        {
            ResetDamageArena(chunk);
            destroyed += kernel.ApplyStructuralDamage(10, 10, damage: 10) ? 1 : 0;
            destroyed += kernel.DamageCircle(20, 20, radius: 2, damage: 10, falloff: false);
            destroyed += kernel.DamageBeam(32, 32, dirX: 1f, dirY: 0f, length: 5, damagePerCell: 10);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        // Assert：验证预期结果
        Assert.True(destroyed > 0);
        Assert.Equal(0, allocated);
    }

    private static MaterialTable CreateMaterials()
    {
        return new MaterialTable(
        [
            Material(Empty, "empty", CellType.Empty),
            Material(Stone, "stone", CellType.Solid) with { Integrity = 1, DestroyedTarget = Gravel, DebrisCount = 1 },
            Material(Gravel, "gravel", CellType.Powder),
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
            TextureId = -1,
            MeltPoint = float.NaN,
            FreezePoint = float.NaN,
            BoilPoint = float.NaN,
        };
    }

    private static int FillCircle(
        DeterministicSimFixture.TestChunkSource source,
        int centerX,
        int centerY,
        int radius,
        ushort material)
    {
        int count = 0;
        int radiusSquared = radius * radius;
        for (int y = centerY - radius; y <= centerY + radius; y++)
        {
            int dy = y - centerY;
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                int dx = x - centerX;
                if ((dx * dx) + (dy * dy) > radiusSquared)
                {
                    continue;
                }

                SetWorld(source, x, y, material);
                count++;
            }
        }

        return count;
    }

    private static int FillBeamSamples(
        DeterministicSimFixture.TestChunkSource source,
        int startX,
        int startY,
        float dirX,
        float dirY,
        int length,
        ushort material)
    {
        float magnitude = MathF.Sqrt((dirX * dirX) + (dirY * dirY));
        float stepX = dirX / magnitude;
        float stepY = dirY / magnitude;
        int count = 0;
        int lastX = int.MinValue;
        int lastY = int.MinValue;
        for (int i = 0; i <= length; i++)
        {
            int x = (int)MathF.Round(startX + (stepX * i));
            int y = (int)MathF.Round(startY + (stepY * i));
            if (x == lastX && y == lastY)
            {
                continue;
            }

            SetWorld(source, x, y, material);
            lastX = x;
            lastY = y;
            count++;
        }

        return count;
    }

    private static void SetWorld(DeterministicSimFixture.TestChunkSource source, int worldX, int worldY, ushort material)
    {
        Chunk chunk = source.GetRequired(CellAddressing.WorldToChunk(worldX, worldY));
        chunk.Material[CellAddressing.LocalIndex(worldX, worldY)] = material;
    }

    private static int CountMaterial(DeterministicSimFixture.TestChunkSource source, ushort material)
    {
        int count = 0;
        foreach (Chunk chunk in source.ResidentChunks.ToArray())
        {
            ReadOnlySpan<ushort> cells = chunk.Material;
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] == material)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static void ResetDamageArena(Chunk chunk)
    {
        chunk.Material.AsSpan().Clear();
        chunk.Flags.AsSpan().Clear();
        chunk.Lifetime.AsSpan().Clear();
        chunk.Damage.AsSpan().Clear();
        chunk.Material[CellAddressing.LocalIndexFromLocal(10, 10)] = Stone;
        FillLocalCircle(chunk, centerX: 20, centerY: 20, radius: 2, Stone);
        for (int x = 32; x <= 37; x++)
        {
            chunk.Material[CellAddressing.LocalIndexFromLocal(x, 32)] = Stone;
        }
    }

    private static void FillLocalCircle(Chunk chunk, int centerX, int centerY, int radius, ushort material)
    {
        int radiusSquared = radius * radius;
        for (int y = centerY - radius; y <= centerY + radius; y++)
        {
            int dy = y - centerY;
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                int dx = x - centerX;
                if ((dx * dx) + (dy * dy) <= radiusSquared)
                {
                    chunk.Material[CellAddressing.LocalIndexFromLocal(x, y)] = material;
                }
            }
        }
    }
}
