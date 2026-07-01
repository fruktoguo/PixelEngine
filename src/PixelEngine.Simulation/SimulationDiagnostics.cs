namespace PixelEngine.Simulation;

internal sealed class SimulationDiagnostics
{
    private const int MaxRecordCount = 256;
    private readonly BoundaryWakeRecord[] _boundaryWakeRecords = new BoundaryWakeRecord[MaxRecordCount];
    private readonly ReactionProbeRecord[] _reactionRecords = new ReactionProbeRecord[MaxRecordCount];
    private int _boundaryWakeCount;
    private int _boundaryWakeRecordCount;
    private int _reactionAttemptCount;
    private int _reactionSuccessCount;
    private int _boundaryReactionCount;
    private int _reactionRecordCount;

    public int BoundaryWakeCount => Volatile.Read(ref _boundaryWakeCount);

    public int ReactionAttemptCount => Volatile.Read(ref _reactionAttemptCount);

    public int ReactionSuccessCount => Volatile.Read(ref _reactionSuccessCount);

    public int BoundaryReactionCount => Volatile.Read(ref _boundaryReactionCount);

    public ReadOnlySpan<BoundaryWakeRecord> BoundaryWakeRecords => _boundaryWakeRecords.AsSpan(0, Math.Min(Volatile.Read(ref _boundaryWakeRecordCount), _boundaryWakeRecords.Length));

    public ReadOnlySpan<ReactionProbeRecord> ReactionRecords => _reactionRecords.AsSpan(0, Math.Min(Volatile.Read(ref _reactionRecordCount), _reactionRecords.Length));

    public void ResetFrameCounters()
    {
        Volatile.Write(ref _boundaryWakeCount, 0);
        Volatile.Write(ref _reactionAttemptCount, 0);
        Volatile.Write(ref _reactionSuccessCount, 0);
        Volatile.Write(ref _boundaryReactionCount, 0);
        Volatile.Write(ref _boundaryWakeRecordCount, 0);
        Volatile.Write(ref _reactionRecordCount, 0);
    }

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
}
