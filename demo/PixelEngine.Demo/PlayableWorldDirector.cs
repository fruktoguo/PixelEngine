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
        Context.Lighting.RevealViewport();
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
        player.MaxRunSpeed = 120f;
        player.GroundAcceleration = 1_650f;
        player.AirAcceleration = 1_050f;
        player.GroundFriction = 2_800f;
        player.AirFriction = 180f;
        player.JumpSpeed = 245f;
        player.Gravity = 860f;

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

        _ = playerEntity.AddComponent<PlayerVisual>();
        _ = playerEntity.AddComponent<PlayableHud>();
        _ = playerEntity.AddComponent<PauseMenu>();
        _ = playerEntity.AddComponent<GameUiDemoController>();
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
