using System.Buffers.Binary;
using PixelEngine.Core;
using PixelEngine.Core.Events;
using PixelEngine.Core.Threading;
using PixelEngine.Interop.Box2D;
using PixelEngine.Physics;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;
using PixelEngine.Core.Time;
using PixelEngine.Audio;
using Xunit;
using ScriptScene = PixelEngine.Scripting.Scene;

namespace PixelEngine.Scripting.Tests;

/// <summary>
/// ScriptSimulationContext facade 的真实 Simulation/Physics 后端验收。
/// 不变式：未注入服务走 noop、世界写入经延迟命令队列、材质/cell/刚体采样直读后端。
/// </summary>
public sealed class ScriptSimulationContextTests
{
    /// <summary>
    /// 验证未注入 Game UI 后端时真实脚本上下文返回空服务，而不是抛异常。
    /// </summary>
    [Fact]
    public void GameUiDefaultsToNoopServiceWhenBackendIsNotInjected()
    {
        using Fixture fixture = Fixture.Create();

        IGameUiService gameUi = fixture.Context.GameUi;
        UiScreenHandle screen = gameUi.ShowScreen("main");
        gameUi.SetValue(screen, new UiPathId(7), new UiValue(12L));
        gameUi.Invoke(screen, new UiActionId(3), default);

        Assert.Same(NoopGameUiService.Instance, gameUi);
        Assert.Equal(default, screen);
        Assert.False(gameUi.TryGetValue(screen, new UiPathId(7), out UiValue value));
        Assert.Equal(default, value);
    }

    /// <summary>
    /// 验证 UI 事件处理器中的世界写入只进入延迟命令队列，在 cell 安全窗口 flush 前不改网格。
    /// </summary>
    [Fact]
    public void UiEventHandlersQueueWorldWritesUntilCellCommandFlush()
    {
        // Arrange：准备输入与初始状态
        using Fixture fixture = Fixture.Create();
        MaterialId stone = fixture.Context.Materials.Resolve("stone");
        _ = fixture.Context.Events.Subscribe<UiEvent>(_ => fixture.Context.Cells.SetCell(5, 5, stone));
        UiEvent uiEvent = new(
            new UiScreenHandle(1),
            new UiElementId(2),
            new UiActionId(3),
            default);

        // Assert：验证预期结果
        Assert.True(fixture.Context.Events.TryPublish(in uiEvent));
        fixture.ScriptEvents.DrainEvents();

        Assert.Equal((ushort)0, fixture.Grid.GetMaterial(5, 5));

        int flushed = fixture.Context.FlushCellCommands();

        Assert.Equal(1, flushed);
        Assert.Equal(stone.Value, fixture.Grid.GetMaterial(5, 5));
    }

    /// <summary>
    /// 验证材质、cell 与固体采样 facade 直接读取真实 Simulation 后端。
    /// </summary>
    [Fact]
    public void FacadesReadMaterialCellsAndSolidsFromSimulationBackends()
    {
        // Arrange：准备输入与初始状态
        Fixture fixture = Fixture.Create();
        fixture.Grid.SetMaterial(4, 4, 2);
        fixture.Grid.FlagsAt(4, 4) = 7;
        fixture.Grid.LifetimeAt(4, 4) = 9;
        fixture.Grid.DamageAt(4, 4) = 11;

        // Assert：验证预期结果
        Assert.True(fixture.Context.Materials.TryResolve("stone", out MaterialId stone));
        Assert.Equal((ushort)2, stone.Value);
        MaterialInfo info = fixture.Context.Materials.GetInfo(stone);
        Assert.Equal("stone", info.Name);
        Assert.True(info.IsSolid);
        Assert.Equal("Stone", info.DisplayName);
        Assert.Equal(0xFF776655u, info.BaseColorBgra);
        Assert.Equal(CellType.Solid, info.CellType);
        Assert.Equal(MaterialLegendCategory.Destructible, info.Category);
        Assert.Equal("Destructible", info.LegendCategory);
        Assert.True(info.Emissive);
        Assert.Equal(6, info.Hardness);
        Assert.Equal(40, info.MaxIntegrity);
        Assert.True(info.IsDestructible);
        Assert.Equal(3, info.FlowRate);
        Assert.Equal(91, info.Flammability);
        Assert.Equal(320, info.AutoIgnitionTemp);
        Assert.Equal(44, info.FireHp);
        Assert.Equal(120, info.TemperatureOfFire);
        Assert.Equal(7, info.GeneratesSmoke);
        Assert.Equal(177, info.HeatConduct);
        Assert.Equal(2.5f, info.HeatCapacity);
        Assert.Equal(MaterialRenderStyle.Emissive, info.RenderStyle);
        Assert.True((info.Properties & MaterialProperty.Diggable) != 0);
        Assert.Equal(stone, fixture.Context.Cells.GetMaterial(4, 4));
        Assert.Equal(new CellView(stone, 7, 9, 29), fixture.Context.Cells.Sample(4, 4));
        Assert.True(fixture.Context.Cells.IsSolid(4, 4));
        Assert.False(fixture.Context.Cells.IsRigidOwned(4, 4));
        Assert.True(fixture.Context.Solids.SampleSolidAabb(3.5f, 3.5f, 2, 2));

        Assert.True(fixture.Context.Solids.Raycast(0, 4, 1, 0, 8, out RaycastHit hit));
        Assert.Equal(4, hit.X);
        Assert.Equal(4, hit.Y);
        Assert.Equal(stone, hit.Material);
    }

