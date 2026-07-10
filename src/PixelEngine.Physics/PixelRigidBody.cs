using PixelEngine.Core.Mathematics;
using PixelEngine.Interop.Box2D;

namespace PixelEngine.Physics;

/// <summary>
/// 托管侧像素刚体包装。
/// </summary>
public sealed class PixelRigidBody
{
    /// <summary>
    /// 创建像素刚体包装。
    /// </summary>
    public PixelRigidBody(int bodyKey, B2BodyId bodyId, BodyLocalMask mask)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bodyKey);
        BodyKey = bodyKey;
        BodyId = bodyId;
        Mask = mask ?? throw new ArgumentNullException(nameof(mask));
        // inverse-sampling 会扫描旋转后的 AABB；以 mask 面积初始化会在首次斜角 stamp 时扩容，
        // 从而违反 CA↔刚体往返热路径的稳态零分配约束（docs §8.3）。
        PreviousStamps = new List<RigidStampedCell>(GetInitialStampCapacity(mask));
    }

    /// <summary>稠密 body key。</summary>
    public int BodyKey { get; }

    /// <summary>Box2D body 句柄。</summary>
    public B2BodyId BodyId { get; }

    /// <summary>不可变权威形状源。</summary>
    public BodyLocalMask Mask { get; }

    /// <summary>上一帧变换。</summary>
    public Transform2D PreviousTransform { get; set; } = Transform2D.Identity;

    /// <summary>上一帧 stamp 列表。</summary>
    public List<RigidStampedCell> PreviousStamps { get; }

    private static int GetInitialStampCapacity(BodyLocalMask mask)
    {
        long diagonalSquared = checked(((long)mask.Width * mask.Width) + ((long)mask.Height * mask.Height));
        // ComputeWorldBounds 在投影边界两侧各额外扩一格；+4 覆盖 floor/ceiling 的最坏取整差。
        int maximumAxisCells = checked((int)Math.Ceiling(Math.Sqrt(diagonalSquared)) + 4);
        return checked(maximumAxisCells * maximumAxisCells);
    }
}
