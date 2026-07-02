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
    public float PlayerSpawnY { get; set; } = 292f;

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

        Entity playerEntity = Context.Scene.CreateEntity();
        _ = playerEntity.AddComponent<Transform>();
        PlayerController player = playerEntity.AddComponent<PlayerController>();
        player.SpawnX = PlayerSpawnX;
        player.SpawnY = PlayerSpawnY;
        player.MaxRunSpeed = 150f;
        player.JumpSpeed = 235f;

        PlayerHealth health = playerEntity.AddComponent<PlayerHealth>();
        health.LavaDamagePerSecond = 40f;
        health.AcidDamagePerSecond = 35f;

        CameraFollow camera = playerEntity.AddComponent<CameraFollow>();
        camera.MinX = 0f;
        camera.MinY = 0f;
        camera.MaxX = 1536f;
        camera.MaxY = 384f;
        camera.Zoom = 3f;

        PlayableProjectileTool projectile = playerEntity.AddComponent<PlayableProjectileTool>();
        projectile.ImpactRadius = 9;
        projectile.ImpactForce = 24f;

        _ = playerEntity.AddComponent<PlayableHud>();
        _ = playerEntity.AddComponent<PauseMenu>();
        _entitiesBuilt = true;
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
