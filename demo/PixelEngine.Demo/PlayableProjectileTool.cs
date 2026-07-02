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
    public int CollapseScanRadius { get; set; } = 220;

    /// <summary>
    /// 可自动转换的最大连通块包围盒尺寸，避免误把整片程序化地形转成刚体。
    /// </summary>
    public int MaxCollapseRegionSize { get; set; } = 320;

    /// <summary>
    /// 可自动转换的最小固体像素数。
    /// </summary>
    public int MinCollapsePixels { get; set; } = 8;

    /// <summary>
    /// 单次爆破最多转换的悬空固体岛数量，避免一枪把整片程序化山体误拆成过多刚体。
    /// </summary>
    public int MaxCollapsedIslandsPerShot { get; set; } = 10;

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
        _pendingCollapseFrames = 2;
        _pendingCollapsePasses = Math.Clamp(MaxCollapsedIslandsPerShot, 1, 12);
        _pendingCollapseScans = 5;
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

        int converted = ConvertFloatingSolidIslandsNear(_pendingCollapseX, _pendingCollapseY, _pendingCollapsePasses);
        if (converted > 0)
        {
            _pendingCollapsePasses = 0;
            _pendingCollapseScans = 0;
            return;
        }

        _pendingCollapseScans--;
        if (_pendingCollapsePasses > 0 && _pendingCollapseScans > 0)
        {
            _pendingCollapseFrames = 2;
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
                _ = Context.Bodies.CreateFromRegion(worldX, worldY, width, height);
                LastCollapsedRegion = (worldX, worldY, width, height);
                LastCollapseSkipReason = "converted";
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
