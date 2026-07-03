namespace PixelEngine.Rendering;

/// <summary>
/// CPU 侧粗粒度 fog-of-war reveal 字节图。每个 tile 存一个 alpha，0 表示未揭示，255 表示完全揭示。
/// </summary>
public sealed class FogOfWarBuffer
{
    /// <summary>
    /// 默认 fog tile 尺寸，单位为视口局部 cell。
    /// </summary>
    public const int DefaultTileSize = 32;

    private readonly byte[] _reveal;

    /// <summary>
    /// 创建覆盖当前视口 cell 范围的 fog-of-war buffer。
    /// </summary>
    /// <param name="viewportCellWidth">视口宽度，单位为 cell。</param>
    /// <param name="viewportCellHeight">视口高度，单位为 cell。</param>
    /// <param name="tileSize">单个 fog tile 的边长，单位为 cell。</param>
    public FogOfWarBuffer(int viewportCellWidth, int viewportCellHeight, int tileSize = DefaultTileSize)
    {
        ValidateSize(viewportCellWidth, viewportCellHeight, tileSize);
        ViewportCellWidth = viewportCellWidth;
        ViewportCellHeight = viewportCellHeight;
        TileSize = tileSize;
        TileWidth = CeilDiv(viewportCellWidth, tileSize);
        TileHeight = CeilDiv(viewportCellHeight, tileSize);
        _reveal = GC.AllocateArray<byte>(checked(TileWidth * TileHeight), pinned: true);
    }

    /// <summary>
    /// 覆盖的视口宽度，单位为 cell。查询坐标均为视口局部 cell 坐标。
    /// </summary>
    public int ViewportCellWidth { get; }

    /// <summary>
    /// 覆盖的视口高度，单位为 cell。查询坐标均为视口局部 cell 坐标。
    /// </summary>
    public int ViewportCellHeight { get; }

    /// <summary>
    /// 单个 fog tile 的边长，单位为 cell。
    /// </summary>
    public int TileSize { get; }

    /// <summary>
    /// fog tile 网格宽度，单位为 tile。
    /// </summary>
    public int TileWidth { get; }

    /// <summary>
    /// fog tile 网格高度，单位为 tile。
    /// </summary>
    public int TileHeight { get; }

    /// <summary>
    /// reveal alpha 元素数量。
    /// </summary>
    public int Length => _reveal.Length;

    /// <summary>
    /// 可写 reveal alpha 数据视图。索引顺序为 row-major：tileY × TileWidth + tileX。
    /// </summary>
    public Span<byte> Reveal => _reveal;

    /// <summary>
    /// 清空 reveal map，所有 tile 变为未揭示。
    /// </summary>
    public void Clear()
    {
        _reveal.AsSpan().Clear();
    }

    /// <summary>
    /// 揭示当前视口内全部 fog tile，用于可玩 Demo 这类不需要黑边遮罩的场景。
    /// </summary>
    /// <param name="revealAlpha">写入的 reveal alpha；已有 tile 会保留较大 alpha。</param>
    public void RevealAll(byte revealAlpha = byte.MaxValue)
    {
        Span<byte> reveal = _reveal;
        for (int i = 0; i < reveal.Length; i++)
        {
            if (revealAlpha > reveal[i])
            {
                reveal[i] = revealAlpha;
            }
        }
    }

