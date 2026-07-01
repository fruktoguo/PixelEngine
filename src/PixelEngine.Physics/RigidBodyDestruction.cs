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
    private static readonly RangeJob PreparePlansJob = static (start, end, workerIndex, context) =>
    {
        RebuildPreparationBatch batch = (RebuildPreparationBatch)context!;
        batch.MarkWorker(workerIndex);
        for (int i = start; i < end; i++)
        {
            batch.Plans[i] = PreparePlan(batch.Items[i], batch.FragmentPixelThreshold);
        }
    };

    private readonly ParticleSystem? _particles;

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
    }

    /// <summary>
    /// 小于该像素数的连通块转为自由粒子。
    /// </summary>
    public int FragmentPixelThreshold { get; }

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

        Dictionary<int, HashSet<int>> damagedLocalByBody = BuildDamageMap(registry, damageEvents);
        int damagedBodies = 0;
        int skippedSleeping = 0;
        List<RebuildWorkItem> workItems = new(damagedLocalByBody.Count);

        foreach ((int bodyKey, HashSet<int> damagedLocals) in damagedLocalByBody)
        {
            if (!physicsWorld.TryGetBody(bodyKey, out PixelRigidBody? body))
            {
                continue;
            }

            damagedBodies++;
            if (Box2D.b2Body_IsAwake(body.BodyId) == 0)
            {
                skippedSleeping++;
                continue;
            }

            workItems.Add(new RebuildWorkItem(body, ParentBodyState.Capture(body.BodyId), damagedLocals));
        }

        long prepareStarted = Stopwatch.GetTimestamp();
        RebuildPlan[] plans = PreparePlans(workItems, jobs);
        LastPreparationMilliseconds = ElapsedMilliseconds(prepareStarted);
        int destroyedBodies = 0;
        int createdBodies = 0;
        int fragmentPixels = 0;
        long applyStarted = Stopwatch.GetTimestamp();
        for (int i = 0; i < plans.Length; i++)
        {
            RigidDestructionResult result = ApplyPlan(worldId, physicsWorld, grid, registry, plans[i]);
            destroyedBodies += result.DestroyedBodies;
            createdBodies += result.CreatedBodies;
            fragmentPixels += result.FragmentPixels;
        }

        LastApplyMilliseconds = ElapsedMilliseconds(applyStarted);
        return new RigidDestructionResult(damagedBodies, destroyedBodies, createdBodies, fragmentPixels, skippedSleeping);
    }

    private static double ElapsedMilliseconds(long started)
    {
        long elapsed = Stopwatch.GetTimestamp() - started;
        return elapsed * 1000.0 / Stopwatch.Frequency;
    }

    private static Dictionary<int, HashSet<int>> BuildDamageMap(RigidStampRegistry registry, IReadOnlyList<RigidDamageEvent> damageEvents)
    {
        Dictionary<int, HashSet<int>> damagedLocalByBody = [];
        for (int i = 0; i < damageEvents.Count; i++)
        {
            RigidDamageEvent damage = damageEvents[i];
            if (!registry.TryGet(damage.WorldX, damage.WorldY, out RigidStamp stamp))
            {
                continue;
            }

            if (!damagedLocalByBody.TryGetValue(stamp.BodyKey, out HashSet<int>? locals))
            {
                locals = [];
                damagedLocalByBody.Add(stamp.BodyKey, locals);
            }

            _ = locals.Add((stamp.LocalY << 16) ^ stamp.LocalX);
        }

        return damagedLocalByBody;
    }

    private RebuildPlan[] PreparePlans(List<RebuildWorkItem> workItems, JobSystem? jobs)
    {
        if (workItems.Count == 0)
        {
            LastPlanningUsedJobSystem = false;
            LastPlanningWorkerCount = 0;
            return [];
        }

        RebuildPlan[] plans = new RebuildPlan[workItems.Count];
        if (jobs is null)
        {
            LastPlanningUsedJobSystem = false;
            LastPlanningWorkerCount = 0;
            for (int i = 0; i < workItems.Count; i++)
            {
                plans[i] = PreparePlan(workItems[i], FragmentPixelThreshold);
            }

            return plans;
        }

        int[] workerHits = GC.AllocateArray<int>(jobs.WorkerCount, pinned: true);
        RebuildPreparationBatch batch = new([.. workItems], plans, FragmentPixelThreshold, workerHits);
        LastPlanningUsedJobSystem = true;
        jobs.ParallelRange(workItems.Count, 1, PreparePlansJob, batch);
        LastPlanningWorkerCount = CountWorkerHits(workerHits);
        return plans;
    }

    private RigidDestructionResult ApplyPlan(
        B2WorldId worldId,
        PhysicsWorld physicsWorld,
        CellGrid grid,
        RigidStampRegistry registry,
        RebuildPlan plan)
    {
        int erased = RigidBodyRasterizer.EraseAtCurrentTransform(plan.Body, grid, registry);
        _ = erased;
        Box2D.b2DestroyBody(plan.Body.BodyId);
        physicsWorld.RemoveBody(plan.Body.BodyKey);

        int fragments = 0;
        foreach (ParticleSpawn spawn in plan.FragmentSpawns)
        {
            if (_particles is not null)
            {
                _ = _particles.TrySpawn(in spawn);
            }

            fragments++;
        }

        int created = 0;
        foreach (ChildBodyPlan child in plan.Children)
        {
            B2BodyId childBodyId = ShapeBuilder.BuildBody(worldId, child.ConvexPieces.AsSpan(0, child.ConvexPieceCount), plan.Parent.PositionPixels);
            Box2D.b2Body_SetTransform(childBodyId, plan.Parent.NativeTransform.P, plan.Parent.NativeTransform.Q);
            TransferVelocity(childBodyId, child.SourceBounds, plan.Body.Mask.LocalOrigin, plan.Parent);
            _ = physicsWorld.AddBody(childBodyId, child.Mask);
            created++;
        }

        return new RigidDestructionResult(1, 1, created, fragments, 0);
    }

    private static RebuildPlan PreparePlan(RebuildWorkItem workItem, int fragmentPixelThreshold)
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

            RebuildPlan plan = new(workItem.Body, workItem.Parent);
            for (int i = 0; i < componentCount; i++)
            {
                ConnectedComponent component = components[i];
                if (component.IsFragment)
                {
                    CollectFragmentParticles(sourceMask, labels.AsSpan(0, area), component, workItem.Parent, plan.FragmentSpawns);
                    continue;
                }

                if (TryCreateChildPlan(sourceMask, materials.AsSpan(0, area), labels.AsSpan(0, area), component, plan.Children))
                {
                    continue;
                }
            }

            return plan;
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
        List<ChildBodyPlan> children)
    {
        RectI bounds = component.Bounds;
        int width = bounds.Width;
        int height = bounds.Height;
        int area = width * height;
        byte[] childSolid = ArrayPool<byte>.Shared.Rent(area);
        ushort[] childMaterials = ArrayPool<ushort>.Shared.Rent(area);

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
            if (!TryBuildConvexPieces(childSolid.AsSpan(0, area), width, height, childOrigin, out ConvexPolygon[] pieces, out int pieceCount))
            {
                return false;
            }

            BodyLocalMask childMask = new(width, height, childOrigin, childSolid.AsSpan(0, area), childMaterials.AsSpan(0, area));
            children.Add(new ChildBodyPlan(childMask, pieces, pieceCount, bounds));
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(childSolid);
            ArrayPool<ushort>.Shared.Return(childMaterials);
        }
    }

    private static bool TryBuildConvexPieces(
        ReadOnlySpan<byte> solid,
        int width,
        int height,
        Vector2 localOrigin,
        out ConvexPolygon[] pieces,
        out int pieceCount)
    {
        Vector2[] contour = ArrayPool<Vector2>.Shared.Rent(MarchingSquares.GetMaximumContourPointCount(width, height));
        Vector2[] simplified = ArrayPool<Vector2>.Shared.Rent(contour.Length);
        ConvexPolygon[] output = new ConvexPolygon[Math.Max(8, width * height)];

        try
        {
            int contourCount = MarchingSquares.TraceOuterContour(solid, width, height, contour);
            if (contourCount < 4)
            {
                pieces = [];
                pieceCount = 0;
                return false;
            }

            int simplifiedCount = DouglasPeucker.Simplify(contour.AsSpan(0, contourCount), simplified, epsilon: 0f, closed: true);
            for (int i = 0; i < simplifiedCount; i++)
            {
                simplified[i] -= localOrigin;
            }

            pieceCount = ConvexDecomposer.Decompose(simplified.AsSpan(0, simplifiedCount), output);
            pieces = output;
            return pieceCount > 0;
        }
        finally
        {
            ArrayPool<Vector2>.Shared.Return(contour);
            ArrayPool<Vector2>.Shared.Return(simplified);
        }
    }

    private static void CollectFragmentParticles(
        BodyLocalMask sourceMask,
        ReadOnlySpan<int> labels,
        in ConnectedComponent component,
        in ParentBodyState parent,
        List<ParticleSpawn> destination)
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
                destination.Add(new ParticleSpawn(world.X, world.Y, 0f, 0f, material, ColorVariant: 0, Life: 0));
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
        PixelEngine.Core.Mathematics.Transform2D transform)
    {
        public readonly B2Transform NativeTransform = nativeTransform;
        public readonly B2Vec2 LinearVelocity = linearVelocity;
        public readonly float AngularVelocity = angularVelocity;
        public readonly PixelEngine.Core.Mathematics.Transform2D Transform = transform;
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

    private sealed class RebuildWorkItem(PixelRigidBody body, ParentBodyState parent, HashSet<int> damagedLocals)
    {
        public PixelRigidBody Body { get; } = body;

        public ParentBodyState Parent { get; } = parent;

        public HashSet<int> DamagedLocals { get; } = damagedLocals;
    }

    private sealed class RebuildPlan(PixelRigidBody body, ParentBodyState parent)
    {
        public PixelRigidBody Body { get; } = body;

        public ParentBodyState Parent { get; } = parent;

        public List<ChildBodyPlan> Children { get; } = [];

        public List<ParticleSpawn> FragmentSpawns { get; } = [];
    }

    private sealed class ChildBodyPlan(
        BodyLocalMask mask,
        ConvexPolygon[] convexPieces,
        int convexPieceCount,
        RectI sourceBounds)
    {
        public BodyLocalMask Mask { get; } = mask;

        public ConvexPolygon[] ConvexPieces { get; } = convexPieces;

        public int ConvexPieceCount { get; } = convexPieceCount;

        public RectI SourceBounds { get; } = sourceBounds;
    }

    private sealed class RebuildPreparationBatch(
        RebuildWorkItem[] items,
        RebuildPlan[] plans,
        int fragmentPixelThreshold,
        int[] workerHits)
    {
        public RebuildWorkItem[] Items { get; } = items;

        public RebuildPlan[] Plans { get; } = plans;

        public int FragmentPixelThreshold { get; } = fragmentPixelThreshold;

        public void MarkWorker(int workerIndex)
        {
            if ((uint)workerIndex < (uint)workerHits.Length)
            {
                Volatile.Write(ref workerHits[workerIndex], 1);
            }
        }
    }
}
