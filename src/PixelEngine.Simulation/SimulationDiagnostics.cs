namespace PixelEngine.Simulation;

/// <summary>
/// 模拟帧内诊断计数器与环形记录缓冲；供调试叠层与性能分析读取。
/// </summary>
internal sealed class SimulationDiagnostics
{
    private const int MaxRecordCount = 256;
    private readonly BoundaryWakeRecord[] _boundaryWakeRecords = new BoundaryWakeRecord[MaxRecordCount];
    private readonly ReactionProbeRecord[] _reactionRecords = new ReactionProbeRecord[MaxRecordCount];
    private readonly CaIterationSnapshot[] _caIterationRecords = new CaIterationSnapshot[MaxRecordCount];
    private int _boundaryWakeCount;
    private int _boundaryWakeRecordCount;
    private int _reactionAttemptCount;
    private int _reactionSuccessCount;
    private int _boundaryReactionCount;
    private int _reactionRecordCount;
    private int _caIterationRecordCount;

    /// <summary>本帧边界唤醒总次数（含超出记录上限的部分）。</summary>
    public int BoundaryWakeCount => Volatile.Read(ref _boundaryWakeCount);

    /// <summary>本帧化学反应尝试次数。</summary>
    public int ReactionAttemptCount => Volatile.Read(ref _reactionAttemptCount);

    /// <summary>本帧化学反应成功次数。</summary>
    public int ReactionSuccessCount => Volatile.Read(ref _reactionSuccessCount);

    /// <summary>本帧跨 chunk 边界反应成功次数。</summary>
    public int BoundaryReactionCount => Volatile.Read(ref _boundaryReactionCount);

    /// <summary>本帧已记录的边界唤醒明细（最多 <see cref="MaxRecordCount"/> 条）。</summary>
    public ReadOnlySpan<BoundaryWakeRecord> BoundaryWakeRecords => _boundaryWakeRecords.AsSpan(0, Math.Min(Volatile.Read(ref _boundaryWakeRecordCount), _boundaryWakeRecords.Length));

    /// <summary>本帧已记录的反应探测明细。</summary>
    public ReadOnlySpan<ReactionProbeRecord> ReactionRecords => _reactionRecords.AsSpan(0, Math.Min(Volatile.Read(ref _reactionRecordCount), _reactionRecords.Length));

    /// <summary>本帧已记录的 CA 迭代 chunk 快照。</summary>
    public ReadOnlySpan<CaIterationSnapshot> CaIterationRecords => _caIterationRecords.AsSpan(0, Math.Min(Volatile.Read(ref _caIterationRecordCount), _caIterationRecords.Length));

    /// <summary>
    /// 重置帧级计数器与环形缓冲写入位置；每帧开始时调用。
    /// </summary>
    public void ResetFrameCounters()
    {
        Volatile.Write(ref _boundaryWakeCount, 0);
        Volatile.Write(ref _reactionAttemptCount, 0);
        Volatile.Write(ref _reactionSuccessCount, 0);
        Volatile.Write(ref _boundaryReactionCount, 0);
        Volatile.Write(ref _boundaryWakeRecordCount, 0);
        Volatile.Write(ref _reactionRecordCount, 0);
        Volatile.Write(ref _caIterationRecordCount, 0);
    }

    /// <summary>
    /// 仅清空 CA 迭代记录；CA 子步之间可能需要独立重置。
    /// </summary>
    public void ResetCaIterationRecords()
    {
        Volatile.Write(ref _caIterationRecordCount, 0);
    }

    /// <summary>
    /// 记录一次跨 chunk 边界 dirty 唤醒；计数始终递增，明细写入环形缓冲。
    /// </summary>
    public void RecordBoundaryWake(ChunkCoord targetCoord, int incomingSlot, DirtyRect rect)
    {
        _ = Interlocked.Increment(ref _boundaryWakeCount);
        int recordIndex = Interlocked.Increment(ref _boundaryWakeRecordCount) - 1;
        if ((uint)recordIndex < (uint)_boundaryWakeRecords.Length)
        {
            _boundaryWakeRecords[recordIndex] = new BoundaryWakeRecord(targetCoord, incomingSlot, rect);
        }
    }

    public void RecordReactionAttempt()
    {
        _ = Interlocked.Increment(ref _reactionAttemptCount);
    }

    /// <summary>
    /// 记录一次成功反应；若两格跨 chunk 则额外累加边界反应计数。
    /// </summary>
    public void RecordReactionSuccess(int wx1, int wy1, ushort materialA, int wx2, int wy2, ushort materialB)
    {
        bool boundary = CellAddressing.WorldToChunk(wx1, wy1) != CellAddressing.WorldToChunk(wx2, wy2);
        _ = Interlocked.Increment(ref _reactionSuccessCount);
        if (boundary)
        {
            _ = Interlocked.Increment(ref _boundaryReactionCount);
        }

        int recordIndex = Interlocked.Increment(ref _reactionRecordCount) - 1;
        if ((uint)recordIndex < (uint)_reactionRecords.Length)
        {
            _reactionRecords[recordIndex] = new ReactionProbeRecord(wx1, wy1, materialA, wx2, wy2, materialB, boundary);
        }
    }

    /// <summary>
    /// 记录本帧某 chunk 实际参与 CA 迭代的 dirty 矩形。
    /// </summary>
    public void RecordCaIteration(ChunkCoord coord, DirtyRect rect)
    {
        int recordIndex = Interlocked.Increment(ref _caIterationRecordCount) - 1;
        if ((uint)recordIndex < (uint)_caIterationRecords.Length)
        {
            _caIterationRecords[recordIndex] = new CaIterationSnapshot(coord, rect);
        }
    }
}
