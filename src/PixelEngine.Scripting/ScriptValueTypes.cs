using PixelEngine.Simulation;

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
/// 脚本可见的工程资产稳定引用；由 stable asset id、logical path 与资产类型组成。
/// </summary>
/// <param name="AssetType">资产类别。</param>
/// <param name="AssetId">工程级 stable asset id。</param>
/// <param name="LogicalPath">相对 content 根目录的 logical path。</param>
public readonly record struct ScriptAssetReference(ScriptAssetKind AssetType, string AssetId, string LogicalPath)
{
    private const string Prefix = "assetref";

    /// <summary>
    /// 空资产引用。
    /// </summary>
    public static ScriptAssetReference Empty { get; } = new(ScriptAssetKind.Texture, string.Empty, string.Empty);

    /// <summary>
    /// 获取该引用是否同时具备 stable asset id 与 logical path。
    /// </summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(AssetId) && !string.IsNullOrWhiteSpace(LogicalPath);

    /// <summary>
    /// 编码为场景 / Prefab 文档可持久化的 stable asset reference 字符串。
    /// </summary>
    /// <param name="assetId">工程级 stable asset id。</param>
    /// <param name="logicalPath">相对 content 根目录的 logical path。</param>
    /// <param name="assetType">资产类别。</param>
    /// <returns>可写入 authoring SerializedFields 的编码字符串。</returns>
    public static string Encode(string assetId, string logicalPath, ScriptAssetKind assetType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalPath);
        return string.Join('|', Prefix, assetType.ToString(), assetId.Trim(), logicalPath.Trim().Replace('\\', '/'));
    }

    /// <summary>
    /// 尝试从 authoring SerializedFields 字符串解码 stable asset reference。
    /// </summary>
    /// <param name="value">编码字符串。</param>
    /// <param name="reference">解码后的引用。</param>
    /// <returns>格式正确且资产类别可识别时返回 true。</returns>
    public static bool TryDecode(string? value, out ScriptAssetReference reference)
    {
        reference = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string[] parts = value.Split('|', 4);
        if (parts.Length != 4 ||
            !string.Equals(parts[0], Prefix, StringComparison.Ordinal) ||
            !Enum.TryParse(parts[1], ignoreCase: false, out ScriptAssetKind assetType) ||
            string.IsNullOrWhiteSpace(parts[2]) ||
            string.IsNullOrWhiteSpace(parts[3]))
        {
            return false;
        }

        reference = new ScriptAssetReference(assetType, parts[2], parts[3].Replace('\\', '/'));
        return true;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return IsValid ? Encode(AssetId, LogicalPath, AssetType) : string.Empty;
    }
}

/// <summary>
/// 脚本读取 cell 时得到的只读快照。
/// </summary>
/// <param name="Material">材质句柄。</param>
/// <param name="Flags">cell 标志位快照。</param>
/// <param name="Lifetime">cell lifetime 快照。</param>
/// <param name="Integrity">按材质最大完整度与当前 Damage 平面计算的剩余结构完整度，超过 byte 上限时饱和到 255。</param>
public readonly record struct CellView(MaterialId Material, byte Flags, byte Lifetime, byte Integrity = 0);

/// <summary>
/// 脚本安全相位提交的世界区域修改类别；可组合，用于让玩法层有界重建派生拓扑或导航状态。
/// </summary>
[Flags]
public enum WorldMutationKind : byte
{
    /// <summary>没有修改。</summary>
    None = 0,

    /// <summary>直接 cell 写入、清除或画刷修改。</summary>
    CellWrite = 1 << 0,

    /// <summary>直接清除 cell 或用 Empty 画刷移除区域。</summary>
    CellRemoval = 1 << 1,

    /// <summary>结构伤害、光束或爆炸修改。</summary>
    Damage = 1 << 2,

    /// <summary>可能引发相变或结构失效的温度修改。</summary>
    Heat = 1 << 3,

    /// <summary>权威 Simulation 已确认静态 Solid 被移除。</summary>
    SolidTopologyRemoved = 1 << 4,

    /// <summary>权威 Simulation 已确认静态 Solid 被添加。</summary>
    SolidTopologyAdded = 1 << 5,

    /// <summary>任意方向的静态 Solid 占用变化。</summary>
    SolidTopology = SolidTopologyRemoved | SolidTopologyAdded,
}

