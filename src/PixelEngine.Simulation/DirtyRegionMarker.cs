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

    public static bool TryMarkCell(
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
            return false;
        }

        if (includeBoundaryNeighbors && !BoundaryNeighborsResident(chunks, coord, wx, wy))
        {
            return false;
        }

        DirtyRect localRect = DirtyRect.Empty.Union(
            CellAddressing.LocalCoord(wx),
            CellAddressing.LocalCoord(wy),
            EngineConstants.DirtyRectPadding);
        MarkChunkDirty(chunk, localRect, target);

        return !includeBoundaryNeighbors || TryMarkBoundaryNeighbors(chunks, coord, wx, wy, target, diagnostics);
    }

    public static void MarkRectCurrent(
        IChunkSource chunks,
        int minX,
        int minY,
        int maxX,
        int maxY,
        int padding)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        if (minX > maxX || minY > maxY)
        {
            return;
        }

        int dirtyMinX = minX - padding;
        int dirtyMinY = minY - padding;
        int dirtyMaxX = maxX + padding;
        int dirtyMaxY = maxY + padding;
        ChunkCoord minCoord = CellAddressing.WorldToChunk(dirtyMinX, dirtyMinY);
        ChunkCoord maxCoord = CellAddressing.WorldToChunk(dirtyMaxX, dirtyMaxY);
        for (int cy = minCoord.Y; cy <= maxCoord.Y; cy++)
        {
            for (int cx = minCoord.X; cx <= maxCoord.X; cx++)
            {
                ChunkCoord coord = new(cx, cy);
                if (!chunks.TryGetChunk(coord, out Chunk chunk))
                {
                    Debug.Assert(false, $"dirty 矩形传播目标 chunk 未驻留：{coord}。");
                    throw new InvalidOperationException($"dirty 矩形传播目标 chunk 未驻留：{coord}。");
                }

                int chunkMinX = cx * EngineConstants.ChunkSize;
                int chunkMinY = cy * EngineConstants.ChunkSize;
                int chunkMaxX = chunkMinX + EngineConstants.ChunkSize - 1;
                int chunkMaxY = chunkMinY + EngineConstants.ChunkSize - 1;
                int intersectMinX = Math.Max(dirtyMinX, chunkMinX);
                int intersectMinY = Math.Max(dirtyMinY, chunkMinY);
                int intersectMaxX = Math.Min(dirtyMaxX, chunkMaxX);
                int intersectMaxY = Math.Min(dirtyMaxY, chunkMaxY);
                DirtyRect rect = new(
                    CellAddressing.LocalCoord(intersectMinX),
                    CellAddressing.LocalCoord(intersectMinY),
                    CellAddressing.LocalCoord(intersectMaxX),
                    CellAddressing.LocalCoord(intersectMaxY));
                chunk.MarkCurrentDirty(rect);
            }
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
        _ = TryMarkBoundaryNeighbors(chunks, center, wx, wy, target, diagnostics, throwOnMissing: true);
    }

    private static bool TryMarkBoundaryNeighbors(
        IChunkSource chunks,
        ChunkCoord center,
        int wx,
        int wy,
        DirtyPhaseTarget target,
        SimulationDiagnostics? diagnostics,
        bool throwOnMissing = false)
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
                    if (!throwOnMissing)
                    {
                        return false;
                    }

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

        return true;
    }

    private static bool BoundaryNeighborsResident(IChunkSource chunks, ChunkCoord center, int wx, int wy)
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

                if (!chunks.TryGetChunk(neighborCoord, out _))
                {
                    return false;
                }
            }
        }

        return true;
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
