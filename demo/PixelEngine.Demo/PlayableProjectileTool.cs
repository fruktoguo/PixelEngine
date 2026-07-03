using PixelEngine.Scripting;
using PixelEngine.Simulation;

namespace PixelEngine.Demo;

/// <summary>
/// 可玩 Demo 的轻量射击工具，左键朝鼠标方向发射破坏弹。
/// </summary>
public sealed class PlayableProjectileTool : Behaviour
{
    private PlayerController? _player;
    private float _cooldownRemaining;
    private int _pendingCollapseFrames;
    private int _pendingCollapseX;
    private int _pendingCollapseY;
    private int _pendingCollapsePasses;
    private int _pendingCollapseScans;

    /// <summary>
    /// 射击最大距离，单位 cell。
    /// </summary>
    public float Range { get; set; } = 180f;

    /// <summary>
    /// 命中爆破半径，单位 cell。
    /// </summary>
    public int ImpactRadius { get; set; } = 9;

    /// <summary>
    /// 命中冲量强度。
    /// </summary>
    public float ImpactForce { get; set; } = 24f;

    /// <summary>
    /// 射击冷却时间，单位秒。
    /// </summary>
    public float CooldownSeconds { get; set; } = 0.16f;

    /// <summary>
    /// 已开火次数。
    /// </summary>
    public int ShotsFired { get; private set; }

    /// <summary>
    /// 最近一次射击起点 X 坐标。
    /// </summary>
    public float LastShotStartX { get; private set; }

    /// <summary>
    /// 最近一次射击起点 Y 坐标。
    /// </summary>
    public float LastShotStartY { get; private set; }

    /// <summary>
    /// 最近一次命中 X 坐标。
    /// </summary>
    public float LastHitX { get; private set; }

    /// <summary>
    /// 最近一次命中 Y 坐标。
    /// </summary>
    public float LastHitY { get; private set; }

    /// <summary>
    /// 弹道 overlay 剩余显示时间，单位秒。
    /// </summary>
    public float TracerRemainingSeconds { get; private set; }

    /// <summary>
    /// 爆破后局部扫描半径，用于把脱离主地形的小型固体岛转换为刚体。
    /// </summary>
    public int CollapseScanRadius { get; set; } = 260;

    /// <summary>
    /// 可自动转换的最大连通块包围盒尺寸，避免误把整片程序化地形转成刚体。
    /// </summary>
    public int MaxCollapseRegionSize { get; set; } = 224;

    /// <summary>
    /// 可自动转换的最小固体像素数。
    /// </summary>
    public int MinCollapsePixels { get; set; } = 8;

    /// <summary>
    /// 单次爆破最多转换的悬空固体岛数量，避免一枪把整片程序化山体误拆成过多刚体。
    /// </summary>
    public int MaxCollapsedIslandsPerShot { get; set; } = 10;

    /// <summary>
    /// 常规连通块扫描失败时，围绕弹坑把局部悬空边缘提升为刚体的最大半径。
    /// </summary>
    public int FallbackOverhangRadius { get; set; } = 88;

    /// <summary>
    /// 已由破坏弹转换成刚体的悬空固体岛数量。
    /// </summary>
    public int CollapsedFloatingIslands { get; private set; }

    /// <summary>
    /// 最近一次悬空固体岛转换的包围盒。
    /// </summary>
    public (int X, int Y, int Width, int Height) LastCollapsedRegion { get; private set; }

    /// <summary>
    /// 最近一次悬空固体岛扫描跳过转换的原因，供 Demo 验收与问题排查。
    /// </summary>
    public string LastCollapseSkipReason { get; private set; } = "none";

    /// <summary>
    /// 面向 HUD 的悬空块转换状态；成功后优先显示最近一次转换区域。
    /// </summary>
    public string CollapseStatus => CollapsedFloatingIslands > 0
        ? $"converted {LastCollapsedRegion.X},{LastCollapsedRegion.Y},{LastCollapsedRegion.Width}x{LastCollapsedRegion.Height}"
        : LastCollapseSkipReason;

    /// <summary>
    /// 最近一次悬空扫描读到的普通非空 cell 数量。
    /// </summary>
    public int LastCollapseSolidCandidates { get; private set; }

