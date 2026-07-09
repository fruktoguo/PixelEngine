using PixelEngine.Core.Diagnostics;
using PixelEngine.Core.Threading;
using PixelEngine.Simulation;
using PixelEngine.World;
using Xunit;

namespace PixelEngine.Rendering.Tests;

/// <summary>
/// 渲染缓冲构建器测试：覆盖世界到屏幕像素映射、调色板快路径、样式着色、缓存复用与性能门控。
/// </summary>
public sealed class RenderBufferBuilderTests
{
    /// <summary>
    /// 验证相机 1:1 映射下，屏幕像素正确对应世界单元格材质颜色，且非发光材质不写入 Emissive。
    /// </summary>
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

    /// <summary>
    /// 验证调色板快路径按区块行展开，同时写入 Emissive 与 Occluder 辅助缓冲。
    /// </summary>
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

    /// <summary>
    /// 验证快路径对带 ColorNoise 的材质应用与标量算法一致的世界坐标噪声。
    /// </summary>
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

    /// <summary>
    /// 验证 0.5x 缩放下快路径跨区块行重复采样单元格，并正确填充 Emissive/Occluder。
    /// </summary>
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

    /// <summary>
    /// 验证并行 Job 按行独立采样，避免跨任务复制导致行数据错乱。
    /// </summary>
    [Fact]
    public void BuildPaletteZoomFastPathSamplesRepeatedRowsWithoutCrossJobCopies()
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
        using JobSystem jobs = new(workerCount: 2);

        new RenderBufferBuilder(jobs: jobs, options: new RenderBufferBuilderOptions
        {
            MinRowsPerJob = 1,
        }).Build(context, target, aux);

