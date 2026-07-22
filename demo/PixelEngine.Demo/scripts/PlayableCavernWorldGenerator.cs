using PixelEngine.Hosting;
using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 基于全局坐标的确定性流式战役地形生成器；生成自然地表、八个主路径 biome、七个 Holy Mountain 与无限侧区。
/// </summary>
public sealed class PlayableCavernWorldGenerator : IStreamingProceduralWorldGenerator
{
    private TerrainGenerationState? _state;

    /// <summary>
    /// 程序化场景键，同时也是入口 Behaviour 的完整类型名。
    /// </summary>
    public const string Key = "PixelEngine.Demo.PlayableWorldDirector";

    /// <summary>
    /// 当前生成算法与 region 存档兼容身份；改变不兼容算法时必须升级。
    /// </summary>
    public const string PersistenceKey = "showcase-campaign-v6";

    /// <summary>
    /// 原点安全区的地表 Y。
    /// </summary>
    public const int SafeSurfaceY = 224;

    /// <summary>
    /// 默认玩家出生 X。
    /// </summary>
    public const float PlayerSpawnX = 0f;

    /// <summary>
    /// 默认玩家出生 Y；玩家会短距离落到安全地表。
    /// </summary>
    public const float PlayerSpawnY = SafeSurfaceY - 32f;

    internal const ulong Seed = 0x5049_5845_4C53_4248;
    internal const int SeaLevelY = 242;
    private const long SafeInnerRadius = 112;
    private const long SafeOuterRadius = 320;
    private const int MaximumConnectionSegmentsPerRow = 16;
    private const int MaximumBiomeLandmarkSegmentsPerRow = 8;
    private const double Inverse53Bit = 1.0 / 9_007_199_254_740_992.0;

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
        ulong worldSeed = request.WorldSeedOverride ?? config.InitialRunSeed;
        if (request.Materials is not null)
        {
            Volatile.Write(ref _state, CreateGenerationState(request.Materials, config, biomes, worldSeed));
        }

        return ProceduralWorldDescriptor.CreateInfinite(
            worldSeed,
            initialFocusX: (long)PlayerSpawnX,
            initialFocusY: config.SurfaceY - 16,
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
        BiomeCatalog? biomes = null)
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
        long depth = worldY - surfaceY;
        if (depth < 0)
        {
            bool lakeColumn = surfaceY > SeaLevelY + 3;
            return lakeColumn && worldY >= SeaLevelY && Math.Abs((double)worldX) >= SafeInnerRadius
                ? row.Palette.Water
                : row.Palette.Empty;
        }

        if (row.Location.Kind == CampaignDepthKind.HolyMountain)
        {
            if (TrySelectHolyMountainMaterial(worldX, in row, out ushort holyMountainMaterial))
            {
                return holyMountainMaterial;
            }
        }

        if (row.Location.DepthCells >= config.CampaignStartDepthCells)
        {
            if (Math.Abs(worldX - row.PathCenterX) <= config.MainPathHalfWidthCells)
            {
                return row.Palette.Empty;
            }
        }

        if (TrySelectConnectionMaterial(
                worldX,
                worldY,
                sideBiomeOnly: false,
                in row,
                out ushort connectionMaterial))
        {
            return connectionMaterial;
        }

        if (!row.BiomeLandmarkSegments.IsEmpty &&
            TrySelectBiomeLandmarkMaterial(worldX, in row, out ushort landmarkMaterial))
        {
            return landmarkMaterial;
        }

        if (TrySelectConnectionMaterial(
                worldX,
                worldY,
                sideBiomeOnly: true,
                in row,
                out connectionMaterial))
        {
            return connectionMaterial;
        }

        if (depth <= soilDepth)
        {
            return surfaceY < 108
                ? row.Palette.Ice
                : surfaceY >= SeaLevelY - 5 || moisture < -0.28
                    ? row.Palette.Sand
                    : row.Palette.Dirt;
        }

        if (depth <= soilDepth + 7)
        {
            return moisture < -0.5 ? row.Palette.Sand : row.Palette.Dirt;
        }

        bool protectedSpawn = Math.Abs((double)worldX) < SafeOuterRadius && depth < 160;
        return SelectBiomeMaterial(worldX, worldY, protectedSpawn, row.Biome, in row);
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
        if (TrySelectPixelSceneMaterial(worldX, worldY, biome, in row, out ushort sceneMaterial))
        {
            return sceneMaterial;
        }

