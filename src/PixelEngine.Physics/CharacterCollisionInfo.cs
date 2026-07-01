using System.Numerics;
using PixelEngine.Core.Mathematics;

namespace PixelEngine.Physics;

/// <summary>
/// 角色控制器一次移动后的碰撞状态。
/// </summary>
/// <param name="Position">移动后的 AABB 左上角位置。</param>
/// <param name="Bounds">移动后的 AABB。</param>
/// <param name="RequestedDelta">请求位移。</param>
/// <param name="AppliedDelta">实际位移。</param>
/// <param name="IsGrounded">下边缘是否接触固体像素。</param>
/// <param name="HitCeiling">本次移动是否撞到上方固体。</param>
/// <param name="HitWallLeft">左边缘是否接触固体。</param>
/// <param name="HitWallRight">右边缘是否接触固体。</param>
/// <param name="SlopeAngle">地面估算坡度，单位弧度；平地为 0。</param>
public readonly record struct CharacterCollisionInfo(
    Vector2 Position,
    AABB Bounds,
    Vector2 RequestedDelta,
    Vector2 AppliedDelta,
    bool IsGrounded,
    bool HitCeiling,
    bool HitWallLeft,
    bool HitWallRight,
    float SlopeAngle);
