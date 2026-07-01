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
    public List<RigidStamp> PreviousStamps { get; } = [];
}
