using PixelEngine.Simulation;
using PixelEngine.World;
using Xunit;

namespace PixelEngine.Rendering.Tests;

public sealed class RenderBufferBuilderTests
{
    [Fact]
    public void BuildMapsScreenPixelsToWorldCellsAndMaterialColors()
    {
        ResidentChunkMap chunks = new();
        Chunk chunk = new(new ChunkCoord(0, 0));
        SetMaterial(chunk, 2, 3, 1);
        chunks.Add(chunk);
        MaterialTable materials = Materials(
            Material(0, "empty", CellType.Empty, 0),
            Material(1, "sand", CellType.Powder, 0xFF112233u));
        RenderBuffer target = new(4, 4);
        RenderAuxBuffers aux = new(4, 4);
        RenderFrameContext context = new(
            chunks,
            materials,
            new TemperatureField(),
            CameraState.OneToOne(2, 3, 4, 4),
            simStepped: true);

        new RenderBufferBuilder().Build(context, target, aux);

        Assert.Equal(0xFF112233u, target.Pixels[0]);
        Assert.Equal(0u, target.Pixels[1]);
        Assert.All(aux.Emissive.ToArray(), static value => Assert.Equal(0u, value));
    }

    [Fact]
    public void BuildPaletteFastPathSpansChunkRowsAndWritesAux()
    {
        ResidentChunkMap chunks = new();
        Chunk left = new(new ChunkCoord(0, 0));
        Chunk right = new(new ChunkCoord(1, 0));
        SetMaterial(left, 62, 0, 1);
        SetMaterial(left, 63, 0, 2);
        SetMaterial(right, 0, 0, 3);
        chunks.Add(left);
        chunks.Add(right);
        MaterialTable materials = Materials(
            Material(0, "empty", CellType.Empty, 0),
            Material(1, "glow", CellType.Powder, 0xFF010203u) with { PropertyFlags = MaterialProperty.Emissive },
            Material(2, "rock", CellType.Solid, 0xFF040506u),
            Material(3, "static", CellType.Powder, 0xFF070809u) with { PropertyFlags = MaterialProperty.Static });
        RenderBuffer target = new(3, 1);
        RenderAuxBuffers aux = new(3, 1);
        RenderFrameContext context = new(
            chunks,
            materials,
            new TemperatureField(),
            CameraState.OneToOne(62, 0, 3, 1),
            simStepped: true);

        new RenderBufferBuilder().Build(context, target, aux);

        Assert.Equal([0xFF010203u, 0xFF040506u, 0xFF070809u], target.Pixels.ToArray());
        Assert.Equal(0xFF010203u, aux.Emissive[0]);
        Assert.Equal(0u, aux.Emissive[1]);
        Assert.Equal(0u, aux.Emissive[2]);
        Assert.Equal(0, aux.Occluder[0]);
        Assert.Equal(byte.MaxValue, aux.Occluder[1]);
        Assert.Equal(byte.MaxValue, aux.Occluder[2]);
    }

    [Fact]
    public void BuildAppliesTextureProviderTemperatureGlowAndAuxOutputs()
    {
        ResidentChunkMap chunks = new();
        Chunk chunk = new(new ChunkCoord(0, 0));
        SetMaterial(chunk, 0, 0, 1);
        SetMaterial(chunk, 4, 0, 2);
        chunks.Add(chunk);
        MaterialTable materials = Materials(
            Material(0, "empty", CellType.Empty, 0),
            Material(1, "lamp", CellType.Solid, 0xFF000010u) with
            {
                TextureId = 7,
                PropertyFlags = MaterialProperty.Emissive,
            },
            Material(2, "rock", CellType.Solid, 0xFF010101u));
        TemperatureField temperature = new();
        temperature.AddHeat(4, 0, 200);
        RenderBuffer target = new(2, 1);
        RenderAuxBuffers aux = new(2, 1);
        RenderFrameContext context = new(
            chunks,
            materials,
            temperature,
            new CameraState(0, 0, 4, 2, 1),
            simStepped: true);

        new RenderBufferBuilder(textures: new TestTextureProvider()).Build(context, target, aux);

        Assert.Equal(0xFF445566u, target.Pixels[0]);
        Assert.NotEqual(0xFF010101u, target.Pixels[1]);
        Assert.Equal(target.Pixels[0], aux.Emissive[0]);
        Assert.Equal(target.Pixels[1], aux.Emissive[1]);
        Assert.Equal(byte.MaxValue, aux.Occluder[0]);
        Assert.Equal(byte.MaxValue, aux.Occluder[1]);
    }

    [Fact]
    public void BuildDoesNotMutateSimulationCells()
    {
        ResidentChunkMap chunks = new();
        Chunk chunk = new(new ChunkCoord(0, 0));
        SetMaterial(chunk, 1, 1, 1);
        chunk.Flags[CellAddressing.LocalIndexFromLocal(1, 1)] = 0xAA;
        chunk.Lifetime[CellAddressing.LocalIndexFromLocal(1, 1)] = 9;
        chunks.Add(chunk);
        MaterialTable materials = Materials(
            Material(0, "empty", CellType.Empty, 0),
            Material(1, "sand", CellType.Powder, 0xFFABCDEFu));
        RenderBuffer target = new(1, 1);
        RenderAuxBuffers aux = new(1, 1);
        RenderFrameContext context = new(
            chunks,
            materials,
            new TemperatureField(),
            CameraState.OneToOne(1, 1, 1, 1),
            simStepped: true);

        new RenderBufferBuilder().Build(context, target, aux);

        int local = CellAddressing.LocalIndexFromLocal(1, 1);
        Assert.Equal((ushort)1, chunk.Material[local]);
        Assert.Equal(0xAA, chunk.Flags[local]);
        Assert.Equal(9, chunk.Lifetime[local]);
    }

    [Fact]
    public void BuildReusesPreviousBuffersWhenSimDidNotStep()
    {
        ResidentChunkMap chunks = new();
        Chunk chunk = new(new ChunkCoord(0, 0));
        SetMaterial(chunk, 0, 0, 1);
        chunks.Add(chunk);
        MaterialTable materials = Materials(
            Material(0, "empty", CellType.Empty, 0),
            Material(1, "sand", CellType.Powder, 0xFF010203u));
        RenderBuffer target = new(1, 1);
        RenderAuxBuffers aux = new(1, 1);
        target.Pixels[0] = 0xFFEEDDCCu;
        aux.Emissive[0] = 0xFF101010u;
        aux.Occluder[0] = 123;
        RenderFrameContext context = new(
            chunks,
            materials,
            new TemperatureField(),
            CameraState.OneToOne(0, 0, 1, 1),
            simStepped: false);

        new RenderBufferBuilder().Build(context, target, aux);

        Assert.Equal(0xFFEEDDCCu, target.Pixels[0]);
        Assert.Equal(0xFF101010u, aux.Emissive[0]);
        Assert.Equal(123, aux.Occluder[0]);
    }

    private static void SetMaterial(Chunk chunk, int lx, int ly, ushort material)
    {
        chunk.Material[CellAddressing.LocalIndexFromLocal(lx, ly)] = material;
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
            bgra = material.TextureId == 7 ? 0xFF445566u : 0;
            return material.TextureId == 7;
        }
    }
}
