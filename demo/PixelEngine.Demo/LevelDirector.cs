using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 程序化关卡导演，负责铺设初始像素关卡并装配关卡脚本实体。
/// </summary>
public sealed class LevelDirector : Behaviour
{
    private MaterialId _empty;
    private MaterialId _stone;
    private MaterialId _dirt;
    private MaterialId _sand;
    private MaterialId _wood;
    private MaterialId _water;
    private MaterialId _oil;
    private MaterialId _lava;
    private MaterialId _acid;
    private MaterialId _metal;
    private readonly List<BodyHandle> _rigidStructures = [];
    private bool _materialsResolved;
    private bool _worldBuilt;
    private bool _entitiesBuilt;
    private bool _entityBuildSystemRegistered;

    /// <summary>
    /// 关卡宽度，单位 cell。
    /// </summary>
    public int LevelWidth { get; set; } = 640;

    /// <summary>
    /// 关卡高度，单位 cell。
    /// </summary>
    public int LevelHeight { get; set; } = 360;

    /// <summary>
    /// 玩家出生点 X 坐标。
    /// </summary>
    public float PlayerSpawnX { get; set; } = 48f;

    /// <summary>
    /// 玩家出生点 Y 坐标。
    /// </summary>
    public float PlayerSpawnY { get; set; } = 244f;

    /// <summary>
    /// 终点区域左上角 X 坐标。
    /// </summary>
    public float GoalX { get; set; } = 570f;

    /// <summary>
    /// 终点区域左上角 Y 坐标。
    /// </summary>
    public float GoalY { get; set; } = 208f;

    /// <summary>
    /// 是否创建玩家、相机、笔刷、喷口和目标触发器脚本实体。
    /// </summary>
    public bool BuildScriptEntities { get; set; } = true;

    /// <summary>
    /// 是否创建持续喷发的测试火花。默认关闭，避免真实游玩画面出现难以辨识的上方散点。
    /// </summary>
    public bool BuildAmbientSparkEmitters { get; set; }

    /// <summary>
    /// 是否创建持续喷口发射器。默认关闭，避免真实游玩入口出现测试用的上方滴落点。
    /// </summary>
    public bool BuildAmbientMaterialEmitters { get; set; }

    /// <summary>
    /// 是否创建旧版无任务条件的 GoalTrigger。熔岩矿洞逃生场景会关闭它并改用 ExtractionTrigger。
    /// </summary>
    public bool BuildGoalTrigger { get; set; } = true;

    /// <summary>
    /// 是否在玩家出生 AABB 内铺设熔岩；仅用于窗口健康链路探针。
    /// </summary>
    public bool BuildSpawnHazardProbe { get; set; }

    /// <summary>
    /// Demo 相机默认缩放倍率；窗口探针可调小视野以验证真实跟随。
    /// </summary>
    public float CameraZoom { get; set; } = 2f;

    /// <summary>
    /// 已通过脚本刚体 API 注册的可破坏结构数量。
    /// </summary>
    public int RigidStructureCount => _rigidStructures.Count;

    /// <summary>
    /// 是否已把关卡中的木 / 金属结构排队转换为动态刚体。
    /// </summary>
    public bool RigidStructuresQueued { get; private set; }

    /// <summary>
    /// 最近一次阻塞原因；为空表示脚本已完成初始化。
    /// </summary>
    public string BlockedReason { get; private set; } = string.Empty;

    /// <inheritdoc />
    protected override void OnStart()
    {
        ResolveMaterials();
        if (!_materialsResolved)
        {
            return;
        }

        BuildWorld();
        QueueRigidStructures();
        RegisterEntityBuildSystem();
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        _ = dt;
        ResolveMaterials();
        if (!_materialsResolved)
        {
            return;
        }

        if (!_worldBuilt)
        {
            BuildWorld();
        }

        BuildSpawnHazardProbeArea();
        QueueRigidStructures();
        RegisterEntityBuildSystem();
    }

    private void RegisterEntityBuildSystem()
    {
        if (!BuildScriptEntities || _entitiesBuilt || _entityBuildSystemRegistered)
        {
            return;
        }

        Context.Scene.RegisterSystem(new EntityBuildSystem(this));
        _entityBuildSystemRegistered = true;
    }

    private void ResolveMaterials()
    {
        if (_materialsResolved)
        {
            return;
        }

        _empty = Context.Materials.Resolve("empty");
        _stone = Context.Materials.Resolve("stone");
        _dirt = Context.Materials.Resolve("dirt");
        _sand = Context.Materials.Resolve("sand");
        _wood = Context.Materials.Resolve("wood");
        _water = Context.Materials.Resolve("water");
        _oil = Context.Materials.Resolve("oil");
        _lava = Context.Materials.Resolve("lava");
        _acid = Context.Materials.Resolve("acid");
        _metal = Context.Materials.Resolve("metal");
        _materialsResolved = _empty.IsValid &&
            _stone.IsValid &&
            _dirt.IsValid &&
            _sand.IsValid &&
            _wood.IsValid &&
            _water.IsValid &&
            _oil.IsValid &&
            _lava.IsValid &&
            _acid.IsValid &&
            _metal.IsValid;
        BlockedReason = _materialsResolved ? string.Empty : "关卡生成所需材质未全部解析。";
    }

