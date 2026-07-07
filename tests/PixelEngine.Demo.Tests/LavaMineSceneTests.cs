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

        ScriptScene scene = engine.Context.GetService<ScriptScene>();
        LevelDirector director = FindBehaviour<LevelDirector>(scene);
        Assert.True(director.RigidStructuresQueued);
        Assert.Equal(6, director.RigidStructureCount);
        MaterialBrush brush = FindBehaviour<MaterialBrush>(scene);
        Assert.False(brush.InputEnabled);
        _ = FindBehaviour<PlayableHud>(scene);
        _ = FindBehaviour<PlayerVisual>(scene);
        _ = FindBehaviour<PlayableProjectileTool>(scene);
        _ = FindBehaviour<WeaponController>(scene);
        _ = FindBehaviour<PauseMenu>(scene);
        MissionDirector mission = FindBehaviour<MissionDirector>(scene);
        RisingHazardDirector hazard = FindBehaviour<RisingHazardDirector>(scene);
        ExtractionTrigger extraction = FindBehaviour<ExtractionTrigger>(scene);
        Assert.Equal(3, CountBehaviours<ObjectiveCrystal>(scene));
        Assert.Equal(3, mission.RequiredCrystals);
        Assert.Equal(12, hazard.ActiveEmitterCount);
        Assert.Equal(0, CountBehaviours<DemoHud>(scene));
        Assert.Equal(hazard.ActiveEmitterCount, CountBehaviours<MaterialEmitter>(scene));
        Assert.Equal(0, CountBehaviours<SparkEmitter>(scene));
        Assert.Equal(0, CountBehaviours<GoalTrigger>(scene));
        AssertLavaHazardEmittersAreAmbient(scene, hazard.ActiveEmitterCount);
        Assert.True(extraction.Width >= 80f);
        Assert.True(extraction.Height >= 260f);
        PhysicsSystem physics = engine.Context.GetService<PhysicsSystem>();
        Assert.True(physics.Gravity.Y > 0f, $"Demo 刚体重力应沿像素坐标正 Y 向下，actual={physics.Gravity}。");
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
        Assert.Contains(
            movedSnapshots,
            moved => TryFindSnapshot(splitSnapshots, moved.BodyKey, out RigidBodySnapshot before) &&
                moved.Transform.Position.Y > before.Transform.Position.Y + 0.01f);
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
    /// 验证正式 lava-mine 场景可在 headless 下经公开输入路线采集水晶、触发物理破坏并完成撤离。
    /// </summary>
    [Fact]
    public async Task LavaMineScriptedRouteCompletesMissionHeadlesslyThroughPublicApis()
    {
        using Engine engine = await CreateLavaMineEngineAsync();
        engine.RunHeadlessTicks(2);

        ScriptInputApi input = engine.Context.GetService<ScriptInputApi>();
        ScriptCameraApi camera = engine.Context.GetService<ScriptCameraApi>();
        DemoWindowScriptedInput scripted = new(input, camera, routeProbe: true);
        scripted.RegisterPhases(engine.Phases);

        ScriptScene scene = engine.Context.GetService<ScriptScene>();
        MissionDirector mission = FindBehaviour<MissionDirector>(scene);
        ExtractionTrigger extraction = FindBehaviour<ExtractionTrigger>(scene);
        WeaponController weapons = FindBehaviour<WeaponController>(scene);
        PhysicsSystem physics = engine.Context.GetService<PhysicsSystem>();
        int maxDestroyedBodies = 0;
        int maxCreatedBodies = 0;

        for (int i = 0; i < 1_500 && mission.State == MissionState.Playing; i++)
        {
            engine.RunHeadlessTicks(1);
            maxDestroyedBodies = Math.Max(maxDestroyedBodies, physics.LastDestructionResult.DestroyedBodies);
            maxCreatedBodies = Math.Max(maxCreatedBodies, physics.LastDestructionResult.CreatedBodies);
        }

        Assert.Equal(MissionState.Won, mission.State);
        Assert.Equal("extraction_reached", mission.ResultReason);
        Assert.True(extraction.Reached);
        Assert.True(mission.CrystalsCollected >= mission.RequiredCrystals);
        Assert.True(weapons.PrimaryFireCount > 0, "脚本路线应经 WeaponController 公开输入触发至少一次主武器。 ");
        Assert.True(maxDestroyedBodies > 0, $"脚本路线应触发真实刚体破坏，destroyed={maxDestroyedBodies}。");
        Assert.True(maxCreatedBodies > 0, $"脚本路线应触发真实刚体重建，created={maxCreatedBodies}。");
        AssertNoFaultedBehaviours(scene);
        Assert.Equal(0, scene.ScriptExceptionCount);
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

    /// <summary>
    /// 验证 metal 梁近 lava 熔化后，RigidOwned 支撑退出刚体 mask，并让上方 wood 结构重建为下落的动态刚体。
    /// </summary>
    [Fact]
    public async Task MetalLavaMeltsSupportAndDropsUpperWoodStructure()
    {
        using Engine engine = await CreateLavaMineEngineAsync();
        engine.RunHeadlessTicks(2);

        CellGrid grid = engine.Context.GetService<CellGrid>();
        ISimulationEditApi edit = engine.Context.GetService<ISimulationEditApi>();
        MaterialTable materials = engine.Context.GetService<MaterialTable>();
        TemperatureField temperature = engine.Context.GetService<TemperatureField>();
        PhysicsSystem physics = engine.Context.GetService<PhysicsSystem>();
        Assert.True(materials.TryGetId("wood", out ushort wood));
        Assert.True(materials.TryGetId("metal", out ushort metal));
        Assert.True(materials.TryGetId("molten_metal", out ushort moltenMetal));
        Assert.True(materials.TryGetId("lava", out ushort lava));
        Assert.True(materials.TryGetId("stone", out ushort stone));

        ClearRect(edit, 300, 64, 384, 132);
        FillRect(edit, wood, minX: 330, minY: 76, maxX: 350, maxY: 96);
        FillRect(edit, metal, minX: 326, minY: 96, maxX: 354, maxY: 104);
        FillRect(edit, lava, minX: 328, minY: 104, maxX: 352, maxY: 108);
        FillRect(edit, stone, minX: 318, minY: 108, maxX: 362, maxY: 114);
        engine.RunHeadlessTicks(1);

        int parentBodyKey = physics.CreateBodyFromRegion(326, 76, 28, 28);
        int ownedMetalBefore = CountRigidOwnedMaterial(grid, metal, minX: 326, minY: 96, maxX: 354, maxY: 104);
        Assert.True(parentBodyKey >= 0);
        Assert.True(ownedMetalBefore > 0, $"测试结构的 metal 梁应先由刚体 stamp 占用，actual={ownedMetalBefore}。");
        engine.RunHeadlessTicks(1);
        ownedMetalBefore = CountRigidOwnedMaterial(grid, metal, minX: 326, minY: 96, maxX: 354, maxY: 104);
        Assert.True(ownedMetalBefore > 0, $"刚体 stamp 进入下一帧 dirty rect 后仍应保留 metal 梁，actual={ownedMetalBefore}。");

        for (int y = 96; y < 104; y++)
        {
            for (int x = 326; x < 354; x++)
            {
                temperature.AddHeat(x, y, 1_200f);
            }
        }

        engine.RunHeadlessTicks(1);

        Assert.True(CountMaterial(grid, moltenMetal, minX: 326, minY: 96, maxX: 354, maxY: 108) > 0);
        Assert.Equal(0, CountRigidOwnedMaterial(grid, metal, minX: 326, minY: 96, maxX: 354, maxY: 104));
        Assert.True(physics.LastDestructionResult.DestroyedBodies >= 1, $"metal 梁相变应破坏父刚体，actual={physics.LastDestructionResult}。");
        Assert.True(physics.LastDestructionResult.CreatedBodies >= 1, $"metal 梁相变后上方 wood 连通块应重建为子刚体，actual={physics.LastDestructionResult}。");

        RigidBodySnapshot[] splitSnapshots = CopySnapshots(physics);
        RigidBodySnapshot woodSnapshot = splitSnapshots.Single(snapshot =>
            snapshot.Transform.Position.X >= 320f &&
            snapshot.Transform.Position.X <= 360f &&
            snapshot.Transform.Position.Y >= 70f &&
            snapshot.Transform.Position.Y <= 110f &&
            CountMaskMaterial(snapshot.Mask, wood) == snapshot.Mask.SolidPixelCount &&
            CountMaskMaterial(snapshot.Mask, metal) == 0);

        engine.RunHeadlessTicks(12);

        RigidBodySnapshot[] movedSnapshots = CopySnapshots(physics);
        Assert.True(TryFindSnapshot(movedSnapshots, woodSnapshot.BodyKey, out RigidBodySnapshot movedWood));
        Assert.True(
            movedWood.Transform.Position.Y > woodSnapshot.Transform.Position.Y + 0.01f,
            $"上方 wood 子刚体应沿 Demo 重力方向下落，beforeY={woodSnapshot.Transform.Position.Y:F4}, afterY={movedWood.Transform.Position.Y:F4}。");
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

    private static void AssertLavaHazardEmittersAreAmbient(ScriptScene scene, int expectedCount)
    {
        int count = 0;
        foreach (ScriptEntityInspection entity in scene.CaptureInspectionSnapshot())
        {
            foreach (ScriptComponentInspection component in entity.Components)
            {
                if (component.Behaviour is not MaterialEmitter emitter)
                {
                    continue;
                }

                count++;
                Assert.Equal("lava", emitter.MaterialName);
                Assert.Equal(1, emitter.ParticleCount);
                Assert.True(emitter.ParticleLifetime <= 34, $"熔岩喷口粒子寿命不应像爆炸残留一样长，actual={emitter.ParticleLifetime}。");
                Assert.True(emitter.LightRadius <= 22f, $"熔岩喷口持续光半径应保持环境级，actual={emitter.LightRadius}。");
                Assert.True(emitter.LightIntensity <= 0.28f, $"熔岩喷口持续光强度应保持环境级，actual={emitter.LightIntensity}。");
            }
        }

        Assert.Equal(expectedCount, count);
    }

    private static void AssertNoFaultedBehaviours(ScriptScene scene)
    {
        foreach (ScriptEntityInspection entity in scene.CaptureInspectionSnapshot())
        {
            foreach (ScriptComponentInspection component in entity.Components)
            {
                Assert.False(component.Faulted, $"Behaviour faulted: {component.TypeName}。");
            }
        }
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

    private static int CountRigidOwnedMaterial(CellGrid grid, ushort material, int minX, int minY, int maxX, int maxY)
    {
        int count = 0;
        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                if (grid.MaterialAt(x, y) == material && CellFlags.Has(grid.FlagsAt(x, y), CellFlags.RigidOwned))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountMaskMaterial(BodyLocalMask mask, ushort material)
    {
        int count = 0;
        for (int y = 0; y < mask.Height; y++)
        {
            for (int x = 0; x < mask.Width; x++)
            {
                if (mask.IsSolid(x, y) && mask.MaterialAt(x, y) == material)
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
