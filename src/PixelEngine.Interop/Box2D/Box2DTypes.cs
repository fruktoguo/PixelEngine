using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PixelEngine.Interop.Box2D;

/// <summary>
/// Box2D v3 C ABI 常量。
/// </summary>
public static class Box2DConstants
{
    /// <summary>
    /// Box2D 凸多边形最大顶点数。
    /// </summary>
    public const int MaxPolygonVertices = 8;
}

/// <summary>
/// Box2D world opaque handle。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct B2WorldId
{
    /// <summary>1-based world index。</summary>
    public readonly ushort Index1;

    /// <summary>句柄 generation。</summary>
    public readonly ushort Generation;
}

/// <summary>
/// Box2D body opaque handle。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct B2BodyId
{
    /// <summary>1-based body index。</summary>
    public readonly int Index1;

    /// <summary>0-based world index。</summary>
    public readonly ushort World0;

    /// <summary>句柄 generation。</summary>
    public readonly ushort Generation;
}

/// <summary>
/// Box2D shape opaque handle。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct B2ShapeId
{
    /// <summary>1-based shape index。</summary>
    public readonly int Index1;

    /// <summary>0-based world index。</summary>
    public readonly ushort World0;

    /// <summary>句柄 generation。</summary>
    public readonly ushort Generation;
}

/// <summary>
/// Box2D chain opaque handle。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct B2ChainId
{
    /// <summary>1-based chain index。</summary>
    public readonly int Index1;

    /// <summary>0-based world index。</summary>
    public readonly ushort World0;

    /// <summary>句柄 generation。</summary>
    public readonly ushort Generation;
}

/// <summary>
/// Box2D 二维向量。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct B2Vec2
{
    /// <summary>X 分量。</summary>
    public float X;

    /// <summary>Y 分量。</summary>
    public float Y;
}

/// <summary>
/// Box2D cos/sin 旋转。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct B2Rot
{
    /// <summary>cos(theta)。</summary>
    public float C;

    /// <summary>sin(theta)。</summary>
    public float S;
}

/// <summary>
/// Box2D 二维刚体变换。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct B2Transform
{
    /// <summary>平移。</summary>
    public B2Vec2 P;

    /// <summary>旋转。</summary>
    public B2Rot Q;
}

/// <summary>
/// Box2D 轴对齐包围盒。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct B2AABB
{
    /// <summary>下边界。</summary>
    public B2Vec2 LowerBound;

    /// <summary>上边界。</summary>
    public B2Vec2 UpperBound;
}

/// <summary>
/// 8 个 <see cref="B2Vec2"/> 的 inline 缓冲。
/// </summary>
[InlineArray(Box2DConstants.MaxPolygonVertices)]
public struct B2Vec2Buffer8
{
    private B2Vec2 _element0;
}

/// <summary>
/// 2 个 manifold point 的 inline 缓冲。
/// </summary>
[InlineArray(2)]
public struct B2ManifoldPointBuffer2
{
    private B2ManifoldPoint _element0;
}

/// <summary>
/// Box2D 凸包。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct B2Hull
{
    /// <summary>凸包点。</summary>
    public B2Vec2Buffer8 Points;

    /// <summary>点数量。</summary>
    public int Count;
}

/// <summary>
/// Box2D 凸多边形。只能由 Box2D helper 生成，不应手填。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct B2Polygon
{
    /// <summary>多边形顶点。</summary>
    public B2Vec2Buffer8 Vertices;

    /// <summary>边法线。</summary>
    public B2Vec2Buffer8 Normals;

    /// <summary>质心。</summary>
    public B2Vec2 Centroid;

    /// <summary>外部圆角半径；PixelEngine 调用点恒传 0。</summary>
    public float Radius;

    /// <summary>顶点数量。</summary>
    public int Count;
}

/// <summary>
/// Box2D 线段。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct B2Segment
{
    /// <summary>起点。</summary>
    public B2Vec2 Point1;

    /// <summary>终点。</summary>
    public B2Vec2 Point2;
}