    /// <summary>
    /// 在视口局部 cell 坐标中揭示一个圆形区域，边界会裁剪到当前视口。
    /// </summary>
    /// <param name="centerCellX">圆心 X，单位为视口局部 cell。</param>
    /// <param name="centerCellY">圆心 Y，单位为视口局部 cell。</param>
    /// <param name="radiusCells">半径，单位为 cell。</param>
    /// <param name="revealAlpha">写入的 reveal alpha；已有 tile 会保留较大 alpha。</param>
    public void RevealCircle(int centerCellX, int centerCellY, int radiusCells, byte revealAlpha = byte.MaxValue)
    {
        if (radiusCells < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusCells), "reveal 半径不能为负数。");
        }

        int minCellX = centerCellX - radiusCells;
        int minCellY = centerCellY - radiusCells;
        int maxCellX = centerCellX + radiusCells;
        int maxCellY = centerCellY + radiusCells;
        if (maxCellX < 0 || maxCellY < 0 || minCellX >= ViewportCellWidth || minCellY >= ViewportCellHeight)
        {
            return;
        }

        int minTileX = Math.Clamp(FloorDiv(minCellX, TileSize), 0, TileWidth - 1);
        int minTileY = Math.Clamp(FloorDiv(minCellY, TileSize), 0, TileHeight - 1);
        int maxTileX = Math.Clamp(FloorDiv(maxCellX, TileSize), 0, TileWidth - 1);
        int maxTileY = Math.Clamp(FloorDiv(maxCellY, TileSize), 0, TileHeight - 1);
        long radiusSquared = (long)radiusCells * radiusCells;

        for (int tileY = minTileY; tileY <= maxTileY; tileY++)
        {
            for (int tileX = minTileX; tileX <= maxTileX; tileX++)
            {
                if (!CircleIntersectsTile(centerCellX, centerCellY, radiusSquared, tileX, tileY))
                {
                    continue;
                }

                int index = (tileY * TileWidth) + tileX;
                if (revealAlpha > _reveal[index])
                {
                    _reveal[index] = revealAlpha;
                }
            }
        }
    }

    /// <summary>
    /// 查询视口局部 cell 是否已被揭示。
    /// </summary>
    /// <param name="cellX">视口局部 cell X 坐标。</param>
    /// <param name="cellY">视口局部 cell Y 坐标。</param>
    /// <returns>坐标越界或 alpha 为 0 时返回 false。</returns>
    public bool IsRevealed(int cellX, int cellY)
    {
        return RevealAlpha(cellX, cellY) != 0;
    }

    /// <summary>
    /// 查询视口局部 cell 的 reveal alpha。
    /// </summary>
    /// <param name="cellX">视口局部 cell X 坐标。</param>
    /// <param name="cellY">视口局部 cell Y 坐标。</param>
    /// <returns>坐标越界时返回 0，否则返回所属 tile 的 reveal alpha。</returns>
    public byte RevealAlpha(int cellX, int cellY)
    {
        if ((uint)cellX >= (uint)ViewportCellWidth || (uint)cellY >= (uint)ViewportCellHeight)
        {
            return 0;
        }

        int tileX = cellX / TileSize;
        int tileY = cellY / TileSize;
        return _reveal[(tileY * TileWidth) + tileX];
    }

    private bool CircleIntersectsTile(int centerCellX, int centerCellY, long radiusSquared, int tileX, int tileY)
    {
        int minX = tileX * TileSize;
        int minY = tileY * TileSize;
        int maxX = Math.Min(ViewportCellWidth - 1, minX + TileSize - 1);
        int maxY = Math.Min(ViewportCellHeight - 1, minY + TileSize - 1);
        int nearestX = Math.Clamp(centerCellX, minX, maxX);
        int nearestY = Math.Clamp(centerCellY, minY, maxY);
        long dx = centerCellX - nearestX;
        long dy = centerCellY - nearestY;
        return ((dx * dx) + (dy * dy)) <= radiusSquared;
    }

    private static int CeilDiv(int value, int divisor)
    {
        return ((value - 1) / divisor) + 1;
    }

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;
        if (remainder != 0 && value < 0)
        {
            quotient--;
        }

        return quotient;
    }

    private static void ValidateSize(int viewportCellWidth, int viewportCellHeight, int tileSize)
    {
        if (viewportCellWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportCellWidth), "fog-of-war 视口宽度必须为正数。");
        }

        if (viewportCellHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportCellHeight), "fog-of-war 视口高度必须为正数。");
        }

        if (tileSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tileSize), "fog-of-war tileSize 必须为正数。");
        }
    }
}
