using PixelEngine.Hosting;
using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 面向直接试玩的确定性横向闯关世界生成器，生成宽幅路线、熔岩坑、跳台与可拆障碍。
/// </summary>
public sealed class PlayableCavernWorldGenerator(string? materialMapPath = null) : IProceduralWorldGenerator
{
    /// <summary>
    /// 程序化场景键，同时也是入口 Behaviour 的完整类型名。
    /// </summary>
    public const string Key = "PixelEngine.Demo.PlayableWorldDirector";

    /// <summary>
    /// 默认 AI 材质地图相对路径。
    /// </summary>
    public const string DefaultMaterialMapRelativePath = "maps/ai-cavern-material-map.png";

    private const int Width = 1536;
    private const int Height = 384;
    private const ulong Seed = 0x5049_5845_4C44_454D;

    /// <inheritdoc />
    public ProceduralWorldDescriptor Describe(in ProceduralWorldBuildRequest request)
    {
        _ = request;
        return new ProceduralWorldDescriptor(Width, Height, Seed);
    }

    /// <inheritdoc />
    public void Populate(in ProceduralWorldBuildContext context)
    {
        string? mapPath = ResolveMaterialMapPath();
        if (mapPath is not null &&
            ImageMaterialMapWorldPainter.TryPaint(mapPath, context, out _))
        {
            return;
        }

        MaterialId empty = context.Materials.Resolve("empty");
        MaterialId dirt = context.Materials.Resolve("dirt");
        MaterialId stone = context.Materials.Resolve("stone");
        MaterialId sand = context.Materials.Resolve("sand");
        MaterialId lava = context.Materials.Resolve("lava");
        MaterialId metal = context.Materials.Resolve("metal");
        MaterialId wood = context.Materials.Resolve("wood");
        MaterialId boundaryStone = context.Materials.Resolve("boundary_stone");
        if (!empty.IsValid || !dirt.IsValid || !stone.IsValid || !sand.IsValid ||
            !lava.IsValid || !metal.IsValid || !wood.IsValid)
        {
            throw new InvalidOperationException("Playable Demo 需要 empty/dirt/stone/sand/lava/metal/wood 材质。");
        }

        _ = context.Edit.PaintRect(0, 0, context.WidthCells - 1, context.HeightCells - 1, empty.Value);
        PaintSideScrollingRoute(
            context,
            dirt.Value,
            stone.Value,
            sand.Value,
            lava.Value,
            metal.Value,
            wood.Value,
            boundaryStone.IsValid ? boundaryStone.Value : stone.Value);
    }

    private string? ResolveMaterialMapPath()
    {
        return string.IsNullOrWhiteSpace(materialMapPath) ? null : Path.GetFullPath(materialMapPath);
    }

    private static void PaintSideScrollingRoute(
        in ProceduralWorldBuildContext context,
        ushort dirt,
        ushort stone,
        ushort sand,
        ushort lava,
        ushort metal,
        ushort wood,
        ushort boundaryStone)
    {
        int floorY = context.HeightCells - 96;
        FillRect(context, 0, floorY, context.WidthCells, context.HeightCells - floorY, dirt);
        FillRect(context, 0, floorY + 28, context.WidthCells, 22, stone);
        FillRect(context, 20, floorY - 16, 130, 16, stone);
        FillRect(context, 30, floorY - 24, 84, 8, sand);

        for (int x = 190; x < context.WidthCells - 180; x += 260)
        {
            int width = 76 + (int)(Hash01(x, 7) * 28f);
            FillRect(context, x, floorY - 2, width, 30, lava);
            FillRect(context, x - 6, floorY + 28, width + 12, 8, stone);
        }

        for (int x = 180; x < context.WidthCells - 220; x += 190)
        {
            int y = floorY - 42 - (int)(Hash01(x, 17) * 60f);
            ushort material = x / 190 % 2 == 0 ? wood : metal;
            FillRect(context, x, y, 92, 10, material);
        }

        for (int x = 260; x < context.WidthCells - 180; x += 230)
        {
            int height = 24 + (int)(Hash01(x, 31) * 26f);
            ushort material = x / 230 % 3 == 0 ? metal : wood;
            FillRect(context, x, floorY - height, 24, height, material);
        }

        for (int x = 420; x < context.WidthCells - 240; x += 360)
        {
            FillRect(context, x, floorY - 116, 72, 12, metal);
            FillRect(context, x + 92, floorY - 82, 68, 10, wood);
            FillRect(context, x + 38, floorY - 54, 32, 18, stone);
        }

        FillSlope(context, 42, floorY - 1, 96, 24, sand);
        FillSlope(context, context.WidthCells - 250, floorY - 1, 140, 28, sand);
        PaintBounds(context, boundaryStone);
    }

    private static void FillRect(in ProceduralWorldBuildContext context, int x, int y, int width, int height, ushort material)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        int minX = Math.Clamp(x, 0, context.WidthCells - 1);
        int minY = Math.Clamp(y, 0, context.HeightCells - 1);
        int maxX = Math.Clamp(x + width - 1, 0, context.WidthCells - 1);
        int maxY = Math.Clamp(y + height - 1, 0, context.HeightCells - 1);
        if (maxX < minX || maxY < minY)
        {
            return;
        }

        _ = context.Edit.PaintRect(minX, minY, maxX, maxY, material);
    }

    private static void FillSlope(in ProceduralWorldBuildContext context, int x, int baseY, int width, int height, ushort material)
    {
        for (int cx = 0; cx < width; cx++)
        {
            int columnHeight = Math.Max(1, (int)MathF.Round((1f - (cx / (float)Math.Max(1, width - 1))) * height));
            FillRect(context, x + cx, baseY - columnHeight + 1, 1, columnHeight, material);
        }
    }

    private static void PaintBounds(in ProceduralWorldBuildContext context, ushort stone)
    {
        FillRect(context, 0, 0, context.WidthCells, 8, stone);
        FillRect(context, 0, context.HeightCells - 8, context.WidthCells, 8, stone);
        FillRect(context, 0, 0, 8, context.HeightCells, stone);
        FillRect(context, context.WidthCells - 8, 0, 8, context.HeightCells, stone);
    }

    private static float Hash01(int x, int salt)
    {
        uint value = unchecked((uint)(x * 747_796_405) + (uint)(salt * 2_891_336_453));
        value = (value ^ (value >> 16)) * 2_246_822_519u;
        value ^= value >> 13;
        return (value & 0x00FF_FFFF) / 16_777_215f;
    }
}