/// <summary>
/// Box2D 质量数据。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct B2MassData
{
    /// <summary>质量。</summary>
    public float Mass;

    /// <summary>质心。</summary>
    public B2Vec2 Center;

    /// <summary>转动惯量。</summary>
    public float RotationalInertia;
}

/// <summary>
/// Box2D task 回调函数指针。
/// </summary>
public readonly unsafe struct B2TaskCallback
{
    /// <summary>原生函数指针类型。</summary>
    public readonly delegate* unmanaged<int, int, uint, void*, void> Pointer;
}

/// <summary>
/// Box2D task enqueue 回调函数指针。
/// </summary>
public readonly unsafe struct B2EnqueueTaskCallback
{
    /// <summary>原生函数指针类型。</summary>
    public readonly delegate* unmanaged<delegate* unmanaged<int, int, uint, void*, void>, int, int, void*, void*, void*> Pointer;
}

/// <summary>
/// Box2D task finish 回调函数指针。
/// </summary>
public readonly unsafe struct B2FinishTaskCallback
{
    /// <summary>原生函数指针类型。</summary>
    public readonly delegate* unmanaged<void*, void*, void> Pointer;
}

/// <summary>
/// Box2D 摩擦混合回调。
/// </summary>
public readonly unsafe struct B2FrictionCallback
{
    /// <summary>原生函数指针类型。</summary>
    public readonly delegate* unmanaged<float, int, float, int, float> Pointer;
}

/// <summary>
/// Box2D 弹性混合回调。
/// </summary>
public readonly unsafe struct B2RestitutionCallback
{
    /// <summary>原生函数指针类型。</summary>
    public readonly delegate* unmanaged<float, int, float, int, float> Pointer;
}

/// <summary>
/// Box2D world 创建定义。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct B2WorldDef
{
    /// <summary>重力。</summary>
    public B2Vec2 Gravity;

    /// <summary>弹性速度阈值。</summary>
    public float RestitutionThreshold;

    /// <summary>hit event 速度阈值。</summary>
    public float HitEventThreshold;

    /// <summary>接触刚度频率。</summary>
    public float ContactHertz;

    /// <summary>接触阻尼比。</summary>
    public float ContactDampingRatio;

    /// <summary>最大接触推出速度。</summary>
    public float MaxContactPushSpeed;

    /// <summary>最大线速度。</summary>
    public float MaximumLinearSpeed;

    /// <summary>摩擦混合回调。</summary>
    public delegate* unmanaged<float, int, float, int, float> FrictionCallback;

    /// <summary>弹性混合回调。</summary>
    public delegate* unmanaged<float, int, float, int, float> RestitutionCallback;

    /// <summary>是否启用 sleep。</summary>
    public byte EnableSleep;

    /// <summary>是否启用连续碰撞。</summary>
    public byte EnableContinuous;

    /// <summary>worker 数。</summary>
    public int WorkerCount;

    /// <summary>task enqueue 回调。</summary>
    public delegate* unmanaged<delegate* unmanaged<int, int, uint, void*, void>, int, int, void*, void*, void*> EnqueueTask;

    /// <summary>task finish 回调。</summary>
    public delegate* unmanaged<void*, void*, void> FinishTask;

    /// <summary>task 用户上下文。</summary>
    public void* UserTaskContext;

    /// <summary>world 用户数据。</summary>
    public void* UserData;

    /// <summary>Box2D 内部校验值。</summary>
    public int InternalValue;
}

/// <summary>
/// Box2D body 类型。
/// </summary>
public enum B2BodyType
{
    /// <summary>静态 body。</summary>
    StaticBody = 0,

    /// <summary>运动学 body。</summary>
    KinematicBody = 1,

    /// <summary>动态 body。</summary>
    DynamicBody = 2,

    /// <summary>body 类型数量。</summary>
    BodyTypeCount = 3,
}

