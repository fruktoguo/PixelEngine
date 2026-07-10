using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 可玩 Demo 的轻量射击工具，左键朝鼠标方向发射破坏弹。
/// </summary>
public sealed class PlayableProjectileTool : Behaviour
{
    private PlayerController? _player;
    private readonly CollapseScanScratch _collapseScanScratch = new();
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
    /// 命中结构伤害当量；为 0 时回退到 <see cref="ImpactForce"/>。
    /// </summary>
    public float ImpactDamage { get; set; }

    /// <summary>
    /// 是否用爆炸效果造成伤害。小枪关闭该项后仍会触发坍塌扫描，但破坏量会受材质耐久约束。
    /// </summary>
    public bool UseExplosionDamage { get; set; } = true;

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
    /// 弹道 overlay 显示时长，单位秒。保持很短，避免射击失败时被误读成卡住的实体。
    /// </summary>
    public float TracerDurationSeconds { get; set; } = 0.04f;

    /// <summary>
    /// 爆破后局部扫描半径，用于把脱离主地形的小型固体岛转换为刚体。
    /// </summary>
    public int CollapseScanRadius { get; set; } = 40;

    /// <summary>
    /// 单次射击后最多延迟扫描的帧数；保持较小，避免大窗口坍塌扫描拖住真实游玩输入。
    /// </summary>
    public int CollapseScanRetryFrames { get; set; } = 3;

    /// <summary>
    /// 可自动转换的最大连通块包围盒尺寸，避免误把整片程序化地形转成刚体。
    /// </summary>
    public int MaxCollapseRegionSize { get; set; } = 64;

    /// <summary>
    /// 可自动转换的最大固体像素数，避免把玩家脚下主地形整体提升成刚体导致碰撞丢失。
    /// </summary>
    public int MaxCollapsePixels { get; set; } = 2_048;

    /// <summary>
    /// 可自动转换的最小固体像素数。
    /// </summary>
    public int MinCollapsePixels { get; set; } = 8;

    /// <summary>
    /// 单次爆破最多转换的悬空固体岛数量，避免一枪把整片程序化山体误拆成过多刚体。
    /// </summary>
    public int MaxCollapsedIslandsPerShot { get; set; } = 1;

    /// <summary>
    /// 常规连通块扫描失败时，围绕弹坑把局部悬空边缘提升为刚体的最大半径。
    /// </summary>
    public int FallbackOverhangRadius { get; set; } = 32;

    /// <summary>
    /// 玩家周围不会被自动转换成刚体的保护半径，避免一枪把脚下承重地形转走导致穿模。
    /// </summary>
    public int PlayerSupportProtectionRadius { get; set; }

    /// <summary>
    /// 是否启用局部悬挑兜底刚体化。默认关闭，避免把仍连接主地形的弹坑边缘误拆掉。
    /// </summary>
    public bool AllowOverhangFallbackCollapse { get; set; }

    /// <summary>
    /// 是否启用弹坑兜底刚体化。默认关闭，避免可玩 Demo 把仍受支撑的地形 slab 误转为刚体。
    /// </summary>
    public bool AllowImpactFallbackCollapse { get; set; }

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

    /// <summary>
    /// 是否响应鼠标输入。真实可玩关卡默认开启；笔刷/编辑验收可显式关闭以避免左键工具冲突。
    /// </summary>
    public bool InputEnabled { get; set; } = true;

    /// <inheritdoc />
    protected override void OnStart()
    {
        _player = Entity.TryGetComponent(out PlayerController player) ? player : null;
    }

    /// <inheritdoc />
    protected override void OnDestroy()
    {
        _collapseScanScratch.Release();
    }

    internal int CollapseScanScratchCapacity => _collapseScanScratch.Capacity;

    internal int RunCollapseScanForTesting(int centerX, int centerY, int maxConversions)
    {
        return ConvertFloatingSolidIslandsNear(centerX, centerY, maxConversions);
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        float safeDt = MathF.Max(0f, dt);
        _cooldownRemaining = MathF.Max(0f, _cooldownRemaining - safeDt);
        TracerRemainingSeconds = MathF.Max(0f, TracerRemainingSeconds - safeDt);
        // 左键冷却结束后朝鼠标方向发射
        if (InputEnabled && _cooldownRemaining <= 0f && Context.Input.IsMouseDown(MouseButton.Left))
        {
            _ = TryFire();
        }

        ProcessPendingCollapseScanSafely();
    }

