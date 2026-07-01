namespace PixelEngine.Scripting;

/// <summary>
/// 脚本访问引擎能力的统一入口；由 Hosting 在装配期注入 Behaviour。
/// </summary>
public interface IScriptContext
{
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
    /// 引擎只读诊断能力。
    /// </summary>
    IDiagnosticsApi Diagnostics { get; }

    /// <summary>
    /// 脚本事件订阅能力；事件在相位 1 分发。
    /// </summary>
    IEventBus Events { get; }

    /// <summary>
    /// 音频播放能力；播放请求进入事件/音频后端。
    /// </summary>
    IAudioApi Audio { get; }

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
/// 提供脚本可用的世界级复合效果 API。
/// </summary>
public interface IWorldEffects
{
    /// <summary>
    /// 延迟触发一次爆炸：把半径内可抛射 cell 转为自由粒子，并对邻近刚体施加径向冲量。
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
    /// 延迟生成一个自由粒子；脚本可在相位 1 调用，实际生成在粒子安全相位落地。
    /// </summary>
    /// <param name="desc">粒子生成描述。</param>
    void Spawn(in ParticleSpawnDesc desc);

    /// <summary>
    /// 延迟生成一组爆发式自由粒子；脚本可在相位 1 调用，实际生成在粒子安全相位落地。
    /// </summary>
    /// <param name="x">爆发中心 X 坐标。</param>
    /// <param name="y">爆发中心 Y 坐标。</param>
    /// <param name="material">粒子材质句柄。</param>
    /// <param name="count">要生成的粒子数量。</param>
    /// <param name="speed">初始速度标量。</param>
    void Burst(float x, float y, MaterialId material, int count, float speed);
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
/// 提供脚本可用的只读引擎诊断 API。
/// </summary>
public interface IDiagnosticsApi
{
    /// <summary>
    /// 捕获当前诊断计数器快照。
    /// </summary>
    /// <returns>诊断快照。</returns>
    EngineDiagnosticsSnapshot Capture();
}

/// <summary>
/// 脚本 HUD 可消费的引擎诊断快照。
/// </summary>
/// <param name="FrameCount">当前渲染帧序号。</param>
/// <param name="FramesPerSecond">最近一帧估算 FPS。</param>
/// <param name="SimHz">当前 sim 频率。</param>
/// <param name="ActiveChunks">活跃 chunk 数。</param>
/// <param name="ResidentChunks">常驻 chunk 数。</param>
/// <param name="FreeParticles">自由粒子数。</param>
/// <param name="RigidBodies">活跃刚体数。</param>
public readonly record struct EngineDiagnosticsSnapshot(
    long FrameCount,
    float FramesPerSecond,
    float SimHz,
    long ActiveChunks,
    long ResidentChunks,
    long FreeParticles,
    long RigidBodies);

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
