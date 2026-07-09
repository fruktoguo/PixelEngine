using System.Text.Json.Serialization.Metadata;

namespace PixelEngine.Scripting;

/// <summary>
/// 脚本访问引擎能力的统一入口；由 Hosting 在装配期注入 Behaviour。
/// </summary>
public interface IScriptContext
{
    /// <summary>
    /// 清理只允许存在于单个脚本帧内的瞬时请求。默认上下文无瞬时后端时不执行任何操作。
    /// </summary>
    void ClearFrameTransientRequests()
    {
    }

    /// <summary>
    /// cell 读写能力；写操作延迟到相位安全窗口落地。
    /// </summary>
    IWorldCellAccess Cells { get; }

    /// <summary>
    /// 世界级复合效果能力；写操作延迟到对应安全相位落地。
    /// </summary>
    IWorldEffects World { get; }

    /// <summary>
    /// 材质查询能力。
    /// </summary>
    IMaterialQuery Materials { get; }

    /// <summary>
    /// 自由粒子生成能力；写操作延迟到粒子安全相位。
    /// </summary>
    IParticleSpawner Particles { get; }

    /// <summary>
    /// 固体像素采样与 raycast 能力。
    /// </summary>
    ISolidSampler Solids { get; }

    /// <summary>
    /// 刚体创建、查询和控制能力；写操作延迟到 Physics 相位。
    /// </summary>
    IRigidBodyApi Bodies { get; }

    /// <summary>
    /// 角色控制器能力。
    /// </summary>
    ICharacterController Character { get; }

    /// <summary>
    /// PhysicsSync 后事件；用于脚本在刚体 step / inverse-sample stamp 完成后处理角色压入等后物理交互。
    /// </summary>
    IPhysicsStepEvents PhysicsEvents => NoopPhysicsStepEvents.Instance;

    /// <summary>
    /// 相机控制能力。
    /// </summary>
    ICameraApi Camera { get; }

    /// <summary>
    /// 输入读取能力。
    /// </summary>
    IInputApi Input { get; }

    /// <summary>
    /// 光照与 fog-of-war 脚本请求能力。
    /// </summary>
    ILightingApi Lighting { get; }

    /// <summary>
    /// 屏幕空间 overlay 绘制能力；只影响本帧渲染，不写入权威世界。
    /// </summary>
    IOverlayApi Overlay => throw new NotSupportedException("当前脚本上下文未注入 Overlay 后端。");

    /// <summary>
    /// 引擎只读诊断能力。
    /// </summary>
    IDiagnosticsApi Diagnostics { get; }

    /// <summary>
    /// 运行时控制能力；用于脚本暂停/继续和请求关闭宿主。
    /// </summary>
    IRuntimeControlApi Runtime => throw new NotSupportedException("当前脚本上下文未注入 Runtime 控制后端。");

    /// <summary>
    /// 脚本事件订阅能力；事件在相位 1 分发。
    /// </summary>
    IEventBus Events { get; }

    /// <summary>
    /// 音频播放能力；播放请求进入事件/音频后端。
    /// </summary>
    IAudioApi Audio { get; }

    /// <summary>
    /// 游戏大 UI 控制能力；未启用 PixelEngine.UI 时返回空服务。
    /// </summary>
    IGameUiService Ui => GameUi;

    /// <summary>
    /// 游戏大 UI 控制能力；未启用 PixelEngine.UI 时返回空服务。
    /// </summary>
    IGameUiService GameUi => NoopGameUiService.Instance;

    /// <summary>
    /// 内容配置加载能力；脚本只提供类型元数据，实际文件读取和 JSON 解析由 Hosting 门面执行。
    /// </summary>
    IConfigApi Config => throw new NotSupportedException("当前脚本上下文未注入 Config 后端。");

    /// <summary>
    /// 当前运行时间信息。
    /// </summary>
    IGameTime Time { get; }

    /// <summary>
    /// 当前脚本场景。
    /// </summary>
    Scene Scene { get; }
}

/// <summary>
/// 提供脚本可用的 cell 读取与延迟写入能力。
/// </summary>
public interface IWorldCellAccess
{
    /// <summary>
    /// 即时读取指定世界坐标的材质；脚本可在相位 1 调用，读取上帧末一致状态。
    /// </summary>
    /// <param name="x">世界 X 坐标。</param>
    /// <param name="y">世界 Y 坐标。</param>
    /// <returns>该 cell 的运行时材质句柄。</returns>
    MaterialId GetMaterial(int x, int y);

    /// <summary>
    /// 即时读取指定世界坐标的 cell 快照；脚本可在相位 1 调用，读取上帧末一致状态。
    /// </summary>
    /// <param name="x">世界 X 坐标。</param>
    /// <param name="y">世界 Y 坐标。</param>
    /// <returns>只读 cell 快照。</returns>
    CellView Sample(int x, int y);