        if (IsBiomeOpenAt(worldX, worldY, biome, row.WorldSeed))
        {
            return row.Palette.Empty;
        }

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
                CompiledBiome biome = ResolveTemperatureBiome(
                    sampleWorldX,
                    sampleWorldY,
                    regionIndex,
                    state);
                float temperature = location.Kind == CampaignDepthKind.HolyMountain
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
        TerrainGenerationState state)
    {
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
        CampaignConfig config = CampaignConfig.Load(context.Config);
        BiomeCatalog biomes = BiomeCatalog.Load(context.Config, config);
        ulong worldSeed = config.InitialRunSeed;
        TerrainGenerationState state = CreateGenerationState(
            context.Materials,
            config,
            biomes,
            worldSeed);
        TerrainMaterialPalette palette = state.Palette;
        _ = context.Edit.ClearRect(0, 0, context.WidthCells - 1, context.HeightCells - 1);
        Span<int> surfaces = stackalloc int[context.WidthCells];
        Span<int> soilDepths = stackalloc int[context.WidthCells];
        Span<double> moisture = stackalloc double[context.WidthCells];
        double mainPathOriginNoise = MainPathOriginNoise(worldSeed);
        Span<ConnectionRowSegment> connectionSegments =
            stackalloc ConnectionRowSegment[MaximumConnectionSegmentsPerRow];
        Span<BiomeLandmarkRowSegment> landmarkSegments =
            stackalloc BiomeLandmarkRowSegment[MaximumBiomeLandmarkSegmentsPerRow];
        long previewOriginX = -(context.WidthCells / 2L);
        for (int x = 0; x < context.WidthCells; x++)
        {
            long worldX = previewOriginX + x;
            surfaces[x] = SurfaceYAt(worldX, worldSeed);
            moisture[x] = MoistureAt(worldX, worldSeed);
            soilDepths[x] = 5 + (int)Math.Round(
                (ValueNoise1D(worldX * 0.011, worldSeed ^ 0x5A17UL) + 1.0) * 2.5);
        }

        for (int y = 0; y < context.HeightCells; y++)
        {
            CampaignDepthLocation location = config.ResolveLocation(y);
            int regionIndex = Math.Clamp(location.RegionIndex, 0, CampaignConfig.RequiredRegionCount - 1);
            CompiledBiome biome = state.MainBiomes[regionIndex];
            int connectionSegmentCount = BuildConnectionRowSegments(
                state,
                y,
                connectionSegments);
            int landmarkSegmentCount = BuildBiomeLandmarkRowSegments(
                state,
                regionIndex,
                y,
                landmarkSegments);
            PortalRowContext portalRow = BuildPortalRowContext(
                state,
                location,
                regionIndex,
                y);
            long pathCenterX = location.DepthCells >= config.CampaignStartDepthCells
                ? MainPathCenterX(y, config, worldSeed, mainPathOriginNoise)
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
            int runStart = 0;
            ushort runMaterial = SelectMaterialIncludingPortal(
                previewOriginX,
                y,
                surfaces[0],
                soilDepths[0],
                moisture[0],
                in rowContext);
            for (int x = 1; x <= context.WidthCells; x++)
            {
                ushort material = x == context.WidthCells
                    ? ushort.MaxValue
                    : SelectMaterialIncludingPortal(
                        previewOriginX + x,
                        y,
                        surfaces[x],
                        soilDepths[x],
                        moisture[x],
                        in rowContext);
                if (material == runMaterial)
                {
                    continue;
                }

                if (runMaterial != palette.Empty)
                {
                    _ = context.Edit.PaintRect(runStart, y, x - 1, y, new MaterialId(runMaterial));
                }

                runStart = x;
                runMaterial = material;
            }
        }
    }

    private static ushort SelectMaterialIncludingPortal(
        long worldX,
        long worldY,
        int surfaceY,
        int soilDepth,
        double moisture,
        in TerrainRowContext row)
    {
        return row.PortalRow.Kind != PortalRowKind.None &&
            TrySelectPortalTerrain(worldX, in row, out ushort portalTerrainMaterial)
                ? portalTerrainMaterial
                : SelectMaterial(
                    worldX,
                    worldY,
                    surfaceY,
                    soilDepth,
                    moisture,
                    in row);
    }

    private static TerrainGenerationState CreateGenerationState(
        IMaterialQuery materials,
        CampaignConfig config,
        BiomeCatalog biomes,
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
        CampaignPortalAnchor[] portalAnchors = ResolvePortalAnchors(
            config,
            biomes.PortalNetwork,
            worldSeed);
        return new TerrainGenerationState(
            config,
            biomes,
            ResolvePalette(materials),
            mainBiomes,
            sideBiomes,
            resolvedConnections,
            biomeLandmarks,
            holyMountain,
            portalNetwork,
            portalAnchors);
    }

    private static TerrainMaterialPalette ResolvePalette(IMaterialQuery materials)
    {
        return new TerrainMaterialPalette(
            ResolveRequired(materials, "empty"),
            ResolveRequired(materials, "sand"),
            ResolveRequired(materials, "dirt"),
            ResolveRequired(materials, "water"),
            ResolveRequired(materials, "lava"),
            ResolveRequired(materials, "stone"),
            ResolveRequired(materials, "ice"),
            ResolveRequired(materials, "gravel"),
            ResolveRequired(materials, "crystal"));
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
        TerrainMaterialPalette palette,
        CompiledBiome[] mainBiomes,
        CompiledBiome[] sideBiomes,
        ResolvedConnection[] connections,
        ResolvedBiomeLandmark[] biomeLandmarks,
        CompiledHolyMountain holyMountain,
        CompiledPortalNetwork portalNetwork,
        CampaignPortalAnchor[] portalAnchors)
    {
        public CampaignConfig Config { get; } = config;

        public BiomeCatalog Biomes { get; } = biomes;

        public TerrainMaterialPalette Palette { get; } = palette;

        public CompiledBiome[] MainBiomes { get; } = mainBiomes;

        public CompiledBiome[] SideBiomes { get; } = sideBiomes;

        public ResolvedConnection[] Connections { get; } = connections;

        public ResolvedBiomeLandmark[] BiomeLandmarks { get; } = biomeLandmarks;

        public CompiledHolyMountain HolyMountain { get; } = holyMountain;

        public CompiledPortalNetwork PortalNetwork { get; } = portalNetwork;

        public CampaignPortalAnchor[] PortalAnchors { get; } = portalAnchors;
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
        ushort Water,
        ushort Lava,
        ushort Stone,
        ushort Ice,
        ushort Gravel,
        ushort Crystal);
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
