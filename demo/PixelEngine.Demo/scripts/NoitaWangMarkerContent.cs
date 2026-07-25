using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 将 Noita Wang tile 中的 spawn/load marker 以 Demo 自有的图形/VFX prop 形式 materialize。
/// 只使用公开脚本 API 创建实体、overlay 和点光源，不写入 cell 网格，避免重新制造悬浮残留碎点。
/// </summary>
internal sealed class NoitaWangMarkerContentSystem : ISystem
{
    private const float ScanIntervalSeconds = 0.35f;
    private const int ScanHalfWidthCells = 512;
    private const int ScanHalfHeightCells = 384;
    private const int MaxAnchorsPerScan = 96;
    private const int MaxMaterializedMarkers = 128;
    internal const float GameplayWarmupSeconds = 0.5f;

    private readonly NoitaWangMarkerAnchor[] _anchors = new NoitaWangMarkerAnchor[MaxAnchorsPerScan];
    private readonly MarkerKey[] _materialized = new MarkerKey[MaxMaterializedMarkers];
    private readonly Entity?[] _propEntities = new Entity?[MaxMaterializedMarkers];
    private readonly Entity?[] _gameplayEntities = new Entity?[MaxMaterializedMarkers];
    private readonly Behaviour?[] _gameplayComponents = new Behaviour?[MaxMaterializedMarkers];
    private CampaignConfig? _config;
    private BiomeCatalog? _biomes;
    private NoitaWangTerrainCatalog? _wangTerrain;
    private PlayerController? _player;
    private CampaignRunDirector? _runDirector;
    private float _scanTimer;
    private float _gameplayElapsed;

    /// <summary>已经创建为场景 prop 的 marker 数量。</summary>
    public int MaterializedCount { get; private set; }

    /// <summary>最近一次扫描得到的 Wang marker 数量。</summary>
    public int LastScanAnchorCount { get; private set; }

    /// <summary>最近一次扫描中新创建的 prop 数量。</summary>
    public int LastScanMaterializedCount { get; private set; }

    public void OnSimTick(IScriptContext context)
    {
        _ = context;
    }

    public void OnFrame(IScriptContext context, float dt)
    {
        ArgumentNullException.ThrowIfNull(context);
        float safeDt = float.IsFinite(dt) && dt > 0f ? dt : 0f;
        if (!ResolveInputs(context))
        {
            _gameplayElapsed = 0f;
            LastScanAnchorCount = 0;
            LastScanMaterializedCount = 0;
            return;
        }

        bool gameplayActive = IsMarkerGameplayState(_runDirector!.State);
        _gameplayElapsed = AdvanceGameplayElapsed(_gameplayElapsed, _runDirector.State, safeDt);
        _scanTimer -= safeDt;
        if (_scanTimer > 0f)
        {
            return;
        }

        _scanTimer = ScanIntervalSeconds;
        SetGameplayEmittersEnabled(gameplayActive);
        if (!gameplayActive)
        {
            LastScanAnchorCount = 0;
            LastScanMaterializedCount = 0;
            return;
        }

        if (_gameplayElapsed < GameplayWarmupSeconds)
        {
            LastScanAnchorCount = 0;
            LastScanMaterializedCount = 0;
            return;
        }

        PlayerController player = _player!;
        CampaignConfig config = _config!;
        BiomeCatalog biomes = _biomes!;
        NoitaWangTerrainCatalog wangTerrain = _wangTerrain!;
        ulong worldSeed = _runDirector?.RunSeed ?? config.InitialRunSeed;
        long centerX = (long)MathF.Round(player.CenterX);
        long centerY = (long)MathF.Round(player.CenterY);
        int count = PlayableCavernWorldGenerator.CollectGlobalPixelSceneMarkerAnchors(
            centerX - ScanHalfWidthCells,
            centerY - ScanHalfHeightCells,
            centerX + ScanHalfWidthCells,
            centerY + ScanHalfHeightCells,
            _anchors);
        count += PlayableCavernWorldGenerator.CollectWangMarkerAnchors(
            biomes,
            wangTerrain,
            config,
            worldSeed,
            centerX - ScanHalfWidthCells,
            centerY - ScanHalfHeightCells,
            centerX + ScanHalfWidthCells,
            centerY + ScanHalfHeightCells,
            _anchors.AsSpan(count));
        LastScanAnchorCount = count;
        LastScanMaterializedCount = 0;
        EvictMarkersOutsideCurrentScan(_anchors.AsSpan(0, count));
        for (int i = 0; i < count; i++)
        {
            ref readonly NoitaWangMarkerAnchor anchor = ref _anchors[i];
            if (!NoitaWangMarkerVisualProfile.TryCreate(anchor, out NoitaWangMarkerVisualProfile profile))
            {
                continue;
            }

            MarkerKey key = MarkerKey.From(anchor);
            if (ContainsMaterialized(key) || MaterializedCount >= _materialized.Length)
            {
                continue;
            }

            int slot = MaterializedCount++;
            _materialized[slot] = key;
            Entity entity = context.Scene.CreateEntity();
            _propEntities[slot] = entity;
            NoitaWangMarkerProp prop = entity.AddComponent<NoitaWangMarkerProp>();
            prop.Bind(anchor, profile);
            CreateGameplayMarkerEntity(context, anchor, profile, slot);
            LastScanMaterializedCount++;
            TransientParticleBurst.Emit(
                context,
                anchor.WorldX,
                anchor.WorldY,
                profile.BurstCount,
                profile.BurstSpeed,
                lifetime: 72,
                profile.CoreColorBgra,
                profile.TrailColorBgra,
                profile.LightIntensity);
        }
    }

