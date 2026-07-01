using System.Runtime.InteropServices;

namespace PixelEngine.Interop.Box2D;

/// <summary>
/// Box2D v3.1 C API 的 source-generated P/Invoke 薄绑定。
/// </summary>
public static unsafe partial class Box2D
{
    /// <summary>
    /// 设置 Box2D 的长度单位比例。必须在创建 world 前调用。
    /// </summary>
    /// <param name="lengthUnits">每米对应的长度单位数。</param>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial void b2SetLengthUnitsPerMeter(float lengthUnits);

    /// <summary>
    /// 创建默认 world 定义。
    /// </summary>
    /// <returns>默认 world 定义。</returns>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial B2WorldDef b2DefaultWorldDef();

    /// <summary>
    /// 创建 Box2D world。
    /// </summary>
    /// <param name="def">world 定义。</param>
    /// <returns>world 句柄。</returns>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial B2WorldId b2CreateWorld(in B2WorldDef def);

    /// <summary>
    /// 销毁 Box2D world。
    /// </summary>
    /// <param name="worldId">world 句柄。</param>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial void b2DestroyWorld(B2WorldId worldId);

    /// <summary>
    /// 推进 Box2D world 一步。此调用不得使用 SuppressGCTransition。
    /// </summary>
    /// <param name="worldId">world 句柄。</param>
    /// <param name="timeStep">固定时间步长。</param>
    /// <param name="subStepCount">Box2D 内部子步数量。</param>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial void b2World_Step(B2WorldId worldId, float timeStep, int subStepCount);

    /// <summary>
    /// 读取本步 body move events。
    /// </summary>
    /// <param name="worldId">world 句柄。</param>
    /// <returns>transient body event 数组视图。</returns>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial B2BodyEvents b2World_GetBodyEvents(B2WorldId worldId);

    /// <summary>
    /// 读取本步 contact events。
    /// </summary>
    /// <param name="worldId">world 句柄。</param>
    /// <returns>transient contact event 数组视图。</returns>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial B2ContactEvents b2World_GetContactEvents(B2WorldId worldId);

    /// <summary>
    /// 创建默认 body 定义。
    /// </summary>
    /// <returns>默认 body 定义。</returns>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial B2BodyDef b2DefaultBodyDef();

    /// <summary>
    /// 创建 body。
    /// </summary>
    /// <param name="worldId">world 句柄。</param>
    /// <param name="def">body 定义。</param>
    /// <returns>body 句柄。</returns>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial B2BodyId b2CreateBody(B2WorldId worldId, in B2BodyDef def);

    /// <summary>
    /// 销毁 body。
    /// </summary>
    /// <param name="bodyId">body 句柄。</param>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial void b2DestroyBody(B2BodyId bodyId);

    /// <summary>
    /// 设置 body 用户数据。
    /// </summary>
    /// <param name="bodyId">body 句柄。</param>
    /// <param name="userData">用户数据指针。</param>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial void b2Body_SetUserData(B2BodyId bodyId, void* userData);

    /// <summary>
    /// 获取 body 用户数据。
    /// </summary>
    /// <param name="bodyId">body 句柄。</param>
    /// <returns>用户数据指针。</returns>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial void* b2Body_GetUserData(B2BodyId bodyId);

    /// <summary>
    /// 计算凸包。
    /// </summary>
    /// <param name="points">输入点。</param>
    /// <param name="count">输入点数量。</param>
    /// <returns>Box2D 凸包。</returns>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial B2Hull b2ComputeHull(B2Vec2* points, int count);

    /// <summary>
    /// 从凸包创建凸多边形。PixelEngine 调用点半径恒传 0。
    /// </summary>
    /// <param name="hull">Box2D 凸包。</param>
    /// <param name="radius">外部半径。</param>
    /// <returns>Box2D 凸多边形。</returns>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial B2Polygon b2MakePolygon(in B2Hull hull, float radius);

    /// <summary>
    /// 创建默认 shape 定义。
    /// </summary>
    /// <returns>默认 shape 定义。</returns>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial B2ShapeDef b2DefaultShapeDef();

    /// <summary>
    /// 创建 polygon shape。
    /// </summary>
    /// <param name="bodyId">body 句柄。</param>
    /// <param name="def">shape 定义。</param>
    /// <param name="polygon">polygon 几何。</param>
    /// <returns>shape 句柄。</returns>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial B2ShapeId b2CreatePolygonShape(B2BodyId bodyId, in B2ShapeDef def, in B2Polygon polygon);

    /// <summary>
    /// 创建 segment shape。
    /// </summary>
    /// <param name="bodyId">body 句柄。</param>
    /// <param name="def">shape 定义。</param>
    /// <param name="segment">segment 几何。</param>
    /// <returns>shape 句柄。</returns>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial B2ShapeId b2CreateSegmentShape(B2BodyId bodyId, in B2ShapeDef def, in B2Segment segment);

    /// <summary>
    /// 创建默认 chain 定义。
    /// </summary>
    /// <returns>默认 chain 定义。</returns>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial B2ChainDef b2DefaultChainDef();

