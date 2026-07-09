using System.Numerics;
using BenchmarkDotNet.Attributes;
using PixelEngine.Core.Threading;
using PixelEngine.Interop.Box2D;
using PixelEngine.Physics;
using PixelEngine.Physics.Geometry;
using PixelEngine.Simulation;

namespace PixelEngine.Benchmarks;

/// <summary>
/// 物理阶段性能与分配基准，覆盖 Box2D task bridge、刚体破坏重建与稳态同步路径。
/// </summary>
[MemoryDiagnoser]
public class PhysicsBenchmarks
{
    private const int StepBodyCount = 512;
    private const int SyncBodyCount = 32;
    private const float FixedDt = 1f / 60f;

    private StepWorld? _stepWorker1;
    private StepWorld? _stepWorker4;
    private RebuildFixture? _rebuildFixture;
    private SyncFixture? _syncFixture;

    /// <summary>
    /// 最近一次 <see cref="RigidBodyDestruction.RebuildDirty"/> 的准备阶段耗时，单位毫秒。
    /// </summary>
    public double LastPreparationMilliseconds { get; private set; }

    /// <summary>
    /// 最近一次 <see cref="RigidBodyDestruction.RebuildDirty"/> 的 Box2D apply 阶段耗时，单位毫秒。
    /// </summary>
    public double LastApplyMilliseconds { get; private set; }

    /// <summary>
    /// 本轮破坏重建基准中同帧受损刚体数量；1 用于帧预算，16 用于压力观测。
    /// </summary>
    [Params(1, 16)]
    public int DamagedBodyCount { get; set; }

