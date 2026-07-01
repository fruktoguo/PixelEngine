using System.Diagnostics;
using PixelEngine.Core;
using PixelEngine.Core.Random;

namespace PixelEngine.Simulation;

/// <summary>
/// 单个 chunk 的 bottom-up CA 更新器。
/// </summary>
internal static class ChunkUpdater
{
    public static void UpdateChunk(
        Chunk chunk,
        IChunkSource chunks,
        MaterialPropsTable materials,
        byte parityBit,
        uint frameIndex,
        ulong worldSeed,
        IRigidDamageSink rigidDamageSink,
        IReactionExecutor reactionExecutor,
        ILifetimeSink lifetimeSink,
        IMaterialCustomUpdateExecutor customUpdateExecutor,
        SimulationDiagnostics diagnostics)
    {
        DirtyRect rect = chunk.CurrentDirty;
        if (rect.IsEmpty)
        {
            return;
        }

        NeighborWindow window = new(chunks, chunk.Coord);
        Pcg32 rng = RngFactory.ForChunk(worldSeed, chunk.Coord.X, chunk.Coord.Y, frameIndex);
        int worldBaseX = chunk.Coord.X << EngineConstants.ChunkSizeLog2;
        int worldBaseY = chunk.Coord.Y << EngineConstants.ChunkSizeLog2;

        for (int ly = rect.MaxY; ly >= rect.MinY; ly--)
        {
            int wy = worldBaseY + ly;
            for (int lx = rect.MinX; lx <= rect.MaxX; lx++)
            {
                int wx = worldBaseX + lx;
                ushort material = window.GetMaterial(wx, wy);
                if (material == 0)
                {
                    continue;
                }

                byte flags = window.GetFlags(wx, wy);
                if (CellFlags.MatchesFrame(flags, parityBit) || CellFlags.Has(flags, CellFlags.RigidOwned))
                {
                    continue;
                }

                ProcessLifetime(ref window, chunks, lifetimeSink, wx, wy, material, parityBit);
                material = window.GetMaterial(wx, wy);
                if (material == 0)
                {
                    continue;
                }

                flags = window.GetFlags(wx, wy);
                if (CellFlags.MatchesFrame(flags, parityBit) || CellFlags.Has(flags, CellFlags.RigidOwned))
                {
                    continue;
                }

                bool preferNegative = ((parityBit ^ (byte)(ly & 1)) & CellFlags.Parity) == 0;
                int activeX = wx;
                int activeY = wy;
                bool moved = materials.TypeOf(material) switch
                {
                    CellType.Empty => false,
                    CellType.Solid => false,
                    CellType.Powder => TryMovePowder(ref window, chunks, materials, rigidDamageSink, diagnostics, wx, wy, material, parityBit, preferNegative, out activeX, out activeY),
                    CellType.Liquid => TryMoveLiquid(ref window, chunks, materials, rigidDamageSink, diagnostics, wx, wy, material, parityBit, preferNegative, out activeX, out activeY),
                    CellType.Gas => TryMoveGas(ref window, chunks, materials, rigidDamageSink, diagnostics, wx, wy, material, parityBit, ref rng, out activeX, out activeY),
                    CellType.Fire => false,
                    _ => throw new InvalidOperationException($"未知 CellType：{materials.TypeOf(material)}。"),
                };

                ushort activeMaterial = window.GetMaterial(activeX, activeY);
                bool reacted = TryReactVonNeumann(ref window, chunks, materials, reactionExecutor, diagnostics, activeX, activeY, activeMaterial, parityBit, ref rng);
                bool customUpdated = !reacted && TryRunCustomUpdate(ref window, chunks, materials, customUpdateExecutor, activeX, activeY, activeMaterial, parityBit);
                if (!moved && !reacted && !customUpdated && materials.TypeOf(material) != CellType.Solid)
                {
                    window.SetFlags(wx, wy, CellFlags.SetParity(window.GetFlags(wx, wy), parityBit));
                }
            }
        }
    }

