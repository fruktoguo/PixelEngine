namespace PixelEngine.Scripting;

/// <summary>
/// 脚本可见的运行时材质句柄；内部值只在当前运行期间稳定。
/// </summary>
/// <param name="Value">运行时材质 id。</param>
public readonly record struct MaterialId(ushort Value)
{
    /// <summary>
    /// 无效材质句柄。
    /// </summary>
    public static MaterialId Invalid { get; } = new(ushort.MaxValue);

    /// <summary>
    /// 获取该句柄是否有效。
    /// </summary>
    public bool IsValid => Value != ushort.MaxValue;
}

/// <summary>
/// 脚本读取 cell 时得到的只读快照。
/// </summary>
/// <param name="Material">材质句柄。</param>
/// <param name="Flags">cell 标志位快照。</param>
/// <param name="Lifetime">cell lifetime 快照。</param>
public readonly record struct CellView(MaterialId Material, byte Flags, byte Lifetime);

/// <summary>
/// 脚本可见的材质属性摘要。
/// </summary>
/// <param name="Id">材质句柄。</param>
/// <param name="Name">稳定材质名。</param>
/// <param name="Density">材质密度。</param>
/// <param name="IsSolid">是否按固体处理。</param>
public readonly record struct MaterialInfo(MaterialId Id, string Name, byte Density, bool IsSolid);

/// <summary>
/// 像素 raycast 的命中结果。
/// </summary>
/// <param name="Hit">是否命中。</param>
/// <param name="X">命中世界 X 坐标。</param>
/// <param name="Y">命中世界 Y 坐标。</param>
/// <param name="Distance">命中距离。</param>
/// <param name="Material">命中材质。</param>
public readonly record struct RaycastHit(bool Hit, float X, float Y, float Distance, MaterialId Material);

/// <summary>
/// 刚体句柄；具体有效性由 Physics 后端维护。
/// </summary>
/// <param name="Value">运行时刚体 id。</param>
public readonly record struct BodyHandle(int Value);

/// <summary>
/// 刚体变换快照。
/// </summary>
/// <param name="X">世界 X 坐标。</param>
/// <param name="Y">世界 Y 坐标。</param>
/// <param name="RotationRadians">旋转角，单位弧度。</param>
public readonly record struct BodyTransform(float X, float Y, float RotationRadians);

/// <summary>
/// 角色控制器句柄；具体有效性由 Physics 后端维护。
/// </summary>
/// <param name="Value">运行时角色 id。</param>
public readonly record struct CharacterHandle(int Value);

/// <summary>
/// 角色控制器移动 / 状态快照；坐标为 AABB 左上角，单位为世界像素。
/// </summary>
/// <param name="X">AABB 左上角 X 坐标。</param>
/// <param name="Y">AABB 左上角 Y 坐标。</param>
/// <param name="Width">AABB 宽度。</param>
/// <param name="Height">AABB 高度。</param>
/// <param name="OnGround">是否接触地面。</param>
/// <param name="OnWallLeft">是否接触左侧墙面。</param>
/// <param name="OnWallRight">是否接触右侧墙面。</param>
/// <param name="OnCeiling">本次移动是否撞到上方固体。</param>
/// <param name="VelocityX">X 方向估算速度，单位像素/秒。</param>
/// <param name="VelocityY">Y 方向估算速度，单位像素/秒。</param>
/// <param name="RequestedDeltaX">本次请求位移 X 分量。</param>
/// <param name="RequestedDeltaY">本次请求位移 Y 分量。</param>
/// <param name="AppliedDeltaX">本次实际位移 X 分量。</param>
/// <param name="AppliedDeltaY">本次实际位移 Y 分量。</param>
/// <param name="GroundNormalX">地面估算法线 X 分量；未接地时为 0。</param>
/// <param name="GroundNormalY">地面估算法线 Y 分量；未接地时为 0。</param>
/// <param name="GroundSlopeRadians">地面估算坡度，单位弧度。</param>
public readonly record struct CharacterState(
    float X,
    float Y,
    float Width,
    float Height,
    bool OnGround,
    bool OnWallLeft,
    bool OnWallRight,
    bool OnCeiling,
    float VelocityX,
    float VelocityY,
    float RequestedDeltaX,
    float RequestedDeltaY,
    float AppliedDeltaX,
    float AppliedDeltaY,
    float GroundNormalX,
    float GroundNormalY,
    float GroundSlopeRadians)
{
    /// <summary>
    /// 是否接触任一侧墙面。
    /// </summary>
    public bool OnWall => OnWallLeft || OnWallRight;
}

/// <summary>
/// 脚本请求生成自由粒子的描述。
/// </summary>
/// <param name="X">起始 X 坐标。</param>
/// <param name="Y">起始 Y 坐标。</param>
/// <param name="VelocityX">初始 X 速度。</param>
/// <param name="VelocityY">初始 Y 速度。</param>
/// <param name="Material">粒子材质。</param>
/// <param name="Lifetime">粒子 lifetime。</param>
public readonly record struct ParticleSpawnDesc(float X, float Y, float VelocityX, float VelocityY, MaterialId Material, ushort Lifetime);

/// <summary>
/// 矩形区域，单位由调用接口约定。
/// </summary>
/// <param name="X">左上角 X。</param>
/// <param name="Y">左上角 Y。</param>
/// <param name="Width">宽度。</param>
/// <param name="Height">高度。</param>
public readonly record struct RectF(float X, float Y, float Width, float Height);

/// <summary>
/// 脚本输入键枚举；具体映射由输入后端维护。
/// </summary>
public enum Key
{
    /// <summary>
    /// 未指定按键。
    /// </summary>
    Unknown,
}

/// <summary>
/// 脚本输入轴枚举；具体映射由输入后端维护。
/// </summary>
public enum Axis
{
    /// <summary>
    /// 水平轴。
    /// </summary>
    Horizontal,

    /// <summary>
    /// 垂直轴。
    /// </summary>
    Vertical,
}