    private bool ResolveInputs(IScriptContext context)
    {
        _config ??= CampaignConfig.Load(context.Config);
        _biomes ??= BiomeCatalog.Load(context.Config, _config);
        _wangTerrain ??= NoitaWangTerrainCatalog.Load(context.Config);
        if (_player is null || !_player.Enabled)
        {
            _ = context.Scene.TryGetFirstComponent(out _player);
        }

        if (_runDirector is null || !_runDirector.Enabled)
        {
            _ = context.Scene.TryGetFirstComponent(out _runDirector);
        }

        return _player is not null && _runDirector is not null;
    }

    internal static bool IsMarkerGameplayState(CampaignRunState state)
    {
        return state is CampaignRunState.Exploring or
            CampaignRunState.HolyMountain or
            CampaignRunState.Laboratory;
    }

    internal static float AdvanceGameplayElapsed(float elapsed, CampaignRunState state, float dt)
    {
        if (!IsMarkerGameplayState(state))
        {
            return 0f;
        }

        float safeElapsed = float.IsFinite(elapsed) && elapsed > 0f ? elapsed : 0f;
        float safeDt = float.IsFinite(dt) && dt > 0f ? dt : 0f;
        return MathF.Min(GameplayWarmupSeconds, safeElapsed + safeDt);
    }

    private void CreateGameplayMarkerEntity(
        IScriptContext context,
        in NoitaWangMarkerAnchor anchor,
        in NoitaWangMarkerVisualProfile profile,
        int slot)
    {
        if (profile.GameplayKind == NoitaWangMarkerGameplayKind.MaterialEmitter)
        {
            Entity entity = context.Scene.CreateEntity();
            MaterialEmitter emitter = entity.AddComponent<MaterialEmitter>();
            emitter.X = anchor.WorldX;
            emitter.Y = anchor.WorldY;
            emitter.MaterialName = profile.GameplayMaterialName;
            emitter.Radius = 1;
            emitter.IntervalSeconds = 1.35f;
            emitter.ParticleCount = Math.Clamp(profile.BurstCount / 2, 3, 12);
            emitter.ParticleSpeed = Math.Max(18f, profile.BurstSpeed);
            emitter.ParticleLifetime = 84;
            emitter.DirectionY = 0.9f;
            emitter.EmitOnStart = true;
            emitter.AudioCooldownSeconds = 1.25f;
            emitter.AddLight = true;
            emitter.LightRadius = profile.LightRadiusCells;
            emitter.LightColorBgra = profile.CoreColorBgra;
            emitter.LightIntensity = Math.Clamp(profile.LightIntensity, 0.15f, 0.75f);
            _gameplayEntities[slot] = entity;
            _gameplayComponents[slot] = emitter;
            return;
        }

        if (profile.GameplayKind == NoitaWangMarkerGameplayKind.SparkEmitter)
        {
            Entity entity = context.Scene.CreateEntity();
            SparkEmitter spark = entity.AddComponent<SparkEmitter>();
            spark.X = anchor.WorldX;
            spark.Y = anchor.WorldY;
            spark.MaterialName = profile.GameplayMaterialName;
            spark.Count = Math.Clamp(profile.BurstCount / 2, 3, 10);
            spark.Speed = Math.Max(24f, profile.BurstSpeed * 1.4f);
            spark.IntervalSeconds = 0.22f;
            spark.Lifetime = 66;
            spark.Spread = 0.95f;
            _gameplayEntities[slot] = entity;
            _gameplayComponents[slot] = spark;
        }
    }

