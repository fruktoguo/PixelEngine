using PixelEngine.Hosting;
using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 面向直接试玩的确定性洞穴世界生成器，生成宽幅地形、洞穴、矿脉与少量危险池。
/// </summary>
public sealed class PlayableCavernWorldGenerator : IProceduralWorldGenerator
{
    /// <summary>
    /// 程序化场景键，同时也是入口 Behaviour 的完整类型名。
    /// </summary>
    public const string Key = "PixelEngine.Demo.PlayableWorldDirector";

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
        MaterialId empty = context.Materials.Resolve("empty");
        MaterialId dirt = context.Materials.Resolve("dirt");
        MaterialId stone = context.Materials.Resolve("stone");
        MaterialId sand = context.Materials.Resolve("sand");
        MaterialId water = context.Materials.Resolve("water");
        MaterialId lava = context.Materials.Resolve("lava");
        MaterialId metal = context.Materials.Resolve("metal");
        MaterialId wood = context.Materials.Resolve("wood");
        if (!empty.IsValid || !dirt.IsValid || !stone.IsValid || !sand.IsValid ||
            !water.IsValid || !lava.IsValid || !metal.IsValid || !wood.IsValid)
        {
            throw new InvalidOperationException("Playable Demo 需要 empty/dirt/stone/sand/water/lava/metal/wood 材质。");
        }

        _ = context.Edit.PaintRect(0, 0, context.WidthCells - 1, context.HeightCells - 1, empty.Value);
        int floorBase = context.HeightCells - 96;
        for (int x = 0; x < context.WidthCells; x++)
        {
            int surface = SurfaceY(x, floorBase);
            for (int y = surface; y < context.HeightCells; y++)
            {
                ushort material = y > surface + 48 ? stone.Value : dirt.Value;
                context.Edit.PaintCell(x, y, material);
            }

            for (int y = surface - 1; y >= Math.Max(0, surface - 3); y--)
            {
                context.Edit.PaintCell(x, y, sand.Value);
            }
        }

        CarveSpawnPocket(context, empty.Value, wood.Value);
        CarveCaves(context, empty.Value);
        PaintDeposits(context, metal.Value, stone.Value);
        PaintHazards(context, water.Value, lava.Value, stone.Value);
        PaintBounds(context, stone.Value);
    }

    private static int SurfaceY(int x, int floorBase)
    {
        float wave =
            (MathF.Sin(x * 0.013f) * 22f) +
            (MathF.Sin((x * 0.031f) + 1.7f) * 11f) +
            (MathF.Sin((x * 0.004f) + 4.1f) * 28f);
        return Math.Clamp((int)MathF.Round(floorBase + wave), 250, 448);
    }

    private static void CarveSpawnPocket(in ProceduralWorldBuildContext context, ushort empty, ushort wood)
    {
        _ = context.Edit.PaintRect(24, 260, 180, 390, empty);
        _ = context.Edit.PaintRect(28, 382, 168, 390, wood);
        _ = context.Edit.PaintRect(190, 320, 270, 380, empty);
    }

    private static void CarveCaves(in ProceduralWorldBuildContext context, ushort empty)
    {
        for (int x = 96; x < context.WidthCells - 64; x += 112)
        {
            int centerY = SurfaceY(x, context.HeightCells - 96) - 54 + (int)(Hash01(x, 17) * 72f);
            int radiusX = 42 + (int)(Hash01(x, 29) * 34f);
            int radiusY = 18 + (int)(Hash01(x, 43) * 24f);
            CarveEllipse(context, x, centerY, radiusX, radiusY, empty);
        }

        for (int x = 180; x < context.WidthCells - 180; x += 220)
        {
            int y = SurfaceY(x, context.HeightCells - 96) - 30;
            _ = context.Edit.PaintRect(x - 86, y, x + 86, y + 30, empty);
        }
    }

    private static void PaintDeposits(in ProceduralWorldBuildContext context, ushort metal, ushort stone)
    {
        for (int x = 360; x < context.WidthCells - 160; x += 430)
        {
            int y = SurfaceY(x, context.HeightCells - 96) + 26;
            CarveEllipse(context, x, y, 30, 14, metal);
            _ = context.Edit.PaintRect(x - 44, y + 18, x + 44, y + 23, stone);
        }
    }

    private static void PaintHazards(in ProceduralWorldBuildContext context, ushort water, ushort lava, ushort stone)
    {
        for (int x = 560; x < context.WidthCells - 240; x += 760)
        {
            int y = SurfaceY(x, context.HeightCells - 96) - 4;
            _ = context.Edit.PaintRect(x - 60, y, x + 60, y + 20, water);
        }

        for (int x = 1100; x < context.WidthCells - 260; x += 920)
        {
            int y = SurfaceY(x, context.HeightCells - 96) + 12;
            _ = context.Edit.PaintRect(x - 38, y, x + 38, y + 16, lava);
            _ = context.Edit.PaintRect(x - 48, y + 17, x + 48, y + 22, stone);
        }
    }

    private static void PaintBounds(in ProceduralWorldBuildContext context, ushort stone)
    {
        _ = context.Edit.PaintRect(0, 0, context.WidthCells - 1, 7, stone);
        _ = context.Edit.PaintRect(0, context.HeightCells - 8, context.WidthCells - 1, context.HeightCells - 1, stone);
        _ = context.Edit.PaintRect(0, 0, 7, context.HeightCells - 1, stone);
        _ = context.Edit.PaintRect(context.WidthCells - 8, 0, context.WidthCells - 1, context.HeightCells - 1, stone);
    }

    private static void CarveEllipse(in ProceduralWorldBuildContext context, int centerX, int centerY, int radiusX, int radiusY, ushort material)
    {
        int minX = Math.Max(8, centerX - radiusX);
        int maxX = Math.Min(context.WidthCells - 9, centerX + radiusX);
        int minY = Math.Max(8, centerY - radiusY);
        int maxY = Math.Min(context.HeightCells - 9, centerY + radiusY);
        float invX = 1f / Math.Max(1, radiusX * radiusX);
        float invY = 1f / Math.Max(1, radiusY * radiusY);
        for (int y = minY; y <= maxY; y++)
        {
            int dy = y - centerY;
            for (int x = minX; x <= maxX; x++)
            {
                int dx = x - centerX;
                if ((dx * dx * invX) + (dy * dy * invY) <= 1f)
                {
                    context.Edit.PaintCell(x, y, material);
                }
            }
        }
    }

    private static float Hash01(int x, int salt)
    {
        uint value = unchecked((uint)(x * 747_796_405) + (uint)(salt * 2_891_336_453));
        value = (value ^ (value >> 16)) * 2_246_822_519u;
        value ^= value >> 13;
        return (value & 0x00FF_FFFF) / 16_777_215f;
    }
}