    /// <summary>
    /// 验证材质热重载后脚本材质只读投影会读取同一 MaterialTable 的新字段。
    /// </summary>
    [Fact]
    public void MaterialInfoFacadeReflectsMaterialReload()
    {
        // Arrange：准备输入与初始状态
        Fixture fixture = Fixture.Create();

        // Assert：验证预期结果
        Assert.True(fixture.Materials.TryGetId("stone", out ushort stoneId));
        MaterialInfo before = fixture.Context.Materials.GetInfo(new MaterialId(stoneId));
        Assert.Equal(6, before.Hardness);
        Assert.Equal(40, before.MaxIntegrity);

        MaterialDef[] reloaded =
        [
            CreateMaterial(0, "empty", CellType.Empty),
            CreateMaterial(0, "sand", CellType.Powder),
            CreateMaterial(0, "stone", CellType.Solid) with
            {
                Density = 210,
                Dispersion = 5,
                Hardness = 12,
                Integrity = 96,
                DestroyedTarget = 1,
                BaseColorBGRA = 0xFF102030u,
                DisplayName = "Reloaded Stone",
                LegendCategory = MaterialLegendCategory.Resource,
                RenderStyle = MaterialRenderStyle.Solid,
                PropertyFlags = MaterialProperty.Diggable,
                MineYield = 2,
                Flammability = 33,
                AutoIgnitionTemp = 451,
                FireHp = 22,
                TemperatureOfFire = 88,
                GeneratesSmoke = 9,
                HeatConduct = 111,
                HeatCapacity = 3.25f,
            },
        ];

        MaterialReloadResult reload = fixture.Materials.ReloadStable(reloaded, fallbackId: 0);
        MaterialInfo after = fixture.Context.Materials.GetInfo(new MaterialId(stoneId));

        Assert.Equal(3, reload.PreservedCount);
        Assert.Equal("Reloaded Stone", after.DisplayName);
        Assert.Equal(0xFF102030u, after.BaseColorBgra);
        Assert.Equal(MaterialLegendCategory.Resource, after.Category);
        Assert.Equal("Resource", after.LegendCategory);
        Assert.False(after.Emissive);
        Assert.Equal(12, after.Hardness);
        Assert.Equal(96, after.MaxIntegrity);
        Assert.Equal(5, after.FlowRate);
        Assert.Equal(2, after.MineYield);
        Assert.Equal(33, after.Flammability);
        Assert.Equal(451, after.AutoIgnitionTemp);
        Assert.Equal(22, after.FireHp);
        Assert.Equal(88, after.TemperatureOfFire);
        Assert.Equal(9, after.GeneratesSmoke);
        Assert.Equal(111, after.HeatConduct);
        Assert.Equal(3.25f, after.HeatCapacity);
        Assert.Equal(MaterialRenderStyle.Solid, after.RenderStyle);
        Assert.Equal(MaterialProperty.Diggable, after.Properties);
    }

    /// <summary>
    /// 验证脚本 cell 写命令延迟到 flush 后写入 current dirty，供本次 CA 可见。
    /// </summary>
    [Fact]
    public void CellCommandsFlushIntoWorkingDirtyWithoutImmediateMutation()
    {
        // Arrange：准备输入与初始状态
        Fixture fixture = Fixture.Create();
        MaterialId sand = fixture.Context.Materials.Resolve("sand");

        fixture.Context.Cells.SetCell(2, 2, sand);
        // Assert：验证预期结果
        Assert.Equal((ushort)0, fixture.Grid.GetMaterial(2, 2));
        Assert.True(fixture.Chunk.WorkingDirty.IsEmpty);

        int flushed = fixture.Context.FlushCellCommands();

        Assert.Equal(1, flushed);
        Assert.Equal(sand.Value, fixture.Grid.GetMaterial(2, 2));
        Assert.False(fixture.Chunk.CurrentDirty.IsEmpty);
        Assert.True(fixture.Chunk.WorkingDirty.IsEmpty);

        fixture.Kernel.SwapDirtyRects();
        Assert.True(fixture.Chunk.CurrentDirty.IsEmpty);
        Assert.True(fixture.Chunk.WorkingDirty.IsEmpty);
    }

    /// <summary>
    /// 验证脚本 Paint 与 SpawnParticle 进入独立队列，分别 flush 后互不丢失。
    /// </summary>
    [Fact]
    public void CellAndParticleCommandsFlushIndependently()
    {
        // Arrange：准备输入与初始状态
        Fixture fixture = Fixture.Create();
        MaterialId stone = fixture.Context.Materials.Resolve("stone");

        fixture.Context.Cells.Paint(6, 6, radius: 1, stone);
        fixture.Context.Particles.Spawn(new ParticleSpawnDesc(6, 6, 0, 0, stone, 10));

        // Assert：验证预期结果
        Assert.Equal(1, fixture.Context.FlushCellCommands());
        Assert.Equal(stone.Value, fixture.Grid.GetMaterial(6, 6));
        Assert.False(fixture.Chunk.CurrentDirty.IsEmpty);
        Assert.Equal(0, fixture.Particles.ActiveCount);

        Assert.Equal(1, fixture.Context.FlushParticleCommands());
        Assert.Equal(1, fixture.Particles.ActiveCount);
        Assert.Equal(stone.Value, fixture.Particles.ActiveReadOnly[0].Material);
    }