    /// <summary>
    /// 创建 chain shape。
    /// </summary>
    /// <param name="bodyId">body 句柄。</param>
    /// <param name="def">chain 定义。</param>
    /// <returns>chain 句柄。</returns>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial B2ChainId b2CreateChain(B2BodyId bodyId, in B2ChainDef def);

    /// <summary>
    /// 销毁 chain shape。
    /// </summary>
    /// <param name="chainId">chain 句柄。</param>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial void b2DestroyChain(B2ChainId chainId);

    /// <summary>
    /// 销毁 shape。
    /// </summary>
    /// <param name="shapeId">shape 句柄。</param>
    /// <param name="updateBodyMass">是否更新 body mass，按 C bool 传 0/1。</param>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial void b2DestroyShape(B2ShapeId shapeId, byte updateBodyMass);

    /// <summary>
    /// 读取 body 位置。
    /// </summary>
    /// <param name="bodyId">body 句柄。</param>
    /// <returns>body 位置。</returns>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial B2Vec2 b2Body_GetPosition(B2BodyId bodyId);

    /// <summary>
    /// 读取 body 旋转。
    /// </summary>
    /// <param name="bodyId">body 句柄。</param>
    /// <returns>body 旋转。</returns>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial B2Rot b2Body_GetRotation(B2BodyId bodyId);

    /// <summary>
    /// 读取 body 变换。
    /// </summary>
    /// <param name="bodyId">body 句柄。</param>
    /// <returns>body 变换。</returns>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial B2Transform b2Body_GetTransform(B2BodyId bodyId);

    /// <summary>
    /// 设置 body 变换。
    /// </summary>
    /// <param name="bodyId">body 句柄。</param>
    /// <param name="position">位置。</param>
    /// <param name="rotation">旋转。</param>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial void b2Body_SetTransform(B2BodyId bodyId, B2Vec2 position, B2Rot rotation);

    /// <summary>
    /// 读取 body 线速度。
    /// </summary>
    /// <param name="bodyId">body 句柄。</param>
    /// <returns>线速度。</returns>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial B2Vec2 b2Body_GetLinearVelocity(B2BodyId bodyId);

    /// <summary>
    /// 设置 body 线速度。
    /// </summary>
    /// <param name="bodyId">body 句柄。</param>
    /// <param name="linearVelocity">线速度。</param>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial void b2Body_SetLinearVelocity(B2BodyId bodyId, B2Vec2 linearVelocity);

    /// <summary>
    /// 读取 body 角速度。
    /// </summary>
    /// <param name="bodyId">body 句柄。</param>
    /// <returns>角速度。</returns>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial float b2Body_GetAngularVelocity(B2BodyId bodyId);

    /// <summary>
    /// 设置 body 角速度。
    /// </summary>
    /// <param name="bodyId">body 句柄。</param>
    /// <param name="angularVelocity">角速度。</param>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial void b2Body_SetAngularVelocity(B2BodyId bodyId, float angularVelocity);

    /// <summary>
    /// 对 body 施加线性冲量。
    /// </summary>
    /// <param name="bodyId">body 句柄。</param>
    /// <param name="impulse">冲量。</param>
    /// <param name="point">作用点。</param>
    /// <param name="wake">是否唤醒，按 C bool 传 0/1。</param>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial void b2Body_ApplyLinearImpulse(B2BodyId bodyId, B2Vec2 impulse, B2Vec2 point, byte wake);

    /// <summary>
    /// 对 body 施加力。
    /// </summary>
    /// <param name="bodyId">body 句柄。</param>
    /// <param name="force">力。</param>
    /// <param name="point">作用点。</param>
    /// <param name="wake">是否唤醒，按 C bool 传 0/1。</param>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial void b2Body_ApplyForce(B2BodyId bodyId, B2Vec2 force, B2Vec2 point, byte wake);

    /// <summary>
    /// 读取 body 质量。
    /// </summary>
    /// <param name="bodyId">body 句柄。</param>
    /// <returns>质量。</returns>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial float b2Body_GetMass(B2BodyId bodyId);

    /// <summary>
    /// 按 shape 重新计算 body 质量。
    /// </summary>
    /// <param name="bodyId">body 句柄。</param>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial void b2Body_ApplyMassFromShapes(B2BodyId bodyId);

    /// <summary>
    /// 设置 body awake 状态。
    /// </summary>
    /// <param name="bodyId">body 句柄。</param>
    /// <param name="awake">awake 状态，按 C bool 传 0/1。</param>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial void b2Body_SetAwake(B2BodyId bodyId, byte awake);

    /// <summary>
    /// 判断 body 是否 awake。
    /// </summary>
    /// <param name="bodyId">body 句柄。</param>
    /// <returns>0 表示 false，非 0 表示 true。</returns>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial byte b2Body_IsAwake(B2BodyId bodyId);

    /// <summary>
    /// 启用 body。
    /// </summary>
    /// <param name="bodyId">body 句柄。</param>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial void b2Body_Enable(B2BodyId bodyId);

    /// <summary>
    /// 禁用 body。
    /// </summary>
    /// <param name="bodyId">body 句柄。</param>
    [LibraryImport(Box2DLibrary.Name)]
    public static partial void b2Body_Disable(B2BodyId bodyId);
}