    /// <inheritdoc />
    protected override void OnStart()
    {
        _player = Entity.TryGetComponent<PlayerController>(out PlayerController player) ? player : null;
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        float safeDt = MathF.Max(0f, dt);
        _cooldownRemaining = MathF.Max(0f, _cooldownRemaining - safeDt);
        TracerRemainingSeconds = MathF.Max(0f, TracerRemainingSeconds - safeDt);
        ProcessPendingCollapseScan();
        if (_cooldownRemaining > 0f || !Context.Input.WasMousePressed(MouseButton.Left))
        {
            return;
        }

        if (_player is null)
        {
            if (!Entity.TryGetComponent<PlayerController>(out PlayerController player))
            {
                return;
            }

            _player = player;
        }

        (float mouseX, float mouseY) = Context.Input.MousePixel;
        Point2F target = Context.Camera.ScreenToWorld(mouseX, mouseY);
        float startX = _player.CenterX;
        float startY = _player.CenterY - 2f;
        float dx = target.X - startX;
        float dy = target.Y - startY;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length <= 0.001f)
        {
            return;
        }

        dx /= length;
        dy /= length;
        float hitX = startX + (dx * Range);
        float hitY = startY + (dy * Range);
        if (Context.Solids.Raycast(startX, startY, dx, dy, Range, out RaycastHit hit))
        {
            hitX = hit.X;
            hitY = hit.Y;
        }