    /// <summary>
    /// 按当前鼠标目标立即发射一次破坏弹；供数据驱动武器控制器复用已验证的弹道与坍塌扫描后端。
    /// </summary>
    /// <returns>若成功发射则为 true。</returns>
    public bool FireOnceFromCurrentInput()
    {
        return TryFire();
    }

    private bool TryFire()
    {
        if (_player is null)
        {
            if (!Entity.TryGetComponent(out PlayerController player))
            {
                return false;
            }

            _player = player;
        }

        // 屏幕鼠标 → 世界坐标，从玩家胸口略上方沿方向射线求命中点
        (float mouseX, float mouseY) = Context.Input.MousePixel;
        Point2F target = Context.Camera.ScreenToWorld(mouseX, mouseY);
        float startX = _player.CenterX;
        float startY = _player.CenterY - 2f;
        float dx = target.X - startX;
        float dy = target.Y - startY;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length <= 0.001f)
        {
            return false;
        }

        dx /= length;
        dy /= length;
        float hitX = startX + (dx * Range);
        float hitY = startY + (dy * Range);
        if (TryRaycastSolid(startX, startY, dx, dy, Range, out RaycastHit hit) && hit.Hit)
        {
            hitX = hit.X;
            hitY = hit.Y;
        }

        // 大枪走 Explode；小枪走 DamageCircle 并补粒子反馈
        if (UseExplosionDamage)
        {
            Context.World.Explode(hitX, hitY, Math.Max(1, ImpactRadius), MathF.Max(1f, ImpactForce));
        }
        else
        {
            float damage = ImpactDamage > 0f ? ImpactDamage : ImpactForce;
            Context.World.DamageCircle(hitX, hitY, Math.Max(1, ImpactRadius), MathF.Max(1f, damage), falloff: true, DamageKind.Impact);
            EmitSmallImpactParticles(hitX, hitY);
        }