    private static bool TryMovePowder(
        ref NeighborWindow window,
        IChunkSource chunks,
        MaterialPropsTable materials,
        IRigidDamageSink rigidDamageSink,
        SimulationDiagnostics diagnostics,
        int wx,
        int wy,
        ushort material,
        byte parityBit,
        bool preferNegative,
        out int targetX,
        out int targetY)
    {
        int firstDx = preferNegative ? -1 : 1;
        return TryMoveTo(ref window, chunks, materials, rigidDamageSink, diagnostics, wx, wy, wx, wy + 1, material, parityBit, out targetX, out targetY) ||
            TryMoveTo(ref window, chunks, materials, rigidDamageSink, diagnostics, wx, wy, wx + firstDx, wy + 1, material, parityBit, out targetX, out targetY) ||
            TryMoveTo(ref window, chunks, materials, rigidDamageSink, diagnostics, wx, wy, wx - firstDx, wy + 1, material, parityBit, out targetX, out targetY);
    }

    private static bool TryMoveLiquid(
        ref NeighborWindow window,
        IChunkSource chunks,
        MaterialPropsTable materials,
        IRigidDamageSink rigidDamageSink,
        SimulationDiagnostics diagnostics,
        int wx,
        int wy,
        ushort material,
        byte parityBit,
        bool preferNegative,
        out int targetX,
        out int targetY)
    {
        if (TryMovePowder(ref window, chunks, materials, rigidDamageSink, diagnostics, wx, wy, material, parityBit, preferNegative, out targetX, out targetY))
        {
            return true;
        }

        int firstDir = preferNegative ? -1 : 1;
        return TryMoveHorizontal(ref window, chunks, materials, rigidDamageSink, diagnostics, wx, wy, material, parityBit, firstDir, out targetX, out targetY) ||
            TryMoveHorizontal(ref window, chunks, materials, rigidDamageSink, diagnostics, wx, wy, material, parityBit, -firstDir, out targetX, out targetY);
    }

    private static bool TryMoveGas(
        ref NeighborWindow window,
        IChunkSource chunks,
        MaterialPropsTable materials,
        IRigidDamageSink rigidDamageSink,
        SimulationDiagnostics diagnostics,
        int wx,
        int wy,
        ushort material,
        byte parityBit,
        ref Pcg32 rng,
        out int targetX,
        out int targetY)
    {
        int firstDx = rng.NextInt(2) == 0 ? -1 : 1;
        return TryMoveTo(ref window, chunks, materials, rigidDamageSink, diagnostics, wx, wy, wx, wy - 1, material, parityBit, out targetX, out targetY) ||
            TryMoveTo(ref window, chunks, materials, rigidDamageSink, diagnostics, wx, wy, wx + firstDx, wy - 1, material, parityBit, out targetX, out targetY) ||
            TryMoveTo(ref window, chunks, materials, rigidDamageSink, diagnostics, wx, wy, wx - firstDx, wy - 1, material, parityBit, out targetX, out targetY) ||
            TryMoveHorizontal(ref window, chunks, materials, rigidDamageSink, diagnostics, wx, wy, material, parityBit, firstDx, out targetX, out targetY) ||
            TryMoveHorizontal(ref window, chunks, materials, rigidDamageSink, diagnostics, wx, wy, material, parityBit, -firstDx, out targetX, out targetY);
    }

    private static bool TryMoveHorizontal(
        ref NeighborWindow window,
        IChunkSource chunks,
        MaterialPropsTable materials,
        IRigidDamageSink rigidDamageSink,
        SimulationDiagnostics diagnostics,
        int wx,
        int wy,
        ushort material,
        byte parityBit,
        int direction,
        out int movedX,
        out int movedY)
    {
        movedX = wx;
        movedY = wy;
        int maxDistance = Math.Min((int)materials.DispersionOf(material), EngineConstants.MoveCap);
        int targetX = wx;
        for (int step = 1; step <= maxDistance; step++)
        {
            int candidateX = wx + (direction * step);
            if (!CanDisplace(ref window, materials, material, candidateX, wy, parityBit))
            {
                break;
            }

            targetX = candidateX;
        }

        return targetX != wx &&
            TryMoveTo(ref window, chunks, materials, rigidDamageSink, diagnostics, wx, wy, targetX, wy, material, parityBit, out movedX, out movedY);
    }