    /// <summary>
    /// 验证脚本粒子命令延迟到粒子 flush 后进入真实 ParticleSystem。
    /// </summary>
    [Fact]
    public void ParticleCommandsFlushIntoParticleSystem()
    {
        // Arrange：准备输入与初始状态
        Fixture fixture = Fixture.Create();
        MaterialId sand = fixture.Context.Materials.Resolve("sand");

        fixture.Context.Particles.Spawn(new ParticleSpawnDesc(1, 2, 3, 4, sand, 5));
        fixture.Context.Particles.Burst(8, 9, sand, count: 3, speed: 6);
        fixture.Context.Particles.Emit(new ParticleEmit(
            X: 4,
            Y: 5,
            Material: sand,
            Count: 2,
            DirAngleRad: 0f,
            DirSpreadRad: 0f,
            BaseSpeed: 60f,
            SpeedJitter: 0f,
            LifeTicks: 7));
        // Assert：验证预期结果
        Assert.Equal(0, fixture.Particles.ActiveCount);

        int flushed = fixture.Context.FlushParticleCommands();

        Assert.Equal(3, flushed);
        Assert.Equal(6, fixture.Particles.ActiveCount);
        Particle first = fixture.Particles.ActiveReadOnly[0];
        Assert.Equal(1, first.X);
        Assert.Equal(2, first.Y);
        Assert.Equal(3f / 60f, first.Vx, precision: 5);
        Assert.Equal(4f / 60f, first.Vy, precision: 5);
        Assert.Equal(sand.Value, first.Material);
        Assert.Equal(5, first.Life);
        for (int i = 1; i < fixture.Particles.ActiveCount; i++)
        {
            if (i < 4)
            {
                Assert.Equal(EngineConstants.ParticleMaxLifetimeTicks, fixture.Particles.ActiveReadOnly[i].Life);
                continue;
            }

            Assert.Equal(4f, fixture.Particles.ActiveReadOnly[i].X);
            Assert.Equal(5f, fixture.Particles.ActiveReadOnly[i].Y);
            Assert.Equal(1f, fixture.Particles.ActiveReadOnly[i].Vx, precision: 5);
            Assert.Equal(0f, fixture.Particles.ActiveReadOnly[i].Vy, precision: 5);
            Assert.Equal(7, fixture.Particles.ActiveReadOnly[i].Life);
        }
    }

    /// <summary>
    /// 验证方向向量 + 速度区间 Emit 重载会映射为真实速度锥发射，并按 fixed step 缩放速度。
    /// </summary>
    [Fact]
    public void ParticleEmitVelocityConeOverloadMapsDirectionAndSpeedRange()
    {
        // Arrange：准备输入与初始状态
        Fixture fixture = Fixture.Create();
        MaterialId sand = fixture.Context.Materials.Resolve("sand");

        fixture.Context.Particles.Emit(
            originX: 10,
            originY: 11,
            dirX: 0,
            dirY: -2,
            coneRadians: 0f,
            minSpeed: 30f,
            maxSpeed: 90f,
            count: 3,
            material: sand,
            lifeTicks: 6);

        // Assert：验证预期结果
        Assert.Equal(1, fixture.Context.FlushParticleCommands());
        Assert.Equal(3, fixture.Particles.ActiveCount);
        for (int i = 0; i < fixture.Particles.ActiveCount; i++)
        {
            Particle particle = fixture.Particles.ActiveReadOnly[i];
            Assert.Equal(10f, particle.X);
            Assert.Equal(11f, particle.Y);
            Assert.Equal(0f, particle.Vx, precision: 5);
            Assert.InRange(particle.Vy, -1.5f, -0.5f);
            Assert.Equal(6, particle.Life);
            Assert.Equal(sand.Value, particle.Material);
        }
    }

