using PixelEngine.Hosting;
using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 将 AI 生成的 PNG 材质地图缩放绘制到程序化世界，并补齐可玩 Demo 的安全边界与出生平台。
/// </summary>
internal static class ImageMaterialMapWorldPainter
{
    private static readonly PaletteEntry[] Palette =
    [
        new("empty", 0x00, 0x00, 0x00),
        new("stone", 0xB4, 0xB4, 0xB4),
        new("dirt", 0x9B, 0x62, 0x38),
        new("sand", 0xF2, 0xD6, 0x67),
        new("water", 0x1D, 0x70, 0xDA),
        new("lava", 0xF4, 0x43, 0x16),
        new("metal", 0x7A, 0x35, 0xB6),
        new("wood", 0xC4, 0x94, 0x54),
        new("acid", 0x69, 0xC9, 0x3A),
    ];

    /// <summary>
    /// 尝试从 PNG 路径加载材质地图并绘制到 <paramref name="context"/> 网格。
    /// </summary>
    /// <param name="path">材质地图 PNG 路径。</param>
    /// <param name="context">程序化世界构建上下文。</param>
    /// <param name="failureReason">失败时的人类可读原因。</param>
    /// <returns>绘制成功时返回 true。</returns>
    public static bool TryPaint(
        string path,
        in ProceduralWorldBuildContext context,
        out string failureReason)
    {
        failureReason = string.Empty;
        // 校验文件存在并加载 PNG 像素
        if (!File.Exists(path))
        {
            failureReason = $"材质地图不存在：{path}";
            return false;
        }

        MaterialMapPalette palette = MaterialMapPalette.Resolve(context.Materials);
        MaterialMapImage image;
        try
        {
            image = MaterialMapPngLoader.Load(path);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            failureReason = $"材质地图加载失败：{exception.Message}";
            return false;
        }

        // 最近邻缩放：目标网格坐标映射到源图像像素并量化材质
        for (int y = 0; y < context.HeightCells; y++)
        {
            int sourceY = ScaleCoordinate(y, context.HeightCells, image.Height);
            for (int x = 0; x < context.WidthCells; x++)
            {
                int sourceX = ScaleCoordinate(x, context.WidthCells, image.Width);
                ushort material = palette.Quantize(image.PixelAt(sourceX, sourceY));
                context.Edit.PaintCell(x, y, material);
            }
        }

        // 叠加出生平台与世界边界，避免玩家掉入空洞或越界
        PaintPlayableSafety(context, palette);
        return true;
    }

    private static int ScaleCoordinate(int value, int destinationSize, int sourceSize)
    {
        return destinationSize <= 1
            ? 0
            : Math.Clamp((int)((long)value * sourceSize / destinationSize), 0, sourceSize - 1);
    }

    private static void PaintPlayableSafety(in ProceduralWorldBuildContext context, in MaterialMapPalette palette)
    {
        int spawnFloorY = Math.Clamp(188, 24, context.HeightCells - 16);
        _ = context.Edit.PaintRect(28, 138, 190, spawnFloorY - 1, palette.Empty);
        _ = context.Edit.PaintRect(32, spawnFloorY, 184, spawnFloorY + 5, palette.Wood);
        _ = context.Edit.PaintRect(24, spawnFloorY + 6, 192, spawnFloorY + 12, palette.Stone);
        PaintBounds(context, palette.Stone);
    }

    private static void PaintBounds(in ProceduralWorldBuildContext context, ushort stone)
    {
        _ = context.Edit.PaintRect(0, 0, context.WidthCells - 1, 7, stone);
        _ = context.Edit.PaintRect(0, context.HeightCells - 8, context.WidthCells - 1, context.HeightCells - 1, stone);
        _ = context.Edit.PaintRect(0, 0, 7, context.HeightCells - 1, stone);
        _ = context.Edit.PaintRect(context.WidthCells - 8, 0, context.WidthCells - 1, context.HeightCells - 1, stone);
    }

    /// <summary>固定 RGB 调色板条目，与 AI 材质地图约定颜色一一对应。</summary>
    private readonly record struct PaletteEntry(string Name, byte R, byte G, byte B);

    /// <summary>将调色板名称解析为运行时 MaterialId，并提供 RGB 最近邻量化。</summary>
    private readonly struct MaterialMapPalette
    {
        private readonly ushort[] _materials;

        private MaterialMapPalette(ushort[] materials)
        {
            _materials = materials;
        }

        public ushort Empty => _materials[0];

        public ushort Stone => _materials[1];

        public ushort Wood => _materials[7];

        public static MaterialMapPalette Resolve(IMaterialQuery materials)
        {
            ushort[] resolved = new ushort[Palette.Length];
            for (int i = 0; i < Palette.Length; i++)
            {
                MaterialId id = materials.Resolve(Palette[i].Name);
                if (!id.IsValid)
                {
                    throw new InvalidOperationException($"AI 材质地图需要 {Palette[i].Name} 材质。");
                }

                resolved[i] = id.Value;
            }

            return new MaterialMapPalette(resolved);
        }

        public ushort Quantize(uint rgba)
        {
            byte alpha = (byte)(rgba >> 24);
            if (alpha < 16)
            {
                return Empty;
            }

            int r = (byte)rgba;
            int g = (byte)(rgba >> 8);
            int b = (byte)(rgba >> 16);
            int bestIndex = 0;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < Palette.Length; i++)
            {
                int dr = r - Palette[i].R;
                int dg = g - Palette[i].G;
                int db = b - Palette[i].B;
                int distance = (dr * dr) + (dg * dg) + (db * db);
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                bestIndex = i;
            }

            return _materials[bestIndex];
        }
    }
}
