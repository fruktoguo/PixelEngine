using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// Demo 程序化关卡导演，负责铺设初始像素关卡并装配关卡脚本实体。
/// </summary>
public sealed class LevelDirector : Behaviour, IAuthoringWorldPreviewProvider
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
    public AuthoringWorldPreviewDescriptor DescribeAuthoringWorld()
    {
        ResolveAuthoringAnchors();
        int width = Math.Max(128, LevelWidth);
        int height = Math.Max(128, LevelHeight);
        return new AuthoringWorldPreviewDescriptor(width, height, ComputeAuthoringContentHash(width, height));
    }

    /// <inheritdoc />
    public void PopulateAuthoringWorld(in AuthoringWorldPreviewContext context)
    {
        AuthoringWorldPreviewDescriptor descriptor = DescribeAuthoringWorld().Validate();
        if (context.WidthCells != descriptor.WidthCells || context.HeightCells != descriptor.HeightCells)
        {
            throw new InvalidOperationException("LevelDirector authoring world 描述与铺设上下文尺寸不一致。");
        }

        ResolveMaterials(context.Materials);
        if (!_materialsResolved)
        {
            throw new InvalidOperationException(BlockedReason);
        }

        AuthoringWorldWriter writer = new(
            context.Edit,
            descriptor.WidthCells,
            descriptor.HeightCells);
        PopulateWorld(ref writer, descriptor.WidthCells, descriptor.HeightCells);
        _worldBuilt = true;
    }

    /// <inheritdoc />
    public void AdoptAuthoringWorld()
    {
        _worldBuilt = true;
    }

    /// <inheritdoc />
    protected override void OnStart()
    {
        // 启动阶段：解析材质 → 铺设像素世界 → 排队刚体结构 → 注册实体构建系统
        ResolveAuthoringAnchors();
        ResolveMaterials(Context.Materials);
        if (!_materialsResolved)
        {
            return;
        }

        if (!_worldBuilt)
        {
            BuildWorld();
        }

        QueueRigidStructures();
        RegisterEntityBuildSystem();
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        _ = dt;
        // 容错重试：材质未就绪或世界未建成时延迟补建
        if (!_worldBuilt)
        {
            ResolveAuthoringAnchors();
        }

        ResolveMaterials(Context.Materials);
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

    private void ResolveAuthoringAnchors()
    {
        // 新版场景把关键点建模为真实 GameObject；旧 v1/probe 场景仍回退到序列化坐标字段。
        // 所有场景实体已在 Behaviour.OnStart 前完成装配，所以这里可安全读取兄弟实体 Transform。
        if (Entity is null)
        {
            return;
        }

        if (Entity.Scene.TryGetFirstComponent(out PlayerSpawnPoint? spawnPoint) &&
            spawnPoint.Entity.TryGetComponent(out Transform spawnTransform))
        {
            PlayerSpawnX = spawnTransform.X;
            PlayerSpawnY = spawnTransform.Y;
        }

        if (Entity.Scene.TryGetFirstComponent(out GoalPoint? goalPoint) &&
            goalPoint.Entity.TryGetComponent(out Transform goalTransform))
        {
            GoalX = goalTransform.X;
            GoalY = goalTransform.Y;
        }
    }

    private void ResolveMaterials(IMaterialQuery materials)
    {
        if (_materialsResolved)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(materials);
        _empty = materials.Resolve("empty");
        _stone = materials.Resolve("stone");
        _dirt = materials.Resolve("dirt");
        _sand = materials.Resolve("sand");
        _wood = materials.Resolve("wood");
        _water = materials.Resolve("water");
        _oil = materials.Resolve("oil");
        _lava = materials.Resolve("lava");
        _acid = materials.Resolve("acid");
        _metal = materials.Resolve("metal");
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

    // 像素关卡铺设：清场 → 边界 → 地形 → 危险区 → 探针 → 终点标记
    private void BuildWorld()
    {
        int width = Math.Max(128, LevelWidth);
        int height = Math.Max(128, LevelHeight);
        RuntimeWorldWriter writer = new(Context.Cells, width, height);
        PopulateWorld(ref writer, width, height);
        _worldBuilt = true;
    }

    private void PopulateWorld<TWriter>(ref TWriter writer, int width, int height)
        where TWriter : struct, IWorldWriter
    {
        ClearPlayableArea(ref writer, width, height);
        BuildBounds(ref writer, width, height);
        BuildTerrain(ref writer, width, height);
        BuildHazards(ref writer, height);
        BuildSpawnHazardProbeArea(ref writer);
        BuildGoalMarker(ref writer);
    }

    // 装配玩家实体树：移动/生命/相机/工具链/HUD/UI 控制器
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
        player.EnableEscapeRespawn = true;
        player.EscapeMinX = -64f;
        player.EscapeMinY = -128f;
        player.EscapeMaxX = LevelWidth + 64f;
        player.EscapeMaxY = LevelHeight + 96f;

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
        explosive.TerrainEffectScale = 10f;

        PlayableProjectileTool projectile = playerEntity.AddComponent<PlayableProjectileTool>();
        projectile.ImpactRadius = 3;
        projectile.ImpactForce = 9f;
        projectile.ImpactDamage = 36f;
        projectile.UseExplosionDamage = false;
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
            goal.X = MathF.Max(16f, GoalX - 24f);
            goal.Y = MathF.Max(16f, GoalY - 96f);
            goal.Width = MathF.Max(64f, LevelWidth - goal.X - 16f);
            goal.Height = MathF.Max(120f, LevelHeight - goal.Y - 24f);
        }

        _ = playerEntity.AddComponent<PlayerVisual>();
        _ = playerEntity.AddComponent<PlayableHud>();
        _ = playerEntity.AddComponent<PauseMenu>();
        _ = playerEntity.AddComponent<GameUiDemoController>();

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
        if (materialName == "lava")
        {
            emitter.ParticleCount = 1;
            emitter.ParticleSpeed = 18f;
            emitter.ParticleLifetime = 42;
            emitter.LightRadius = 26f;
            emitter.LightIntensity = 0.35f;
        }
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

    private void ClearPlayableArea<TWriter>(ref TWriter writer, int width, int height)
        where TWriter : struct, IWorldWriter
    {
        FillRect(ref writer, 0, 0, width, height, _empty);
    }

    private void BuildBounds<TWriter>(ref TWriter writer, int width, int height)
        where TWriter : struct, IWorldWriter
    {
        FillRect(ref writer, 0, 0, width, 8, _stone);
        FillRect(ref writer, 0, height - 12, width, 12, _stone);
        FillRect(ref writer, 0, 0, 10, height, _stone);
        FillRect(ref writer, width - 10, 0, 10, height, _stone);
    }

    private void BuildTerrain<TWriter>(ref TWriter writer, int width, int height)
        where TWriter : struct, IWorldWriter
    {
        int floorY = height - 72;
        // 横向闯关主路径：左到右的地面、熔岩坑、跳台与必须拆除的路障。
        FillRect(ref writer, 10, floorY, width - 20, height - floorY - 12, _dirt);
        FillRect(ref writer, 10, floorY + 18, width - 20, 16, _stone);
        FillRect(ref writer, 28, floorY - 14, 110, 14, _stone);

        FillRect(ref writer, 138, floorY - 20, 18, 20, _wood);
        FillRect(ref writer, 252, floorY - 34, 22, 34, _stone);
        FillRect(ref writer, 396, floorY - 32, 22, 32, _metal);
        FillRect(ref writer, 514, floorY - 26, 22, 26, _wood);

        FillRect(ref writer, 168, floorY - 42, 82, 10, _wood);
        FillRect(ref writer, 276, floorY - 74, 92, 10, _metal);
        FillRect(ref writer, 410, floorY - 52, 70, 10, _wood);
        FillRect(ref writer, 448, floorY - 36, 92, 10, _wood);
        FillRect(ref writer, 532, floorY - 66, 80, 12, _stone);

        FillRect(ref writer, 98, floorY - 86, 64, 8, _metal);
        FillRect(ref writer, 224, floorY - 122, 58, 8, _wood);
        FillRect(ref writer, 372, floorY - 118, 72, 8, _metal);

        FillRect(ref writer, 188, floorY - 96, 34, 14, _wood);
        FillRect(ref writer, 318, floorY - 32, 42, 12, _stone);
        FillRect(ref writer, 456, floorY - 98, 44, 14, _metal);
        FillRect(ref writer, 138, 108, 12, 96, _stone);
        FillRect(ref writer, 488, floorY - 84, 42, 12, _wood);

        FillSlope(ref writer, 32, floorY - 1, 88, 24, _sand);
        FillSlope(ref writer, 386, floorY - 1, 74, 22, _sand);
    }

    // 把木/金属跳台等区域提升为可破坏刚体，供射击与爆破验证
    private void QueueRigidStructures()
    {
        if (!_worldBuilt || RigidStructuresQueued)
        {
            return;
        }

        int floorY = Math.Max(128, LevelHeight) - 72;
        try
        {
            CreateRigidStructure(138, floorY - 20, 18, 20);
            CreateRigidStructure(252, floorY - 34, 22, 34);
            CreateRigidStructure(396, floorY - 32, 22, 32);
            CreateRigidStructure(514, floorY - 26, 22, 26);
            CreateRigidStructure(168, floorY - 42, 82, 10);
            CreateRigidStructure(276, floorY - 74, 92, 10);
            CreateRigidStructure(410, floorY - 52, 70, 10);
            CreateRigidStructure(98, floorY - 86, 64, 8);
            CreateRigidStructure(224, floorY - 122, 58, 8);
            CreateRigidStructure(372, floorY - 118, 72, 8);
            CreateRigidStructure(188, floorY - 96, 34, 14);
            CreateRigidStructure(456, floorY - 98, 44, 14);
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

    private void BuildHazards<TWriter>(ref TWriter writer, int height)
        where TWriter : struct, IWorldWriter
    {
        int floorY = height - 72;
        FillRect(ref writer, 178, floorY - 2, 58, 24, _lava);
        FillRect(ref writer, 312, floorY - 2, 54, 26, _lava);
        FillRect(ref writer, 452, floorY - 2, 54, 24, _lava);
        FillRect(ref writer, 554, floorY - 2, 28, 20, _lava);
        FillRect(ref writer, 172, floorY + 21, 70, 5, _stone);
        FillRect(ref writer, 306, floorY + 23, 66, 5, _stone);
        FillRect(ref writer, 446, floorY + 21, 66, 5, _stone);
    }

    private void BuildSpawnHazardProbeArea()
    {
        int width = Math.Max(128, LevelWidth);
        int height = Math.Max(128, LevelHeight);
        RuntimeWorldWriter writer = new(Context.Cells, width, height);
        BuildSpawnHazardProbeArea(ref writer);
    }

    private void BuildSpawnHazardProbeArea<TWriter>(ref TWriter writer)
        where TWriter : struct, IWorldWriter
    {
        if (!BuildSpawnHazardProbe)
        {
            return;
        }

        int x = (int)MathF.Floor(PlayerSpawnX);
        int y = (int)MathF.Floor(PlayerSpawnY);
        FillRect(ref writer, x, y, 8, 14, _lava);
    }

    private void BuildGoalMarker<TWriter>(ref TWriter writer)
        where TWriter : struct, IWorldWriter
    {
        int x = (int)MathF.Round(GoalX);
        int y = (int)MathF.Round(GoalY);
        FillRect(ref writer, x - 4, y, 4, 54, _wood);
        FillRect(ref writer, x, y, 34, 5, _sand);
        FillRect(ref writer, x, y + 49, 34, 5, _sand);
        FillRect(ref writer, x + 29, y, 5, 54, _sand);
    }

    private static void FillRect<TWriter>(
        ref TWriter writer,
        int x,
        int y,
        int width,
        int height,
        MaterialId material)
        where TWriter : struct, IWorldWriter
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        writer.FillRect(x, y, width, height, material);
    }

    private static void FillSlope<TWriter>(
        ref TWriter writer,
        int x,
        int baseY,
        int width,
        int height,
        MaterialId material)
        where TWriter : struct, IWorldWriter
    {
        for (int cx = 0; cx < width; cx++)
        {
            int columnHeight = Math.Max(1, (int)MathF.Round((1f - (cx / (float)Math.Max(1, width - 1))) * height));
            for (int cy = 0; cy < columnHeight; cy++)
            {
                writer.SetCell(x + cx, baseY - cy, material);
            }
        }
    }

    private ulong ComputeAuthoringContentHash(int width, int height)
    {
        const ulong offset = 14695981039346656037UL;
        ulong hash = offset;
        hash = MixHash(hash, (uint)width);
        hash = MixHash(hash, (uint)height);
        hash = MixHash(hash, BitConverter.SingleToUInt32Bits(PlayerSpawnX));
        hash = MixHash(hash, BitConverter.SingleToUInt32Bits(PlayerSpawnY));
        hash = MixHash(hash, BitConverter.SingleToUInt32Bits(GoalX));
        hash = MixHash(hash, BitConverter.SingleToUInt32Bits(GoalY));
        hash = MixHash(hash, BuildSpawnHazardProbe ? 1u : 0u);
        return hash;
    }

    private static ulong MixHash(ulong hash, uint value)
    {
        const ulong prime = 1099511628211UL;
        for (int shift = 0; shift < 32; shift += 8)
        {
            hash ^= (byte)(value >> shift);
            hash *= prime;
        }

        return hash;
    }

    private interface IWorldWriter
    {
        void SetCell(int x, int y, MaterialId material);

        void FillRect(int x, int y, int width, int height, MaterialId material);
    }

    private readonly struct RuntimeWorldWriter(
        IWorldCellAccess cells,
        int width,
        int height) : IWorldWriter
    {
        private readonly IWorldCellAccess _cells = cells ?? throw new ArgumentNullException(nameof(cells));
        private readonly int _width = width > 0 ? width : throw new ArgumentOutOfRangeException(nameof(width));
        private readonly int _height = height > 0 ? height : throw new ArgumentOutOfRangeException(nameof(height));

        public void SetCell(int x, int y, MaterialId material)
        {
            if ((uint)x < (uint)_width && (uint)y < (uint)_height)
            {
                _cells.SetCell(x, y, material);
            }
        }

        public void FillRect(int x, int y, int width, int height, MaterialId material)
        {
            int minX = Math.Max(0, x);
            int minY = Math.Max(0, y);
            int maxX = (int)Math.Min(_width, (long)x + width);
            int maxY = (int)Math.Min(_height, (long)y + height);
            for (int cy = minY; cy < maxY; cy++)
            {
                for (int cx = minX; cx < maxX; cx++)
                {
                    _cells.SetCell(cx, cy, material);
                }
            }
        }
    }

    private readonly struct AuthoringWorldWriter(
        IAuthoringWorldEditApi edit,
        int width,
        int height) : IWorldWriter
    {
        private readonly IAuthoringWorldEditApi _edit = edit ?? throw new ArgumentNullException(nameof(edit));
        private readonly int _width = width > 0 ? width : throw new ArgumentOutOfRangeException(nameof(width));
        private readonly int _height = height > 0 ? height : throw new ArgumentOutOfRangeException(nameof(height));

        public void SetCell(int x, int y, MaterialId material)
        {
            if ((uint)x < (uint)_width && (uint)y < (uint)_height)
            {
                _edit.PaintCell(x, y, material);
            }
        }

        public void FillRect(int x, int y, int width, int height, MaterialId material)
        {
            int minX = Math.Max(0, x);
            int minY = Math.Max(0, y);
            int maxX = (int)Math.Min(_width, (long)x + width);
            int maxY = (int)Math.Min(_height, (long)y + height);
            if (minX < maxX && minY < maxY)
            {
                _ = _edit.PaintRect(minX, minY, maxX - 1, maxY - 1, material);
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