    /// <summary>
    /// 验证脚本 Emit 命令入队与 flush 在预热后不产生托管堆分配。
    /// </summary>
    [Fact]
    public void ParticleEmitCommandsDoNotAllocateAfterWarmup()
    {
        // Arrange：准备输入与初始状态
        using Fixture fixture = Fixture.Create();
        MaterialId sand = fixture.Context.Materials.Resolve("sand");
        ParticleEmit emit = new(4, 5, sand, Count: 1, DirAngleRad: 0f, DirSpreadRad: 0.1f, BaseSpeed: 60f, SpeedJitter: 3f, LifeTicks: 7);
        const int Iterations = 128;

        fixture.Context.Particles.Emit(in emit);
        // Assert：验证预期结果
        Assert.Equal(1, fixture.Context.FlushParticleCommands());
        fixture.Particles.Clear();

        long before = GC.GetAllocatedBytesForCurrentThread();
        int flushed = 0;
        for (int i = 0; i < Iterations; i++)
        {
            fixture.Context.Particles.Emit(in emit);
            flushed += fixture.Context.FlushParticleCommands();
            fixture.Particles.Clear();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(Iterations, flushed);
        Assert.Equal(0, allocated);
    }

    /// <summary>
    /// 验证 Emit 生成的短寿命视觉粒子会按 lifetime 退场，不形成粒子泄漏。
    /// </summary>
    [Fact]
    public void ParticleEmitParticlesUseFiniteLifetimeAndExpire()
    {
        // Arrange：准备输入与初始状态
        Fixture fixture = Fixture.Create();
        MaterialId sand = fixture.Context.Materials.Resolve("sand");
        ParticleEmit emit = new(8, 9, sand, Count: 4, DirAngleRad: 0f, DirSpreadRad: 0f, BaseSpeed: 60f, SpeedJitter: 0f, LifeTicks: 3);

        fixture.Context.Particles.Emit(in emit);
        // Assert：验证预期结果
        Assert.Equal(1, fixture.Context.FlushParticleCommands());
        Assert.Equal(4, fixture.Particles.ActiveCount);

        for (int i = 0; i < 3; i++)
        {
            fixture.Particles.IntegrateAndAdvance(fixture.Grid);
            fixture.Particles.ResolveDeposits(fixture.Kernel, fixture.Grid);
        }

        Assert.Equal(0, fixture.Particles.ActiveCount);
        Assert.Equal(4, fixture.Particles.Stats.KilledByLifetimeThisTick);
    }

    /// <summary>
    /// 验证 burst 视觉粒子使用有限默认寿命，既不会下一帧立即死亡，也不会无限残留。
    /// </summary>
    [Fact]
    public void BurstParticlesUseFiniteDefaultLifetimeAndExpire()
    {
        // Arrange：准备输入与初始状态
        Fixture fixture = Fixture.Create();
        MaterialId sand = fixture.Context.Materials.Resolve("sand");

        fixture.Context.Particles.Burst(8, 9, sand, count: 2, speed: 6);

        // Assert：验证预期结果
        Assert.Equal(1, fixture.Context.FlushParticleCommands());
        Assert.Equal(2, fixture.Particles.ActiveCount);
        Assert.Equal(EngineConstants.ParticleMaxLifetimeTicks, fixture.Particles.ActiveReadOnly[0].Life);
        Assert.Equal(EngineConstants.ParticleMaxLifetimeTicks, fixture.Particles.ActiveReadOnly[1].Life);

        for (int i = 0; i < EngineConstants.ParticleMaxLifetimeTicks; i++)
        {
            fixture.Particles.IntegrateAndAdvance(fixture.Grid);
            fixture.Particles.ResolveDeposits(fixture.Kernel, fixture.Grid);
        }

        Assert.Equal(0, fixture.Particles.ActiveCount);
    }

    /// <summary>
    /// 验证脚本世界破坏 API 会在 cell 安全相位落地抗性感知区域伤害。
    /// </summary>
    [Fact]
    public void WorldDamageCommandsFlushIntoStructuralDamage()
    {
        // Arrange：准备输入与初始状态
        Fixture fixture = Fixture.Create();
        MaterialId stone = fixture.Context.Materials.Resolve("stone");
        MaterialId sand = fixture.Context.Materials.Resolve("sand");
        FillRect(fixture.Chunk, minX: 12, minY: 12, maxX: 18, maxY: 18, material: stone.Value);

        fixture.Context.World.DamageCircle(15f, 15f, radius: 3, damage: 80f, falloff: false);
        fixture.Context.World.DamageBeam(12f, 12f, 1f, 0f, length: 5, damagePerCell: 80f);

        // Assert：验证预期结果
        Assert.Equal(stone.Value, fixture.Grid.GetMaterial(15, 15));
        Assert.Equal(2, fixture.Context.FlushCellCommands());

        Assert.Equal(sand.Value, fixture.Grid.GetMaterial(15, 15));
        Assert.Equal(sand.Value, fixture.Grid.GetMaterial(12, 12));
        Assert.False(fixture.Chunk.WorkingDirty.IsEmpty);
    }

    /// <summary>
    /// 验证脚本热量命令延迟到 cell 安全相位落地，并标记对应区域 dirty。
    /// </summary>
    [Fact]
    public void WorldAddHeatFlushesIntoTemperatureField()
    {
        Fixture fixture = Fixture.Create();

        fixture.Context.World.AddHeat(10f, 10f, radius: 1, deltaCelsius: 80f);
        Assert.Equal(0f, fixture.Temperature.GetTemperature(10, 10));

        int flushed = fixture.Context.FlushCellCommands();

        Assert.Equal(1, flushed);
        Assert.True(fixture.Temperature.GetTemperature(10, 10) > 0f);
        Assert.False(fixture.Chunk.CurrentDirty.IsEmpty);
    }

    /// <summary>
    /// 验证 Demo 破坏 API 的脚本命令入队与安全相位 flush 稳态不产生托管堆分配。
    /// </summary>
    [Fact]
    public void WorldDamageAndHeatCommandsDoNotAllocateAfterWarmup()
    {
        // Arrange：准备输入与初始状态
        using Fixture fixture = Fixture.Create();
        MaterialId stone = fixture.Context.Materials.Resolve("stone");
        const int Iterations = 128;

        QueueDamageAndHeat(fixture);
        // Assert：验证预期结果
        Assert.Equal(3, fixture.Context.FlushCellCommands());

        long before = GC.GetAllocatedBytesForCurrentThread();
        int flushed = 0;
        for (int i = 0; i < Iterations; i++)
        {
            FillRect(fixture.Chunk, minX: 20, minY: 20, maxX: 30, maxY: 30, material: stone.Value);
            QueueDamageAndHeat(fixture);
            flushed += fixture.Context.FlushCellCommands();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(Iterations * 3, flushed);
        Assert.Equal(0, allocated);
    }

    /// <summary>
    /// 验证脚本世界爆炸 API 会先按材质抗性破坏 solid，再抛射碎屑/非固体，并对半径内刚体施加径向冲量。
    /// </summary>
    [Fact]
    public void WorldExplodeFlushesStructuralDamageDebrisAndRigidImpulse()
    {
        // Arrange：准备输入与初始状态
        using Fixture fixture = Fixture.Create(withPhysics: true);
        MaterialId sand = fixture.Context.Materials.Resolve("sand");
        MaterialId stone = fixture.Context.Materials.Resolve("stone");
        FillRect(fixture.Chunk, minX: 48, minY: 48, maxX: 60, maxY: 60, material: 2);
        BodyHandle handle = fixture.Context.Bodies.CreateFromRegion(48, 48, 12, 12);
        // Assert：验证预期结果
        Assert.Equal(1, fixture.Context.FlushPhysicsCommands());
        Assert.True(fixture.Context.Bodies.TryGetTransform(handle, out _));
        FillRect(fixture.Chunk, minX: 32, minY: 32, maxX: 36, maxY: 36, material: stone.Value);

        fixture.Context.World.Explode(34f, 34f, radius: 32, force: 80f);

        Assert.Equal(1, fixture.Context.FlushCellCommands());
        Assert.Equal(sand.Value, fixture.Grid.GetMaterial(34, 34));
        Assert.Equal(1, fixture.Context.FlushParticleCommands());
        fixture.Particles.RunEjectionPass(fixture.Kernel, fixture.Grid);
        Assert.True(fixture.Particles.ActiveCount > 0);
        Assert.True(AnyCleared(fixture.Grid, minX: 32, minY: 32, maxX: 36, maxY: 36));

        Assert.Equal(1, fixture.Context.FlushPhysicsCommands());
        PixelRigidBody body = fixture.Physics!.PhysicsWorld.GetBody(0);
        B2Vec2 velocity = Box2D.b2Body_GetLinearVelocity(body.BodyId);
        Assert.True(velocity.X > 0f);
    }

    /// <summary>
    /// 验证脚本爆炸只把粉末碎屑抛射为自由粒子，不把液体/smoke/fire 这种易被误读为特效的材质变成持久爆炸残留。
    /// </summary>
    [Fact]
    public void WorldExplodeDoesNotEjectLiquidGasOrFireAsPersistentVisualDebris()
    {
        // Arrange：准备输入与初始状态
        using Fixture fixture = Fixture.Create();
        MaterialId sand = fixture.Context.Materials.Resolve("sand");
        MaterialId water = fixture.Context.Materials.Resolve("water");
        MaterialId smoke = fixture.Context.Materials.Resolve("smoke");
        MaterialId fire = fixture.Context.Materials.Resolve("fire");
        fixture.Grid.SetMaterial(10, 10, sand.Value);
        fixture.Grid.SetMaterial(11, 10, water.Value);
        fixture.Grid.SetMaterial(12, 10, smoke.Value);
        fixture.Grid.SetMaterial(13, 10, fire.Value);

        fixture.Context.World.Explode(11f, 10f, radius: 4, force: 12f);

        // Assert：验证预期结果
        Assert.Equal(1, fixture.Context.FlushParticleCommands());
        fixture.Particles.RunEjectionPass(fixture.Kernel, fixture.Grid);

        Assert.Equal(1, fixture.Particles.ActiveCount);
        Assert.Contains(fixture.Particles.ActiveReadOnly.ToArray(), particle => particle.Material == sand.Value);
        Assert.DoesNotContain(
            fixture.Particles.ActiveReadOnly.ToArray(),
            particle => particle.Material == water.Value || particle.Material == smoke.Value || particle.Material == fire.Value);
        Assert.Equal((ushort)0, fixture.Grid.GetMaterial(10, 10));
        Assert.Equal(water.Value, fixture.Grid.GetMaterial(11, 10));
        Assert.Equal(smoke.Value, fixture.Grid.GetMaterial(12, 10));
        Assert.Equal(fire.Value, fixture.Grid.GetMaterial(13, 10));
    }

    /// <summary>
    /// 验证脚本角色控制器 facade 延迟到 Physics flush 后使用真实像素碰撞后端更新状态。
    /// </summary>
    [Fact]
    public void CharacterFacadeMovesAgainstSolidPixelsAndReturnsCollisionState()
    {
        // Arrange：准备输入与初始状态
        using Fixture fixture = Fixture.Create();
        FillRect(fixture.Chunk, minX: 0, minY: 10, maxX: 32, maxY: 11, material: 2);
        CharacterHandle handle = fixture.Context.Character.Create(4, 0, 4, 4);

        CharacterState pending = fixture.Context.Character.Move(handle, 0, 20);

        // Assert：验证预期结果
        Assert.Equal(4f, pending.X);
        Assert.Equal(0f, pending.Y);
        Assert.False(pending.OnGround);
        Assert.Equal(pending, fixture.Context.Character.GetState(handle));
        Assert.Equal(0, fixture.Context.FlushCellCommands());
        Assert.Equal(0, fixture.Context.FlushParticleCommands());
        Assert.Equal(pending, fixture.Context.Character.GetState(handle));

        int flushed = fixture.Context.FlushPhysicsCommands();

        Assert.Equal(1, flushed);
        CharacterState state = fixture.Context.Character.GetState(handle);
        Assert.True(state.OnGround);
        Assert.False(state.OnCeiling);
        Assert.False(state.OnWall);
        Assert.Equal(4f, state.X);
        Assert.Equal(6f, state.Y);
        Assert.Equal(4f, state.Width);
        Assert.Equal(4f, state.Height);
        Assert.Equal(20f, state.RequestedDeltaY);
        Assert.Equal(6f, state.AppliedDeltaY);
        Assert.Equal(0f, state.GroundNormalX, precision: 5);
        Assert.Equal(-1f, state.GroundNormalY, precision: 5);
        Assert.Equal(0, fixture.Context.FlushPhysicsCommands());
        Assert.Equal(state, fixture.Context.Character.GetState(handle));

        CharacterState teleported = fixture.Context.Character.SetPosition(handle, 12, 1);

        Assert.Equal(12f, teleported.X);
        Assert.Equal(1f, teleported.Y);
        Assert.Equal(0f, teleported.AppliedDeltaX);
        Assert.Equal(0f, teleported.AppliedDeltaY);
        Assert.Equal(teleported, fixture.Context.Character.GetState(handle));
    }

    /// <summary>
    /// 验证脚本刚体命令延迟到 Physics flush 后创建、查询和销毁真实刚体。
    /// </summary>
    [Fact]
    public void BodyFacadeFlushesCreateQueryImpulseAndDestroyThroughPhysics()
    {
        // Arrange：准备输入与初始状态
        using Fixture fixture = Fixture.Create(withPhysics: true);
        FillRect(fixture.Chunk, minX: 8, minY: 8, maxX: 24, maxY: 24, material: 2);

        BodyHandle handle = fixture.Context.Bodies.CreateFromRegion(8, 8, 16, 16);

        // Assert：验证预期结果
        Assert.False(fixture.Context.Bodies.TryGetTransform(handle, out _));
        Assert.Equal(0, fixture.Physics!.PhysicsWorld.ActiveBodyCount);
        Assert.Equal((ushort)2, fixture.Grid.GetMaterial(16, 16));

        int created = fixture.Context.FlushPhysicsCommands();

        Assert.Equal(1, created);
        Assert.Equal(1, fixture.Physics.PhysicsWorld.ActiveBodyCount);
        Assert.True(CellFlags.Has(fixture.Grid.FlagsAt(16, 16), CellFlags.RigidOwned));
        Assert.True(fixture.Context.Bodies.TryGetTransform(handle, out BodyTransform transform));
        Assert.Equal(16f, transform.X, precision: 3);
        Assert.Equal(16f, transform.Y, precision: 3);

        fixture.Context.Bodies.ApplyImpulse(handle, 32, 0);
        Assert.Equal(1, fixture.Context.FlushPhysicsCommands());

        fixture.Context.Bodies.Destroy(handle);
        Assert.Equal(1, fixture.Context.FlushPhysicsCommands());

        Assert.Equal(0, fixture.Physics.PhysicsWorld.ActiveBodyCount);
        Assert.False(fixture.Context.Bodies.TryGetTransform(handle, out _));
        Assert.Equal((ushort)0, fixture.Grid.GetMaterial(16, 16));
        Assert.False(CellFlags.Has(fixture.Grid.FlagsAt(16, 16), CellFlags.RigidOwned));
    }

    /// <summary>
    /// 验证脚本时间 facade 从真实 FrameClock 读取固定步长、帧号与本帧 sim 决策。
    /// </summary>
    [Fact]
    public void ScriptFrameTimeReadsFromFrameClock()
    {
        // Arrange：准备输入与初始状态
        FrameClock clock = new(EngineConstants.SimHzDownscaled);
        ScriptFrameTime time = new(clock);

        _ = clock.BeginFrame(0);

        // Assert：验证预期结果
        Assert.Equal(1, time.FrameCount);
        Assert.Equal((float)clock.Dt, time.FixedStep);
        Assert.Equal((float)clock.Dt, time.DeltaTime);
        Assert.Equal((float)clock.Dt, time.RealDeltaTime);
        Assert.True(time.SimSteppedThisFrame);

        _ = clock.BeginFrame(clock.Dt * 2);
        time.SetRealDeltaTime(clock.Dt * 2);

        Assert.Equal(2, time.FrameCount);
        Assert.Equal((float)(clock.Dt * 2), time.RealDeltaTime);
        Assert.False(time.SimSteppedThisFrame);
        Assert.True(time.TimeScale < 1f);
    }

    /// <summary>
    /// 验证脚本上下文可注入相机与输入后端，供 Behaviour 通过统一入口访问。
    /// </summary>
    [Fact]
    public void ScriptContextExposesInjectedCameraAndInputBackends()
    {
        // Arrange：准备输入与初始状态
        ScriptOverlayApi overlay = new();
        Fixture fixture = Fixture.Create(
            camera: new ScriptCameraApi(100, 50, centerX: 10, centerY: 20),
            input: new ScriptInputApi(),
            lighting: new ScriptLightingApi(),
            overlay: overlay);
        ((ScriptInputApi)fixture.Context.Input).Update([Key.Space], [MouseButton.Middle], 1, 2, 3);
        fixture.Context.Lighting.RevealAround(10, 20, 8);
        fixture.Context.Overlay.SolidRectangle(4, 5, 6, 7, 0xFF010203u);

        // Assert：验证预期结果
        Assert.Equal(10f, fixture.Context.Camera.CenterX);
        Assert.True(fixture.Context.Input.WasPressed(Key.Space));
        Assert.True(fixture.Context.Input.WasMousePressed(MouseButton.Middle));
        Assert.Equal(3f, fixture.Context.Input.MouseWheelY);
        Assert.Equal(1, fixture.Context.Lighting.RevealCount);
        Assert.Equal(1, overlay.CommandCount);
        Assert.Equal(ScriptOverlayPrimitive.SolidRectangle, overlay.GetCommand(0).Primitive);
        Assert.Equal(0xFF010203u, overlay.GetCommand(0).ColorBgra);
    }

    /// <summary>
    /// 验证脚本相机可通过实体 Transform 执行基础 Follow(Entity)。
    /// </summary>
    [Fact]
    public void ScriptCameraFollowsEntityTransform()
    {
        ScriptScene scene = new();
        Entity entity = scene.CreateEntity();
        Transform transform = entity.AddComponent<Transform>();
        transform.SetPosition(42f, 24f);
        ScriptCameraApi camera = new(100, 50);

        camera.Follow(entity);

        Assert.Equal(42f, camera.CenterX);
        Assert.Equal(24f, camera.CenterY);
        _ = Assert.Throws<InvalidOperationException>(() => camera.Follow(scene.CreateEntity()));
    }

    /// <summary>
    /// 验证脚本音频 facade 只播放已加载 cue，并把位置/音量交给真实 AudioSystem。
    /// </summary>
    [Fact]
    public async Task ScriptAudioApiPlaysLoadedCueThroughAudioSystem()
    {
        // Arrange：搭建测试场景与依赖
        byte[] wav = CreateWav(channels: 1, bitsPerSample: 8, sampleRate: 8_000, [128]);
        using NullAudioBackend backend = new();
        AudioClipCache cache = new(backend, new MemoryAssetStore(wav), new WavDecoder());
        // Act：执行被测操作
        _ = await cache.LoadAsync("sfx/hit.wav");
        using AudioSystem audio = new();
        audio.Initialize(new AudioSettings { MaxVoices = 2, PixelsPerMeter = 16f, SfxVolume = 0.5f }, backend);
        ScriptAudioApi api = new(audio, cache);

        api.PlayAt("sfx/hit.wav", 32, 16, volume: 0.25f);
        api.PlayOneShot("sfx/hit.wav", volume: 0.5f);

        // Assert：验证不变式与预期结果
        Assert.Equal(2, backend.PlayCalls);
        Assert.Equal(new System.Numerics.Vector3(2f, 1f, 0f), backend.GetSourcePosition(1));
        Assert.Equal(0.125f, backend.GetSourceGain(1), precision: 5);
        Assert.Equal(System.Numerics.Vector3.Zero, backend.GetSourcePosition(2));
        Assert.Equal(0.25f, backend.GetSourceGain(2), precision: 5);
        _ = Assert.Throws<InvalidOperationException>(() => api.PlayAt("sfx/missing.wav", 0, 0));
        audio.Shutdown();
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(
            Chunk chunk,
            CellGrid grid,
            SimulationKernel kernel,
            TemperatureField temperature,
            ParticleSystem particles,
            ScriptEventBus scriptEvents,
            ScriptSimulationContext context,
            MaterialTable materials,
            PhysicsSystem? physics,
            JobSystem? jobs)
        {
            Chunk = chunk;
            Grid = grid;
            Kernel = kernel;
            Temperature = temperature;
            Particles = particles;
            ScriptEvents = scriptEvents;
            Context = context;
            Materials = materials;
            Physics = physics;
            Jobs = jobs;
        }

        public Chunk Chunk { get; }

        public CellGrid Grid { get; }

        public SimulationKernel Kernel { get; }

        public TemperatureField Temperature { get; }

        public ParticleSystem Particles { get; }

        public ScriptEventBus ScriptEvents { get; }

        public ScriptSimulationContext Context { get; }

        public MaterialTable Materials { get; }

        public PhysicsSystem? Physics { get; }

        private JobSystem? Jobs { get; }

        public static Fixture Create(
            ICameraApi? camera = null,
            IInputApi? input = null,
            ILightingApi? lighting = null,
            IOverlayApi? overlay = null,
            bool withPhysics = false)
        {
            MaterialTable materials = Materials(
                ("empty", CellType.Empty),
                ("sand", CellType.Powder),
                ("stone", CellType.Solid),
                ("water", CellType.Liquid),
                ("smoke", CellType.Gas),
                ("fire", CellType.Fire));
            Chunk chunk = new(new ChunkCoord(0, 0));
            TestChunkSource chunks = new(chunk);
            MaterialPropsTable props = new(materials.Hot);
            CellGrid grid = new(chunks, props);
            SimulationKernel kernel = new(chunks, props);
            TemperatureField temperature = new();
            ParticleSystem particles = new(capacity: 16);
            EventBus coreEvents = new(capacityPerChannel: 8);
            ScriptEventBus scriptEvents = new(coreEvents);
            JobSystem? jobs = null;
            PhysicsSystem? physics = null;
            if (withPhysics)
            {
                jobs = new JobSystem(workerCount: 1);
                B2WorldDef worldDef = Box2D.b2DefaultWorldDef();
                worldDef.Gravity = new B2Vec2 { X = 0f, Y = 0f };
                physics = PhysicsSystem.Initialize(grid, jobs, worldDef: worldDef);
            }

            ScriptSimulationContext context = new(new ScriptScene(), grid, kernel, particles, materials, temperature, scriptEvents, physics: physics, camera: camera, input: input, lighting: lighting, overlay: overlay);
            return new Fixture(chunk, grid, kernel, temperature, particles, scriptEvents, context, materials, physics, jobs);
        }

        public void Dispose()
        {
            Context.Dispose();
            ScriptEvents.Dispose();
            Physics?.Dispose();
            Jobs?.Dispose();
        }
    }

    private static MaterialTable Materials(params (string Name, CellType Type)[] definitions)
    {
        MaterialDef[] materials = new MaterialDef[definitions.Length];
        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = CreateMaterial((ushort)i, definitions[i].Name, definitions[i].Type) with
            {
                Density = i == 0 ? (byte)0 : (byte)100,
                Integrity = definitions[i].Type == CellType.Solid ? (ushort)40 : (ushort)0,
                DestroyedTarget = definitions[i].Type == CellType.Solid ? (ushort)1 : (ushort)0,
                Hardness = definitions[i].Name == "stone" ? (byte)6 : (byte)0,
                Dispersion = definitions[i].Name == "stone" ? (byte)3 : (byte)0,
                BaseColorBGRA = definitions[i].Name == "stone" ? 0xFF776655u : 0,
                DisplayName = definitions[i].Name == "stone" ? "Stone" : string.Empty,
                LegendCategory = definitions[i].Name == "stone" ? MaterialLegendCategory.Destructible : MaterialLegendCategory.Terrain,
                RenderStyle = definitions[i].Name == "stone" ? MaterialRenderStyle.Emissive : MaterialRenderStyle.Ground,
                PropertyFlags = definitions[i].Name == "stone" ? MaterialProperty.Emissive | MaterialProperty.Diggable : MaterialProperty.None,
                Flammability = definitions[i].Name == "stone" ? (byte)91 : (byte)0,
                AutoIgnitionTemp = definitions[i].Name == "stone" ? (ushort)320 : (ushort)0,
                FireHp = definitions[i].Name == "stone" ? 44 : 0,
                TemperatureOfFire = definitions[i].Name == "stone" ? (byte)120 : (byte)0,
                GeneratesSmoke = definitions[i].Name == "stone" ? (byte)7 : (byte)0,
                HeatConduct = definitions[i].Name == "stone" ? (byte)177 : (byte)255,
                HeatCapacity = definitions[i].Name == "stone" ? 2.5f : 1f,
            };
        }

        return new MaterialTable(materials);
    }

    private static MaterialDef CreateMaterial(ushort id, string name, CellType type)
    {
        return new MaterialDef
        {
            Id = id,
            Name = name,
            Type = type,
            HeatCapacity = 1f,
            HeatConduct = 255,
            TextureId = -1,
            MeltPoint = float.NaN,
            FreezePoint = float.NaN,
            BoilPoint = float.NaN,
        };
    }

    private static byte[] CreateWav(short channels, short bitsPerSample, int sampleRate, ReadOnlySpan<byte> pcm)
    {
        short blockAlign = (short)(channels * bitsPerSample / 8);
        int byteRate = sampleRate * blockAlign;
        byte[] wav = new byte[44 + pcm.Length];
        WriteAscii(wav, 0, "RIFF");
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4, 4), 36 + pcm.Length);
        WriteAscii(wav, 8, "WAVE");
        WriteAscii(wav, 12, "fmt ");
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22, 2), channels);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24, 4), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28, 4), byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32, 2), blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34, 2), bitsPerSample);
        WriteAscii(wav, 36, "data");
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40, 4), pcm.Length);
        pcm.CopyTo(wav.AsSpan(44));
        return wav;
    }

    private static void FillRect(Chunk chunk, int minX, int minY, int maxX, int maxY, ushort material)
    {
        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                chunk.MaterialBuffer[CellAddressing.LocalIndexFromLocal(x, y)] = material;
            }
        }
    }

    private static bool AnyCleared(CellGrid grid, int minX, int minY, int maxX, int maxY)
    {
        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                if (grid.GetMaterial(x, y) == 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void QueueDamageAndHeat(Fixture fixture)
    {
        fixture.Context.World.DamageCircle(24f, 24f, radius: 4, damage: 80f, falloff: true);
        fixture.Context.World.DamageBeam(20f, 24f, 1f, 0f, length: 8, damagePerCell: 80f);
        fixture.Context.World.AddHeat(24f, 24f, radius: 2, deltaCelsius: 16f);
    }

    private static void WriteAscii(byte[] destination, int offset, string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            destination[offset + i] = (byte)text[i];
        }
    }

    private sealed class MemoryAssetStore(byte[] bytes) : IAudioAssetStore
    {
        public ValueTask<byte[]> LoadBytesAsync(string assetId, CancellationToken cancellationToken = default)
        {
            _ = assetId;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(bytes);
        }
    }

    private sealed class TestChunkSource(params Chunk[] chunks) : IChunkSource
    {
        public ReadOnlySpan<Chunk> ResidentChunks => chunks;

        public bool TryGetChunk(ChunkCoord coord, out Chunk chunk)
        {
            for (int i = 0; i < chunks.Length; i++)
            {
                if (chunks[i].Coord == coord)
                {
                    chunk = chunks[i];
                    return true;
                }
            }

            chunk = null!;
            return false;
        }

        public bool ResolveNeighborhood(ChunkCoord center, out ChunkNeighborhood neighborhood)
        {
            if (!TryGetChunk(center, out Chunk chunk))
            {
                neighborhood = default;
                return false;
            }

            neighborhood = new ChunkNeighborhood(chunk, chunk, chunk, chunk, chunk, chunk, chunk, chunk, chunk);
            return true;
        }
    }
}