        Context.World.Explode(hitX, hitY, Math.Max(1, ImpactRadius), MathF.Max(1f, ImpactForce));
        Context.Lighting.RevealAround(hitX, hitY, ImpactRadius * 2.5f);
        Context.Lighting.AddPointLight(hitX, hitY, ImpactRadius * 3f, 0xFF_60_D8_FF, 0.35f);
        Context.Audio.PlayAt("explosion.wav", hitX, hitY, 0.85f);
        LastShotStartX = startX;
        LastShotStartY = startY;
        LastHitX = hitX;
        LastHitY = hitY;
        TracerRemainingSeconds = 0.10f;
        ShotsFired++;
        QueueCollapseScan(hitX, hitY);
        _cooldownRemaining = MathF.Max(0f, CooldownSeconds);
    }

    private void QueueCollapseScan(float hitX, float hitY)
    {
        _pendingCollapseX = (int)MathF.Round(hitX);
        _pendingCollapseY = (int)MathF.Round(hitY);
        _pendingCollapseFrames = 1;
        _pendingCollapsePasses = Math.Clamp(MaxCollapsedIslandsPerShot, 1, 12);
        _pendingCollapseScans = 8;
    }

    private void ProcessPendingCollapseScan()
    {
        if (_pendingCollapseFrames <= 0)
        {
            return;
        }

        _pendingCollapseFrames--;
        if (_pendingCollapseFrames > 0)
        {
            return;
        }

        int converted = ConvertFloatingSolidIslandsNear(_pendingCollapseX, _pendingCollapseY, maxConversions: 1);
        if (converted > 0)
        {
            _pendingCollapsePasses -= converted;
            if (_pendingCollapsePasses <= 0)
            {
                _pendingCollapseScans = 0;
                return;
            }

            _pendingCollapseFrames = 1;
            return;
        }

        _pendingCollapseScans--;
        if (_pendingCollapsePasses > 0 && _pendingCollapseScans > 0)
        {
            _pendingCollapseFrames = 1;
        }
    }

    private int ConvertFloatingSolidIslandsNear(int centerX, int centerY, int maxConversions)
    {
        int radius = Math.Clamp(CollapseScanRadius, 4, 320);
        int size = (radius * 2) + 1;
        int originX = centerX - radius;
        int originY = centerY - radius;
        bool[] visited = new bool[size * size];
        bool[] component = new bool[size * size];
        int[] queue = new int[size * size];
        int[] cells = new int[size * size];
        int converted = 0;
        LastCollapseSkipReason = "scan_empty";
        LastCollapseSolidCandidates = 0;

        for (int localY = 0; localY < size; localY++)
        {
            for (int localX = 0; localX < size; localX++)
            {
                int startIndex = Pack(localX, localY, size);
                if (visited[startIndex] || !IsSolid(originX + localX, originY + localY))
                {
                    visited[startIndex] = true;
                    continue;
                }

                Array.Clear(component);
                int cellCount = FloodFillSolidIsland(localX, localY, originX, originY, size, visited, component, queue, cells, out int minX, out int minY, out int maxX, out int maxY, out ComponentBorderContact borderContact);
                if (!CanConvertIsland(cellCount, minX, minY, maxX, maxY, borderContact, out string rejection))
                {
                    LastCollapseSkipReason = rejection;
                    continue;
                }

                if (HasExternalSupport(originX, originY, size, cells, cellCount, component))
                {
                    LastCollapseSkipReason = "external_support";
                    continue;
                }

                int worldX = originX + minX;
                int worldY = originY + minY;
                int width = maxX - minX + 1;
                int height = maxY - minY + 1;
                if (!TryCreateBodyFromSolidBounds(worldX, worldY, width, height, "degenerate"))
                {
                    continue;
                }

                LastCollapseSkipReason = "converted";
                CollapsedFloatingIslands++;
                converted++;
                if (converted >= Math.Max(1, maxConversions))
                {
                    return converted;
                }
            }
        }

        if (converted < Math.Max(1, maxConversions))
        {
            converted += ConvertUnsupportedOverhangNear(centerX, centerY, Math.Max(1, maxConversions) - converted);
        }

        if (converted == 0)
        {
            converted += ConvertImpactFractureChunk(centerX, centerY);
        }

        return converted;
    }

    private int FloodFillSolidIsland(
        int startX,
        int startY,
        int originX,
        int originY,
        int size,
        bool[] visited,
        bool[] component,
        int[] queue,
        int[] cells,
        out int minX,
        out int minY,
        out int maxX,
        out int maxY,
        out ComponentBorderContact borderContact)
    {
        int head = 0;
        int tail = 0;
        int count = 0;
        minX = maxX = startX;
        minY = maxY = startY;
        borderContact = ComponentBorderContact.None;
        int start = Pack(startX, startY, size);
        queue[tail++] = start;
        visited[start] = true;
        component[start] = true;

        while (head < tail)
        {
            int packed = queue[head++];
            cells[count++] = packed;
            int x = packed % size;
            int y = packed / size;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
            borderContact |= BorderContact(x, y, size);

            TryEnqueueSolidNeighbor(x - 1, y, originX, originY, size, visited, component, queue, ref tail);
            TryEnqueueSolidNeighbor(x + 1, y, originX, originY, size, visited, component, queue, ref tail);
            TryEnqueueSolidNeighbor(x, y - 1, originX, originY, size, visited, component, queue, ref tail);
            TryEnqueueSolidNeighbor(x, y + 1, originX, originY, size, visited, component, queue, ref tail);
        }

        return count;
    }

    private void TryEnqueueSolidNeighbor(int x, int y, int originX, int originY, int size, bool[] visited, bool[] component, int[] queue, ref int tail)
    {
        if ((uint)x >= (uint)size || (uint)y >= (uint)size)
        {
            return;
        }

        int packed = Pack(x, y, size);
        if (visited[packed])
        {
            return;
        }

        visited[packed] = true;
        if (!IsSolid(originX + x, originY + y))
        {
            return;
        }

        component[packed] = true;
        queue[tail++] = packed;
    }

    private bool CanConvertIsland(
        int cellCount,
        int minX,
        int minY,
        int maxX,
        int maxY,
        ComponentBorderContact borderContact,
        out string rejection)
    {
        if (cellCount < Math.Max(1, MinCollapsePixels))
        {
            rejection = "too_few_pixels";
            return false;
        }

        if ((borderContact & ComponentBorderContact.Bottom) != 0)
        {
            rejection = $"scan_border_{borderContact}";
            return false;
        }

        int maxSize = Math.Clamp(MaxCollapseRegionSize, 4, 320);
        if (maxX - minX + 1 > maxSize || maxY - minY + 1 > maxSize)
        {
            rejection = "too_large";
            return false;
        }

        rejection = "none";
        return true;
    }

    private bool HasExternalSupport(int originX, int originY, int size, int[] cells, int cellCount, bool[] component)
    {
        for (int i = 0; i < cellCount; i++)
        {
            int packed = cells[i];
            int x = packed % size;
            int y = packed / size;
            int belowY = y + 1;
            if (belowY < size && component[Pack(x, belowY, size)])
            {
                continue;
            }

            if (IsSolid(originX + x, originY + belowY))
            {
                return true;
            }
        }

        return false;
    }

    private int ConvertUnsupportedOverhangNear(int centerX, int centerY, int maxConversions)
    {
        int radius = Math.Clamp(FallbackOverhangRadius, 12, 96);
        int size = (radius * 2) + 1;
        int originX = centerX - radius;
        int originY = centerY - radius;
        bool[] visited = new bool[size * size];
        bool[] candidate = new bool[size * size];
        int[] queue = new int[size * size];
        int converted = 0;

        for (int localY = 0; localY < size; localY++)
        {
            for (int localX = 0; localX < size; localX++)
            {
                int worldX = originX + localX;
                int worldY = originY + localY;
                candidate[Pack(localX, localY, size)] = IsSolid(worldX, worldY) && HasOpenAirBelow(worldX, worldY);
            }
        }

        for (int localY = 0; localY < size; localY++)
        {
            for (int localX = 0; localX < size; localX++)
            {
                int start = Pack(localX, localY, size);
                if (visited[start] || !candidate[start])
                {
                    visited[start] = true;
                    continue;
                }

                int count = FloodFillCandidate(localX, localY, size, candidate, visited, queue, out int minX, out int minY, out int maxX, out int maxY);
                if (count < Math.Max(1, MinCollapsePixels))
                {
                    LastCollapseSkipReason = "fallback_too_few_pixels";
                    continue;
                }

                int growX = Math.Clamp(ImpactRadius + 4, 6, 18);
                int growUp = Math.Clamp(ImpactRadius * 3, 16, 42);
                int growDown = Math.Clamp(ImpactRadius, 4, 14);
                int worldX0 = originX + Math.Max(0, minX - growX);
                int worldY0 = originY + Math.Max(0, minY - growUp);
                int worldX1 = originX + Math.Min(size - 1, maxX + growX);
                int worldY1 = originY + Math.Min(size - 1, maxY + growDown);
                int width = worldX1 - worldX0 + 1;
                int height = worldY1 - worldY0 + 1;
                if (width > MaxCollapseRegionSize || height > MaxCollapseRegionSize)
                {
                    LastCollapseSkipReason = "fallback_too_large";
                    continue;
                }

                int solidCount = CountConvertibleSolids(worldX0, worldY0, width, height);
                if (solidCount < Math.Max(1, MinCollapsePixels))
                {
                    LastCollapseSkipReason = "fallback_empty_region";
                    continue;
                }

                if (!TryCreateBodyFromSolidBounds(worldX0, worldY0, width, height, "fallback_degenerate"))
                {
                    continue;
                }

                LastCollapseSkipReason = "fallback_converted";
                CollapsedFloatingIslands++;
                converted++;
                if (converted >= Math.Max(1, maxConversions))
                {
                    return converted;
                }
            }
        }

        return converted;
    }

    private int ConvertImpactFractureChunk(int centerX, int centerY)
    {
        int halfWidth = Math.Clamp(ImpactRadius * 5, 22, 48);
        int growUp = Math.Clamp(ImpactRadius * 4, 20, 48);
        int growDown = Math.Clamp(ImpactRadius * 3, 12, 32);
        int x = centerX - halfWidth;
        int y = centerY - growUp;
        int width = (halfWidth * 2) + 1;
        int height = growUp + growDown + 1;
        if (width > MaxCollapseRegionSize || height > MaxCollapseRegionSize)
        {
            LastCollapseSkipReason = "impact_fracture_too_large";
            return 0;
        }

        int solidCount = CountConvertibleSolids(x, y, width, height);
        if (solidCount < Math.Max(1, MinCollapsePixels))
        {
            LastCollapseSkipReason = "impact_fracture_no_solid";
            return 0;
        }

        int emptyCount = CountEmptyCells(x, y, width, height);
        if (emptyCount < Math.Max(4, ImpactRadius))
        {
            LastCollapseSkipReason = "impact_fracture_no_crater";
            return 0;
        }

        if (TryConvertNearestShelfPatch(x, y, width, height))
        {
            LastCollapseSkipReason = "impact_fracture_shelf_converted";
            CollapsedFloatingIslands++;
            return 1;
        }

        if (!TryCreateBodyFromSolidBounds(x, y, width, height, "impact_fracture_degenerate"))
        {
            return 0;
        }

        LastCollapseSkipReason = "impact_fracture_converted";
        CollapsedFloatingIslands++;
        return 1;
    }

    private bool TryConvertNearestShelfPatch(int x, int y, int width, int height)
    {
        int bestX = 0;
        int bestY = 0;
        int bestDistanceSq = int.MaxValue;
        int centerX = x + (width / 2);
        int centerY = y + Math.Clamp(ImpactRadius, 1, height);
        for (int yy = y; yy < y + height - 1; yy++)
        {
            for (int xx = x; xx < x + width - 1; xx++)
            {
                if (!IsSolid(xx, yy) ||
                    !IsSolid(xx + 1, yy) ||
                    !IsSolid(xx, yy + 1) ||
                    !IsSolid(xx + 1, yy + 1) ||
                    !HasOpenAirBelow(xx, yy + 1))
                {
                    continue;
                }

                int dx = xx - centerX;
                int dy = yy - centerY;
                int distanceSq = (dx * dx) + (dy * dy);
                if (distanceSq >= bestDistanceSq)
                {
                    continue;
                }

                bestDistanceSq = distanceSq;
                bestX = xx;
                bestY = yy;
            }
        }

        if (bestDistanceSq == int.MaxValue)
        {
            LastCollapseSkipReason = "impact_fracture_no_shelf";
            return false;
        }

        int halfWidth = Math.Clamp(ImpactRadius * 3, 12, 32);
        int growUp = Math.Clamp(ImpactRadius * 4, 14, 42);
        int growDown = Math.Clamp(ImpactRadius * 2, 8, 24);
        int patchX = bestX - halfWidth;
        int patchY = bestY - growUp;
        int patchWidth = (halfWidth * 2) + 2;
        int patchHeight = growUp + growDown + 2;
        return TryCreateConnectedSolidBodyFromSeed(
            patchX,
            patchY,
            patchWidth,
            patchHeight,
            bestX,
            bestY,
            "impact_fracture_shelf_degenerate");
    }

    private int FloodFillCandidate(
        int startX,
        int startY,
        int size,
        bool[] candidate,
        bool[] visited,
        int[] queue,
        out int minX,
        out int minY,
        out int maxX,
        out int maxY)
    {
        int head = 0;
        int tail = 0;
        int count = 0;
        minX = maxX = startX;
        minY = maxY = startY;
        int start = Pack(startX, startY, size);
        queue[tail++] = start;
        visited[start] = true;

        while (head < tail)
        {
            int packed = queue[head++];
            count++;
            int x = packed % size;
            int y = packed / size;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);

            TryEnqueueCandidate(x - 1, y, size, candidate, visited, queue, ref tail);
            TryEnqueueCandidate(x + 1, y, size, candidate, visited, queue, ref tail);
            TryEnqueueCandidate(x, y - 1, size, candidate, visited, queue, ref tail);
            TryEnqueueCandidate(x, y + 1, size, candidate, visited, queue, ref tail);
        }

        return count;
    }

    private static void TryEnqueueCandidate(int x, int y, int size, bool[] candidate, bool[] visited, int[] queue, ref int tail)
    {
        if ((uint)x >= (uint)size || (uint)y >= (uint)size)
        {
            return;
        }

        int packed = Pack(x, y, size);
        if (visited[packed])
        {
            return;
        }

        visited[packed] = true;
        if (!candidate[packed])
        {
            return;
        }

        queue[tail++] = packed;
    }

    private bool HasOpenAirBelow(int x, int y)
    {
        int probe = Math.Clamp(ImpactRadius + 2, 4, 14);
        for (int dy = 1; dy <= probe; dy++)
        {
            if (!IsSolid(x, y + dy))
            {
                return true;
            }
        }

        return false;
    }

    private int CountConvertibleSolids(int x, int y, int width, int height)
    {
        int count = 0;
        for (int yy = y; yy < y + height; yy++)
        {
            for (int xx = x; xx < x + width; xx++)
            {
                if (IsSolid(xx, yy))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private bool TryCreateBodyFromSolidBounds(int x, int y, int width, int height, string degenerateReason)
    {
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;
        int solidCount = 0;
        for (int yy = y; yy < y + height; yy++)
        {
            for (int xx = x; xx < x + width; xx++)
            {
                if (!IsSolid(xx, yy))
                {
                    continue;
                }

                minX = Math.Min(minX, xx);
                minY = Math.Min(minY, yy);
                maxX = Math.Max(maxX, xx);
                maxY = Math.Max(maxY, yy);
                solidCount++;
            }
        }

        if (solidCount < Math.Max(1, MinCollapsePixels))
        {
            LastCollapseSkipReason = degenerateReason;
            return false;
        }

        int tightWidth = maxX - minX + 1;
        int tightHeight = maxY - minY + 1;
        if (tightWidth > MaxCollapseRegionSize || tightHeight > MaxCollapseRegionSize ||
            !HasSolidBlock2x2(minX, minY, tightWidth, tightHeight))
        {
            LastCollapseSkipReason = degenerateReason;
            return false;
        }

        _ = Context.Bodies.CreateFromRegion(minX, minY, tightWidth, tightHeight);
        LastCollapsedRegion = (minX, minY, tightWidth, tightHeight);
        return true;
    }

    private bool TryCreateConnectedSolidBodyFromSeed(
        int x,
        int y,
        int width,
        int height,
        int seedX,
        int seedY,
        string degenerateReason)
    {
        if ((uint)(seedX - x) >= (uint)width || (uint)(seedY - y) >= (uint)height || !IsSolid(seedX, seedY))
        {
            LastCollapseSkipReason = degenerateReason;
            return false;
        }

        int size = width * height;
        bool[] visited = new bool[size];
        int[] queue = new int[size];
        int head = 0;
        int tail = 0;
        int count = 0;
        int minX = seedX;
        int minY = seedY;
        int maxX = seedX;
        int maxY = seedY;
        int seedLocalX = seedX - x;
        int seedLocalY = seedY - y;
        int seed = Pack(seedLocalX, seedLocalY, width);
        visited[seed] = true;
        queue[tail++] = seed;
        while (head < tail)
        {
            int packed = queue[head++];
            int localX = packed % width;
            int localY = packed / width;
            int worldX = x + localX;
            int worldY = y + localY;
            count++;
            minX = Math.Min(minX, worldX);
            minY = Math.Min(minY, worldY);
            maxX = Math.Max(maxX, worldX);
            maxY = Math.Max(maxY, worldY);
            TryEnqueueConnected(localX - 1, localY);
            TryEnqueueConnected(localX + 1, localY);
            TryEnqueueConnected(localX, localY - 1);
            TryEnqueueConnected(localX, localY + 1);
        }

        int tightWidth = maxX - minX + 1;
        int tightHeight = maxY - minY + 1;
        if (count < Math.Max(1, MinCollapsePixels) ||
            tightWidth > MaxCollapseRegionSize ||
            tightHeight > MaxCollapseRegionSize ||
            !HasSolidBlock2x2(minX, minY, tightWidth, tightHeight))
        {
            LastCollapseSkipReason = degenerateReason;
            return false;
        }

        _ = Context.Bodies.CreateFromRegion(minX, minY, tightWidth, tightHeight);
        LastCollapsedRegion = (minX, minY, tightWidth, tightHeight);
        return true;

        void TryEnqueueConnected(int localX, int localY)
        {
            if ((uint)localX >= (uint)width || (uint)localY >= (uint)height)
            {
                return;
            }

            int packed = Pack(localX, localY, width);
            if (visited[packed])
            {
                return;
            }

            visited[packed] = true;
            if (IsSolid(x + localX, y + localY))
            {
                queue[tail++] = packed;
            }
        }
    }

    private bool HasSolidBlock2x2(int x, int y, int width, int height)
    {
        if (width < 2 || height < 2)
        {
            return false;
        }

        for (int yy = y; yy < y + height - 1; yy++)
        {
            for (int xx = x; xx < x + width - 1; xx++)
            {
                if (IsSolid(xx, yy) &&
                    IsSolid(xx + 1, yy) &&
                    IsSolid(xx, yy + 1) &&
                    IsSolid(xx + 1, yy + 1))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private int CountEmptyCells(int x, int y, int width, int height)
    {
        int count = 0;
        for (int yy = y; yy < y + height; yy++)
        {
            for (int xx = x; xx < x + width; xx++)
            {
                if (Context.Cells.Sample(xx, yy).Material.Value == 0)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private bool IsSolid(int x, int y)
    {
        CellView cell = Context.Cells.Sample(x, y);
        bool solid = cell.Material.Value != 0 && !CellFlags.Has(cell.Flags, CellFlags.RigidOwned);
        if (solid)
        {
            LastCollapseSolidCandidates++;
        }

        return solid;
    }

    private static int Pack(int x, int y, int width)
    {
        return (y * width) + x;
    }

    private static ComponentBorderContact BorderContact(int x, int y, int size)
    {
        ComponentBorderContact contact = ComponentBorderContact.None;
        if (x == 0)
        {
            contact |= ComponentBorderContact.Left;
        }

        if (x == size - 1)
        {
            contact |= ComponentBorderContact.Right;
        }

        if (y == 0)
        {
            contact |= ComponentBorderContact.Top;
        }

        if (y == size - 1)
        {
            contact |= ComponentBorderContact.Bottom;
        }

        return contact;
    }

    [Flags]
    private enum ComponentBorderContact : byte
    {
        None = 0,
        Left = 1,
        Right = 2,
        Top = 4,
        Bottom = 8,
    }
}