    /// <summary>
    /// 初始化可跨 invocation 复用的 Box2D step world 与 SyncStep fixture。
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        PhysicsScale.ConfigureBox2DLengthUnits();
        _stepWorker1 = StepWorld.Create(workerCount: 1, StepBodyCount);
        _stepWorker4 = StepWorld.Create(workerCount: 4, StepBodyCount);
        _syncFixture = SyncFixture.Create(SyncBodyCount);
    }

    /// <summary>
    /// 释放基准期间持有的 native world 与 JobSystem。
    /// </summary>
    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _rebuildFixture?.Dispose();
        _rebuildFixture = null;
        _syncFixture?.Dispose();
        _syncFixture = null;
        _stepWorker4?.Dispose();
        _stepWorker4 = null;
        _stepWorker1?.Dispose();
        _stepWorker1 = null;
    }

    /// <summary>
    /// 为破坏重建基准创建一次性 fixture，保证目标方法每次都执行完整 rebuild。
    /// </summary>
    [IterationSetup(Target = nameof(RigidBodyDestructionRebuildDirty))]
    public void SetupRebuildIteration()
    {
        _rebuildFixture?.Dispose();
        _rebuildFixture = RebuildFixture.Create(DamagedBodyCount);
    }

    /// <summary>
    /// 清理破坏重建基准的一次性 fixture。
    /// </summary>
    [IterationCleanup(Target = nameof(RigidBodyDestructionRebuildDirty))]
    public void CleanupRebuildIteration()
    {
        _rebuildFixture?.Dispose();
        _rebuildFixture = null;
    }

    /// <summary>
    /// 验证Box2D Task Bridge Step Worker1。
    /// </summary>
    [Benchmark(Baseline = true)]
    public void Box2DTaskBridgeStepWorker1()
    {
        _stepWorker1!.Step();
    }

    /// <summary>
    /// 验证Box2D Task Bridge Step Worker4。
    /// </summary>
    [Benchmark]
    public void Box2DTaskBridgeStepWorker4()
    {
        _stepWorker4!.Step();
    }

    /// <summary>
    /// 执行完整刚体 damage 合并、CCL/凸分解准备与 Box2D body 重建。
    /// </summary>
    [Benchmark]
    [InvocationCount(1)]
    public RigidDestructionResult RigidBodyDestructionRebuildDirty()
    {
        RebuildFixture fixture = _rebuildFixture ?? throw new InvalidOperationException("Rebuild fixture 未初始化。");
        RigidDestructionResult result = fixture.Rebuild();
        LastPreparationMilliseconds = fixture.Destruction.LastPreparationMilliseconds;
        LastApplyMilliseconds = fixture.Destruction.LastApplyMilliseconds;
        return result;
    }

    /// <summary>
    /// 验证Physics System Sync Step Steady State。
    /// </summary>
    [Benchmark]
    public int PhysicsSystemSyncStepSteadyState()
    {
        SyncFixture fixture = _syncFixture ?? throw new InvalidOperationException("Sync fixture 未初始化。");
        fixture.System.SyncStep(FixedDt);
        return fixture.System.LastStampedCellCount;
    }

    private static BodyLocalMask CreateFilledMask(int width, int height, ushort material, Vector2 origin)
    {
        int area = width * height;
        byte[] solid = new byte[area];
        ushort[] materials = new ushort[area];
        Array.Fill(solid, (byte)1);
        Array.Fill(materials, material);
        return new BodyLocalMask(width, height, origin, solid, materials);
    }

    private static B2BodyId CreateBoxBody(B2WorldId worldId, int width, int height, Vector2 bodyPositionPixels, float density = 1f)
    {
        Span<ConvexPolygon> pieces = stackalloc ConvexPolygon[1];
        ReadOnlySpan<Vector2> vertices =
        [
            new(0, 0),
            new(width, 0),
            new(width, height),
            new(0, height),
        ];
        pieces[0] = ConvexPolygon.From(vertices);
        return ShapeBuilder.BuildBody(worldId, pieces, bodyPositionPixels, density);
    }

    private static void CreateDynamicBodyGrid(B2WorldId worldId, int count, int columns, int bodySize, Vector2 origin)
    {
        for (int i = 0; i < count; i++)
        {
            int x = i % columns;
            int y = i / columns;
            B2BodyId bodyId = CreateBoxBody(
                worldId,
                bodySize,
                bodySize,
                origin + new Vector2(x * bodySize, y * bodySize),
                density: 1f);
            Box2D.b2Body_SetLinearVelocity(bodyId, new B2Vec2
            {
                X = PhysicsScale.PixelToPhysics((x & 1) == 0 ? 8f : -8f),
                Y = PhysicsScale.PixelToPhysics((y & 1) == 0 ? 4f : -4f),
            });
            Box2D.b2Body_SetAngularVelocity(bodyId, ((i % 7) - 3) * 0.05f);
        }
    }

    private sealed class StepWorld : IDisposable
    {
        private readonly JobSystem _jobs;
        private readonly PhysicsSystem _system;

        private StepWorld(JobSystem jobs, PhysicsSystem system)
        {
            _jobs = jobs;
            _system = system;
        }

        public static StepWorld Create(int workerCount, int bodyCount)
        {
            JobSystem jobs = new(workerCount);
            TestChunkSource source = TestChunkSource.CreateRectangle(minChunkX: 0, minChunkY: 0, width: 12, height: 12);
            CellGrid grid = new(source, MaterialPropsTable.Empty);
            B2WorldDef worldDef = Box2D.b2DefaultWorldDef();
            worldDef.Gravity = new B2Vec2 { X = 0f, Y = PhysicsScale.PixelToPhysics(48f) };
            worldDef.EnableSleep = 0;
            PhysicsSystem system = PhysicsSystem.Initialize(grid, jobs, worldDef: worldDef);
            CreateDynamicBodyGrid(system.WorldId, bodyCount, columns: 32, bodySize: 8, origin: new Vector2(16, 16));
            for (int i = 0; i < 8; i++)
            {
                Box2D.b2World_Step(system.WorldId, FixedDt, 4);
            }

            return new StepWorld(jobs, system);
        }

        public void Step()
        {
            Box2D.b2World_Step(_system.WorldId, FixedDt, 4);
        }

        public void Dispose()
        {
            _system.Dispose();
            _jobs.Dispose();
        }
    }

    private sealed class RebuildFixture : IDisposable
    {
        private readonly JobSystem _jobs;
        private readonly TestChunkSource _source;

        private RebuildFixture(
            JobSystem jobs,
            TestChunkSource source,
            B2WorldId worldId,
            PhysicsWorld physicsWorld,
            CellGrid grid,
            RigidStampRegistry registry,
            RigidDamageEvent[] damageEvents,
            RigidBodyDestruction destruction)
        {
            _jobs = jobs;
            _source = source;
            WorldId = worldId;
            PhysicsWorld = physicsWorld;
            Grid = grid;
            Registry = registry;
            DamageEvents = damageEvents;
            Destruction = destruction;
        }

        public B2WorldId WorldId { get; }

        public PhysicsWorld PhysicsWorld { get; }

        public CellGrid Grid { get; }

        public RigidStampRegistry Registry { get; }

        public RigidDamageEvent[] DamageEvents { get; }

        public RigidBodyDestruction Destruction { get; }

        public static RebuildFixture Create(int bodyCount)
        {
            JobSystem jobs = new(workerCount: 4);
            TestChunkSource source = TestChunkSource.CreateRectangle(minChunkX: 0, minChunkY: 0, width: 10, height: 10);
            CellGrid grid = new(source, MaterialPropsTable.Empty);
            PhysicsWorld physicsWorld = new();
            RigidStampRegistry registry = new();
            RigidDamageEvent[] damageEvents = new RigidDamageEvent[bodyCount * 24];
            B2WorldDef worldDef = Box2D.b2DefaultWorldDef();
            worldDef.Gravity = new B2Vec2 { X = 0f, Y = 0f };
            worldDef.EnableSleep = 0;
            B2WorldId worldId = Box2D.b2CreateWorld(in worldDef);

            for (int i = 0; i < bodyCount; i++)
            {
                int column = i % 4;
                int row = i / 4;
                Vector2 position = new(32 + (column * 96), 32 + (row * 80));
                BodyLocalMask mask = CreateFilledMask(48, 24, material: 2, origin: Vector2.Zero);
                B2BodyId bodyId = CreateBoxBody(worldId, mask.Width, mask.Height, position);
                PixelRigidBody body = physicsWorld.AddBody(bodyId, mask);
                _ = RigidBodyRasterizer.StampInverseSampling(body, body.PreviousTransform, grid, registry);
                AddVerticalCut(grid, damageEvents, i * 24, (int)position.X + 24, (int)position.Y, 24);
            }

            return new RebuildFixture(
                jobs,
                source,
                worldId,
                physicsWorld,
                grid,
                registry,
                damageEvents,
                new RigidBodyDestruction(fragmentPixelThreshold: 4));
        }

        public RigidDestructionResult Rebuild()
        {
            return Destruction.RebuildDirty(WorldId, PhysicsWorld, Grid, Registry, DamageEvents, _jobs);
        }

        public void Dispose()
        {
            Box2D.b2DestroyWorld(WorldId);
            _jobs.Dispose();
            _source.Dispose();
        }

        private static void AddVerticalCut(CellGrid grid, RigidDamageEvent[] damageEvents, int offset, int worldX, int minY, int height)
        {
            for (int y = 0; y < height; y++)
            {
                int worldY = minY + y;
                grid.FlagsAt(worldX, worldY) = 0;
                grid.MaterialAt(worldX, worldY) = 0;
                damageEvents[offset + y] = new RigidDamageEvent(worldX, worldY);
            }
        }
    }

    private sealed class SyncFixture : IDisposable
    {
        private readonly JobSystem _jobs;
        private readonly TestChunkSource _source;

        private SyncFixture(JobSystem jobs, TestChunkSource source, PhysicsSystem system)
        {
            _jobs = jobs;
            _source = source;
            System = system;
        }

        public PhysicsSystem System { get; }

        public static SyncFixture Create(int bodyCount)
        {
            JobSystem jobs = new(workerCount: 4);
            TestChunkSource source = TestChunkSource.CreateRectangle(minChunkX: 0, minChunkY: 0, width: 8, height: 8);
            CellGrid grid = new(source, MaterialPropsTable.Empty);
            PhysicsWorld physicsWorld = new();
            RigidStampRegistry registry = new();
            B2WorldDef worldDef = Box2D.b2DefaultWorldDef();
            worldDef.Gravity = new B2Vec2 { X = 0f, Y = 0f };
            worldDef.EnableSleep = 0;
            PhysicsSystem system = PhysicsSystem.Initialize(grid, jobs, physicsWorld, registry, worldDef: worldDef);

            BodyLocalMask mask = CreateFilledMask(12, 12, material: 2, origin: Vector2.Zero);
            for (int i = 0; i < bodyCount; i++)
            {
                int x = i % 8;
                int y = i / 8;
                B2BodyId bodyId = CreateBoxBody(system.WorldId, 12, 12, new Vector2(48 + (x * 42), 48 + (y * 42)));
                Box2D.b2Body_SetLinearVelocity(bodyId, new B2Vec2
                {
                    X = PhysicsScale.PixelToPhysics((x & 1) == 0 ? 2f : -2f),
                    Y = PhysicsScale.PixelToPhysics((y & 1) == 0 ? 2f : -2f),
                });
                PixelRigidBody body = physicsWorld.AddBody(bodyId, mask);
                _ = RigidBodyRasterizer.StampInverseSampling(body, body.PreviousTransform, grid, registry);
            }

            for (int i = 0; i < 4; i++)
            {
                system.SyncStep(FixedDt);
            }

            return new SyncFixture(jobs, source, system);
        }

        public void Dispose()
        {
            System.Dispose();
            _jobs.Dispose();
            _source.Dispose();
        }
    }

    private sealed class TestChunkSource : IChunkSource, IDisposable
    {
        private readonly Chunk[] _chunks;
        private readonly Dictionary<ChunkCoord, Chunk> _map;

        private TestChunkSource(Chunk[] chunks)
        {
            _chunks = chunks;
            _map = new Dictionary<ChunkCoord, Chunk>(chunks.Length);
            for (int i = 0; i < chunks.Length; i++)
            {
                _map.Add(chunks[i].Coord, chunks[i]);
            }
        }

        public ReadOnlySpan<Chunk> ResidentChunks => _chunks;

        public static TestChunkSource CreateRectangle(int minChunkX, int minChunkY, int width, int height)
        {
            Chunk[] chunks = new Chunk[width * height];
            int index = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    chunks[index++] = new Chunk(new ChunkCoord(minChunkX + x, minChunkY + y));
                }
            }

            return new TestChunkSource(chunks);
        }

        public bool TryGetChunk(ChunkCoord coord, out Chunk chunk)
        {
            return _map.TryGetValue(coord, out chunk!);
        }

        public bool ResolveNeighborhood(ChunkCoord center, out ChunkNeighborhood neighborhood)
        {
            if (!TryGetChunk(new ChunkCoord(center.X - 1, center.Y - 1), out Chunk slot0) ||
                !TryGetChunk(new ChunkCoord(center.X, center.Y - 1), out Chunk slot1) ||
                !TryGetChunk(new ChunkCoord(center.X + 1, center.Y - 1), out Chunk slot2) ||
                !TryGetChunk(new ChunkCoord(center.X - 1, center.Y), out Chunk slot3) ||
                !TryGetChunk(center, out Chunk slot4) ||
                !TryGetChunk(new ChunkCoord(center.X + 1, center.Y), out Chunk slot5) ||
                !TryGetChunk(new ChunkCoord(center.X - 1, center.Y + 1), out Chunk slot6) ||
                !TryGetChunk(new ChunkCoord(center.X, center.Y + 1), out Chunk slot7) ||
                !TryGetChunk(new ChunkCoord(center.X + 1, center.Y + 1), out Chunk slot8))
            {
                neighborhood = default;
                return false;
            }

            neighborhood = new ChunkNeighborhood(slot0, slot1, slot2, slot3, slot4, slot5, slot6, slot7, slot8);
            return true;
        }

        public void Dispose()
        {
            _map.Clear();
        }
    }
}
