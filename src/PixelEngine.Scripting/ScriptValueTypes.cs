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
/// <param name="DisplayName">面向玩家显示的材质名。</param>
/// <param name="LegendCategory">材质图例分组。</param>
/// <param name="LegendVisible">是否应在玩家图例中显示。</param>
/// <param name="BaseColorBgra">材质代表色，BGRA8。</param>
/// <param name="MineYield">采矿 / 目标收集时每个 cell 贡献的收益。</param>
public readonly record struct MaterialInfo(
    MaterialId Id,
    string Name,
    byte Density,
    bool IsSolid,
    string DisplayName = "",
    string LegendCategory = "",
    bool LegendVisible = true,
    uint BaseColorBgra = 0,
    byte MineYield = 0);

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
/// <param name="VelocityX">脚本层初始 X 速度，单位 cell/秒。</param>
/// <param name="VelocityY">脚本层初始 Y 速度，单位 cell/秒。</param>
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
/// 二维点，单位由调用接口约定。
/// </summary>
/// <param name="X">X 坐标。</param>
/// <param name="Y">Y 坐标。</param>
public readonly record struct Point2F(float X, float Y);

/// <summary>
/// 脚本相机快照；Hosting 可把它适配成 Rendering 的 CameraState。
/// </summary>
/// <param name="OriginWorldX">视口左上角对应世界 X 坐标。</param>
/// <param name="OriginWorldY">视口左上角对应世界 Y 坐标。</param>
/// <param name="CellsPerPixel">每个屏幕像素覆盖的世界 cell 数。</param>
/// <param name="ViewportWidth">视口宽度，单位像素。</param>
/// <param name="ViewportHeight">视口高度，单位像素。</param>
public readonly record struct CameraSnapshot(
    float OriginWorldX,
    float OriginWorldY,
    float CellsPerPixel,
    int ViewportWidth,
    int ViewportHeight);

/// <summary>
/// 脚本请求的点光源快照。
/// </summary>
/// <param name="X">光源世界 X 坐标。</param>
/// <param name="Y">光源世界 Y 坐标。</param>
/// <param name="Radius">光照半径，单位 cell。</param>
/// <param name="ColorBgra">BGRA 颜色。</param>
/// <param name="Intensity">光照强度。</param>
public readonly record struct ScriptPointLight(float X, float Y, float Radius, uint ColorBgra, float Intensity);

/// <summary>
/// 脚本 overlay 绘制原语类型。
/// </summary>
public enum ScriptOverlayPrimitive
{
    /// <summary>
    /// 实色矩形。
    /// </summary>
    SolidRectangle,

    /// <summary>
    /// 矩形描边。
    /// </summary>
    OutlineRectangle,

    /// <summary>
    /// 带厚度线段。
    /// </summary>
    Line,
}

