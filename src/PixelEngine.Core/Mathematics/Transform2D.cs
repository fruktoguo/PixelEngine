using System.Numerics;

namespace PixelEngine.Core.Mathematics;

/// <summary>
/// 表示二维刚体变换，旋转以 cos/sin 形式保存以对齐 Box2D 的 <c>b2Rot</c> 约定。
/// </summary>
/// <remarks>
/// 使用位置与 cos/sin 旋转创建二维变换。
/// </remarks>
/// <param name="position">世界空间位置。</param>
/// <param name="cos">旋转角的余弦值。</param>
/// <param name="sin">旋转角的正弦值。</param>
public readonly struct Transform2D(Vector2 position, float cos, float sin)
{
    /// <summary>
    /// 世界空间位置。
    /// </summary>
    public readonly Vector2 Position = position;

    /// <summary>
    /// 旋转角的余弦值。
    /// </summary>
    public readonly float Cos = cos;

    /// <summary>
    /// 旋转角的正弦值。
    /// </summary>
    public readonly float Sin = sin;

    /// <summary>
    /// 单位变换。
    /// </summary>
    public static readonly Transform2D Identity = new(Vector2.Zero, 1f, 0f);

    /// <summary>
    /// 使用位置与弧度角创建二维变换。
    /// </summary>
    /// <param name="position">世界空间位置。</param>
    /// <param name="radians">旋转弧度。</param>
    public Transform2D(Vector2 position, float radians)
        : this(position, MathF.Cos(radians), MathF.Sin(radians))
    {
    }

    /// <summary>
    /// 获取当前旋转角的弧度值。
    /// </summary>
    public float Angle => MathF.Atan2(Sin, Cos);

    /// <summary>
    /// 将局部空间点变换到世界空间。
    /// </summary>
    /// <param name="local">局部空间点。</param>
    /// <returns>世界空间点。</returns>
    public Vector2 TransformPoint(Vector2 local)
    {
        return Position + TransformDirection(local);
    }

    /// <summary>
    /// 将世界空间点逆变换到局部空间。
    /// </summary>
    /// <param name="world">世界空间点。</param>
    /// <returns>局部空间点。</returns>
    public Vector2 InverseTransformPoint(Vector2 world)
    {
        Vector2 translated = world - Position;
        return new Vector2(
            (translated.X * Cos) + (translated.Y * Sin),
            (-translated.X * Sin) + (translated.Y * Cos));
    }

    /// <summary>
    /// 将局部空间方向变换到世界空间，不应用平移。
    /// </summary>
    /// <param name="direction">局部空间方向。</param>
    /// <returns>世界空间方向。</returns>
    public Vector2 TransformDirection(Vector2 direction)
    {
        return new Vector2(
            (direction.X * Cos) - (direction.Y * Sin),
            (direction.X * Sin) + (direction.Y * Cos));
    }
}
