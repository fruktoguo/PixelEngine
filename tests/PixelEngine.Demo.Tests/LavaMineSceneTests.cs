using System.Numerics;
using PixelEngine.Audio;
using PixelEngine.Hosting;
using PixelEngine.Physics;
using PixelEngine.Simulation;
using PixelEngine.Scripting;
using Xunit;
using ScriptScene = PixelEngine.Scripting.Scene;

namespace PixelEngine.Demo.Tests;

/// <summary>
/// Lava Mine Demo 场景加载与可玩性契约测试。
/// 不变式：场景材质/实体/脚本组件齐备、关卡目标与危险区在 headless tick 下可验证。
/// </summary>
public sealed class LavaMineSceneTests
{
    /// <summary>
    /// 验证默认横向关卡会通过脚本公开刚体 API 把可拆障碍注册为动态刚体。
    /// </summary>
    [Fact]
    public async Task LavaMineSceneRegistersDestructibleWoodAndMetalStructures()
    {
        // Arrange：准备输入与初始状态
        using Engine engine = await CreateLavaMineEngineAsync();
        engine.RunHeadlessTicks(2);

        ScriptScene scene = engine.Context.GetService<ScriptScene>();
        LevelDirector director = FindBehaviour<LevelDirector>(scene);
        // Assert：验证预期结果
        Assert.True(director.RigidStructuresQueued);
        Assert.Equal(12, director.RigidStructureCount);
        MaterialBrush brush = FindBehaviour<MaterialBrush>(scene);
        Assert.False(brush.InputEnabled);
        _ = FindBehaviour<PlayableHud>(scene);
        _ = FindBehaviour<PlayerVisual>(scene);
        _ = FindBehaviour<PlayableProjectileTool>(scene);
        _ = FindBehaviour<WeaponController>(scene);
        _ = FindBehaviour<PauseMenu>(scene);
        _ = FindBehaviour<GameUiDemoController>(scene);
        GoalTrigger goal = FindBehaviour<GoalTrigger>(scene);
        Assert.Equal(0, CountBehaviours<MissionDirector>(scene));
        Assert.Equal(0, CountBehaviours<RisingHazardDirector>(scene));
        Assert.Equal(0, CountBehaviours<ExtractionTrigger>(scene));
        Assert.Equal(0, CountBehaviours<ObjectiveCrystal>(scene));
        Assert.Equal(0, CountBehaviours<DemoHud>(scene));
        Assert.Equal(0, CountBehaviours<SparkEmitter>(scene));
        Assert.True(goal.Width >= 20f);
        Assert.True(goal.Height >= 36f);
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
    /// 验证默认关卡是左到右的横向熔岩闯关，而不是旧的上涨熔岩 / 水位任务。
    /// </summary>
    [Fact]
    public async Task LavaMineSceneBuildsDirectSideScrollingLavaRoute()
    {
        // Arrange：准备输入与初始状态
        using Engine engine = await CreateLavaMineEngineAsync();
        engine.RunHeadlessTicks(2);

        ScriptScene scene = engine.Context.GetService<ScriptScene>();
        LevelDirector director = FindBehaviour<LevelDirector>(scene);
        WeaponController weapons = FindBehaviour<WeaponController>(scene);
        ExplosiveTool explosive = FindBehaviour<ExplosiveTool>(scene);
        CellGrid grid = engine.Context.GetService<CellGrid>();
        MaterialTable materials = engine.Context.GetService<MaterialTable>();
        // Assert：验证预期结果
        Assert.True(materials.TryGetId("lava", out ushort lava));
        Assert.True(materials.TryGetId("wood", out ushort wood));
        Assert.True(materials.TryGetId("metal", out ushort metal));
        Assert.True(materials.TryGetId("stone", out ushort stone));

        int floorY = director.LevelHeight - 72;
        Assert.Equal(0, CountBehaviours<MissionDirector>(scene));
        Assert.Equal(0, CountBehaviours<RisingHazardDirector>(scene));
        Assert.True(director.PlayerSpawnX < director.GoalX, $"横向路线应从左向右推进，spawn={director.PlayerSpawnX}, goal={director.GoalX}。");
        int lavaCells = CountMaterial(grid, lava, minX: 172, minY: floorY - 4, maxX: 582, maxY: floorY + 28);
        int woodCells = CountRigidOwnedMaterial(grid, wood, minX: 132, minY: floorY - 130, maxX: 250, maxY: floorY + 4);
        int metalCells = CountRigidOwnedMaterial(grid, metal, minX: 270, minY: floorY - 130, maxX: 430, maxY: floorY + 4);
        int stoneCells = CountRigidOwnedMaterial(grid, stone, minX: 246, minY: floorY - 40, maxX: 374, maxY: floorY + 4);
        Assert.True(lavaCells > 200, $"横向路线中段应保留多个 lava 坑，actual={lavaCells}。");
        Assert.True(woodCells > 200, $"横向路线应包含 wood 可拆路障 / 桥梁，actual={woodCells}。");
        Assert.True(metalCells > 200, $"横向路线应包含 metal 可拆路障 / 桥梁，actual={metalCells}。");
        Assert.True(stoneCells > 100, $"横向路线应包含 stone 可拆矮障碍，actual={stoneCells}。");
        Assert.Equal(10f, weapons.TerrainEffectScale);
        Assert.Equal(10f, weapons.GrenadeTerrainEffectScale);
        Assert.Equal(10f, explosive.TerrainEffectScale);
        Assert.Equal(720, explosive.EffectiveRadius);
        Assert.Equal(3_200f, explosive.EffectiveForce);
    }

    /// <summary>
    /// 验证默认关卡中材质笔刷可经公开输入和相机 API 擦断木桥，并触发刚体拆分。
    /// </summary>
    [Fact]
    public async Task MaterialBrushCanDigRigidBridgeThroughPublicInput()
    {
        // Arrange：准备输入与初始状态
        using Engine engine = await CreateLavaMineEngineAsync();
        engine.RunHeadlessTicks(2);

        ScriptScene scene = engine.Context.GetService<ScriptScene>();
        MaterialBrush brush = FindBehaviour<MaterialBrush>(scene);
        brush.InputEnabled = true;
        FindBehaviour<PlayableProjectileTool>(scene).InputEnabled = false;
        FindBehaviour<WeaponController>(scene).InputEnabled = false;
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

        // Assert：验证预期结果
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
        // Arrange：准备输入与初始状态
        using Engine engine = await CreateLavaMineEngineAsync();
        engine.RunHeadlessTicks(2);

        ScriptScene scene = engine.Context.GetService<ScriptScene>();
        MaterialBrush brush = FindBehaviour<MaterialBrush>(scene);
        brush.InputEnabled = true;
        FindBehaviour<PlayableProjectileTool>(scene).InputEnabled = false;
        FindBehaviour<WeaponController>(scene).InputEnabled = false;
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
        // Assert：验证预期结果
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
        int minimumSettledSand = Math.Max(10, sandInitial / 4);
        Assert.True(settledSand >= minimumSettledSand, $"sand 应被 CA 接管并沉积到接料槽底部，initial={sandInitial}, settled={settledSand}, minimum={minimumSettledSand}");
        Assert.True(steamAverageYAfter < steamAverageYBefore - 4.0, $"steam 气体应上升，beforeY={steamAverageYBefore:F2}, afterY={steamAverageYAfter:F2}");
    }

    /// <summary>
    /// 验证正式 lava-mine 场景可在 headless 下经公开输入横向路线触发物理破坏并抵达终点。
    /// </summary>
    [Fact]
    public async Task LavaMineScriptedRouteCompletesMissionHeadlesslyThroughPublicApis()
    {
        using Engine engine = await CreateLavaMineEngineAsync();
        engine.RunHeadlessTicks(2);

        LavaMineRouteResult route = RunLavaMineScriptedRoute(engine);

        Assert.True(route.GoalReached, route.Describe());
        Assert.True(route.PrimaryFireCount > 0, "脚本路线应经 WeaponController 公开输入触发至少一次主武器。 ");
        Assert.True(route.MaxDestroyedBodies > 0, $"脚本路线应触发真实刚体破坏，destroyed={route.MaxDestroyedBodies}。");
        Assert.True(route.MaxCreatedBodies > 0, $"脚本路线应触发真实刚体重建，created={route.MaxCreatedBodies}。");
        Assert.Equal(0, route.ScriptExceptionCount);
    }

    /// <summary>
    /// 验证正式 lava-mine 场景通关后，Runtime 重开会恢复终点和武器计数基线。
    /// </summary>
    [Fact]
    public async Task LavaMineRuntimeRestartRestoresGoalBaselineAfterVictory()
    {
        // Arrange：准备输入与初始状态
        using Engine engine = await CreateLavaMineEngineAsync();
        engine.RunHeadlessTicks(2);

        ScriptScene scene = engine.Context.GetService<ScriptScene>();
        GoalTrigger goal = FindBehaviour<GoalTrigger>(scene);
        PlayerController player = FindBehaviour<PlayerController>(scene);
        WeaponController weapons = FindBehaviour<WeaponController>(scene);
        ScriptInputApi input = engine.Context.GetService<ScriptInputApi>();
        ScriptCameraApi camera = engine.Context.GetService<ScriptCameraApi>();

        Point2F shot = camera.WorldToScreen(player.CenterX + 16f, player.CenterY);
        input.Update([], [MouseButton.Left], shot.X, shot.Y, wheelY: 0f);
        engine.RunHeadlessTicks(2);
        // Assert：验证预期结果
        Assert.True(weapons.PrimaryFireCount > 0, "重开前应先产生一次武器分派，证明重开会恢复武器运行态。");

        player.SpawnX = goal.X + 2f;
        player.SpawnY = goal.Y + 2f;
        player.Respawn();
        input.Update([], [], shot.X, shot.Y, wheelY: 0f);
        engine.RunHeadlessTicks(2);
        Assert.True(goal.Reached, "玩家进入终点区域后应先触发通关状态。");

        RuntimeControlResult restart = engine.RestartCurrentScene();
        Assert.True(restart.Success, restart.Message);
        engine.RunHeadlessTicks(1);

        ScriptScene restartedScene = engine.Context.GetService<ScriptScene>();
        GoalTrigger restartedGoal = FindBehaviour<GoalTrigger>(restartedScene);
        WeaponController restartedWeapons = FindBehaviour<WeaponController>(restartedScene);

        Assert.False(restartedGoal.Reached);
        Assert.Equal(0, restartedWeapons.PrimaryFireCount);
        AssertNoFaultedBehaviours(restartedScene);
        Assert.Equal(0, restartedScene.ScriptExceptionCount);
    }

    /// <summary>
    /// 验证 Demo 内容包中的 acid 腐蚀反应会损坏 RigidOwned 木结构，并触发 Physics 刚体重建。
    /// </summary>
    [Fact]
    public async Task AcidCorrosionDamagesRigidWoodAndRebuildsBody()
    {
        // Arrange：准备输入与初始状态
        using Engine engine = await CreateLavaMineEngineAsync();
        engine.RunHeadlessTicks(2);

        CellGrid grid = engine.Context.GetService<CellGrid>();
        ISimulationEditApi edit = engine.Context.GetService<ISimulationEditApi>();
        MaterialTable materials = engine.Context.GetService<MaterialTable>();
        PhysicsSystem physics = engine.Context.GetService<PhysicsSystem>();
        // Assert：验证预期结果
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
        // Arrange：准备输入与初始状态
        using Engine engine = await CreateLavaMineEngineAsync();
        engine.RunHeadlessTicks(2);

        CellGrid grid = engine.Context.GetService<CellGrid>();
        ISimulationEditApi edit = engine.Context.GetService<ISimulationEditApi>();
        MaterialTable materials = engine.Context.GetService<MaterialTable>();
        TemperatureField temperature = engine.Context.GetService<TemperatureField>();
        PhysicsSystem physics = engine.Context.GetService<PhysicsSystem>();
        // Assert：验证预期结果
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
        _ = await engine.AttachAudioFromContentAsync(new NullAudioBackend());
        engine.RegisterScriptAssembly(typeof(DemoProgram).Assembly);
        _ = engine.AttachScriptingFromServices();
        return engine;
    }

    private static LavaMineRouteResult RunLavaMineScriptedRoute(Engine engine)
    {
        ScriptInputApi input = engine.Context.GetService<ScriptInputApi>();
        ScriptCameraApi camera = engine.Context.GetService<ScriptCameraApi>();
        DemoWindowScriptedInput scripted = new(input, camera, routeProbe: true);
        scripted.RegisterPhases(engine.Phases);

        ScriptScene scene = engine.Context.GetService<ScriptScene>();
        GoalTrigger goal = FindBehaviour<GoalTrigger>(scene);
        PlayerController player = FindBehaviour<PlayerController>(scene);
        WeaponController weapons = FindBehaviour<WeaponController>(scene);
        PhysicsSystem physics = engine.Context.GetService<PhysicsSystem>();
        int maxDestroyedBodies = 0;
        int maxCreatedBodies = 0;
        int frames = 0;

        for (; frames < 2_400 && !goal.Reached; frames++)
        {
            engine.RunHeadlessTicks(1);
            maxDestroyedBodies = Math.Max(maxDestroyedBodies, physics.LastDestructionResult.DestroyedBodies);
            maxCreatedBodies = Math.Max(maxCreatedBodies, physics.LastDestructionResult.CreatedBodies);
        }

        AssertNoFaultedBehaviours(scene);
        return new LavaMineRouteResult(
            goal.Reached,
            frames,
            player.State.X,
            player.State.Y,
            goal.X,
            goal.Y,
            weapons.PrimaryFireCount,
            maxDestroyedBodies,
            maxCreatedBodies,
            scene.ScriptExceptionCount);
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

    private static void AssertNoFaultedBehaviours(ScriptScene scene)
    {
        foreach (ScriptEntityInspection entity in scene.CaptureInspectionSnapshot())
        {
            foreach (ScriptComponentInspection component in entity.Components)
            {
                Assert.False(
                    component.Faulted,
                    $"Behaviour faulted: {component.TypeName}。{component.Behaviour.LastException}");
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

    private readonly record struct LavaMineRouteResult(
        bool GoalReached,
        int Frames,
        float PlayerX,
        float PlayerY,
        float GoalX,
        float GoalY,
        int PrimaryFireCount,
        int MaxDestroyedBodies,
        int MaxCreatedBodies,
        int ScriptExceptionCount)
    {
        public string Describe()
        {
            return $"goal={GoalReached}, frames={Frames}, player=({PlayerX:F2},{PlayerY:F2}), goal=({GoalX:F2},{GoalY:F2}), primary={PrimaryFireCount}, destroyed={MaxDestroyedBodies}, created={MaxCreatedBodies}, scriptExceptions={ScriptExceptionCount}";
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
