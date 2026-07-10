using System.Buffers;
using System.Diagnostics;
using System.Numerics;
using PixelEngine.Core.Mathematics;
using PixelEngine.Core.Threading;
using PixelEngine.Interop.Box2D;
using PixelEngine.Physics.Geometry;
using PixelEngine.Simulation;
using PixelEngine.Simulation.Particles;

namespace PixelEngine.Physics;

/// <summary>
/// 相位 8a 的刚体 damage 合并、CCL 拆分与 Box2D body 重建服务。
/// </summary>
public sealed class RigidBodyDestruction
{
    private const int InitialBodyScratchCapacity = 16;
    private const int InitialDamageLocalCapacity = 32;
    private const int InitialWorkerScratchCapacity = 4;

    private static readonly RangeJob PreparePlansJob = static (start, end, workerIndex, context) =>
    {
        RebuildPreparationBatch batch = (RebuildPreparationBatch)context!;
        batch.MarkWorker(workerIndex);
        for (int i = start; i < end; i++)
        {
            PreparePlan(batch.Items[i], batch.Plans[i], batch.GeometryScratch[workerIndex], batch.FragmentPixelThreshold);
        }
    };

    private readonly ParticleSystem? _particles;
    private readonly Dictionary<int, HashSet<int>> _damagedLocalByBody = new(InitialBodyScratchCapacity);
    private HashSet<int>[] _damageSetPool = new HashSet<int>[InitialBodyScratchCapacity];
    private RebuildWorkItem[] _workItems = new RebuildWorkItem[InitialBodyScratchCapacity];
    private RebuildPlan[] _plans = new RebuildPlan[InitialBodyScratchCapacity];
    private int[] _workerHits = GC.AllocateArray<int>(InitialWorkerScratchCapacity, pinned: true);
    private MarchingSquares.TraceScratch[] _geometryScratch = new MarchingSquares.TraceScratch[InitialWorkerScratchCapacity];
    private readonly RebuildPreparationBatch _preparationBatch = new();
    private int _damageSetCount;
    private int _workItemCount;

    /// <summary>
    /// 创建刚体破坏重建服务。
    /// </summary>
    /// <param name="fragmentPixelThreshold">小于该像素数的连通块转为自由粒子。</param>
    /// <param name="particles">可选自由粒子系统，用于接收碎片像素。</param>
    public RigidBodyDestruction(int fragmentPixelThreshold = 4, ParticleSystem? particles = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fragmentPixelThreshold);
        FragmentPixelThreshold = fragmentPixelThreshold;
        _particles = particles;
        for (int i = 0; i < _damageSetPool.Length; i++)
        {
            _damageSetPool[i] = new HashSet<int>(InitialDamageLocalCapacity);
        }

        for (int i = 0; i < InitialBodyScratchCapacity; i++)
        {
            _workItems[i] = new RebuildWorkItem();
            _plans[i] = new RebuildPlan();
        }