    /// <summary>
    /// 即时判断指定世界坐标是否为固体；脚本可在相位 1 调用，读取上帧末一致状态。
    /// </summary>
    /// <param name="x">世界 X 坐标。</param>
    /// <param name="y">世界 Y 坐标。</param>
    /// <returns>若该坐标按固体处理则返回 true，否则返回 false。</returns>
    bool IsSolid(int x, int y);

    /// <summary>
    /// 即时判断指定世界坐标是否由动态刚体 stamp 占用；用于脚本避免把刚体像素再次当作静态地形处理。
    /// </summary>
    /// <param name="x">世界 X 坐标。</param>
    /// <param name="y">世界 Y 坐标。</param>
    /// <returns>若该 cell 当前带有 rigid-owned 标记则返回 true，否则返回 false。</returns>
    bool IsRigidOwned(int x, int y);

    /// <summary>
    /// 延迟写入指定 cell，并在相位安全窗口落地时标记 dirty；脚本可在相位 1 调用。
    /// </summary>
    /// <param name="x">世界 X 坐标。</param>
    /// <param name="y">世界 Y 坐标。</param>
    /// <param name="material">要写入的材质句柄。</param>
    void SetCell(int x, int y, MaterialId material);

    /// <summary>
    /// 延迟绘制圆形区域，并在相位安全窗口落地时标记 dirty；脚本可在相位 1 调用。
    /// </summary>
    /// <param name="x">圆心世界 X 坐标。</param>
    /// <param name="y">圆心世界 Y 坐标。</param>
    /// <param name="radius">绘制半径，单位像素。</param>
    /// <param name="material">要写入的材质句柄。</param>
    void Paint(int x, int y, int radius, MaterialId material);
}

/// <summary>
/// 脚本化结构破坏类型；当前用于 API 语义与后续材质差异化扩展，CPU sim 仍以材质抗性为权威。
/// </summary>
public enum DamageKind : byte
{
    /// <summary>
    /// 冲击 / 爆破类破坏。
    /// </summary>
    Impact,

    /// <summary>
    /// 光束类持续破坏。
    /// </summary>
    Beam,

    /// <summary>
    /// 腐蚀类破坏。
    /// </summary>
    Corrosion,

    /// <summary>
    /// 热破坏。
    /// </summary>
    Heat,
}

/// <summary>
/// 提供脚本可用的世界级复合效果 API。
/// </summary>
public interface IWorldEffects
{
    /// <summary>
    /// 延迟对圆形区域施加抗性感知结构破坏；命中 RigidOwned cell 时只通知物理层重建，不累加 cell Damage。
    /// </summary>
    /// <param name="x">圆心 X 坐标。</param>
    /// <param name="y">圆心 Y 坐标。</param>
    /// <param name="radius">破坏半径，单位 cell。</param>
    /// <param name="damage">中心破坏当量。</param>
    /// <param name="falloff">是否按距离线性衰减。</param>
    /// <param name="kind">破坏类型。</param>
    void DamageCircle(float x, float y, int radius, float damage, bool falloff = true, DamageKind kind = DamageKind.Impact);

    /// <summary>
    /// 延迟沿光束路径施加抗性感知结构破坏；命中 RigidOwned cell 时只通知物理层重建，不累加 cell Damage。
    /// </summary>
    /// <param name="x">起点 X 坐标。</param>
    /// <param name="y">起点 Y 坐标。</param>
    /// <param name="dx">方向 X 分量。</param>
    /// <param name="dy">方向 Y 分量。</param>
    /// <param name="length">光束长度，单位 cell。</param>
    /// <param name="damagePerCell">每个命中 cell 的破坏当量。</param>
    /// <param name="kind">破坏类型。</param>
    void DamageBeam(float x, float y, float dx, float dy, int length, float damagePerCell, DamageKind kind = DamageKind.Beam);

    /// <summary>
    /// 延迟向圆形区域注入热量；实际写入在 cell 安全相位落地，并标记 dirty/KeepAlive。
    /// </summary>
    /// <param name="x">圆心 X 坐标。</param>
    /// <param name="y">圆心 Y 坐标。</param>
    /// <param name="radius">注热半径，单位 cell。</param>
    /// <param name="deltaCelsius">温度增量，单位摄氏度。</param>
    void AddHeat(float x, float y, int radius, float deltaCelsius);

    /// <summary>
    /// 延迟触发一次爆炸：先施加抗性感知结构破坏，再把可抛射碎屑 / 粉液气火转为自由粒子，并对邻近刚体施加径向冲量。
    /// </summary>
    /// <param name="x">爆炸中心 X 坐标。</param>
    /// <param name="y">爆炸中心 Y 坐标。</param>
    /// <param name="radius">爆炸半径，单位 cell。</param>
    /// <param name="force">径向冲量强度，单位为像素质量单位每秒。</param>
    void Explode(float x, float y, int radius, float force);
}

