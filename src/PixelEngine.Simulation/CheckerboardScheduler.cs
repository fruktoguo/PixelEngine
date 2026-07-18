using System.Diagnostics;
using PixelEngine.Core;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Core.Threading;

namespace PixelEngine.Simulation;

/// <summary>
/// 按 2x2 chunk parity bucket 执行 4-pass checkerboard CA 调度。
/// </summary>
public sealed class CheckerboardScheduler
{
    private static readonly RangeJob UpdateRangeJob = static (start, end, workerIndex, context) =>
    {
        CheckerboardScheduler scheduler = (CheckerboardScheduler)context!;
        scheduler.UpdateActiveBucketRange(start, end);
    };

    private readonly Chunk[][] _buckets =
    [
        [],
        [],
        [],
        [],
    ];
    private readonly ChunkNeighborhood[][] _bucketNeighborhoods =
    [
        [],
        [],
        [],
        [],
    ];

    private readonly int[] _counts = new int[4];
    private Chunk[] _parityPrepareChunks = [];
    private int _parityPrepareCount;
    private Chunk[] _activeBucket = [];
    private ChunkNeighborhood[] _activeNeighborhoodBucket = [];
    private IChunkSource? _activeChunks;
    private MaterialPropsTable? _activeMaterials;
    private IRigidDamageSink? _activeRigidDamageSink;
    private IReactionExecutor? _activeReactionExecutor;
    private ILifetimeSink? _activeLifetimeSink;
    private IMaterialCustomUpdateExecutor? _activeCustomUpdateExecutor;
    private SimulationDiagnostics? _activeDiagnostics;
    private ulong _activeWorldSeed;
    private uint _activeFrameIndex;
    private byte _activeParityBit;

    /// <summary>
    /// 按 checkerboard 4-pass 更新所有 awake chunk。
    /// </summary>
    internal void Step(
        IChunkSource chunks,
        JobSystem jobs,
        MaterialPropsTable materials,
        byte parityBit,
        uint frameIndex,
        ulong worldSeed,
        IRigidDamageSink rigidDamageSink,
        IReactionExecutor reactionExecutor,
        ILifetimeSink lifetimeSink,
        IMaterialCustomUpdateExecutor customUpdateExecutor,
        SimulationDiagnostics diagnostics,
        FrameProfiler? profiler,
        CaChunkThrottlePolicy throttlePolicy = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(rigidDamageSink);
        ArgumentNullException.ThrowIfNull(reactionExecutor);
        ArgumentNullException.ThrowIfNull(lifetimeSink);
        ArgumentNullException.ThrowIfNull(customUpdateExecutor);
        ArgumentNullException.ThrowIfNull(diagnostics);

        // 先把 awake 且 current-dirty 的 chunk 分到 2x2 parity bucket，再按 4-pass 顺序更新。
        int awakeCount = BuildBuckets(chunks, throttlePolicy);
        if (awakeCount == 0)
        {
            return;
        }

        // 远区降频 chunk 需先裁剪 current dirty，避免隔帧更新时 parity 不一致。
        PrepareThrottledChunkParity(parityBit);
        CaptureContext(chunks, materials, rigidDamageSink, reactionExecutor, lifetimeSink, customUpdateExecutor, diagnostics, parityBit, frameIndex, worldSeed);
        try
        {
            if (awakeCount < EngineConstants.SingleThreadChunkThreshold)
            {
                StepBucketsSingleThread();
                return;
            }

            // 每个 pass 内 bucket 互不邻接，可并行；pass 之间串行保证无写冲突。
            for (int pass = 0; pass < _buckets.Length; pass++)
            {
                int count = _counts[pass];
                if (count == 0)
                {
                    continue;
                }

                long startTimestamp = Stopwatch.GetTimestamp();
                _activeBucket = _buckets[pass];
                _activeNeighborhoodBucket = _bucketNeighborhoods[pass];
                jobs.ParallelRange(count, 1, UpdateRangeJob, this);
                RecordPass(profiler, pass, startTimestamp);
            }
        }
        finally
        {
            ClearContext();
        }
    }

