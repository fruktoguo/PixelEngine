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
    public void BuildPaletteFastPathAppliesColorNoise()
    {
        ResidentChunkMap chunks = new();
        Chunk chunk = new(new ChunkCoord(0, 0));
        SetMaterial(chunk, 3, 5, 1);
        chunks.Add(chunk);
        MaterialTable materials = Materials(
            Material(0, "empty", CellType.Empty, 0),
            Material(1, "sand", CellType.Powder, 0xFF404040u) with { ColorNoise = 96 });
        RenderBuffer target = new(1, 1);
        RenderAuxBuffers aux = new(1, 1);
        RenderFrameContext context = new(
            chunks,
            materials,
            new TemperatureField(),
            CameraState.OneToOne(3, 5, 1, 1),
            simStepped: true);

        new RenderBufferBuilder().Build(context, target, aux);

        Assert.Equal(ExpectedColorNoise(0xFF404040u, 96, 3, 5), target.Pixels[0]);
    }

    [Fact]
    public void BuildPaletteZoomFastPathRepeatsCellsAcrossChunkRowsAndWritesAux()
    {
        ResidentChunkMap chunks = new();
        Chunk left = new(new ChunkCoord(0, 0));
        Chunk right = new(new ChunkCoord(1, 0));
        SetMaterial(left, 62, 0, 1);
        SetMaterial(left, 63, 0, 2);
        SetMaterial(right, 0, 0, 3);
        SetMaterial(left, 62, 1, 2);
        SetMaterial(left, 63, 1, 1);
        SetMaterial(right, 0, 1, 0);
        chunks.Add(left);
        chunks.Add(right);
        MaterialTable materials = Materials(
            Material(0, "empty", CellType.Empty, 0),
            Material(1, "glow", CellType.Powder, 0xFF202020u) with
            {
                ColorNoise = 32,
                PropertyFlags = MaterialProperty.Emissive,
            },
            Material(2, "rock", CellType.Solid, 0xFF040506u),
            Material(3, "static", CellType.Powder, 0xFF070809u) with { PropertyFlags = MaterialProperty.Static });
        RenderBuffer target = new(5, 3);
        RenderAuxBuffers aux = new(5, 3);
        RenderFrameContext context = new(
            chunks,
            materials,
            new TemperatureField(),
            new CameraState(62, 0, 0.5f, 5, 3),
            simStepped: true);

        new RenderBufferBuilder().Build(context, target, aux);

        uint glow00 = ExpectedColorNoise(0xFF202020u, 32, 62, 0);
        uint glow11 = ExpectedColorNoise(0xFF202020u, 32, 63, 1);
        Assert.Equal(
            [
                glow00, glow00, 0xFF040506u, 0xFF040506u, 0xFF070809u,
                glow00, glow00, 0xFF040506u, 0xFF040506u, 0xFF070809u,
                0xFF040506u, 0xFF040506u, glow11, glow11, 0u,
            ],
            target.Pixels.ToArray());
        Assert.Equal(
            [
                glow00, glow00, 0u, 0u, 0u,
                glow00, glow00, 0u, 0u, 0u,
                0u, 0u, glow11, glow11, 0u,
            ],
            aux.Emissive.ToArray());
        byte[] expectedOccluder =
        [
            0, 0, byte.MaxValue, byte.MaxValue, byte.MaxValue,
            0, 0, byte.MaxValue, byte.MaxValue, byte.MaxValue,
            byte.MaxValue, byte.MaxValue, 0, 0, 0,
        ];
        Assert.Equal(expectedOccluder, aux.Occluder.ToArray());
    }

    [Fact]
    public void BuildPaletteZoomFastPathCopiesRepeatedScreenRows()
    {
        ResidentChunkMap chunks = new();
        Chunk chunk = new(new ChunkCoord(0, 0));
        SetMaterial(chunk, 0, 0, 1);
        SetMaterial(chunk, 1, 0, 2);
        SetMaterial(chunk, 2, 0, 3);
        SetMaterial(chunk, 0, 1, 3);
        SetMaterial(chunk, 1, 1, 2);
        SetMaterial(chunk, 2, 1, 1);
        chunks.Add(chunk);
        CountingChunkSource counting = new(chunks);
        MaterialTable materials = Materials(
            Material(0, "empty", CellType.Empty, 0),
            Material(1, "sand", CellType.Powder, 0xFF101010u),
            Material(2, "stone", CellType.Solid, 0xFF202020u),
            Material(3, "glow", CellType.Powder, 0xFF303030u) with { PropertyFlags = MaterialProperty.Emissive });
        RenderBuffer target = new(6, 4);
        RenderAuxBuffers aux = new(6, 4);
        RenderFrameContext context = new(
            counting,
            materials,
            new TemperatureField(),
            new CameraState(0, 0, 0.5f, 6, 4),
            simStepped: true);

        new RenderBufferBuilder().Build(context, target, aux);

        Assert.Equal(
            [
                0xFF101010u, 0xFF101010u, 0xFF202020u, 0xFF202020u, 0xFF303030u, 0xFF303030u,
                0xFF101010u, 0xFF101010u, 0xFF202020u, 0xFF202020u, 0xFF303030u, 0xFF303030u,
                0xFF303030u, 0xFF303030u, 0xFF202020u, 0xFF202020u, 0xFF101010u, 0xFF101010u,
                0xFF303030u, 0xFF303030u, 0xFF202020u, 0xFF202020u, 0xFF101010u, 0xFF101010u,
            ],
            target.Pixels.ToArray());
        Assert.True(
            counting.TryGetChunkCalls <= 6,
            $"2x zoom 应只采样两个 world row 的三段水平 run，actual calls={counting.TryGetChunkCalls}。");
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
    public void BuildUsesDebugCellColorProviderWhenPresent()
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
        RenderFrameContext context = new(
            chunks,
            materials,
            new TemperatureField(),
            CameraState.OneToOne(0, 0, 1, 1),
            simStepped: true,
            new SolidDebugColorProvider(0xCC445566u));

        new RenderBufferBuilder().Build(context, target, aux);

        Assert.Equal(0xCC445566u, target.Pixels[0]);
    }

    [Fact]
    public void BuildRebuildsWhenDebugCellColorProviderIsPresentEvenWithoutSimStep()
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
        RenderFrameContext context = new(
            chunks,
            materials,
            new TemperatureField(),
            CameraState.OneToOne(0, 0, 1, 1),
            simStepped: false,
            new SolidDebugColorProvider(0xCC445566u));

        new RenderBufferBuilder().Build(context, target, aux);

        Assert.Equal(0xCC445566u, target.Pixels[0]);
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

    [Fact]
    public void BuildClearsBuffersForTransparentEmptyWorld()
    {
        ResidentChunkMap chunks = new();
        chunks.Add(new Chunk(new ChunkCoord(0, 0)));
        MaterialTable materials = Materials(Material(0, "empty", CellType.Empty, 0));
        RenderBuffer target = new(2, 2);
        RenderAuxBuffers aux = new(2, 2);
        target.Pixels.Fill(0xFFEEDDCCu);
        aux.Emissive.Fill(0xFF101010u);
        aux.Occluder.Fill(123);
        RenderFrameContext context = new(
            chunks,
            materials,
            new TemperatureField(),
            CameraState.OneToOne(0, 0, 2, 2),
            simStepped: true);

        new RenderBufferBuilder().Build(context, target, aux);

        Assert.All(target.Pixels.ToArray(), static value => Assert.Equal(0u, value));
        Assert.All(aux.Emissive.ToArray(), static value => Assert.Equal(0u, value));
        Assert.All(aux.Occluder.ToArray(), static value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public void BuildClearsTransparentEmptyWorldEvenWhenTemperatureBlocksExist()
    {
        ResidentChunkMap chunks = new();
        chunks.Add(new Chunk(new ChunkCoord(0, 0)));
        MaterialTable materials = Materials(Material(0, "empty", CellType.Empty, 0));
        TemperatureField temperature = new();
        temperature.AddHeat(0, 0, 1000f);
        RenderBuffer target = new(1, 1);
        RenderAuxBuffers aux = new(1, 1);
        target.Pixels[0] = 0xFFEEDDCCu;
        aux.Emissive[0] = 0xFF101010u;
        aux.Occluder[0] = 123;
        RenderFrameContext context = new(
            chunks,
            materials,
            temperature,
            CameraState.OneToOne(0, 0, 1, 1),
            simStepped: true);

        new RenderBufferBuilder().Build(context, target, aux);

        Assert.Equal(0u, target.Pixels[0]);
        Assert.Equal(0u, aux.Emissive[0]);
        Assert.Equal(0, aux.Occluder[0]);
    }

    [Fact]
    public void BuildInvalidatesEmptyWorldCacheWhenChunkBecomesDirty()
    {
        ResidentChunkMap chunks = new();
        Chunk chunk = new(new ChunkCoord(0, 0));
        chunks.Add(chunk);
        MaterialTable materials = Materials(
            Material(0, "empty", CellType.Empty, 0),
            Material(1, "sand", CellType.Powder, 0xFF112233u));
        RenderBuffer target = new(1, 1);
        RenderAuxBuffers aux = new(1, 1);
        RenderFrameContext empty = new(
            chunks,
            materials,
            new TemperatureField(),
            CameraState.OneToOne(0, 0, 1, 1),
            simStepped: true);
        RenderBufferBuilder builder = new();

        builder.Build(empty, target, aux);
        SetMaterial(chunk, 0, 0, 1);
        chunk.SetCurrentDirty(new DirtyRect(0, 0, 0, 0));
        RenderFrameContext dirty = new(
            chunks,
            materials,
            new TemperatureField(),
            CameraState.OneToOne(0, 0, 1, 1),
            simStepped: true);

        builder.Build(dirty, target, aux);

        Assert.Equal(0xFF112233u, target.Pixels[0]);
    }

    [Fact]
    public void BuildDoesNotClearWhenMaterialZeroIsVisible()
    {
        ResidentChunkMap chunks = new();
        chunks.Add(new Chunk(new ChunkCoord(0, 0)));
        MaterialTable materials = Materials(Material(0, "empty", CellType.Empty, 0xFF010203u));
        RenderBuffer target = new(1, 1);
        RenderAuxBuffers aux = new(1, 1);
        RenderFrameContext context = new(
            chunks,
            materials,
            new TemperatureField(),
            CameraState.OneToOne(0, 0, 1, 1),
            simStepped: true);

        new RenderBufferBuilder().Build(context, target, aux);

        Assert.Equal(0xFF010203u, target.Pixels[0]);
    }

    [Fact]
    public void BuildAppliesRenderStylesAndDamageWithoutMutatingCells()
    {
        ResidentChunkMap chunks = new();
        Chunk chunk = new(new ChunkCoord(0, 0));
        SetMaterial(chunk, 0, 0, 1);
        SetMaterial(chunk, 1, 0, 2);
        SetMaterial(chunk, 2, 0, 3);
        SetMaterial(chunk, 3, 0, 4);
        int damagedLocal = CellAddressing.LocalIndexFromLocal(0, 0);
        chunk.Damage[damagedLocal] = 50;
        chunks.Add(chunk);
        MaterialTable materials = Materials(
            Material(0, "empty", CellType.Empty, 0),
            Material(1, "stone", CellType.Solid, 0xFF404040u) with
            {
                Integrity = 100,
                RenderStyle = MaterialRenderStyle.Destructible,
                EdgeColorBGRA = 0xFFFFFFFFu,
            },
            Material(2, "water", CellType.Liquid, 0xFF001020u) with
            {
                RenderStyle = MaterialRenderStyle.Liquid,
                HighlightColorBGRA = 0xFF80C0FFu,
            },
            Material(3, "smoke", CellType.Gas, 0xFF808080u) with
            {
                RenderStyle = MaterialRenderStyle.Gas,
                Opacity = 128,
            },
            Material(4, "lava", CellType.Liquid, 0xFF101000u) with
            {
                RenderStyle = MaterialRenderStyle.Hazard,
                HighlightColorBGRA = 0xFFFF8000u,
            });
        RenderBuffer target = new(4, 1);
        RenderAuxBuffers aux = new(4, 1);
        RenderFrameContext context = new(
            chunks,
            materials,
            new TemperatureField(),
            CameraState.OneToOne(0, 0, 4, 1),
            simStepped: true,
            frameTimeSeconds: 0.5f);

        new RenderBufferBuilder().Build(context, target, aux);

        Assert.NotEqual(0xFF404040u, target.Pixels[0]);
        Assert.NotEqual(0xFF001020u, target.Pixels[1]);
        Assert.Equal(0x80404040u, target.Pixels[2]);
        Assert.NotEqual(0u, aux.Emissive[3]);
        Assert.Equal((ushort)1, chunk.Material[damagedLocal]);
        Assert.Equal(50, chunk.Damage[damagedLocal]);
    }

    [Fact]
    public void BuildStyleLevelOffFallsBackToBaseColor()
    {
        ResidentChunkMap chunks = new();
        Chunk chunk = new(new ChunkCoord(0, 0));
        SetMaterial(chunk, 0, 0, 1);
        chunk.Damage[0] = 200;
        chunks.Add(chunk);
        MaterialTable materials = Materials(
            Material(0, "empty", CellType.Empty, 0),
            Material(1, "stone", CellType.Solid, 0xFF404040u) with
            {
                Integrity = 100,
                RenderStyle = MaterialRenderStyle.Destructible,
                EdgeColorBGRA = 0xFFFFFFFFu,
            });
        RenderBuffer target = new(1, 1);
        RenderAuxBuffers aux = new(1, 1);
        RenderFrameContext context = new(
            chunks,
            materials,
            new TemperatureField(),
            CameraState.OneToOne(0, 0, 1, 1),
            simStepped: true);

        new RenderBufferBuilder(options: new RenderBufferBuilderOptions { StyleLevel = RenderBufferStyleLevel.Off })
            .Build(context, target, aux);

        Assert.Equal(0xFF404040u, target.Pixels[0]);
        Assert.Equal(200, chunk.Damage[0]);
    }

    [Fact]
    public void RenderStyleQualityGateControlsPaletteZoomFastPath()
    {
        ResidentChunkMap chunks = new();
        Chunk chunk = new(new ChunkCoord(0, 0));
        SetMaterial(chunk, 0, 0, 1);
        SetMaterial(chunk, 1, 0, 1);
        SetMaterial(chunk, 0, 1, 1);
        SetMaterial(chunk, 1, 1, 1);
        chunks.Add(chunk);
        CountingChunkSource counting = new(chunks);
        MaterialTable materials = Materials(
            Material(0, "empty", CellType.Empty, 0),
            Material(1, "stone", CellType.Solid, 0xFF404040u) with
            {
                RenderStyle = MaterialRenderStyle.Destructible,
                EdgeColorBGRA = 0xFFFFFFFFu,
            });
        RenderBuffer target = new(4, 4);
        RenderAuxBuffers aux = new(4, 4);
        RenderFrameContext context = new(
            counting,
            materials,
            new TemperatureField(),
            new CameraState(0, 0, 0.5f, 4, 4),
            simStepped: true);

        new RenderBufferBuilder(options: new RenderBufferBuilderOptions { StyleLevel = RenderBufferStyleLevel.Off })
            .Build(context, target, aux);
        int offCalls = counting.TryGetChunkCalls;

        counting.Reset();
        new RenderBufferBuilder().Build(context, target, aux);

        Assert.True(offCalls <= 4, $"StyleLevel.Off 应继续命中 zoom 行复制快路径，actual calls={offCalls}。");
        Assert.True(
            counting.TryGetChunkCalls > offCalls,
            $"StyleLevel.Full 且存在样式效果时应禁用 zoom 行复制快路径，off={offCalls}, full={counting.TryGetChunkCalls}。");
    }

    [Fact]
    public void MaterialSwatchProviderReturnsStableRepresentativeColor()
    {
        MaterialTable materials = Materials(
            Material(0, "empty", CellType.Empty, 0),
            Material(1, "crystal", CellType.Solid, 0xFF102030u) with
            {
                RenderStyle = MaterialRenderStyle.Emissive,
                HighlightColorBGRA = 0xFF90A0B0u,
            },
            Material(2, "sand", CellType.Powder, 0xFF405060u));

        Assert.Equal(0xFF90A0B0u, MaterialSwatchProvider.GetSwatch(materials, 1));
        Assert.Equal(0xFF405060u, MaterialSwatchProvider.GetSwatch(materials, 2));
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

    private static uint ExpectedColorNoise(uint bgra, byte amount, int worldX, int worldY)
    {
        uint hash = unchecked((uint)(worldX * 73856093) ^ (uint)(worldY * 19349663));
        int delta = ((int)(hash & 0xFF) - 128) * amount / 255;
        byte b = Adjust((byte)bgra, delta);
        byte g = Adjust((byte)(bgra >> 8), delta);
        byte r = Adjust((byte)(bgra >> 16), delta);
        byte a = (byte)(bgra >> 24);
        return b | ((uint)g << 8) | ((uint)r << 16) | ((uint)a << 24);
    }

    private static byte Adjust(byte value, int delta)
    {
        return (byte)Math.Clamp(value + delta, 0, 255);
    }

    private sealed class TestTextureProvider : IMaterialTextureProvider
    {
        public bool TrySample(in MaterialDef material, int worldX, int worldY, out uint bgra)
        {
            bgra = material.TextureId == 7 ? 0xFF445566u : 0;
            return material.TextureId == 7;
        }
    }

    private sealed class CountingChunkSource(IChunkSource inner) : IChunkSource
    {
        private readonly IChunkSource _inner = inner;

        public int TryGetChunkCalls { get; private set; }

        public void Reset()
        {
            TryGetChunkCalls = 0;
        }

        public ReadOnlySpan<Chunk> ResidentChunks => _inner.ResidentChunks;

        public bool TryGetChunk(ChunkCoord coord, out Chunk chunk)
        {
            TryGetChunkCalls++;
            return _inner.TryGetChunk(coord, out chunk);
        }

        public bool ResolveNeighborhood(ChunkCoord center, out ChunkNeighborhood neighborhood)
        {
            return _inner.ResolveNeighborhood(center, out neighborhood);
        }
    }

    private sealed class SolidDebugColorProvider(uint color) : IDebugCellColorProvider
    {
        public bool TryGetDebugColor(int worldX, int worldY, ushort materialId, byte flags, float temperatureCelsius, out uint colorBgra)
        {
            colorBgra = color;
            return true;
        }
    }
}