    private void SetGameplayEmittersEnabled(bool enabled)
    {
        for (int i = 0; i < MaterializedCount; i++)
        {
            if (_gameplayComponents[i] is { } component)
            {
                component.Enabled = enabled;
            }
        }
    }

    private void EvictMarkersOutsideCurrentScan(ReadOnlySpan<NoitaWangMarkerAnchor> anchors)
    {
        int slot = 0;
        while (slot < MaterializedCount)
        {
            if (ContainsAnchor(anchors, _materialized[slot]))
            {
                slot++;
                continue;
            }

            _propEntities[slot]!.Destroy();
            _gameplayEntities[slot]?.Destroy();
            int last = --MaterializedCount;
            if (slot != last)
            {
                _materialized[slot] = _materialized[last];
                _propEntities[slot] = _propEntities[last];
                _gameplayEntities[slot] = _gameplayEntities[last];
                _gameplayComponents[slot] = _gameplayComponents[last];
            }

            _materialized[last] = default;
            _propEntities[last] = null;
            _gameplayEntities[last] = null;
            _gameplayComponents[last] = null;
        }
    }

    private static bool ContainsAnchor(ReadOnlySpan<NoitaWangMarkerAnchor> anchors, MarkerKey key)
    {
        for (int i = 0; i < anchors.Length; i++)
        {
            if (MarkerKey.From(anchors[i]).Equals(key))
            {
                return true;
            }
        }

        return false;
    }

