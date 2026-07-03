using PixelEngine.Scripting;

namespace PixelEngine.Demo;

/// <summary>
/// 可玩程序化 Demo 的脚本入口，装配玩家、相机、射击工具与简洁 HUD。
/// </summary>
public sealed class PlayableWorldDirector : Behaviour
{
    private bool _entitiesBuilt;
    private bool _entityBuildSystemRegistered;

    /// <summary>
    /// 玩家出生点 X 坐标。
    /// </summary>
    public float PlayerSpawnX { get; set; } = 72f;

    /// <summary>
    /// 玩家出生点 Y 坐标。
    /// </summary>
    public float PlayerSpawnY { get; set; } = 172f;

    /// <inheritdoc />
    protected override void OnStart()
    {
        RegisterEntityBuildSystem();
    }

    /// <inheritdoc />
    protected override void OnUpdate(float dt)
    {
        _ = dt;
        RegisterEntityBuildSystem();
    }

    private void RegisterEntityBuildSystem()
    {
        if (_entitiesBuilt || _entityBuildSystemRegistered)
        {
            return;
        }

        Context.Scene.RegisterSystem(new EntityBuildSystem(this));
        _entityBuildSystemRegistered = true;
    }

    private void BuildEntities()
    {
        if (_entitiesBuilt)
        {
            return;
        }

        DisableDebugOverlaysForPlayableScene();

        Entity playerEntity = Context.Scene.CreateEntity();
        _ = playerEntity.AddComponent<Transform>();
        PlayerController player = playerEntity.AddComponent<PlayerController>();
        player.SpawnX = PlayerSpawnX;
        player.SpawnY = PlayerSpawnY;
        player.MaxRunSpeed = 145f;
        player.GroundAcceleration = 1_850f;
        player.AirAcceleration = 1_150f;
        player.GroundFriction = 3_100f;
        player.AirFriction = 220f;
        player.JumpSpeed = 250f;
        player.Gravity = 900f;

        PlayerHealth health = playerEntity.AddComponent<PlayerHealth>();
        health.LavaDamagePerSecond = 40f;
        health.AcidDamagePerSecond = 35f;

        CameraFollow camera = playerEntity.AddComponent<CameraFollow>();
        camera.MinX = 0f;
        camera.MinY = 0f;
        camera.MaxX = 1536f;
        camera.MaxY = 384f;
        camera.Zoom = 2f;

        PlayableProjectileTool projectile = playerEntity.AddComponent<PlayableProjectileTool>();
        projectile.ImpactRadius = 5;
        projectile.ImpactForce = 12f;
        projectile.CollapseScanRadius = 72;
        projectile.FallbackOverhangRadius = 24;
        projectile.MaxCollapseRegionSize = 48;
        projectile.MaxCollapsePixels = 160;
        projectile.MaxCollapsedIslandsPerShot = 1;
        projectile.PlayerSupportProtectionRadius = 48;

        _ = playerEntity.AddComponent<PlayerVisual>();
        _ = playerEntity.AddComponent<PlayableHud>();
        _ = playerEntity.AddComponent<PauseMenu>();
        _entitiesBuilt = true;
    }

    private void DisableDebugOverlaysForPlayableScene()
    {
        Context.Diagnostics.SetOverlay(DebugOverlayKind.DirtyRects, enabled: false);
        Context.Diagnostics.SetOverlay(DebugOverlayKind.CaIterationRects, enabled: false);
        Context.Diagnostics.SetOverlay(DebugOverlayKind.ChunkGridParity, enabled: false);
        Context.Diagnostics.SetOverlay(DebugOverlayKind.KeepAliveHotspots, enabled: false);
        Context.Diagnostics.SetOverlay(DebugOverlayKind.CellParity, enabled: false);
        Context.Diagnostics.SetOverlay(DebugOverlayKind.TemperatureHeatmap, enabled: false);
        Context.Diagnostics.SetOverlay(DebugOverlayKind.OwnedByBody, enabled: false);
        Context.Diagnostics.SetOverlay(DebugOverlayKind.ParticleTrails, enabled: false);
        Context.Diagnostics.SetOverlay(DebugOverlayKind.ConnectedComponents, enabled: false);
    }

    private sealed class EntityBuildSystem(PlayableWorldDirector director) : ISystem
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