/// <summary>
/// 脚本提交的屏幕空间 overlay 命令。
/// </summary>
/// <param name="Primitive">绘制原语。</param>
/// <param name="X">起点或矩形左上角 X，单位屏幕像素。</param>
/// <param name="Y">起点或矩形左上角 Y，单位屏幕像素。</param>
/// <param name="Width">矩形宽度，单位屏幕像素。</param>
/// <param name="Height">矩形高度，单位屏幕像素。</param>
/// <param name="ColorBgra">BGRA8 非预乘颜色。</param>
/// <param name="Thickness">描边或线段厚度，单位屏幕像素。</param>
/// <param name="EndX">线段终点 X，单位屏幕像素。</param>
/// <param name="EndY">线段终点 Y，单位屏幕像素。</param>
public readonly record struct ScriptOverlayCommand(
    ScriptOverlayPrimitive Primitive,
    float X,
    float Y,
    float Width,
    float Height,
    uint ColorBgra,
    float Thickness,
    float EndX,
    float EndY)
{
    /// <summary>
    /// 创建实色矩形命令。
    /// </summary>
    /// <param name="x">矩形左上角 X。</param>
    /// <param name="y">矩形左上角 Y。</param>
    /// <param name="width">矩形宽度。</param>
    /// <param name="height">矩形高度。</param>
    /// <param name="colorBgra">BGRA8 非预乘颜色。</param>
    /// <returns>实色矩形命令。</returns>
    public static ScriptOverlayCommand SolidRectangle(float x, float y, float width, float height, uint colorBgra)
    {
        return new ScriptOverlayCommand(ScriptOverlayPrimitive.SolidRectangle, x, y, width, height, colorBgra, 0f, 0f, 0f);
    }

    /// <summary>
    /// 创建矩形描边命令。
    /// </summary>
    /// <param name="x">矩形左上角 X。</param>
    /// <param name="y">矩形左上角 Y。</param>
    /// <param name="width">矩形宽度。</param>
    /// <param name="height">矩形高度。</param>
    /// <param name="thickness">描边厚度。</param>
    /// <param name="colorBgra">BGRA8 非预乘颜色。</param>
    /// <returns>矩形描边命令。</returns>
    public static ScriptOverlayCommand OutlineRectangle(float x, float y, float width, float height, float thickness, uint colorBgra)
    {
        return new ScriptOverlayCommand(ScriptOverlayPrimitive.OutlineRectangle, x, y, width, height, colorBgra, thickness, 0f, 0f);
    }

    /// <summary>
    /// 创建带厚度线段命令。
    /// </summary>
    /// <param name="startX">线段起点 X。</param>
    /// <param name="startY">线段起点 Y。</param>
    /// <param name="endX">线段终点 X。</param>
    /// <param name="endY">线段终点 Y。</param>
    /// <param name="thickness">线段厚度。</param>
    /// <param name="colorBgra">BGRA8 非预乘颜色。</param>
    /// <returns>线段命令。</returns>
    public static ScriptOverlayCommand Line(float startX, float startY, float endX, float endY, float thickness, uint colorBgra)
    {
        return new ScriptOverlayCommand(ScriptOverlayPrimitive.Line, startX, startY, 1f, 1f, colorBgra, thickness, endX, endY);
    }

    /// <summary>
    /// 校验 overlay 命令参数。
    /// </summary>
    public void Validate()
    {
        if (!float.IsFinite(X) || !float.IsFinite(Y))
        {
            throw new ArgumentOutOfRangeException(nameof(X), "Overlay 坐标必须为有限数值。");
        }

        if (!float.IsFinite(Width) || !float.IsFinite(Height) || Width <= 0f || Height <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(Width), "Overlay 尺寸必须为正有限数值。");
        }

        if (Primitive == ScriptOverlayPrimitive.OutlineRectangle &&
            (!float.IsFinite(Thickness) || Thickness <= 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(Thickness), "Overlay 描边厚度必须为正有限数值。");
        }

        if (Primitive == ScriptOverlayPrimitive.Line)
        {
            if (!float.IsFinite(EndX) || !float.IsFinite(EndY))
            {
                throw new ArgumentOutOfRangeException(nameof(EndX), "Overlay 线段终点必须为有限数值。");
            }

            if (!float.IsFinite(Thickness) || Thickness <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(Thickness), "Overlay 线段厚度必须为正有限数值。");
            }

            if (X == EndX && Y == EndY)
            {
                throw new ArgumentOutOfRangeException(nameof(EndX), "Overlay 线段长度必须大于 0。");
            }
        }
    }
}

/// <summary>
/// fog-of-war 揭示请求。
/// </summary>
/// <param name="X">揭示中心世界 X 坐标。</param>
/// <param name="Y">揭示中心世界 Y 坐标。</param>
/// <param name="Radius">揭示半径，单位 cell。</param>
/// <param name="Alpha">揭示强度。</param>
public readonly record struct FogRevealRequest(float X, float Y, float Radius, byte Alpha);

/// <summary>
/// 脚本输入键枚举；具体映射由输入后端维护。
/// </summary>
public enum Key
{
    /// <summary>
    /// 未指定按键。
    /// </summary>
    Unknown,

    /// <summary>
    /// A 键。
    /// </summary>
    A,

    /// <summary>
    /// D 键。
    /// </summary>
    D,

    /// <summary>
    /// W 键。
    /// </summary>
    W,

    /// <summary>
    /// S 键。
    /// </summary>
    S,

    /// <summary>
    /// R 键。
    /// </summary>
    R,

    /// <summary>
    /// 左方向键。
    /// </summary>
    Left,

    /// <summary>
    /// 右方向键。
    /// </summary>
    Right,

    /// <summary>
    /// 上方向键。
    /// </summary>
    Up,

    /// <summary>
    /// 下方向键。
    /// </summary>
    Down,

    /// <summary>
    /// 空格键。
    /// </summary>
    Space,

    /// <summary>
    /// Escape 键。
    /// </summary>
    Escape,

    /// <summary>
    /// 数字键 0。
    /// </summary>
    Digit0,

    /// <summary>
    /// 数字键 1。
    /// </summary>
    Digit1,

    /// <summary>
    /// 数字键 2。
    /// </summary>
    Digit2,

    /// <summary>
    /// 数字键 3。
    /// </summary>
    Digit3,

    /// <summary>
    /// 数字键 4。
    /// </summary>
    Digit4,

    /// <summary>
    /// 数字键 5。
    /// </summary>
    Digit5,

    /// <summary>
    /// 数字键 6。
    /// </summary>
    Digit6,

    /// <summary>
    /// 数字键 7。
    /// </summary>
    Digit7,

    /// <summary>
    /// 数字键 8。
    /// </summary>
    Digit8,

    /// <summary>
    /// 数字键 9。
    /// </summary>
    Digit9,
}

/// <summary>
/// 脚本鼠标按键枚举；具体映射由输入后端维护。
/// </summary>
public enum MouseButton
{
    /// <summary>
    /// 左键。
    /// </summary>
    Left,

    /// <summary>
    /// 右键。
    /// </summary>
    Right,

    /// <summary>
    /// 中键。
    /// </summary>
    Middle,
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
