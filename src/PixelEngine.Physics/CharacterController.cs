using System.Numerics;
using PixelEngine.Core.Mathematics;
using PixelEngine.Simulation;

namespace PixelEngine.Physics;

/// <summary>
/// 独立于 Box2D 的 kinematic AABB 角色控制器，直接对权威像素场做碰撞解算。
/// </summary>
public sealed class CharacterController
{
    private const float CollisionStep = 0.25f;
    private const float GroundProbeDistance = CollisionStep + 0.1f;

    private readonly CellGrid _grid;

    /// <summary>
    /// 创建角色控制器。坐标系使用像素世界坐标，Y 正方向向下，<paramref name="position"/> 表示 AABB 左上角。
    /// </summary>
    /// <param name="grid">权威 cell 网格只读查询入口。</param>
    /// <param name="position">初始 AABB 左上角。</param>
    /// <param name="size">AABB 尺寸，单位 cell。</param>
    public CharacterController(CellGrid grid, Vector2 position, Vector2 size)
    {
        _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        if (size.X <= 0f || size.Y <= 0f || float.IsNaN(size.X) || float.IsNaN(size.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "角色 AABB 尺寸必须为有限正数。");
        }

        Position = position;
        Size = size;
    }

    /// <summary>
    /// AABB 左上角世界坐标。
    /// </summary>
    public Vector2 Position { get; private set; }

    /// <summary>
    /// AABB 尺寸。
    /// </summary>
    public Vector2 Size { get; }

    /// <summary>
    /// 碰撞采样内缩距离，避免浮点边界抖动。
    /// </summary>
    public float SkinWidth { get; set; } = 0.05f;

    /// <summary>
    /// 每次 Move 单轴最多子迭代数。
    /// </summary>
    public int MaxSubIterations { get; set; } = 128;

    /// <summary>
    /// 水平移动受阻时允许尝试向上爬升的像素高度。
    /// </summary>
    public int StepUpHeight { get; set; } = 6;

    /// <summary>
    /// 当前 AABB。
    /// </summary>
    public AABB Bounds => BoundsAt(Position);

    /// <summary>
    /// 当前是否站在固体像素上。
    /// </summary>
    public bool IsGrounded => ProbeBelow(Position);

    /// <summary>
    /// 当前是否接触左墙。
    /// </summary>
    public bool IsTouchingWallLeft => ProbeSide(Position, direction: -1);

    /// <summary>
    /// 当前是否接触右墙。
    /// </summary>
    public bool IsTouchingWallRight => ProbeSide(Position, direction: 1);

    /// <summary>
    /// 读取当前地面坡度估算。
    /// </summary>
    public float SlopeAngle => EstimateSlopeAngle(Position);

    /// <summary>
    /// 立即设置 AABB 左上角位置。
    /// </summary>
    public void SetPosition(Vector2 position)
    {
        Position = position;
    }

    /// <summary>
    /// 尝试按请求位移移动角色，并输出碰撞信息。
    /// </summary>
    /// <param name="desired">请求位移，单位 cell。</param>
    /// <param name="info">移动结果。</param>
    public void Move(in Vector2 desired, out CharacterCollisionInfo info)
    {
        ValidateRuntimeSettings();

        Vector2 start = Position;
        bool hitCeiling = false;
        bool hitWallLeft = false;
        bool hitWallRight = false;

        // 分轴解算：先水平再垂直，避免斜向移动在角点卡死。
        if (desired.X != 0f)
        {
            MoveHorizontal(desired.X, ref hitWallLeft, ref hitWallRight);
        }

        if (desired.Y != 0f)
        {
            MoveVertical(desired.Y, ref hitCeiling);
        }

        Vector2 applied = Position - start;
        bool grounded = ProbeBelow(Position);
        bool wallLeft = hitWallLeft || ProbeSide(Position, direction: -1);
        bool wallRight = hitWallRight || ProbeSide(Position, direction: 1);
        info = new CharacterCollisionInfo(
            Position,
            Bounds,
            desired,
            applied,
            grounded,
            hitCeiling,
            wallLeft,
            wallRight,
            grounded ? EstimateSlopeAngle(Position) : 0f);
    }