    private static bool TryMoveTo(
        ref NeighborWindow window,
        IChunkSource chunks,
        MaterialPropsTable materials,
        IRigidDamageSink rigidDamageSink,
        SimulationDiagnostics diagnostics,
        int sourceX,
        int sourceY,
        int targetX,
        int targetY,
        ushort sourceMaterial,
        byte parityBit,
        out int movedX,
        out int movedY)
    {
        movedX = sourceX;
        movedY = sourceY;
        ValidateMoveCap(sourceX, sourceY, targetX, targetY);
        if (!CanDisplace(ref window, materials, sourceMaterial, targetX, targetY, parityBit))
        {
            return false;
        }

        int targetSlot = window.SlotOf(targetX, targetY);
        byte targetFlags = window.GetFlags(targetX, targetY);
        if (CellFlags.Has(targetFlags, CellFlags.RigidOwned))
        {
            rigidDamageSink.OnOwnedCellDamaged(targetX, targetY);
            window.SetFlags(targetX, targetY, CellFlags.Clear(targetFlags, CellFlags.RigidOwned));
        }

        _ = window.Swap(sourceX, sourceY, targetX, targetY);
        window.SetFlags(sourceX, sourceY, CellFlags.SetParity(window.GetFlags(sourceX, sourceY), parityBit));
        window.SetFlags(targetX, targetY, CellFlags.SetParity(window.GetFlags(targetX, targetY), parityBit));
        MarkDirty(chunks, sourceX, sourceY);
        if (targetSlot == 4)
        {
            MarkDirty(chunks, targetX, targetY);
        }
        else
        {
            MarkKeepAliveIfCrossChunk(chunks, targetX, targetY, targetSlot, diagnostics);
        }
        movedX = targetX;
        movedY = targetY;
        return true;
    }

    private static void ProcessLifetime(
        ref NeighborWindow window,
        IChunkSource chunks,
        ILifetimeSink lifetimeSink,
        int wx,
        int wy,
        ushort material,
        byte parityBit)
    {
        byte lifetime = window.GetLifetime(wx, wy);
        if (lifetime == 0)
        {
            return;
        }

        lifetime--;
        window.SetLifetime(wx, wy, lifetime);
        MarkDirty(chunks, wx, wy);
        if (lifetime == 0)
        {
            lifetimeSink.OnExpired(ref window, wx, wy, material, parityBit);
        }
    }

    private static bool TryReactVonNeumann(
        ref NeighborWindow window,
        IChunkSource chunks,
        MaterialPropsTable materials,
        IReactionExecutor reactionExecutor,
        SimulationDiagnostics diagnostics,
        int wx,
        int wy,
        ushort material,
        byte parityBit,
        ref Pcg32 rng)
    {
        return material != 0 &&
            materials.ReactionCountOf(material) != 0 &&
            (TryReactNeighbor(ref window, chunks, reactionExecutor, diagnostics, wx, wy, material, wx, wy - 1, parityBit, ref rng) ||
            TryReactNeighbor(ref window, chunks, reactionExecutor, diagnostics, wx, wy, material, wx - 1, wy, parityBit, ref rng) ||
            TryReactNeighbor(ref window, chunks, reactionExecutor, diagnostics, wx, wy, material, wx + 1, wy, parityBit, ref rng) ||
            TryReactNeighbor(ref window, chunks, reactionExecutor, diagnostics, wx, wy, material, wx, wy + 1, parityBit, ref rng));
    }

    private static bool TryRunCustomUpdate(
        ref NeighborWindow window,
        IChunkSource chunks,
        MaterialPropsTable materials,
        IMaterialCustomUpdateExecutor customUpdateExecutor,
        int wx,
        int wy,
        ushort material,
        byte parityBit)
    {
        return material != 0 &&
            (materials.Hot.PropertyFlags[material] & MaterialProperty.HasCustomUpdate) != 0 &&
            customUpdateExecutor.TryUpdate(ref window, chunks, wx, wy, material, parityBit);
    }

