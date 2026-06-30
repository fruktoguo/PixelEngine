using System.Diagnostics;
using PixelEngine.Core;

namespace PixelEngine.Simulation;

internal static class DirtyRegionMarker
{
    public static void MarkCell(
        IChunkSource chunks,
        int wx,
        int wy,
        DirtyPhaseTarget target,
        bool includeBoundaryNeighbors,
        SimulationDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        ChunkCoord coord = CellAddressing.WorldToChunk(wx, wy);
        if (!chunks.TryGetChunk(coord, out Chunk chunk))
        {
            throw new InvalidOperationException($"目标 chunk 未驻留：{coord}。");
        }

        DirtyRect localRect = DirtyRect.Empty.Union(
            CellAddressing.LocalCoord(wx),
            CellAddressing.LocalCoord(wy),
            EngineConstants.DirtyRectPadding);
        MarkChunkDirty(chunk, localRect, target);

        if (includeBoundaryNeighbors)
        {
            MarkBoundaryNeighbors(chunks, coord, wx, wy, target, diagnostics);
        }
    }

    private static void MarkBoundaryNeighbors(
        IChunkSource chunks,
        ChunkCoord center,
        int wx,
        int wy,
        DirtyPhaseTarget target,
        SimulationDiagnostics? diagnostics)
    {
        const int chunkSize = EngineConstants.ChunkSize;
        int padding = EngineConstants.DirtyRectPadding;
        int dirtyMinX = wx - padding;
        int dirtyMinY = wy - padding;
        int dirtyMaxX = wx + padding;
        int dirtyMaxY = wy + padding;

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                ChunkCoord neighborCoord = new(center.X + dx, center.Y + dy);
                int neighborMinX = neighborCoord.X * chunkSize;
                int neighborMinY = neighborCoord.Y * chunkSize;
                int neighborMaxX = neighborMinX + chunkSize - 1;
                int neighborMaxY = neighborMinY + chunkSize - 1;

                int intersectMinX = Math.Max(dirtyMinX, neighborMinX);
                int intersectMinY = Math.Max(dirtyMinY, neighborMinY);
                int intersectMaxX = Math.Min(dirtyMaxX, neighborMaxX);
                int intersectMaxY = Math.Min(dirtyMaxY, neighborMaxY);
                if (intersectMinX > intersectMaxX || intersectMinY > intersectMaxY)
                {
                    continue;
                }

                if (!chunks.TryGetChunk(neighborCoord, out Chunk neighbor))
                {
                    Debug.Assert(false, $"dirty 边界传播目标 chunk 未驻留：{neighborCoord}。");
                    throw new InvalidOperationException($"dirty 边界传播目标 chunk 未驻留：{neighborCoord}。");
                }

                DirtyRect neighborRect = new(
                    CellAddressing.LocalCoord(intersectMinX),
                    CellAddressing.LocalCoord(intersectMinY),
                    CellAddressing.LocalCoord(intersectMaxX),
                    CellAddressing.LocalCoord(intersectMaxY));

                if (target == DirtyPhaseTarget.Current)
                {
                    neighbor.MarkCurrentDirty(neighborRect);
                }
                else
                {
                    int neighborSlot = ((dy + 1) * 3) + dx + 1;
                    int incomingSlot = KeepAliveDirections.IncomingSlotForTouchedNeighborSlot(neighborSlot);
                    neighbor.MarkIncomingDirty(incomingSlot, neighborRect);
                    diagnostics?.RecordBoundaryWake(neighborCoord, incomingSlot, neighborRect);
                }

                if (target == DirtyPhaseTarget.Current)
                {
                    int neighborSlot = ((dy + 1) * 3) + dx + 1;
                    diagnostics?.RecordBoundaryWake(
                        neighborCoord,
                        KeepAliveDirections.IncomingSlotForTouchedNeighborSlot(neighborSlot),
                        neighborRect);
                }
            }
        }
    }

    private static void MarkChunkDirty(Chunk chunk, DirtyRect rect, DirtyPhaseTarget target)
    {
        if (target == DirtyPhaseTarget.Current)
        {
            chunk.MarkCurrentDirty(rect);
        }
        else
        {
            chunk.MarkWorkingDirty(rect);
        }
    }
}