    private void BuildWorld()
    {
        int width = Math.Max(128, LevelWidth);
        int height = Math.Max(128, LevelHeight);
        ClearPlayableArea(width, height);
        BuildBounds(width, height);
        BuildTerrain(width, height);
        BuildHazards(height);
        BuildSpawnHazardProbeArea();
        BuildGoalMarker();
        _worldBuilt = true;
    }

    private void BuildEntities()
    {
        if (_entitiesBuilt)
        {
            return;
        }

        Entity playerEntity = Context.Scene.CreateEntity();
        _ = playerEntity.AddComponent<Transform>();
        PlayerController player = playerEntity.AddComponent<PlayerController>();
        player.SpawnX = PlayerSpawnX;
        player.SpawnY = PlayerSpawnY;
        player.Width = 6f;
        player.Height = 12f;

        PlayerHealth health = playerEntity.AddComponent<PlayerHealth>();
        health.ForceHazardForProbe = BuildSpawnHazardProbe;

        CameraFollow camera = playerEntity.AddComponent<CameraFollow>();
        camera.MinX = 0f;
        camera.MinY = 0f;
        camera.MaxX = LevelWidth;
        camera.MaxY = LevelHeight;
        camera.Zoom = CameraZoom;

        MaterialBrush brush = playerEntity.AddComponent<MaterialBrush>();
        brush.InputEnabled = false;

        ExplosiveTool explosive = playerEntity.AddComponent<ExplosiveTool>();
        _ = explosive;

        PlayableProjectileTool projectile = playerEntity.AddComponent<PlayableProjectileTool>();
        projectile.ImpactRadius = 3;
        projectile.ImpactForce = 9f;
        projectile.CollapseScanRadius = 36;
        projectile.CollapseScanRetryFrames = 2;
        projectile.FallbackOverhangRadius = 18;
        projectile.MaxCollapseRegionSize = 48;
        projectile.MaxCollapsePixels = 512;
        projectile.MaxCollapsedIslandsPerShot = 1;
        projectile.PlayerSupportProtectionRadius = 72;
        projectile.InputEnabled = false;

        _ = playerEntity.AddComponent<WeaponController>();

        if (BuildGoalTrigger)
        {
            GoalTrigger goal = playerEntity.AddComponent<GoalTrigger>();
            goal.X = GoalX;
            goal.Y = GoalY;
            goal.Width = 34f;
            goal.Height = 54f;
        }

        _ = playerEntity.AddComponent<PlayerVisual>();
        PlayableHud playableHud = playerEntity.AddComponent<PlayableHud>();
        playableHud.X = 14f;
        playableHud.Y = 14f;
        _ = playerEntity.AddComponent<PauseMenu>();

        if (BuildAmbientMaterialEmitters)
        {
            CreateEmitter(156f, 86f, "water", 0f, 1f, 0.18f, 2, "splash_water.wav", addLight: false);
            CreateEmitter(314f, 146f, "oil", -0.25f, 1f, 0.35f, 2, string.Empty, addLight: false);
            CreateEmitter(438f, 236f, "lava", 0f, 1f, 0.45f, 2, "lava_bubble_loop.wav", addLight: true);
            CreateEmitter(508f, 150f, "acid", 0f, 1f, 0.32f, 2, "splash_acid.wav", addLight: false);
        }

        if (BuildAmbientSparkEmitters)
        {
            CreateSparkEmitter(294f, 214f, count: 6, intervalSeconds: 0.07f);
            CreateSparkEmitter(438f, 232f, count: 5, intervalSeconds: 0.09f);
        }

        _entitiesBuilt = true;
    }

    private void CreateEmitter(
        float x,
        float y,
        string materialName,
        float directionX,
        float directionY,
        float interval,
        int radius,
        string audioCue,
        bool addLight)
    {
        Entity entity = Context.Scene.CreateEntity();
        MaterialEmitter emitter = entity.AddComponent<MaterialEmitter>();
        emitter.X = x;
        emitter.Y = y;
        emitter.MaterialName = materialName;
        emitter.DirectionX = directionX;
        emitter.DirectionY = directionY;
        emitter.IntervalSeconds = interval;
        emitter.Radius = radius;
        emitter.AudioCue = audioCue;
        emitter.AddLight = addLight;
        emitter.LightColorBgra = materialName == "lava" ? 0xFF_20_70_FF : 0xFF_40_80_FF;
    }