    /// <summary>
    /// 查询指定 AABB 是否与固体像素相交。<see cref="CellFlags.RigidOwned"/> 会被视为固体。
    /// </summary>
    public bool OverlapsSolid(in AABB bounds)
    {
        RectI rect = bounds.ToRectI();
        for (int y = rect.MinY; y < rect.MaxY; y++)
        {
            for (int x = rect.MinX; x < rect.MaxX; x++)
            {
                if (IsSolidCell(x, y))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void MoveHorizontal(float delta, ref bool hitWallLeft, ref bool hitWallRight)
    {
        float sign = MathF.Sign(delta);
        float remaining = MathF.Abs(delta);
        int iterations = 0;
        // 固定步长子迭代，碰撞时尝试 StepUp 再判定撞墙。
        while (remaining > 0f && iterations++ < MaxSubIterations)
        {
            float step = MathF.Min(CollisionStep, remaining) * sign;
            Vector2 next = Position + new Vector2(step, 0f);
            if (!OverlapsSolid(BoundsAt(next)))
            {
                Position = next;
                remaining -= MathF.Abs(step);
                continue;
            }

            if (TryStepUp(step))
            {
                remaining -= MathF.Abs(step);
                continue;
            }

            if (sign < 0f)
            {
                hitWallLeft = true;
            }
            else
            {
                hitWallRight = true;
            }

            return;
        }
    }

    private void MoveVertical(float delta, ref bool hitCeiling)
    {
        float sign = MathF.Sign(delta);
        float remaining = MathF.Abs(delta);
        int iterations = 0;
        while (remaining > 0f && iterations++ < MaxSubIterations)
        {
            float step = MathF.Min(CollisionStep, remaining) * sign;
            Vector2 next = Position + new Vector2(0f, step);
            if (!OverlapsSolid(BoundsAt(next)))
            {
                Position = next;
                remaining -= MathF.Abs(step);
                continue;
            }

            if (sign < 0f)
            {
                hitCeiling = true;
            }

            return;
        }
    }

    private bool TryStepUp(float horizontalStep)
    {
        if (StepUpHeight <= 0 || !ProbeBelow(Position))
        {
            return false;
        }

        // 逐像素抬高 AABB，找到可水平通过的台阶高度。
        for (int stepUp = 1; stepUp <= StepUpHeight; stepUp++)
        {
            Vector2 raised = Position + new Vector2(0f, -stepUp);
            if (OverlapsSolid(BoundsAt(raised)))
            {
                continue;
            }

            Vector2 moved = raised + new Vector2(horizontalStep, 0f);
            if (OverlapsSolid(BoundsAt(moved)))
            {
                continue;
            }

            Position = moved;
            return true;
        }

        return false;
    }

    private bool ProbeBelow(Vector2 position)
    {
        AABB bounds = BoundsAt(position);
        float y = bounds.Max.Y + GroundProbeDistance;
        int minX = (int)MathF.Floor(bounds.Min.X + SkinWidth);
        int maxX = (int)MathF.Ceiling(bounds.Max.X - SkinWidth) - 1;
        int cellY = (int)MathF.Floor(y);
        for (int x = minX; x <= maxX; x++)
        {
            if (IsSolidCell(x, cellY))
            {
                return true;
            }
        }

        return false;
    }

    private bool ProbeSide(Vector2 position, int direction)
    {
        AABB bounds = BoundsAt(position);
        float x = direction < 0 ? bounds.Min.X - 0.05f : bounds.Max.X + 0.05f;
        int cellX = (int)MathF.Floor(x);
        int minY = (int)MathF.Floor(bounds.Min.Y + SkinWidth);
        int maxY = (int)MathF.Ceiling(bounds.Max.Y - SkinWidth) - 1;
        for (int y = minY; y <= maxY; y++)
        {
            if (IsSolidCell(cellX, y))
            {
                return true;
            }
        }

        return false;
    }

    private float EstimateSlopeAngle(Vector2 position)
    {
        AABB bounds = BoundsAt(position);
        int leftX = (int)MathF.Floor(bounds.Min.X + SkinWidth);
        int rightX = (int)MathF.Ceiling(bounds.Max.X - SkinWidth) - 1;
        int baseY = (int)MathF.Floor(bounds.Max.Y + GroundProbeDistance);
        int leftHeight = FindGroundOffset(leftX, baseY);
        int rightHeight = FindGroundOffset(rightX, baseY);
        return MathF.Atan2(rightHeight - leftHeight, Math.Max(1, rightX - leftX));
    }

    private int FindGroundOffset(int x, int baseY)
    {
        for (int offset = -StepUpHeight; offset <= StepUpHeight; offset++)
        {
            if (IsSolidCell(x, baseY + offset))
            {
                return offset;
            }
        }

        return 0;
    }

    private AABB BoundsAt(Vector2 position)
    {
        Vector2 min = position + new Vector2(SkinWidth, SkinWidth);
        Vector2 max = position + Size - new Vector2(SkinWidth, SkinWidth);
        return new AABB(min, max);
    }

    private bool IsSolidCell(int x, int y)
    {
        if (!_grid.TryGetMaterial(x, y, out ushort material))
        {
            return false;
        }

        byte flags = _grid.FlagsAt(x, y);
        // RigidOwned stamp 与固体同等阻挡，供角色与碎块交互。
        if (CellFlags.Has(flags, CellFlags.RigidOwned))
        {
            return true;
        }

        if (material == 0)
        {
            return false;
        }

        if (material >= _grid.MaterialProps.Count)
        {
            return true;
        }

        CellType type = _grid.MaterialProps.TypeOf(material);
        return type is CellType.Solid or CellType.Powder;
    }

    private void ValidateRuntimeSettings()
    {
        if (SkinWidth < 0f || SkinWidth * 2f >= MathF.Min(Size.X, Size.Y))
        {
            throw new InvalidOperationException("SkinWidth 必须非负，且不能超过 AABB 最短边的一半。");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxSubIterations);
        ArgumentOutOfRangeException.ThrowIfNegative(StepUpHeight);
    }
}