    /// <summary>
    /// 不经过 JobSystem，按同样 4-pass 顺序单线程更新所有 awake chunk。
    /// </summary>
    internal void StepSingleThread(
        IChunkSource chunks,
        MaterialPropsTable materials,
        byte parityBit,
        uint frameIndex,
        ulong worldSeed,
        IRigidDamageSink rigidDamageSink,
        IReactionExecutor reactionExecutor,
        ILifetimeSink lifetimeSink,
        IMaterialCustomUpdateExecutor customUpdateExecutor,
        SimulationDiagnostics diagnostics,
        FrameProfiler? profiler = null,
        CaChunkThrottlePolicy throttlePolicy = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(rigidDamageSink);
        ArgumentNullException.ThrowIfNull(reactionExecutor);
        ArgumentNullException.ThrowIfNull(lifetimeSink);
        ArgumentNullException.ThrowIfNull(customUpdateExecutor);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (BuildBuckets(chunks, throttlePolicy) == 0)
        {
            return;
        }

        PrepareThrottledChunkParity(parityBit);
        CaptureContext(chunks, materials, rigidDamageSink, reactionExecutor, lifetimeSink, customUpdateExecutor, diagnostics, parityBit, frameIndex, worldSeed);
        try
        {
            StepBucketsSingleThread(profiler);
        }
        finally
        {
            ClearContext();
        }
    }

    private int BuildBuckets(IChunkSource chunks, CaChunkThrottlePolicy throttlePolicy)
    {
        ReadOnlySpan<Chunk> residentChunks = chunks.ResidentChunks;
        EnsureBucketCapacity(residentChunks.Length);
        EnsureParityPrepareCapacity(residentChunks.Length);
        ClearBuckets();
        _parityPrepareCount = 0;

        int awakeCount = 0;
        foreach (Chunk chunk in residentChunks)
        {
            // sleeping 或本帧无 current dirty 的 chunk 不进调度，保持零 CA 迭代。
            if (chunk.State != ChunkState.Awake || chunk.CurrentDirty.IsEmpty)
            {
                continue;
            }

            if (!throttlePolicy.ShouldRunDistantThisFrame(chunk.Coord))
            {
                chunk.DeferCurrentDirty();
                continue;
            }

            if (!chunks.ResolveNeighborhood(chunk.Coord, out ChunkNeighborhood neighborhood))
            {
                // 固定 resident world 的最外圈 guard chunk 只提供 halo 读写缓冲，不应被 CA 调度。
                // 缺少完整 3x3 邻域时直接丢弃这圈 dirty，避免边缘传播把有限世界外壳唤醒成非法 active chunk。
                chunk.ClearDirty();
                continue;
            }

            // 垂直 movement 最多向南跨一个 chunk。派生列位图只在 active 邻域首次失效时重建，
            // sleeping 远区既不扫描 Material SoA，也不进入 CA 迭代。
            neighborhood.Slot4.EnsureColumnOccupancy();
            neighborhood.Slot7.EnsureColumnOccupancy();

            if (throttlePolicy.Enabled && !throttlePolicy.IsFullRate(chunk.Coord))
            {
                _parityPrepareChunks[_parityPrepareCount++] = chunk;
            }

            int bucket = (chunk.Coord.X & 1) | ((chunk.Coord.Y & 1) << 1);
            int bucketIndex = _counts[bucket]++;
            _buckets[bucket][bucketIndex] = chunk;
            _bucketNeighborhoods[bucket][bucketIndex] = neighborhood;
            awakeCount++;
        }

        return awakeCount;
    }

    private void EnsureBucketCapacity(int capacity)
    {
        for (int i = 0; i < _buckets.Length; i++)
        {
            if (_buckets[i].Length < capacity)
            {
                _buckets[i] = new Chunk[capacity];
                _bucketNeighborhoods[i] = new ChunkNeighborhood[capacity];
            }
        }
    }

    private void EnsureParityPrepareCapacity(int capacity)
    {
        if (_parityPrepareChunks.Length < capacity)
        {
            _parityPrepareChunks = new Chunk[capacity];
        }
    }

    private void ClearBuckets()
    {
        for (int i = 0; i < _buckets.Length; i++)
        {
            Array.Clear(_buckets[i], 0, _counts[i]);
            Array.Clear(_bucketNeighborhoods[i], 0, _counts[i]);
            _counts[i] = 0;
        }

        Array.Clear(_parityPrepareChunks, 0, _parityPrepareCount);
    }