        for (int i = 0; i < _geometryScratch.Length; i++)
        {
            _geometryScratch[i] = new MarchingSquares.TraceScratch();
        }
    }

    /// <summary>
    /// 小于该像素数的连通块转为自由粒子。
    /// </summary>
    public int FragmentPixelThreshold { get; private set; }

    /// <summary>
    /// 在帧边界更新碎片像素阈值，后续 damage 重建会使用新阈值。
    /// </summary>
    /// <param name="fragmentPixelThreshold">小于该像素数的连通块转为自由粒子。</param>
    public void SetFragmentPixelThreshold(int fragmentPixelThreshold)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fragmentPixelThreshold);
        FragmentPixelThreshold = fragmentPixelThreshold;
    }

    /// <summary>
    /// 最近一次 CCL、轮廓追踪与凸分解准备阶段耗时，单位毫秒。
    /// </summary>
    public double LastPreparationMilliseconds { get; private set; }

    /// <summary>
    /// 最近一次 Box2D body 销毁、重建与碎片写出阶段耗时，单位毫秒。
    /// </summary>
    public double LastApplyMilliseconds { get; private set; }

    /// <summary>
    /// 最近一次 RebuildDirty 是否通过 JobSystem 派发了重建准备阶段。
    /// </summary>
    public bool LastPlanningUsedJobSystem { get; private set; }

    /// <summary>
    /// 最近一次 JobSystem 准备阶段触达的 worker 数。
    /// </summary>
    public int LastPlanningWorkerCount { get; private set; }

    /// <summary>
    /// 对本帧 damage 触及的 awake 刚体执行每体最多一次重建。
    /// </summary>
    public RigidDestructionResult RebuildDirty(
        B2WorldId worldId,
        PhysicsWorld physicsWorld,
        CellGrid grid,
        RigidStampRegistry registry,
        IReadOnlyList<RigidDamageEvent> damageEvents,
        JobSystem? jobs = null)
    {
        ArgumentNullException.ThrowIfNull(physicsWorld);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(damageEvents);

        if (damageEvents.Count == 0)
        {
            LastPlanningUsedJobSystem = false;
            LastPlanningWorkerCount = 0;
            LastPreparationMilliseconds = 0;
            LastApplyMilliseconds = 0;
            return default;
        }

        // 按 bodyKey 合并本帧 damage 事件，每体最多重建一次；map 与局部坐标集合跨 burst 复用。
        BuildDamageMap(registry, damageEvents);
        int damagedBodies = 0;
        int skippedSleeping = 0;
        _workItemCount = 0;

        foreach ((int bodyKey, HashSet<int> damagedLocals) in _damagedLocalByBody)
        {
            if (!physicsWorld.TryGetBody(bodyKey, out PixelRigidBody? body))
            {
                continue;
            }

            damagedBodies++;
            // sleeping 刚体延迟重建，避免静止堆叠体每帧 CCL 开销。
            if (Box2D.b2Body_IsAwake(body.BodyId) == 0)
            {
                skippedSleeping++;
                continue;
            }

            EnsureScratchCapacity(_workItemCount + 1);
            _workItems[_workItemCount++].Set(body, ParentBodyState.Capture(body.BodyId), damagedLocals);
        }

        int destroyedBodies = 0;
        int createdBodies = 0;
        int fragmentPixels = 0;
        try
        {
            // 准备阶段：CCL + 凸分解可并行；Apply 阶段串行改 Box2D world。
            long prepareStarted = Stopwatch.GetTimestamp();
            PreparePlans(_workItemCount, jobs);
            LastPreparationMilliseconds = ElapsedMilliseconds(prepareStarted);
            long applyStarted = Stopwatch.GetTimestamp();
            for (int i = 0; i < _workItemCount; i++)
            {
                RigidDestructionResult result = ApplyPlan(worldId, physicsWorld, grid, registry, _plans[i]);
                destroyedBodies += result.DestroyedBodies;
                createdBodies += result.CreatedBodies;
                fragmentPixels += result.FragmentPixels;
            }

            LastApplyMilliseconds = ElapsedMilliseconds(applyStarted);
        }
        finally
        {
            for (int i = 0; i < _workItemCount; i++)
            {
                _plans[i].ClearTransientState();
                _workItems[i].Clear();
            }
        }

        return new RigidDestructionResult(damagedBodies, destroyedBodies, createdBodies, fragmentPixels, skippedSleeping);
    }

    private static double ElapsedMilliseconds(long started)
    {
        long elapsed = Stopwatch.GetTimestamp() - started;
        return elapsed * 1000.0 / Stopwatch.Frequency;
    }

    private void BuildDamageMap(RigidStampRegistry registry, IReadOnlyList<RigidDamageEvent> damageEvents)
    {
        for (int i = 0; i < _damageSetCount; i++)
        {
            _damageSetPool[i].Clear();
        }

        _damageSetCount = 0;
        _damagedLocalByBody.Clear();
        for (int i = 0; i < damageEvents.Count; i++)
        {
            RigidDamageEvent damage = damageEvents[i];
            if (!registry.TryGet(damage.WorldX, damage.WorldY, out RigidStamp stamp))
            {
                continue;
            }

            if (!_damagedLocalByBody.TryGetValue(stamp.BodyKey, out HashSet<int>? locals))
            {
                locals = RentDamageSet();
                _damagedLocalByBody.Add(stamp.BodyKey, locals);
            }

            // local 坐标打包为 int，避免 (x,y) 结构体在 HashSet 中额外分配。
            _ = locals.Add((stamp.LocalY << 16) ^ stamp.LocalX);
        }
    }

    private HashSet<int> RentDamageSet()
    {
        if (_damageSetCount == _damageSetPool.Length)
        {
            int oldLength = _damageSetPool.Length;
            Array.Resize(ref _damageSetPool, Math.Max(4, oldLength * 2));
            for (int i = oldLength; i < _damageSetPool.Length; i++)
            {
                _damageSetPool[i] = new HashSet<int>(InitialDamageLocalCapacity);
            }
        }

        HashSet<int> result = _damageSetPool[_damageSetCount++];
        result.Clear();
        return result;
    }

    private void EnsureScratchCapacity(int required)
    {
        if (required <= _workItems.Length)
        {
            return;
        }

        int oldLength = _workItems.Length;
        int newLength = oldLength == 0 ? 4 : oldLength;
        while (newLength < required)
        {
            newLength *= 2;
        }

        Array.Resize(ref _workItems, newLength);
        Array.Resize(ref _plans, newLength);
        for (int i = oldLength; i < newLength; i++)
        {
            _workItems[i] = new RebuildWorkItem();
            _plans[i] = new RebuildPlan();
        }
    }

    private void EnsureWorkerScratchCapacity(int required)
    {
        if (required <= _workerHits.Length)
        {
            return;
        }

        _workerHits = GC.AllocateArray<int>(required, pinned: true);
        Array.Resize(ref _geometryScratch, required);
        for (int i = 0; i < _geometryScratch.Length; i++)
        {
            _geometryScratch[i] ??= new MarchingSquares.TraceScratch();
        }
    }

    private void PreparePlans(int workItemCount, JobSystem? jobs)
    {
        if (workItemCount == 0)
        {
            LastPlanningUsedJobSystem = false;
            LastPlanningWorkerCount = 0;
            return;
        }

        if (jobs is null)
        {
            LastPlanningUsedJobSystem = false;
            LastPlanningWorkerCount = 0;
            for (int i = 0; i < workItemCount; i++)
            {
                PreparePlan(_workItems[i], _plans[i], _geometryScratch[0], FragmentPixelThreshold);
            }

            return;
        }

        EnsureWorkerScratchCapacity(jobs.WorkerCount);
        _workerHits.AsSpan(0, jobs.WorkerCount).Clear();
        _preparationBatch.Configure(_workItems, _plans, _geometryScratch, FragmentPixelThreshold, _workerHits);
        LastPlanningUsedJobSystem = true;
        jobs.ParallelRange(workItemCount, 1, PreparePlansJob, _preparationBatch);
        LastPlanningWorkerCount = CountWorkerHits(_workerHits.AsSpan(0, jobs.WorkerCount));
    }

    private RigidDestructionResult ApplyPlan(
        B2WorldId worldId,
        PhysicsWorld physicsWorld,
        CellGrid grid,
        RigidStampRegistry registry,
        RebuildPlan plan)
    {
        // Apply：先擦网格 stamp 再销毁父 body，子 body 继承父 transform 与速度。
        int erased = RigidBodyRasterizer.EraseAtCurrentTransform(plan.Body, grid, registry);
        _ = erased;
        Box2D.b2DestroyBody(plan.Body.BodyId);
        physicsWorld.RemoveBody(plan.Body.BodyKey);

        int fragments = 0;
        for (int i = 0; i < plan.FragmentSpawnCount; i++)
        {
            ParticleSpawn spawn = plan.GetFragmentSpawn(i);
            if (_particles is not null)
            {
                _ = _particles.TrySpawn(in spawn);
            }

            fragments++;
        }

        int created = 0;
        for (int i = 0; i < plan.ChildCount; i++)
        {
            ChildBodyPlan child = plan.GetChild(i);
            if (!ShapeBuilder.TryBuildBody(
                worldId,
                child.ConvexPieces.AsSpan(0, child.ConvexPieceCount),
                plan.Parent.PositionPixels,
                out B2BodyId childBodyId))
            {
                fragments += SpawnDegenerateChildFragments(child, plan.Parent);
                continue;
            }

            Box2D.b2Body_SetTransform(childBodyId, plan.Parent.NativeTransform.P, plan.Parent.NativeTransform.Q);
            TransferVelocity(childBodyId, child.SourceBounds, plan.Body.Mask.LocalOrigin, plan.Parent);
            _ = physicsWorld.AddBody(childBodyId, child.Mask);
            created++;
        }

        return new RigidDestructionResult(1, 1, created, fragments, 0);
    }

    private int SpawnDegenerateChildFragments(ChildBodyPlan child, in ParentBodyState parent)
    {
        int fragments = 0;
        BodyLocalMask mask = child.Mask;
        for (int y = 0; y < mask.Height; y++)
        {
            for (int x = 0; x < mask.Width; x++)
            {
                if (!mask.IsSolid(x, y))
                {
                    continue;
                }

                ushort material = mask.MaterialAt(x, y);
                if (material == 0)
                {
                    continue;
                }

                Vector2 local = new Vector2(x + 0.5f, y + 0.5f) - mask.LocalOrigin;
                Vector2 world = parent.Transform.TransformPoint(local);
                if (_particles is not null)
                {
                    _ = _particles.TrySpawn(new ParticleSpawn(world.X, world.Y, 0f, 0f, material, ColorVariant: 0, Life: 0));
                }

                fragments++;
            }
        }

        return fragments;
    }

    private static void PreparePlan(
        RebuildWorkItem workItem,
        RebuildPlan plan,
        MarchingSquares.TraceScratch geometryScratch,
        int fragmentPixelThreshold)
    {
        BodyLocalMask sourceMask = workItem.Body.Mask;
        int area = sourceMask.Width * sourceMask.Height;
        byte[] solid = ArrayPool<byte>.Shared.Rent(area);
        ushort[] materials = ArrayPool<ushort>.Shared.Rent(area);
        int[] labels = ArrayPool<int>.Shared.Rent(area);
        ConnectedComponent[] components = ArrayPool<ConnectedComponent>.Shared.Rent(area);

        try
        {
            sourceMask.SolidBits.CopyTo(solid);
            sourceMask.Materials.CopyTo(materials);
            // 先把 damage 触及的 body-local 像素挖空，再对剩余固体做 CCL。
            foreach (int packed in workItem.DamagedLocals)
            {
                int localX = packed & 0xFFFF;
                int localY = packed >> 16;
                int index = (localY * sourceMask.Width) + localX;
                solid[index] = 0;
                materials[index] = 0;
            }

            int componentCount = ConnectedComponentLabeler.Label(
                solid.AsSpan(0, area),
                sourceMask.Width,
                sourceMask.Height,
                labels.AsSpan(0, area),
                components.AsSpan(0, area),
                Connectivity.Four,
                fragmentPixelThreshold);

            plan.Initialize(workItem.Body, workItem.Parent);
            for (int i = 0; i < componentCount; i++)
            {
                ConnectedComponent component = components[i];
                // 低于阈值的连通块转自由粒子，不再创建子刚体。
                if (component.IsFragment)
                {
                    CollectFragmentParticles(sourceMask, labels.AsSpan(0, area), component, workItem.Parent, plan);
                    continue;
                }

                if (TryCreateChildPlan(sourceMask, materials.AsSpan(0, area), labels.AsSpan(0, area), component, geometryScratch, plan))
                {
                    continue;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(solid);
            ArrayPool<ushort>.Shared.Return(materials);
            ArrayPool<int>.Shared.Return(labels);
            ArrayPool<ConnectedComponent>.Shared.Return(components);
        }
    }

    private static bool TryCreateChildPlan(
        BodyLocalMask sourceMask,
        ReadOnlySpan<ushort> sourceMaterials,
        ReadOnlySpan<int> labels,
        in ConnectedComponent component,
        MarchingSquares.TraceScratch geometryScratch,
        RebuildPlan plan)
    {
        RectI bounds = component.Bounds;
        int width = bounds.Width;
        int height = bounds.Height;
        int area = width * height;
        byte[] childSolid = ArrayPool<byte>.Shared.Rent(area);
        ushort[] childMaterials = ArrayPool<ushort>.Shared.Rent(area);
        ConvexPolygon[] convexPieces = ArrayPool<ConvexPolygon>.Shared.Rent(Math.Max(8, area));
        bool retainedConvexPieces = false;

        try
        {
            childSolid.AsSpan(0, area).Clear();
            childMaterials.AsSpan(0, area).Clear();
            for (int y = bounds.MinY; y < bounds.MaxY; y++)
            {
                for (int x = bounds.MinX; x < bounds.MaxX; x++)
                {
                    int sourceIndex = (y * sourceMask.Width) + x;
                    if (labels[sourceIndex] != component.Label)
                    {
                        continue;
                    }

                    int childIndex = ((y - bounds.MinY) * width) + x - bounds.MinX;
                    childSolid[childIndex] = 1;
                    childMaterials[childIndex] = sourceMaterials[sourceIndex];
                }
            }

            Vector2 childOrigin = sourceMask.LocalOrigin - new Vector2(bounds.MinX, bounds.MinY);
            if (!RigidBodyMaskShapeBuilder.TryBuildConvexPieces(childSolid.AsSpan(0, area), width, height, childOrigin, convexPieces, geometryScratch, out int pieceCount))
            {
                return false;
            }

            BodyLocalMask childMask = new(width, height, childOrigin, childSolid.AsSpan(0, area), childMaterials.AsSpan(0, area));
            plan.AddChild(childMask, convexPieces, pieceCount, bounds);
            retainedConvexPieces = true;
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(childSolid);
            ArrayPool<ushort>.Shared.Return(childMaterials);
            if (!retainedConvexPieces)
            {
                ArrayPool<ConvexPolygon>.Shared.Return(convexPieces);
            }
        }
    }

    private static void CollectFragmentParticles(
        BodyLocalMask sourceMask,
        ReadOnlySpan<int> labels,
        in ConnectedComponent component,
        in ParentBodyState parent,
        RebuildPlan destination)
    {
        for (int y = component.Bounds.MinY; y < component.Bounds.MaxY; y++)
        {
            for (int x = component.Bounds.MinX; x < component.Bounds.MaxX; x++)
            {
                int index = (y * sourceMask.Width) + x;
                if (labels[index] != component.Label)
                {
                    continue;
                }

                ushort material = sourceMask.Materials[index];
                if (material == 0)
                {
                    continue;
                }

                Vector2 local = new Vector2(x + 0.5f, y + 0.5f) - sourceMask.LocalOrigin;
                Vector2 world = parent.Transform.TransformPoint(local);
                destination.AddFragment(new ParticleSpawn(world.X, world.Y, 0f, 0f, material, ColorVariant: 0, Life: 0));
            }
        }
    }

    private static int CountWorkerHits(ReadOnlySpan<int> workerHits)
    {
        int count = 0;
        for (int i = 0; i < workerHits.Length; i++)
        {
            if (workerHits[i] != 0)
            {
                count++;
            }
        }

        return count;
    }

    private static void TransferVelocity(B2BodyId childBodyId, in RectI bounds, Vector2 sourceOrigin, in ParentBodyState parent)
    {
        Vector2 centerLocalPixels = new(
            ((bounds.MinX + bounds.MaxX) * 0.5f) - sourceOrigin.X,
            ((bounds.MinY + bounds.MaxY) * 0.5f) - sourceOrigin.Y);
        Vector2 centerWorldOffsetPixels = parent.Transform.TransformDirection(centerLocalPixels);
        B2Vec2 r = new()
        {
            X = PhysicsScale.PixelToPhysics(centerWorldOffsetPixels.X),
            Y = PhysicsScale.PixelToPhysics(centerWorldOffsetPixels.Y),
        };
        B2Vec2 linear = new()
        {
            X = parent.LinearVelocity.X - (parent.AngularVelocity * r.Y),
            Y = parent.LinearVelocity.Y + (parent.AngularVelocity * r.X),
        };

        Box2D.b2Body_SetLinearVelocity(childBodyId, linear);
        Box2D.b2Body_SetAngularVelocity(childBodyId, parent.AngularVelocity);
    }

    private readonly struct ParentBodyState(
        B2Transform nativeTransform,
        B2Vec2 linearVelocity,
        float angularVelocity,
        Transform2D transform)
    {
        public readonly B2Transform NativeTransform = nativeTransform;
        public readonly B2Vec2 LinearVelocity = linearVelocity;
        public readonly float AngularVelocity = angularVelocity;
        public readonly Transform2D Transform = transform;
        public readonly Vector2 PositionPixels = transform.Position;

        public static ParentBodyState Capture(B2BodyId bodyId)
        {
            B2Transform nativeTransform = Box2D.b2Body_GetTransform(bodyId);
            return new ParentBodyState(
                nativeTransform,
                Box2D.b2Body_GetLinearVelocity(bodyId),
                Box2D.b2Body_GetAngularVelocity(bodyId),
                PhysicsScale.ToTransform2D(nativeTransform));
        }
    }

    private sealed class RebuildWorkItem
    {
        public PixelRigidBody Body { get; private set; } = null!;

        public ParentBodyState Parent { get; private set; }

        public HashSet<int> DamagedLocals { get; private set; } = null!;

        public void Set(PixelRigidBody body, ParentBodyState parent, HashSet<int> damagedLocals)
        {
            Body = body;
            Parent = parent;
            DamagedLocals = damagedLocals;
        }

        public void Clear()
        {
            Body = null!;
            Parent = default;
            DamagedLocals = null!;
        }
    }

    private sealed class RebuildPlan
    {
        private const int InitialChildCapacity = 4;
        private const int InitialFragmentCapacity = 32;
        private ChildBodyPlan[] _children = new ChildBodyPlan[InitialChildCapacity];
        private ParticleSpawn[] _fragmentSpawns = new ParticleSpawn[InitialFragmentCapacity];
        private PixelRigidBody BodyStorage { get; set; } = null!;
        private ParentBodyState ParentStorage { get; set; }

        public RebuildPlan()
        {
            for (int i = 0; i < _children.Length; i++)
            {
                _children[i] = new ChildBodyPlan();
            }
        }

        public int ChildCount { get; private set; }

        public int FragmentSpawnCount { get; private set; }

        public PixelRigidBody Body => BodyStorage;

        public ParentBodyState Parent => ParentStorage;

        public void Initialize(PixelRigidBody body, ParentBodyState parent)
        {
            BodyStorage = body;
            ParentStorage = parent;
            ChildCount = 0;
            FragmentSpawnCount = 0;
        }

        public void AddChild(BodyLocalMask mask, ConvexPolygon[] convexPieces, int convexPieceCount, RectI sourceBounds)
        {
            EnsureChildCapacity(ChildCount + 1);
            _children[ChildCount++].Set(mask, convexPieces, convexPieceCount, sourceBounds);
        }

        public void AddFragment(in ParticleSpawn spawn)
        {
            if (FragmentSpawnCount == _fragmentSpawns.Length)
            {
                Array.Resize(ref _fragmentSpawns, _fragmentSpawns.Length * 2);
            }

            _fragmentSpawns[FragmentSpawnCount++] = spawn;
        }

        public ChildBodyPlan GetChild(int index)
        {
            return _children[index];
        }

        public ParticleSpawn GetFragmentSpawn(int index)
        {
            return _fragmentSpawns[index];
        }

        public void ClearTransientState()
        {
            for (int i = 0; i < ChildCount; i++)
            {
                _children[i].ClearTransientState();
            }

            ChildCount = 0;
            FragmentSpawnCount = 0;
            BodyStorage = null!;
            ParentStorage = default;
        }

        private void EnsureChildCapacity(int required)
        {
            if (required <= _children.Length)
            {
                return;
            }

            int oldLength = _children.Length;
            Array.Resize(ref _children, Math.Max(required, oldLength * 2));
            for (int i = oldLength; i < _children.Length; i++)
            {
                _children[i] = new ChildBodyPlan();
            }
        }
    }

    private sealed class ChildBodyPlan
    {
        private BodyLocalMask? MaskStorage { get; set; }
        private ConvexPolygon[]? ConvexPiecesStorage { get; set; }

        public BodyLocalMask Mask => MaskStorage!;

        public ConvexPolygon[] ConvexPieces => ConvexPiecesStorage!;

        public int ConvexPieceCount { get; private set; }

        public RectI SourceBounds { get; private set; }

        public void Set(BodyLocalMask mask, ConvexPolygon[] convexPieces, int convexPieceCount, RectI sourceBounds)
        {
            MaskStorage = mask;
            ConvexPiecesStorage = convexPieces;
            ConvexPieceCount = convexPieceCount;
            SourceBounds = sourceBounds;
        }

        public void ClearTransientState()
        {
            if (ConvexPiecesStorage is not null)
            {
                ArrayPool<ConvexPolygon>.Shared.Return(ConvexPiecesStorage);
            }

            MaskStorage = null;
            ConvexPiecesStorage = null;
            ConvexPieceCount = 0;
            SourceBounds = default;
        }
    }

    private sealed class RebuildPreparationBatch
    {
        private int[] _workerHits = [];

        public RebuildWorkItem[] Items { get; private set; } = [];

        public RebuildPlan[] Plans { get; private set; } = [];

        public MarchingSquares.TraceScratch[] GeometryScratch { get; private set; } = [];

        public int FragmentPixelThreshold { get; private set; }

        public void Configure(
            RebuildWorkItem[] items,
            RebuildPlan[] plans,
            MarchingSquares.TraceScratch[] geometryScratch,
            int fragmentPixelThreshold,
            int[] workerHits)
        {
            Items = items;
            Plans = plans;
            GeometryScratch = geometryScratch;
            FragmentPixelThreshold = fragmentPixelThreshold;
            _workerHits = workerHits;
        }

        public void MarkWorker(int workerIndex)
        {
            if ((uint)workerIndex < (uint)_workerHits.Length)
            {
                Volatile.Write(ref _workerHits[workerIndex], 1);
            }
        }
    }
}
