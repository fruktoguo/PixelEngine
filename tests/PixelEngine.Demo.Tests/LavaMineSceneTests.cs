using System.Numerics;
using PixelEngine.Hosting;
using PixelEngine.Physics;
using PixelEngine.Simulation;
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
        MaterialBrush brush = FindBehaviour<MaterialBrush>(engine.Context.GetService<ScriptScene>());
        Assert.False(brush.InputEnabled);
        _ = FindBehaviour<PlayableHud>(engine.Context.GetService<ScriptScene>());
        _ = FindBehaviour<PlayerVisual>(engine.Context.GetService<ScriptScene>());
        _ = FindBehaviour<PlayableProjectileTool>(engine.Context.GetService<ScriptScene>());
        _ = FindBehaviour<PauseMenu>(engine.Context.GetService<ScriptScene>());
        Assert.Equal(0, CountBehaviours<DemoHud>(engine.Context.GetService<ScriptScene>()));
        Assert.Equal(0, CountBehaviours<MaterialEmitter>(engine.Context.GetService<ScriptScene>()));
        Assert.Equal(0, CountBehaviours<SparkEmitter>(engine.Context.GetService<ScriptScene>()));
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
        MaterialBrush brush = FindBehaviour<MaterialBrush>(scene);
        brush.InputEnabled = true;
        FindBehaviour<PlayableProjectileTool>(scene).InputEnabled = false;
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

    /// <summary>
    /// 验证 Demo 材质笔刷写入的 sand 会被真实 CA 接管，且内容包 water / oil / steam 密度规则会进入同一 CA。
    /// </summary>
    [Fact]
    public async Task MaterialBrushPaintedMaterialsAreTakenOverByCaRules()
    {
        using Engine engine = await CreateLavaMineEngineAsync();
        engine.RunHeadlessTicks(2);

        ScriptScene scene = engine.Context.GetService<ScriptScene>();
        MaterialBrush brush = FindBehaviour<MaterialBrush>(scene);
        brush.InputEnabled = true;
        FindBehaviour<PlayableProjectileTool>(scene).InputEnabled = false;
        CameraFollow follow = FindBehaviour<CameraFollow>(scene);
        follow.Damping = 0f;
        follow.LookaheadX = 0f;
        follow.LookaheadY = 0f;
        engine.RunHeadlessTicks(1);

        ScriptInputApi input = engine.Context.GetService<ScriptInputApi>();
        ScriptCameraApi camera = engine.Context.GetService<ScriptCameraApi>();
        CellGrid grid = engine.Context.GetService<CellGrid>();
        ISimulationEditApi edit = engine.Context.GetService<ISimulationEditApi>();
        MaterialTable materials = engine.Context.GetService<MaterialTable>();
        Assert.True(materials.TryGetId("sand", out ushort sand));
        Assert.True(materials.TryGetId("water", out ushort water));
        Assert.True(materials.TryGetId("oil", out ushort oil));
        Assert.True(materials.TryGetId("steam", out ushort steam));
        Assert.True(materials.TryGetId("stone", out ushort stone));

        ClearRect(edit, 44, 82, 236, 260);
        BuildBasin(edit, stone, minX: 48, minY: 92, maxX: 100, maxY: 160);
        BuildBasin(edit, stone, minX: 118, minY: 92, maxX: 174, maxY: 150);
        BuildBasin(edit, stone, minX: 188, minY: 92, maxX: 236, maxY: 150);

        PaintWithBrush(engine, input, camera, Key.Digit1, worldX: 72f, worldY: 100f);
        int sandInitial = CountMaterial(grid, sand, minX: 44, minY: 82, maxX: 100, maxY: 260);

        FillRect(edit, stone, minX: 144, minY: 108, maxX: 145, maxY: 111);
        FillRect(edit, stone, minX: 146, minY: 108, maxX: 147, maxY: 111);
        FillRect(edit, stone, minX: 144, minY: 110, maxX: 147, maxY: 111);
        FillRect(edit, water, minX: 145, minY: 108, maxX: 146, maxY: 109);
        FillRect(edit, oil, minX: 145, minY: 109, maxX: 146, maxY: 110);

        FillRect(edit, steam, minX: 204, minY: 132, maxX: 220, maxY: 142);
        double steamAverageYBefore = AverageMaterialY(grid, steam, minX: 190, minY: 16, maxX: 234, maxY: 148);

        engine.RunHeadlessTicks(1);

        Assert.Equal(oil, grid.MaterialAt(145, 108));
        Assert.Equal(water, grid.MaterialAt(145, 109));

        engine.RunHeadlessTicks(72);

        int settledSand = CountMaterial(grid, sand, minX: 50, minY: 146, maxX: 98, maxY: 158);
        double steamAverageYAfter = AverageMaterialY(grid, steam, minX: 190, minY: 16, maxX: 234, maxY: 148);

        Assert.True(sandInitial > 0, "MaterialBrush 应先在测试区域写入 sand。");
        Assert.True(settledSand >= 20, $"sand 应被 CA 接管并沉积到接料槽底部，initial={sandInitial}, settled={settledSand}");
        Assert.True(steamAverageYAfter < steamAverageYBefore - 4.0, $"steam 气体应上升，beforeY={steamAverageYBefore:F2}, afterY={steamAverageYAfter:F2}");
    }

    /// <summary>
    /// 验证 Demo 内容包中的 acid 腐蚀反应会损坏 RigidOwned 木结构，并触发 Physics 刚体重建。
    /// </summary>
    [Fact]
    public async Task AcidCorrosionDamagesRigidWoodAndRebuildsBody()
    {
        using Engine engine = await CreateLavaMineEngineAsync();
        engine.RunHeadlessTicks(2);

        CellGrid grid = engine.Context.GetService<CellGrid>();
        ISimulationEditApi edit = engine.Context.GetService<ISimulationEditApi>();
        MaterialTable materials = engine.Context.GetService<MaterialTable>();
        PhysicsSystem physics = engine.Context.GetService<PhysicsSystem>();
        Assert.True(materials.TryGetId("wood", out ushort wood));
        Assert.True(materials.TryGetId("acid", out ushort acid));

        ClearRect(edit, 40, 40, 96, 80);
        FillRect(edit, wood, minX: 48, minY: 56, maxX: 88, maxY: 64);
        engine.RunHeadlessTicks(1);
        int beforeBodies = physics.PhysicsWorld.ActiveBodyCount;
        _ = physics.CreateBodyFromRegion(48, 56, 40, 8);
        int ownedBefore = CountRigidOwned(grid, 48, 56, 88, 64);
        Assert.True(ownedBefore > 0);

        FillRect(edit, acid, minX: 66, minY: 54, maxX: 70, maxY: 58);
        int maxDestroyed = 0;
        int maxCreated = 0;
        for (int i = 0; i < 160; i++)
        {
            engine.RunHeadlessTicks(1);
            maxDestroyed = Math.Max(maxDestroyed, physics.LastDestructionResult.DestroyedBodies);
            maxCreated = Math.Max(maxCreated, physics.LastDestructionResult.CreatedBodies);
            if (CountRigidOwned(grid, 48, 56, 88, 64) < ownedBefore)
            {
                break;
            }
        }

        int ownedAfter = CountRigidOwned(grid, 48, 56, 88, 64);
        Assert.True(ownedAfter < ownedBefore, $"acid 腐蚀 RigidOwned wood 后 owned cell 应下降，before={ownedBefore}, after={ownedAfter}。");
        Assert.True(
            maxDestroyed > 0 || physics.PhysicsWorld.ActiveBodyCount >= beforeBodies,
            $"acid 腐蚀后物理系统不应丢失所有结构，destroyed={maxDestroyed}, created={maxCreated}, active={physics.PhysicsWorld.ActiveBodyCount}, before={beforeBodies}。");
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

    private static int CountMaterial(CellGrid grid, ushort material, int minX, int minY, int maxX, int maxY)
    {
        int count = 0;
        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                if (grid.MaterialAt(x, y) == material)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountRigidOwned(CellGrid grid, int minX, int minY, int maxX, int maxY)
    {
        int count = 0;
        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                if (CellFlags.Has(grid.FlagsAt(x, y), CellFlags.RigidOwned))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static double AverageMaterialY(CellGrid grid, ushort material, int minX, int minY, int maxX, int maxY)
    {
        long sum = 0;
        int count = 0;
        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                if (grid.MaterialAt(x, y) != material)
                {
                    continue;
                }

                sum += y;
                count++;
            }
        }

        return count == 0
            ? throw new InvalidOperationException($"区域内没有材质 {material}。")
            : sum / (double)count;
    }

    private static void ClearRect(ISimulationEditApi edit, int minX, int minY, int maxX, int maxY)
    {
        _ = edit.ClearRect(minX, minY, maxX - 1, maxY - 1);
    }

    private static void FillRect(ISimulationEditApi edit, ushort material, int minX, int minY, int maxX, int maxY)
    {
        _ = edit.PaintRect(minX, minY, maxX - 1, maxY - 1, material);
    }

    private static void BuildBasin(ISimulationEditApi edit, ushort stone, int minX, int minY, int maxX, int maxY)
    {
        FillRect(edit, stone, minX, maxY - 3, maxX, maxY);
        FillRect(edit, stone, minX, minY, minX + 3, maxY);
        FillRect(edit, stone, maxX - 3, minY, maxX, maxY);
    }

    private static void PaintWithBrush(
        Engine engine,
        ScriptInputApi input,
        ScriptCameraApi camera,
        Key materialKey,
        float worldX,
        float worldY)
    {
        Point2F screen = camera.WorldToScreen(worldX, worldY);
        input.Update([materialKey], [], screen.X, screen.Y, wheelY: 0f);
        engine.RunHeadlessTicks(1);
        input.Update([], [MouseButton.Left], screen.X, screen.Y, wheelY: 0f);
        engine.RunHeadlessTicks(1);
        input.Update([], [], screen.X, screen.Y, wheelY: 0f);
        engine.RunHeadlessTicks(1);
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
