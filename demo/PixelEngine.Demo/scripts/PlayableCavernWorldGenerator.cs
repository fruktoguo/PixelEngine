using PixelEngine.Hosting;
using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 基于全局坐标的确定性流式沙盒地形生成器；生成山脉、丘陵、盆地、湖泊、土层、洞穴与深层矿脉。
/// </summary>
public sealed class PlayableCavernWorldGenerator : IStreamingProceduralWorldGenerator
{
    /// <summary>
    /// 程序化场景键，同时也是入口 Behaviour 的完整类型名。
    /// </summary>
    public const string Key = "PixelEngine.Demo.PlayableWorldDirector";

    /// <summary>
    /// 当前生成算法与 region 存档兼容身份；改变不兼容算法时必须升级。
    /// </summary>
    public const string PersistenceKey = "showcase-infinite-sandbox-v1";

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
        _ = request;
        return ProceduralWorldDescriptor.CreateInfinite(
            worldSeed: Seed,
            initialFocusX: (long)PlayerSpawnX,
            initialFocusY: SafeSurfaceY - 16,
            persistenceKey: PersistenceKey);
    }

    /// <inheritdoc />
    public void PopulateChunk(in ProceduralChunkBuildContext context)
    {
        PopulateChunkCore(
            context.Materials,
            context.OriginCellX,
            context.OriginCellY,
            context.SizeCells,
            context.TemperatureSizeCells,
            context.MaterialCells,
            context.TemperatureCells);
    }

    internal static void PopulateChunkForBenchmark(
        IMaterialQuery materials,
        int chunkX,
        int chunkY,
        Span<ushort> materialCells,
        Span<Half> temperatureCells)
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

        PopulateChunkCore(
            materials,
            (long)chunkX * SizeCells,
            (long)chunkY * SizeCells,
            SizeCells,
            TemperatureSizeCells,
            materialCells,
            temperatureCells);
    }

    private static void PopulateChunkCore(
        IMaterialQuery materials,
        long originCellX,
        long originCellY,
        int sizeCells,
        int temperatureSizeCells,
        Span<ushort> materialCells,
        Span<Half> temperatureCells)
    {
        TerrainMaterialPalette palette = ResolvePalette(materials);
        for (int localX = 0; localX < sizeCells; localX++)
        {
            long worldX = originCellX + localX;
            int surfaceY = SurfaceYAt(worldX);
            double moisture = MoistureAt(worldX);
            int soilDepth = 5 + (int)Math.Round((ValueNoise1D(worldX * 0.011, Seed ^ 0x5A17UL) + 1.0) * 2.5);
            for (int localY = 0; localY < sizeCells; localY++)
            {
                long worldY = originCellY + localY;
                materialCells[(localY * sizeCells) + localX] = SelectMaterial(
                    worldX,
                    worldY,
                    surfaceY,
                    soilDepth,
                    moisture,
                    in palette);
            }
        }

        PopulateTemperature(
            materialCells,
            sizeCells,
            temperatureCells,
            temperatureSizeCells,
            palette.Lava);
    }

    /// <summary>
    /// 返回任意全局 X 的确定性地表高度，供预览和自动化验证复用。
    /// </summary>
    internal static int SurfaceYAt(long worldX)
    {
        double x = worldX;
        double warp = Fractal1D(x * 0.00072, Seed ^ 0xA11CEUL, 3) * 260.0;
        double warpedX = x + warp;
        double continental = Fractal1D(warpedX * 0.00048, Seed ^ 0xC0171E17UL, 5);
        double mountainRegion = SmoothStep(
            -0.18,
            0.58,
            Fractal1D((warpedX - 7_900.0) * 0.00023, Seed ^ 0xBADC0FFEEUL, 4));
        double ridgeBase = 1.0 - Math.Abs(Fractal1D(warpedX * 0.00185, Seed ^ 0x718D6EUL, 5));
        double ridges = ridgeBase * ridgeBase * ridgeBase;
        double basin = SmoothStep(
            0.28,
            0.72,
            Fractal1D((warpedX + 13_700.0) * 0.00039, Seed ^ 0xBA51AUL, 4));
        double hills = (Fractal1D(warpedX * 0.0048, Seed ^ 0x41115UL, 4) * 25.0) +
            (Fractal1D(warpedX * 0.016, Seed ^ 0x5CA1EUL, 2) * 6.0);
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
        long depth = worldY - surfaceY;
        if (depth < 24 || (Math.Abs((double)worldX) < SafeOuterRadius && depth < 104))
        {
            return false;
        }

        double broad = Fractal2D(worldX * 0.0125, worldY * 0.0145, Seed ^ 0xCA7EUL, 3);
        double tunnel = Math.Abs(Fractal2D(worldX * 0.0062, worldY * 0.0091, Seed ^ 0x71A9E1UL, 2));
        double threshold = depth < 64 ? 0.73 : depth < 180 ? 0.62 : 0.57;
        return broad - (tunnel * 0.24) > threshold;
    }

    private static ushort SelectMaterial(
        long worldX,
        long worldY,
        int surfaceY,
        int soilDepth,
        double moisture,
        in TerrainMaterialPalette palette)
    {
        long depth = worldY - surfaceY;
        if (depth < 0)
        {
            bool lakeColumn = surfaceY > SeaLevelY + 3;
            return lakeColumn && worldY >= SeaLevelY && Math.Abs((double)worldX) >= SafeInnerRadius
                ? palette.Water
                : palette.Empty;
        }

        if (IsCaveAt(worldX, worldY, surfaceY))
        {
            return palette.Empty;
        }

        if (worldY > 560 &&
            Fractal2D(worldX * 0.008, worldY * 0.010, Seed ^ 0x1A7AUL, 3) > 0.72)
        {
            return palette.Lava;
        }

        if (depth <= soilDepth)
        {
            return surfaceY < 108
                ? palette.Ice
                : surfaceY >= SeaLevelY - 5 || moisture < -0.28
                ? palette.Sand
                : palette.Dirt;
        }

        if (depth <= soilDepth + 7)
        {
            return moisture < -0.5 ? palette.Sand : palette.Dirt;
        }

        double deposit = Fractal2D(worldX * 0.034, worldY * 0.031, Seed ^ 0xDE90517UL, 2);
        return depth > 70 && deposit > 0.82
            ? palette.Crystal
            : depth > 28 && deposit < -0.58
                ? palette.Gravel
                : palette.Stone;
    }

    private static void PopulateTemperature(
        ReadOnlySpan<ushort> materialCells,
        int sizeCells,
        Span<Half> temperatureCells,
        int temperatureSizeCells,
        ushort lava)
    {
        int cellScale = sizeCells / temperatureSizeCells;
        for (int temperatureY = 0; temperatureY < temperatureSizeCells; temperatureY++)
        {
            for (int temperatureX = 0; temperatureX < temperatureSizeCells; temperatureX++)
            {
                bool containsLava = false;
                int startY = temperatureY * cellScale;
                int startX = temperatureX * cellScale;
                for (int y = 0; y < cellScale && !containsLava; y++)
                {
                    int row = (startY + y) * sizeCells;
                    for (int x = 0; x < cellScale; x++)
                    {
                        if (materialCells[row + startX + x] == lava)
                        {
                            containsLava = true;
                            break;
                        }
                    }
                }

                if (containsLava)
                {
                    temperatureCells[(temperatureY * temperatureSizeCells) + temperatureX] = (Half)255f;
                }
            }
        }
    }

    /// <summary>
    /// 在有限 Editor 预览画布中显示以世界原点为中心的同源地形切片。
    /// </summary>
    internal static void PopulateAuthoringWorld(in AuthoringWorldPreviewContext context)
    {
        TerrainMaterialPalette palette = ResolvePalette(context.Materials);
        _ = context.Edit.ClearRect(0, 0, context.WidthCells - 1, context.HeightCells - 1);
        Span<int> surfaces = stackalloc int[context.WidthCells];
        Span<int> soilDepths = stackalloc int[context.WidthCells];
        Span<double> moisture = stackalloc double[context.WidthCells];
        long previewOriginX = -(context.WidthCells / 2L);
        for (int x = 0; x < context.WidthCells; x++)
        {
            long worldX = previewOriginX + x;
            surfaces[x] = SurfaceYAt(worldX);
            moisture[x] = MoistureAt(worldX);
            soilDepths[x] = 5 + (int)Math.Round((ValueNoise1D(worldX * 0.011, Seed ^ 0x5A17UL) + 1.0) * 2.5);
        }

        for (int y = 0; y < context.HeightCells; y++)
        {
            int runStart = 0;
            ushort runMaterial = SelectMaterial(
                previewOriginX,
                y,
                surfaces[0],
                soilDepths[0],
                moisture[0],
                in palette);
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
                        in palette);
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

    private static ushort ResolveRequired(IMaterialQuery materials, string name)
    {
        MaterialId id = materials.Resolve(name);
        return id.IsValid
            ? id.Value
            : throw new InvalidOperationException($"无限沙盒地形需要材质 {name}。");
    }

    private static double MoistureAt(long worldX)
    {
        return Fractal1D((worldX + 4_300.0) * 0.00091, Seed ^ 0xA0157UL, 4);
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