/// <summary>
/// 提供基于稳定材质名的查询能力。
/// </summary>
public interface IMaterialQuery
{
    /// <summary>
    /// 按稳定材质名解析材质句柄；脚本可在相位 1 调用，失败时返回 <see cref="MaterialId.Invalid" />。
    /// </summary>
    /// <param name="name">稳定材质名。</param>
    /// <returns>解析得到的运行时材质句柄。</returns>
    MaterialId Resolve(string name);

    /// <summary>
    /// 尝试按稳定材质名解析材质句柄；脚本可在相位 1 调用。
    /// </summary>
    /// <param name="name">稳定材质名。</param>
    /// <param name="id">解析成功时返回运行时材质句柄。</param>
    /// <returns>若材质名存在则返回 true，否则返回 false。</returns>
    bool TryResolve(string name, out MaterialId id);

    /// <summary>
    /// 读取材质属性摘要；脚本可在相位 1 调用。
    /// </summary>
    /// <param name="id">运行时材质句柄。</param>
    /// <returns>材质属性只读摘要。</returns>
    MaterialInfo GetInfo(MaterialId id);
}

/// <summary>
/// 提供自由粒子生成能力。
/// </summary>
public interface IParticleSpawner
{
    /// <summary>
    /// 延迟生成一个自由粒子；脚本层速度以 cell/秒 表示，实际生成时会按固定 tick 步长换算为每 tick 位移。
    /// </summary>
    /// <param name="desc">粒子生成描述。</param>
    void Spawn(in ParticleSpawnDesc desc);

    /// <summary>
    /// 延迟生成一组爆发式自由粒子；脚本层速度以 cell/秒 表示，实际生成时会按固定 tick 步长换算为每 tick 位移。
    /// </summary>
    /// <param name="x">爆发中心 X 坐标。</param>
    /// <param name="y">爆发中心 Y 坐标。</param>
    /// <param name="material">粒子材质句柄。</param>
    /// <param name="count">要生成的粒子数量。</param>
    /// <param name="speed">初始速度标量，单位 cell/秒。</param>
    void Burst(float x, float y, MaterialId material, int count, float speed);

    /// <summary>
    /// 延迟按方向锥发射一组自由粒子；脚本层速度以 cell/秒 表示，实际生成时会按固定 tick 步长换算为每 tick 位移。
    /// </summary>
    /// <param name="emit">粒子速度锥发射描述。</param>
    void Emit(in ParticleEmit emit);
}

/// <summary>
/// 提供固体像素采样与 raycast 能力。
/// </summary>
public interface ISolidSampler
{
    /// <summary>
    /// 从指定位置沿方向执行 raycast；脚本可在相位 1 调用，读取上帧末一致状态。
    /// </summary>
    /// <param name="x">起点 X 坐标。</param>
    /// <param name="y">起点 Y 坐标。</param>
    /// <param name="dx">方向 X 分量。</param>
    /// <param name="dy">方向 Y 分量。</param>
    /// <param name="maxDist">最大检测距离。</param>
    /// <param name="hit">命中时返回命中信息。</param>
    /// <returns>若检测到固体像素则返回 true，否则返回 false。</returns>
    bool Raycast(float x, float y, float dx, float dy, float maxDist, out RaycastHit hit);

    /// <summary>
    /// 判断指定 AABB 内是否包含固体像素；脚本可在相位 1 调用，读取上帧末一致状态。
    /// </summary>
    /// <param name="x">AABB 左上角 X 坐标。</param>
    /// <param name="y">AABB 左上角 Y 坐标。</param>
    /// <param name="width">AABB 宽度。</param>
    /// <param name="height">AABB 高度。</param>
    /// <returns>若区域内存在固体像素则返回 true，否则返回 false。</returns>
    bool SampleSolidAabb(float x, float y, float width, float height);
}

/// <summary>
/// 提供脚本可用的刚体 API。
/// </summary>
public interface IRigidBodyApi
{
    /// <summary>
    /// 延迟从像素区域创建刚体；脚本可在相位 1 调用，实际创建在 Physics 安全相位落地。
    /// </summary>
    /// <param name="x">区域左上角 X 坐标。</param>
    /// <param name="y">区域左上角 Y 坐标。</param>
    /// <param name="width">区域宽度。</param>
    /// <param name="height">区域高度。</param>
    /// <returns>新刚体的运行时句柄；若后端采用延迟分配，可返回待解析句柄。</returns>
    BodyHandle CreateFromRegion(int x, int y, int width, int height);

    /// <summary>
    /// 尝试读取刚体上一帧末的变换；脚本可在相位 1 调用。
    /// </summary>
    /// <param name="handle">刚体句柄。</param>
    /// <param name="transform">读取成功时返回刚体变换。</param>
    /// <returns>若句柄有效且存在变换则返回 true，否则返回 false。</returns>
    bool TryGetTransform(BodyHandle handle, out BodyTransform transform);

    /// <summary>
    /// 延迟对刚体施加冲量；脚本可在相位 1 调用，实际施加在 Physics step 前落地。
    /// </summary>
    /// <param name="handle">刚体句柄。</param>
    /// <param name="impulseX">X 方向冲量。</param>
    /// <param name="impulseY">Y 方向冲量。</param>
    void ApplyImpulse(BodyHandle handle, float impulseX, float impulseY);

