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
        IRigidDamageSink rigidDamageSink)
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

                bool preferNegative = ((parityBit ^ (byte)(ly & 1)) & CellFlags.Parity) == 0;
                bool moved = materials.TypeOf(material) switch
                {
                    CellType.Empty => false,
                    CellType.Solid => false,
                    CellType.Powder => TryMovePowder(ref window, chunks, materials, rigidDamageSink, wx, wy, material, parityBit, preferNegative),
                    CellType.Liquid => TryMoveLiquid(ref window, chunks, materials, rigidDamageSink, wx, wy, material, parityBit, preferNegative),
                    CellType.Gas => TryMoveGas(ref window, chunks, materials, rigidDamageSink, wx, wy, material, parityBit, ref rng),
                    CellType.Fire => false,
                    _ => throw new InvalidOperationException($"未知 CellType：{materials.TypeOf(material)}。"),
                };

                if (!moved && materials.TypeOf(material) != CellType.Solid)
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
        int wx,
        int wy,
        ushort material,
        byte parityBit,
        bool preferNegative)
    {
        int firstDx = preferNegative ? -1 : 1;
        return TryMoveTo(ref window, chunks, materials, rigidDamageSink, wx, wy, wx, wy + 1, material, parityBit) ||
            TryMoveTo(ref window, chunks, materials, rigidDamageSink, wx, wy, wx + firstDx, wy + 1, material, parityBit) ||
            TryMoveTo(ref window, chunks, materials, rigidDamageSink, wx, wy, wx - firstDx, wy + 1, material, parityBit);
    }

    private static bool TryMoveLiquid(
        ref NeighborWindow window,
        IChunkSource chunks,
        MaterialPropsTable materials,
        IRigidDamageSink rigidDamageSink,
        int wx,
        int wy,
        ushort material,
        byte parityBit,
        bool preferNegative)
    {
        if (TryMovePowder(ref window, chunks, materials, rigidDamageSink, wx, wy, material, parityBit, preferNegative))
        {
            return true;
        }

        int firstDir = preferNegative ? -1 : 1;
        return TryMoveHorizontal(ref window, chunks, materials, rigidDamageSink, wx, wy, material, parityBit, firstDir) ||
            TryMoveHorizontal(ref window, chunks, materials, rigidDamageSink, wx, wy, material, parityBit, -firstDir);
    }

    private static bool TryMoveGas(
        ref NeighborWindow window,
        IChunkSource chunks,
        MaterialPropsTable materials,
        IRigidDamageSink rigidDamageSink,
        int wx,
        int wy,
        ushort material,
        byte parityBit,
        ref Pcg32 rng)
    {
        int firstDx = rng.NextInt(2) == 0 ? -1 : 1;
        return TryMoveTo(ref window, chunks, materials, rigidDamageSink, wx, wy, wx, wy - 1, material, parityBit) ||
            TryMoveTo(ref window, chunks, materials, rigidDamageSink, wx, wy, wx + firstDx, wy - 1, material, parityBit) ||
            TryMoveTo(ref window, chunks, materials, rigidDamageSink, wx, wy, wx - firstDx, wy - 1, material, parityBit) ||
            TryMoveHorizontal(ref window, chunks, materials, rigidDamageSink, wx, wy, material, parityBit, firstDx) ||
            TryMoveHorizontal(ref window, chunks, materials, rigidDamageSink, wx, wy, material, parityBit, -firstDx);
    }

    private static bool TryMoveHorizontal(
        ref NeighborWindow window,
        IChunkSource chunks,
        MaterialPropsTable materials,
        IRigidDamageSink rigidDamageSink,
        int wx,
        int wy,
        ushort material,
        byte parityBit,
        int direction)
    {
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
            TryMoveTo(ref window, chunks, materials, rigidDamageSink, wx, wy, targetX, wy, material, parityBit);
    }

    private static bool TryMoveTo(
        ref NeighborWindow window,
        IChunkSource chunks,
        MaterialPropsTable materials,
        IRigidDamageSink rigidDamageSink,
        int sourceX,
        int sourceY,
        int targetX,
        int targetY,
        ushort sourceMaterial,
        byte parityBit)
    {
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
        MarkDirty(chunks, targetX, targetY);
        MarkKeepAliveIfCrossChunk(chunks, targetX, targetY, targetSlot);
        return true;
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

    private static void MarkKeepAliveIfCrossChunk(IChunkSource chunks, int wx, int wy, int targetSlot)
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
        targetChunk.MarkIncomingDirty(KeepAliveDirections.IncomingSlotForTouchedNeighborSlot(targetSlot), rect);
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