    private void CreateSparkEmitter(float x, float y, int count, float intervalSeconds)
    {
        Entity entity = Context.Scene.CreateEntity();
        SparkEmitter emitter = entity.AddComponent<SparkEmitter>();
        emitter.X = x;
        emitter.Y = y;
        emitter.Count = count;
        emitter.IntervalSeconds = intervalSeconds;
    }

    private void ClearPlayableArea(int width, int height)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Context.Cells.SetCell(x, y, _empty);
            }
        }
    }

    private void BuildBounds(int width, int height)
    {
        FillRect(0, 0, width, 8, _stone);
        FillRect(0, height - 12, width, 12, _stone);
        FillRect(0, 0, 10, height, _stone);
        FillRect(width - 10, 0, 10, height, _stone);
    }

    private void BuildTerrain(int width, int height)
    {
        int floorY = height - 72;
        FillRect(10, floorY, width - 20, height - floorY - 12, _dirt);
        FillRect(10, floorY + 18, width - 20, 16, _stone);
        FillRect(28, floorY - 14, 110, 14, _stone);
        FillRect(168, floorY - 42, 82, 10, _wood);
        FillRect(276, floorY - 74, 92, 10, _metal);
        FillRect(410, floorY - 52, 70, 10, _wood);
        FillRect(532, floorY - 66, 80, 12, _stone);
        FillRect(98, floorY - 86, 64, 8, _metal);
        FillRect(224, floorY - 122, 58, 8, _wood);
        FillRect(372, floorY - 118, 72, 8, _metal);
        FillRect(138, 108, 34, floorY - 108, _stone);
        FillRect(488, 120, 28, floorY - 52, _stone);
        FillSlope(32, floorY - 1, 88, 24, _sand);
        FillSlope(386, floorY - 1, 74, 22, _sand);
    }

    private void QueueRigidStructures()
    {
        if (!_worldBuilt || RigidStructuresQueued)
        {
            return;
        }

        int floorY = Math.Max(128, LevelHeight) - 72;
        try
        {
            CreateRigidStructure(168, floorY - 42, 82, 10);
            CreateRigidStructure(276, floorY - 74, 92, 10);
            CreateRigidStructure(410, floorY - 52, 70, 10);
            CreateRigidStructure(98, floorY - 86, 64, 8);
            CreateRigidStructure(224, floorY - 122, 58, 8);
            CreateRigidStructure(372, floorY - 118, 72, 8);
            RigidStructuresQueued = true;
            BlockedReason = string.Empty;
        }
        catch (InvalidOperationException exception)
        {
            BlockedReason = $"可破坏结构注册失败：{exception.Message}";
        }
    }

    private void CreateRigidStructure(int x, int y, int width, int height)
    {
        _rigidStructures.Add(Context.Bodies.CreateFromRegion(x, y, width, height));
    }

    private void BuildHazards(int height)
    {
        int floorY = height - 72;
        FillRect(252, floorY - 1, 84, 22, _lava);
        FillRect(496, floorY - 1, 52, 18, _acid);
        FillRect(338, floorY - 18, 44, 16, _water);
        FillRect(188, floorY - 31, 34, 20, _oil);
    }

    private void BuildSpawnHazardProbeArea()
    {
        if (!BuildSpawnHazardProbe)
        {
            return;
        }

        int x = (int)MathF.Floor(PlayerSpawnX);
        int y = (int)MathF.Floor(PlayerSpawnY);
        FillRect(x, y, 8, 14, _lava);
    }

    private void BuildGoalMarker()
    {
        int x = (int)MathF.Round(GoalX);
        int y = (int)MathF.Round(GoalY);
        FillRect(x - 4, y, 4, 54, _wood);
        FillRect(x, y, 34, 5, _sand);
        FillRect(x, y + 49, 34, 5, _sand);
        FillRect(x + 29, y, 5, 54, _sand);
    }

    private void FillRect(int x, int y, int width, int height, MaterialId material)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        for (int cy = y; cy < y + height; cy++)
        {
            for (int cx = x; cx < x + width; cx++)
            {
                Context.Cells.SetCell(cx, cy, material);
            }
        }
    }

    private void FillSlope(int x, int baseY, int width, int height, MaterialId material)
    {
        for (int cx = 0; cx < width; cx++)
        {
            int columnHeight = Math.Max(1, (int)MathF.Round((1f - (cx / (float)Math.Max(1, width - 1))) * height));
            for (int cy = 0; cy < columnHeight; cy++)
            {
                Context.Cells.SetCell(x + cx, baseY - cy, material);
            }
        }
    }

    private sealed class EntityBuildSystem(LevelDirector director) : ISystem
    {
        public void OnSimTick(IScriptContext context)
        {
            _ = context;
        }

        public void OnFrame(IScriptContext context, float dt)
        {
            _ = context;
            _ = dt;
            director.BuildEntities();
        }
    }
}
