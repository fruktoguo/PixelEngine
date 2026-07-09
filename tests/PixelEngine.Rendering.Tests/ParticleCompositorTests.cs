using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using Xunit;

namespace PixelEngine.Rendering.Tests;

/// <summary>
/// 粒子合成器测试：与背景混合及深度排序。
/// </summary>
public sealed class ParticleCompositorTests
{
    /// <summary>
    /// 验证Stamp Rounds World Position And Clips Viewport。
    /// </summary>
    [Fact]
    public void StampRoundsWorldPositionAndClipsViewport()
    {
        MaterialTable materials = Materials(
            Material(0, "empty", CellType.Empty, 0),
            Material(1, "spark", CellType.Powder, 0xFF112233u));
        RenderBuffer target = new(4, 4);
        RenderAuxBuffers aux = new(4, 4);
        Particle[] particles =
        [
            new() { X = 11.49f, Y = 21.50f, Material = 1 },
            new() { X = -4, Y = 0, Material = 1 },
        ];

        new ParticleCompositor().Stamp(
            particles,
            materials,
            CameraState.OneToOne(10, 20, 4, 4),
            target,
            aux);

        Assert.Equal(0xFF112233u, target.Pixels[(2 * 4) + 1]);
        Assert.Equal(0u, target.Pixels[0]);
    }

    /// <summary>
    /// 验证Stamp Writes Emissive For Fire And Emissive Materials。
    /// </summary>
    [Fact]
    public void StampWritesEmissiveForFireAndEmissiveMaterials()
    {
        MaterialTable materials = Materials(
            Material(0, "empty", CellType.Empty, 0),
            Material(1, "fire", CellType.Fire, 0xFF0000FFu),
            Material(2, "glow", CellType.Powder, 0xFF00FF00u) with { PropertyFlags = MaterialProperty.Emissive },
            Material(3, "dust", CellType.Powder, 0xFFFF0000u));
        RenderBuffer target = new(3, 1);
        RenderAuxBuffers aux = new(3, 1);
        Particle[] particles =
        [
            new() { X = 0, Y = 0, Material = 1 },
            new() { X = 1, Y = 0, Material = 2 },
            new() { X = 2, Y = 0, Material = 3 },
        ];

        new ParticleCompositor().Stamp(
            particles,
            materials,
            CameraState.OneToOne(0, 0, 3, 1),
            target,
            aux);

        Assert.Equal(target.Pixels[0], aux.Emissive[0]);
        Assert.Equal(target.Pixels[1], aux.Emissive[1]);
        Assert.Equal(0u, aux.Emissive[2]);
    }

    /// <summary>
    /// 验证Stamp Can Use Texture Provider And Color Variant。
    /// </summary>
    [Fact]
    public void StampCanUseTextureProviderAndColorVariant()
    {
        MaterialTable materials = Materials(
            Material(0, "empty", CellType.Empty, 0),
            Material(1, "textured", CellType.Powder, 0xFF101010u) with
            {
                TextureId = 3,
                ColorNoise = 64,
            });
        RenderBuffer target = new(1, 1);
        RenderAuxBuffers aux = new(1, 1);
        Particle particle = new() { X = 0, Y = 0, Material = 1, ColorVariant = 255 };

        new ParticleCompositor(new TestTextureProvider()).Stamp(
            [particle],
            materials,
            CameraState.OneToOne(0, 0, 1, 1),
            target,
            aux);

        Assert.NotEqual(0xFF202020u, target.Pixels[0]);
        Assert.True(target.Pixels[0] > 0xFF202020u);
    }

    private static MaterialDef Material(ushort id, string name, CellType type, uint color)
    {
        return new MaterialDef
        {
            Id = id,
            Name = name,
            Type = type,
            HeatCapacity = 1f,
            TextureId = -1,
            BaseColorBGRA = color,
        };
    }

    private static MaterialTable Materials(params MaterialDef[] definitions)
    {
        return new MaterialTable(definitions);
    }

    private sealed class TestTextureProvider : IMaterialTextureProvider
    {
        public bool TrySample(in MaterialDef material, int worldX, int worldY, out uint bgra)
        {
            bgra = 0xFF202020u;
            return true;
        }
    }
}