/// <summary>
/// Box2D body 创建定义。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct B2BodyDef
{
    /// <summary>body 类型。</summary>
    public B2BodyType Type;

    /// <summary>初始位置。</summary>
    public B2Vec2 Position;

    /// <summary>初始旋转。</summary>
    public B2Rot Rotation;

    /// <summary>初始线速度。</summary>
    public B2Vec2 LinearVelocity;

    /// <summary>初始角速度。</summary>
    public float AngularVelocity;

    /// <summary>线性阻尼。</summary>
    public float LinearDamping;

    /// <summary>角阻尼。</summary>
    public float AngularDamping;

    /// <summary>重力缩放。</summary>
    public float GravityScale;

    /// <summary>sleep 速度阈值。</summary>
    public float SleepThreshold;

    /// <summary>调试名称。</summary>
    public byte* Name;

    /// <summary>用户数据。</summary>
    public void* UserData;

    /// <summary>是否允许 sleep。</summary>
    public byte EnableSleep;

    /// <summary>初始是否醒着。</summary>
    public byte IsAwake;

    /// <summary>是否固定旋转。</summary>
    public byte FixedRotation;

    /// <summary>是否作为 bullet。</summary>
    public byte IsBullet;

    /// <summary>是否启用。</summary>
    public byte IsEnabled;

    /// <summary>是否允许快速旋转。</summary>
    public byte AllowFastRotation;

    /// <summary>Box2D 内部校验值。</summary>
    public int InternalValue;
}

/// <summary>
/// Box2D 碰撞过滤器。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct B2Filter
{
    /// <summary>类别位。</summary>
    public ulong CategoryBits;

    /// <summary>碰撞掩码位。</summary>
    public ulong MaskBits;

    /// <summary>碰撞组索引。</summary>
    public int GroupIndex;
}

/// <summary>
/// Box2D 表面材质。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct B2SurfaceMaterial
{
    /// <summary>摩擦系数。</summary>
    public float Friction;

    /// <summary>弹性系数。</summary>
    public float Restitution;

    /// <summary>滚动阻力。</summary>
    public float RollingResistance;

    /// <summary>切向速度。</summary>
    public float TangentSpeed;

    /// <summary>用户材质 id。</summary>
    public int UserMaterialId;

    /// <summary>调试颜色。</summary>
    public uint CustomColor;
}

/// <summary>
/// Box2D shape 创建定义。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct B2ShapeDef
{
    /// <summary>用户数据。</summary>
    public void* UserData;

    /// <summary>表面材质。</summary>
    public B2SurfaceMaterial Material;

    /// <summary>密度。</summary>
    public float Density;

    /// <summary>碰撞过滤器。</summary>
    public B2Filter Filter;

    /// <summary>是否 sensor。</summary>
    public byte IsSensor;

    /// <summary>是否启用 sensor event。</summary>
    public byte EnableSensorEvents;

    /// <summary>是否启用 contact event。</summary>
    public byte EnableContactEvents;

    /// <summary>是否启用 hit event。</summary>
    public byte EnableHitEvents;

    /// <summary>是否启用 pre-solve event。</summary>
    public byte EnablePreSolveEvents;

    /// <summary>是否立即创建 contact。</summary>
    public byte InvokeContactCreation;

    /// <summary>是否更新 body mass。</summary>
    public byte UpdateBodyMass;

    /// <summary>Box2D 内部校验值。</summary>
    public int InternalValue;
}

/// <summary>
/// Box2D chain 创建定义。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct B2ChainDef
{
    /// <summary>用户数据。</summary>
    public void* UserData;

    /// <summary>点数组。</summary>
    public B2Vec2* Points;

    /// <summary>点数量。</summary>
    public int Count;

    /// <summary>材质数组。</summary>
    public B2SurfaceMaterial* Materials;

    /// <summary>材质数量。</summary>
    public int MaterialCount;

    /// <summary>过滤器。</summary>
    public B2Filter Filter;

    /// <summary>是否闭合。</summary>
    public byte IsLoop;

    /// <summary>是否启用 sensor event。</summary>
    public byte EnableSensorEvents;

    /// <summary>Box2D 内部校验值。</summary>
    public int InternalValue;
}