    /// <summary>
    /// 延迟销毁刚体；脚本可在相位 1 调用，实际销毁在 Physics 安全相位落地。
    /// </summary>
    /// <param name="handle">刚体句柄。</param>
    void Destroy(BodyHandle handle);
}

/// <summary>
/// 提供脚本可订阅的物理相位事件。
/// </summary>
public interface IPhysicsStepEvents
{
    /// <summary>
    /// 最近一次 PhysicsSync 中角色 proxy 或兜底 overlap 清理记录到的刚体撞击次数。
    /// </summary>
    int LastCharacterImpactCount { get; }

    /// <summary>
    /// 订阅 PhysicsSync 完成后的回调。回调运行在相位 8 末尾，能读取本 tick 刚体 stamp 后的世界状态。
    /// </summary>
    /// <param name="callback">要执行的回调。</param>
    /// <returns>用于取消订阅的句柄。</returns>
    IDisposable SubscribePostStep(Action callback);
}

/// <summary>
/// 空物理事件后端，供未接入 Physics 的脚本上下文安全降级。
/// </summary>
public sealed class NoopPhysicsStepEvents : IPhysicsStepEvents
{
    /// <summary>
    /// 单例实例。
    /// </summary>
    public static NoopPhysicsStepEvents Instance { get; } = new();

    private NoopPhysicsStepEvents()
    {
    }

    /// <inheritdoc />
    public int LastCharacterImpactCount => 0;

    /// <inheritdoc />
    public IDisposable SubscribePostStep(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return NoopSubscription.Instance;
    }

    private sealed class NoopSubscription : IDisposable
    {
        public static NoopSubscription Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// Hosting 物理相位使用的脚本后物理事件总线。
/// </summary>
public sealed class PhysicsStepEventBus : IPhysicsStepEvents
{
    private readonly List<Action> _postStepCallbacks = [];

    /// <inheritdoc />
    public int LastCharacterImpactCount { get; private set; }

    /// <inheritdoc />
    public IDisposable SubscribePostStep(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _postStepCallbacks.Add(callback);
        return new Subscription(this, callback);
    }

    /// <summary>
    /// 由 Hosting 在 PhysicsSync 完成后发布一次。
    /// </summary>
    public void PublishPostStep(int characterImpactCount = 0)
    {
        LastCharacterImpactCount = Math.Max(0, characterImpactCount);
        for (int i = 0; i < _postStepCallbacks.Count; i++)
        {
            _postStepCallbacks[i]();
        }
    }

    private void Unsubscribe(Action callback)
    {
        _ = _postStepCallbacks.Remove(callback);
    }

    private sealed class Subscription(PhysicsStepEventBus owner, Action callback) : IDisposable
    {
        private PhysicsStepEventBus? _owner = owner;
        private Action? _callback = callback;

        public void Dispose()
        {
            PhysicsStepEventBus? owner = _owner;
            Action? callback = _callback;
            if (owner is null || callback is null)
            {
                return;
            }

            _owner = null;
            _callback = null;
            owner.Unsubscribe(callback);
        }
    }
}

/// <summary>
/// 提供 kinematic 角色控制器 API。
/// </summary>
public interface ICharacterController
{
    /// <summary>
    /// 创建一个角色控制器；脚本可在相位 1 调用，后端可在 Physics 安全相位落地。
    /// </summary>
    /// <param name="x">初始 X 坐标。</param>
    /// <param name="y">初始 Y 坐标。</param>
    /// <param name="width">控制器宽度。</param>
    /// <param name="height">控制器高度。</param>
    /// <returns>角色控制器句柄。</returns>
    CharacterHandle Create(float x, float y, float width, float height);

    /// <summary>
    /// 立即设置角色控制器 AABB 左上角位置，并清空本次移动位移；用于出生点放置、重生或传送。
    /// </summary>
    /// <param name="handle">角色控制器句柄。</param>
    /// <param name="x">目标 X 坐标。</param>
    /// <param name="y">目标 Y 坐标。</param>
    /// <returns>设置后的角色状态。</returns>
    CharacterState SetPosition(CharacterHandle handle, float x, float y);

    /// <summary>
    /// 延迟移动角色控制器；脚本可在相位 1 调用，实际碰撞解算在 Physics 安全相位落地。
    /// </summary>
    /// <param name="handle">角色控制器句柄。</param>
    /// <param name="dx">X 方向位移。</param>
    /// <param name="dy">Y 方向位移。</param>
    /// <returns>当前已知角色状态；本次移动结果在 Physics 相位后可通过 <see cref="GetState" /> 读取。</returns>
    CharacterState Move(CharacterHandle handle, float dx, float dy);