/// <summary>
/// 一个脚本帧内合并后的世界修改区域；边界采用左闭右开世界 cell 坐标。
/// </summary>
/// <param name="MinX">最小 X，包含。</param>
/// <param name="MinY">最小 Y，包含。</param>
/// <param name="MaxXExclusive">最大 X，不包含。</param>
/// <param name="MaxYExclusive">最大 Y，不包含。</param>
/// <param name="Kinds">该区域内合并后的修改类别。</param>
public readonly record struct WorldMutationEvent(
    int MinX,
    int MinY,
    int MaxXExclusive,
    int MaxYExclusive,
    WorldMutationKind Kinds)
{
    /// <summary>区域宽度。</summary>
    public int Width => Math.Max(0, MaxXExclusive - MinX);

    /// <summary>区域高度。</summary>
    public int Height => Math.Max(0, MaxYExclusive - MinY);
}

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
/// <param name="CellType">CA movement 消费的基础 cell 类型。</param>
/// <param name="Category">编辑器、HUD 与图例使用的材质分类。</param>
/// <param name="Emissive">该材质是否会进入发光 / bloom 路径。</param>
/// <param name="Hardness">结构破坏吸收强度。</param>
/// <param name="MaxIntegrity">结构破坏最大完整度阈值；0 表示有效伤害命中后即时破坏。</param>
/// <param name="IsDestructible">该材质是否会被结构破坏 API 处理。</param>
/// <param name="FlowRate">液体或气体每步横向扩散距离。</param>
/// <param name="BlocksCharacter">该材质是否应阻挡 kinematic character。</param>
/// <param name="Flammability">接触点燃概率权重，范围 0-255。</param>
/// <param name="AutoIgnitionTemp">自燃温度阈值，单位摄氏度；0 表示不开启自燃。</param>
/// <param name="FireHp">燃烧耐久；-1 表示永燃。</param>
/// <param name="TemperatureOfFire">燃烧或高温材质每 tick 注入温度场的热量基准。</param>
/// <param name="GeneratesSmoke">燃烧或反应产烟倾向，0 表示不产烟。</param>
/// <param name="HeatConduct">每帧热传导概率权重，范围 0-255。</param>
/// <param name="HeatCapacity">热容量，用于温度场能量换算。</param>
/// <param name="RenderStyle">渲染相位使用的材质着色风格。</param>
/// <param name="Properties">材质标签与运行时行为位。</param>
public readonly record struct MaterialInfo(
    MaterialId Id,
    string Name,
    byte Density,
    bool IsSolid,
    string DisplayName = "",
    string LegendCategory = "",
    bool LegendVisible = true,
    uint BaseColorBgra = 0,
    byte MineYield = 0,
    CellType CellType = CellType.Empty,
    MaterialLegendCategory Category = MaterialLegendCategory.Terrain,
    bool Emissive = false,
    byte Hardness = 0,
    ushort MaxIntegrity = 0,
    bool IsDestructible = false,
    byte FlowRate = 0,
    bool BlocksCharacter = false,
    byte Flammability = 0,
    ushort AutoIgnitionTemp = 0,
    int FireHp = 0,
    byte TemperatureOfFire = 0,
    byte GeneratesSmoke = 0,
    byte HeatConduct = 0,
    float HeatCapacity = 1f,
    MaterialRenderStyle RenderStyle = MaterialRenderStyle.Ground,
    MaterialProperty Properties = MaterialProperty.None);

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
/// 脚本请求按速度锥发射自由粒子的描述。
/// </summary>
/// <param name="X">发射原点 X 坐标。</param>
/// <param name="Y">发射原点 Y 坐标。</param>
/// <param name="Material">粒子材质句柄。</param>
/// <param name="Count">请求发射的粒子数量。</param>
/// <param name="DirAngleRad">中心方向角，单位弧度。</param>
/// <param name="DirSpreadRad">方向半角扩散，单位弧度。</param>
/// <param name="BaseSpeed">基础速度，单位 cell/秒。</param>
/// <param name="SpeedJitter">速度抖动半径；实际速度落在 <c>BaseSpeed±SpeedJitter</c> 后钳到非负，单位 cell/秒。</param>
/// <param name="LifeTicks">粒子 lifetime；0 表示使用粒子系统默认最大 lifetime。</param>
public readonly record struct ParticleEmit(
    float X,
    float Y,
    MaterialId Material,
    int Count,
    float DirAngleRad,
    float DirSpreadRad,
    float BaseSpeed,
    float SpeedJitter,
    ushort LifeTicks)
{
    /// <summary>
    /// 从方向向量、速度区间与锥半角创建发射描述；速度单位为 cell/秒。
    /// </summary>
    /// <param name="originX">发射原点 X 坐标。</param>
    /// <param name="originY">发射原点 Y 坐标。</param>
    /// <param name="dirX">中心方向向量 X 分量。</param>
    /// <param name="dirY">中心方向向量 Y 分量。</param>
    /// <param name="coneRadians">方向半角扩散，单位弧度。</param>
    /// <param name="minSpeed">速度区间下限，单位 cell/秒。</param>
    /// <param name="maxSpeed">速度区间上限，单位 cell/秒。</param>
    /// <param name="count">请求发射的粒子数量。</param>
    /// <param name="material">粒子材质句柄。</param>
    /// <param name="lifeTicks">粒子 lifetime；0 表示使用粒子系统默认最大 lifetime。</param>
    /// <returns>规范化后的速度锥发射描述。</returns>
    public static ParticleEmit FromVelocityCone(
        float originX,
        float originY,
        float dirX,
        float dirY,
        float coneRadians,
        float minSpeed,
        float maxSpeed,
        int count,
        MaterialId material,
        ushort lifeTicks)
    {
        ValidateFinite(originX, nameof(originX));
        ValidateFinite(originY, nameof(originY));
        ValidateFinite(dirX, nameof(dirX));
        ValidateFinite(dirY, nameof(dirY));
        ValidateFinite(coneRadians, nameof(coneRadians));
        ValidateFinite(minSpeed, nameof(minSpeed));
        ValidateFinite(maxSpeed, nameof(maxSpeed));
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        float lengthSq = (dirX * dirX) + (dirY * dirY);
        if (lengthSq <= float.Epsilon)
        {
            throw new ArgumentOutOfRangeException(nameof(dirX), "粒子发射方向向量不能为零。");
        }

        float lower = MathF.Max(0f, MathF.Min(minSpeed, maxSpeed));
        float upper = MathF.Max(0f, MathF.Max(minSpeed, maxSpeed));
        return new ParticleEmit(
            originX,
            originY,
            material,
            count,
            MathF.Atan2(dirY, dirX),
            MathF.Max(0f, coneRadians),
            (lower + upper) * 0.5f,
            (upper - lower) * 0.5f,
            lifeTicks);
    }

    private static void ValidateFinite(float value, string name)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "粒子发射参数必须为有限数值。");
        }
    }
}

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
    /// B 键。
    /// </summary>
    B,

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