/// <summary>
/// Box2D manifold point。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct B2ManifoldPoint
{
    /// <summary>世界接触点。</summary>
    public B2Vec2 Point;

    /// <summary>相对 shape A 的 anchor。</summary>
    public B2Vec2 AnchorA;

    /// <summary>相对 shape B 的 anchor。</summary>
    public B2Vec2 AnchorB;

    /// <summary>分离距离。</summary>
    public float Separation;

    /// <summary>法向冲量。</summary>
    public float NormalImpulse;

    /// <summary>切向冲量。</summary>
    public float TangentImpulse;

    /// <summary>总法向冲量。</summary>
    public float TotalNormalImpulse;

    /// <summary>求解前法向速度。</summary>
    public float NormalVelocity;

    /// <summary>接触点 id。</summary>
    public ushort Id;

    /// <summary>上一帧是否存在。</summary>
    public byte Persisted;
}

/// <summary>
/// Box2D contact manifold。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct B2Manifold
{
    /// <summary>世界法线。</summary>
    public B2Vec2 Normal;

    /// <summary>滚动阻力角冲量。</summary>
    public float RollingImpulse;

    /// <summary>接触点。</summary>
    public B2ManifoldPointBuffer2 Points;

    /// <summary>接触点数量。</summary>
    public int PointCount;
}

/// <summary>
/// Box2D begin-touch contact event。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct B2ContactBeginTouchEvent
{
    /// <summary>shape A。</summary>
    public B2ShapeId ShapeIdA;

    /// <summary>shape B。</summary>
    public B2ShapeId ShapeIdB;

    /// <summary>初始 manifold。</summary>
    public B2Manifold Manifold;
}

/// <summary>
/// Box2D end-touch contact event。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct B2ContactEndTouchEvent
{
    /// <summary>shape A。</summary>
    public B2ShapeId ShapeIdA;

    /// <summary>shape B。</summary>
    public B2ShapeId ShapeIdB;
}

/// <summary>
/// Box2D hit contact event。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct B2ContactHitEvent
{
    /// <summary>shape A。</summary>
    public B2ShapeId ShapeIdA;

    /// <summary>shape B。</summary>
    public B2ShapeId ShapeIdB;

    /// <summary>碰撞点。</summary>
    public B2Vec2 Point;

    /// <summary>碰撞法线。</summary>
    public B2Vec2 Normal;

    /// <summary>接近速度。</summary>
    public float ApproachSpeed;
}

/// <summary>
/// Box2D contact event 数组视图。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct B2ContactEvents
{
    /// <summary>begin-touch event 数组。</summary>
    public B2ContactBeginTouchEvent* BeginEvents;

    /// <summary>end-touch event 数组。</summary>
    public B2ContactEndTouchEvent* EndEvents;

    /// <summary>hit event 数组。</summary>
    public B2ContactHitEvent* HitEvents;

    /// <summary>begin-touch 数量。</summary>
    public int BeginCount;

    /// <summary>end-touch 数量。</summary>
    public int EndCount;

    /// <summary>hit 数量。</summary>
    public int HitCount;
}

/// <summary>
/// Box2D body move event。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct B2BodyMoveEvent
{
    /// <summary>新变换。</summary>
    public B2Transform Transform;

    /// <summary>body id。</summary>
    public B2BodyId BodyId;

    /// <summary>用户数据。</summary>
    public void* UserData;

    /// <summary>是否进入 sleep。</summary>
    public byte FellAsleep;
}

/// <summary>
/// Box2D body event 数组视图。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct B2BodyEvents
{
    /// <summary>move event 数组。</summary>
    public B2BodyMoveEvent* MoveEvents;

    /// <summary>move event 数量。</summary>
    public int MoveCount;
}