        Context.Lighting.AddPointLight(hitX, hitY, ImpactRadius * 3f, 0xFF_60_D8_FF, 0.35f);
        Context.Audio.PlayAt(UseExplosionDamage ? "explosion.wav" : "impact_stone.wav", hitX, hitY, UseExplosionDamage ? 0.85f : 0.55f);
        LastShotStartX = startX;
        LastShotStartY = startY;
        LastHitX = hitX;
        LastHitY = hitY;
        TracerRemainingSeconds = Math.Clamp(TracerDurationSeconds, 0f, 0.25f);
        ShotsFired++;
        QueueCollapseScan(hitX, hitY);
        _cooldownRemaining = MathF.Max(0f, CooldownSeconds);
        return true;
    }

    private bool TryRaycastSolid(float x, float y, float dx, float dy, float range, out RaycastHit hit)
    {
        try
        {
            _ = Context.Solids.Raycast(x, y, dx, dy, range, out hit);
            return true;
        }
        catch (InvalidOperationException exception) when (IsUnresidentChunk(exception))
        {
            hit = default;
            return false;
        }
    }

    private static bool IsUnresidentChunk(InvalidOperationException exception)
    {
        return exception.Message.Contains("目标 chunk 未驻留", StringComparison.Ordinal);
    }

    private void EmitSmallImpactParticles(float hitX, float hitY)
    {
        TransientParticleBurst.Emit(
            Context,
            hitX,
            hitY,
            Math.Clamp(ImpactRadius + 2, 3, 12),
            Math.Max(1f, ImpactForce * 0.2f),
            lifetime: 45);
    }

    private void ProcessPendingCollapseScanSafely()
    {
        try
        {
            ProcessPendingCollapseScan();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _pendingCollapseFrames = 0;
            _pendingCollapsePasses = 0;
            _pendingCollapseScans = 0;
            TracerRemainingSeconds = 0f;
            LastCollapseSkipReason = $"collapse_error_{exception.GetType().Name}";
        }
    }

    private void QueueCollapseScan(float hitX, float hitY)
    {
        _pendingCollapseX = (int)MathF.Round(hitX);
        _pendingCollapseY = (int)MathF.Round(hitY);
        _pendingCollapseFrames = 1;
        _pendingCollapsePasses = Math.Clamp(MaxCollapsedIslandsPerShot, 1, 12);
        _pendingCollapseScans = Math.Clamp(CollapseScanRetryFrames, 1, 8);
    }

    // 延迟帧扫描：爆破后等待 CA 稳定，再把悬空固体岛转为刚体
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

        int perScanConversions = Math.Clamp(_pendingCollapsePasses, 1, 3);
        int converted = ConvertFloatingSolidIslandsNear(_pendingCollapseX, _pendingCollapseY, perScanConversions);
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

    // 在弹坑周围做 4-连通 flood fill，筛选真正悬空且尺寸合规的固体岛
    private int ConvertFloatingSolidIslandsNear(int centerX, int centerY, int maxConversions)
    {
        int radius = Math.Clamp(CollapseScanRadius, 4, 320);
        int size = (radius * 2) + 1;
        int area = size * size;
        int originX = centerX - radius;
        int originY = centerY - radius;
        _collapseScanScratch.EnsureCapacity(area);
        bool[] visited = _collapseScanScratch.Visited;
        bool[] component = _collapseScanScratch.WorkingMask;
        int[] queue = _collapseScanScratch.Queue;
        int[] cells = _collapseScanScratch.Cells;
        Array.Clear(visited, 0, area);
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

                Array.Clear(component, 0, area);
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

                if (HasScanWindowExternalConnection(originX, originY, size, cells, cellCount))
                {
                    LastCollapseSkipReason = "scan_window_external_connection";
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

        if (converted < Math.Max(1, maxConversions) && AllowOverhangFallbackCollapse)
        {
            converted += ConvertUnsupportedOverhangNear(centerX, centerY, Math.Max(1, maxConversions) - converted);
        }

        if (converted == 0 && AllowImpactFallbackCollapse)
        {
            converted += ConvertImpactFractureChunk(centerX, centerY);
        }

        if (converted == 0 && AllowImpactFallbackCollapse)
        {
            converted += ConvertLocalImpactSlab(centerX, centerY);
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

        if ((borderContact & (ComponentBorderContact.Left | ComponentBorderContact.Right | ComponentBorderContact.Bottom)) != 0)
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

        if (cellCount > Math.Max(Math.Max(1, MinCollapsePixels), MaxCollapsePixels))
        {
            rejection = "too_many_pixels";
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

    private bool HasScanWindowExternalConnection(int originX, int originY, int size, int[] cells, int cellCount)
    {
        for (int i = 0; i < cellCount; i++)
        {
            int packed = cells[i];
            int x = packed % size;
            int y = packed / size;
            if (y == 0 && IsSolid(originX + x, originY - 1))
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
        int area = size * size;
        int originX = centerX - radius;
        int originY = centerY - radius;
        _collapseScanScratch.EnsureCapacity(area);
        bool[] visited = _collapseScanScratch.Visited;
        bool[] candidate = _collapseScanScratch.WorkingMask;
        int[] queue = _collapseScanScratch.Queue;
        Array.Clear(visited, 0, area);
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

                if (solidCount > Math.Max(Math.Max(1, MinCollapsePixels), MaxCollapsePixels))
                {
                    LastCollapseSkipReason = "fallback_too_many_pixels";
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

    private int ConvertLocalImpactSlab(int centerX, int centerY)
    {
        int halfWidth = Math.Clamp(ImpactRadius * 3, 14, 34);
        int growUp = Math.Clamp(ImpactRadius * 4, 18, 46);
        int growDown = Math.Clamp(ImpactRadius, 6, 16);
        int x = centerX - halfWidth;
        int y = centerY - growUp;
        int width = (halfWidth * 2) + 1;
        int height = growUp + growDown + 1;
        int solidCount = CountConvertibleSolids(x, y, width, height);
        if (solidCount < Math.Max(MinCollapsePixels, ImpactRadius * 2))
        {
            LastCollapseSkipReason = "impact_slab_too_few_pixels";
            return 0;
        }

        if (solidCount > Math.Max(Math.Max(1, MinCollapsePixels), MaxCollapsePixels))
        {
            LastCollapseSkipReason = "impact_slab_too_many_pixels";
            return 0;
        }

        if (!TryCreateBodyFromSolidBounds(x, y, width, height, "impact_slab_degenerate"))
        {
            return 0;
        }

        LastCollapseSkipReason = "impact_slab_converted";
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

        if (solidCount > Math.Max(Math.Max(1, MinCollapsePixels), MaxCollapsePixels))
        {
            LastCollapseSkipReason = "too_many_pixels";
            return false;
        }

        int tightWidth = maxX - minX + 1;
        int tightHeight = maxY - minY + 1;
        if (IntersectsPlayerSupportZone(minX, minY, tightWidth, tightHeight))
        {
            LastCollapseSkipReason = "player_support_zone";
            return false;
        }

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
        _collapseScanScratch.EnsureCapacity(size);
        bool[] visited = _collapseScanScratch.Visited;
        int[] queue = _collapseScanScratch.Queue;
        Array.Clear(visited, 0, size);
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
            TryEnqueueConnected(localX - 1, localY, x, y, width, height, visited, queue, ref tail);
            TryEnqueueConnected(localX + 1, localY, x, y, width, height, visited, queue, ref tail);
            TryEnqueueConnected(localX, localY - 1, x, y, width, height, visited, queue, ref tail);
            TryEnqueueConnected(localX, localY + 1, x, y, width, height, visited, queue, ref tail);
        }

        int tightWidth = maxX - minX + 1;
        int tightHeight = maxY - minY + 1;
        if (IntersectsPlayerSupportZone(minX, minY, tightWidth, tightHeight))
        {
            LastCollapseSkipReason = "player_support_zone";
            return false;
        }

        if (count < Math.Max(1, MinCollapsePixels) ||
            count > Math.Max(Math.Max(1, MinCollapsePixels), MaxCollapsePixels) ||
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
    }

    private void TryEnqueueConnected(
        int localX,
        int localY,
        int originX,
        int originY,
        int width,
        int height,
        bool[] visited,
        int[] queue,
        ref int tail)
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
        if (IsSolid(originX + localX, originY + localY))
        {
            queue[tail++] = packed;
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

    private bool IntersectsPlayerSupportZone(int x, int y, int width, int height)
    {
        if (_player is null && Entity.TryGetComponent(out PlayerController player))
        {
            _player = player;
        }

        if (_player is null)
        {
            return false;
        }

        CharacterState state = _player.State;
        int radius = Math.Clamp(PlayerSupportProtectionRadius, 0, 160);
        if (radius <= 0)
        {
            return false;
        }

        float zoneX = state.X - radius;
        float zoneY = state.Y - (radius * 0.5f);
        float zoneWidth = state.Width + (radius * 2f);
        float zoneHeight = state.Height + (radius * 1.75f);
        return x < zoneX + zoneWidth &&
            x + width > zoneX &&
            y < zoneY + zoneHeight &&
            y + height > zoneY;
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
        bool solid = Context.Cells.IsSolid(x, y) && !Context.Cells.IsRigidOwned(x, y);
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

    /// <summary>
    /// 为坍塌扫描按需扩容的四个实例级工作数组。主扫描、悬挑兜底和 impact fallback 串行运行，因此可以安全共享。
    /// </summary>
    private sealed class CollapseScanScratch
    {
        public bool[] Visited { get; private set; } = [];

        public bool[] WorkingMask { get; private set; } = [];

        public int[] Queue { get; private set; } = [];

        public int[] Cells { get; private set; } = [];

        internal int Capacity => Visited.Length;

        public void EnsureCapacity(int requiredLength)
        {
            if (requiredLength <= Visited.Length)
            {
                return;
            }

            int capacity = Visited.Length;
            if (capacity == 0)
            {
                capacity = requiredLength;
            }
            else
            {
                int grown = capacity <= (int.MaxValue - (capacity / 2))
                    ? capacity + (capacity / 2)
                    : int.MaxValue;
                capacity = Math.Max(requiredLength, grown);
            }

            Visited = new bool[capacity];
            WorkingMask = new bool[capacity];
            Queue = new int[capacity];
            Cells = new int[capacity];
        }

        public void Release()
        {
            Visited = [];
            WorkingMask = [];
            Queue = [];
            Cells = [];
        }
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
