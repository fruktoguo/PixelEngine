using PixelEngine.Hosting;
using PixelEngine.Physics;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using PixelEngine.Scripting;
using Xunit;
using ScriptScene = PixelEngine.Scripting.Scene;

namespace PixelEngine.Demo.Tests;

/// <summary>
/// Demo lava-mine 场景端到端装配测试。
/// </summary>
public sealed class LavaMineSceneTests
{
    /// <summary>
    /// 验证默认关卡会通过脚本公开刚体 API 把木桥与金属梁注册为动态刚体。
    /// </summary>
    [Fact]
    public async Task LavaMineSceneRegistersDestructibleWoodAndMetalStructures()
    {
        string root = FindRepositoryRoot();
        DemoStartupOptions options = new()
        {
            Headless = true,
            HeadlessTicks = 2,
            HotReloadEnabled = false,
            ContentRoot = Path.Combine(root, "demo", "PixelEngine.Demo", "content"),
            Scene = Path.Combine("scenes", "lava-mine.scene"),
        };
        EngineProject project = DemoProgram.BuildProject(options);
        using Engine engine = DemoProgram.BuildEngine(options, project);

        _ = engine.LoadContentPackage();
        Assert.Null(engine.AttachCurrentSceneWorld());
        _ = engine.AttachResidentSimulationWorld(worldWidthCells: 640, worldHeightCells: 360);
        _ = engine.AttachPhysics();
        _ = await engine.AttachAudioFromContentAsync();
        engine.RegisterScriptAssembly(typeof(DemoProgram).Assembly);
        _ = engine.AttachScriptingFromServices();

        engine.RunHeadlessTicks(options.HeadlessTicks);

        LevelDirector director = FindBehaviour<LevelDirector>(engine.Context.GetService<ScriptScene>());
        Assert.True(director.RigidStructuresQueued);
        Assert.Equal(6, director.RigidStructureCount);
        Assert.True(CountBehaviours<SparkEmitter>(engine.Context.GetService<ScriptScene>()) >= 2);
        ParticleSystem particles = engine.Context.GetService<ParticleSystem>();
        Assert.True(particles.ActiveCount > 0);
        PhysicsSystem physics = engine.Context.GetService<PhysicsSystem>();
        Assert.True(physics.PhysicsWorld.ActiveBodyCount >= director.RigidStructureCount);
        Assert.True(physics.LastStampedCellCount > 0);

        CellGrid grid = engine.Context.GetService<CellGrid>();
        RigidDamageQueue damageQueue = engine.Context.GetService<RigidDamageQueue>();
        int beforeCutBodies = physics.PhysicsWorld.ActiveBodyCount;
        QueueVerticalCut(grid, damageQueue, x: 209, minY: 246, maxY: 256);

        engine.RunHeadlessTicks(1);

        Assert.Equal(1, physics.LastDestructionResult.DestroyedBodies);
        Assert.Equal(2, physics.LastDestructionResult.CreatedBodies);
        Assert.True(physics.PhysicsWorld.ActiveBodyCount > beforeCutBodies);
    }

    private static TBehaviour FindBehaviour<TBehaviour>(ScriptScene scene)
        where TBehaviour : Behaviour
    {
        foreach (ScriptEntityInspection entity in scene.CaptureInspectionSnapshot())
        {
            foreach (ScriptComponentInspection component in entity.Components)
            {
                if (component.Behaviour is TBehaviour behaviour)
                {
                    return behaviour;
                }
            }
        }

        throw new InvalidOperationException($"未找到 Behaviour：{typeof(TBehaviour).FullName}。");
    }

    private static int CountBehaviours<TBehaviour>(ScriptScene scene)
        where TBehaviour : Behaviour
    {
        int count = 0;
        foreach (ScriptEntityInspection entity in scene.CaptureInspectionSnapshot())
        {
            foreach (ScriptComponentInspection component in entity.Components)
            {
                if (component.Behaviour is TBehaviour)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static void QueueVerticalCut(CellGrid grid, RigidDamageQueue damageQueue, int x, int minY, int maxY)
    {
        for (int y = minY; y < maxY; y++)
        {
            if (!CellFlags.Has(grid.FlagsAt(x, y), CellFlags.RigidOwned))
            {
                continue;
            }

            grid.MaterialAt(x, y) = 0;
            grid.FlagsAt(x, y) = 0;
            grid.LifetimeAt(x, y) = 0;
            damageQueue.OnOwnedCellDamaged(x, y);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PixelEngine.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("无法从测试输出目录定位 PixelEngine.sln。");
    }
}