    /// <summary>
    /// 立即移动角色控制器并完成像素碰撞解算；用于玩家控制这类需要当前帧手感反馈的脚本。
    /// </summary>
    /// <param name="handle">角色控制器句柄。</param>
    /// <param name="dx">X 方向位移。</param>
    /// <param name="dy">Y 方向位移。</param>
    /// <returns>移动后的角色状态。</returns>
    CharacterState MoveNow(CharacterHandle handle, float dx, float dy);

    /// <summary>
    /// 读取角色控制器状态；脚本可在相位 1 调用。
    /// </summary>
    /// <param name="handle">角色控制器句柄。</param>
    /// <returns>上一解算帧的角色状态。</returns>
    CharacterState GetState(CharacterHandle handle);
}

/// <summary>
/// 提供脚本可用的相机 API。
/// </summary>
public interface ICameraApi
{
    /// <summary>
    /// 当前相机中心 X 坐标。
    /// </summary>
    float CenterX { get; }

    /// <summary>
    /// 当前相机中心 Y 坐标。
    /// </summary>
    float CenterY { get; }

    /// <summary>
    /// 当前缩放倍率；1 表示 1 个世界 cell 对应 1 个屏幕像素，值越大越放大。
    /// </summary>
    float Zoom { get; }

    /// <summary>
    /// 设置相机中心；脚本可在相位 1 调用。
    /// </summary>
    /// <param name="x">中心 X 坐标。</param>
    /// <param name="y">中心 Y 坐标。</param>
    void SetCenter(float x, float y);

    /// <summary>
    /// 设置缩放倍率；脚本可在相位 1 调用。
    /// </summary>
    /// <param name="zoom">缩放倍率，必须为有限正数。</param>
    void SetZoom(float zoom);

    /// <summary>
    /// 让相机跟随指定实体；脚本可在相位 1 调用。
    /// </summary>
    /// <param name="target">要跟随的脚本实体。</param>
    void Follow(Entity target);

    /// <summary>
    /// 当前相机视口；脚本可在相位 1 读取。
    /// </summary>
    RectF Viewport { get; }

    /// <summary>
    /// 将屏幕像素坐标转换为世界坐标。
    /// </summary>
    /// <param name="screenX">屏幕 X 坐标。</param>
    /// <param name="screenY">屏幕 Y 坐标。</param>
    /// <returns>对应世界坐标。</returns>
    Point2F ScreenToWorld(float screenX, float screenY);

    /// <summary>
    /// 将世界坐标转换为屏幕像素坐标。
    /// </summary>
    /// <param name="worldX">世界 X 坐标。</param>
    /// <param name="worldY">世界 Y 坐标。</param>
    /// <returns>对应屏幕坐标。</returns>
    Point2F WorldToScreen(float worldX, float worldY);
}

/// <summary>
/// 提供脚本可用的输入 API。
/// </summary>
public interface IInputApi
{
    /// <summary>
    /// 判断按键当前是否按下；脚本可在相位 1 调用，读取本帧输入快照。
    /// </summary>
    /// <param name="key">要查询的按键。</param>
    /// <returns>若当前按下则返回 true，否则返回 false。</returns>
    bool IsDown(Key key);

    /// <summary>
    /// 判断按键是否在本帧按下；脚本可在相位 1 调用，读取本帧输入快照。
    /// </summary>
    /// <param name="key">要查询的按键。</param>
    /// <returns>若该按键在本帧产生按下边沿则返回 true，否则返回 false。</returns>
    bool WasPressed(Key key);

    /// <summary>
    /// 判断按键是否在本帧释放；脚本可在相位 1 调用，读取本帧输入快照。
    /// </summary>
    /// <param name="key">要查询的按键。</param>
    /// <returns>若该按键在本帧产生释放边沿则返回 true，否则返回 false。</returns>
    bool WasReleased(Key key);

    /// <summary>
    /// 读取输入轴；脚本可在相位 1 调用，读取本帧输入快照。
    /// </summary>
    /// <param name="axis">要查询的输入轴。</param>
    /// <returns>输入轴当前值。</returns>
    float Axis(Axis axis);

    /// <summary>
    /// 鼠标所在像素坐标；脚本可在相位 1 读取本帧输入快照。
    /// </summary>
    (float X, float Y) MousePixel { get; }

    /// <summary>
    /// 当前帧鼠标滚轮纵向增量。
    /// </summary>
    float MouseWheelY { get; }

    /// <summary>
    /// 判断鼠标按键当前是否按下。
    /// </summary>
    /// <param name="button">鼠标按键。</param>
    /// <returns>若当前按下则返回 true，否则返回 false。</returns>
    bool IsMouseDown(MouseButton button);

    /// <summary>
    /// 判断鼠标按键是否在本帧按下。
    /// </summary>
    /// <param name="button">鼠标按键。</param>
    /// <returns>若本帧产生按下边沿则返回 true，否则返回 false。</returns>
    bool WasMousePressed(MouseButton button);