    private bool ContainsMaterialized(MarkerKey key)
    {
        for (int i = 0; i < MaterializedCount; i++)
        {
            if (_materialized[i].Equals(key))
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct MarkerKey(long WorldX, long WorldY, byte Semantic)
    {
        public static MarkerKey From(in NoitaWangMarkerAnchor anchor)
        {
            return new MarkerKey(anchor.WorldX, anchor.WorldY, anchor.Semantic);
        }
    }
}

/// <summary>
/// 单个已 materialize 的 Noita marker prop。它是原创 overlay/VFX 表达，
/// 后续可按同一 anchor 替换为怪物、箱子、背景 pixel-scene 或陷阱实体。
/// </summary>
internal sealed class NoitaWangMarkerProp : Behaviour
{
    private NoitaWangMarkerVisualProfile _profile;
    private float _elapsed;

    public long WorldX { get; private set; }

    public long WorldY { get; private set; }

    public string Function { get; private set; } = string.Empty;

    public NoitaWangMarkerVisualKind Kind => _profile.Kind;

    public void Bind(in NoitaWangMarkerAnchor anchor, in NoitaWangMarkerVisualProfile profile)
    {
        WorldX = anchor.WorldX;
        WorldY = anchor.WorldY;
        Function = anchor.Function;
        _profile = profile;
    }

    protected override void OnUpdate(float dt)
    {
        float safeDt = float.IsFinite(dt) && dt > 0f ? dt : 0f;
        _elapsed += safeDt;
        DrawMarker();
    }

    private void DrawMarker()
    {
        Point2F center = Context.Camera.WorldToScreen(WorldX, WorldY);
        float scale = MathF.Max(1f, Context.Camera.Zoom);
        float pulse = 0.72f + (MathF.Sin(_elapsed * 3.4f) * 0.18f);
        float size = MathF.Max(5f, scale * _profile.SizeCells);
        uint core = ScaleAlpha(_profile.CoreColorBgra, pulse);
        uint trail = ScaleAlpha(_profile.TrailColorBgra, 0.65f + (pulse * 0.25f));
        float half = size * 0.5f;

        if (_profile.Kind == NoitaWangMarkerVisualKind.Treasure)
        {
            Context.Overlay.SolidRectangle(center.X - half, center.Y - (half * 0.65f), size, size * 0.72f, core);
            Context.Overlay.OutlineRectangle(center.X - half, center.Y - (half * 0.65f), size, size * 0.72f, 1.5f, trail);
            Context.Overlay.Line(center.X - half, center.Y, center.X + half, center.Y, 1.2f, trail);
        }
        else if (_profile.Kind == NoitaWangMarkerVisualKind.Plant)
        {
            Context.Overlay.Line(center.X, center.Y - size, center.X, center.Y + half, 2f, trail);
            Context.Overlay.Line(center.X, center.Y - half, center.X - half, center.Y, 1.5f, core);
            Context.Overlay.Line(center.X, center.Y - (half * 0.35f), center.X + half, center.Y + (half * 0.1f), 1.5f, core);
        }
        else if (_profile.Kind == NoitaWangMarkerVisualKind.Machine)
        {
            Context.Overlay.OutlineRectangle(center.X - half, center.Y - half, size, size, 1.5f, trail);
            Context.Overlay.Line(center.X - half, center.Y - half, center.X + half, center.Y + half, 1.4f, core);
            Context.Overlay.Line(center.X + half, center.Y - half, center.X - half, center.Y + half, 1.4f, core);
        }
        else
        {
            Context.Overlay.SolidRectangle(center.X - (half * 0.55f), center.Y - (half * 0.55f), size * 0.55f, size * 0.55f, core);
            Context.Overlay.OutlineRectangle(center.X - half, center.Y - half, size, size, 1.25f, trail);
        }

        Context.Lighting.AddPointLight(
            WorldX,
            WorldY,
            _profile.LightRadiusCells * (0.88f + (pulse * 0.18f)),
            _profile.CoreColorBgra,
            _profile.LightIntensity * pulse);
    }

    private static uint ScaleAlpha(uint bgra, float multiplier)
    {
        byte alpha = (byte)(bgra >> 24);
        byte scaled = (byte)Math.Clamp((int)MathF.Round(alpha * Math.Clamp(multiplier, 0f, 1.25f)), 0, byte.MaxValue);
        return (bgra & 0x00_FF_FF_FFu) | ((uint)scaled << 24);
    }
}

internal enum NoitaWangMarkerVisualKind : byte
{
    SceneLoad,
    Spawn,
    Plant,
    Machine,
    Treasure,
    Hazard,
}

internal enum NoitaWangMarkerGameplayKind : byte
{
    None,
    SparkEmitter,
    MaterialEmitter,
}

internal readonly record struct NoitaWangMarkerVisualProfile(
    NoitaWangMarkerVisualKind Kind,
    uint CoreColorBgra,
    uint TrailColorBgra,
    float SizeCells,
    float LightRadiusCells,
    float LightIntensity,
    int BurstCount,
    float BurstSpeed,
    NoitaWangMarkerGameplayKind GameplayKind,
    string GameplayMaterialName)
{
    public static bool TryCreate(in NoitaWangMarkerAnchor anchor, out NoitaWangMarkerVisualProfile profile)
    {
        if (NoitaSnowcastlePixelSceneCatalog.Supports(anchor) || NoitaRandomPixelSceneCatalog.Supports(anchor))
        {
            profile = default;
            return false;
        }

        string function = anchor.Function;
        if (function.StartsWith("builtin-or-unresolved", StringComparison.Ordinal) ||
            !NoitaMarkerRuleCatalog.TryResolve(function, out NoitaMarkerRule rule))
        {
            profile = default;
            return false;
        }

        // 来源中的 Vegetation marker 实际混合 Verlet chain、刚体、敌人、产怪建筑与静态植物。
        // 在对应 C# 运行时逐类实现前不得继续用同一个绿色 overlay/SparkEmitter 冒充。
        if (rule.Kind == NoitaMarkerRuleKind.Vegetation)
        {
            profile = default;
            return false;
        }

        profile = rule.Kind switch
        {
            NoitaMarkerRuleKind.Vegetation => throw new InvalidOperationException(
                "Vegetation marker 必须在 profile switch 前抑制。"),
            NoitaMarkerRuleKind.Loot => new NoitaWangMarkerVisualProfile(
                NoitaWangMarkerVisualKind.Treasure,
                0xFF_28_D8_FF,
                0xCC_50_7A_FF,
                8f,
                42f,
                0.72f,
                12,
                32f,
                NoitaWangMarkerGameplayKind.SparkEmitter,
                "crystal"),
            NoitaMarkerRuleKind.Prop => CreatePropProfile(function),
            NoitaMarkerRuleKind.PixelScene => new NoitaWangMarkerVisualProfile(
                NoitaWangMarkerVisualKind.SceneLoad,
                0xFF_F0_7C_FF,
                0xCC_80_44_D0,
                7f,
                38f,
                0.55f,
                9,
                26f,
                NoitaWangMarkerGameplayKind.None,
                string.Empty),
            NoitaMarkerRuleKind.Encounter => new NoitaWangMarkerVisualProfile(
                NoitaWangMarkerVisualKind.Spawn,
                0xFF_70_A8_FF,
                0xCC_38_58_A0,
                8f,
                38f,
                0.54f,
                10,
                28f,
                NoitaWangMarkerGameplayKind.SparkEmitter,
                "fire"),
            NoitaMarkerRuleKind.Trigger => new NoitaWangMarkerVisualProfile(
                NoitaWangMarkerVisualKind.Machine,
                0xFF_E8_D8_58,
                0xCC_88_70_20,
                8f,
                40f,
                0.58f,
                9,
                25f,
                NoitaWangMarkerGameplayKind.SparkEmitter,
                "crystal"),
            NoitaMarkerRuleKind.Effect => new NoitaWangMarkerVisualProfile(
                NoitaWangMarkerVisualKind.Spawn,
                0xFF_F0_E8_B0,
                0xCC_A0_90_50,
                7f,
                34f,
                0.48f,
                7,
                22f,
                NoitaWangMarkerGameplayKind.SparkEmitter,
                "fire"),
            _ => throw new InvalidOperationException($"未知 Noita marker rule kind：{rule.Kind}。"),
        };
        return true;
    }

    private static NoitaWangMarkerVisualProfile CreatePropProfile(string function)
    {
        bool hazard = Contains(function, "acid") || Contains(function, "gunpowder") || Contains(function, "oil") ||
            Contains(function, "tank") || Contains(function, "laser") || Contains(function, "trap");
        return hazard
            ? new NoitaWangMarkerVisualProfile(
                NoitaWangMarkerVisualKind.Hazard,
                0xFF_42_FF_B8,
                0xCC_30_66_D8,
                8f,
                46f,
                0.68f,
                14,
                38f,
                NoitaWangMarkerGameplayKind.MaterialEmitter,
                HazardMaterialName(function))
            : new NoitaWangMarkerVisualProfile(
                NoitaWangMarkerVisualKind.Machine,
                0xFF_FF_B0_58,
                0xCC_C0_50_20,
                8f,
                40f,
                0.62f,
                10,
                28f,
                NoitaWangMarkerGameplayKind.SparkEmitter,
                "fire");
    }

    private static string HazardMaterialName(string function)
    {
        return Contains(function, "oil")
            ? "oil"
            : Contains(function, "gunpowder") || Contains(function, "laser")
                ? "fire"
                : "acid";
    }

    private static bool Contains(string value, string pattern)
    {
        return value.Contains(pattern, StringComparison.Ordinal);
    }
}