    private void PrepareThrottledChunkParity(byte parityBit)
    {
        for (int i = 0; i < _parityPrepareCount; i++)
        {
            _parityPrepareChunks[i].PrepareCurrentDirtyForParity(parityBit);
        }
    }

    private void CaptureContext(
        IChunkSource chunks,
        MaterialPropsTable materials,
        IRigidDamageSink rigidDamageSink,
        IReactionExecutor reactionExecutor,
        ILifetimeSink lifetimeSink,
        IMaterialCustomUpdateExecutor customUpdateExecutor,
        SimulationDiagnostics diagnostics,
        byte parityBit,
        uint frameIndex,
        ulong worldSeed)
    {
        _activeChunks = chunks;
        _activeMaterials = materials;
        _activeRigidDamageSink = rigidDamageSink;
        _activeReactionExecutor = reactionExecutor;
        _activeLifetimeSink = lifetimeSink;
        _activeCustomUpdateExecutor = customUpdateExecutor;
        _activeDiagnostics = diagnostics;
        _activeParityBit = parityBit;
        _activeFrameIndex = frameIndex;
        _activeWorldSeed = worldSeed;
    }

    private void ClearContext()
    {
        _activeBucket = [];
        _activeNeighborhoodBucket = [];
        _activeChunks = null;
        _activeMaterials = null;
        _activeRigidDamageSink = null;
        _activeReactionExecutor = null;
        _activeLifetimeSink = null;
        _activeCustomUpdateExecutor = null;
        _activeDiagnostics = null;
        _activeWorldSeed = 0;
        _activeFrameIndex = 0;
        _activeParityBit = 0;
    }

    private void StepBucketsSingleThread(FrameProfiler? profiler = null)
    {
        for (int pass = 0; pass < _buckets.Length; pass++)
        {
            long startTimestamp = Stopwatch.GetTimestamp();
            _activeBucket = _buckets[pass];
            _activeNeighborhoodBucket = _bucketNeighborhoods[pass];
            UpdateActiveBucketRange(0, _counts[pass]);
            RecordPass(profiler, pass, startTimestamp);
        }
    }

    private static void RecordPass(FrameProfiler? profiler, int pass, long startTimestamp)
    {
        if (profiler is null)
        {
            return;
        }

        long elapsed = Stopwatch.GetTimestamp() - startTimestamp;
        double ms = elapsed * 1000.0 / Stopwatch.Frequency;
        profiler.RecordSub((FrameSubPhase)((int)FrameSubPhase.CaPassA + pass), ms);
    }

    private void UpdateActiveBucketRange(int start, int end)
    {
        IChunkSource chunks = _activeChunks ?? throw new InvalidOperationException("checkerboard 调度上下文未设置。");
        MaterialPropsTable materials = _activeMaterials ?? throw new InvalidOperationException("checkerboard 材质上下文未设置。");
        IRigidDamageSink rigidDamageSink = _activeRigidDamageSink ?? throw new InvalidOperationException("checkerboard damage sink 上下文未设置。");
        IReactionExecutor reactionExecutor = _activeReactionExecutor ?? throw new InvalidOperationException("checkerboard reaction 上下文未设置。");
        ILifetimeSink lifetimeSink = _activeLifetimeSink ?? throw new InvalidOperationException("checkerboard lifetime 上下文未设置。");
        IMaterialCustomUpdateExecutor customUpdateExecutor = _activeCustomUpdateExecutor ?? throw new InvalidOperationException("checkerboard custom-update 上下文未设置。");
        SimulationDiagnostics diagnostics = _activeDiagnostics ?? throw new InvalidOperationException("checkerboard diagnostics 上下文未设置。");

        for (int i = start; i < end; i++)
        {
            Chunk chunk = _activeBucket[i];
            ChunkNeighborhood neighborhood = _activeNeighborhoodBucket[i];
            // 记录本帧实际迭代的 dirty rect，供 Editor 叠层验证 sleeping 区零迭代。
            diagnostics.RecordCaIteration(chunk.Coord, chunk.CurrentDirty);
            ChunkUpdater.UpdateChunk(
                chunk,
                in neighborhood,
                chunks,
                materials,
                _activeParityBit,
                _activeFrameIndex,
                _activeWorldSeed,
                rigidDamageSink,
                reactionExecutor,
                lifetimeSink,
                customUpdateExecutor,
                diagnostics);
        }
    }
}
