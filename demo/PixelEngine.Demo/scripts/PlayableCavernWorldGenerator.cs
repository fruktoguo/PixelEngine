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
    public const string PersistenceKey = "showcase-campaign-v2";

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
    private const double Inverse53Bit = 1.0 / 9_007_199_254_740_992.0;

    /// <inheritdoc />
    public ProceduralWorldDescriptor Describe(in ProceduralWorldBuildRequest request)
    {
        TerrainGenerationState? currentState = Volatile.Read(ref _state);
        CampaignConfig config = request.Config is null
            ? currentState?.Config ?? CampaignConfig.BuiltinDefault
            : CampaignConfig.Load(request.Config);
        if (request.Materials is not null)
        {
            Volatile.Write(ref _state, CreateGenerationState(request.Materials, config));
        }

        ulong worldSeed = request.WorldSeedOverride ?? config.InitialRunSeed;
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
            TerrainGenerationState created = CreateGenerationState(context.Materials, CampaignConfig.BuiltinDefault);
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
        CampaignConfig? config = null)
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

        TerrainGenerationState state = CreateGenerationState(
            materials,
            config ?? CampaignConfig.BuiltinDefault);
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
        ReadOnlySpan<ushort> regionRock = state.RegionRock;
        ReadOnlySpan<ushort> regionLoose = state.RegionLoose;
        ReadOnlySpan<ushort> regionHazard = state.RegionHazard;
        Span<int> surfaces = stackalloc int[sizeCells];
        Span<int> soilDepths = stackalloc int[sizeCells];
        Span<double> moisture = stackalloc double[sizeCells];
        double mainPathOriginNoise = MainPathOriginNoise(worldSeed);
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
            long pathCenterX = location.DepthCells >= config.CampaignStartDepthCells
                ? MainPathCenterX(worldY, config, worldSeed, mainPathOriginNoise)
                : config.MainPathEntranceX;
            double hazardThreshold = HazardThreshold(config.Regions[regionIndex].HazardFrequency);
            TerrainRowContext rowContext = new(
                worldSeed,
                config,
                in location,
                pathCenterX,
                regionIndex,
                hazardThreshold,
                in palette,
                regionRock,
                regionLoose,
                regionHazard);
            int row = localY * sizeCells;
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
        }

        PopulateTemperature(
            materialCells,
            sizeCells,
            temperatureCells,
            temperatureSizeCells,
            originCellY,
            config,
            in palette,
            regionHazard);
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
            long distanceX = Math.Abs(worldX - row.PathCenterX);
            int shell = 8;
            if (distanceX <= row.Config.HolyMountainHalfWidthCells + shell)
            {
                bool passage = distanceX <= row.Config.MainPathHalfWidthCells + 4;
                bool cap = row.Location.LocalDepthCells < shell ||
                    row.Location.LocalDepthCells >= row.Config.HolyMountainHeightCells - shell;
                bool wall = distanceX >= row.Config.HolyMountainHalfWidthCells;
                bool platform = row.Location.LocalDepthCells is >= 60 and <= 64 &&
                    distanceX > row.Config.MainPathHalfWidthCells + 20;
                return wall || (cap && !passage)
                    ? row.Palette.HolyMountainShell
                    : platform
                        ? row.Palette.HolyMountainPlatform
                        : row.Palette.Empty;
            }
        }

        if (row.Location.DepthCells >= row.Config.CampaignStartDepthCells)
        {
            if (Math.Abs(worldX - row.PathCenterX) <= row.Config.MainPathHalfWidthCells)
            {
                return row.Palette.Empty;
            }
        }

        if (IsCaveAt(worldX, worldY, surfaceY, row.WorldSeed))
        {
            return row.Palette.Empty;
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
        double strata = Fractal2D(
            worldX * 0.034,
            worldY * 0.031,
            row.WorldSeed ^ (0xDE90517UL + ((ulong)row.RegionIndex * 0x85EBUL)),
            2);
        return !protectedSpawn && depth > 28 && strata > row.HazardThreshold
            ? row.RegionHazard[row.RegionIndex]
            : strata > 0.32
                ? row.RegionLoose[row.RegionIndex]
                : depth > 70 && strata < -0.70
                    ? row.Palette.Crystal
                    : row.RegionRock[row.RegionIndex];
    }

    private static void PopulateTemperature(
        ReadOnlySpan<ushort> materialCells,
        int sizeCells,
        Span<Half> temperatureCells,
        int temperatureSizeCells,
        long originCellY,
        CampaignConfig config,
        in TerrainMaterialPalette palette,
        ReadOnlySpan<ushort> regionHazard)
    {
        int cellScale = sizeCells / temperatureSizeCells;
        for (int temperatureY = 0; temperatureY < temperatureSizeCells; temperatureY++)
        {
            long sampleWorldY = originCellY + (temperatureY * cellScale) + (cellScale / 2);
            CampaignDepthLocation location = config.ResolveLocation(sampleWorldY);
            int regionIndex = Math.Clamp(location.RegionIndex, 0, CampaignConfig.RequiredRegionCount - 1);
            float temperature = location.Kind == CampaignDepthKind.HolyMountain
                ? 20f
                : config.Regions[regionIndex].BaseTemperature;
            for (int temperatureX = 0; temperatureX < temperatureSizeCells; temperatureX++)
            {
                int startY = temperatureY * cellScale;
                int startX = temperatureX * cellScale;
                bool containsHotHazard = false;
                for (int y = 0; y < cellScale && !containsHotHazard; y++)
                {
                    int row = (startY + y) * sizeCells;
                    for (int x = 0; x < cellScale; x++)
                    {
                        ushort material = materialCells[row + startX + x];
                        if (material == palette.Lava && regionHazard[regionIndex] == palette.Lava)
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

    /// <summary>
    /// 在有限 Editor 预览画布中显示以世界原点为中心的同源地形切片。
    /// </summary>
    internal static void PopulateAuthoringWorld(in AuthoringWorldPreviewContext context)
    {
        TerrainGenerationState state = CreateGenerationState(context.Materials, CampaignConfig.BuiltinDefault);
        CampaignConfig config = state.Config;
        TerrainMaterialPalette palette = state.Palette;
        ReadOnlySpan<ushort> regionRock = state.RegionRock;
        ReadOnlySpan<ushort> regionLoose = state.RegionLoose;
        ReadOnlySpan<ushort> regionHazard = state.RegionHazard;
        _ = context.Edit.ClearRect(0, 0, context.WidthCells - 1, context.HeightCells - 1);
        Span<int> surfaces = stackalloc int[context.WidthCells];
        Span<int> soilDepths = stackalloc int[context.WidthCells];
        Span<double> moisture = stackalloc double[context.WidthCells];
        double mainPathOriginNoise = MainPathOriginNoise(Seed);
        long previewOriginX = -(context.WidthCells / 2L);
        for (int x = 0; x < context.WidthCells; x++)
        {
            long worldX = previewOriginX + x;
            surfaces[x] = SurfaceYAt(worldX);
            moisture[x] = MoistureAt(worldX, Seed);
            soilDepths[x] = 5 + (int)Math.Round((ValueNoise1D(worldX * 0.011, Seed ^ 0x5A17UL) + 1.0) * 2.5);
        }

        for (int y = 0; y < context.HeightCells; y++)
        {
            CampaignDepthLocation location = config.ResolveLocation(y);
            int regionIndex = Math.Clamp(location.RegionIndex, 0, CampaignConfig.RequiredRegionCount - 1);
            long pathCenterX = location.DepthCells >= config.CampaignStartDepthCells
                ? MainPathCenterX(y, config, Seed, mainPathOriginNoise)
                : config.MainPathEntranceX;
            double hazardThreshold = HazardThreshold(config.Regions[regionIndex].HazardFrequency);
            TerrainRowContext rowContext = new(
                Seed,
                config,
                in location,
                pathCenterX,
                regionIndex,
                hazardThreshold,
                in palette,
                regionRock,
                regionLoose,
                regionHazard);
            int runStart = 0;
            ushort runMaterial = SelectMaterial(
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
                    : SelectMaterial(
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

    private static TerrainGenerationState CreateGenerationState(
        IMaterialQuery materials,
        CampaignConfig config)
    {
        ushort[] regionRock = new ushort[CampaignConfig.RequiredRegionCount];
        ushort[] regionLoose = new ushort[CampaignConfig.RequiredRegionCount];
        ushort[] regionHazard = new ushort[CampaignConfig.RequiredRegionCount];
        ResolveRegionMaterials(materials, config, regionRock, regionLoose, regionHazard);
        return new TerrainGenerationState(
            config,
            ResolvePalette(materials, config),
            regionRock,
            regionLoose,
            regionHazard);
    }

    private static TerrainMaterialPalette ResolvePalette(IMaterialQuery materials, CampaignConfig config)
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
            ResolveRequired(materials, "crystal"),
            ResolveRequired(materials, config.HolyMountainShellMaterial),
            ResolveRequired(materials, config.HolyMountainPlatformMaterial));
    }

    private static void ResolveRegionMaterials(
        IMaterialQuery materials,
        CampaignConfig config,
        Span<ushort> regionRock,
        Span<ushort> regionLoose,
        Span<ushort> regionHazard)
    {
        for (int i = 0; i < CampaignConfig.RequiredRegionCount; i++)
        {
            CampaignRegionDefinition region = config.Regions[i];
            regionRock[i] = ResolveRequired(materials, region.RockMaterial);
            regionLoose[i] = ResolveRequired(materials, region.LooseMaterial);
            regionHazard[i] = ResolveRequired(materials, region.HazardMaterial);
        }
    }

    private static double HazardThreshold(double frequency)
    {
        return frequency <= 0.0 ? double.PositiveInfinity : 0.58 - (frequency * 3.0);
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
        TerrainMaterialPalette palette,
        ushort[] regionRock,
        ushort[] regionLoose,
        ushort[] regionHazard)
    {
        public CampaignConfig Config { get; } = config;

        public TerrainMaterialPalette Palette { get; } = palette;

        public ushort[] RegionRock { get; } = regionRock;

        public ushort[] RegionLoose { get; } = regionLoose;

        public ushort[] RegionHazard { get; } = regionHazard;
    }

    private readonly ref struct TerrainRowContext
    {
        public TerrainRowContext(
            ulong worldSeed,
            CampaignConfig config,
            in CampaignDepthLocation location,
            long pathCenterX,
            int regionIndex,
            double hazardThreshold,
            in TerrainMaterialPalette palette,
            ReadOnlySpan<ushort> regionRock,
            ReadOnlySpan<ushort> regionLoose,
            ReadOnlySpan<ushort> regionHazard)
        {
            WorldSeed = worldSeed;
            Config = config;
            Location = location;
            PathCenterX = pathCenterX;
            RegionIndex = regionIndex;
            HazardThreshold = hazardThreshold;
            Palette = palette;
            RegionRock = regionRock;
            RegionLoose = regionLoose;
            RegionHazard = regionHazard;
        }

        public ulong WorldSeed { get; }

        public CampaignConfig Config { get; }

        public CampaignDepthLocation Location { get; }

        public long PathCenterX { get; }

        public int RegionIndex { get; }

        public double HazardThreshold { get; }

        public TerrainMaterialPalette Palette { get; }

        public ReadOnlySpan<ushort> RegionRock { get; }

        public ReadOnlySpan<ushort> RegionLoose { get; }

        public ReadOnlySpan<ushort> RegionHazard { get; }
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
        ushort Crystal,
        ushort HolyMountainShell,
        ushort HolyMountainPlatform);
}
