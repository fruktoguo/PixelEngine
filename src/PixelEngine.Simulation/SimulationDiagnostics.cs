namespace PixelEngine.Simulation;

internal sealed class SimulationDiagnostics
{
    private const int MaxRecordCount = 256;
    private readonly BoundaryWakeRecord[] _boundaryWakeRecords = new BoundaryWakeRecord[MaxRecordCount];
    private readonly ReactionProbeRecord[] _reactionRecords = new ReactionProbeRecord[MaxRecordCount];
    private int _boundaryWakeRecordCount;
    private int _reactionRecordCount;

    public int BoundaryWakeCount { get; private set; }

    public int ReactionAttemptCount { get; private set; }

    public int ReactionSuccessCount { get; private set; }

    public int BoundaryReactionCount { get; private set; }

    public ReadOnlySpan<BoundaryWakeRecord> BoundaryWakeRecords => _boundaryWakeRecords.AsSpan(0, _boundaryWakeRecordCount);

    public ReadOnlySpan<ReactionProbeRecord> ReactionRecords => _reactionRecords.AsSpan(0, _reactionRecordCount);

    public void ResetFrameCounters()
    {
        BoundaryWakeCount = 0;
        ReactionAttemptCount = 0;
        ReactionSuccessCount = 0;
        BoundaryReactionCount = 0;
        _boundaryWakeRecordCount = 0;
        _reactionRecordCount = 0;
    }

    public void RecordBoundaryWake(ChunkCoord targetCoord, int incomingSlot, DirtyRect rect)
    {
        BoundaryWakeCount++;
        if (_boundaryWakeRecordCount < _boundaryWakeRecords.Length)
        {
            _boundaryWakeRecords[_boundaryWakeRecordCount++] = new BoundaryWakeRecord(targetCoord, incomingSlot, rect);
        }
    }

    public void RecordReactionAttempt()
    {
        ReactionAttemptCount++;
    }

    public void RecordReactionSuccess(int wx1, int wy1, ushort materialA, int wx2, int wy2, ushort materialB)
    {
        bool boundary = CellAddressing.WorldToChunk(wx1, wy1) != CellAddressing.WorldToChunk(wx2, wy2);
        ReactionSuccessCount++;
        if (boundary)
        {
            BoundaryReactionCount++;
        }

        if (_reactionRecordCount < _reactionRecords.Length)
        {
            _reactionRecords[_reactionRecordCount++] = new ReactionProbeRecord(wx1, wy1, materialA, wx2, wy2, materialB, boundary);
        }
    }
}
