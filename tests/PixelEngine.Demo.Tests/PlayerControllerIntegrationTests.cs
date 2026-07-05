using PixelEngine.Hosting;
using PixelEngine.Core.Time;
using PixelEngine.Gui;
using PixelEngine.Physics;
using PixelEngine.Simulation.Particles;
using PixelEngine.Scripting;
using PixelEngine.Simulation;
using Xunit;
using ScriptScene = PixelEngine.Scripting.Scene;

namespace PixelEngine.Demo.Tests;

/// <summary>
/// Demo 玩家控制器端到端脚本集成测试。
/// </summary>
public sealed class PlayerControllerIntegrationTests
{
    /// <summary>
    /// 验证 Demo HUD 与暂停菜单只经脚本 GUI 门面绘制玩家状态、笔刷、性能行、菜单按钮和默认折叠的调试叠层。
    /// </summary>
    [Fact]
    public void DemoHudAndPauseMenuDrawExpectedGuiThroughScriptContext()
    {
        using Engine engine = CreateManualScriptEngine(out ScriptInputApi input, out _, out _, out ScriptScene scene, DemoMaterials());
        Entity entity = scene.CreateEntity();
        _ = entity.AddComponent<PlayerController>();
        _ = entity.AddComponent<PlayerHealth>();
        _ = entity.AddComponent<MaterialBrush>();
        _ = entity.AddComponent<ExplosiveTool>();
        _ = entity.AddComponent<GoalTrigger>();
        _ = entity.AddComponent<DemoHud>();
        _ = entity.AddComponent<PauseMenu>();

        engine.RunHeadlessTicks(1);

        IScriptRuntime runtime = engine.Context.GetService<IScriptRuntime>();
        RecordingGuiContext hudGui = new();
        runtime.DrawGui(hudGui);

        Assert.Contains("begin:demo-hud:Demo HUD:NoTitleBar, NoResize, NoMove, NoSavedSettings", hudGui.Drawn);
        Assert.Contains("swatch:selected-material:FF5FC8D8:14", hudGui.Drawn);
        Assert.Contains(hudGui.Drawn, line => line.StartsWith("progress:", StringComparison.Ordinal));
        Assert.Contains("text:材质 sand    半径 4", hudGui.Drawn);
        Assert.Contains("text-colored:目标: 未抵达:FFE0E0E0", hudGui.Drawn);
        Assert.Contains("text:爆破 0", hudGui.Drawn);
        Assert.Contains(hudGui.Drawn, line => line.StartsWith("text:FPS ", StringComparison.Ordinal));
        Assert.Contains(hudGui.Drawn, line => line.StartsWith("text:Chunks ", StringComparison.Ordinal));
        Assert.DoesNotContain("text-colored:HUD 等待玩家脚本组件。:FF4080FF", hudGui.Drawn);

        input.Update([Key.Escape], [], mouseX: 0, mouseY: 0, wheelY: 0);
        engine.RunHeadlessTicks(1);

        IRuntimeControlApi runtimeControl = engine.Context.GetService<IRuntimeControlApi>();
        Assert.False(runtimeControl.Capture().IsPlaying);

        RecordingGuiContext pauseGui = new();
        runtime.DrawGui(pauseGui);

        Assert.Contains("begin:demo-pause-menu:暂停:NoResize, NoSavedSettings", pauseGui.Drawn);
        Assert.Contains("text:状态: 暂停", pauseGui.Drawn);
        Assert.Contains("button:继续", pauseGui.Drawn);
        Assert.DoesNotContain("button:打开 Editor", pauseGui.Drawn);
        Assert.Contains("button:重开", pauseGui.Drawn);
        Assert.Contains("button:退出", pauseGui.Drawn);
        Assert.Contains("text:调试", pauseGui.Drawn);
        Assert.Contains("checkbox:显示调试叠层:False", pauseGui.Drawn);
        Assert.DoesNotContain("checkbox:dirty rect:False", pauseGui.Drawn);
        Assert.Contains("text-colored:重开关卡快照尚未捕获。:FF4080FF", pauseGui.Drawn);

        RecordingGuiContext continueGui = new(clickedButtons: ["继续"]);
        runtime.DrawGui(continueGui);

        Assert.True(runtimeControl.Capture().IsPlaying);

        RecordingGuiContext closedGui = new();
        runtime.DrawGui(closedGui);
        Assert.DoesNotContain(closedGui.Drawn, line => line.StartsWith("begin:demo-pause-menu:", StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证暂停菜单按钮会经公开 Runtime/Diagnostics API 触发重开与调试叠层控制。
    /// </summary>
    [Fact]
    public void PauseMenuButtonsDriveRestartAndDiagnosticsFacades()
    {
        using Engine engine = CreateManualScriptEngine(out ScriptInputApi input, out _, out _, out ScriptScene scene, DemoMaterials());
        _ = scene.CreateEntity().AddComponent<PauseMenu>();
        engine.RunHeadlessTicks(1);

        input.Update([Key.Escape], [], mouseX: 0, mouseY: 0, wheelY: 0);
        engine.RunHeadlessTicks(1);

        IScriptRuntime runtime = engine.Context.GetService<IScriptRuntime>();
        IRuntimeControlApi runtimeControl = engine.Context.GetService<IRuntimeControlApi>();
        IDiagnosticsApi diagnostics = engine.Context.GetService<IDiagnosticsApi>();

        RecordingGuiContext revealGui = new(toggledCheckboxes: ["显示调试叠层"]);
        runtime.DrawGui(revealGui);

        RecordingGuiContext overlayGui = new(toggledCheckboxes: ["dirty rect"]);
        runtime.DrawGui(overlayGui);

        Assert.True(diagnostics.IsOverlayEnabled(DebugOverlayKind.DirtyRects));

        RecordingGuiContext restartGui = new(clickedButtons: ["重开"]);
        runtime.DrawGui(restartGui);

        Assert.Contains(restartGui.Drawn, line => line.StartsWith("text:已重开当前关卡", StringComparison.Ordinal));
        Assert.True(runtimeControl.Capture().IsPlaying);

    }

    /// <summary>
    /// 验证 Demo 终点触发器会在玩家进入出口区域时标记通关，并产生粒子、光照、fog 与音效反馈。
    /// </summary>
    [Fact]
    public void GoalTriggerMarksReachedAndQueuesCelebrationFeedback()
    {
        RecordingAudioApi audio = new();
        MaterialTable materials = DemoMaterials();
        using Engine engine = CreateManualScriptEngine(out _, out _, out _, out ScriptScene scene, materials, audio);
        Entity entity = scene.CreateEntity();
        PlayerController player = entity.AddComponent<PlayerController>();
        player.SpawnX = 12f;
        player.SpawnY = 12f;
        GoalTrigger goal = entity.AddComponent<GoalTrigger>();
        goal.X = 10f;
        goal.Y = 10f;
        goal.Width = 8f;
        goal.Height = 8f;
        goal.CelebrationParticleCount = 8;
        goal.CelebrationParticleSpeed = 30f;
        goal.GoalAudioCue = "goal_reached.wav";

        engine.RunHeadlessTicks(1);

        Assert.True(goal.Reached, $"玩家应进入终点触发区，state={Describe(player.State)}");

        ParticleSystem particles = engine.Context.GetService<ParticleSystem>();
        Assert.True(particles.ActiveCount > 0);

        ScriptLightingSynchronizer lighting = engine.Context.GetService<ScriptLightingSynchronizer>();
        Assert.True(lighting.PointLights.Length > 0);
        Assert.True(lighting.FogOfWar.RevealAlpha(14, 14) > 0);
        Assert.Equal("goal_reached.wav", audio.LastCue);
        Assert.Equal(14f, audio.LastX, precision: 3);
        Assert.Equal(14f, audio.LastY, precision: 3);

        IScriptRuntime runtime = engine.Context.GetService<IScriptRuntime>();
        RecordingGuiContext victoryGui = new();
        runtime.DrawGui(victoryGui);

        Assert.Contains("begin:demo-victory-menu:通关:NoResize, NoSavedSettings", victoryGui.Drawn);
        Assert.Contains("text-colored:矿洞出口已抵达:FF80F080", victoryGui.Drawn);
        Assert.Contains("text:目标完成", victoryGui.Drawn);
        Assert.Contains("button:重开关卡", victoryGui.Drawn);
        Assert.Contains("button:退出", victoryGui.Drawn);

        RecordingGuiContext exitGui = new(clickedButtons: ["退出"]);
        runtime.DrawGui(exitGui);

        Assert.True(engine.IsShutdownRequested);
        Assert.Contains("text:已请求关闭。", exitGui.Drawn);
    }

    /// <summary>
    /// 验证 PlayerHealth 会按玩家 AABB 采样危险材质，造成伤害、喷粒子、播放受击音效并在死亡后重生。
    /// </summary>
    [Fact]
    public void PlayerHealthSamplesHazardsEmitsFeedbackAndRespawns()
    {
        RecordingAudioApi audio = new();
        MaterialTable materials = DemoMaterials();
        Assert.True(materials.TryGetId("lava", out ushort lava));
        using Engine engine = CreateManualScriptEngine(out _, out CellGrid grid, out _, out ScriptScene scene, materials, audio);
        Entity entity = scene.CreateEntity();
        PlayerController player = entity.AddComponent<PlayerController>();
        player.SpawnX = 12f;
        player.SpawnY = 12f;
        PlayerHealth health = entity.AddComponent<PlayerHealth>();
        health.MaxHealth = 20f;
        health.LavaDamagePerSecond = 60f;
        health.HurtParticleCount = 4;
        health.HurtParticleSpeed = 20f;
        health.HurtSoundCooldown = 0f;
        FillRect(grid, lava, minX: 12, minY: 12, maxX: 18, maxY: 24);

        engine.RunHeadlessTicks(1);

        Assert.InRange(health.Health, 0.01f, health.MaxHealth - 0.01f);
        Assert.Equal(1, health.DamageEventCount);
        Assert.Equal(0, health.RespawnCount);
        Assert.Equal("player_hurt.wav", audio.LastCue);
        Assert.Equal(player.CenterX, audio.LastX, precision: 3);
        Assert.Equal(player.CenterY, audio.LastY, precision: 3);
        Assert.True(engine.Context.GetService<ParticleSystem>().ActiveCount > 0);

        health.LavaDamagePerSecond = 2_000f;
        engine.RunHeadlessTicks(1);

        Assert.Equal(health.MaxHealth, health.Health);
        Assert.True(health.DamageEventCount >= 2);
        Assert.Equal(1, health.RespawnCount);
        Assert.Equal(player.SpawnX, player.State.X, precision: 3);
        Assert.Equal(player.SpawnY, player.State.Y, precision: 3);
    }

    /// <summary>
    /// 验证 Demo 爆破工具会从鼠标世界坐标触发抗性感知破坏、碎屑粒子与光照反馈。
    /// </summary>
    [Fact]
    public void ExplosiveToolMiddleClickDamagesCellsAndQueuesLighting()
    {
        MaterialTable materials = DemoMaterials();
        Assert.True(materials.TryGetId("stone", out ushort stone));
        using Engine engine = CreateManualScriptEngine(out ScriptInputApi input, out CellGrid grid, out _, out ScriptScene scene, materials);
        ExplosiveTool tool = scene.CreateEntity().AddComponent<ExplosiveTool>();
        tool.Radius = 5;
        tool.Force = 20f;
        tool.CooldownSeconds = 0f;
        FillRect(grid, material: stone, minX: 10, minY: 10, maxX: 15, maxY: 15);

        input.Update([], [MouseButton.Middle], mouseX: 12.25f, mouseY: 12.75f, wheelY: 0f);
        engine.RunHeadlessTicks(1);

        Assert.Equal(1, tool.ExplosionCount);
        Assert.Equal(12.25f, tool.LastExplosionX, precision: 3);
        Assert.Equal(12.75f, tool.LastExplosionY, precision: 3);
        Assert.NotEqual(stone, grid.MaterialAt(12, 12));

        ParticleSystem particles = engine.Context.GetService<ParticleSystem>();
        Assert.True(particles.ActiveCount > 0);

        ScriptLightingSynchronizer lighting = engine.Context.GetService<ScriptLightingSynchronizer>();
        Assert.Equal(1, lighting.PointLights.Length);
        Assert.True(lighting.FogOfWar.RevealAlpha(12, 13) > 0);
    }

    /// <summary>
    /// 验证 Demo 爆破工具会通过公开 World.Explode 链路对邻近刚体施加径向冲量。
    /// </summary>
    [Fact]
    public void ExplosiveToolMiddleClickPushesNearbyRigidBody()
    {
        MaterialTable materials = DemoMaterials(destructibleSolids: false);
        Assert.True(materials.TryGetId("stone", out ushort stone));
        using Engine engine = CreateManualScriptEngine(out ScriptInputApi input, out CellGrid grid, out _, out ScriptScene scene, materials);
        FillRect(grid, stone, minX: 48, minY: 24, maxX: 72, maxY: 36);

        PhysicsSystem physics = engine.Context.GetService<PhysicsSystem>();
        _ = physics.CreateBodyFromRegion(48, 24, 24, 12);
        Assert.True(physics.PhysicsWorld.ActiveBodyCount > 0);
        Assert.Equal(0f, MaxBodyVelocityX(physics), precision: 3);

        ExplosiveTool tool = scene.CreateEntity().AddComponent<ExplosiveTool>();
        tool.Radius = 22;
        tool.Force = 1_000f;
        tool.CooldownSeconds = 0f;

        input.Update([], [MouseButton.Middle], mouseX: 40f, mouseY: 30f, wheelY: 0f);
        engine.RunHeadlessTicks(1);
        input.Update([], [], mouseX: 40f, mouseY: 30f, wheelY: 0f);
        engine.RunHeadlessTicks(1);

        Assert.Equal(1, tool.ExplosionCount);
        Assert.True(
            MaxBodyVelocityX(physics) > 0.1f,
            $"爆炸应把左侧邻近刚体向右推出或推动重建后的子刚体，active={physics.PhysicsWorld.ActiveBodyCount}");
    }

    /// <summary>
    /// 验证 Demo 材质笔刷会响应数字键、滚轮和鼠标按钮，并按相机世界坐标写入/擦除 cell。
    /// </summary>
    [Fact]
    public void MaterialBrushSelectsRadiusPaintsAndErasesWorldCells()
    {
        MaterialTable materials = DemoMaterials();
        Assert.True(materials.TryGetId("stone", out ushort stone));
        using Engine engine = CreateManualScriptEngine(out ScriptInputApi input, out CellGrid grid, out _, out ScriptScene scene, materials);
        MaterialBrush brush = scene.CreateEntity().AddComponent<MaterialBrush>();

        engine.RunHeadlessTicks(1);
        input.Update([Key.Digit6], [], mouseX: 12.25f, mouseY: 15.75f, wheelY: 1f);
        engine.RunHeadlessTicks(1);

        Assert.Equal(5, brush.SelectedIndex);
        Assert.Equal("stone", brush.SelectedMaterialName);
        Assert.Equal(5, brush.Radius);

        input.Update([], [MouseButton.Left], mouseX: 12.25f, mouseY: 15.75f, wheelY: 0f);
        engine.RunHeadlessTicks(1);

        Assert.Equal(stone, grid.MaterialAt(12, 15));

        input.Update([], [MouseButton.Right], mouseX: 12.25f, mouseY: 15.75f, wheelY: 0f);
        engine.RunHeadlessTicks(1);

        Assert.Equal((ushort)0, grid.MaterialAt(12, 15));
    }

    /// <summary>
    /// 验证材质喷口会按公开脚本 API 写入 cell、生成粒子、播放音效并请求点光源。
    /// </summary>
    [Fact]
    public void MaterialEmitterWritesCellsParticlesAudioAndLighting()
    {
        RecordingAudioApi audio = new();
        MaterialTable materials = DemoMaterials();
        Assert.True(materials.TryGetId("stone", out ushort stone));
        using Engine engine = CreateManualScriptEngine(out _, out CellGrid grid, out _, out ScriptScene scene, materials, audio);
        MaterialEmitter emitter = scene.CreateEntity().AddComponent<MaterialEmitter>();
        emitter.X = 18f;
        emitter.Y = 14f;
        emitter.MaterialName = "stone";
        emitter.Radius = 1;
        emitter.IntervalSeconds = 1f;
        emitter.ParticleCount = 3;
        emitter.AudioCue = "splash_water.wav";
        emitter.AudioCooldownSeconds = 0f;
        emitter.AddLight = true;
        emitter.LightRadius = 24f;
        emitter.LightIntensity = 0.5f;
        ushort afterParticleToCell = ushort.MaxValue;
        ushort afterCa = ushort.MaxValue;
        ushort afterDirtySwap = ushort.MaxValue;
        engine.Phases.Register(EnginePhase.ParticleToCell, _ => afterParticleToCell = grid.MaterialAt(18, 14));
        engine.Phases.Register(EnginePhase.CaSimulation, _ => afterCa = grid.MaterialAt(18, 14));
        engine.Phases.Register(EnginePhase.DirtyRectSwap, _ => afterDirtySwap = grid.MaterialAt(18, 14));

        FrameTiming timing = engine.RunOneTick(1.0 / 60.0);

        Assert.True(timing.RunSim);
        Assert.False(emitter.Faulted, emitter.LastException?.ToString());
        Assert.Equal(string.Empty, emitter.BlockedReason);
        Assert.Equal(stone, afterParticleToCell);
        Assert.Equal(stone, afterCa);
        Assert.Equal(stone, afterDirtySwap);
        Assert.Equal(stone, grid.MaterialAt(18, 14));
        Assert.True(engine.Context.GetService<ParticleSystem>().ActiveCount >= emitter.ParticleCount);
        Assert.Equal("splash_water.wav", audio.LastCue);
        Assert.Equal(18f, audio.LastX, precision: 3);
        Assert.Equal(14f, audio.LastY, precision: 3);

        ScriptLightingSynchronizer lighting = engine.Context.GetService<ScriptLightingSynchronizer>();
        Assert.Equal(1, lighting.PointLights.Length);
        Assert.Equal(18f, lighting.PointLights[0].X, precision: 3);
        Assert.Equal(14f, lighting.PointLights[0].Y, precision: 3);
    }

    /// <summary>
    /// 验证 Demo 相机脚本会跟随玩家、写入缩放，并把中心夹在关卡边界内。
    /// </summary>
    [Fact]
    public void CameraFollowTracksPlayerZoomAndClampsToWorldBounds()
    {
        using Engine engine = CreateManualScriptEngine(out ScriptInputApi input, out CellGrid grid, out ScriptCameraApi camera, out ScriptScene scene);
        Entity entity = scene.CreateEntity();
        _ = entity.AddComponent<Transform>();
        PlayerController player = entity.AddComponent<PlayerController>();
        player.SpawnX = 32f;
        player.SpawnY = 32f;
        CameraFollow follow = entity.AddComponent<CameraFollow>();
        follow.Zoom = 2f;
        follow.Damping = 240f;
        follow.LookaheadX = 0f;
        follow.LookaheadY = 0f;
        follow.MinX = 0f;
        follow.MinY = 0f;
        follow.MaxX = 96f;
        follow.MaxY = 64f;
        FillFloor(grid, material: 1, y: 46, x0: 0, x1: 96, rigidOwned: false);

        engine.RunHeadlessTicks(4);

        Assert.Equal(2f, camera.Zoom);
        CameraSnapshot firstSnapshot = camera.Snapshot();
        Assert.Equal(MathF.Truncate(firstSnapshot.OriginWorldX), firstSnapshot.OriginWorldX);
        Assert.Equal(MathF.Truncate(firstSnapshot.OriginWorldY), firstSnapshot.OriginWorldY);
        Assert.InRange(camera.CenterX, player.CenterX - 1f, player.CenterX + 1f);
        Assert.InRange(camera.CenterY, player.CenterY - 1f, player.CenterY + 1f);
        Assert.True(entity.TryGetComponent(out Transform transform));
        Assert.Equal(player.CenterX, transform.X, precision: 3);
        Assert.Equal(player.CenterY, transform.Y, precision: 3);
        camera.Follow(entity);
        Assert.Equal(transform.X, camera.CenterX, precision: 3);
        Assert.Equal(transform.Y, camera.CenterY, precision: 3);

        float startCameraX = camera.CenterX;
        input.Update([Key.D], [], mouseX: 0, mouseY: 0, wheelY: 0);
        engine.RunHeadlessTicks(8);

        Assert.True(camera.CenterX > startCameraX, $"相机应随玩家右移，start={startCameraX}, actual={camera.CenterX}");
        CameraSnapshot movedSnapshot = camera.Snapshot();
        Assert.Equal(MathF.Truncate(movedSnapshot.OriginWorldX), movedSnapshot.OriginWorldX);
        Assert.Equal(MathF.Truncate(movedSnapshot.OriginWorldY), movedSnapshot.OriginWorldY);

        player.SpawnX = -20f;
        player.SpawnY = -20f;
        player.Respawn();
        input.Update([], [], mouseX: 0, mouseY: 0, wheelY: 0);
        engine.RunHeadlessTicks(3);

        float minCenterX = follow.MinX + (camera.Viewport.Width / (2f * camera.Zoom));
        float minCenterY = follow.MinY + (camera.Viewport.Height / (2f * camera.Zoom));
        Assert.Equal(minCenterX, camera.CenterX, precision: 3);
        Assert.Equal(minCenterY, camera.CenterY, precision: 3);
        CameraSnapshot clampedSnapshot = camera.Snapshot();
        Assert.Equal(0f, clampedSnapshot.OriginWorldX, precision: 3);
        Assert.Equal(0f, clampedSnapshot.OriginWorldY, precision: 3);
    }

    /// <summary>
    /// 验证可玩相机使用滚轮在固定整数倍率间缩放视野，避免窗口尺寸直接改变内部像素数量。
    /// </summary>
    [Fact]
    public void CameraFollowMouseWheelAdjustsIntegerZoomLevels()
    {
        using Engine engine = CreateManualScriptEngine(out ScriptInputApi input, out CellGrid grid, out ScriptCameraApi camera, out ScriptScene scene);
        Entity entity = scene.CreateEntity();
        PlayerController player = entity.AddComponent<PlayerController>();
        player.SpawnX = 32f;
        player.SpawnY = 32f;
        CameraFollow follow = entity.AddComponent<CameraFollow>();
        follow.Zoom = 2f;
        follow.MinZoom = 1f;
        follow.MaxZoom = 4f;
        follow.ZoomStep = 1f;
        follow.Damping = 240f;
        follow.LookaheadX = 0f;
        follow.LookaheadY = 0f;
        follow.MaxX = 180f;
        follow.MaxY = 120f;
        FillFloor(grid, material: 1, y: 46, x0: 0, x1: 96, rigidOwned: false);

        engine.RunHeadlessTicks(2);
        Assert.Equal(2f, camera.Zoom);
        CameraSnapshot zoomedSnapshot = camera.Snapshot();
        Assert.Equal(MathF.Truncate(zoomedSnapshot.OriginWorldX), zoomedSnapshot.OriginWorldX);
        Assert.Equal(MathF.Truncate(zoomedSnapshot.OriginWorldY), zoomedSnapshot.OriginWorldY);

        input.Update([], [], mouseX: 0, mouseY: 0, wheelY: -1f);
        engine.RunHeadlessTicks(1);
        Assert.Equal(1f, camera.Zoom);
        CameraSnapshot oneToOneSnapshot = camera.Snapshot();
        Assert.Equal(MathF.Truncate(oneToOneSnapshot.OriginWorldX), oneToOneSnapshot.OriginWorldX);
        Assert.Equal(MathF.Truncate(oneToOneSnapshot.OriginWorldY), oneToOneSnapshot.OriginWorldY);

        input.Update([], [], mouseX: 0, mouseY: 0, wheelY: 1f);
        engine.RunHeadlessTicks(1);
        Assert.Equal(2f, camera.Zoom);
        CameraSnapshot zoomedAgainSnapshot = camera.Snapshot();
        Assert.Equal(MathF.Truncate(zoomedAgainSnapshot.OriginWorldX), zoomedAgainSnapshot.OriginWorldX);
        Assert.Equal(MathF.Truncate(zoomedAgainSnapshot.OriginWorldY), zoomedAgainSnapshot.OriginWorldY);
    }

    /// <summary>
    /// 验证可玩 Demo 玩家可见层会提交玩家、准星与光照 overlay，不再只有不可见的碰撞 AABB。
    /// </summary>
    [Fact]
    public void PlayerVisualSubmitsOverlayAndLocalLight()
    {
        using Engine engine = CreateManualScriptEngine(out ScriptInputApi input, out CellGrid grid, out _, out ScriptScene scene, DemoMaterials());
        FillFloor(grid, material: 6, y: 46, x0: 0, x1: 96, rigidOwned: false);
        Entity entity = scene.CreateEntity();
        _ = entity.AddComponent<Transform>();
        PlayerController player = entity.AddComponent<PlayerController>();
        player.SpawnX = 16f;
        player.SpawnY = 30f;
        _ = entity.AddComponent<PlayableProjectileTool>();
        _ = entity.AddComponent<PlayerVisual>();
        ScriptOverlayApi overlay = engine.Context.GetService<ScriptOverlayApi>();

        input.Update([], [], mouseX: 20, mouseY: 10, wheelY: 0);
        engine.RunHeadlessTicks(2);

        Assert.True(overlay.CommandCount >= 6);
        Assert.Contains(
            Enumerable.Range(0, overlay.CommandCount).Select(overlay.GetCommand),
            command => command.Primitive == ScriptOverlayPrimitive.SolidRectangle && command.ColorBgra == 0xFF_F2_D0_5E);
        ScriptLightingSynchronizer lighting = engine.Context.GetService<ScriptLightingSynchronizer>();
        Assert.True(lighting.FogOfWar.RevealAlpha(0, 0) > 0);
        Assert.True(lighting.FogOfWar.RevealAlpha(39, 19) > 0);
        Assert.Equal(0, engine.Context.GetService<ParticleSystem>().ActiveCount);
    }

    /// <summary>
    /// 验证退化为零长度的弹道不会让玩家可见层 fault，避免黄线停留后后续帧不再更新。
    /// </summary>
    [Fact]
    public void PlayerVisualIgnoresDegenerateTracer()
    {
        MaterialTable materials = DemoMaterials();
        using Engine engine = CreateManualScriptEngine(out ScriptInputApi input, out CellGrid grid, out _, out ScriptScene scene, materials);
        Assert.True(materials.TryGetId("stone", out ushort stone));
        Entity entity = scene.CreateEntity();
        _ = entity.AddComponent<Transform>();
        PlayerController player = entity.AddComponent<PlayerController>();
        player.SpawnX = 16f;
        player.SpawnY = 30f;
        PlayableProjectileTool projectile = entity.AddComponent<PlayableProjectileTool>();
        projectile.CooldownSeconds = 0f;
        projectile.Range = 16f;
        projectile.ImpactRadius = 1;
        PlayerVisual visual = entity.AddComponent<PlayerVisual>();

        engine.RunHeadlessTicks(2);
        int muzzleX = (int)MathF.Floor(player.CenterX);
        int muzzleY = (int)MathF.Floor(player.CenterY - 2f);
        FillRect(grid, stone, muzzleX, muzzleY, muzzleX, muzzleY);
        input.Update([], [MouseButton.Left], mouseX: player.CenterX + 12f, mouseY: player.CenterY - 2f, wheelY: 0f);
        engine.RunHeadlessTicks(1);

        Assert.False(visual.Faulted, visual.LastException?.ToString());
        Assert.True(projectile.TracerRemainingSeconds > 0f);
        ScriptOverlayApi overlay = engine.Context.GetService<ScriptOverlayApi>();
        Assert.DoesNotContain(
            Enumerable.Range(0, overlay.CommandCount).Select(overlay.GetCommand),
            command => command.Primitive == ScriptOverlayPrimitive.Line);
    }

    /// <summary>
    /// 验证玩家可在普通 settled cell 地面上接地、水平跑动并响应跳跃输入。
    /// </summary>
    [Fact]
    public void PlayerRunsAndJumpsFromSettledTerrain()
    {
        using Engine engine = CreatePlayerEngine(out ScriptInputApi input, out CellGrid grid);
        FillFloor(grid, material: 1, y: 46, x0: 0, x1: 96, rigidOwned: false);

        engine.RunHeadlessTicks(20);
        PlayerController player = FindPlayer(engine);
        Assert.True(player.State.OnGround, $"玩家应落在普通地面上，state={Describe(player.State)}");

        float startX = player.State.X;
        input.Update([Key.D], [], mouseX: 0, mouseY: 0, wheelY: 0);
        engine.RunHeadlessTicks(6);
        Assert.True(player.State.X > startX, $"玩家应向右跑动，start={startX}, actual={player.State.X}");
        Assert.True(player.State.OnGround);

        input.Update([], [], mouseX: 0, mouseY: 0, wheelY: 0);
        engine.RunHeadlessTicks(2);
        Assert.True(player.State.OnGround);

        input.Update([Key.Space], [], mouseX: 0, mouseY: 0, wheelY: 0);
        Assert.True(input.WasPressed(Key.Space));
        engine.RunHeadlessTicks(3);
        Assert.True(
            player.State.AppliedDeltaY < 0f,
            $"跳跃后应产生向上的位移，state={Describe(player.State)}");
    }

    /// <summary>
    /// 验证玩家可站在刚体往返 stamp 的 RigidOwned 像素上，不会穿透。
    /// </summary>
    [Fact]
    public void PlayerStandsOnRigidOwnedPixels()
    {
        using Engine engine = CreatePlayerEngine(out _, out CellGrid grid);
        FillFloor(grid, material: 0, y: 46, x0: 24, x1: 48, rigidOwned: true);

        engine.RunHeadlessTicks(20);
        PlayerController player = FindPlayer(engine);

        Assert.True(player.State.OnGround);
        Assert.True(player.State.Y + player.State.Height <= 46f);
    }

    /// <summary>
    /// 验证玩家在粉末堆形成的非平整小台阶上仍可横向移动，不会把沙堆边缘误当成整面墙。
    /// </summary>
    [Fact]
    public void PlayerRunsAcrossUnevenPowderPile()
    {
        MaterialTable materials = DemoMaterials();
        using Engine engine = CreateManualScriptEngine(out ScriptInputApi input, out CellGrid grid, out _, out ScriptScene scene, materials);
        Entity entity = scene.CreateEntity();
        PlayerController player = entity.AddComponent<PlayerController>();
        player.SpawnX = 12f;
        player.SpawnY = 26f;
        player.MaxRunSpeed = 145f;
        player.GroundAcceleration = 1_850f;
        player.AirAcceleration = 1_150f;
        Assert.True(materials.TryGetId("sand", out ushort sand));

        FillSteppedPile(grid, sand, startX: 0, endX: 72, baseY: 42);
        engine.RunHeadlessTicks(20);
        Assert.True(player.State.OnGround, $"玩家应站在沙堆上，state={Describe(player.State)}");

        float startX = player.State.X;
        input.Update([Key.D], [], mouseX: 0, mouseY: 0, wheelY: 0);
        engine.RunHeadlessTicks(24);

        Assert.True(player.State.X > startX + 12f, $"玩家应能越过非平整粉末地面继续移动，start={startX}, state={Describe(player.State)}");
        Assert.False(player.State.OnWallRight, $"沙堆小台阶不应长期被判为右墙，state={Describe(player.State)}");
    }

    /// <summary>
    /// 验证可玩 Demo 左键射击会触发破坏弹、粒子和音效。
    /// </summary>
    [Fact]
    public void PlayableProjectileLeftClickQueuesAudioAndParticles()
    {
        RecordingAudioApi audio = new();
        MaterialTable materials = DemoMaterials();
        using Engine engine = CreateManualScriptEngine(out ScriptInputApi input, out CellGrid grid, out _, out ScriptScene scene, materials, audio);
        Assert.True(materials.TryGetId("stone", out ushort stone));
        FillFloor(grid, stone, y: 46, x0: 0, x1: 96, rigidOwned: false);
        FillWall(grid, stone, x: 34, y0: 24, y1: 46);
        Entity entity = scene.CreateEntity();
        _ = entity.AddComponent<Transform>();
        PlayerController player = entity.AddComponent<PlayerController>();
        player.SpawnX = 14f;
        player.SpawnY = 30f;
        PlayableProjectileTool projectile = entity.AddComponent<PlayableProjectileTool>();
        projectile.CooldownSeconds = 0f;
        projectile.Range = 80f;
        projectile.ImpactRadius = 3;

        engine.RunHeadlessTicks(20);
        input.Update([], [MouseButton.Left], mouseX: 36f, mouseY: 34f, wheelY: 0f);
        engine.RunHeadlessTicks(1);

        Assert.Equal(1, projectile.ShotsFired);
        Assert.Equal("explosion.wav", audio.LastCue);
        Assert.True(engine.Context.GetService<ParticleSystem>().ActiveCount > 0);
        Assert.NotEqual(stone, grid.MaterialAt((int)MathF.Round(projectile.LastHitX), (int)MathF.Round(projectile.LastHitY)));
    }

    /// <summary>
    /// 验证按住左键会按冷却连续发射破坏弹，而不是只响应第一帧点击边沿。
    /// </summary>
    [Fact]
    public void PlayableProjectileAutoFiresWhileLeftMouseIsHeld()
    {
        MaterialTable materials = DemoMaterials();
        using Engine engine = CreateManualScriptEngine(out ScriptInputApi input, out CellGrid grid, out _, out ScriptScene scene, materials);
        Assert.True(materials.TryGetId("stone", out ushort stone));
        FillWall(grid, stone, x: 54, y0: 12, y1: 54);
        Entity entity = scene.CreateEntity();
        PlayerController player = entity.AddComponent<PlayerController>();
        player.SpawnX = 14f;
        player.SpawnY = 30f;
        PlayableProjectileTool projectile = entity.AddComponent<PlayableProjectileTool>();
        projectile.CooldownSeconds = 0f;
        projectile.Range = 96f;
        projectile.ImpactRadius = 1;
        projectile.CollapseScanRadius = 6;

        engine.RunHeadlessTicks(2);
        for (int i = 0; i < 4; i++)
        {
            input.Update([], [MouseButton.Left], mouseX: 64f, mouseY: 30f, wheelY: 0f);
            engine.RunHeadlessTicks(1);
        }

        Assert.True(projectile.ShotsFired >= 4, $"按住左键应持续开火，actual={projectile.ShotsFired}");
    }

    /// <summary>
    /// 验证数据驱动武器控制器加载真实 weapons.json、支持键盘/滚轮切换，并通过旧破坏弹后端保持坍塌扫描能力。
    /// </summary>
    [Fact]
    public void WeaponControllerSwitchesAndRoutesHitscanThroughProjectileBackend()
    {
        RecordingAudioApi audio = new();
        MaterialTable materials = DemoMaterials();
        using Engine engine = CreateManualScriptEngine(
            out ScriptInputApi input,
            out CellGrid grid,
            out _,
            out ScriptScene scene,
            materials,
            audio,
            contentRoot: DemoContentRoot());
        Assert.True(materials.TryGetId("stone", out ushort stone));
        FillWall(grid, stone, x: 34, y0: 24, y1: 46);
        Entity entity = scene.CreateEntity();
        _ = entity.AddComponent<Transform>();
        PlayerController player = entity.AddComponent<PlayerController>();
        player.SpawnX = 14f;
        player.SpawnY = 30f;
        PlayableProjectileTool projectile = entity.AddComponent<PlayableProjectileTool>();
        projectile.InputEnabled = false;
        WeaponController weapons = entity.AddComponent<WeaponController>();

        engine.RunHeadlessTicks(2);
        Assert.Equal("pistol", weapons.SelectedWeaponId);
        Assert.Equal(120, weapons.CurrentAmmo);

        input.Update([Key.Digit4], [], mouseX: 36f, mouseY: 34f, wheelY: 0f);
        engine.RunHeadlessTicks(1);
        Assert.Equal("laser", weapons.SelectedWeaponId);

        input.Update([], [], mouseX: 36f, mouseY: 34f, wheelY: -1f);
        engine.RunHeadlessTicks(1);
        Assert.Equal("excavator", weapons.SelectedWeaponId);

        input.Update([Key.Digit1], [], mouseX: 36f, mouseY: 34f, wheelY: 0f);
        engine.RunHeadlessTicks(1);
        input.Update([], [MouseButton.Left], mouseX: 36f, mouseY: 34f, wheelY: 0f);
        engine.RunHeadlessTicks(1);

        Assert.Equal(1, weapons.PrimaryFireCount);
        Assert.Equal(1, projectile.ShotsFired);
        Assert.Equal(WeaponKind.SingleShot, weapons.LastDispatchedKind);
        Assert.Equal(119, weapons.CurrentAmmo);
        Assert.Equal("explosion.wav", audio.LastCue);
        Assert.True(projectile.TracerRemainingSeconds > 0f);
        Assert.False(string.IsNullOrWhiteSpace(projectile.CollapseStatus));

        input.Update([], [MouseButton.Left], mouseX: 36f, mouseY: 34f, wheelY: 0f);
        engine.RunHeadlessTicks(1);
        Assert.Equal(1, weapons.PrimaryFireCount);
        Assert.Equal(1, projectile.ShotsFired);
        Assert.True(weapons.CooldownRemaining > 0f);
    }

    /// <summary>
    /// 验证 R 换弹和过热锁定由 weapons.json 数据驱动，不依赖真实内容目录的默认零换弹配置。
    /// </summary>
    [Fact]
    public void WeaponControllerReloadsAndLocksOutOverheatedWeapon()
    {
        string contentRoot = CreateTemporaryWeaponContent(
            """
            {
              "weapons": [
                {
                  "id": "test-pistol",
                  "displayName": "Test Pistol",
                  "kind": "singleShot",
                  "damage": 12,
                  "radius": 1,
                  "falloff": "none",
                  "cooldownSeconds": 0,
                  "ammoMax": 1,
                  "reloadSeconds": 0.05,
                  "impactCue": "explosion"
                },
                {
                  "id": "test-laser",
                  "displayName": "Test Laser",
                  "kind": "laser",
                  "damage": 12,
                  "radius": 1,
                  "falloff": "none",
                  "cooldownSeconds": 0,
                  "ammoMax": 4,
                  "reloadSeconds": 0,
                  "heatPerCell": 120,
                  "beamDps": 60,
                  "impactCue": "sizzle_lava_water"
                }
              ]
            }
            """);
        try
        {
            using Engine engine = CreateManualScriptEngine(
                out ScriptInputApi input,
                out CellGrid grid,
                out _,
                out ScriptScene scene,
                DemoMaterials(),
                contentRoot: contentRoot);
            Entity entity = scene.CreateEntity();
            _ = entity.AddComponent<Transform>();
            PlayerController player = entity.AddComponent<PlayerController>();
            player.SpawnX = 14f;
            player.SpawnY = 30f;
            WeaponController weapons = entity.AddComponent<WeaponController>();

            engine.RunHeadlessTicks(2);
            input.Update([], [MouseButton.Left], mouseX: 36f, mouseY: 34f, wheelY: 0f);
            engine.RunHeadlessTicks(1);
            Assert.Equal(0, weapons.CurrentAmmo);

            input.Update([Key.R], [], mouseX: 36f, mouseY: 34f, wheelY: 0f);
            engine.RunHeadlessTicks(1);
            Assert.True(weapons.IsReloading);
            engine.RunHeadlessTicks(6);
            Assert.False(weapons.IsReloading);
            Assert.Equal(1, weapons.CurrentAmmo);

            input.Update([Key.Digit2], [], mouseX: 36f, mouseY: 34f, wheelY: 0f);
            engine.RunHeadlessTicks(1);
            Assert.Equal("test-laser", weapons.SelectedWeaponId);
            int fireCountBeforeLaser = weapons.PrimaryFireCount;
            input.Update([], [MouseButton.Left], mouseX: 36f, mouseY: 34f, wheelY: 0f);
            engine.RunHeadlessTicks(1);
            Assert.True(weapons.IsOverheated);
            Assert.Equal(fireCountBeforeLaser + 1, weapons.PrimaryFireCount);
            int ammoAfterOverheatShot = weapons.CurrentAmmo;

            input.Update([], [MouseButton.Left], mouseX: 36f, mouseY: 34f, wheelY: 0f);
            engine.RunHeadlessTicks(1);
            Assert.Equal(fireCountBeforeLaser + 1, weapons.PrimaryFireCount);
            Assert.Equal(ammoAfterOverheatShot, weapons.CurrentAmmo);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    /// <summary>
    /// 验证六类武器均可经公开脚本输入触发，并在真实后端产生差异化效果。
    /// </summary>
    [Fact]
    public void WeaponControllerDispatchesSixWeaponKindsIntoDistinctEffects()
    {
        string contentRoot = CreateTemporaryWeaponContent(
            """
            {
              "weapons": [
                { "id": "shot", "displayName": "Shot", "kind": "singleShot", "damage": 40, "radius": 2, "falloff": "none", "impulse": 8, "cooldownSeconds": 0, "ammoMax": 10, "tracerDuration": 0.03, "impactCue": "explosion" },
                { "id": "bomb", "displayName": "Bomb", "kind": "bomb", "damage": 120, "radius": 5, "falloff": "quadratic", "impulse": 12, "cooldownSeconds": 0, "ammoMax": 10, "impactCue": "explosion" },
                { "id": "grenade", "displayName": "Grenade", "kind": "grenade", "damage": 90, "radius": 4, "falloff": "linear", "impulse": 10, "fuseSeconds": 0.04, "throwSpeed": 3, "gravity": 0, "bounce": 0, "cooldownSeconds": 0, "ammoMax": 10, "impactCue": "explosion" },
                { "id": "laser", "displayName": "Laser", "kind": "laser", "damage": 10, "radius": 2, "falloff": "none", "cooldownSeconds": 0, "ammoMax": 10, "heatPerCell": 45, "beamDps": 600, "impactCue": "sizzle_lava_water" },
                { "id": "excavator", "displayName": "Excavator", "kind": "excavator", "damage": 0, "radius": 2, "falloff": "none", "cooldownSeconds": 0, "ammoMax": 10, "impactCue": "impact_stone" },
                { "id": "builder", "displayName": "Builder", "kind": "builder", "damage": 0, "radius": 2, "falloff": "none", "cooldownSeconds": 0, "ammoMax": 10, "spawnMaterial": "stone", "impactCue": "impact_stone" }
              ]
            }
            """);
        try
        {
            RecordingAudioApi audio = new();
            MaterialTable materials = DemoMaterials();
            using Engine engine = CreateManualScriptEngine(
                out ScriptInputApi input,
                out CellGrid grid,
                out _,
                out ScriptScene scene,
                materials,
                audio,
                contentRoot: contentRoot);
            Assert.True(materials.TryGetId("stone", out ushort stone));
            FillRect(grid, stone, minX: 32, minY: 24, maxX: 38, maxY: 40);
            Entity entity = scene.CreateEntity();
            _ = entity.AddComponent<Transform>();
            PlayerController player = entity.AddComponent<PlayerController>();
            player.SpawnX = 14f;
            player.SpawnY = 30f;
            PlayableProjectileTool projectile = entity.AddComponent<PlayableProjectileTool>();
            projectile.InputEnabled = false;
            WeaponController weapons = entity.AddComponent<WeaponController>();
            TemperatureField temperature = engine.Context.GetService<TemperatureField>();
            ParticleSystem particles = engine.Context.GetService<ParticleSystem>();

            engine.RunHeadlessTicks(2);
            input.Update([Key.Digit1], [MouseButton.Left], mouseX: 36f, mouseY: 34f, wheelY: 0f);
            engine.RunHeadlessTicks(1);
            Assert.Equal(WeaponKind.SingleShot, weapons.LastDispatchedKind);
            Assert.Equal(1, projectile.ShotsFired);

            int particlesAfterShot = particles.ActiveCount;
            input.Update([Key.Digit2], [MouseButton.Left], mouseX: 36f, mouseY: 34f, wheelY: 0f);
            engine.RunHeadlessTicks(1);
            Assert.Equal(WeaponKind.Bomb, weapons.LastDispatchedKind);
            Assert.True(particles.ActiveCount > particlesAfterShot);

            int entitiesBeforeGrenade = scene.EntityCount;
            input.Update([Key.Digit3], [MouseButton.Left], mouseX: 36f, mouseY: 34f, wheelY: 0f);
            engine.RunHeadlessTicks(1);
            Assert.True(scene.EntityCount > entitiesBeforeGrenade);
            engine.RunHeadlessTicks(20);
            Assert.Equal(WeaponKind.Grenade, weapons.LastDispatchedKind);
            Assert.True(scene.EntityCount <= entitiesBeforeGrenade);

            float temperatureBeforeLaser = MaxTemperature(temperature, minX: 16, minY: 16, maxX: 48, maxY: 48);
            input.Update([Key.Digit4], [MouseButton.Left], mouseX: 36f, mouseY: 34f, wheelY: 0f);
            engine.RunHeadlessTicks(1);
            Assert.Equal(WeaponKind.Laser, weapons.LastDispatchedKind);
            Assert.True(MaxTemperature(temperature, minX: 16, minY: 16, maxX: 48, maxY: 48) > temperatureBeforeLaser);

            FillRect(grid, stone, minX: 34, minY: 32, maxX: 36, maxY: 36);
            input.Update([Key.Digit5], [MouseButton.Left], mouseX: 36f, mouseY: 34f, wheelY: 0f);
            engine.RunHeadlessTicks(1);
            Assert.Equal(WeaponKind.Excavator, weapons.LastDispatchedKind);
            Assert.Equal(0, grid.GetMaterial(35, 34));

            int stoneBeforeBuilder = CountMaterial(grid, stone, minX: 0, minY: 0, maxX: 95, maxY: 63);
            input.Update([Key.Digit6], [MouseButton.Left], mouseX: 70f, mouseY: 50f, wheelY: 0f);
            engine.RunHeadlessTicks(1);
            Assert.Equal(WeaponKind.Builder, weapons.LastDispatchedKind);
            Assert.True(CountMaterial(grid, stone, minX: 0, minY: 0, maxX: 95, maxY: 63) > stoneBeforeBuilder);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    /// <summary>
    /// 验证可玩 Demo 的破坏弹会在爆破后把局部脱离主地形的小型固体岛转换成刚体，避免石块长时间静态浮空。
    /// </summary>
    [Fact]
    public void PlayableProjectileConvertsDetachedSolidIslandIntoRigidBody()
    {
        RecordingAudioApi audio = new();
        MaterialTable materials = DemoMaterials();
        using Engine engine = CreateManualScriptEngine(out ScriptInputApi input, out CellGrid grid, out _, out ScriptScene scene, materials, audio);
        Assert.True(materials.TryGetId("stone", out ushort stone));
        FillRect(grid, stone, minX: 40, minY: 26, maxX: 50, maxY: 34);

        Entity entity = scene.CreateEntity();
        _ = entity.AddComponent<Transform>();
        PlayerController player = entity.AddComponent<PlayerController>();
        player.SpawnX = 16f;
        player.SpawnY = 26f;
        PlayableProjectileTool projectile = entity.AddComponent<PlayableProjectileTool>();
        projectile.CooldownSeconds = 0f;
        projectile.Range = 80f;
        projectile.ImpactRadius = 2;
        projectile.CollapseScanRadius = 18;

        engine.RunHeadlessTicks(2);
        input.Update([], [MouseButton.Left], mouseX: 41f, mouseY: 30f, wheelY: 0f);
        engine.RunHeadlessTicks(1);
        input.Update([], [], mouseX: 41f, mouseY: 30f, wheelY: 0f);
        engine.RunHeadlessTicks(6);

        PhysicsSystem physics = engine.Context.GetService<PhysicsSystem>();
        Assert.Equal(1, projectile.ShotsFired);
        Assert.True(projectile.CollapsedFloatingIslands >= 1, $"应转换悬空石块，region={projectile.LastCollapsedRegion}");
        Assert.True(physics.Stats.ActiveBodyCount >= 1);
    }

    /// <summary>
    /// 验证爆破扫描允许悬空块接触扫描框上边界，避免打掉支撑后上方石块因局部窗口过小被误判为不可转换。
    /// </summary>
    [Fact]
    public void PlayableProjectileConvertsTopBorderDetachedIslandIntoRigidBody()
    {
        RecordingAudioApi audio = new();
        MaterialTable materials = DemoMaterials();
        using Engine engine = CreateManualScriptEngine(out ScriptInputApi input, out CellGrid grid, out _, out ScriptScene scene, materials, audio);
        Assert.True(materials.TryGetId("stone", out ushort stone));
        FillRect(grid, stone, minX: 40, minY: 20, maxX: 50, maxY: 31);

        Entity entity = scene.CreateEntity();
        _ = entity.AddComponent<Transform>();
        PlayerController player = entity.AddComponent<PlayerController>();
        player.SpawnX = 16f;
        player.SpawnY = 26f;
        PlayableProjectileTool projectile = entity.AddComponent<PlayableProjectileTool>();
        projectile.CooldownSeconds = 0f;
        projectile.Range = 80f;
        projectile.ImpactRadius = 1;
        projectile.CollapseScanRadius = 10;

        engine.RunHeadlessTicks(2);
        input.Update([], [MouseButton.Left], mouseX: 41f, mouseY: 30f, wheelY: 0f);
        engine.RunHeadlessTicks(1);
        input.Update([], [], mouseX: 41f, mouseY: 30f, wheelY: 0f);
        engine.RunHeadlessTicks(6);

        PhysicsSystem physics = engine.Context.GetService<PhysicsSystem>();
        Assert.Equal(1, projectile.ShotsFired);
        Assert.True(projectile.CollapsedFloatingIslands >= 1, $"应转换贴近扫描框上边界的悬空石块，region={projectile.LastCollapsedRegion}");
        Assert.True(physics.Stats.ActiveBodyCount >= 1);
    }

    /// <summary>
    /// 验证打掉连续石块的下半部后，弹坑上沿局部悬空块会被提升为刚体，而不是继续静态浮空。
    /// </summary>
    [Fact]
    public void PlayableProjectileConvertsImpactFracturedShelfIntoRigidBody()
    {
        RecordingAudioApi audio = new();
        MaterialTable materials = DemoMaterials();
        using Engine engine = CreateManualScriptEngine(out ScriptInputApi input, out CellGrid grid, out _, out ScriptScene scene, materials, audio);
        Assert.True(materials.TryGetId("stone", out ushort stone));
        FillRect(grid, stone, minX: 38, minY: 18, maxX: 70, maxY: 42);

        Entity entity = scene.CreateEntity();
        _ = entity.AddComponent<Transform>();
        PlayerController player = entity.AddComponent<PlayerController>();
        player.SpawnX = 16f;
        player.SpawnY = 26f;
        PlayableProjectileTool projectile = entity.AddComponent<PlayableProjectileTool>();
        projectile.CooldownSeconds = 0f;
        projectile.Range = 96f;
        projectile.ImpactRadius = 4;
        projectile.MinCollapsePixels = 4;
        projectile.MaxCollapsePixels = 2_048;
        projectile.CollapseScanRadius = 28;
        projectile.FallbackOverhangRadius = 20;
        projectile.AllowOverhangFallbackCollapse = true;
        projectile.AllowImpactFallbackCollapse = true;

        engine.RunHeadlessTicks(2);
        input.Update([], [MouseButton.Left], mouseX: 52f, mouseY: 34f, wheelY: 0f);
        engine.RunHeadlessTicks(1);
        input.Update([], [], mouseX: 52f, mouseY: 34f, wheelY: 0f);
        engine.RunHeadlessTicks(10);

        PhysicsSystem physics = engine.Context.GetService<PhysicsSystem>();
        Assert.Equal(1, projectile.ShotsFired);
        Assert.Contains("converted", projectile.CollapseStatus, StringComparison.Ordinal);
        Assert.True(projectile.CollapsedFloatingIslands >= 1, $"应转换弹坑上沿悬空块，status={projectile.CollapseStatus}");
        Assert.True(physics.Stats.ActiveBodyCount >= 1);
    }

    /// <summary>
    /// 验证完全悬空且超过旧 72 cell 上限的固体岛仍会转成动态刚体，不会永久静态浮空。
    /// </summary>
    [Fact]
    public void PlayableProjectileConvertsLargerFullyDetachedIslandIntoRigidBody()
    {
        RecordingAudioApi audio = new();
        MaterialTable materials = DemoMaterials();
        using Engine engine = CreateManualScriptEngine(out ScriptInputApi input, out CellGrid grid, out _, out ScriptScene scene, materials, audio);
        Assert.True(materials.TryGetId("stone", out ushort stone));
        FillRect(grid, stone, minX: 40, minY: 18, maxX: 65, maxY: 36);

        Entity entity = scene.CreateEntity();
        _ = entity.AddComponent<Transform>();
        PlayerController player = entity.AddComponent<PlayerController>();
        player.SpawnX = 12f;
        player.SpawnY = 28f;
        PlayableProjectileTool projectile = entity.AddComponent<PlayableProjectileTool>();
        projectile.CooldownSeconds = 0f;
        projectile.Range = 96f;
        projectile.ImpactRadius = 2;
        projectile.CollapseScanRadius = 36;
        projectile.CollapseScanRetryFrames = 2;
        projectile.MaxCollapseRegionSize = 48;
        projectile.MaxCollapsePixels = 512;

        engine.RunHeadlessTicks(2);
        input.Update([], [MouseButton.Left], mouseX: 52f, mouseY: 28f, wheelY: 0f);
        engine.RunHeadlessTicks(1);
        input.Update([], [], mouseX: 52f, mouseY: 28f, wheelY: 0f);
        engine.RunHeadlessTicks(10);

        PhysicsSystem physics = engine.Context.GetService<PhysicsSystem>();
        Assert.Equal(1, projectile.ShotsFired);
        Assert.Contains("converted", projectile.CollapseStatus, StringComparison.Ordinal);
        Assert.True(projectile.CollapsedFloatingIslands >= 1, $"完全悬空固体岛应变成刚体，status={projectile.CollapseStatus}, region={projectile.LastCollapsedRegion}");
        Assert.True(physics.Stats.ActiveBodyCount >= 1);
    }

    /// <summary>
    /// 验证默认破坏弹不会把仍连接主地形的受支撑大块误转成刚体，避免玩家脚下碰撞被整体清空。
    /// </summary>
    [Fact]
    public void PlayableProjectileDoesNotConvertSupportedTerrainSlabByDefault()
    {
        RecordingAudioApi audio = new();
        MaterialTable materials = DemoMaterials();
        using Engine engine = CreateManualScriptEngine(out ScriptInputApi input, out CellGrid grid, out _, out ScriptScene scene, materials, audio);
        Assert.True(materials.TryGetId("stone", out ushort stone));
        FillRect(grid, stone, minX: 22, minY: 20, maxX: 82, maxY: 50);

        Entity entity = scene.CreateEntity();
        _ = entity.AddComponent<Transform>();
        PlayerController player = entity.AddComponent<PlayerController>();
        player.SpawnX = 12f;
        player.SpawnY = 28f;
        PlayableProjectileTool projectile = entity.AddComponent<PlayableProjectileTool>();
        projectile.CooldownSeconds = 0f;
        projectile.Range = 96f;
        projectile.ImpactRadius = 4;
        projectile.CollapseScanRadius = 28;

        engine.RunHeadlessTicks(2);
        input.Update([], [MouseButton.Left], mouseX: 48f, mouseY: 34f, wheelY: 0f);
        engine.RunHeadlessTicks(1);
        input.Update([], [], mouseX: 48f, mouseY: 34f, wheelY: 0f);
        engine.RunHeadlessTicks(10);

        PhysicsSystem physics = engine.Context.GetService<PhysicsSystem>();
        Assert.Equal(1, projectile.ShotsFired);
        Assert.Equal(0, projectile.CollapsedFloatingIslands);
        Assert.Equal(0, physics.Stats.ActiveBodyCount);
        Assert.DoesNotContain("converted", projectile.CollapseStatus, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 验证破坏弹不会把扫描窗口上边界之外仍相连的主地形误判成独立碎块。
    /// </summary>
    [Fact]
    public void PlayableProjectileDoesNotConvertTerrainConnectedAboveScanWindow()
    {
        MaterialTable materials = DemoMaterials();
        using Engine engine = CreateManualScriptEngine(out ScriptInputApi input, out CellGrid grid, out _, out ScriptScene scene, materials);
        Assert.True(materials.TryGetId("stone", out ushort stone));
        FillRect(grid, stone, minX: 40, minY: 18, maxX: 49, maxY: 33);

        Entity entity = scene.CreateEntity();
        _ = entity.AddComponent<Transform>();
        PlayerController player = entity.AddComponent<PlayerController>();
        player.SpawnX = 12f;
        player.SpawnY = 28f;
        PlayableProjectileTool projectile = entity.AddComponent<PlayableProjectileTool>();
        projectile.CooldownSeconds = 0f;
        projectile.Range = 96f;
        projectile.ImpactRadius = 1;
        projectile.CollapseScanRadius = 10;

        engine.RunHeadlessTicks(2);
        input.Update([], [MouseButton.Left], mouseX: 42f, mouseY: 30f, wheelY: 0f);
        engine.RunHeadlessTicks(1);
        input.Update([], [], mouseX: 42f, mouseY: 30f, wheelY: 0f);
        engine.RunHeadlessTicks(8);

        PhysicsSystem physics = engine.Context.GetService<PhysicsSystem>();
        Assert.Equal(1, projectile.ShotsFired);
        Assert.Equal(0, projectile.CollapsedFloatingIslands);
        Assert.Equal(0, physics.Stats.ActiveBodyCount);
        Assert.Equal("scan_window_external_connection", projectile.LastCollapseSkipReason);
    }

    /// <summary>
    /// 验证坍塌扫描跳过受支撑地形后，射击组件不会 fault，后续点击仍可继续开火。
    /// </summary>
    [Fact]
    public void PlayableProjectileKeepsFiringAfterCollapseScanSkipsSupportedTerrain()
    {
        MaterialTable materials = DemoMaterials();
        using Engine engine = CreateManualScriptEngine(out ScriptInputApi input, out CellGrid grid, out _, out ScriptScene scene, materials);
        Assert.True(materials.TryGetId("stone", out ushort stone));
        FillRect(grid, stone, minX: 28, minY: 20, maxX: 82, maxY: 50);

        Entity entity = scene.CreateEntity();
        _ = entity.AddComponent<Transform>();
        PlayerController player = entity.AddComponent<PlayerController>();
        player.SpawnX = 12f;
        player.SpawnY = 28f;
        PlayableProjectileTool projectile = entity.AddComponent<PlayableProjectileTool>();
        projectile.CooldownSeconds = 0f;
        projectile.Range = 96f;
        projectile.ImpactRadius = 2;
        projectile.CollapseScanRadius = 24;
        projectile.PlayerSupportProtectionRadius = 48;

        engine.RunHeadlessTicks(2);
        input.Update([], [MouseButton.Left], mouseX: 48f, mouseY: 34f, wheelY: 0f);
        engine.RunHeadlessTicks(1);
        input.Update([], [], mouseX: 48f, mouseY: 34f, wheelY: 0f);
        engine.RunHeadlessTicks(4);
        input.Update([], [MouseButton.Left], mouseX: 58f, mouseY: 34f, wheelY: 0f);
        engine.RunHeadlessTicks(1);

        Assert.False(projectile.Faulted, projectile.LastException?.ToString());
        Assert.Equal(2, projectile.ShotsFired);
        Assert.DoesNotContain("collapse_error", projectile.LastCollapseSkipReason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 验证一次射击转换首个悬空块后仍会分帧继续扫描，避免较大的残留碎块长时间静态浮空。
    /// </summary>
    [Fact]
    public void PlayableProjectileContinuesCollapseScanAfterFirstConvertedIsland()
    {
        RecordingAudioApi audio = new();
        MaterialTable materials = DemoMaterials();
        using Engine engine = CreateManualScriptEngine(out ScriptInputApi input, out CellGrid grid, out _, out ScriptScene scene, materials, audio);
        Assert.True(materials.TryGetId("stone", out ushort stone));
        FillRect(grid, stone, minX: 40, minY: 22, maxX: 48, maxY: 30);
        FillRect(grid, stone, minX: 56, minY: 20, maxX: 72, maxY: 32);

        Entity entity = scene.CreateEntity();
        _ = entity.AddComponent<Transform>();
        PlayerController player = entity.AddComponent<PlayerController>();
        player.SpawnX = 16f;
        player.SpawnY = 26f;
        PlayableProjectileTool projectile = entity.AddComponent<PlayableProjectileTool>();
        projectile.CooldownSeconds = 0f;
        projectile.Range = 96f;
        projectile.ImpactRadius = 1;
        projectile.CollapseScanRadius = 48;
        projectile.MaxCollapsedIslandsPerShot = 3;

        engine.RunHeadlessTicks(2);
        input.Update([], [MouseButton.Left], mouseX: 41f, mouseY: 26f, wheelY: 0f);
        engine.RunHeadlessTicks(1);
        input.Update([], [], mouseX: 41f, mouseY: 26f, wheelY: 0f);
        engine.RunHeadlessTicks(18);

        PhysicsSystem physics = engine.Context.GetService<PhysicsSystem>();
        Assert.Equal(1, projectile.ShotsFired);
        Assert.True(projectile.CollapsedFloatingIslands >= 2, $"应继续转换后续悬空块，status={projectile.CollapseStatus}");
        Assert.True(physics.Stats.ActiveBodyCount >= 2);
    }

    /// <summary>
    /// 验证可玩 Demo 进入场景时会显式关闭调试叠层，避免 dirty rect / 粒子轨迹等诊断点污染真实游玩画面。
    /// </summary>
    [Fact]
    public void PlayableWorldDirectorDisablesDebugOverlaysOnSceneStart()
    {
        using Engine engine = CreateScriptEngine(typeof(PlayableWorldDirector), out _, out _, out _);
        IDiagnosticsApi diagnostics = engine.Context.GetService<IDiagnosticsApi>();
        DebugOverlayKind[] overlays =
        [
            DebugOverlayKind.DirtyRects,
            DebugOverlayKind.CaIterationRects,
            DebugOverlayKind.ChunkGridParity,
            DebugOverlayKind.KeepAliveHotspots,
            DebugOverlayKind.CellParity,
            DebugOverlayKind.TemperatureHeatmap,
            DebugOverlayKind.OwnedByBody,
            DebugOverlayKind.ParticleTrails,
            DebugOverlayKind.ConnectedComponents,
        ];
        foreach (DebugOverlayKind overlay in overlays)
        {
            diagnostics.SetOverlay(overlay, enabled: true);
        }

        engine.RunHeadlessTicks(3);

        DebugOverlaySettings settings = engine.Context.GetService<DebugOverlaySettings>();
        Assert.Equal(default, settings.Enabled);
        Assert.All(overlays, overlay => Assert.False(diagnostics.IsOverlayEnabled(overlay)));
    }

    /// <summary>
    /// 验证真实可玩场景使用受控爆破与更宽的悬空块扫描配置，避免满屏高速碎点和弹坑残块长时间浮空。
    /// </summary>
    [Fact]
    public void PlayableWorldDirectorConfiguresProjectileForReadablePlayableDemo()
    {
        using Engine engine = CreateScriptEngine(typeof(PlayableWorldDirector), out _, out _, out _);

        engine.RunHeadlessTicks(3);

        PlayableProjectileTool projectile = FindBehaviour<PlayableProjectileTool>(engine);
        Assert.Equal(3, projectile.ImpactRadius);
        Assert.Equal(9f, projectile.ImpactForce);
        Assert.Equal(36, projectile.CollapseScanRadius);
        Assert.Equal(2, projectile.CollapseScanRetryFrames);
        Assert.Equal(18, projectile.FallbackOverhangRadius);
        Assert.Equal(48, projectile.MaxCollapseRegionSize);
        Assert.Equal(512, projectile.MaxCollapsePixels);
        Assert.Equal(1, projectile.MaxCollapsedIslandsPerShot);
        Assert.Equal(72, projectile.PlayerSupportProtectionRadius);
        Assert.False(projectile.AllowOverhangFallbackCollapse);
        Assert.False(projectile.AllowImpactFallbackCollapse);
    }

    /// <summary>
    /// 验证可玩 Demo 导演每帧揭示整个视口，避免真实游玩画面退化成点光源圆形可见范围。
    /// </summary>
    [Fact]
    public void PlayableWorldDirectorRevealsFullViewport()
    {
        using Engine engine = CreateScriptEngine(typeof(PlayableWorldDirector), out _, out _, out _);

        engine.RunHeadlessTicks(3);

        ScriptLightingSynchronizer lighting = engine.Context.GetService<ScriptLightingSynchronizer>();
        Assert.Equal(40, lighting.FogOfWar.ViewportCellWidth);
        Assert.Equal(20, lighting.FogOfWar.ViewportCellHeight);
        Assert.Equal(byte.MaxValue, lighting.FogOfWar.RevealAlpha(0, 0));
        Assert.Equal(byte.MaxValue, lighting.FogOfWar.RevealAlpha(39, 0));
        Assert.Equal(byte.MaxValue, lighting.FogOfWar.RevealAlpha(0, 19));
        Assert.Equal(byte.MaxValue, lighting.FogOfWar.RevealAlpha(39, 19));
    }

    /// <summary>
    /// 验证普通脚本化窗口短跑的第一下左键用于右侧山体坍塌验证，而不是旧的起点刷子输入。
    /// </summary>
    [Fact]
    public void ScriptedPlayableDemoFirstLeftClickTargetsCollapseTerrain()
    {
        using Engine engine = CreateBaseEngine(out ScriptInputApi input, out _, out ScriptCameraApi camera);
        DemoWindowScriptedInput scripted = new(input, camera);
        scripted.RegisterPhases(engine.Phases);

        int firstLeftClickFrame = -1;
        Point2F firstLeftClickWorld = default;
        for (int frame = 0; frame < 140; frame++)
        {
            _ = engine.RunOneTick(1.0 / 60.0);
            if (!input.WasMousePressed(MouseButton.Left))
            {
                continue;
            }

            firstLeftClickFrame = frame;
            (float mouseX, float mouseY) = input.MousePixel;
            firstLeftClickWorld = camera.ScreenToWorld(mouseX, mouseY);
            break;
        }

        Assert.Equal(124, firstLeftClickFrame);
        Assert.Equal(scripted.PlayableCollapseTargetWorld.X, firstLeftClickWorld.X, precision: 3);
        Assert.Equal(scripted.PlayableCollapseTargetWorld.Y, firstLeftClickWorld.Y, precision: 3);
        Assert.NotEqual(scripted.BrushTargetWorld, scripted.PlayableCollapseTargetWorld);
    }

    /// <summary>
    /// 验证贴墙时跳跃输入会触发蹬墙，离开墙面并获得反向水平位移。
    /// </summary>
    [Fact]
    public void PlayerWallJumpsAwayFromSolidWall()
    {
        using Engine engine = CreatePlayerEngine(out ScriptInputApi input, out CellGrid grid);
        FillWall(grid, material: 1, x: 32, y0: 16, y1: 48);

        engine.RunHeadlessTicks(1);
        PlayerController player = FindPlayer(engine);
        player.SpawnX = 32.95f;
        player.Respawn();

        engine.RunHeadlessTicks(1);
        Assert.True(player.State.OnWallLeft, $"玩家应贴左墙，state={Describe(player.State)}");

        float beforeJumpX = player.State.X;
        input.Update([Key.Space], [], mouseX: 0, mouseY: 0, wheelY: 0);
        engine.RunHeadlessTicks(2);

        Assert.True(player.State.X > beforeJumpX, $"蹬左墙后应向右离墙，before={beforeJumpX}, state={Describe(player.State)}");
        Assert.True(player.State.AppliedDeltaY < 0f, $"蹬墙后应产生向上的位移，state={Describe(player.State)}");
    }

    private static Engine CreatePlayerEngine(out ScriptInputApi input, out CellGrid grid)
    {
        return CreateScriptEngine(typeof(PlayerController), out input, out grid, out _);
    }

    private static Engine CreateManualScriptEngine(
        out ScriptInputApi input,
        out CellGrid grid,
        out ScriptCameraApi camera,
        out ScriptScene scene,
        MaterialTable? materials = null,
        IAudioApi? audio = null,
        string? contentRoot = null)
    {
        Engine engine = CreateBaseEngine(out input, out grid, out camera, materials: materials, audio: audio, contentRoot: contentRoot);
        scene = new ScriptScene();
        engine.Context.RegisterService(scene);
        _ = engine.AttachScriptingFromServices();
        return engine;
    }

    private static Engine CreateScriptEngine(Type entryType, out ScriptInputApi input, out CellGrid grid, out ScriptCameraApi camera)
    {
        Engine engine = CreateBaseEngine(out input, out grid, out camera, entryType);
        engine.RegisterScriptAssembly(entryType.Assembly);
        engine.RegisterScriptAssembly(typeof(PlayerController).Assembly);
        ScriptSimulationContext scripts = engine.AttachScriptingFromServices();
        Assert.Same(input, scripts.Input);
        Assert.Same(camera, scripts.Camera);
        return engine;
    }

    private static Engine CreateBaseEngine(
        out ScriptInputApi input,
        out CellGrid grid,
        out ScriptCameraApi camera,
        Type? entryType = null,
        MaterialTable? materials = null,
        IAudioApi? audio = null,
        string? contentRoot = null)
    {
        materials ??= Materials(("empty", CellType.Empty), ("stone", CellType.Solid));
        EngineBuilder builder = new EngineBuilder()
            .UseHeadless()
            .UseDeterministicMode();
        if (!string.IsNullOrWhiteSpace(contentRoot))
        {
            _ = builder.WithContentRoot(contentRoot);
        }

        if (entryType is not null)
        {
            _ = builder
                .AddScene(new SceneDescriptor("test", SceneSourceKind.Procedural, entryType.FullName!))
                .WithStartScene("test");
        }

        Engine engine = builder.Build();
        engine.Context.RegisterService(materials);
        _ = engine.AttachResidentSimulationWorld(worldWidthCells: 96, worldHeightCells: 64, particleCapacity: 16);
        _ = engine.AttachPhysics();
        input = new ScriptInputApi();
        camera = new ScriptCameraApi(viewportWidth: 40, viewportHeight: 20, centerX: 20, centerY: 10, zoom: 1);
        engine.Context.RegisterService<IInputApi>(EngineServiceRole.Input, input);
        engine.Context.RegisterService(input);
        engine.Context.RegisterService<ICameraApi>(EngineServiceRole.Camera, camera);
        engine.Context.RegisterService(camera);
        engine.Context.RegisterService<IAudioApi>(EngineServiceRole.AudioService, audio ?? NoOpAudioApi.Instance);
        grid = engine.Context.GetService<CellGrid>();
        return engine;
    }

    private static string DemoContentRoot()
    {
        return Path.Combine(FindRepositoryRoot(), "demo", "PixelEngine.Demo", "content");
    }

    private static float MaxTemperature(TemperatureField temperature, int minX, int minY, int maxX, int maxY)
    {
        float max = 0f;
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                max = MathF.Max(max, temperature.GetTemperature(x, y));
            }
        }

        return max;
    }

    private static int CountMaterial(CellGrid grid, ushort material, int minX, int minY, int maxX, int maxY)
    {
        int count = 0;
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (grid.GetMaterial(x, y) == material)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static string CreateTemporaryWeaponContent(string weaponsJson)
    {
        string directory = Path.Combine(Path.GetTempPath(), "pixelengine-weapon-tests-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "weapons.json"), weaponsJson);
        return directory;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PixelEngine.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("未找到仓库根目录。");
    }

    private static PlayerController FindPlayer(Engine engine)
    {
        return FindBehaviour<PlayerController>(engine);
    }

    private static TBehaviour FindBehaviour<TBehaviour>(Engine engine)
        where TBehaviour : Behaviour
    {
        ScriptScene scene = engine.Context.GetService<ScriptScene>();
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

        throw new InvalidOperationException($"未找到 {typeof(TBehaviour).Name}。");
    }

    private static string Describe(CharacterState state)
    {
        return $"x={state.X}, y={state.Y}, ground={state.OnGround}, wallL={state.OnWallLeft}, wallR={state.OnWallRight}, req=({state.RequestedDeltaX},{state.RequestedDeltaY}), applied=({state.AppliedDeltaX},{state.AppliedDeltaY})";
    }

    private static float MaxBodyVelocityX(PhysicsSystem physics)
    {
        RigidBodySnapshot[] snapshots = new RigidBodySnapshot[Math.Max(1, physics.PhysicsWorld.ActiveBodyCount)];
        int count = physics.CopyBodySnapshots(snapshots);
        float max = 0f;
        for (int i = 0; i < count; i++)
        {
            max = MathF.Max(max, snapshots[i].LinearVelocityPixelsPerSecond.X);
        }

        return max;
    }

    private static void FillFloor(CellGrid grid, ushort material, int y, int x0, int x1, bool rigidOwned)
    {
        for (int x = x0; x < x1; x++)
        {
            grid.MaterialAt(x, y) = material;
            grid.FlagsAt(x, y) = rigidOwned ? CellFlags.RigidOwned : default;
        }
    }

    private static void FillWall(CellGrid grid, ushort material, int x, int y0, int y1)
    {
        for (int y = y0; y < y1; y++)
        {
            grid.MaterialAt(x, y) = material;
            grid.FlagsAt(x, y) = default;
        }
    }

    private static void FillRect(CellGrid grid, ushort material, int minX, int minY, int maxX, int maxY)
    {
        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                grid.MaterialAt(x, y) = material;
                grid.FlagsAt(x, y) = default;
            }
        }
    }

    private static void FillSteppedPile(CellGrid grid, ushort material, int startX, int endX, int baseY)
    {
        for (int x = startX; x < endX; x++)
        {
            int surface = baseY - Math.Min(5, Math.Abs(x - 28) / 4);
            for (int y = surface; y < 48; y++)
            {
                grid.MaterialAt(x, y) = material;
                grid.FlagsAt(x, y) = default;
            }
        }
    }

    private static MaterialTable Materials(params (string Name, CellType Type)[] definitions)
    {
        return Materials(destructibleSolids: true, definitions);
    }

    private static MaterialTable Materials(bool destructibleSolids, params (string Name, CellType Type)[] definitions)
    {
        MaterialDef[] materials = new MaterialDef[definitions.Length];
        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = new MaterialDef
            {
                Id = (ushort)i,
                Name = definitions[i].Name,
                Type = definitions[i].Type,
                Density = i == 0 ? (byte)0 : (byte)100,
                HeatCapacity = 1,
                HeatConduct = byte.MaxValue,
                TextureId = -1,
                MeltPoint = float.NaN,
                FreezePoint = float.NaN,
                BoilPoint = float.NaN,
                Integrity = destructibleSolids && definitions[i].Type == CellType.Solid ? (ushort)40 : (ushort)0,
                DestroyedTarget = destructibleSolids && definitions[i].Type == CellType.Solid ? (ushort)1 : (ushort)0,
            };
        }

        return new MaterialTable(materials);
    }

    private static MaterialTable DemoMaterials(bool destructibleSolids = true)
    {
        return Materials(
            destructibleSolids,
            ("empty", CellType.Empty),
            ("sand", CellType.Powder),
            ("water", CellType.Liquid),
            ("oil", CellType.Liquid),
            ("lava", CellType.Liquid),
            ("fire", CellType.Gas),
            ("stone", CellType.Solid),
            ("wood", CellType.Solid),
            ("acid", CellType.Liquid),
            ("ice", CellType.Solid),
            ("metal", CellType.Solid),
            ("ash", CellType.Powder));
    }

    private sealed class NoOpAudioApi : IAudioApi
    {
        public static NoOpAudioApi Instance { get; } = new();

        public void PlayOneShot(string cue, float volume = 1f)
        {
        }

        public void PlayAt(string cue, float x, float y, float volume = 1f)
        {
        }
    }

    private sealed class RecordingAudioApi : IAudioApi
    {
        public string LastCue { get; private set; } = string.Empty;

        public float LastX { get; private set; }

        public float LastY { get; private set; }

        public void PlayOneShot(string cue, float volume = 1f)
        {
            LastCue = cue;
        }

        public void PlayAt(string cue, float x, float y, float volume = 1f)
        {
            LastCue = cue;
            LastX = x;
            LastY = y;
        }
    }

    private sealed class RecordingGuiContext(
        IEnumerable<string>? clickedButtons = null,
        IEnumerable<string>? toggledCheckboxes = null) : IGuiContext
    {
        private readonly HashSet<string> _clickedButtons = clickedButtons is null
            ? []
            : new HashSet<string>(clickedButtons, StringComparer.Ordinal);
        private readonly HashSet<string> _toggledCheckboxes = toggledCheckboxes is null
            ? []
            : new HashSet<string>(toggledCheckboxes, StringComparer.Ordinal);

        public int Width => 800;

        public int Height => 450;

        public float DeltaTime => 1f / 60f;

        public bool WantsMouse => false;

        public bool WantsKeyboard => false;

        public List<string> Drawn { get; } = [];

        public void SetNextWindow(float x, float y, float width, float height, GuiCondition condition = GuiCondition.Always)
        {
            Drawn.Add($"next:{x},{y},{width},{height},{condition}");
        }

        public bool BeginWindow(string id, string title, GuiWindowFlags flags = GuiWindowFlags.None)
        {
            Drawn.Add($"begin:{id}:{title}:{flags}");
            return true;
        }

        public void EndWindow()
        {
            Drawn.Add("end");
        }

        public void Text(string text)
        {
            Drawn.Add($"text:{text}");
        }

        public void TextColored(string text, uint colorBgra)
        {
            Drawn.Add($"text-colored:{text}:{colorBgra:X8}");
        }

        public void SameLine()
        {
            Drawn.Add("same-line");
        }

        public void Separator()
        {
            Drawn.Add("separator");
        }

        public bool Button(string label)
        {
            Drawn.Add($"button:{label}");
            return _clickedButtons.Contains(label);
        }

        public bool Checkbox(string label, ref bool value)
        {
            Drawn.Add($"checkbox:{label}:{value}");
            if (!_toggledCheckboxes.Contains(label))
            {
                return false;
            }

            value = !value;
            return true;
        }

        public void ProgressBar(float value01, string? label = null)
        {
            Drawn.Add($"progress:{value01}:{label}");
        }

        public void ColorSwatch(string id, uint colorBgra, float size = 16)
        {
            Drawn.Add($"swatch:{id}:{colorBgra:X8}:{size}");
        }
    }

}
