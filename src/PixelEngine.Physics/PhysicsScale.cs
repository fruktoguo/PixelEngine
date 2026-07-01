using System.Numerics;
using System.Runtime.CompilerServices;
using PixelEngine.Core;
using PixelEngine.Core.Mathematics;
using PixelEngine.Interop.Box2D;

namespace PixelEngine.Physics;

/// <summary>
/// PixelEngine 像素坐标与 Box2D 物理坐标之间的集中转换入口。
/// </summary>
public static class PhysicsScale
{
    /// <summary>
    /// 每米对应的像素数，见架构 §8.1。
    /// </summary>
    public const int PixelsPerMeter = EngineConstants.PhysicsPixelsPerMeter;

    /// <summary>
    /// 每像素对应的物理单位数。
    /// </summary>
    public const float UnitsPerPixel = EngineConstants.MetersPerPixel;

    /// <summary>
    /// PixelEngine 刚体多边形半径恒为 0，保持像素锐利边缘。
    /// </summary>
    public const float SharpPolygonRadius = 0f;

    private static int _lengthUnitsConfigured;

    /// <summary>
    /// 对 Box2D 设置一次长度单位比例。必须在创建 world 前调用。
    /// </summary>
    public static void ConfigureBox2DLengthUnits()
    {
        if (Interlocked.Exchange(ref _lengthUnitsConfigured, 1) == 0)
        {
            Box2D.b2SetLengthUnitsPerMeter(PixelsPerMeter);
        }
    }

    /// <summary>
    /// 将整数像素距离转换为 Box2D 物理单位。
    /// </summary>
    /// <param name="pixels">像素距离。</param>
    /// <returns>物理单位距离。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float PixelToPhysics(int pixels)
    {
        return pixels * UnitsPerPixel;
    }

    /// <summary>
    /// 将浮点像素距离转换为 Box2D 物理单位。
    /// </summary>
    /// <param name="pixels">像素距离。</param>
    /// <returns>物理单位距离。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float PixelToPhysics(float pixels)
    {
        return pixels * UnitsPerPixel;
    }

    /// <summary>
    /// 将 Box2D 物理单位距离转换为浮点像素距离。
    /// </summary>
    /// <param name="units">物理单位距离。</param>
    /// <returns>像素距离。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float PhysicsToPixel(float units)
    {
        return units * PixelsPerMeter;
    }

    /// <summary>
    /// 将 cell 坐标转换为 Box2D 物理坐标。
    /// </summary>
    /// <param name="cell">cell 坐标。</param>
    /// <returns>物理坐标。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static B2Vec2 ToPhysics(Vector2i cell)
    {
        return new B2Vec2
        {
            X = PixelToPhysics(cell.X),
            Y = PixelToPhysics(cell.Y),
        };
    }

    /// <summary>
    /// 将 Box2D 物理坐标转换为最近 cell 坐标。
    /// </summary>
    /// <param name="position">物理坐标。</param>
    /// <returns>最近 cell 坐标。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2i ToCell(B2Vec2 position)
    {
        return Vector2i.Round(new Vector2(PhysicsToPixel(position.X), PhysicsToPixel(position.Y)));
    }

    /// <summary>
    /// 将 Box2D 变换转换为像素世界坐标变换。
    /// </summary>
    /// <param name="transform">Box2D 物理变换。</param>
    /// <returns>像素坐标系变换。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Transform2D ToTransform2D(B2Transform transform)
    {
        return new Transform2D(
            new Vector2(PhysicsToPixel(transform.P.X), PhysicsToPixel(transform.P.Y)),
            transform.Q.C,
            transform.Q.S);
    }

    /// <summary>
    /// 用 Box2D 凸包创建半径为 0 的锐利多边形。
    /// </summary>
    /// <param name="hull">Box2D 凸包。</param>
    /// <returns>Box2D 凸多边形。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static B2Polygon MakeSharpPolygon(in B2Hull hull)
    {
        return Box2D.b2MakePolygon(in hull, SharpPolygonRadius);
    }
}