    /// <summary>
    /// 判断鼠标按键是否在本帧释放。
    /// </summary>
    /// <param name="button">鼠标按键。</param>
    /// <returns>若本帧产生释放边沿则返回 true，否则返回 false。</returns>
    bool WasMouseReleased(MouseButton button);
}

/// <summary>
/// 提供脚本可用的光照与 fog-of-war 请求 API。
/// </summary>
public interface ILightingApi
{
    /// <summary>
    /// 请求在指定世界坐标周围揭示 fog-of-war。
    /// </summary>
    /// <param name="x">世界 X 坐标。</param>
    /// <param name="y">世界 Y 坐标。</param>
    /// <param name="radius">揭示半径，单位 cell。</param>
    /// <param name="alpha">揭示强度。</param>
    void RevealAround(float x, float y, float radius, byte alpha = byte.MaxValue);

    /// <summary>
    /// 请求揭示当前完整渲染视口，用于不希望 fog-of-war 圆形遮罩裁掉屏幕边角的玩法场景。
    /// </summary>
    /// <param name="alpha">揭示强度。</param>
    void RevealViewport(byte alpha = byte.MaxValue);

    /// <summary>
    /// 添加一盏当前帧点光源；Hosting 在渲染相位消费后可清空。
    /// </summary>
    /// <param name="x">世界 X 坐标。</param>
    /// <param name="y">世界 Y 坐标。</param>
    /// <param name="radius">光照半径，单位 cell。</param>
    /// <param name="colorBgra">BGRA 颜色。</param>
    /// <param name="intensity">光照强度。</param>
    void AddPointLight(float x, float y, float radius, uint colorBgra, float intensity = 1f);

    /// <summary>
    /// 当前待消费点光源数量。
    /// </summary>
    int PointLightCount { get; }

    /// <summary>
    /// 当前待消费 fog reveal 请求数量。
    /// </summary>
    int RevealCount { get; }

    /// <summary>
    /// 按索引读取点光源。
    /// </summary>
    /// <param name="index">点光源索引。</param>
    /// <returns>点光源快照。</returns>
    ScriptPointLight GetPointLight(int index);

    /// <summary>
    /// 按索引读取 fog reveal 请求。
    /// </summary>
    /// <param name="index">请求索引。</param>
    /// <returns>fog reveal 请求。</returns>
    FogRevealRequest GetReveal(int index);

    /// <summary>
    /// 清空瞬时点光源。
    /// </summary>
    void ClearPointLights();
}

/// <summary>
/// 提供脚本可用的屏幕空间 overlay 绘制 API。
/// </summary>
public interface IOverlayApi
{
    /// <summary>
    /// 绘制实色矩形。
    /// </summary>
    /// <param name="x">矩形左上角 X，单位屏幕像素。</param>
    /// <param name="y">矩形左上角 Y，单位屏幕像素。</param>
    /// <param name="width">矩形宽度，单位屏幕像素。</param>
    /// <param name="height">矩形高度，单位屏幕像素。</param>
    /// <param name="colorBgra">BGRA8 非预乘颜色。</param>
    void SolidRectangle(float x, float y, float width, float height, uint colorBgra);

    /// <summary>
    /// 绘制矩形描边。
    /// </summary>
    /// <param name="x">矩形左上角 X，单位屏幕像素。</param>
    /// <param name="y">矩形左上角 Y，单位屏幕像素。</param>
    /// <param name="width">矩形宽度，单位屏幕像素。</param>
    /// <param name="height">矩形高度，单位屏幕像素。</param>
    /// <param name="thickness">描边厚度，单位屏幕像素。</param>
    /// <param name="colorBgra">BGRA8 非预乘颜色。</param>
    void OutlineRectangle(float x, float y, float width, float height, float thickness, uint colorBgra);

    /// <summary>
    /// 绘制带厚度线段。
    /// </summary>
    /// <param name="startX">起点 X，单位屏幕像素。</param>
    /// <param name="startY">起点 Y，单位屏幕像素。</param>
    /// <param name="endX">终点 X，单位屏幕像素。</param>
    /// <param name="endY">终点 Y，单位屏幕像素。</param>
    /// <param name="thickness">线段厚度，单位屏幕像素。</param>
    /// <param name="colorBgra">BGRA8 非预乘颜色。</param>
    void Line(float startX, float startY, float endX, float endY, float thickness, uint colorBgra);
}

/// <summary>
/// 提供脚本可用的只读引擎诊断 API。
/// </summary>
public interface IDiagnosticsApi
{
    /// <summary>
    /// 捕获当前诊断计数器快照。
    /// </summary>
    /// <returns>诊断快照。</returns>
    EngineDiagnosticsSnapshot Capture();

    /// <summary>
    /// 判断指定调试叠层是否启用。
    /// </summary>
    /// <param name="overlay">调试叠层类型。</param>
    /// <returns>启用时返回 true。</returns>
    bool IsOverlayEnabled(DebugOverlayKind overlay);

    /// <summary>
    /// 设置指定调试叠层开关。
    /// </summary>
    /// <param name="overlay">调试叠层类型。</param>
    /// <param name="enabled">是否启用。</param>
    void SetOverlay(DebugOverlayKind overlay, bool enabled);