        Assert.Equal(
            [
                0xFF101010u, 0xFF101010u, 0xFF202020u, 0xFF202020u, 0xFF303030u, 0xFF303030u,
                0xFF101010u, 0xFF101010u, 0xFF202020u, 0xFF202020u, 0xFF303030u, 0xFF303030u,
                0xFF303030u, 0xFF303030u, 0xFF202020u, 0xFF202020u, 0xFF101010u, 0xFF101010u,
                0xFF303030u, 0xFF303030u, 0xFF202020u, 0xFF202020u, 0xFF101010u, 0xFF101010u,
            ],
            target.Pixels.ToArray());
        Assert.True(
            counting.TryGetChunkCalls <= 12,
            $"2x zoom 并行路径应按屏幕行独立采样，避免跨任务复制旧行，actual calls={counting.TryGetChunkCalls}。");
    }

    /// <summary>
    /// 验证纹理提供器、温度发光叠加及 Emissive/Occluder 辅助输出。
    /// </summary>
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

    /// <summary>
    /// 验证构建过程只读模拟数据，不修改材质/标志/寿命字段。
    /// </summary>
    [Fact]
    public void BuildDoesNotMutateSimulationCells()
    {
        ResidentChunkMap chunks = new();
        Chunk chunk = new(new ChunkCoord(0, 0));
        SetMaterial(chunk, 1, 1, 1);
        chunk.FlagsBuffer[CellAddressing.LocalIndexFromLocal(1, 1)] = 0xAA;
        chunk.LifetimeBuffer[CellAddressing.LocalIndexFromLocal(1, 1)] = 9;
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
        Assert.Equal((ushort)1, chunk.MaterialBuffer[local]);
        Assert.Equal(0xAA, chunk.FlagsBuffer[local]);
        Assert.Equal(9, chunk.LifetimeBuffer[local]);
    }

    /// <summary>
    /// 验证存在 IDebugCellColorProvider 时优先使用调试色而非材质基色。
    /// </summary>
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

    /// <summary>
    /// 验证无模拟步进时，调试色提供器仍会触发缓冲重建。
    /// </summary>
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

    /// <summary>
    /// 验证模拟未步进时复用上一帧像素与辅助缓冲，避免无效重绘。
    /// </summary>
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

    /// <summary>
    /// 验证全空材质世界将像素与辅助缓冲清零为透明。
    /// </summary>
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

    /// <summary>
    /// 验证存在温度场但无可见材质时仍清空缓冲。
    /// </summary>
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

    /// <summary>
    /// 验证空世界缓存会在区块变脏后失效并反映新材质。
    /// </summary>
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

    /// <summary>
    /// 验证材质 0 若配置为可见色，则不会当作透明空世界清空。
    /// </summary>
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

    /// <summary>
    /// 验证可破坏/液体/气体/危险等渲染样式与损伤着色，且不写回模拟单元格。
    /// </summary>
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
        chunk.DamageBuffer[damagedLocal] = 50;
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
        Assert.Equal((ushort)1, chunk.MaterialBuffer[damagedLocal]);
        Assert.Equal(50, chunk.DamageBuffer[damagedLocal]);
    }

    /// <summary>
    /// 验证 StyleLevel.Off 时忽略样式效果，仅输出材质基色。
    /// </summary>
    [Fact]
    public void BuildStyleLevelOffFallsBackToBaseColor()
    {
        ResidentChunkMap chunks = new();
        Chunk chunk = new(new ChunkCoord(0, 0));
        SetMaterial(chunk, 0, 0, 1);
        chunk.DamageBuffer[0] = 200;
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
        Assert.Equal(200, chunk.DamageBuffer[0]);
    }

    /// <summary>
    /// 验证样式质量门控：Off 仍可走调色板缩放快路径，Full 有样式时禁用行复制快路径。
    /// </summary>
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

        Assert.True(offCalls <= 8, $"StyleLevel.Off 应继续命中 zoom palette 快路径，actual calls={offCalls}。");
        Assert.True(
            counting.TryGetChunkCalls > offCalls,
            $"StyleLevel.Full 且存在样式效果时应禁用 zoom 行复制快路径，off={offCalls}, full={counting.TryGetChunkCalls}。");
    }

    /// <summary>
    /// 验证未破损纯固体连续段走分段调色板路径，减少逐像素取区块次数。
    /// </summary>
    [Fact]
    public void BuildUsesStyledSegmentedPalettePathForUnbrokenSolidRuns()
    {
        ResidentChunkMap chunks = new();
        Chunk chunk = new(new ChunkCoord(0, 0));
        for (int x = 0; x < 32; x++)
        {
            SetMaterial(chunk, x, 0, 1);
        }

        chunks.Add(chunk);
        CountingChunkSource counting = new(chunks);
        MaterialTable materials = Materials(
            Material(0, "empty", CellType.Empty, 0),
            Material(1, "stone", CellType.Solid, 0xFF102030u) with
            {
                RenderStyle = MaterialRenderStyle.Solid,
            });
        RenderBuffer target = new(32, 1);
        RenderAuxBuffers aux = new(32, 1);
        RenderFrameContext context = new(
            counting,
            materials,
            new TemperatureField(),
            CameraState.OneToOne(0, 0, 32, 1),
            simStepped: true);

        new RenderBufferBuilder().Build(context, target, aux);

        Assert.True(
            counting.TryGetChunkCalls <= 2,
            $"未破损纯固体样式段应按 chunk row 分段构建，避免逐像素取 chunk，actual calls={counting.TryGetChunkCalls}。");
        Assert.All(target.Pixels.ToArray(), static value => Assert.Equal(0xFF102030u, value));
        Assert.All(aux.Occluder.ToArray(), static value => Assert.Equal(byte.MaxValue, value));
    }

    /// <summary>
    /// 验证混合材质连续段的分段路径与标量路径像素/aux 完全一致。
    /// </summary>
    [Fact]
    public void BuildStyledSegmentedPathMatchesScalarPathForMixedRuns()
    {
        ResidentChunkMap chunks = new();
        Chunk chunk = new(new ChunkCoord(0, 0));
        SetMaterial(chunk, 0, 0, 1);
        SetMaterial(chunk, 1, 0, 1);
        SetMaterial(chunk, 2, 0, 1);
        SetMaterial(chunk, 3, 0, 2);
        SetMaterial(chunk, 4, 0, 3);
        SetMaterial(chunk, 5, 0, 4);
        SetMaterial(chunk, 6, 0, 5);
        SetMaterial(chunk, 7, 0, 1);
        chunk.DamageBuffer[CellAddressing.LocalIndexFromLocal(2, 0)] = 40;
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
            Material(4, "sand", CellType.Powder, 0xFF604020u) with
            {
                RenderStyle = MaterialRenderStyle.Powder,
            },
            Material(5, "lava", CellType.Liquid, 0xFF101000u) with
            {
                RenderStyle = MaterialRenderStyle.Hazard,
                HighlightColorBGRA = 0xFFFF8000u,
            });
        RenderBuffer segmented = new(8, 1);
        RenderAuxBuffers segmentedAux = new(8, 1);
        RenderBuffer scalar = new(8, 1);
        RenderAuxBuffers scalarAux = new(8, 1);

        new RenderBufferBuilder().Build(
            new RenderFrameContext(
                chunks,
                materials,
                new TemperatureField(),
                CameraState.OneToOne(0, 0, 8, 1),
                simStepped: true,
                frameTimeSeconds: 0.25f),
            segmented,
            segmentedAux);
        new RenderBufferBuilder().Build(
            new RenderFrameContext(
                chunks,
                materials,
                new TemperatureField(),
                CameraState.OneToOne(0.25f, 0, 8, 1),
                simStepped: true,
                frameTimeSeconds: 0.25f),
            scalar,
            scalarAux);

        Assert.Equal(scalar.Pixels.ToArray(), segmented.Pixels.ToArray());
        Assert.Equal(scalarAux.Emissive.ToArray(), segmentedAux.Emissive.ToArray());
        Assert.Equal(scalarAux.Occluder.ToArray(), segmentedAux.Occluder.ToArray());
    }

    /// <summary>
    /// 验证 MaterialSwatchProvider 对发光/粉末材质返回稳定代表色。
    /// </summary>
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

    /// <summary>
    /// 验证仅当样式路径激活时 FrameProfiler 记录 RenderStyleShading 子阶段耗时。
    /// </summary>
    [Fact]
    public void BuildRecordsRenderStyleSubPhaseOnlyWhenStylePathIsActive()
    {
        ResidentChunkMap chunks = new();
        Chunk chunk = new(new ChunkCoord(0, 0));
        SetMaterial(chunk, 0, 0, 1);
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

        FrameProfiler fullProfiler = new();
        fullProfiler.BeginFrame();
        new RenderBufferBuilder().Build(context, target, aux, fullProfiler);
        fullProfiler.EndFrame();

        FrameProfiler offProfiler = new();
        offProfiler.BeginFrame();
        new RenderBufferBuilder(options: new RenderBufferBuilderOptions { StyleLevel = RenderBufferStyleLevel.Off })
            .Build(context, target, aux, offProfiler);
        offProfiler.EndFrame();

        Assert.True(fullProfiler.LastSubFrame[(int)FrameSubPhase.RenderStyleShading] > 0);
        Assert.Equal(0, offProfiler.LastSubFrame[(int)FrameSubPhase.RenderStyleShading]);
    }

    /// <summary>
    /// 验证可在运行时开关样式路径并影响 Profiler 子阶段计时。
    /// </summary>
    [Fact]
    public void RenderStyleQualityControllerTogglesStylePathAtRuntime()
    {
        ResidentChunkMap chunks = new();
        Chunk chunk = new(new ChunkCoord(0, 0));
        SetMaterial(chunk, 0, 0, 1);
        chunk.DamageBuffer[0] = 200;
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
        RenderBufferBuilder builder = new();

        FrameProfiler offProfiler = new();
        offProfiler.BeginFrame();
        builder.SetRenderStyleLevel(RenderBufferStyleLevel.Off);
        builder.Build(context, target, aux, offProfiler);
        offProfiler.EndFrame();

        FrameProfiler fullProfiler = new();
        fullProfiler.BeginFrame();
        builder.SetRenderStyleLevel(RenderBufferStyleLevel.Full);
        builder.Build(context, target, aux, fullProfiler);
        fullProfiler.EndFrame();

        Assert.Equal(RenderBufferStyleLevel.Full, builder.RenderStyleLevel);
        Assert.Equal(0, offProfiler.LastSubFrame[(int)FrameSubPhase.RenderStyleShading]);
        Assert.True(fullProfiler.LastSubFrame[(int)FrameSubPhase.RenderStyleShading] > 0);
    }

    private static void SetMaterial(Chunk chunk, int lx, int ly, ushort material)
    {
        chunk.MaterialBuffer[CellAddressing.LocalIndexFromLocal(lx, ly)] = material;
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
