using System.Numerics;
using PixelEngine.Core.Mathematics;

namespace PixelEngine.Physics;

/// <summary>
/// 表示供存档系统读取的不可变刚体快照。
/// </summary>
/// <param name="BodyKey">托管刚体 key。</param>
/// <param name="Transform">像素世界空间刚体变换。</param>
/// <param name="LinearVelocityPixelsPerSecond">像素/秒线速度。</param>
/// <param name="AngularVelocityRadiansPerSecond">弧度/秒角速度。</param>
/// <param name="Mask">body-local 不可变权威形状 mask。</param>
public readonly record struct RigidBodySnapshot(
    int BodyKey,
    Transform2D Transform,
    Vector2 LinearVelocityPixelsPerSecond,
    float AngularVelocityRadiansPerSecond,
    BodyLocalMask Mask);
