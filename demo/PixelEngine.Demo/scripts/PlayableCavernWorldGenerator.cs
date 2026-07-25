using PixelEngine.Hosting;
using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 基于全局坐标的确定性流式战役地形生成器；生成自然地表、八个主路径 biome、七个 Holy Mountain 与无限侧区。
/// </summary>
public sealed class PlayableCavernWorldGenerator :
    IStreamingProceduralWorldGenerator,
    IWorldVisualLayerProvider,
    IViewportWorldVisualLayerProvider
{
    private static readonly ScriptAssetReference EntranceBackgroundAsset = new(
        ScriptAssetKind.Texture,
        "noita-mountain-left-entrance-background",
        "maps/noita/mountain/left_entrance_background.png");
    private static readonly ScriptAssetReference EntranceDecorationAsset = new(
        ScriptAssetKind.Texture,
        "noita-mountain-left-entrance-visual",
        "maps/noita/mountain/left_entrance_visual.png");
    private static readonly ScriptAssetReference EntranceBelowBackgroundAsset = new(
        ScriptAssetKind.Texture,
        "noita-mountain-left-entrance-below-background",
        "maps/noita/mountain/left_entrance_below_background.png");
    private static readonly ScriptAssetReference LeftStubBackgroundAsset = new(
        ScriptAssetKind.Texture,
        "noita-mountain-left-stub-background",
        "maps/noita/mountain/left_stub_background.png");
    private TerrainGenerationState? _state;
    private readonly NoitaWangMarkerAnchor[] _visualMarkerBuffer = new NoitaWangMarkerAnchor[512];
    private readonly NoitaVegetationPlacement[] _visualVegetationBuffer = new NoitaVegetationPlacement[2048];
    [ThreadStatic]
    private static NoitaWangMarkerAnchor[]? _chunkMarkerBuffer;
    [ThreadStatic]
    private static NoitaVegetationPlacement[]? _chunkVegetationBuffer;

    /// <summary>
    /// 程序化场景键，同时也是入口 Behaviour 的完整类型名。
    /// </summary>
    public const string Key = "PixelEngine.Demo.PlayableWorldDirector";

    /// <summary>
    /// 当前生成算法与 region 存档兼容身份；改变不兼容算法时必须升级。
    /// </summary>
    public const string PersistenceKey = "showcase-campaign-v15";

    /// <summary>
    /// 原点安全区的地表 Y。
    /// </summary>
    public const int SafeSurfaceY = 224;

    /// <summary>
    /// Noita 参考出生中心 X。
    /// </summary>
    public const float PlayerSpawnCenterX = 227f;

    /// <summary>
    /// Noita 参考出生中心相对地表的 Y 偏移。
    /// </summary>
    public const float PlayerSpawnCenterOffsetY = -85f;

    /// <summary>默认玩家碰撞 AABB 宽度。</summary>
    public const float PlayerCollisionWidth = 6f;

    /// <summary>默认玩家碰撞 AABB 高度。</summary>
    public const float PlayerCollisionHeight = 12f;

    /// <summary>传给角色控制器的默认出生左上角 X。</summary>
    public const float PlayerSpawnX = PlayerSpawnCenterX - (PlayerCollisionWidth * 0.5f);

    /// <summary>传给角色控制器的默认出生左上角 Y。</summary>
    public const float PlayerSpawnY = SafeSurfaceY + PlayerSpawnCenterOffsetY - (PlayerCollisionHeight * 0.5f);

    internal const ulong Seed = 0x5049_5845_4C53_4248;
    internal const int SeaLevelY = 242;
    private const long SafeInnerRadius = 112;
    private const long SafeOuterRadius = 320;
    private const int MaximumConnectionSegmentsPerRow = 16;
    private const int MaximumBiomeLandmarkSegmentsPerRow = 8;
    private const double Inverse53Bit = 1.0 / 9_007_199_254_740_992.0;

    /// <inheritdoc />
    public int WorldVisualLayerCount => 4 + NoitaPixelSceneVisualCatalog.LayerCount;

    /// <inheritdoc />
    public WorldVisualLayerDescriptor GetWorldVisualLayer(int index)
    {
        TerrainGenerationState? state = Volatile.Read(ref _state);
        float worldY = (state?.Config.SurfaceY ?? SafeSurfaceY) - 512f;
        return index switch
        {
            0 => new WorldVisualLayerDescriptor(
                EntranceBackgroundAsset,
                0f,
                worldY,
                512f,
                512f,
                WorldVisualLayerKind.Background),
            1 => new WorldVisualLayerDescriptor(
                EntranceBelowBackgroundAsset,
                0f,
                worldY + 512f,
                512f,
                512f,
                WorldVisualLayerKind.Background),
            2 => new WorldVisualLayerDescriptor(
                LeftStubBackgroundAsset,
                -512f,
                worldY + 512f,
                550f,
                512f,
                WorldVisualLayerKind.Background),
            3 => new WorldVisualLayerDescriptor(
                EntranceDecorationAsset,
                0f,
                worldY,
                512f,
                512f,
                WorldVisualLayerKind.Decoration),
            >= 4 when index < WorldVisualLayerCount => NoitaPixelSceneVisualCatalog.GetLayer(index - 4),
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
    }

    /// <inheritdoc />
    public int CollectWorldVisualLayers(
        float minimumWorldX,
        float minimumWorldY,
        float maximumWorldX,
        float maximumWorldY,
        Span<WorldVisualLayerDescriptor> destination)
    {
        TerrainGenerationState? state = Volatile.Read(ref _state);
        if (state is null || destination.IsEmpty)
        {
            return 0;
        }

        long minimumX = checked((long)MathF.Floor(minimumWorldX)) - 259;
        long minimumY = checked((long)MathF.Floor(minimumWorldY)) - 259;
        long maximumX = checked((long)MathF.Ceiling(maximumWorldX));
        long maximumY = checked((long)MathF.Ceiling(maximumWorldY));
        int count = CollectBiomeBackgroundLayers(
            state,
            minimumX,
            minimumY,
            maximumX,
            maximumY,
            destination);
        int markerCount = CollectWangMarkerAnchors(
            state.Biomes,
            state.WangTerrain,
            state.Config,
            state.WorldSeed,
            minimumX,
            minimumY,
            maximumX,
            maximumY,
            _visualMarkerBuffer);
        for (int i = 0; i < markerCount && count < destination.Length; i++)
        {
            ref readonly NoitaWangMarkerAnchor anchor = ref _visualMarkerBuffer[i];
            if (!NoitaSnowcastlePixelSceneCatalog.Supports(anchor) || !IsSnowcastlePixelSceneSafe(anchor.WorldX, anchor.WorldY))
            {
                continue;
            }

            int sceneIndex = NoitaSnowcastlePixelSceneCatalog.SelectSceneIndex(
                NoitaSnowcastlePixelSceneCatalog.ResolveMarkerFunction(in anchor)!,
                anchor.WorldX,
                anchor.WorldY,
                state.WorldSeed);
            if (sceneIndex < 0)
            {
                continue;
            }

            NoitaSnowcastlePixelSceneDefinition scene = state.SnowcastlePixelScenes.Scenes[sceneIndex].Definition;
            if (scene.BackgroundAsset.IsValid && count < destination.Length)
            {
                destination[count++] = new WorldVisualLayerDescriptor(
                    scene.BackgroundAsset,
                    anchor.WorldX,
                    anchor.WorldY,
                    scene.Width,
                    scene.Height,
                    WorldVisualLayerKind.Background);
            }
            if (scene.VisualAsset.IsValid && count < destination.Length)
            {
                destination[count++] = new WorldVisualLayerDescriptor(
                    scene.VisualAsset,
                    anchor.WorldX,
                    anchor.WorldY,
                    scene.Width,
                    scene.Height,
                    WorldVisualLayerKind.Decoration);
            }
        }

        count += CollectVegetationVisualLayers(
            state,
            minimumX,
            minimumY,
            maximumX,
            maximumY,
            destination[count..],
            _visualVegetationBuffer);

        return count;
    }

    private static int CollectBiomeBackgroundLayers(
        TerrainGenerationState state,
        long minimumX,
        long minimumY,
        long maximumX,
        long maximumY,
        Span<WorldVisualLayerDescriptor> destination)
    {
        const int macroCellSize = 512;
        int count = 0;
        long minimumMacroX = FloorDivide(minimumX, macroCellSize);
        long maximumMacroX = FloorDivide(maximumX, macroCellSize);
        long minimumMacroY = FloorDivide(minimumY - state.Config.SurfaceY, macroCellSize);
        long maximumMacroY = FloorDivide(maximumY - state.Config.SurfaceY, macroCellSize);
        for (long macroY = minimumMacroY; macroY <= maximumMacroY && count < destination.Length; macroY++)
        {
            for (long macroX = minimumMacroX; macroX <= maximumMacroX && count < destination.Length; macroX++)
            {
                long cellX = macroX * macroCellSize;
                long cellY = state.Config.SurfaceY + (macroY * macroCellSize);
                if (!TryGetBiomeBackground(state, cellX, cellY, out NoitaBiomeBackgroundDefinition background) ||
                    background.ImageAssetIndex < 0)
                {
                    continue;
                }

                ref readonly NoitaBiomeBackgroundAssetDefinition image =
                    ref NoitaBiomeBackgroundCatalog.Asset(background.ImageAssetIndex);
                int tilesX = Math.Max(1, (macroCellSize + image.Width - 1) / image.Width);
                int tilesY = Math.Max(1, (macroCellSize + image.Height - 1) / image.Height);
                for (int tileY = 0; tileY < tilesY && count < destination.Length; tileY++)
                {
                    for (int tileX = 0; tileX < tilesX && count < destination.Length; tileX++)
                    {
                        destination[count++] = new WorldVisualLayerDescriptor(
                            image.Asset,
                            cellX + (tileX * image.Width),
                            cellY + (tileY * image.Height),
                            image.Width,
                            image.Height,
                            WorldVisualLayerKind.Background);
                    }
                }

                count = CollectBiomeBackgroundEdges(
                    state,
                    in background,
                    cellX,
                    cellY,
                    destination,
                    count);
            }
        }

        return count;
    }

    private static int CollectBiomeBackgroundEdges(
        TerrainGenerationState state,
        in NoitaBiomeBackgroundDefinition background,
        long cellX,
        long cellY,
        Span<WorldVisualLayerDescriptor> destination,
        int count)
    {
        count = TryAddBiomeBackgroundEdge(
            state, in background, background.LeftAssetIndex, cellX - 64L, cellY - 64L,
            cellX - 1L, cellY + 256L, destination, count);
        count = TryAddBiomeBackgroundEdge(
            state, in background, background.RightAssetIndex, cellX + 512L, cellY - 64L,
            cellX + 512L, cellY + 256L, destination, count);
        count = TryAddBiomeBackgroundEdge(
            state, in background, background.TopAssetIndex, cellX - 64L, cellY - 64L,
            cellX + 256L, cellY - 1L, destination, count);
        return TryAddBiomeBackgroundEdge(
            state, in background, background.BottomAssetIndex, cellX - 64L, cellY + 512L,
            cellX + 256L, cellY + 512L, destination, count);
    }

    private static int TryAddBiomeBackgroundEdge(
        TerrainGenerationState state,
        in NoitaBiomeBackgroundDefinition background,
        int assetIndex,
        long worldX,
        long worldY,
        long neighborSampleX,
        long neighborSampleY,
        Span<WorldVisualLayerDescriptor> destination,
        int count)
    {
        if (assetIndex < 0 || count >= destination.Length)
        {
            return count;
        }

        bool neighborResolved = TryGetBiomeBackground(
            state,
            neighborSampleX,
            neighborSampleY,
            out NoitaBiomeBackgroundDefinition neighbor);
        if (neighborResolved &&
            neighbor.ImageAssetIndex == background.ImageAssetIndex &&
            neighbor.EdgePriority == background.EdgePriority)
        {
            return count;
        }
        if (neighborResolved && neighbor.EdgePriority > background.EdgePriority)
        {
            return count;
        }

        ref readonly NoitaBiomeBackgroundAssetDefinition asset = ref NoitaBiomeBackgroundCatalog.Asset(assetIndex);
        destination[count++] = new WorldVisualLayerDescriptor(
            asset.Asset,
            worldX,
            worldY,
            asset.Width,
            asset.Height,
            WorldVisualLayerKind.Background);
        return count;
    }

    private static bool TryGetBiomeBackground(
        TerrainGenerationState state,
        long worldX,
        long worldY,
        out NoitaBiomeBackgroundDefinition background)
    {
        CompiledTopologyCell topology = state.WorldTopology.Resolve(worldX, worldY - state.Config.SurfaceY);
        if (topology.ReferenceBiomeIndex < 0)
        {
            background = default;
            return false;
        }

        ref readonly CompiledReferenceBiome referenceBiome =
            ref state.WorldTopology.ReferenceBiome(topology.ReferenceBiomeIndex);
        return NoitaBiomeBackgroundCatalog.TryGet(referenceBiome.Id, out background);
    }

    /// <inheritdoc />
    public ProceduralWorldDescriptor Describe(in ProceduralWorldBuildRequest request)
    {
        TerrainGenerationState? currentState = Volatile.Read(ref _state);
        CampaignConfig config = request.Config is null
            ? currentState?.Config ?? CampaignConfig.BuiltinDefault
            : CampaignConfig.Load(request.Config);
        BiomeCatalog biomes = request.Config is null
            ? currentState?.Biomes ?? BiomeCatalog.BuiltinDefault
            : BiomeCatalog.Load(request.Config, config);
        NoitaWangTerrainCatalog wangTerrain = request.Config is null
            ? currentState?.WangTerrain ?? NoitaWangTerrainCatalog.BuiltinDefault
            : NoitaWangTerrainCatalog.Load(request.Config);
        ulong worldSeed = request.WorldSeedOverride ?? config.InitialRunSeed;
        if (request.Materials is not null)
        {
            Volatile.Write(ref _state, CreateGenerationState(request.Materials, config, biomes, wangTerrain, worldSeed));
        }

        return ProceduralWorldDescriptor.CreateInfinite(
            worldSeed,
            initialFocusX: (long)PlayerSpawnCenterX,
            initialFocusY: (long)(config.SurfaceY + PlayerSpawnCenterOffsetY),
            persistenceKey: PersistenceKey);
    }

    /// <inheritdoc />
    public void PopulateChunk(in ProceduralChunkBuildContext context)
    {
        TerrainGenerationState? state = Volatile.Read(ref _state);
        if (state is null)
        {
            TerrainGenerationState created = CreateGenerationState(
                context.Materials,
                CampaignConfig.BuiltinDefault,
                BiomeCatalog.BuiltinDefault,
                NoitaWangTerrainCatalog.BuiltinDefault,
                context.WorldSeed);
            state = Interlocked.CompareExchange(ref _state, created, null) ?? created;
        }

        PopulateChunkCore(
            state,
            context.WorldSeed,
            context.OriginCellX,
            context.OriginCellY,
            context.SizeCells,
            context.TemperatureSizeCells,
            context.MaterialCells,
            context.TemperatureCells);
    }

    internal static void PopulateChunkForVerification(
        IMaterialQuery materials,
        int chunkX,
        int chunkY,
        Span<ushort> materialCells,
        Span<Half> temperatureCells,
        ulong worldSeed = Seed,
        CampaignConfig? config = null,
        BiomeCatalog? biomes = null,
        NoitaWangTerrainCatalog? wangTerrain = null)
    {
        const int SizeCells = 64;
        const int TemperatureSizeCells = 16;
        if (materialCells.Length != SizeCells * SizeCells)
        {
            throw new ArgumentException("材质数组必须恰好容纳一个 64x64 chunk。", nameof(materialCells));
        }

        if (temperatureCells.Length != TemperatureSizeCells * TemperatureSizeCells)
        {
            throw new ArgumentException("温度数组必须恰好容纳一个 16x16 temperature chunk。", nameof(temperatureCells));
        }

        CampaignConfig resolvedConfig = config ?? CampaignConfig.BuiltinDefault;
        TerrainGenerationState state = CreateGenerationState(
            materials,
            resolvedConfig,
            biomes ?? BiomeCatalog.BuiltinDefault,
            wangTerrain ?? NoitaWangTerrainCatalog.BuiltinDefault,
            worldSeed);
        PopulateChunkCore(
            state,
            worldSeed,
            (long)chunkX * SizeCells,
            (long)chunkY * SizeCells,
            SizeCells,
            TemperatureSizeCells,
            materialCells,
            temperatureCells);
    }

    internal void PopulatePreparedChunkForBenchmark(
        int chunkX,
        int chunkY,
        Span<ushort> materialCells,
        Span<Half> temperatureCells,
        ulong worldSeed = Seed)
    {
        const int SizeCells = 64;
        const int TemperatureSizeCells = 16;
        if (materialCells.Length != SizeCells * SizeCells)
        {
            throw new ArgumentException("材质数组必须恰好容纳一个 64x64 chunk。", nameof(materialCells));
        }

        if (temperatureCells.Length != TemperatureSizeCells * TemperatureSizeCells)
        {
            throw new ArgumentException("温度数组必须恰好容纳一个 16x16 temperature chunk。", nameof(temperatureCells));
        }

        TerrainGenerationState state = Volatile.Read(ref _state) ??
            throw new InvalidOperationException("benchmark 前必须先调用 Describe 装配生成状态。");
        PopulateChunkCore(
            state,
            worldSeed,
            (long)chunkX * SizeCells,
            (long)chunkY * SizeCells,
            SizeCells,
            TemperatureSizeCells,
            materialCells,
            temperatureCells);
    }

    private static void PopulateChunkCore(
        TerrainGenerationState state,
        ulong worldSeed,
        long originCellX,
        long originCellY,
        int sizeCells,
        int temperatureSizeCells,
        Span<ushort> materialCells,
        Span<Half> temperatureCells)
    {
        CampaignConfig config = state.Config;
        TerrainMaterialPalette palette = state.Palette;
        Span<int> surfaces = stackalloc int[sizeCells];
        Span<int> soilDepths = stackalloc int[sizeCells];
        Span<double> moisture = stackalloc double[sizeCells];
        double mainPathOriginNoise = MainPathOriginNoise(worldSeed);
        Span<ConnectionRowSegment> connectionSegments =
            stackalloc ConnectionRowSegment[MaximumConnectionSegmentsPerRow];
        Span<BiomeLandmarkRowSegment> landmarkSegments =
            stackalloc BiomeLandmarkRowSegment[MaximumBiomeLandmarkSegmentsPerRow];
        for (int localX = 0; localX < sizeCells; localX++)
        {
            long worldX = originCellX + localX;
            surfaces[localX] = SurfaceYAt(worldX, worldSeed);
            moisture[localX] = MoistureAt(worldX, worldSeed);
            soilDepths[localX] = 5 + (int)Math.Round(
                (ValueNoise1D(worldX * 0.011, worldSeed ^ 0x5A17UL) + 1.0) * 2.5);
        }

        for (int localY = 0; localY < sizeCells; localY++)
        {
            long worldY = originCellY + localY;
            CampaignDepthLocation location = config.ResolveLocation(worldY);
            int regionIndex = Math.Clamp(location.RegionIndex, 0, CampaignConfig.RequiredRegionCount - 1);
            CompiledBiome biome = state.MainBiomes[regionIndex];
            int connectionSegmentCount = BuildConnectionRowSegments(
                state,
                worldY,
                connectionSegments);
            int landmarkSegmentCount = BuildBiomeLandmarkRowSegments(
                state,
                regionIndex,
                worldY,
                landmarkSegments);
            PortalRowContext portalRow = BuildPortalRowContext(
                state,
                location,
                regionIndex,
                worldY);
            long pathCenterX = location.DepthCells >= config.CampaignStartDepthCells
                ? MainPathCenterX(worldY, config, worldSeed, mainPathOriginNoise)
                : config.MainPathEntranceX;
            TerrainRowContext rowContext = new(
                worldSeed,
                state,
                location,
                pathCenterX,
                regionIndex,
                biome,
                palette,
                portalRow,
                landmarkSegments[..landmarkSegmentCount],
                connectionSegments[..connectionSegmentCount]);
            int row = localY * sizeCells;
            if (portalRow.Kind == PortalRowKind.None)
            {
                for (int localX = 0; localX < sizeCells; localX++)
                {
                    materialCells[row + localX] = SelectMaterial(
                        originCellX + localX,
                        worldY,
                        surfaces[localX],
                        soilDepths[localX],
                        moisture[localX],
                        in rowContext);
                }

                continue;
            }

            for (int localX = 0; localX < sizeCells; localX++)
            {
                long worldX = originCellX + localX;
                materialCells[row + localX] = TrySelectPortalTerrain(
                    worldX,
                    in rowContext,
                    out ushort portalTerrainMaterial)
                        ? portalTerrainMaterial
                        : SelectMaterial(
                            worldX,
                            worldY,
                            surfaces[localX],
                            soilDepths[localX],
                            moisture[localX],
                            in rowContext);
            }
        }

        ApplyVegetation(
            state,
            originCellX,
            originCellY,
            sizeCells,
            materialCells);
        ApplySnowcastlePixelScenes(
            state,
            worldSeed,
            originCellX,
            originCellY,
            sizeCells,
            materialCells);
        PopulateTemperature(
            materialCells,
            sizeCells,
            temperatureCells,
            temperatureSizeCells,
            originCellX,
            originCellY,
            state,
            in palette);
    }

    private static void ApplyVegetation(
        TerrainGenerationState state,
        long originCellX,
        long originCellY,
        int sizeCells,
        Span<ushort> materialCells)
    {
        long maximumX = originCellX + sizeCells - 1L;
        long maximumY = originCellY + sizeCells - 1L;
        NoitaVegetationPlacement[] placements = _chunkVegetationBuffer ??= new NoitaVegetationPlacement[2048];
        ReadOnlySpan<NoitaVegetationLayerDefinition> definitions = NoitaVegetationCatalog.Layers;
        for (int definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
        {
            ref readonly NoitaVegetationLayerDefinition definition = ref definitions[definitionIndex];
            if (!definition.Enabled || definition.IsVisual || definition.Material.Length == 0 ||
                !state.Vegetation.TryGetLayers(definition.BiomeId, out CompiledNoitaVegetationLayer[] layers))
            {
                continue;
            }

            CompiledNoitaVegetationLayer layer = layers[FindVegetationLayer(layers, definition.Ordinal)];
            int placementCount = CollectVegetationPlacements(
                state,
                in layer,
                originCellX - layer.MaximumWidth,
                originCellY - layer.MaximumHeight,
                maximumX + layer.MaximumWidth,
                maximumY + layer.MaximumHeight,
                placements);
            for (int placementIndex = 0; placementIndex < placementCount; placementIndex++)
            {
                StampVegetation(
                    state,
                    in layer,
                    in placements[placementIndex],
                    originCellX,
                    originCellY,
                    sizeCells,
                    materialCells);
            }
        }
    }

    private static int CollectVegetationVisualLayers(
        TerrainGenerationState state,
        long minimumX,
        long minimumY,
        long maximumX,
        long maximumY,
        Span<WorldVisualLayerDescriptor> destination,
        Span<NoitaVegetationPlacement> placements)
    {
        int count = 0;
        ReadOnlySpan<NoitaVegetationLayerDefinition> definitions = NoitaVegetationCatalog.Layers;
        for (int definitionIndex = 0; definitionIndex < definitions.Length && count < destination.Length; definitionIndex++)
        {
            ref readonly NoitaVegetationLayerDefinition definition = ref definitions[definitionIndex];
            if (!definition.Enabled || !definition.IsVisual || definition.VariantIndices.Length == 0 ||
                !state.Vegetation.TryGetLayers(definition.BiomeId, out CompiledNoitaVegetationLayer[] layers))
            {
                continue;
            }

            CompiledNoitaVegetationLayer layer = layers[FindVegetationLayer(layers, definition.Ordinal)];
            int placementCount = CollectVegetationPlacements(
                state,
                in layer,
                minimumX - layer.MaximumWidth,
                minimumY - layer.MaximumHeight,
                maximumX + layer.MaximumWidth,
                maximumY + layer.MaximumHeight,
                placements);
            for (int placementIndex = 0; placementIndex < placementCount && count < destination.Length; placementIndex++)
            {
                ref readonly NoitaVegetationPlacement placement = ref placements[placementIndex];
                ref readonly DecodedNoitaVegetationAsset asset = ref state.Vegetation.Assets[placement.VariantIndex];
                NoitaVegetationAssetDefinition assetDefinition = asset.Definition;
                ResolveVegetationRectangle(in layer, in assetDefinition, in placement, out float x, out float y);
                if (x + assetDefinition.Width < minimumX || y + assetDefinition.Height < minimumY ||
                    x > maximumX || y > maximumY)
                {
                    continue;
                }

                destination[count++] = new WorldVisualLayerDescriptor(
                    assetDefinition.Asset,
                    x,
                    y,
                    assetDefinition.Width,
                    assetDefinition.Height,
                    WorldVisualLayerKind.Decoration,
                    layer.Definition.VisualColor);
            }
        }

        return count;
    }

    private static int FindVegetationLayer(CompiledNoitaVegetationLayer[] layers, int ordinal)
    {
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i].Definition.Ordinal == ordinal)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"Noita vegetation layer {ordinal} 未编译。");
    }

    private static int CollectVegetationPlacements(
        TerrainGenerationState state,
        in CompiledNoitaVegetationLayer layer,
        long minimumX,
        long minimumY,
        long maximumX,
        long maximumY,
        Span<NoitaVegetationPlacement> destination)
    {
        int count = 0;
        int spacing = Math.Clamp((int)Math.Round(layer.Definition.TreeWidth), 4, 2048);
        long minimumGridX = FloorDivide(minimumX, spacing);
        long maximumGridX = FloorDivide(maximumX, spacing);
        long minimumGridY = FloorDivide(minimumY, spacing);
        long maximumGridY = FloorDivide(maximumY, spacing);
        ulong salt = StableIdSalt(layer.Definition.BiomeId) ^
            ((ulong)layer.Definition.Ordinal * 0x9E37_79B9_7F4A_7C15UL) ^
            BitConverter.DoubleToUInt64Bits(layer.Definition.RandomSeed);
        for (long gridY = minimumGridY; gridY <= maximumGridY; gridY++)
        {
            for (long gridX = minimumGridX; gridX <= maximumGridX; gridX++)
            {
                if (HashUnit(gridX, gridY, state.WorldSeed ^ salt) >= layer.Definition.Probability)
                {
                    continue;
                }

                long cellMinimumX = gridX * spacing;
                long cellMinimumY = gridY * spacing;
                int jitterRange = Math.Max(1, spacing);
                long anchorX = cellMinimumX + (long)(HashUnit(gridX, gridY, state.WorldSeed ^ salt ^ 0x4A49_5454_4552UL) * jitterRange);
                int startY = (int)(HashUnit(gridX, gridY, state.WorldSeed ^ salt ^ 0x5354_4152_5459UL) * jitterRange);
                for (int scan = 0; scan < spacing; scan++)
                {
                    long anchorY = cellMinimumY + ((startY + scan) % spacing);
                    if (!TryResolveVegetationBiome(state, anchorX, anchorY, out CompiledReferenceBiome referenceBiome) ||
                        !string.Equals(referenceBiome.Id, layer.Definition.BiomeId, StringComparison.Ordinal) ||
                        !IsVegetationOpenAt(state, anchorX, anchorY, in referenceBiome) ||
                        (layer.Definition.IsCeiling
                            ? IsVegetationOpenAt(state, anchorX, anchorY - 1L, in referenceBiome)
                            : IsVegetationOpenAt(state, anchorX, anchorY + 1L, in referenceBiome)))
                    {
                        continue;
                    }

                    int variantIndex = layer.Definition.VariantIndices.Length == 0
                        ? -1
                        : layer.Definition.VariantIndices[(int)(HashUnit(
                            gridX,
                            gridY,
                            state.WorldSeed ^ salt ^ 0x5641_5249_414E_54UL) * layer.Definition.VariantIndices.Length) %
                            layer.Definition.VariantIndices.Length];
                    if (count < destination.Length)
                    {
                        destination[count++] = new NoitaVegetationPlacement(anchorX, anchorY, variantIndex);
                    }
                    break;
                }
            }
        }

        return count;
    }

    private static bool TryResolveVegetationBiome(
        TerrainGenerationState state,
        long worldX,
        long worldY,
        out CompiledReferenceBiome referenceBiome)
    {
        CompiledTopologyCell topology = state.WorldTopology.Resolve(worldX, worldY - state.Config.SurfaceY);
        if (topology.Kind is not (CompiledTopologyCellKind.MainBiome or CompiledTopologyCellKind.SideBiome) ||
            topology.ReferenceBiomeIndex < 0)
        {
            referenceBiome = default;
            return false;
        }

        referenceBiome = state.WorldTopology.ReferenceBiome(topology.ReferenceBiomeIndex);
        return referenceBiome.WangTerrain is not null;
    }

    private static bool IsVegetationOpenAt(
        TerrainGenerationState state,
        long worldX,
        long worldY,
        in CompiledReferenceBiome referenceBiome)
    {
        if (NoitaPixelSceneCatalog.TrySample(worldX, worldY, out NoitaPixelSceneMaterial sceneMaterial))
        {
            return sceneMaterial == NoitaPixelSceneMaterial.Empty;
        }

        if (referenceBiome.BitmapCaves is null ||
            !referenceBiome.BitmapCaves.TrySample(
                worldX,
                worldY,
                state.WorldSeed,
                referenceBiome.Salt,
                out byte semantic))
        {
            semantic = referenceBiome.WangTerrain!.Sample(worldX, worldY, state.WorldSeed, referenceBiome.Salt);
        }

        return semantic == (byte)NoitaWangTerrainSemantic.Empty ||
            DecodedNoitaWangTerrainSet.IsMarker(semantic) ||
            (semantic == (byte)NoitaWangTerrainSemantic.RandomBinary &&
                !DecodedNoitaWangTerrainSet.IsRandomBinarySolid(worldX, worldY, state.WorldSeed, referenceBiome.Salt));
    }

    private static void StampVegetation(
        TerrainGenerationState state,
        in CompiledNoitaVegetationLayer layer,
        in NoitaVegetationPlacement placement,
        long originCellX,
        long originCellY,
        int sizeCells,
        Span<ushort> materialCells)
    {
        if (placement.VariantIndex < 0)
        {
            long worldY = placement.AnchorY + (long)Math.Round(layer.Definition.ExtraY);
            StampVegetationCell(
                placement.AnchorX,
                worldY,
                layer.MaterialId,
                state.Palette.Empty,
                originCellX,
                originCellY,
                sizeCells,
                materialCells);
            return;
        }

        ref readonly DecodedNoitaVegetationAsset asset = ref state.Vegetation.Assets[placement.VariantIndex];
        NoitaVegetationAssetDefinition assetDefinition = asset.Definition;
        ResolveVegetationRectangle(in layer, in assetDefinition, in placement, out float x, out float y);
        long worldOriginX = (long)MathF.Round(x);
        long worldOriginY = (long)MathF.Round(y);
        for (int assetY = 0; assetY < asset.Definition.Height; assetY++)
        {
            int sourceRow = assetY * asset.Definition.Width;
            for (int assetX = 0; assetX < asset.Definition.Width; assetX++)
            {
                if (asset.Mask[sourceRow + assetX] == 0)
                {
                    continue;
                }

                StampVegetationCell(
                    worldOriginX + assetX,
                    worldOriginY + assetY,
                    layer.MaterialId,
                    state.Palette.Empty,
                    originCellX,
                    originCellY,
                    sizeCells,
                    materialCells);
            }
        }
    }

    private static void ResolveVegetationRectangle(
        in CompiledNoitaVegetationLayer layer,
        in NoitaVegetationAssetDefinition asset,
        in NoitaVegetationPlacement placement,
        out float x,
        out float y)
    {
        x = placement.AnchorX - asset.OffsetX + (float)layer.Definition.VisualOffsetX;
        y = placement.AnchorY - asset.OffsetY + (float)(layer.Definition.ExtraY + layer.Definition.VisualOffsetY);
    }

    private static void StampVegetationCell(
        long worldX,
        long worldY,
        ushort material,
        ushort empty,
        long originCellX,
        long originCellY,
        int sizeCells,
        Span<ushort> materialCells)
    {
        long localX = worldX - originCellX;
        long localY = worldY - originCellY;
        if ((ulong)localX >= (uint)sizeCells || (ulong)localY >= (uint)sizeCells)
        {
            return;
        }

        int index = checked(((int)localY * sizeCells) + (int)localX);
        if (materialCells[index] == empty)
        {
            materialCells[index] = material;
        }
    }

    private static void ApplySnowcastlePixelScenes(
        TerrainGenerationState state,
        ulong worldSeed,
        long originCellX,
        long originCellY,
        int sizeCells,
        Span<ushort> materialCells)
    {
        NoitaWangMarkerAnchor[] markerBuffer = _chunkMarkerBuffer ??= new NoitaWangMarkerAnchor[512];
        int markerCount = CollectWangMarkerAnchors(
            state.Biomes,
            state.WangTerrain,
            state.Config,
            worldSeed,
            originCellX - 259,
            originCellY - 259,
            originCellX + sizeCells - 1L,
            originCellY + sizeCells - 1L,
            markerBuffer);
        long chunkMaximumX = originCellX + sizeCells - 1L;
        long chunkMaximumY = originCellY + sizeCells - 1L;
        for (int markerIndex = 0; markerIndex < markerCount; markerIndex++)
        {
            ref readonly NoitaWangMarkerAnchor anchor = ref markerBuffer[markerIndex];
            if (!NoitaSnowcastlePixelSceneCatalog.Supports(anchor) || !IsSnowcastlePixelSceneSafe(anchor.WorldX, anchor.WorldY))
            {
                continue;
            }

            int sceneIndex = NoitaSnowcastlePixelSceneCatalog.SelectSceneIndex(
                NoitaSnowcastlePixelSceneCatalog.ResolveMarkerFunction(in anchor)!,
                anchor.WorldX,
                anchor.WorldY,
                worldSeed);
            if (sceneIndex < 0)
            {
                continue;
            }

            DecodedNoitaSnowcastlePixelScene scene = state.SnowcastlePixelScenes.Scenes[sceneIndex];
            long sceneMaximumX = anchor.WorldX + scene.Definition.Width - 1L;
            long sceneMaximumY = anchor.WorldY + scene.Definition.Height - 1L;
            if (sceneMaximumX < originCellX || sceneMaximumY < originCellY ||
                anchor.WorldX > chunkMaximumX || anchor.WorldY > chunkMaximumY)
            {
                continue;
            }

            int minimumLocalX = (int)Math.Max(0L, originCellX - anchor.WorldX);
            int minimumLocalY = (int)Math.Max(0L, originCellY - anchor.WorldY);
            int maximumLocalX = (int)Math.Min(scene.Definition.Width - 1L, chunkMaximumX - anchor.WorldX);
            int maximumLocalY = (int)Math.Min(scene.Definition.Height - 1L, chunkMaximumY - anchor.WorldY);
            for (int sceneY = minimumLocalY; sceneY <= maximumLocalY; sceneY++)
            {
                long worldY = anchor.WorldY + sceneY;
                int destinationRow = checked((int)(worldY - originCellY)) * sizeCells;
                int sourceRow = sceneY * scene.Definition.Width;
                for (int sceneX = minimumLocalX; sceneX <= maximumLocalX; sceneX++)
                {
                    byte materialIndex = scene.MaterialIndices[sourceRow + sceneX];
                    if (materialIndex == 0)
                    {
                        continue;
                    }

                    long worldX = anchor.WorldX + sceneX;
                    if (NoitaPixelSceneCatalog.TrySample(worldX, worldY, out _))
                    {
                        continue;
                    }

                    materialCells[destinationRow + checked((int)(worldX - originCellX))] =
                        state.SnowcastlePixelScenes.MaterialIds[materialIndex];
                }
            }
        }
    }

    private static bool IsSnowcastlePixelSceneSafe(long worldX, long worldY)
    {
        return !(worldX is >= 125 and <= 249 && worldY is >= 5118 and <= 5259) && worldY <= 6100;
    }

    internal static double PixelSceneRandomUnit(long worldX, long worldY, ulong worldSeed)
    {
        return HashUnit(worldX, worldY, worldSeed ^ 0x534E_4F57_4341_5354UL);
    }

    /// <summary>
    /// 返回任意全局 X 的确定性地表高度，供预览和自动化验证复用。
    /// </summary>
    internal static int SurfaceYAt(long worldX)
    {
        return SurfaceYAt(worldX, Seed);
    }

    internal static int SurfaceYAt(long worldX, ulong worldSeed)
    {
        double x = worldX;
        double warp = Fractal1D(x * 0.00072, worldSeed ^ 0xA11CEUL, 3) * 260.0;
        double warpedX = x + warp;
        double continental = Fractal1D(warpedX * 0.00048, worldSeed ^ 0xC0171E17UL, 5);
        double mountainRegion = SmoothStep(
            -0.18,
            0.58,
            Fractal1D((warpedX - 7_900.0) * 0.00023, worldSeed ^ 0xBADC0FFEEUL, 4));
        double ridgeBase = 1.0 - Math.Abs(Fractal1D(warpedX * 0.00185, worldSeed ^ 0x718D6EUL, 5));
        double ridges = ridgeBase * ridgeBase * ridgeBase;
        double basin = SmoothStep(
            0.28,
            0.72,
            Fractal1D((warpedX + 13_700.0) * 0.00039, worldSeed ^ 0xBA51AUL, 4));
        double hills = (Fractal1D(warpedX * 0.0048, worldSeed ^ 0x41115UL, 4) * 25.0) +
            (Fractal1D(warpedX * 0.016, worldSeed ^ 0x5CA1EUL, 2) * 6.0);
        double naturalSurface = 214.0 +
            (continental * 42.0) +
            (basin * 82.0) -
            (mountainRegion * ridges * 142.0) +
            hills;

        double distance = Math.Abs((double)worldX);
        double naturalBlend = SmoothStep(SafeInnerRadius, SafeOuterRadius, distance);
        double surface = Lerp(SafeSurfaceY, naturalSurface, naturalBlend);
        return Math.Clamp((int)Math.Round(surface), 54, 348);
    }

    /// <summary>
    /// 返回指定全局 cell 在初始世界中是否为洞穴空腔。
    /// </summary>
    internal static bool IsCaveAt(long worldX, long worldY, int surfaceY)
    {
        return IsCaveAt(worldX, worldY, surfaceY, Seed);
    }

    internal static bool IsCaveAt(long worldX, long worldY, int surfaceY, ulong worldSeed)
    {
        long depth = worldY - surfaceY;
        if (depth < 24 || (Math.Abs((double)worldX) < SafeOuterRadius && depth < 104))
        {
            return false;
        }

        double broad = Fractal2D(worldX * 0.0125, worldY * 0.0145, worldSeed ^ 0xCA7EUL, 3);
        double tunnel = Math.Abs(Fractal2D(worldX * 0.0062, worldY * 0.0091, worldSeed ^ 0x71A9E1UL, 2));
        double threshold = depth < 64 ? 0.73 : depth < 180 ? 0.62 : 0.57;
        return broad - (tunnel * 0.24) > threshold;
    }

    private static ushort SelectMaterial(
        long worldX,
        long worldY,
        int surfaceY,
        int soilDepth,
        double moisture,
        in TerrainRowContext row)
    {
        CampaignConfig config = row.State.Config;
        long topologyDepthCells = worldY - config.SurfaceY;
        CompiledTopologyCell topologyCell = row.State.WorldTopology.Resolve(
            worldX,
            topologyDepthCells);

        if (topologyCell.Kind != CompiledTopologyCellKind.Legacy)
        {
            return topologyCell.Kind == CompiledTopologyCellKind.HolyMountain &&
                row.Location.Kind == CampaignDepthKind.HolyMountain &&
                TrySelectHolyMountainMaterial(worldX, in row, out ushort holyMountainMaterial)
                    ? holyMountainMaterial
                    : TrySelectConnectionMaterial(
                        worldX,
                        worldY,
                        sideBiomeOnly: false,
                        in row,
                        out ushort connectionMaterial)
                            ? connectionMaterial
                            : !row.BiomeLandmarkSegments.IsEmpty &&
                                TrySelectBiomeLandmarkMaterial(worldX, in row, out ushort landmarkMaterial)
                                    ? landmarkMaterial
                                    : TrySelectConnectionMaterial(
                                        worldX,
                                        worldY,
                                        sideBiomeOnly: true,
                                        in row,
                                        out connectionMaterial)
                                            ? connectionMaterial
                                            : SelectTopologyMaterial(
                                                worldX,
                                                worldY,
                                                topologyDepthCells,
                                                protectedSpawn: false,
                                                topologyCell,
                                                in row);
        }

        long depth = worldY - surfaceY;
        if (depth < 0)
        {
            bool lakeColumn = surfaceY > SeaLevelY + 3;
            return lakeColumn && worldY >= SeaLevelY && Math.Abs((double)worldX) >= SafeInnerRadius
                ? row.Palette.Water
                : row.Palette.Empty;
        }

        if (depth <= soilDepth)
        {
            return surfaceY < 108
                ? row.Palette.Ice
                : surfaceY >= SeaLevelY - 5 || moisture < -0.28
                    ? row.Palette.PackedSand
                    : row.Palette.PackedDirt;
        }

        if (depth <= soilDepth + 7)
        {
            return moisture < -0.5 ? row.Palette.PackedSand : row.Palette.PackedDirt;
        }

        bool protectedSpawn = Math.Abs((double)worldX) < SafeOuterRadius && depth < 160;
        return SelectBiomeMaterial(worldX, worldY, protectedSpawn, row.Biome, in row);
    }

    private static ushort SelectTopologyMaterial(
        long worldX,
        long worldY,
        long topologyDepthCells,
        bool protectedSpawn,
        in CompiledTopologyCell topologyCell,
        in TerrainRowContext row)
    {
        TerrainGenerationState state = row.State;
        TerrainMaterialPalette palette = row.Palette;
        ref readonly CompiledReferenceBiome referenceBiome =
            ref state.WorldTopology.ReferenceBiome(topologyCell.ReferenceBiomeIndex);
        return TrySelectGlobalPixelSceneMaterial(worldX, worldY, in row, out ushort globalSceneMaterial)
            ? globalSceneMaterial
            : topologyCell.Kind is not (CompiledTopologyCellKind.MainBiome or CompiledTopologyCellKind.SideBiome) &&
                referenceBiome.WangTerrain is not null
            ? SelectReferenceWangBiomeMaterial(
                worldX,
                worldY,
                protectedSpawn: false,
                referenceBiome,
                in row)
            : topologyCell.Kind switch
            {
                CompiledTopologyCellKind.MainBiome => SelectWangBiomeMaterial(
                    worldX,
                    worldY,
                    protectedSpawn,
                    state.MainBiomes[topologyCell.BiomeIndex],
                    state.WorldTopology.ReferenceBiome(topologyCell.ReferenceBiomeIndex),
                    in row),
                CompiledTopologyCellKind.SideBiome => SelectWangBiomeMaterial(
                    worldX,
                    worldY,
                    protectedSpawn: false,
                    state.SideBiomes[topologyCell.BiomeIndex],
                    state.WorldTopology.ReferenceBiome(topologyCell.ReferenceBiomeIndex),
                    in row),
                CompiledTopologyCellKind.Solid => SelectReferenceSolidMaterial(
                    worldX,
                    worldY,
                    referenceBiome,
                    palette.BoundaryStone,
                    in row),
                CompiledTopologyCellKind.Lava => palette.Lava,
                CompiledTopologyCellKind.HolyMountain => SelectHolyMountainReferenceMaterial(
                    worldX,
                    topologyDepthCells,
                    state.WorldTopology.ReferenceBiome(topologyCell.ReferenceBiomeIndex),
                    in palette),
                CompiledTopologyCellKind.Empty => palette.Empty,
                CompiledTopologyCellKind.Water => SelectReferenceSolidMaterial(
                    worldX,
                    worldY,
                    referenceBiome,
                    palette.Water,
                    in row),
                CompiledTopologyCellKind.Clouds => SelectCloudMaterial(
                    worldX,
                    worldY,
                    state.WorldTopology.ReferenceBiome(topologyCell.ReferenceBiomeIndex),
                    in palette),
                CompiledTopologyCellKind.SurfaceHills => SelectReferenceCaveMaterial(
                    worldX,
                    worldY,
                    state.WorldTopology.ReferenceBiome(topologyCell.ReferenceBiomeIndex),
                    palette.PackedDirt,
                    palette.Stone,
                    palette.PackedGravel,
                    in palette,
                    in row),
                CompiledTopologyCellKind.SurfaceDesert => SelectReferenceCaveMaterial(
                    worldX,
                    worldY,
                    state.WorldTopology.ReferenceBiome(topologyCell.ReferenceBiomeIndex),
                    palette.PackedSand,
                    palette.Stone,
                    palette.PackedGravel,
                    in palette,
                    in row),
                CompiledTopologyCellKind.SurfaceWinter => SelectReferenceCaveMaterial(
                    worldX,
                    worldY,
                    state.WorldTopology.ReferenceBiome(topologyCell.ReferenceBiomeIndex),
                    palette.Ice,
                    palette.Stone,
                    palette.PackedGravel,
                    in palette,
                    in row),
                CompiledTopologyCellKind.Mountain => SelectMountainMaterial(
                    worldX,
                    topologyDepthCells,
                    state.WorldTopology.ReferenceBiome(topologyCell.ReferenceBiomeIndex),
                    in palette,
                    in row),
                CompiledTopologyCellKind.GenericCave => SelectReferenceCaveMaterial(
                    worldX,
                    worldY,
                    state.WorldTopology.ReferenceBiome(topologyCell.ReferenceBiomeIndex),
                    palette.Stone,
                    palette.PackedGravel,
                    palette.Crystal,
                    in palette,
                    in row),
                CompiledTopologyCellKind.GenericStructure => SelectReferenceStructureMaterial(
                    worldX,
                    worldY,
                    topologyDepthCells,
                    state.WorldTopology.ReferenceBiome(topologyCell.ReferenceBiomeIndex),
                    in palette,
                    in row),
                CompiledTopologyCellKind.FixedLaboratory => SelectFixedLaboratoryMaterial(
                    worldX,
                    topologyDepthCells,
                    state.WorldTopology,
                    state.MainBiomes[CampaignConfig.RequiredRegionCount - 1],
                    in palette),
                CompiledTopologyCellKind.Legacy => SelectBiomeMaterial(
                    worldX,
                    worldY,
                    protectedSpawn,
                    row.Biome,
                    in row),
                _ => throw new InvalidOperationException($"未知 world topology kind：{topologyCell.Kind}。"),
            };
    }

    private static ushort SelectCloudMaterial(
        long worldX,
        long worldY,
        in CompiledReferenceBiome biome,
        in TerrainMaterialPalette palette)
    {
        double cloud = Fractal2D(
            worldX * 0.0065,
            worldY * 0.008,
            biome.Salt ^ 0xC10D_5EEDUL,
            3);
        return cloud > 0.76 ? palette.Water : palette.Empty;
    }

    private static ushort SelectReferenceCaveMaterial(
        long worldX,
        long worldY,
        in CompiledReferenceBiome biome,
        ushort primary,
        ushort secondary,
        ushort accent,
        in TerrainMaterialPalette palette,
        in TerrainRowContext row)
    {
        double chambers = Fractal2D(
            worldX * 0.0075,
            worldY * 0.0085,
            biome.Salt ^ 0xCA7E_512UL,
            4);
        double tunnels = Math.Abs(Fractal2D(
            worldX * 0.0035,
            worldY * 0.0045,
            biome.Salt ^ 0x71A9_512UL,
            2));
        if (chambers - (tunnels * 0.22) > 0.18)
        {
            return palette.Empty;
        }

        double strata = Fractal2D(
            worldX * 0.018,
            worldY * 0.021,
            biome.Salt ^ 0x57A7_AUL,
            2);
        ushort fallbackMaterial = strata > 0.52
            ? accent
            : strata < -0.44
                ? secondary
                : primary;
        if (biome.WangMaterialLayers.Length == 0)
        {
            return fallbackMaterial;
        }

        byte density = (byte)Math.Clamp((int)Math.Round(((strata * 0.5) + 0.5) * byte.MaxValue), 0, byte.MaxValue);
        return SelectWangMaterialLayer(worldX, worldY, density, biome, in row, fallbackMaterial);
    }

    private static ushort SelectReferenceStructureMaterial(
        long worldX,
        long worldY,
        long depthCells,
        in CompiledReferenceBiome biome,
        in TerrainMaterialPalette palette,
        in TerrainRowContext row)
    {
        int localX = FloorRemainder(worldX, 64);
        int localY = FloorRemainder(depthCells, 64);
        bool room = localX is >= 8 and < 56 && localY is >= 8 and < 56;
        bool doorway = (localX is >= 27 and < 37 && (localY < 12 || localY >= 52)) ||
            (localY is >= 27 and < 37 && (localX < 12 || localX >= 52));
        if (room || doorway)
        {
            double debris = HashUnit(
                FloorDivide(worldX, 8),
                FloorDivide(worldY, 8),
                biome.Salt ^ 0x57A0_C7UL);
            return debris > 0.94 ? palette.Crystal : palette.Empty;
        }

        ushort fallbackMaterial = localX < 4 || localY < 4 ? palette.BoundaryStone : palette.Stone;
        return SelectReferenceSolidMaterial(worldX, worldY, biome, fallbackMaterial, in row);
    }

    private static ushort SelectReferenceSolidMaterial(
        long worldX,
        long worldY,
        in CompiledReferenceBiome biome,
        ushort fallbackMaterial,
        in TerrainRowContext row)
    {
        if (biome.WangMaterialLayers.Length == 0)
        {
            return fallbackMaterial;
        }

        float field = NormalizedFractal2D(
            worldX * 0.008,
            worldY * 0.008,
            row.WorldSeed ^ biome.Salt ^ 0x4D41_5445_5249_414CUL,
            4);
        byte density = (byte)Math.Clamp((int)Math.Round(field * byte.MaxValue), 0, byte.MaxValue);
        return SelectWangMaterialLayer(worldX, worldY, density, biome, in row, fallbackMaterial);
    }

    private static ushort SelectMountainMaterial(
        long worldX,
        long depthCells,
        in CompiledReferenceBiome biome,
        in TerrainMaterialPalette palette,
        in TerrainRowContext row)
    {
        int localX = FloorRemainder(worldX, 512);
        int localY = FloorRemainder(depthCells, 512);
        if (biome.TerrainMask.Length > 0)
        {
            return SelectReferenceTerrainMaskMaterial(localX, localY, biome, in palette);
        }

        switch (biome.Mountain)
        {
            case ReferenceMountainKind.LeftEntrance:
                {
                    int groundY = 438 + (int)Math.Round(
                        Fractal1D(worldX * 0.015, biome.Salt ^ 0x6A0DUL, 2) * 7.0);
                    bool entrancePassage = localX >= 342 && localY is >= 286 and < 452;
                    bool dropShaft = localX >= 452 && localY >= 394;
                    if (entrancePassage || dropShaft)
                    {
                        return palette.Empty;
                    }

                    bool rightCliff = localX >= 334 && localY < 350 - ((localX - 334) / 3);
                    return rightCliff
                        ? palette.Stone
                        : localY >= groundY
                            ? localY < groundY + 10 ? palette.PackedDirt : palette.Stone
                            : palette.Empty;
                }
            case ReferenceMountainKind.Hall:
                {
                    bool upperRoom = localX is >= 8 and < 504 && localY is >= 230 and < 438;
                    bool lowerShaft = localX is >= 64 and < 160 && localY >= 392;
                    bool mineEntrance = localX is >= 196 and < 348 && localY >= 416;
                    if (upperRoom || lowerShaft || mineEntrance)
                    {
                        bool platform = localY is >= 394 and < 402 && localX is >= 168 and < 404;
                        return platform ? palette.Stone : palette.Empty;
                    }

                    return palette.BoundaryStone;
                }
            case ReferenceMountainKind.Right:
                {
                    bool hallExit = localX < 96 && localY is >= 248 and < 420;
                    bool lowerGallery = localY is >= 402 and < 466 && localX < 384;
                    return hallExit || lowerGallery ? palette.Empty : palette.Stone;
                }
            case ReferenceMountainKind.Top:
                {
                    int distance = Math.Abs(localX - 256);
                    int surface = Math.Clamp(54 + (distance * 7 / 5), 54, 470);
                    return localY >= surface ? palette.Stone : palette.Empty;
                }
            case ReferenceMountainKind.LeftStub:
                return localX >= 360 && localY >= 120 + ((511 - localX) / 2)
                    ? palette.Stone
                    : palette.Empty;
            case ReferenceMountainKind.RightStub:
                return localX < 152 && localY >= 120 + (localX / 2)
                    ? palette.Stone
                    : palette.Empty;
            case ReferenceMountainKind.FloatingIsland:
                {
                    long dx = localX - 256L;
                    long dy = localY - 300L;
                    return ((dx * dx * 4L) + (dy * dy * 16L)) <= 150L * 150L * 4L
                        ? palette.Stone
                        : palette.Empty;
                }
            case ReferenceMountainKind.Lake:
                return localY >= 250 ? palette.Water : palette.Empty;
            case ReferenceMountainKind.Generic:
                return SelectReferenceCaveMaterial(
                    worldX,
                    depthCells,
                    biome,
                    palette.PackedDirt,
                    palette.Stone,
                    palette.PackedGravel,
                    in palette,
                    in row);
            default:
                throw new InvalidOperationException($"未知的参考山体类型：{biome.Mountain}。");
        }
    }

    private static ushort SelectHolyMountainReferenceMaterial(
        long worldX,
        long depthCells,
        in CompiledReferenceBiome biome,
        in TerrainMaterialPalette palette)
    {
        return biome.TerrainMask.Length == 0
            ? palette.BoundaryStone
            : SelectReferenceTerrainMaskMaterial(
                FloorRemainder(worldX, 512),
                FloorRemainder(depthCells, 512),
                biome,
                in palette);
    }

    private static ushort SelectReferenceTerrainMaskMaterial(
        int localX,
        int localY,
        in CompiledReferenceBiome biome,
        in TerrainMaterialPalette palette)
    {
        int pixelIndex = (localY * 512) + localX;
        int maskKind = (biome.TerrainMask[pixelIndex >> 2] >> ((pixelIndex & 3) * 2)) & 3;
        return maskKind switch
        {
            0 => palette.Empty,
            1 => palette.PackedDirt,
            2 => palette.BoundaryStone,
            3 => biome.MaskAccent switch
            {
                ReferenceTerrainMaskAccent.Water => palette.Water,
                ReferenceTerrainMaskAccent.Ice => palette.Ice,
                ReferenceTerrainMaskAccent.Gravel => palette.PackedGravel,
                _ => throw new InvalidOperationException($"未知的参考地形掩码强调材质：{biome.MaskAccent}。"),
            },
            _ => throw new InvalidOperationException($"未知的参考地形掩码值：{maskKind}。"),
        };
    }

    private static ushort SelectFixedLaboratoryMaterial(
        long worldX,
        long depthCells,
        CompiledWorldTopology topology,
        CompiledBiome laboratory,
        in TerrainMaterialPalette palette)
    {
        long localX = worldX - topology.LaboratoryOriginX;
        long localY = depthCells - topology.LaboratoryOriginDepthCells;
        if ((ulong)localX >= (uint)topology.LaboratoryWidthCells ||
            (ulong)localY >= (uint)topology.LaboratoryHeightCells)
        {
            return palette.BoundaryStone;
        }

        int pixelIndex = checked(((int)localY * topology.LaboratoryWidthCells) + (int)localX);
        int maskKind = (topology.LaboratoryTerrainMask[pixelIndex >> 1] >> ((pixelIndex & 1) * 4)) & 0x0F;
        return maskKind switch
        {
            0 => palette.Empty,
            1 => palette.BoundaryStone,
            2 => laboratory.Structure,
            3 => laboratory.Hazard,
            4 => palette.Water,
            5 => palette.PackedDirt,
            6 => palette.Crystal,
            7 => palette.Stone,
            _ => throw new InvalidOperationException($"未知的 Laboratory 参考地形掩码值：{maskKind}。"),
        };
    }

    private static bool TrySelectHolyMountainMaterial(
        long worldX,
        in TerrainRowContext row,
        out ushort material)
    {
        CampaignConfig config = row.State.Config;
        CompiledHolyMountain holyMountain = row.State.HolyMountain;
        long relativeX = worldX - row.PathCenterX;
        long localY = row.Location.LocalDepthCells;
        ReadOnlySpan<CompiledHolyMountainOperation> operations = holyMountain.Operations;
        for (int i = operations.Length - 1; i >= 0; i--)
        {
            CompiledHolyMountainOperation operation = operations[i];
            if (operation.Contains(relativeX, localY))
            {
                material = operation.Material;
                return true;
            }
        }

        long distanceX = Math.Abs(relativeX);
        int shellThickness = holyMountain.ShellThicknessCells;
        if (distanceX > config.HolyMountainHalfWidthCells + shellThickness)
        {
            material = default;
            return false;
        }

        bool passage = distanceX <= config.MainPathHalfWidthCells + 4;
        bool cap = localY < shellThickness ||
            localY >= config.HolyMountainHeightCells - shellThickness;
        bool wall = distanceX >= config.HolyMountainHalfWidthCells;
        bool centralPlatform = localY is >= 60 and <= 63 &&
            distanceX > config.MainPathHalfWidthCells + 20;
        material = wall || (cap && !passage)
            ? holyMountain.ShellMaterial
            : centralPlatform
                ? holyMountain.PlatformMaterial
                : row.Palette.Empty;
        return true;
    }

    private static bool TrySelectPortalTerrain(
        long worldX,
        in TerrainRowContext row,
        out ushort material)
    {
        TerrainGenerationState state = row.State;
        CompiledPortalNetwork portal = state.PortalNetwork;
        PortalRowContext portalRow = row.PortalRow;
        int firstPortalIndex = portalRow.FirstPortalIndex;
        long relativeY = portalRow.RelativeY;
        int basinBottom = portal.BasinTopOffsetCells + portal.BasinDepthCells;

        if (portalRow.Kind == PortalRowKind.Chamber)
        {
            long chamberMinimumX = state.PortalAnchors[firstPortalIndex].SourceX -
                portal.TriggerHalfWidthCells;
            long chamberMaximumX = state.PortalAnchors[
                firstPortalIndex + portal.PortalsPerHolyMountain - 1].SourceX +
                portal.TriggerHalfWidthCells;
            bool insideChamber = worldX >= chamberMinimumX && worldX <= chamberMaximumX;
            material = insideChamber ? row.Palette.Empty : default;
            return insideChamber;
        }

        for (int i = 0; i < portal.PortalsPerHolyMountain; i++)
        {
            CampaignPortalAnchor anchor = state.PortalAnchors[firstPortalIndex + i];
            long distanceX = Math.Abs(worldX - anchor.SourceX);
            if (distanceX > portal.BasinHalfWidthCells + 2L)
            {
                continue;
            }

            bool shell = distanceX >= portal.BasinHalfWidthCells || relativeY >= basinBottom;
            bool liquid = relativeY >= portal.BasinTopOffsetCells + 2L;
            material = shell
                ? portal.EyeShellMaterial
                : liquid
                    ? portal.TeleportatiumMaterial
                    : row.Palette.Empty;
            return true;
        }

        material = default;
        return false;
    }

    private static PortalRowContext BuildPortalRowContext(
        TerrainGenerationState state,
        in CampaignDepthLocation location,
        int regionIndex,
        long worldY)
    {
        if (location.Kind != CampaignDepthKind.Region ||
            (uint)regionIndex >= CampaignConfig.RequiredRegionCount - 1)
        {
            return default;
        }

        CompiledPortalNetwork portal = state.PortalNetwork;
        int firstPortalIndex = regionIndex * portal.PortalsPerHolyMountain;
        long relativeY = worldY - state.PortalAnchors[firstPortalIndex].SourceY;
        int basinBottom = portal.BasinTopOffsetCells + portal.BasinDepthCells;
        return relativeY < -portal.TriggerHalfHeightCells || relativeY > basinBottom + 1L
            ? default
            : new PortalRowContext(
                relativeY < portal.BasinTopOffsetCells
                    ? PortalRowKind.Chamber
                    : PortalRowKind.Basins,
                firstPortalIndex,
                relativeY);
    }

    private static ushort SelectBiomeMaterial(
        long worldX,
        long worldY,
        bool protectedSpawn,
        CompiledBiome biome,
        in TerrainRowContext row)
    {
        return TrySelectGlobalPixelSceneMaterial(worldX, worldY, in row, out ushort globalSceneMaterial)
            ? globalSceneMaterial
            : TrySelectPixelSceneMaterial(worldX, worldY, biome, in row, out ushort sceneMaterial)
            ? sceneMaterial
            : IsBiomeOpenAt(worldX, worldY, biome, row.WorldSeed)
                ? row.Palette.Empty
                : SelectBiomeSolidMaterial(worldX, worldY, protectedSpawn, biome, in row);
    }

    private static ushort SelectWangBiomeMaterial(
        long worldX,
        long worldY,
        bool protectedSpawn,
        CompiledBiome biome,
        in CompiledReferenceBiome referenceBiome,
        in TerrainRowContext row)
    {
        return TrySelectPixelSceneMaterial(worldX, worldY, biome, in row, out ushort sceneMaterial)
            ? sceneMaterial
            : SelectCompiledWangMaterial(
                worldX,
                worldY,
                protectedSpawn,
                biome,
                referenceBiome,
                in row);
    }

    private static ushort SelectReferenceWangBiomeMaterial(
        long worldX,
        long worldY,
        bool protectedSpawn,
        in CompiledReferenceBiome referenceBiome,
        in TerrainRowContext row)
    {
        return SelectCompiledWangMaterial(
            worldX,
            worldY,
            protectedSpawn,
            row.Biome,
            referenceBiome,
            in row);
    }

    private static ushort SelectCompiledWangMaterial(
        long worldX,
        long worldY,
        bool protectedSpawn,
        CompiledBiome biome,
        in CompiledReferenceBiome referenceBiome,
        in TerrainRowContext row)
    {

        DecodedNoitaWangTerrainSet wangTerrain = referenceBiome.WangTerrain ??
            throw new InvalidOperationException($"参考 biome {referenceBiome.Id} 缺少已编译的 Noita Wang 模板。");
        byte wangSemantic = wangTerrain.Sample(
            worldX,
            worldY,
            row.WorldSeed,
            referenceBiome.Salt,
            out byte density);
        byte semantic = wangSemantic;
        if (referenceBiome.BitmapCaves is not null &&
            referenceBiome.BitmapCaves.TrySample(
                worldX,
                worldY,
                row.WorldSeed,
                referenceBiome.Salt,
                out byte bitmapSemantic))
        {
            semantic = bitmapSemantic;
        }
        bool empty = semantic == (byte)NoitaWangTerrainSemantic.Empty ||
            DecodedNoitaWangTerrainSet.IsMarker(semantic) ||
            (semantic == (byte)NoitaWangTerrainSemantic.RandomBinary &&
                !DecodedNoitaWangTerrainSet.IsRandomBinarySolid(worldX, worldY, row.WorldSeed, referenceBiome.Salt));
        if (empty)
        {
            return row.Palette.Empty;
        }

        if (DecodedNoitaWangTerrainSet.IsMaterial(semantic))
        {
            int materialIndex = semantic - NoitaWangTerrainCatalog.MaterialSemanticBase;
            ReadOnlySpan<ushort> materials = protectedSpawn
                ? referenceBiome.ProtectedWangMaterials
                : referenceBiome.WangMaterials;
            return (uint)materialIndex < (uint)materials.Length
                ? materials[materialIndex]
                : throw new InvalidOperationException($"Noita Wang 材质 semantic {semantic} 未编译。");
        }

        return semantic switch
        {
            (byte)NoitaWangTerrainSemantic.Primary or
            (byte)NoitaWangTerrainSemantic.RandomBinary => SelectWangMaterialLayer(
                worldX,
                worldY,
                density,
                referenceBiome,
                in row,
                SelectBiomeSolidMaterial(worldX, worldY, protectedSpawn, biome, in row)),
            (byte)NoitaWangTerrainSemantic.RandomMaterial => SelectRandomWangMaterial(
                worldX,
                worldY,
                row.WorldSeed,
                referenceBiome),
            (byte)NoitaWangTerrainSemantic.Secondary => biome.Secondary,
            (byte)NoitaWangTerrainSemantic.Loose => biome.Loose,
            (byte)NoitaWangTerrainSemantic.Structure => biome.Structure,
            (byte)NoitaWangTerrainSemantic.Hazard => protectedSpawn ? biome.Primary : biome.Hazard,
            (byte)NoitaWangTerrainSemantic.Pool => biome.Pool,
            _ => throw new InvalidOperationException($"未知 Noita Wang terrain semantic：{semantic}。"),
        };
    }

    private static ushort SelectRandomWangMaterial(
        long worldX,
        long worldY,
        ulong worldSeed,
        in CompiledReferenceBiome referenceBiome)
    {
        ushort[] materials = referenceBiome.RandomWangMaterials;
        if (materials.Length == 0)
        {
            throw new InvalidOperationException($"参考 biome {referenceBiome.Id} 缺少 RandomMaterial 输出。");
        }

        int index = Math.Min(
            (int)(HashUnit(worldX, worldY, worldSeed ^ referenceBiome.Salt ^ 0x5241_4E44_4D41_544CUL) * materials.Length),
            materials.Length - 1);
        return materials[index];
    }

    private static ushort SelectWangMaterialLayer(
        long worldX,
        long worldY,
        byte density,
        in CompiledReferenceBiome referenceBiome,
        in TerrainRowContext row,
        ushort fallbackMaterial)
    {
        CompiledWangMaterialLayer[] layers = referenceBiome.WangMaterialLayers;
        float primaryField = density * (1.0f / byte.MaxValue);
        for (int i = 0; i < layers.Length; i++)
        {
            ref readonly CompiledWangMaterialLayer layer = ref layers[i];
            if (!layer.Enabled ||
                (layer.LimitY && (worldY < layer.LimitMinY || worldY > layer.LimitMaxY)))
            {
                continue;
            }

            float field = layer.MaterialIndex == 10
                ? primaryField
                : IndexedWangMaterialField(worldX, worldY, row.WorldSeed, referenceBiome.Salt, layer.MaterialIndex);
            if (layer.AddPerlin > 0)
            {
                field += layer.AddPerlin * NormalizedFractal2D(
                    worldX * layer.AddPerlinScaleX,
                    worldY * layer.AddPerlinScaleY,
                    row.WorldSeed ^ referenceBiome.Salt ^ ((uint)layer.MaterialIndex * 0x9E37_79B9UL),
                    3);
            }

            if (layer.IsRare)
            {
                if (!IsWangRareLayerAt(worldX, worldY, row.WorldSeed, referenceBiome.Salt, in layer))
                {
                    continue;
                }

                field += 1.0f;
            }

            if (field >= layer.MaterialMin && field <= layer.MaterialMax)
            {
                return layer.Material;
            }
        }

        return fallbackMaterial;
    }

    private static float IndexedWangMaterialField(
        long worldX,
        long worldY,
        ulong worldSeed,
        ulong biomeSalt,
        int materialIndex)
    {
        ulong channelSalt = worldSeed ^ biomeSalt ^ ((uint)materialIndex * 0xD6E8_FEB8_6659_FD93UL);
        return NormalizedFractal2D(worldX * 0.01, worldY * 0.01, channelSalt, 3);
    }

    private static bool IsWangRareLayerAt(
        long worldX,
        long worldY,
        ulong worldSeed,
        ulong biomeSalt,
        in CompiledWangMaterialLayer layer)
    {
        ulong salt = worldSeed ^ biomeSalt ^ ((uint)layer.MaterialIndex * 0xA076_1D64_78BD_642FUL) ^ layer.MaterialSalt;
        bool selected = !layer.RareUsePerlin && !layer.RareUsePolka;
        if (layer.RareUsePerlin)
        {
            float rareField = NormalizedFractal2D(
                worldX * layer.RareScaleX,
                worldY * layer.RareScaleY,
                salt,
                3);
            selected |= rareField >= layer.RareRequiredMin && rareField <= layer.RareRequiredMax;
        }

        if (layer.RareUsePolka)
        {
            selected |= IsPolkaSelected(worldX, worldY, salt, in layer);
        }

        return selected;
    }

    private static bool IsPolkaSelected(
        long worldX,
        long worldY,
        ulong salt,
        in CompiledWangMaterialLayer layer)
    {
        double scaleX = layer.RareScaleX > 0 ? layer.RareScaleX : 0.01;
        double scaleY = layer.RareScaleY > 0 ? layer.RareScaleY : 0.01;
        long cellX = (long)Math.Floor(worldX * scaleX);
        long cellY = (long)Math.Floor(worldY * scaleY);
        float probability = (float)HashUnit(cellX, cellY, salt);
        if (probability > layer.RarePolkaProbability)
        {
            return false;
        }

        float centerX = (float)HashUnit(cellX, cellY, salt ^ 0x9E37_79B9_7F4A_7C15UL);
        float centerY = (float)HashUnit(cellX, cellY, salt ^ 0xC2B2_AE3D_27D4_EB4FUL);
        float radiusMix = (float)HashUnit(cellX, cellY, salt ^ 0x94D0_49BB_1331_11EBUL);
        float radius = layer.RarePolkaRadiusLow +
            ((layer.RarePolkaRadiusHigh - layer.RarePolkaRadiusLow) * radiusMix);
        float localX = (float)((worldX * scaleX) - cellX) - centerX;
        float localY = (float)((worldY * scaleY) - cellY) - centerY;
        return layer.RarePolkaIsBoxed
            ? Math.Max(Math.Abs(localX), Math.Abs(localY)) <= radius
            : (localX * localX) + (localY * localY) <= radius * radius;
    }

    private static float NormalizedFractal2D(double x, double y, ulong salt, int octaves)
    {
        return (float)((Fractal2D(x, y, salt, octaves) + 1.0) * 0.5);
    }

    private static bool TrySelectGlobalPixelSceneMaterial(
        long worldX,
        long worldY,
        in TerrainRowContext row,
        out ushort material)
    {
        if (!NoitaPixelSceneCatalog.TrySample(worldX, worldY, out NoitaPixelSceneMaterial sceneMaterial))
        {
            material = default;
            return false;
        }

        TerrainMaterialPalette palette = row.Palette;
        material = sceneMaterial switch
        {
            NoitaPixelSceneMaterial.Empty => palette.Empty,
            NoitaPixelSceneMaterial.Dirt => palette.Dirt,
            NoitaPixelSceneMaterial.PackedDirt => palette.PackedDirt,
            NoitaPixelSceneMaterial.Water => palette.Water,
            NoitaPixelSceneMaterial.Stone => palette.Stone,
            NoitaPixelSceneMaterial.Metal => palette.Metal,
            NoitaPixelSceneMaterial.Wood => palette.Wood,
            NoitaPixelSceneMaterial.PackedSand => palette.PackedSand,
            NoitaPixelSceneMaterial.BoundaryStone => palette.BoundaryStone,
            NoitaPixelSceneMaterial.Crystal => palette.Crystal,
            NoitaPixelSceneMaterial.Smoke => palette.Smoke,
            NoitaPixelSceneMaterial.Ice => palette.Ice,
            NoitaPixelSceneMaterial.Sand => palette.Sand,
            NoitaPixelSceneMaterial.Glass => palette.Glass,
            NoitaPixelSceneMaterial.Lava => palette.Lava,
            NoitaPixelSceneMaterial.Blood => palette.Blood,
            NoitaPixelSceneMaterial.BoneStatic => palette.BoneStatic,
            NoitaPixelSceneMaterial.CheeseStatic => palette.CheeseStatic,
            NoitaPixelSceneMaterial.Mud => palette.Mud,
            NoitaPixelSceneMaterial.SandPetrify => palette.SandPetrify,
            NoitaPixelSceneMaterial.SnowSticky => palette.SnowSticky,
            _ => throw new InvalidOperationException($"未知 Noita Pixel Scene material：{sceneMaterial}。"),
        };
        return true;
    }

    private static ushort SelectBiomeSolidMaterial(
        long worldX,
        long worldY,
        bool protectedSpawn,
        CompiledBiome biome,
        in TerrainRowContext row)
    {
        double strata = Fractal2D(
            worldX * biome.Grammar.HorizontalScale * 1.9,
            worldY * biome.Grammar.VerticalScale * 1.9,
            row.WorldSeed ^ biome.Salt ^ 0xDE90_517UL,
            2);
        return !protectedSpawn && strata > biome.HazardThreshold
            ? biome.Hazard
            : strata > 0.34
                ? biome.Loose
                : strata < -0.56
                    ? biome.Secondary
                    : biome.Primary;
    }

    private static bool TrySelectConnectionMaterial(
        long worldX,
        long worldY,
        bool sideBiomeOnly,
        in TerrainRowContext row,
        out ushort material)
    {
        ReadOnlySpan<ConnectionRowSegment> segments = row.ConnectionSegments;
        for (int i = 0; i < segments.Length; i++)
        {
            ConnectionRowSegment segment = segments[i];
            if (segment.Kind == ConnectionRowSegmentKind.SideBiome != sideBiomeOnly)
            {
                continue;
            }

            if (worldX < segment.MinimumX || worldX > segment.MaximumX)
            {
                continue;
            }

            if (segment.Kind == ConnectionRowSegmentKind.SideBiome)
            {
                material = SelectBiomeMaterial(
                    worldX,
                    worldY,
                    protectedSpawn: false,
                    row.State.SideBiomes[segment.SideBiomeIndex],
                    in row);
                return true;
            }

            material = segment.Kind == ConnectionRowSegmentKind.Access &&
                worldX >= segment.GateMinimumX &&
                worldX <= segment.GateMaximumX
                    ? segment.GateMaterial
                    : segment.Material;
            return true;
        }

        material = default;
        return false;
    }

    private static int BuildConnectionRowSegments(
        TerrainGenerationState state,
        long worldY,
        Span<ConnectionRowSegment> destination)
    {
        int count = 0;
        ReadOnlySpan<ResolvedConnection> connections = state.Connections;
        for (int i = 0; i < connections.Length; i++)
        {
            ResolvedConnection resolved = connections[i];
            CompiledConnection connection = resolved.Connection;
            if (connection.Kind is CompiledConnectionKind.SideBiome or CompiledConnectionKind.SecretSideBiome)
            {
                if (Math.Abs(worldY - resolved.AnchorY) <= connection.CorridorHalfWidthCells)
                {
                    AddAccessSegment(
                        resolved,
                        resolved.AnchorPathX,
                        state.Palette.Empty,
                        destination,
                        ref count);
                }

                if (worldY >= resolved.StartY && worldY <= resolved.EndY)
                {
                    AddSideBiomeSegment(resolved, destination, ref count);
                }

                continue;
            }

            if (connection.Kind == CompiledConnectionKind.VerticalShortcut)
            {
                if (worldY >= resolved.StartY && worldY <= resolved.EndY)
                {
                    AddMaterialSegment(
                        resolved.CenterX - connection.HalfWidthCells,
                        resolved.CenterX + connection.HalfWidthCells,
                        state.Palette.Empty,
                        destination,
                        ref count);
                }

                if (Math.Abs(worldY - resolved.StartY) <= connection.CorridorHalfWidthCells)
                {
                    AddAccessSegment(
                        resolved,
                        resolved.StartPathX,
                        state.Palette.Empty,
                        destination,
                        ref count);
                }

                if (Math.Abs(worldY - resolved.EndY) <= connection.CorridorHalfWidthCells)
                {
                    AddAccessSegment(
                        resolved,
                        resolved.EndPathX,
                        state.Palette.Empty,
                        destination,
                        ref count);
                }

                continue;
            }

            if (worldY >= resolved.StartY && worldY <= resolved.EndY)
            {
                AddSideBiomeSegment(resolved, destination, ref count);
            }

            if (Math.Abs(worldY - resolved.StartY) <= connection.CorridorHalfWidthCells)
            {
                AddAccessSegment(
                    resolved,
                    resolved.StartPathX,
                    state.Palette.Empty,
                    destination,
                    ref count);
            }

            if (Math.Abs(worldY - resolved.EndY) <= connection.CorridorHalfWidthCells)
            {
                AddAccessSegment(
                    resolved,
                    resolved.EndPathX,
                    state.Palette.Empty,
                    destination,
                    ref count);
            }
        }

        return count;
    }

    private static int BuildBiomeLandmarkRowSegments(
        TerrainGenerationState state,
        int regionIndex,
        long worldY,
        Span<BiomeLandmarkRowSegment> destination)
    {
        int count = 0;
        ReadOnlySpan<ResolvedBiomeLandmark> landmarks = state.BiomeLandmarks;
        for (int i = 0; i < landmarks.Length; i++)
        {
            ResolvedBiomeLandmark landmark = landmarks[i];
            if (landmark.RegionIndex != regionIndex ||
                worldY < landmark.MinimumY ||
                worldY > landmark.MaximumY)
            {
                continue;
            }

            if ((uint)count >= (uint)destination.Length)
            {
                throw new InvalidOperationException(
                    $"单行 biome landmark segment 超过固定容量 {destination.Length}。 ");
            }

            destination[count++] = new BiomeLandmarkRowSegment(
                i,
                checked((int)(worldY - landmark.MinimumY)),
                landmark.MinimumX,
                landmark.MaximumX);
        }

        return count;
    }

    private static bool TrySelectBiomeLandmarkMaterial(
        long worldX,
        in TerrainRowContext row,
        out ushort material)
    {
        ReadOnlySpan<BiomeLandmarkRowSegment> segments = row.BiomeLandmarkSegments;
        for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
        {
            BiomeLandmarkRowSegment segment = segments[segmentIndex];
            if (worldX < segment.MinimumX || worldX > segment.MaximumX)
            {
                continue;
            }

            ResolvedBiomeLandmark landmark = row.State.BiomeLandmarks[segment.LandmarkIndex];
            int localX = checked((int)(worldX - segment.MinimumX));
            ReadOnlySpan<CompiledPixelSceneOperation> operations = landmark.Operations;
            for (int operationIndex = operations.Length - 1; operationIndex >= 0; operationIndex--)
            {
                CompiledPixelSceneOperation operation = operations[operationIndex];
                if (operation.Contains(localX, segment.LocalY))
                {
                    material = operation.Material;
                    return true;
                }
            }
        }

        material = default;
        return false;
    }

    private static void AddSideBiomeSegment(
        in ResolvedConnection resolved,
        Span<ConnectionRowSegment> destination,
        ref int count)
    {
        CompiledConnection connection = resolved.Connection;
        AddConnectionSegment(
            new ConnectionRowSegment(
                ConnectionRowSegmentKind.SideBiome,
                resolved.CenterX - connection.HalfWidthCells,
                resolved.CenterX + connection.HalfWidthCells,
                1,
                0,
                default,
                default,
                connection.SideBiomeIndex),
            destination,
            ref count);
    }

    private static void AddMaterialSegment(
        long minimumX,
        long maximumX,
        ushort material,
        Span<ConnectionRowSegment> destination,
        ref int count)
    {
        AddConnectionSegment(
            new ConnectionRowSegment(
                ConnectionRowSegmentKind.Material,
                minimumX,
                maximumX,
                1,
                0,
                material,
                material,
                -1),
            destination,
            ref count);
    }

    private static void AddAccessSegment(
        in ResolvedConnection resolved,
        long pathX,
        ushort empty,
        Span<ConnectionRowSegment> destination,
        ref int count)
    {
        CompiledConnection connection = resolved.Connection;
        long nearEdgeX = resolved.CenterX - ((long)connection.Direction * connection.HalfWidthCells);
        long gateX = nearEdgeX - (connection.Direction * 2L);
        AddConnectionSegment(
            new ConnectionRowSegment(
                ConnectionRowSegmentKind.Access,
                Math.Min(pathX, nearEdgeX) - connection.CorridorHalfWidthCells,
                Math.Max(pathX, nearEdgeX) + connection.CorridorHalfWidthCells,
                gateX - 2,
                gateX + 2,
                empty,
                connection.GateMaterial,
                -1),
            destination,
            ref count);
    }

    private static void AddConnectionSegment(
        in ConnectionRowSegment segment,
        Span<ConnectionRowSegment> destination,
        ref int count)
    {
        if ((uint)count >= (uint)destination.Length)
        {
            throw new InvalidOperationException(
                $"单行 biome connection segment 超过固定容量 {destination.Length}。 ");
        }

        destination[count++] = segment;
    }

    private static bool TrySelectPixelSceneMaterial(
        long worldX,
        long worldY,
        CompiledBiome biome,
        in TerrainRowContext row,
        out ushort material)
    {
        BiomeTerrainGrammarDefinition grammar = biome.Grammar;
        int tileSize = grammar.TileSizeCells;
        long tileX = FloorDivide(worldX, tileSize);
        long tileY = FloorDivide(worldY, tileSize);
        int tileLocalX = FloorRemainder(worldX, tileSize);
        int tileLocalY = FloorRemainder(worldY, tileSize);
        ReadOnlySpan<CompiledPixelScene> scenes = biome.PixelScenes;
        for (int sceneIndex = 0; sceneIndex < scenes.Length; sceneIndex++)
        {
            CompiledPixelScene scene = scenes[sceneIndex];
            if (!IsPixelSceneSelected(tileX, tileY, row.WorldSeed, scene.Salt, scene.SpawnChance))
            {
                continue;
            }

            int sceneX = tileLocalX - ((tileSize - scene.WidthCells) / 2);
            int sceneY = tileLocalY - ((tileSize - scene.HeightCells) / 2);
            if ((uint)sceneX >= (uint)scene.WidthCells || (uint)sceneY >= (uint)scene.HeightCells)
            {
                continue;
            }

            ReadOnlySpan<CompiledPixelSceneOperation> operations = scene.Operations;
            for (int operationIndex = operations.Length - 1; operationIndex >= 0; operationIndex--)
            {
                CompiledPixelSceneOperation operation = operations[operationIndex];
                if (operation.Contains(sceneX, sceneY))
                {
                    material = operation.Material;
                    return true;
                }
            }
        }

        material = default;
        return false;
    }

    private static bool IsBiomeOpenAt(long worldX, long worldY, CompiledBiome biome, ulong worldSeed)
    {
        return IsBiomeGrammarOpenAt(worldX, worldY, biome.Grammar, biome.Salt, worldSeed);
    }

    internal static bool IsBiomeGrammarOpenAt(
        long worldX,
        long worldY,
        BiomeDefinition biome,
        ulong worldSeed)
    {
        ArgumentNullException.ThrowIfNull(biome);
        return IsBiomeGrammarOpenAt(
            worldX,
            worldY,
            biome.Grammar,
            StableIdSalt(biome.Id),
            worldSeed);
    }

    private static bool IsBiomeGrammarOpenAt(
        long worldX,
        long worldY,
        BiomeTerrainGrammarDefinition grammar,
        ulong biomeSalt,
        ulong worldSeed)
    {
        int tileSize = grammar.TileSizeCells;
        long tileX = FloorDivide(worldX, tileSize);
        long tileY = FloorDivide(worldY, tileSize);
        int localX = FloorRemainder(worldX, tileSize);
        int localY = FloorRemainder(worldY, tileSize);
        int center = tileSize / 2;
        int dx = localX - center;
        int dy = localY - center;
        int radius = grammar.ChamberRadiusCells;
        if ((((long)dx * dx) + ((long)dy * dy)) <= (long)radius * radius)
        {
            return true;
        }

        ulong edgeSalt = worldSeed ^ biomeSalt ^ 0xED6E_5A17UL;
        bool north = HashUnit(tileX, tileY, edgeSalt) < grammar.EdgeOpenChance;
        bool south = HashUnit(tileX, tileY + 1, edgeSalt) < grammar.EdgeOpenChance;
        bool west = HashUnit(tileX, tileY, edgeSalt ^ 0x9E37_79B9UL) < grammar.EdgeOpenChance;
        bool east = HashUnit(tileX + 1, tileY, edgeSalt ^ 0x9E37_79B9UL) < grammar.EdgeOpenChance;
        int verticalCorridor = grammar.CorridorHalfWidthCells +
            (int)Math.Round(Math.Max(0.0, grammar.VerticalBias) * 8.0);
        int horizontalCorridor = grammar.CorridorHalfWidthCells +
            (int)Math.Round(Math.Max(0.0, -grammar.VerticalBias) * 8.0);
        if (localY == 0 && Math.Abs(dx) <= verticalCorridor)
        {
            return north;
        }

        if (localY == tileSize - 1 && Math.Abs(dx) <= verticalCorridor)
        {
            return south;
        }

        if (localX == 0 && Math.Abs(dy) <= horizontalCorridor)
        {
            return west;
        }

        if (localX == tileSize - 1 && Math.Abs(dy) <= horizontalCorridor)
        {
            return east;
        }

        if ((north && localY <= center && Math.Abs(dx) <= verticalCorridor) ||
            (south && localY >= center && Math.Abs(dx) <= verticalCorridor) ||
            (west && localX <= center && Math.Abs(dy) <= horizontalCorridor) ||
            (east && localX >= center && Math.Abs(dy) <= horizontalCorridor))
        {
            return true;
        }

        double cave = Fractal2D(
            worldX * grammar.HorizontalScale,
            worldY * grammar.VerticalScale,
            worldSeed ^ biomeSalt ^ 0xCA7E_61DUL,
            3);
        return cave > grammar.NoiseOpenThreshold;
    }

    private static bool IsPixelSceneSelected(
        long tileX,
        long tileY,
        ulong worldSeed,
        ulong sceneSalt,
        double spawnChance)
    {
        return HashUnit(tileX, tileY, worldSeed ^ sceneSalt) < spawnChance;
    }

    private static void PopulateTemperature(
        ReadOnlySpan<ushort> materialCells,
        int sizeCells,
        Span<Half> temperatureCells,
        int temperatureSizeCells,
        long originCellX,
        long originCellY,
        TerrainGenerationState state,
        in TerrainMaterialPalette palette)
    {
        CampaignConfig config = state.Config;
        int cellScale = sizeCells / temperatureSizeCells;
        for (int temperatureY = 0; temperatureY < temperatureSizeCells; temperatureY++)
        {
            long sampleWorldY = originCellY + (temperatureY * cellScale) + (cellScale / 2);
            CampaignDepthLocation location = config.ResolveLocation(sampleWorldY);
            int regionIndex = Math.Clamp(location.RegionIndex, 0, CampaignConfig.RequiredRegionCount - 1);
            for (int temperatureX = 0; temperatureX < temperatureSizeCells; temperatureX++)
            {
                int startY = temperatureY * cellScale;
                int startX = temperatureX * cellScale;
                long sampleWorldX = originCellX + (temperatureX * cellScale) + (cellScale / 2);
                CompiledTopologyCell topologyCell = state.WorldTopology.Resolve(
                    sampleWorldX,
                    sampleWorldY - config.SurfaceY);
                CompiledBiome biome = ResolveTemperatureBiome(
                    sampleWorldX,
                    sampleWorldY,
                    regionIndex,
                    state,
                    topologyCell);
                bool authoredHolyMountain = location.Kind == CampaignDepthKind.HolyMountain &&
                    topologyCell.Kind is CompiledTopologyCellKind.HolyMountain or CompiledTopologyCellKind.Legacy;
                float temperature = authoredHolyMountain
                    ? state.HolyMountain.BaseTemperature
                    : biome.BaseTemperature;
                bool containsHotHazard = false;
                for (int y = 0; y < cellScale && !containsHotHazard; y++)
                {
                    int row = (startY + y) * sizeCells;
                    for (int x = 0; x < cellScale; x++)
                    {
                        ushort material = materialCells[row + startX + x];
                        if (material == palette.Lava)
                        {
                            containsHotHazard = true;
                            break;
                        }
                    }
                }

                temperatureCells[(temperatureY * temperatureSizeCells) + temperatureX] =
                    (Half)(containsHotHazard ? 255f : temperature);
            }
        }
    }

    private static CompiledBiome ResolveTemperatureBiome(
        long worldX,
        long worldY,
        int mainRegionIndex,
        TerrainGenerationState state,
        CompiledTopologyCell topologyCell)
    {
        if (topologyCell.Kind == CompiledTopologyCellKind.MainBiome)
        {
            return state.MainBiomes[topologyCell.BiomeIndex];
        }

        if (topologyCell.Kind == CompiledTopologyCellKind.SideBiome)
        {
            return state.SideBiomes[topologyCell.BiomeIndex];
        }

        if (topologyCell.Kind == CompiledTopologyCellKind.FixedLaboratory)
        {
            return state.MainBiomes[CampaignConfig.RequiredRegionCount - 1];
        }

        ReadOnlySpan<ResolvedConnection> connections = state.Connections;
        for (int i = 0; i < connections.Length; i++)
        {
            ResolvedConnection resolved = connections[i];
            CompiledConnection connection = resolved.Connection;
            if (connection.SideBiomeIndex < 0)
            {
                continue;
            }

            if (worldY < resolved.StartY || worldY > resolved.EndY)
            {
                continue;
            }

            if (Math.Abs(worldX - resolved.CenterX) <= connection.HalfWidthCells)
            {
                return state.SideBiomes[connection.SideBiomeIndex];
            }
        }

        return state.MainBiomes[mainRegionIndex];
    }

    /// <summary>
    /// 在有限 Editor 预览画布中显示以世界原点为中心的同源地形切片。
    /// </summary>
    internal static void PopulateAuthoringWorld(in AuthoringWorldPreviewContext context)
    {
        const int ChunkSizeCells = 64;
        const int TemperatureSizeCells = 16;
        CampaignConfig config = CampaignConfig.Load(context.Config);
        BiomeCatalog biomes = BiomeCatalog.Load(context.Config, config);
        NoitaWangTerrainCatalog wangTerrain = NoitaWangTerrainCatalog.Load(context.Config);
        ulong worldSeed = config.InitialRunSeed;
        TerrainGenerationState state = CreateGenerationState(
            context.Materials,
            config,
            biomes,
            wangTerrain,
            worldSeed);
        _ = context.Edit.ClearRect(0, 0, context.WidthCells - 1, context.HeightCells - 1);
        Span<ushort> materialCells = stackalloc ushort[ChunkSizeCells * ChunkSizeCells];
        Span<Half> temperatureCells = stackalloc Half[TemperatureSizeCells * TemperatureSizeCells];
        int chunkCountX = (context.WidthCells + ChunkSizeCells - 1) / ChunkSizeCells;
        int chunkCountY = (context.HeightCells + ChunkSizeCells - 1) / ChunkSizeCells;
        for (int chunkY = 0; chunkY < chunkCountY; chunkY++)
        {
            int originY = chunkY * ChunkSizeCells;
            int copyHeight = Math.Min(ChunkSizeCells, context.HeightCells - originY);
            for (int chunkX = 0; chunkX < chunkCountX; chunkX++)
            {
                int originX = chunkX * ChunkSizeCells;
                int copyWidth = Math.Min(ChunkSizeCells, context.WidthCells - originX);
                PopulateChunkCore(
                    state,
                    worldSeed,
                    originX,
                    originY,
                    ChunkSizeCells,
                    TemperatureSizeCells,
                    materialCells,
                    temperatureCells);
                for (int localY = 0; localY < copyHeight; localY++)
                {
                    int rowOffset = localY * ChunkSizeCells;
                    int runStart = 0;
                    ushort runMaterial = materialCells[rowOffset];
                    for (int localX = 1; localX <= copyWidth; localX++)
                    {
                        ushort material = localX == copyWidth
                            ? ushort.MaxValue
                            : materialCells[rowOffset + localX];
                        if (material == runMaterial)
                        {
                            continue;
                        }

                        if (runMaterial != state.Palette.Empty)
                        {
                            _ = context.Edit.PaintRect(
                                originX + runStart,
                                originY + localY,
                                originX + localX - 1,
                                originY + localY,
                                new MaterialId(runMaterial));
                        }

                        runStart = localX;
                        runMaterial = material;
                    }
                }
            }
        }
    }

    private static TerrainGenerationState CreateGenerationState(
        IMaterialQuery materials,
        CampaignConfig config,
        BiomeCatalog biomes,
        NoitaWangTerrainCatalog wangTerrain,
        ulong worldSeed)
    {
        CompiledPixelScene[] pixelScenes = CompilePixelScenes(materials, biomes);
        CompiledBiome[] mainBiomes = CompileBiomes(
            materials,
            biomes.MainPath,
            biomes,
            pixelScenes);
        CompiledBiome[] sideBiomes = CompileBiomes(
            materials,
            biomes.SideBiomes,
            biomes,
            pixelScenes);
        CompiledWorldTopology worldTopology = CompileWorldTopology(materials, biomes, wangTerrain);
        CompiledConnection[] connections = CompileConnections(
            materials,
            biomes);
        ResolvedConnection[] resolvedConnections = ResolveConnections(
            config,
            worldSeed,
            connections);
        ResolvedBiomeLandmark[] biomeLandmarks = CompileAndResolveBiomeLandmarks(
            materials,
            biomes,
            config,
            worldSeed);
        CompiledHolyMountain holyMountain = CompileHolyMountain(materials, biomes.HolyMountain);
        CompiledPortalNetwork portalNetwork = CompilePortalNetwork(materials, biomes.PortalNetwork);
        CompiledNoitaSnowcastlePixelSceneCatalog snowcastlePixelScenes =
            NoitaSnowcastlePixelSceneCatalog.Compile(materials);
        CompiledNoitaVegetationCatalog vegetation = NoitaVegetationCatalog.Compile(materials);
        CampaignPortalAnchor[] portalAnchors = ResolvePortalAnchors(
            config,
            biomes.PortalNetwork,
            worldSeed);
        return new TerrainGenerationState(
            config,
            biomes,
            wangTerrain,
            ResolvePalette(materials),
            mainBiomes,
            sideBiomes,
            worldTopology,
            resolvedConnections,
            biomeLandmarks,
            holyMountain,
            portalNetwork,
            portalAnchors,
            snowcastlePixelScenes,
            vegetation,
            worldSeed);
    }

    private static TerrainMaterialPalette ResolvePalette(IMaterialQuery materials)
    {
        return new TerrainMaterialPalette(
            ResolveRequired(materials, "empty"),
            ResolveRequired(materials, "sand"),
            ResolveRequired(materials, "dirt"),
            ResolveRequired(materials, "packed_sand"),
            ResolveRequired(materials, "packed_dirt"),
            ResolveRequired(materials, "water"),
            ResolveRequired(materials, "lava"),
            ResolveRequired(materials, "stone"),
            ResolveRequired(materials, "boundary_stone"),
            ResolveRequired(materials, "ice"),
            ResolveRequired(materials, "gravel"),
            ResolveRequired(materials, "packed_gravel"),
            ResolveRequired(materials, "crystal"),
            ResolveRequired(materials, "metal"),
            ResolveRequired(materials, "wood"),
            ResolveRequired(materials, "smoke"),
            ResolveRequired(materials, "glass"),
            ResolveRequired(materials, "blood"),
            ResolveRequired(materials, "bone_static"),
            ResolveRequired(materials, "cheese_static"),
            ResolveRequired(materials, "mud"),
            ResolveRequired(materials, "sand_petrify"),
            ResolveRequired(materials, "snow_sticky"));
    }

    private static CompiledWorldTopology CompileWorldTopology(
        IMaterialQuery materials,
        BiomeCatalog biomes,
        NoitaWangTerrainCatalog wangTerrain)
    {
        WorldTopologyDefinition definition = biomes.WorldTopology;
        CompiledTopologyCell[] cells = new CompiledTopologyCell[checked(definition.Width * definition.Height)];
        ReferenceBiomeDefinition[] sources = definition.ReferenceBiomes;
        CompiledReferenceBiome[] referenceBiomes = new CompiledReferenceBiome[sources.Length];
        for (int i = 0; i < sources.Length; i++)
        {
            ReferenceBiomeDefinition source = sources[i];
            _ = wangTerrain.TryFindDefinitionForReferenceBiome(source.Id, out NoitaWangTerrainSetDefinition terrainSet);
            _ = NoitaBiomeMaterialCatalog.TryFindBySourcePath(
                source.ReferencePath,
                out NoitaBiomeMaterialProfile materialProfile);
            ushort[] wangMaterials = CompileWangMaterials(materials, terrainSet, protectedSpawn: false);
            ushort[] protectedWangMaterials = CompileWangMaterials(materials, terrainSet, protectedSpawn: true);
            ushort[] randomWangMaterials = CompileRandomWangMaterials(materials, terrainSet);
            NoitaWangMaterialLayerDefinition[] materialLayers = terrainSet?.MaterialLayers ?? materialProfile.Layers ?? [];
            CompiledWangMaterialLayer[] wangMaterialLayers = CompileWangMaterialLayers(materials, materialLayers);
            referenceBiomes[i] = new CompiledReferenceBiome(
                source.Id,
                StableIdSalt(source.Id),
                source.Id switch
                {
                    "mountain-left-entrance" => ReferenceMountainKind.LeftEntrance,
                    "mountain-hall" => ReferenceMountainKind.Hall,
                    "mountain-right" => ReferenceMountainKind.Right,
                    "mountain-top" => ReferenceMountainKind.Top,
                    "mountain-left-stub" => ReferenceMountainKind.LeftStub,
                    "mountain-right-stub" => ReferenceMountainKind.RightStub,
                    "mountain-floating-island" => ReferenceMountainKind.FloatingIsland,
                    "mountain-lake" => ReferenceMountainKind.Lake,
                    _ => ReferenceMountainKind.Generic,
                },
                source.DecodedReferenceTerrainMask,
                source.ReferenceTerrainMask?.Accent switch
                {
                    "water" => ReferenceTerrainMaskAccent.Water,
                    "ice" => ReferenceTerrainMaskAccent.Ice,
                    "gravel" or null => ReferenceTerrainMaskAccent.Gravel,
                    _ => throw new InvalidOperationException($"未编译的参考地形掩码强调材质：{source.ReferenceTerrainMask.Accent}。"),
                },
                terrainSet?.Decoded,
                terrainSet?.DecodedBitmapCaves,
                wangMaterials,
                protectedWangMaterials,
                terrainSet?.WangMapWidth ?? 0,
                terrainSet?.WangMapHeight ?? 0,
                wangMaterialLayers,
                randomWangMaterials);
        }

        for (int mapY = 0; mapY < definition.Height; mapY++)
        {
            string row = definition.MacroRows[mapY];
            int cellRow = mapY * definition.Width;
            for (int mapX = 0; mapX < definition.Width; mapX++)
            {
                int referenceIndex = BiomeCatalog.DecodeReferenceBiomeIndex(row, mapX);
                ReferenceBiomeDefinition source = sources[referenceIndex];
                CompiledTopologyCellKind kind = source.Terrain switch
                {
                    "main-biome" => CompiledTopologyCellKind.MainBiome,
                    "side-biome" => CompiledTopologyCellKind.SideBiome,
                    "holy-mountain" => CompiledTopologyCellKind.HolyMountain,
                    "lava" => CompiledTopologyCellKind.Lava,
                    "solid" => CompiledTopologyCellKind.Solid,
                    "empty" => CompiledTopologyCellKind.Empty,
                    "water" => CompiledTopologyCellKind.Water,
                    "clouds" => CompiledTopologyCellKind.Clouds,
                    "surface-hills" => CompiledTopologyCellKind.SurfaceHills,
                    "surface-desert" => CompiledTopologyCellKind.SurfaceDesert,
                    "surface-winter" => CompiledTopologyCellKind.SurfaceWinter,
                    "mountain" => CompiledTopologyCellKind.Mountain,
                    "generic-cave" => CompiledTopologyCellKind.GenericCave,
                    "generic-structure" => CompiledTopologyCellKind.GenericStructure,
                    _ => throw new InvalidOperationException($"未编译的 reference terrain：{source.Terrain}。"),
                };
                int index = kind == CompiledTopologyCellKind.MainBiome
                    ? biomes.FindMainPathIndex(source.GameplayBiome)
                    : kind == CompiledTopologyCellKind.SideBiome
                        ? biomes.FindSideBiomeIndex(source.GameplayBiome)
                        : referenceIndex;
                cells[cellRow + mapX] = new CompiledTopologyCell(kind, index, referenceIndex);
            }
        }

        FixedLaboratoryTopologyDefinition laboratory = definition.FixedLaboratory;
        int laboratoryMacroX = checked((int)FloorDivide(laboratory.OriginX, definition.MacroCellSize));
        int laboratoryMacroY = checked((int)FloorDivide(laboratory.OriginDepthCells, definition.MacroCellSize));
        int laboratoryMacroWidth = checked(
            (laboratory.WidthCells + definition.MacroCellSize - 1) / definition.MacroCellSize);
        int laboratoryMacroHeight = checked(
            (laboratory.HeightCells + definition.MacroCellSize - 1) / definition.MacroCellSize);
        for (int y = 0; y < laboratoryMacroHeight; y++)
        {
            int mapY = laboratoryMacroY + y + definition.OriginMacroY;
            int mapX = laboratoryMacroX + definition.OriginMacroX;
            cells.AsSpan((mapY * definition.Width) + mapX, laboratoryMacroWidth).Fill(
                new CompiledTopologyCell(
                    CompiledTopologyCellKind.FixedLaboratory,
                    CampaignConfig.RequiredRegionCount - 1,
                    -1));
        }

        return new CompiledWorldTopology(
            definition.MacroCellSize,
            definition.Width,
            definition.Height,
            definition.OriginMacroX,
            definition.OriginMacroY,
            laboratory.OriginX,
            laboratory.OriginDepthCells,
            laboratory.WidthCells,
            laboratory.HeightCells,
            laboratory.DecodedReferenceTerrainMask,
            cells,
            referenceBiomes);
    }

    private static ushort[] CompileWangMaterials(
        IMaterialQuery materials,
        NoitaWangTerrainSetDefinition? terrainSet,
        bool protectedSpawn)
    {
        if (terrainSet is null)
        {
            return [];
        }

        NoitaWangMaterialMappingDefinition[] mappings = terrainSet.MaterialMappings;
        ushort[] result = new ushort[mappings.Length];
        ushort protectedFallback = protectedSpawn ? ResolveRequired(materials, "stone") : default;
        for (int i = 0; i < mappings.Length; i++)
        {
            NoitaWangMaterialMappingDefinition mapping = mappings[i];
            result[i] = protectedSpawn && string.Equals(mapping.Semantic, "hazard", StringComparison.Ordinal)
                ? protectedFallback
                : ResolveRequired(materials, mapping.Material);
        }

        return result;
    }

    private static CompiledWangMaterialLayer[] CompileWangMaterialLayers(
        IMaterialQuery materials,
        NoitaWangMaterialLayerDefinition[] layers)
    {
        if (layers.Length == 0)
        {
            return [];
        }

        CompiledWangMaterialLayer[] result = new CompiledWangMaterialLayer[layers.Length];
        for (int i = 0; i < layers.Length; i++)
        {
            NoitaWangMaterialLayerDefinition layer = layers[i];
            result[i] = new CompiledWangMaterialLayer(
                ResolveRequired(materials, layer.MaterialName),
                layer.Enabled,
                (float)layer.AddPerlin,
                (float)layer.AddPerlinScaleX,
                (float)layer.AddPerlinScaleY,
                layer.IsPolygon,
                layer.IsRare,
                (float)layer.LimitMaxY,
                (float)layer.LimitMinY,
                layer.LimitY,
                layer.MaterialIndex,
                (float)layer.MaterialMax,
                (float)layer.MaterialMin,
                layer.RarePolkaIsBoxed,
                (float)layer.RarePolkaProbability,
                (float)layer.RarePolkaRadiusHigh,
                (float)layer.RarePolkaRadiusLow,
                (float)layer.RareRequiredMax,
                (float)layer.RareRequiredMin,
                (float)layer.RareScaleX,
                (float)layer.RareScaleY,
                layer.RareUsePerlin,
                layer.RareUsePolka,
                StableIdSalt(layer.MaterialName) ^ ((uint)i * 0xD6E8_FEB8_6659_FD93UL));
        }

        return result;
    }

    private static ushort[] CompileRandomWangMaterials(
        IMaterialQuery materials,
        NoitaWangTerrainSetDefinition? terrainSet)
    {
        if (terrainSet?.RandomMaterialMappings is not { Length: 1 } mappings)
        {
            return [];
        }

        string[] names = mappings[0].Materials;
        ushort[] result = new ushort[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            result[i] = ResolveRequired(materials, names[i]);
        }

        return result;
    }

    private static CompiledHolyMountain CompileHolyMountain(
        IMaterialQuery materials,
        HolyMountainDefinition definition)
    {
        CompiledHolyMountainOperation[] operations =
            new CompiledHolyMountainOperation[definition.LayoutOperations.Length];
        for (int i = 0; i < operations.Length; i++)
        {
            HolyMountainOperationDefinition operation = definition.LayoutOperations[i];
            operations[i] = new CompiledHolyMountainOperation(
                ResolveRequired(materials, operation.Material),
                operation.X,
                operation.Y,
                operation.Width,
                operation.Height);
        }

        return new CompiledHolyMountain(
            definition.BaseTemperature,
            ResolveRequired(materials, definition.ShellMaterial),
            ResolveRequired(materials, definition.PlatformMaterial),
            definition.ShellThicknessCells,
            operations);
    }

    private static CompiledPortalNetwork CompilePortalNetwork(
        IMaterialQuery materials,
        PortalNetworkDefinition definition)
    {
        return new CompiledPortalNetwork(
            definition.PortalsPerHolyMountain,
            definition.SourceOffsetAboveBoundaryCells,
            definition.TriggerHalfWidthCells,
            definition.TriggerHalfHeightCells,
            ResolveRequired(materials, definition.EyeShellMaterial),
            ResolveRequired(materials, definition.TeleportatiumMaterial),
            definition.BasinTopOffsetCells,
            definition.BasinHalfWidthCells,
            definition.BasinDepthCells,
            definition.MinimumPowerCells);
    }

    private static CompiledPixelScene[] CompilePixelScenes(
        IMaterialQuery materials,
        BiomeCatalog catalog)
    {
        CompiledPixelScene[] result = new CompiledPixelScene[catalog.PixelScenes.Length];
        for (int sceneIndex = 0; sceneIndex < result.Length; sceneIndex++)
        {
            BiomePixelSceneDefinition source = catalog.PixelScenes[sceneIndex];
            CompiledPixelSceneOperation[] operations =
                new CompiledPixelSceneOperation[source.Operations.Length];
            for (int operationIndex = 0; operationIndex < operations.Length; operationIndex++)
            {
                BiomePixelSceneOperationDefinition operation = source.Operations[operationIndex];
                operations[operationIndex] = new CompiledPixelSceneOperation(
                    operation.Kind == "carveEllipse"
                        ? CompiledPixelSceneOperationKind.Ellipse
                        : CompiledPixelSceneOperationKind.Rectangle,
                    ResolveRequired(materials, operation.Material),
                    operation.X,
                    operation.Y,
                    operation.Width,
                    operation.Height);
            }

            result[sceneIndex] = new CompiledPixelScene(
                source.WidthCells,
                source.HeightCells,
                source.SpawnChance,
                StableIdSalt(source.Id),
                source.EncounterId,
                operations);
        }

        return result;
    }

    private static CompiledBiome[] CompileBiomes(
        IMaterialQuery materials,
        BiomeDefinition[] definitions,
        BiomeCatalog catalog,
        CompiledPixelScene[] pixelScenes)
    {
        CompiledBiome[] result = new CompiledBiome[definitions.Length];
        for (int biomeIndex = 0; biomeIndex < result.Length; biomeIndex++)
        {
            BiomeDefinition definition = definitions[biomeIndex];
            CompiledPixelScene[] scenes = new CompiledPixelScene[definition.PixelScenes.Length];
            for (int sceneIndex = 0; sceneIndex < scenes.Length; sceneIndex++)
            {
                int catalogIndex = catalog.FindPixelSceneIndex(definition.PixelScenes[sceneIndex]);
                if (catalogIndex < 0)
                {
                    throw new InvalidDataException(
                        $"biome {definition.Id} 引用了未编译的 pixel scene {definition.PixelScenes[sceneIndex]}。 ");
                }

                CompiledPixelScene scene = pixelScenes[catalogIndex];
                if (scene.WidthCells > definition.Grammar.TileSizeCells ||
                    scene.HeightCells > definition.Grammar.TileSizeCells)
                {
                    throw new InvalidDataException(
                        $"biome {definition.Id} 的 pixel scene {definition.PixelScenes[sceneIndex]} 超出 tile。 ");
                }

                scenes[sceneIndex] = scene;
            }

            BiomeMaterialPaletteDefinition palette = definition.Palette;
            result[biomeIndex] = new CompiledBiome(
                definition.Id,
                definition.BaseTemperature,
                definition.Grammar,
                ResolveRequired(materials, palette.Primary),
                ResolveRequired(materials, palette.Secondary),
                ResolveRequired(materials, palette.Loose),
                ResolveRequired(materials, palette.Structure),
                ResolveRequired(materials, palette.Hazard),
                ResolveRequired(materials, palette.Pool),
                HazardThreshold(definition.HazardFrequency),
                StableIdSalt(definition.Id),
                scenes);
        }

        return result;
    }

    private static ResolvedBiomeLandmark[] CompileAndResolveBiomeLandmarks(
        IMaterialQuery materials,
        BiomeCatalog catalog,
        CampaignConfig config,
        ulong worldSeed)
    {
        ResolvedBiomeLandmark[] result = new ResolvedBiomeLandmark[catalog.Landmarks.Length];
        double mainPathOriginNoise = MainPathOriginNoise(worldSeed);
        for (int landmarkIndex = 0; landmarkIndex < result.Length; landmarkIndex++)
        {
            BiomeLandmarkDefinition source = catalog.Landmarks[landmarkIndex];
            int regionIndex = catalog.FindMainPathIndex(source.Biome);
            long centerY = config.RegionStartCellY(regionIndex) + source.LocalDepthCells;
            long centerX = MainPathCenterX(centerY, config, worldSeed, mainPathOriginNoise) +
                source.OffsetCells;
            long minimumX = centerX - (source.WidthCells / 2L);
            long minimumY = centerY - (source.HeightCells / 2L);
            CompiledPixelSceneOperation[] operations =
                new CompiledPixelSceneOperation[source.Operations.Length];
            for (int operationIndex = 0; operationIndex < operations.Length; operationIndex++)
            {
                BiomePixelSceneOperationDefinition operation = source.Operations[operationIndex];
                operations[operationIndex] = new CompiledPixelSceneOperation(
                    operation.Kind == "carveEllipse"
                        ? CompiledPixelSceneOperationKind.Ellipse
                        : CompiledPixelSceneOperationKind.Rectangle,
                    ResolveRequired(materials, operation.Material),
                    operation.X,
                    operation.Y,
                    operation.Width,
                    operation.Height);
            }

            result[landmarkIndex] = new ResolvedBiomeLandmark(
                regionIndex,
                minimumX,
                minimumY,
                minimumX + source.WidthCells - 1L,
                minimumY + source.HeightCells - 1L,
                operations);
        }

        return result;
    }

    private static CompiledConnection[] CompileConnections(
        IMaterialQuery materials,
        BiomeCatalog catalog)
    {
        CompiledConnection[] result = new CompiledConnection[catalog.Connections.Length];
        for (int i = 0; i < result.Length; i++)
        {
            BiomeConnectionDefinition definition = catalog.Connections[i];
            CompiledConnectionKind kind = definition.Kind switch
            {
                "side-biome" => CompiledConnectionKind.SideBiome,
                "secret-side-biome" => CompiledConnectionKind.SecretSideBiome,
                "vertical-shortcut" => CompiledConnectionKind.VerticalShortcut,
                "vertical-side-biome" => CompiledConnectionKind.VerticalSideBiome,
                _ => throw new InvalidDataException($"不支持的 biome connection kind：{definition.Kind}。"),
            };
            int fromRegionIndex = catalog.FindMainPathIndex(definition.From);
            int toRegionIndex = kind is CompiledConnectionKind.SideBiome or CompiledConnectionKind.SecretSideBiome
                ? -1
                : catalog.FindMainPathIndex(definition.To);
            int sideBiomeIndex = kind switch
            {
                CompiledConnectionKind.SideBiome or CompiledConnectionKind.SecretSideBiome =>
                    catalog.FindSideBiomeIndex(definition.To),
                CompiledConnectionKind.VerticalSideBiome => catalog.FindSideBiomeIndex(definition.SideBiome),
                CompiledConnectionKind.VerticalShortcut => -1,
                _ => throw new InvalidDataException($"不支持的 biome connection kind：{kind}。"),
            };
            result[i] = new CompiledConnection(
                kind,
                fromRegionIndex,
                toRegionIndex,
                definition.Side == "west" ? -1 : 1,
                definition.OffsetCells,
                definition.HalfWidthCells,
                definition.CorridorHalfWidthCells,
                definition.FromLocalDepthCells,
                definition.ToLocalDepthCells,
                ResolveRequired(materials, definition.GateMaterial),
                sideBiomeIndex);
        }

        return result;
    }

    private static ResolvedConnection[] ResolveConnections(
        CampaignConfig config,
        ulong worldSeed,
        CompiledConnection[] connections)
    {
        ResolvedConnection[] result = new ResolvedConnection[connections.Length];
        double mainPathOriginNoise = MainPathOriginNoise(worldSeed);
        for (int i = 0; i < result.Length; i++)
        {
            CompiledConnection connection = connections[i];
            long startY = config.RegionStartCellY(connection.FromRegionIndex) +
                connection.FromLocalDepthCells;
            long endY = connection.Kind is CompiledConnectionKind.SideBiome or CompiledConnectionKind.SecretSideBiome
                ? config.RegionStartCellY(connection.FromRegionIndex) + connection.ToLocalDepthCells
                : config.RegionStartCellY(connection.ToRegionIndex) + connection.ToLocalDepthCells;
            long anchorY = startY + ((endY - startY) / 2);
            long startPathX = MainPathCenterX(startY, config, worldSeed, mainPathOriginNoise);
            long endPathX = MainPathCenterX(endY, config, worldSeed, mainPathOriginNoise);
            long anchorPathX = MainPathCenterX(anchorY, config, worldSeed, mainPathOriginNoise);
            long centerX = anchorPathX +
                ((long)connection.Direction * connection.OffsetCells);
            result[i] = new ResolvedConnection(
                connection,
                startY,
                endY,
                anchorY,
                centerX,
                startPathX,
                endPathX,
                anchorPathX);
        }

        return result;
    }

    private static double HazardThreshold(double frequency)
    {
        return frequency <= 0.0 ? double.PositiveInfinity : 0.58 - (frequency * 3.0);
    }

    private static ulong StableIdSalt(string value)
    {
        ulong hash = 14_695_981_039_346_656_037UL;
        for (int i = 0; i < value.Length; i++)
        {
            hash ^= value[i];
            hash *= 1_099_511_628_211UL;
        }

        return hash;
    }

    private static ushort ResolveRequired(IMaterialQuery materials, string name)
    {
        MaterialId id = materials.Resolve(name);
        return id.IsValid
            ? id.Value
            : throw new InvalidOperationException($"战役地形需要材质 {name}。");
    }

    private static double MoistureAt(long worldX, ulong worldSeed)
    {
        return Fractal1D((worldX + 4_300.0) * 0.00091, worldSeed ^ 0xA0157UL, 4);
    }

    internal static long MainPathCenterX(long worldY, CampaignConfig config, ulong worldSeed)
    {
        return MainPathCenterX(worldY, config, worldSeed, MainPathOriginNoise(worldSeed));
    }

    private static CampaignPortalAnchor[] ResolvePortalAnchors(
        CampaignConfig config,
        PortalNetworkDefinition portal,
        ulong worldSeed)
    {
        int holyMountainCount = CampaignConfig.RequiredRegionCount - 1;
        CampaignPortalAnchor[] result =
            new CampaignPortalAnchor[holyMountainCount * portal.PortalsPerHolyMountain];
        int index = 0;
        for (int holyMountainIndex = 0; holyMountainIndex < holyMountainCount; holyMountainIndex++)
        {
            for (int portalIndex = 0; portalIndex < portal.PortalsPerHolyMountain; portalIndex++)
            {
                result[index++] = ResolvePortalAnchor(
                    config,
                    portal,
                    holyMountainIndex,
                    portalIndex,
                    worldSeed);
            }
        }

        return result;
    }

    internal static CampaignPortalAnchor ResolvePortalAnchor(
        CampaignConfig config,
        PortalNetworkDefinition portal,
        int holyMountainIndex,
        int portalIndex,
        ulong worldSeed)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(portal);
        if ((uint)holyMountainIndex >= CampaignConfig.RequiredRegionCount - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(holyMountainIndex));
        }

        if ((uint)portalIndex >= (uint)portal.PortalsPerHolyMountain)
        {
            throw new ArgumentOutOfRangeException(nameof(portalIndex));
        }

        long holyMountainY = config.HolyMountainStartCellY(holyMountainIndex);
        long sourceY = holyMountainY - portal.SourceOffsetAboveBoundaryCells;
        int centeredPortalIndex = portalIndex - (portal.PortalsPerHolyMountain / 2);
        long sourceX = MainPathCenterX(sourceY, config, worldSeed) +
            ((long)centeredPortalIndex * portal.SpacingCells);
        long destinationY = holyMountainY + portal.DestinationLocalDepthCells;
        long destinationX = MainPathCenterX(destinationY, config, worldSeed) +
            portal.DestinationOffsetCells;
        return new CampaignPortalAnchor(
            holyMountainIndex,
            portalIndex,
            sourceX,
            sourceY,
            destinationX,
            destinationY);
    }

    /// <summary>
    /// 收集给定 Holy Mountain 的数据化地图地标；调用方提供固定容量目标，热路径不分配。
    /// </summary>
    internal static int CollectHolyMountainLandmarkAnchors(
        BiomeCatalog catalog,
        CampaignConfig config,
        int holyMountainIndex,
        ulong worldSeed,
        Span<HolyMountainLandmarkAnchor> destination)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(config);
        if ((uint)holyMountainIndex >= CampaignConfig.RequiredRegionCount - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(holyMountainIndex));
        }

        ReadOnlySpan<HolyMountainLandmarkDefinition> landmarks = catalog.HolyMountain.Landmarks;
        int count = Math.Min(destination.Length, landmarks.Length);
        long holyMountainY = config.HolyMountainStartCellY(holyMountainIndex);
        for (int i = 0; i < count; i++)
        {
            HolyMountainLandmarkDefinition landmark = landmarks[i];
            long worldY = holyMountainY + landmark.LocalDepthCells;
            long worldX = MainPathCenterX(worldY, config, worldSeed) + landmark.OffsetXCells;
            destination[i] = new HolyMountainLandmarkAnchor(
                holyMountainIndex,
                landmark.Id,
                landmark.Kind,
                worldX,
                worldY);
        }

        return count;
    }

    /// <summary>
    /// 收集给定主路径 biome 的数据化固定地标；调用方提供固定容量目标，热路径不分配。
    /// </summary>
    internal static int CollectBiomeLandmarkAnchors(
        BiomeCatalog catalog,
        CampaignConfig config,
        int regionIndex,
        ulong worldSeed,
        Span<BiomeLandmarkAnchor> destination)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(config);
        if ((uint)regionIndex >= CampaignConfig.RequiredRegionCount)
        {
            throw new ArgumentOutOfRangeException(nameof(regionIndex));
        }

        int count = 0;
        long regionStartY = config.RegionStartCellY(regionIndex);
        string biomeId = catalog.MainPath[regionIndex].Id;
        ReadOnlySpan<BiomeLandmarkDefinition> landmarks = catalog.Landmarks;
        for (int i = 0; i < landmarks.Length; i++)
        {
            BiomeLandmarkDefinition landmark = landmarks[i];
            if (!string.Equals(landmark.Biome, biomeId, StringComparison.Ordinal))
            {
                continue;
            }

            if (count == destination.Length)
            {
                return count;
            }

            long worldY = regionStartY + landmark.LocalDepthCells;
            long worldX = MainPathCenterX(worldY, config, worldSeed) + landmark.OffsetCells;
            destination[count++] = new BiomeLandmarkAnchor(
                regionIndex,
                landmark.Id,
                landmark.DisplayName,
                landmark.EncounterId,
                worldX,
                worldY,
                landmark.WidthCells,
                landmark.HeightCells);
        }

        return count;
    }

    /// <summary>
    /// 收集给定主路径 biome 和矩形内由 authored pixel-scene 产生的确定性遭遇锚点。
    /// 调用方提供固定容量目标，热路径不分配；返回值不会超过 destination 长度。
    /// </summary>
    internal static int CollectEncounterAnchors(
        BiomeCatalog catalog,
        CampaignConfig config,
        int regionIndex,
        ulong worldSeed,
        long minimumX,
        long minimumY,
        long maximumX,
        long maximumY,
        Span<BiomeEncounterAnchor> destination)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(config);
        if ((uint)regionIndex >= CampaignConfig.RequiredRegionCount)
        {
            throw new ArgumentOutOfRangeException(nameof(regionIndex));
        }

        if (minimumX > maximumX || minimumY > maximumY)
        {
            throw new ArgumentException("遭遇锚点查询矩形无效。");
        }

        // Laboratory 是固定 boss_arena 场景，不能再叠加主路径 biome 的随机
        // pixel-scene；终局内容通过固定地标和后续 Boss 实体目录提供。
        if (regionIndex == CampaignConfig.RequiredRegionCount - 1)
        {
            return 0;
        }

        BiomeDefinition biome = catalog.MainPath[regionIndex];
        int tileSize = biome.Grammar.TileSizeCells;
        long regionMinimumY = config.RegionStartCellY(regionIndex);
        long regionMaximumY = checked(regionMinimumY + config.RegionHeightCellsAt(regionIndex) - 1L);
        long clippedMinimumY = Math.Max(minimumY, regionMinimumY);
        long clippedMaximumY = Math.Min(maximumY, regionMaximumY);
        if (clippedMinimumY > clippedMaximumY || destination.IsEmpty)
        {
            return 0;
        }

        long minimumTileX = FloorDivide(minimumX, tileSize);
        long maximumTileX = FloorDivide(maximumX, tileSize);
        long minimumTileY = FloorDivide(clippedMinimumY, tileSize);
        long maximumTileY = FloorDivide(clippedMaximumY, tileSize);
        int count = 0;
        for (long tileY = minimumTileY; tileY <= maximumTileY; tileY++)
        {
            for (long tileX = minimumTileX; tileX <= maximumTileX; tileX++)
            {
                for (int sceneReferenceIndex = 0; sceneReferenceIndex < biome.PixelScenes.Length; sceneReferenceIndex++)
                {
                    int sceneIndex = catalog.FindPixelSceneIndex(biome.PixelScenes[sceneReferenceIndex]);
                    BiomePixelSceneDefinition scene = catalog.PixelScenes[sceneIndex];
                    ulong sceneSalt = StableIdSalt(scene.Id);
                    if (!IsPixelSceneSelected(tileX, tileY, worldSeed, sceneSalt, scene.SpawnChance))
                    {
                        continue;
                    }

                    long worldX = checked((tileX * tileSize) + (tileSize / 2L));
                    long worldY = checked((tileY * tileSize) + (tileSize / 2L));
                    long sceneMinimumX = worldX - (scene.WidthCells / 2L);
                    long sceneMaximumX = sceneMinimumX + scene.WidthCells - 1L;
                    long sceneMinimumY = worldY - (scene.HeightCells / 2L);
                    long sceneMaximumY = sceneMinimumY + scene.HeightCells - 1L;
                    if (!catalog.IsTopologyBiomeAt(biome.Id, sceneMinimumX, sceneMinimumY, config) ||
                        !catalog.IsTopologyBiomeAt(biome.Id, sceneMaximumX, sceneMinimumY, config) ||
                        !catalog.IsTopologyBiomeAt(biome.Id, sceneMinimumX, sceneMaximumY, config) ||
                        !catalog.IsTopologyBiomeAt(biome.Id, sceneMaximumX, sceneMaximumY, config))
                    {
                        continue;
                    }

                    if (sceneMinimumX < minimumX || sceneMaximumX > maximumX ||
                        sceneMinimumY < clippedMinimumY || sceneMaximumY > clippedMaximumY)
                    {
                        continue;
                    }

                    destination[count++] = new BiomeEncounterAnchor(
                        biome.Id,
                        scene.Id,
                        scene.EncounterId,
                        worldX,
                        worldY,
                        scene.WidthCells,
                        scene.HeightCells);
                    if (count == destination.Length)
                    {
                        return count;
                    }
                }
            }
        }

        return count;
    }

    /// <summary>
    /// 收集 Noita Wang tile 中由 Lua / 内建颜色导出的 marker 锚点；调用方提供固定容量目标，
    /// 用于后续将参考地图的 spawn/load marker 转成 Demo 自己的数据化实体、道具与背景场景。
    /// </summary>
    internal static int CollectWangMarkerAnchors(
        BiomeCatalog catalog,
        NoitaWangTerrainCatalog wangTerrain,
        CampaignConfig config,
        ulong worldSeed,
        long minimumX,
        long minimumY,
        long maximumX,
        long maximumY,
        Span<NoitaWangMarkerAnchor> destination)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(wangTerrain);
        ArgumentNullException.ThrowIfNull(config);
        if (minimumX > maximumX || minimumY > maximumY)
        {
            throw new ArgumentException("Wang marker 查询矩形无效。");
        }

        if (destination.IsEmpty)
        {
            return 0;
        }

        int count = 0;
        string lastReferenceBiomeId = string.Empty;
        NoitaWangTerrainSetDefinition? lastSet = null;
        ulong lastSalt = 0UL;
        for (long worldY = minimumY; worldY <= maximumY; worldY++)
        {
            for (long worldX = minimumX; worldX <= maximumX; worldX++)
            {
                if (!TryResolveReferenceBiome(catalog, config, worldX, worldY, out ReferenceBiomeDefinition referenceBiome))
                {
                    continue;
                }

                NoitaWangTerrainSetDefinition set;
                ulong salt;
                if (lastSet is not null && string.Equals(lastReferenceBiomeId, referenceBiome.Id, StringComparison.Ordinal))
                {
                    set = lastSet;
                    salt = lastSalt;
                }
                else
                {
                    if (!wangTerrain.TryFindDefinitionForReferenceBiome(referenceBiome.Id, out set))
                    {
                        continue;
                    }

                    lastSet = set;
                    lastReferenceBiomeId = referenceBiome.Id;
                    lastSalt = StableIdSalt(referenceBiome.Id);
                    salt = lastSalt;
                }

                byte semantic = 0;
                bool sampledBitmap = set.DecodedBitmapCaves is not null &&
                    set.DecodedBitmapCaves.TrySample(worldX, worldY, worldSeed, salt, out semantic);
                if (!sampledBitmap)
                {
                    semantic = set.Decoded.Sample(worldX, worldY, worldSeed, salt);
                }
                if (!DecodedNoitaWangTerrainSet.IsMarker(semantic))
                {
                    continue;
                }

                if (!sampledBitmap &&
                    (FloorRemainder(worldX, DecodedNoitaWangTerrainSet.SemanticPixelScale) != 0 ||
                     FloorRemainder(worldY, DecodedNoitaWangTerrainSet.SemanticPixelScale) != 0))
                {
                    continue;
                }

                int markerIndex = semantic - NoitaWangTerrainCatalog.MarkerSemanticBase;
                NoitaWangMarkerDefinition marker = set.Markers[markerIndex];
                destination[count++] = new NoitaWangMarkerAnchor(
                    referenceBiome.Id,
                    set.Id,
                    marker.Color,
                    marker.Function,
                    marker.Origin,
                    semantic,
                    worldX,
                    worldY);
                if (count == destination.Length)
                {
                    return count;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// 收集全局 buffered Pixel Scene material PNG 中的实体 marker。数据在构建时从参考图片与
    /// RegisterSpawnFunction 声明编译为 C#，运行时不解释或执行 Lua。
    /// </summary>
    internal static int CollectGlobalPixelSceneMarkerAnchors(
        long minimumX,
        long minimumY,
        long maximumX,
        long maximumY,
        Span<NoitaWangMarkerAnchor> destination)
    {
        if (minimumX > maximumX || minimumY > maximumY)
        {
            throw new ArgumentException("Pixel Scene marker 查询矩形无效。");
        }

        int count = 0;
        ReadOnlySpan<NoitaPixelSceneMarkerDefinition> markers = NoitaPixelSceneCatalog.Markers;
        for (int i = 0; i < markers.Length && count < destination.Length; i++)
        {
            ref readonly NoitaPixelSceneMarkerDefinition marker = ref markers[i];
            if (marker.WorldX < minimumX || marker.WorldX > maximumX ||
                marker.WorldY < minimumY || marker.WorldY > maximumY)
            {
                continue;
            }

            destination[count++] = new NoitaWangMarkerAnchor(
                "global-pixel-scene",
                "global-pixel-scene",
                marker.Color,
                marker.Function,
                marker.Origin,
                Semantic: byte.MaxValue,
                marker.WorldX,
                marker.WorldY);
        }

        return count;
    }

    private static bool TryResolveReferenceBiome(
        BiomeCatalog catalog,
        CampaignConfig config,
        long worldX,
        long worldY,
        out ReferenceBiomeDefinition referenceBiome)
    {
        WorldTopologyDefinition topology = catalog.WorldTopology;
        long macroX = FloorDivide(worldX, topology.MacroCellSize);
        long macroY = FloorDivide(worldY - config.SurfaceY, topology.MacroCellSize);
        long mapX = macroX + topology.OriginMacroX;
        long mapY = macroY + topology.OriginMacroY;
        if ((ulong)mapX >= (uint)topology.Width || (ulong)mapY >= (uint)topology.Height)
        {
            referenceBiome = null!;
            return false;
        }

        int referenceBiomeIndex = BiomeCatalog.DecodeReferenceBiomeIndex(topology.MacroRows[(int)mapY], (int)mapX);
        referenceBiome = topology.ReferenceBiomes[referenceBiomeIndex];
        return true;
    }

    private static long MainPathCenterX(
        long worldY,
        CampaignConfig config,
        ulong worldSeed,
        double originNoise)
    {
        long depth = Math.Max(0, worldY - config.SurfaceY);
        ulong salt = worldSeed ^ 0xC0A7_1D07UL;
        double current = Fractal1D(depth * 0.00135, salt, 2);
        double offset = Math.Clamp((current - originNoise) * 0.5, -1.0, 1.0) * config.MainPathWanderCells;
        return config.MainPathEntranceX + (long)Math.Round(offset);
    }

    private static double MainPathOriginNoise(ulong worldSeed)
    {
        return Fractal1D(0, worldSeed ^ 0xC0A7_1D07UL, 2);
    }

    private static double Fractal1D(double x, ulong salt, int octaves)
    {
        double sum = 0.0;
        double amplitude = 1.0;
        double normalization = 0.0;
        for (int octave = 0; octave < octaves; octave++)
        {
            sum += ValueNoise1D(x, salt + ((ulong)octave * 0x9E37_79B9UL)) * amplitude;
            normalization += amplitude;
            x *= 2.03;
            amplitude *= 0.5;
        }

        return sum / normalization;
    }

    private static double Fractal2D(double x, double y, ulong salt, int octaves)
    {
        double sum = 0.0;
        double amplitude = 1.0;
        double normalization = 0.0;
        for (int octave = 0; octave < octaves; octave++)
        {
            sum += ValueNoise2D(x, y, salt + ((ulong)octave * 0x85EB_CA6BUL)) * amplitude;
            normalization += amplitude;
            x *= 2.01;
            y *= 2.07;
            amplitude *= 0.5;
        }

        return sum / normalization;
    }

    private static double ValueNoise1D(double x, ulong salt)
    {
        long x0 = checked((long)Math.Floor(x));
        double t = SmoothFraction(x - x0);
        return Lerp(HashSigned(x0, 0, salt), HashSigned(x0 + 1, 0, salt), t);
    }

    private static double ValueNoise2D(double x, double y, ulong salt)
    {
        long x0 = checked((long)Math.Floor(x));
        long y0 = checked((long)Math.Floor(y));
        double tx = SmoothFraction(x - x0);
        double ty = SmoothFraction(y - y0);
        double top = Lerp(HashSigned(x0, y0, salt), HashSigned(x0 + 1, y0, salt), tx);
        double bottom = Lerp(HashSigned(x0, y0 + 1, salt), HashSigned(x0 + 1, y0 + 1, salt), tx);
        return Lerp(top, bottom, ty);
    }

    private static double HashSigned(long x, long y, ulong salt)
    {
        ulong value = salt;
        unchecked
        {
            value ^= (ulong)x * 0x9E37_79B9_7F4A_7C15UL;
            value ^= (ulong)y * 0xC2B2_AE3D_27D4_EB4FUL;
        }

        value ^= value >> 30;
        value *= 0xBF58_476D_1CE4_E5B9UL;
        value ^= value >> 27;
        value *= 0x94D0_49BB_1331_11EBUL;
        value ^= value >> 31;
        return ((value >> 11) * Inverse53Bit * 2.0) - 1.0;
    }

    private static double HashUnit(long x, long y, ulong salt)
    {
        return (HashSigned(x, y, salt) + 1.0) * 0.5;
    }

    private static long FloorDivide(long value, int divisor)
    {
        long quotient = Math.DivRem(value, divisor, out long remainder);
        return remainder < 0 ? quotient - 1 : quotient;
    }

    private static int FloorRemainder(long value, int divisor)
    {
        long remainder = value % divisor;
        return checked((int)(remainder < 0 ? remainder + divisor : remainder));
    }

    private static double SmoothFraction(double value)
    {
        return value * value * (3.0 - (2.0 * value));
    }

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        double t = Math.Clamp((value - edge0) / (edge1 - edge0), 0.0, 1.0);
        return SmoothFraction(t);
    }

    private static double Lerp(double a, double b, double t)
    {
        return a + ((b - a) * t);
    }

    private sealed class TerrainGenerationState(
        CampaignConfig config,
        BiomeCatalog biomes,
        NoitaWangTerrainCatalog wangTerrain,
        TerrainMaterialPalette palette,
        CompiledBiome[] mainBiomes,
        CompiledBiome[] sideBiomes,
        CompiledWorldTopology worldTopology,
        ResolvedConnection[] connections,
        ResolvedBiomeLandmark[] biomeLandmarks,
        CompiledHolyMountain holyMountain,
        CompiledPortalNetwork portalNetwork,
        CampaignPortalAnchor[] portalAnchors,
        CompiledNoitaSnowcastlePixelSceneCatalog snowcastlePixelScenes,
        CompiledNoitaVegetationCatalog vegetation,
        ulong worldSeed)
    {
        public CampaignConfig Config { get; } = config;

        public BiomeCatalog Biomes { get; } = biomes;

        public NoitaWangTerrainCatalog WangTerrain { get; } = wangTerrain;

        public TerrainMaterialPalette Palette { get; } = palette;

        public CompiledBiome[] MainBiomes { get; } = mainBiomes;

        public CompiledBiome[] SideBiomes { get; } = sideBiomes;

        public CompiledWorldTopology WorldTopology { get; } = worldTopology;

        public ResolvedConnection[] Connections { get; } = connections;

        public ResolvedBiomeLandmark[] BiomeLandmarks { get; } = biomeLandmarks;

        public CompiledHolyMountain HolyMountain { get; } = holyMountain;

        public CompiledPortalNetwork PortalNetwork { get; } = portalNetwork;

        public CampaignPortalAnchor[] PortalAnchors { get; } = portalAnchors;

        public CompiledNoitaSnowcastlePixelSceneCatalog SnowcastlePixelScenes { get; } = snowcastlePixelScenes;

        public CompiledNoitaVegetationCatalog Vegetation { get; } = vegetation;

        public ulong WorldSeed { get; } = worldSeed;
    }

    private readonly ref struct TerrainRowContext(
        ulong worldSeed,
        TerrainGenerationState state,
        CampaignDepthLocation location,
        long pathCenterX,
        int regionIndex,
        CompiledBiome biome,
        TerrainMaterialPalette palette,
        PortalRowContext portalRow,
        ReadOnlySpan<BiomeLandmarkRowSegment> biomeLandmarkSegments,
        ReadOnlySpan<ConnectionRowSegment> connectionSegments)
    {
        public ulong WorldSeed { get; } = worldSeed;

        public TerrainGenerationState State { get; } = state;

        public CampaignDepthLocation Location { get; } = location;

        public long PathCenterX { get; } = pathCenterX;

        public int RegionIndex { get; } = regionIndex;

        public CompiledBiome Biome { get; } = biome;

        public TerrainMaterialPalette Palette { get; } = palette;

        public PortalRowContext PortalRow { get; } = portalRow;

        public ReadOnlySpan<BiomeLandmarkRowSegment> BiomeLandmarkSegments { get; } = biomeLandmarkSegments;

        public ReadOnlySpan<ConnectionRowSegment> ConnectionSegments { get; } = connectionSegments;
    }

    private sealed class CompiledBiome(
        string id,
        float baseTemperature,
        BiomeTerrainGrammarDefinition grammar,
        ushort primary,
        ushort secondary,
        ushort loose,
        ushort structure,
        ushort hazard,
        ushort pool,
        double hazardThreshold,
        ulong salt,
        CompiledPixelScene[] pixelScenes)
    {
        public string Id { get; } = id;

        public float BaseTemperature { get; } = baseTemperature;

        public BiomeTerrainGrammarDefinition Grammar { get; } = grammar;

        public ushort Primary { get; } = primary;

        public ushort Secondary { get; } = secondary;

        public ushort Loose { get; } = loose;

        public ushort Structure { get; } = structure;

        public ushort Hazard { get; } = hazard;

        public ushort Pool { get; } = pool;

        public double HazardThreshold { get; } = hazardThreshold;

        public ulong Salt { get; } = salt;

        public CompiledPixelScene[] PixelScenes { get; } = pixelScenes;
    }

    private sealed class CompiledWorldTopology(
        int macroCellSize,
        int width,
        int height,
        int originMacroX,
        int originMacroY,
        int laboratoryOriginX,
        int laboratoryOriginDepthCells,
        int laboratoryWidthCells,
        int laboratoryHeightCells,
        byte[] laboratoryTerrainMask,
        CompiledTopologyCell[] cells,
        CompiledReferenceBiome[] referenceBiomes)
    {
        public int LaboratoryOriginX { get; } = laboratoryOriginX;

        public int LaboratoryOriginDepthCells { get; } = laboratoryOriginDepthCells;

        public int LaboratoryWidthCells { get; } = laboratoryWidthCells;

        public int LaboratoryHeightCells { get; } = laboratoryHeightCells;

        public byte[] LaboratoryTerrainMask { get; } = laboratoryTerrainMask;

        public ref readonly CompiledReferenceBiome ReferenceBiome(int index)
        {
            return ref referenceBiomes[index];
        }

        public CompiledTopologyCell Resolve(long worldX, long depthCells)
        {
            long mapX = FloorDivide(worldX, macroCellSize) + originMacroX;
            long mapY = FloorDivide(depthCells, macroCellSize) + originMacroY;
            return (ulong)mapX >= (uint)width || (ulong)mapY >= (uint)height
                ? new CompiledTopologyCell(CompiledTopologyCellKind.Legacy, -1, -1)
                : cells[checked(((int)mapY * width) + (int)mapX)];
        }
    }

    private sealed class CompiledPixelScene(
        int widthCells,
        int heightCells,
        double spawnChance,
        ulong salt,
        string encounterId,
        CompiledPixelSceneOperation[] operations)
    {
        public int WidthCells { get; } = widthCells;

        public int HeightCells { get; } = heightCells;

        public double SpawnChance { get; } = spawnChance;

        public ulong Salt { get; } = salt;

        public string EncounterId { get; } = encounterId;

        public CompiledPixelSceneOperation[] Operations { get; } = operations;
    }

    private sealed class CompiledHolyMountain(
        float baseTemperature,
        ushort shellMaterial,
        ushort platformMaterial,
        int shellThicknessCells,
        CompiledHolyMountainOperation[] operations)
    {
        public float BaseTemperature { get; } = baseTemperature;

        public ushort ShellMaterial { get; } = shellMaterial;

        public ushort PlatformMaterial { get; } = platformMaterial;

        public int ShellThicknessCells { get; } = shellThicknessCells;

        public CompiledHolyMountainOperation[] Operations { get; } = operations;
    }

    private readonly record struct CompiledHolyMountainOperation(
        ushort Material,
        int X,
        int Y,
        int Width,
        int Height)
    {
        public bool Contains(long x, long y)
        {
            return x >= X && x < X + (long)Width &&
                y >= Y && y < Y + (long)Height;
        }
    }

    private readonly record struct CompiledPortalNetwork(
        int PortalsPerHolyMountain,
        int SourceOffsetAboveBoundaryCells,
        int TriggerHalfWidthCells,
        int TriggerHalfHeightCells,
        ushort EyeShellMaterial,
        ushort TeleportatiumMaterial,
        int BasinTopOffsetCells,
        int BasinHalfWidthCells,
        int BasinDepthCells,
        int MinimumPowerCells);

    private readonly record struct CompiledConnection(
        CompiledConnectionKind Kind,
        int FromRegionIndex,
        int ToRegionIndex,
        int Direction,
        int OffsetCells,
        int HalfWidthCells,
        int CorridorHalfWidthCells,
        int FromLocalDepthCells,
        int ToLocalDepthCells,
        ushort GateMaterial,
        int SideBiomeIndex);

    private readonly record struct ResolvedConnection(
        CompiledConnection Connection,
        long StartY,
        long EndY,
        long AnchorY,
        long CenterX,
        long StartPathX,
        long EndPathX,
        long AnchorPathX);

    private readonly record struct ConnectionRowSegment(
        ConnectionRowSegmentKind Kind,
        long MinimumX,
        long MaximumX,
        long GateMinimumX,
        long GateMaximumX,
        ushort Material,
        ushort GateMaterial,
        int SideBiomeIndex);

    private readonly record struct ResolvedBiomeLandmark(
        int RegionIndex,
        long MinimumX,
        long MinimumY,
        long MaximumX,
        long MaximumY,
        CompiledPixelSceneOperation[] Operations);

    private readonly record struct BiomeLandmarkRowSegment(
        int LandmarkIndex,
        int LocalY,
        long MinimumX,
        long MaximumX);

    private readonly record struct PortalRowContext(
        PortalRowKind Kind,
        int FirstPortalIndex,
        long RelativeY);

    private readonly record struct CompiledPixelSceneOperation(
        CompiledPixelSceneOperationKind Kind,
        ushort Material,
        int X,
        int Y,
        int Width,
        int Height)
    {
        public bool Contains(int x, int y)
        {
            int localX = x - X;
            int localY = y - Y;
            if ((uint)localX >= (uint)Width || (uint)localY >= (uint)Height)
            {
                return false;
            }

            if (Kind == CompiledPixelSceneOperationKind.Rectangle)
            {
                return true;
            }

            long dx = (localX * 2L) + 1L - Width;
            long dy = (localY * 2L) + 1L - Height;
            long widthSquared = (long)Width * Width;
            long heightSquared = (long)Height * Height;
            return (dx * dx * heightSquared) + (dy * dy * widthSquared) <=
                widthSquared * heightSquared;
        }
    }

    private enum CompiledConnectionKind : byte
    {
        SideBiome,
        SecretSideBiome,
        VerticalShortcut,
        VerticalSideBiome,
    }

    private enum CompiledTopologyCellKind : byte
    {
        Legacy,
        MainBiome,
        SideBiome,
        Solid,
        Lava,
        HolyMountain,
        FixedLaboratory,
        Empty,
        Water,
        Clouds,
        SurfaceHills,
        SurfaceDesert,
        SurfaceWinter,
        Mountain,
        GenericCave,
        GenericStructure,
    }

    private readonly record struct CompiledTopologyCell(
        CompiledTopologyCellKind Kind,
        int BiomeIndex,
        int ReferenceBiomeIndex);

    private readonly record struct CompiledReferenceBiome(
        string Id,
        ulong Salt,
        ReferenceMountainKind Mountain,
        byte[] TerrainMask,
        ReferenceTerrainMaskAccent MaskAccent,
        DecodedNoitaWangTerrainSet? WangTerrain,
        DecodedNoitaBitmapCaves? BitmapCaves,
        ushort[] WangMaterials,
        ushort[] ProtectedWangMaterials,
        int WangMapWidth,
        int WangMapHeight,
        CompiledWangMaterialLayer[] WangMaterialLayers,
        ushort[] RandomWangMaterials);

    private readonly record struct CompiledWangMaterialLayer(
        ushort Material,
        bool Enabled,
        float AddPerlin,
        float AddPerlinScaleX,
        float AddPerlinScaleY,
        bool IsPolygon,
        bool IsRare,
        float LimitMaxY,
        float LimitMinY,
        bool LimitY,
        int MaterialIndex,
        float MaterialMax,
        float MaterialMin,
        bool RarePolkaIsBoxed,
        float RarePolkaProbability,
        float RarePolkaRadiusHigh,
        float RarePolkaRadiusLow,
        float RareRequiredMax,
        float RareRequiredMin,
        float RareScaleX,
        float RareScaleY,
        bool RareUsePerlin,
        bool RareUsePolka,
        ulong MaterialSalt);

    private enum ReferenceTerrainMaskAccent : byte
    {
        Gravel,
        Water,
        Ice,
    }

    private enum ReferenceMountainKind : byte
    {
        Generic,
        LeftEntrance,
        Hall,
        Right,
        Top,
        LeftStub,
        RightStub,
        FloatingIsland,
        Lake,
    }

    private enum ConnectionRowSegmentKind : byte
    {
        Material,
        Access,
        SideBiome,
    }

    private enum PortalRowKind : byte
    {
        None,
        Chamber,
        Basins,
    }

    private enum CompiledPixelSceneOperationKind : byte
    {
        Rectangle,
        Ellipse,
    }

    private readonly record struct TerrainMaterialPalette(
        ushort Empty,
        ushort Sand,
        ushort Dirt,
        ushort PackedSand,
        ushort PackedDirt,
        ushort Water,
        ushort Lava,
        ushort Stone,
        ushort BoundaryStone,
        ushort Ice,
        ushort Gravel,
        ushort PackedGravel,
        ushort Crystal,
        ushort Metal,
        ushort Wood,
        ushort Smoke,
        ushort Glass,
        ushort Blood,
        ushort BoneStatic,
        ushort CheeseStatic,
        ushort Mud,
        ushort SandPetrify,
        ushort SnowSticky);
}

internal readonly record struct BiomeEncounterAnchor(
    string BiomeId,
    string PixelSceneId,
    string EncounterId,
    long WorldX,
    long WorldY,
    int WidthCells,
    int HeightCells);

internal readonly record struct BiomeLandmarkAnchor(
    int RegionIndex,
    string LandmarkId,
    string DisplayName,
    string EncounterId,
    long WorldX,
    long WorldY,
    int WidthCells,
    int HeightCells);

internal readonly record struct CampaignPortalAnchor(
    int HolyMountainIndex,
    int PortalIndex,
    long SourceX,
    long SourceY,
    long DestinationX,
    long DestinationY);

internal readonly record struct HolyMountainLandmarkAnchor(
    int HolyMountainIndex,
    string Id,
    string Kind,
    long WorldX,
    long WorldY);

internal readonly record struct NoitaWangMarkerAnchor(
    string ReferenceBiomeId,
    string WangSetId,
    string MarkerColor,
    string Function,
    string Origin,
    byte Semantic,
    long WorldX,
    long WorldY);