    /// <summary>
    /// 切换指定调试叠层开关。
    /// </summary>
    /// <param name="overlay">调试叠层类型。</param>
    /// <returns>切换后的启用状态。</returns>
    bool ToggleOverlay(DebugOverlayKind overlay);
}

/// <summary>
/// 脚本 HUD 可消费的引擎诊断快照。
/// </summary>
/// <param name="FrameCount">当前渲染帧序号。</param>
/// <param name="FramesPerSecond">长窗口平均 render FPS；无墙钟样本时回退为当前固定步长帧率。</param>
/// <param name="FrameMilliseconds">长窗口平均渲染帧耗时，单位毫秒。</param>
/// <param name="FrameLastMilliseconds">最近一帧渲染耗时，单位毫秒。</param>
/// <param name="FrameP99Milliseconds">长窗口 99 分位渲染帧耗时，单位毫秒。</param>
/// <param name="FrameLow1PercentFps">基于 99 分位帧耗时计算的 1% low FPS。</param>
/// <param name="FrameJitterMilliseconds">长窗口渲染帧耗时标准差，单位毫秒。</param>
/// <param name="FrameSampleCount">渲染帧率统计窗口内的样本数。</param>
/// <param name="SimHz">当前 sim 频率。</param>
/// <param name="ActiveChunks">活跃 chunk 数。</param>
/// <param name="ResidentChunks">常驻 chunk 数。</param>
/// <param name="FreeParticles">自由粒子数。</param>
/// <param name="RigidBodies">活跃刚体数。</param>
/// <param name="PointLights">当前脚本光照同步后可供渲染消费的点光源数量。</param>
public readonly record struct EngineDiagnosticsSnapshot(
    long FrameCount,
    float FramesPerSecond,
    float FrameMilliseconds,
    float FrameLastMilliseconds,
    float FrameP99Milliseconds,
    float FrameLow1PercentFps,
    float FrameJitterMilliseconds,
    int FrameSampleCount,
    float SimHz,
    long ActiveChunks,
    long ResidentChunks,
    long FreeParticles,
    long RigidBodies,
    long PointLights);

/// <summary>
/// 脚本可切换的调试叠层类型。
/// </summary>
public enum DebugOverlayKind
{
    /// <summary>
    /// dirty rectangle 边框。
    /// </summary>
    DirtyRects,

    /// <summary>
    /// 本帧 CA 实际迭代的 dirty rectangle。
    /// </summary>
    CaIterationRects,

    /// <summary>
    /// chunk 网格与 4-pass parity 着色边框。
    /// </summary>
    ChunkGridParity,

    /// <summary>
    /// KeepAlive / 边界唤醒热点。
    /// </summary>
    KeepAliveHotspots,

    /// <summary>
    /// cell parity 位着色。
    /// </summary>
    CellParity,

    /// <summary>
    /// 温度热力图。
    /// </summary>
    TemperatureHeatmap,

    /// <summary>
    /// owned-by-body 着色。
    /// </summary>
    OwnedByBody,

    /// <summary>
    /// 自由粒子轨迹。
    /// </summary>
    ParticleTrails,

    /// <summary>
    /// 刚体连通块区域。
    /// </summary>
    ConnectedComponents,
}

/// <summary>
/// 脚本可见的运行时控制能力。
/// </summary>
public interface IRuntimeControlApi
{
    /// <summary>
    /// 捕获当前运行控制状态。
    /// </summary>
    /// <returns>运行控制快照。</returns>
    RuntimeControlSnapshot Capture();

    /// <summary>
    /// 暂停 sim/physics；渲染、GUI、输入和后台流式相位仍继续出帧。
    /// </summary>
    void PauseSimulation();

    /// <summary>
    /// 恢复 sim/physics 运行。
    /// </summary>
    void ResumeSimulation();

    /// <summary>
    /// 请求宿主在当前 tick 结束后关闭。
    /// </summary>
    /// <returns>控制操作结果。</returns>
    RuntimeControlResult RequestShutdown();

    /// <summary>
    /// 请求宿主打开已启用的内嵌 Editor dockspace。
    /// </summary>
    /// <returns>控制操作结果。</returns>
    RuntimeControlResult OpenEditor();

    /// <summary>
    /// 请求宿主在相位安全点重开当前关卡。
    /// </summary>
    /// <returns>控制操作结果。</returns>
    RuntimeControlResult RequestRestartCurrentScene();

    /// <summary>
    /// 捕获运行时可由游戏 UI 切换的设置状态。
    /// </summary>
    /// <returns>运行时设置快照。</returns>
    RuntimeSettingsSnapshot CaptureSettings();

    /// <summary>
    /// 切换窗口 VSync。
    /// </summary>
    /// <param name="enabled">是否开启 VSync。</param>
    /// <returns>控制操作结果。</returns>
    RuntimeControlResult SetVSyncEnabled(bool enabled);

