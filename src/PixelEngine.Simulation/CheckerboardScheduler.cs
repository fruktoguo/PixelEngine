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

    private readonly int[] _counts = new int[4];
    private Chunk[] _activeBucket = [];
    private IChunkSource? _activeChunks;
    private MaterialPropsTable? _activeMaterials;
    private IRigidDamageSink? _activeRigidDamageSink;
    private IReactionExecutor? _activeReactionExecutor;
    private ILifetimeSink? _activeLifetimeSink;
    private ulong _activeWorldSeed;
    private uint _activeFrameIndex;
    private byte _activeParityBit;

    /// <summary>
    /// 按 checkerboard 4-pass 更新所有 awake chunk。
    /// </summary>
    public void Step(
        IChunkSource chunks,
        JobSystem jobs,
        MaterialPropsTable materials,
        byte parityBit,
        uint frameIndex,
        ulong worldSeed,
        IRigidDamageSink rigidDamageSink,
        IReactionExecutor reactionExecutor,
        ILifetimeSink lifetimeSink,
        FrameProfiler? profiler)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(rigidDamageSink);
        ArgumentNullException.ThrowIfNull(reactionExecutor);
        ArgumentNullException.ThrowIfNull(lifetimeSink);

        int awakeCount = BuildBuckets(chunks.ResidentChunks);
        if (awakeCount == 0)
        {
            return;
        }

        CaptureContext(chunks, materials, rigidDamageSink, reactionExecutor, lifetimeSink, parityBit, frameIndex, worldSeed);
        try
        {
            if (awakeCount < EngineConstants.SingleThreadChunkThreshold)
            {
                StepBucketsSingleThread();
                return;
            }

            for (int pass = 0; pass < _buckets.Length; pass++)
            {
                int count = _counts[pass];
                if (count == 0)
                {
                    continue;
                }

                long startTimestamp = Stopwatch.GetTimestamp();
                _activeBucket = _buckets[pass];
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
    public void StepSingleThread(
        IChunkSource chunks,
        MaterialPropsTable materials,
        byte parityBit,
        uint frameIndex,
        ulong worldSeed,
        IRigidDamageSink rigidDamageSink,
        IReactionExecutor reactionExecutor,
        ILifetimeSink lifetimeSink,
        FrameProfiler? profiler = null)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(rigidDamageSink);
        ArgumentNullException.ThrowIfNull(reactionExecutor);
        ArgumentNullException.ThrowIfNull(lifetimeSink);

        if (BuildBuckets(chunks.ResidentChunks) == 0)
        {
            return;
        }

        CaptureContext(chunks, materials, rigidDamageSink, reactionExecutor, lifetimeSink, parityBit, frameIndex, worldSeed);
        try
        {
            StepBucketsSingleThread(profiler);
        }
        finally
        {
            ClearContext();
        }
    }

    private int BuildBuckets(ReadOnlySpan<Chunk> residentChunks)
    {
        EnsureBucketCapacity(residentChunks.Length);
        ClearBuckets();

        int awakeCount = 0;
        foreach (Chunk chunk in residentChunks)
        {
            if (chunk.State != ChunkState.Awake || chunk.CurrentDirty.IsEmpty)
            {
                continue;
            }

            int bucket = (chunk.Coord.X & 1) | ((chunk.Coord.Y & 1) << 1);
            _buckets[bucket][_counts[bucket]++] = chunk;
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
            }
        }
    }

    private void ClearBuckets()
    {
        for (int i = 0; i < _buckets.Length; i++)
        {
            Array.Clear(_buckets[i], 0, _counts[i]);
            _counts[i] = 0;
        }
    }

    private void CaptureContext(
        IChunkSource chunks,
        MaterialPropsTable materials,
        IRigidDamageSink rigidDamageSink,
        IReactionExecutor reactionExecutor,
        ILifetimeSink lifetimeSink,
        byte parityBit,
        uint frameIndex,
        ulong worldSeed)
    {
        _activeChunks = chunks;
        _activeMaterials = materials;
        _activeRigidDamageSink = rigidDamageSink;
        _activeReactionExecutor = reactionExecutor;
        _activeLifetimeSink = lifetimeSink;
        _activeParityBit = parityBit;
        _activeFrameIndex = frameIndex;
        _activeWorldSeed = worldSeed;
    }

    private void ClearContext()
    {
        _activeBucket = [];
        _activeChunks = null;
        _activeMaterials = null;
        _activeRigidDamageSink = null;
        _activeReactionExecutor = null;
        _activeLifetimeSink = null;
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
        profiler.RecordSub((FrameSubPhase)((int)FrameSubPhase.CheckerboardA + pass), ms);
    }

    private void UpdateActiveBucketRange(int start, int end)
    {
        IChunkSource chunks = _activeChunks ?? throw new InvalidOperationException("checkerboard 调度上下文未设置。");
        MaterialPropsTable materials = _activeMaterials ?? throw new InvalidOperationException("checkerboard 材质上下文未设置。");
        IRigidDamageSink rigidDamageSink = _activeRigidDamageSink ?? throw new InvalidOperationException("checkerboard damage sink 上下文未设置。");
        IReactionExecutor reactionExecutor = _activeReactionExecutor ?? throw new InvalidOperationException("checkerboard reaction 上下文未设置。");
        ILifetimeSink lifetimeSink = _activeLifetimeSink ?? throw new InvalidOperationException("checkerboard lifetime 上下文未设置。");

        for (int i = start; i < end; i++)
        {
            ChunkUpdater.UpdateChunk(
                _activeBucket[i],
                chunks,
                materials,
                _activeParityBit,
                _activeFrameIndex,
                _activeWorldSeed,
                rigidDamageSink,
                reactionExecutor,
                lifetimeSink);
        }
    }
}
