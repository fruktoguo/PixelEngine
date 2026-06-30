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
    /// 即时读取指定世界坐标的材质。
    /// </summary>
    MaterialId GetMaterial(int x, int y);

    /// <summary>
    /// 即时读取指定世界坐标的 cell 快照。
    /// </summary>
    CellView Sample(int x, int y);

    /// <summary>
    /// 即时判断指定世界坐标是否为固体。
    /// </summary>
    bool IsSolid(int x, int y);

    /// <summary>
    /// 延迟写入指定 cell，并在落地时标记 dirty。
    /// </summary>
    void SetCell(int x, int y, MaterialId material);

    /// <summary>
    /// 延迟绘制圆形区域，并在落地时标记 dirty。
    /// </summary>
    void Paint(int x, int y, int radius, MaterialId material);
}

/// <summary>
/// 提供基于稳定材质名的查询能力。
/// </summary>
public interface IMaterialQuery
{
    /// <summary>
    /// 按稳定材质名解析材质句柄；失败时返回 <see cref="MaterialId.Invalid" />。
    /// </summary>
    MaterialId Resolve(string name);

    /// <summary>
    /// 尝试按稳定材质名解析材质句柄。
    /// </summary>
    bool TryResolve(string name, out MaterialId id);

    /// <summary>
    /// 读取材质属性摘要。
    /// </summary>
    MaterialInfo GetInfo(MaterialId id);
}

/// <summary>
/// 提供自由粒子生成能力。
/// </summary>
public interface IParticleSpawner
{
    /// <summary>
    /// 延迟生成一个自由粒子。
    /// </summary>
    void Spawn(in ParticleSpawnDesc desc);

    /// <summary>
    /// 延迟生成一组爆发式自由粒子。
    /// </summary>
    void Burst(float x, float y, MaterialId material, int count, float speed);
}

/// <summary>
/// 提供固体像素采样与 raycast 能力。
/// </summary>
public interface ISolidSampler
{
    /// <summary>
    /// 从指定位置沿方向执行 raycast。
    /// </summary>
    bool Raycast(float x, float y, float dx, float dy, float maxDist, out RaycastHit hit);

    /// <summary>
    /// 判断指定 AABB 内是否包含固体像素。
    /// </summary>
    bool SampleSolidAabb(float x, float y, float width, float height);
}

/// <summary>
/// 提供脚本可用的刚体 API。
/// </summary>
public interface IRigidBodyApi
{
    /// <summary>
    /// 延迟从像素区域创建刚体。
    /// </summary>
    BodyHandle CreateFromRegion(int x, int y, int width, int height);

    /// <summary>
    /// 尝试读取刚体上一帧末的变换。
    /// </summary>
    bool TryGetTransform(BodyHandle handle, out BodyTransform transform);

    /// <summary>
    /// 延迟对刚体施加冲量。
    /// </summary>
    void ApplyImpulse(BodyHandle handle, float impulseX, float impulseY);

    /// <summary>
    /// 延迟销毁刚体。
    /// </summary>
    void Destroy(BodyHandle handle);
}

/// <summary>
/// 提供 kinematic 角色控制器 API。
/// </summary>
public interface ICharacterController
{
    /// <summary>
    /// 创建一个角色控制器。
    /// </summary>
    CharacterHandle Create(float x, float y, float width, float height);

    /// <summary>
    /// 延迟移动角色控制器。
    /// </summary>
    void Move(CharacterHandle handle, float dx, float dy);

    /// <summary>
    /// 读取角色控制器状态。
    /// </summary>
    CharacterState GetState(CharacterHandle handle);
}

/// <summary>
/// 提供脚本可用的相机 API。
/// </summary>
public interface ICameraApi
{
    /// <summary>
    /// 设置相机中心。
    /// </summary>
    void SetCenter(float x, float y);

    /// <summary>
    /// 让相机跟随指定实体。
    /// </summary>
    void Follow(Entity target);

    /// <summary>
    /// 当前相机视口。
    /// </summary>
    RectF Viewport { get; }
}

/// <summary>
/// 提供脚本可用的输入 API。
/// </summary>
public interface IInputApi
{
    /// <summary>
    /// 判断按键当前是否按下。
    /// </summary>
    bool IsDown(Key key);

    /// <summary>
    /// 判断按键是否在本帧按下。
    /// </summary>
    bool WasPressed(Key key);

    /// <summary>
    /// 读取输入轴。
    /// </summary>
    float Axis(Axis axis);

    /// <summary>
    /// 鼠标所在像素坐标。
    /// </summary>
    (float X, float Y) MousePixel { get; }
}

/// <summary>
/// 提供脚本可用的事件订阅 API。
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// 订阅指定事件类型；返回的句柄释放后取消订阅。
    /// </summary>
    IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : struct;
}

/// <summary>
/// 提供脚本可用的音频播放 API。
/// </summary>
public interface IAudioApi
{
    /// <summary>
    /// 在指定世界坐标播放音效。
    /// </summary>
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