    /// <summary>
    /// 切换运行时音频总开关。
    /// </summary>
    /// <param name="enabled">是否开启音频。</param>
    /// <returns>控制操作结果。</returns>
    RuntimeControlResult SetAudioEnabled(bool enabled);
}

/// <summary>
/// 运行时控制状态快照。
/// </summary>
/// <param name="IsPlaying">当前是否推进 sim/physics。</param>
/// <param name="IsShutdownRequested">是否已请求关闭。</param>
/// <param name="RequestedSimHz">当前请求的 sim 频率。</param>
/// <param name="FrameCount">当前渲染帧序号。</param>
public readonly record struct RuntimeControlSnapshot(
    bool IsPlaying,
    bool IsShutdownRequested,
    double RequestedSimHz,
    long FrameCount);

/// <summary>
/// 运行时控制操作结果。
/// </summary>
/// <param name="Success">操作是否已被宿主接纳。</param>
/// <param name="Message">面向脚本/调试 UI 的简短说明。</param>
public readonly record struct RuntimeControlResult(bool Success, string Message);

/// <summary>
/// 运行时可调设置快照。
/// </summary>
/// <param name="VSyncEnabled">当前 VSync 是否开启。</param>
/// <param name="CanToggleVSync">当前宿主是否支持运行时切换 VSync。</param>
/// <param name="AudioEnabled">当前音频是否开启。</param>
/// <param name="CanToggleAudio">当前宿主是否支持运行时切换音频。</param>
public readonly record struct RuntimeSettingsSnapshot(
    bool VSyncEnabled,
    bool CanToggleVSync,
    bool AudioEnabled,
    bool CanToggleAudio);

/// <summary>
/// 提供脚本可用的事件订阅 API。
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// 订阅指定事件类型；脚本可在相位 1 调用，返回的句柄释放后取消订阅。
    /// </summary>
    /// <typeparam name="TEvent">要订阅的事件类型。</typeparam>
    /// <param name="handler">事件处理器；由运行时在相位 1 分发事件时调用。</param>
    /// <returns>用于取消订阅的释放句柄。</returns>
    IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : unmanaged;

    /// <summary>
    /// 向指定事件类型通道发布事件；容量满时返回 false，由调用方决定降级或丢弃。
    /// </summary>
    /// <typeparam name="TEvent">要发布的事件类型。</typeparam>
    /// <param name="item">事件载荷。</param>
    /// <returns>事件是否已成功写入通道。</returns>
    bool TryPublish<TEvent>(in TEvent item)
        where TEvent : unmanaged
    {
        _ = item;
        throw new NotSupportedException("当前脚本上下文未提供事件发布后端。");
    }
}

/// <summary>
/// 提供脚本可用的音频播放 API。
/// </summary>
public interface IAudioApi
{
    /// <summary>
    /// 播放非空间化一次性音效；脚本可在相位 1 调用，请求由音频后端异步消费。
    /// </summary>
    /// <param name="cue">音效 cue 名。</param>
    /// <param name="volume">播放音量。</param>
    void PlayOneShot(string cue, float volume = 1f);

    /// <summary>
    /// 在指定世界坐标播放音效；脚本可在相位 1 调用，请求由音频后端异步消费。
    /// </summary>
    /// <param name="cue">音效 cue 名。</param>
    /// <param name="x">播放位置 X 坐标。</param>
    /// <param name="y">播放位置 Y 坐标。</param>
    /// <param name="volume">播放音量。</param>
    void PlayAt(string cue, float x, float y, float volume = 1f);
}

/// <summary>
/// 提供脚本可用的时间信息。
/// </summary>
public interface IGameTime
{
    /// <summary>
    /// 当前渲染帧 delta time，单位秒。
    /// </summary>
    float DeltaTime { get; }

    /// <summary>
    /// 当前渲染帧真实墙钟 delta time，单位秒；无墙钟样本时回退到 <see cref="DeltaTime" />。
    /// </summary>
    float RealDeltaTime => DeltaTime;

    /// <summary>
    /// 当前固定逻辑步长，单位秒。
    /// </summary>
    float FixedStep { get; }

    /// <summary>
    /// 当前渲染帧序号。
    /// </summary>
    long FrameCount { get; }

    /// <summary>
    /// 当前时间膨胀系数。
    /// </summary>
    float TimeScale { get; }

    /// <summary>
    /// 当前帧是否执行了 sim step。
    /// </summary>
    bool SimSteppedThisFrame { get; }
}

/// <summary>
/// 提供脚本可用的内容配置加载 API。
/// </summary>
public interface IConfigApi
{
    /// <summary>
    /// 从当前 ContentRoot 加载一个配置文档。
    /// </summary>
    /// <typeparam name="TConfig">配置文档类型。</typeparam>
    /// <param name="relativePath">相对 ContentRoot 的配置路径。</param>
    /// <param name="typeInfo">source-generated JSON 类型元数据。</param>
    /// <returns>解析后的配置文档。</returns>
    TConfig Load<TConfig>(string relativePath, JsonTypeInfo<TConfig> typeInfo)
        where TConfig : class;
}
