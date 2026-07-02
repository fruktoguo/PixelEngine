using System.Numerics;
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
        using Engine engine = await CreateLavaMineEngineAsync();
        engine.RunHeadlessTicks(2);

        LevelDirector director = FindBehaviour<LevelDirector>(engine.Context.GetService<ScriptScene>());
        Assert.True(director.RigidStructuresQueued);
        Assert.Equal(6, director.RigidStructureCount);
        _ = FindBehaviour<DemoHud>(engine.Context.GetService<ScriptScene>());
        _ = FindBehaviour<PauseMenu>(engine.Context.GetService<ScriptScene>());
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

        Assert.True(physics.LastDestructionResult.DestroyedBodies >= 1);
        Assert.True(physics.LastDestructionResult.CreatedBodies >= 2);
        Assert.True(physics.PhysicsWorld.ActiveBodyCount > beforeCutBodies);

        RigidBodySnapshot[] splitSnapshots = CopySnapshots(physics);
        Assert.True(splitSnapshots.Length >= beforeCutBodies + 1);
        Assert.Contains(splitSnapshots, snapshot => snapshot.AngularVelocityRadiansPerSecond != 0f);

        engine.RunHeadlessTicks(6);

        RigidBodySnapshot[] movedSnapshots = CopySnapshots(physics);
        Assert.True(physics.LastStampedCellCount > 0);
        Assert.Contains(
            movedSnapshots,
            moved => TryFindSnapshot(splitSnapshots, moved.BodyKey, out RigidBodySnapshot before) &&
                (Vector2.Distance(before.Transform.Position, moved.Transform.Position) > 0.01f ||
                    MathF.Abs(before.Transform.Sin - moved.Transform.Sin) > 0.0001f ||
                    MathF.Abs(before.Transform.Cos - moved.Transform.Cos) > 0.0001f));
    }

    /// <summary>
    /// 验证默认关卡中材质笔刷可经公开输入和相机 API 擦断木桥，并触发刚体拆分。
    /// </summary>
    [Fact]
    public async Task MaterialBrushCanDigRigidBridgeThroughPublicInput()
    {
        using Engine engine = await CreateLavaMineEngineAsync();
        engine.RunHeadlessTicks(2);

        ScriptScene scene = engine.Context.GetService<ScriptScene>();
        _ = FindBehaviour<MaterialBrush>(scene);
        CameraFollow follow = FindBehaviour<CameraFollow>(scene);
        follow.Damping = 0f;
        follow.LookaheadX = 0f;
        follow.LookaheadY = 0f;
        engine.RunHeadlessTicks(1);

        ScriptInputApi input = engine.Context.GetService<ScriptInputApi>();
        ScriptCameraApi camera = engine.Context.GetService<ScriptCameraApi>();
        CellGrid grid = engine.Context.GetService<CellGrid>();
        PhysicsSystem physics = engine.Context.GetService<PhysicsSystem>();
        int beforeCutBodies = physics.PhysicsWorld.ActiveBodyCount;

        int maxDestroyedBodies = 0;
        int maxCreatedBodies = 0;
        foreach (float y in new[] { 246f, 248f, 250f, 252f, 254f, 255f })
        {
            Point2F screen = camera.WorldToScreen(209f, y);
            input.Update([], [MouseButton.Right], screen.X, screen.Y, wheelY: 0f);
            engine.RunHeadlessTicks(1);
            maxDestroyedBodies = Math.Max(maxDestroyedBodies, physics.LastDestructionResult.DestroyedBodies);
            maxCreatedBodies = Math.Max(maxCreatedBodies, physics.LastDestructionResult.CreatedBodies);
        }

        Assert.True(CountEmpty(grid, 168, 246, 250, 256) > 0);
        Assert.True(maxDestroyedBodies > 0);
        Assert.True(maxCreatedBodies > 0);
        Assert.True(physics.PhysicsWorld.ActiveBodyCount > beforeCutBodies);
    }

    private static async Task<Engine> CreateLavaMineEngineAsync()
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
        Engine engine = DemoProgram.BuildEngine(options, project);

        _ = engine.LoadContentPackage();
        Assert.Null(engine.AttachCurrentSceneWorld());
        _ = engine.AttachResidentSimulationWorld(worldWidthCells: 640, worldHeightCells: 360);
        _ = engine.AttachPhysics();
        _ = await engine.AttachAudioFromContentAsync();
        engine.RegisterScriptAssembly(typeof(DemoProgram).Assembly);
        _ = engine.AttachScriptingFromServices();
        return engine;
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

    private static RigidBodySnapshot[] CopySnapshots(PhysicsSystem physics)
    {
        RigidBodySnapshot[] snapshots = new RigidBodySnapshot[physics.PhysicsWorld.ActiveBodyCount];
        int count = physics.CopyBodySnapshots(snapshots);
        Array.Resize(ref snapshots, count);
        return snapshots;
    }

    private static int CountEmpty(CellGrid grid, int minX, int minY, int maxX, int maxY)
    {
        int count = 0;
        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                if (grid.MaterialAt(x, y) == 0)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static bool TryFindSnapshot(RigidBodySnapshot[] snapshots, int bodyKey, out RigidBodySnapshot snapshot)
    {
        for (int i = 0; i < snapshots.Length; i++)
        {
            if (snapshots[i].BodyKey == bodyKey)
            {
                snapshot = snapshots[i];
                return true;
            }
        }

        snapshot = default;
        return false;
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
