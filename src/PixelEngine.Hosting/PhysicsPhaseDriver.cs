using PixelEngine.Physics;
using PixelEngine.Scripting;
using PixelEngine.Simulation;

namespace PixelEngine.Hosting;

/// <summary>
/// 将真实 PhysicsSystem 接入 Hosting 相位 8。
/// </summary>
/// <param name="physics">真实 PhysicsSystem。</param>
/// <param name="chunks">驻留 chunk 源；提供时相位 8 会同步局部静态地形 collider。</param>
public sealed class PhysicsPhaseDriver(PhysicsSystem physics, IChunkSource? chunks = null) : IEnginePhaseDriver
{
    private readonly PhysicsSystem _physics = physics ?? throw new ArgumentNullException(nameof(physics));
    private readonly IChunkSource? _chunks = chunks;

    /// <summary>
    /// 注册相位 8 的物理同步 hook。
    /// </summary>
    /// <param name="phases">Hosting 相位管线。</param>
    public void RegisterPhases(EnginePhasePipeline phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        phases.Register(EnginePhase.PhysicsSync, RunPhysicsSync);
    }

    private void RunPhysicsSync(EngineTickContext context)
    {
        if (!context.Timing.RunPhysics)
        {
            return;
        }

        if (context.Context.TryGetService(out ScriptSimulationContext scripts))
        {
            _ = scripts.FlushPhysicsCommands();
        }

        if (_chunks is not null)
        {
            _physics.UpdateStaticTerrainColliders(_chunks);
        }

        _physics.SyncStep((float)context.Timing.Dt);
        RigidDestructionResult destruction = _physics.LastDestructionResult;
        context.Context.Counters.RigidBodies = _physics.PhysicsWorld.ActiveBodyCount;
        context.Context.Counters.RigidBodiesDestroyedThisTick = destruction.DestroyedBodies;
        context.Context.Counters.RigidBodiesCreatedThisTick = destruction.CreatedBodies;
        if (context.Context.TryGetService(out PhysicsStepEventBus physicsEvents))
        {
            physicsEvents.PublishPostStep(_physics.LastCharacterProxyContactCount);
        }
    }
}