    private static bool TryReactNeighbor(
        ref NeighborWindow window,
        IChunkSource chunks,
        IReactionExecutor reactionExecutor,
        SimulationDiagnostics diagnostics,
        int wx,
        int wy,
        ushort material,
        int neighborX,
        int neighborY,
        byte parityBit,
        ref Pcg32 rng)
    {
        ValidateReactionNeighbor(wx, wy, neighborX, neighborY);
        ushort neighborMaterial = window.GetMaterial(neighborX, neighborY);
        if (neighborMaterial == 0)
        {
            return false;
        }

        diagnostics.RecordReactionAttempt();
        byte randomByte = (byte)(rng.NextUInt() >> 24);
        bool reacted = reactionExecutor.TryReact(ref window, wx, wy, material, neighborX, neighborY, neighborMaterial, parityBit, randomByte);
        if (reacted)
        {
            MarkReactionTouchedCell(ref window, chunks, wx, wy, diagnostics);
            MarkReactionTouchedCell(ref window, chunks, neighborX, neighborY, diagnostics);
            diagnostics.RecordReactionSuccess(wx, wy, material, neighborX, neighborY, neighborMaterial);
        }

        return reacted;
    }

    private static void ValidateReactionNeighbor(int wx, int wy, int neighborX, int neighborY)
    {
        int distance = Math.Abs(neighborX - wx) + Math.Abs(neighborY - wy);
        if (distance != 1)
        {
            throw new InvalidOperationException("反应目标必须是 von Neumann 邻居，确保跨界写恒在 32px halo 内。");
        }
    }

    private static void MarkReactionTouchedCell(
        ref NeighborWindow window,
        IChunkSource chunks,
        int wx,
        int wy,
        SimulationDiagnostics diagnostics)
    {
        int slot = window.SlotOf(wx, wy);
        if (slot == 4)
        {
            MarkDirty(chunks, wx, wy);
        }
        else
        {
            MarkKeepAliveIfCrossChunk(chunks, wx, wy, slot, diagnostics);
        }
    }

    private static bool CanDisplace(
        ref NeighborWindow window,
        MaterialPropsTable materials,
        ushort sourceMaterial,
        int targetX,
        int targetY,
        byte parityBit)
    {
        if (CellFlags.MatchesFrame(window.GetFlags(targetX, targetY), parityBit))
        {
            return false;
        }

        ushort targetMaterial = window.GetMaterial(targetX, targetY);
        return targetMaterial == 0 || materials.DensityOf(targetMaterial) < materials.DensityOf(sourceMaterial);
    }

    private static void MarkDirty(IChunkSource chunks, int wx, int wy)
    {
        ChunkCoord coord = CellAddressing.WorldToChunk(wx, wy);
        if (!chunks.TryGetChunk(coord, out Chunk chunk))
        {
            throw new InvalidOperationException($"目标 chunk 未驻留：{coord}。");
        }

        chunk.MarkWorkingDirty(
            CellAddressing.LocalCoord(wx),
            CellAddressing.LocalCoord(wy),
            EngineConstants.DirtyRectPadding);
    }

    private static void MarkKeepAliveIfCrossChunk(
        IChunkSource chunks,
        int wx,
        int wy,
        int targetSlot,
        SimulationDiagnostics diagnostics)
    {
        if (targetSlot == 4)
        {
            return;
        }

        ChunkCoord targetCoord = CellAddressing.WorldToChunk(wx, wy);
        if (!chunks.TryGetChunk(targetCoord, out Chunk targetChunk))
        {
            Debug.Assert(false, $"KeepAlive 目标 chunk 未驻留：{targetCoord}。");
            throw new InvalidOperationException($"KeepAlive 目标 chunk 未驻留：{targetCoord}。");
        }

        DirtyRect rect = DirtyRect.Empty.Union(
            CellAddressing.LocalCoord(wx),
            CellAddressing.LocalCoord(wy),
            EngineConstants.DirtyRectPadding);
        int incomingSlot = KeepAliveDirections.IncomingSlotForTouchedNeighborSlot(targetSlot);
        targetChunk.MarkIncomingDirty(incomingSlot, rect);
        diagnostics.RecordBoundaryWake(targetCoord, incomingSlot, rect);
    }

    private static void ValidateMoveCap(int sourceX, int sourceY, int targetX, int targetY)
    {
        if (Math.Abs(targetX - sourceX) > EngineConstants.MoveCap ||
            Math.Abs(targetY - sourceY) > EngineConstants.MoveCap)
        {
            throw new InvalidOperationException("movement 目标超出 32px halo。");
        }
    }
}
