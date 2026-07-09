using System.Runtime.CompilerServices;
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
        in ChunkNeighborhood neighborhood,
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

        // 以 3x3 邻域窗口读写，跨界移动/反应恒落在 32px halo 内。
        NeighborWindow window = new(chunk.Coord, in neighborhood);
        Pcg32 rng = RngFactory.ForChunk(worldSeed, chunk.Coord.X, chunk.Coord.Y, frameIndex);
        int worldBaseX = chunk.Coord.X << EngineConstants.ChunkSizeLog2;
        int worldBaseY = chunk.Coord.Y << EngineConstants.ChunkSizeLog2;
        ref ushort materialBase = ref chunk.GetMaterialBase();
        ref byte flagsBase = ref chunk.GetFlagsBase();
        ref byte lifetimeBase = ref chunk.GetLifetimeBase();

        // bottom-up 扫描：粉液下落依赖下方 cell 已在本帧或上帧落定。
        for (int ly = rect.MaxY; ly >= rect.MinY; ly--)
        {
            int wy = worldBaseY + ly;
            int wx = worldBaseX + rect.MinX;
            int localOffset = (ly * EngineConstants.ChunkSize) + rect.MinX;
            for (int lx = rect.MinX; lx <= rect.MaxX; lx++)
            {
                ushort material = Unsafe.Add(ref materialBase, localOffset);
                if (material == 0)
                {
                    wx++;
                    localOffset++;
                    continue;
                }

                byte flags = Unsafe.Add(ref flagsBase, localOffset);
                if (CellFlags.MatchesFrame(flags, parityBit) || CellFlags.Has(flags, CellFlags.RigidOwned))
                {
                    wx++;
                    localOffset++;
                    continue;
                }

                // lifetime 先于位移：到期可能清空 cell，后续 movement 直接跳过。
                ref byte lifetime = ref Unsafe.Add(ref lifetimeBase, localOffset);
                if (lifetime != 0)
                {
                    ProcessLifetime(ref window, chunk, lifetimeSink, wx, wy, material, parityBit, ref lifetime);
                    material = Unsafe.Add(ref materialBase, localOffset);
                    if (material == 0)
                    {
                        wx++;
                        localOffset++;
                        continue;
                    }

                    flags = Unsafe.Add(ref flagsBase, localOffset);
                    if (CellFlags.MatchesFrame(flags, parityBit) || CellFlags.Has(flags, CellFlags.RigidOwned))
                    {
                        wx++;
                        localOffset++;
                        continue;
                    }
                }

                bool preferNegative = ((parityBit ^ (byte)(ly & 1)) & CellFlags.Parity) == 0;
                int activeX = wx;
                int activeY = wy;
                CellType materialType = materials.TypeOf(material);
                bool moved = materialType switch
                {
                    CellType.Empty => false,
                    CellType.Solid => false,
                    CellType.Powder => TryMovePowder(ref window, chunk, materials, rigidDamageSink, diagnostics, wx, wy, materials.DensityOf(material), parityBit, preferNegative, out activeX, out activeY),
                    CellType.Liquid => TryMoveLiquid(ref window, chunk, materials, rigidDamageSink, diagnostics, wx, wy, materials.DensityOf(material), materials.DispersionOf(material), parityBit, preferNegative, out activeX, out activeY),
                    CellType.Gas => TryMoveGas(ref window, chunk, materials, rigidDamageSink, diagnostics, wx, wy, materials.DensityOf(material), materials.DispersionOf(material), parityBit, ref rng, out activeX, out activeY),
                    CellType.Fire => false,
                    _ => throw new InvalidOperationException($"未知 CellType：{materialType}。"),
                };

                ushort activeMaterial = material;
                // 位移后的活跃坐标上尝试 von Neumann 反应，再跑材质自定义更新。
                bool reacted = TryReactVonNeumann(ref window, materials, reactionExecutor, rigidDamageSink, diagnostics, activeX, activeY, activeMaterial, parityBit, ref rng);
                bool customUpdated = !reacted && TryRunCustomUpdate(ref window, chunks, materials, customUpdateExecutor, activeX, activeY, activeMaterial, parityBit);
                if (!moved && !reacted && !customUpdated && materialType == CellType.Fire)
                {
                    window.SetFlags(wx, wy, CellFlags.SetParity(window.GetFlags(wx, wy), parityBit));
                }

                wx++;
                localOffset++;
            }
        }
    }

    private static bool TryMovePowder(
        ref NeighborWindow window,
        Chunk centerChunk,
        MaterialPropsTable materials,
        IRigidDamageSink rigidDamageSink,
        SimulationDiagnostics diagnostics,
        int wx,
        int wy,
        byte sourceDensity,
        byte parityBit,
        bool preferNegative,
        out int targetX,
        out int targetY)
    {
        int firstDx = preferNegative ? -1 : 1;
        return TryMoveDown(ref window, centerChunk, materials, rigidDamageSink, diagnostics, wx, wy, sourceDensity, parityBit, out targetX, out targetY) ||
            TryMoveTo(ref window, centerChunk, materials, rigidDamageSink, diagnostics, wx, wy, wx + firstDx, wy + 1, sourceDensity, parityBit, out targetX, out targetY) ||
            TryMoveTo(ref window, centerChunk, materials, rigidDamageSink, diagnostics, wx, wy, wx - firstDx, wy + 1, sourceDensity, parityBit, out targetX, out targetY);
    }

    private static bool TryMoveDown(
        ref NeighborWindow window,
        Chunk centerChunk,
        MaterialPropsTable materials,
        IRigidDamageSink rigidDamageSink,
        SimulationDiagnostics diagnostics,
        int wx,
        int wy,
        byte sourceDensity,
        byte parityBit,
        out int movedX,
        out int movedY)
    {
        movedX = wx;
        movedY = wy;
        int targetY = wy;
        // 垂直扫描受 MoveCap 约束，保证目标仍在 halo 内且可一次交换到位。
        for (int step = 1; step <= EngineConstants.MoveCap; step++)
        {
            int candidateY = wy + step;
            if (!window.TryReadNonEmptyMoveTarget(wx, candidateY, out ushort targetMaterial, out byte targetFlags))
            {
                targetY = candidateY;
                continue;
            }

            if (!CellFlags.MatchesFrame(targetFlags, parityBit) &&
                materials.DensityOf(targetMaterial) < sourceDensity)
            {
                targetY = candidateY;
            }

            break;
        }

        return targetY != wy &&
            TryMoveTo(ref window, centerChunk, materials, rigidDamageSink, diagnostics, wx, wy, wx, targetY, sourceDensity, parityBit, out movedX, out movedY);
    }

    private static bool TryMoveLiquid(
        ref NeighborWindow window,
        Chunk centerChunk,
        MaterialPropsTable materials,
        IRigidDamageSink rigidDamageSink,
        SimulationDiagnostics diagnostics,
        int wx,
        int wy,
        byte sourceDensity,
        byte dispersion,
        byte parityBit,
        bool preferNegative,
        out int targetX,
        out int targetY)
    {
        if (TryMovePowder(ref window, centerChunk, materials, rigidDamageSink, diagnostics, wx, wy, sourceDensity, parityBit, preferNegative, out targetX, out targetY))
        {
            return true;
        }

        int firstDir = preferNegative ? -1 : 1;
        return TryMoveHorizontal(ref window, centerChunk, materials, rigidDamageSink, diagnostics, wx, wy, sourceDensity, dispersion, parityBit, firstDir, out targetX, out targetY) ||
            TryMoveHorizontal(ref window, centerChunk, materials, rigidDamageSink, diagnostics, wx, wy, sourceDensity, dispersion, parityBit, -firstDir, out targetX, out targetY);
    }

    private static bool TryMoveGas(
        ref NeighborWindow window,
        Chunk centerChunk,
        MaterialPropsTable materials,
        IRigidDamageSink rigidDamageSink,
        SimulationDiagnostics diagnostics,
        int wx,
        int wy,
        byte sourceDensity,
        byte dispersion,
        byte parityBit,
        ref Pcg32 rng,
        out int targetX,
        out int targetY)
    {
        int firstDx = rng.NextInt(2) == 0 ? -1 : 1;
        return TryMoveTo(ref window, centerChunk, materials, rigidDamageSink, diagnostics, wx, wy, wx, wy - 1, sourceDensity, parityBit, out targetX, out targetY) ||
            TryMoveTo(ref window, centerChunk, materials, rigidDamageSink, diagnostics, wx, wy, wx + firstDx, wy - 1, sourceDensity, parityBit, out targetX, out targetY) ||
            TryMoveTo(ref window, centerChunk, materials, rigidDamageSink, diagnostics, wx, wy, wx - firstDx, wy - 1, sourceDensity, parityBit, out targetX, out targetY) ||
            TryMoveHorizontal(ref window, centerChunk, materials, rigidDamageSink, diagnostics, wx, wy, sourceDensity, dispersion, parityBit, firstDx, out targetX, out targetY) ||
            TryMoveHorizontal(ref window, centerChunk, materials, rigidDamageSink, diagnostics, wx, wy, sourceDensity, dispersion, parityBit, -firstDx, out targetX, out targetY);
    }

    private static bool TryMoveHorizontal(
        ref NeighborWindow window,
        Chunk centerChunk,
        MaterialPropsTable materials,
        IRigidDamageSink rigidDamageSink,
        SimulationDiagnostics diagnostics,
        int wx,
        int wy,
        byte sourceDensity,
        byte dispersion,
        byte parityBit,
        int direction,
        out int movedX,
        out int movedY)
    {
        movedX = wx;
        movedY = wy;
        int maxDistance = Math.Min((int)dispersion, EngineConstants.MoveCap);
        int targetX = wx;
        for (int step = 1; step <= maxDistance; step++)
        {
            int candidateX = wx + (direction * step);
            if (!window.CanDisplaceForMove(candidateX, wy, materials, sourceDensity, parityBit))
            {
                break;
            }

            targetX = candidateX;
        }

        return targetX != wx &&
            TryMoveTo(ref window, centerChunk, materials, rigidDamageSink, diagnostics, wx, wy, targetX, wy, sourceDensity, parityBit, out movedX, out movedY);
    }

    private static bool TryMoveTo(
        ref NeighborWindow window,
        Chunk centerChunk,
        MaterialPropsTable materials,
        IRigidDamageSink rigidDamageSink,
        SimulationDiagnostics diagnostics,
        int sourceX,
        int sourceY,
        int targetX,
        int targetY,
        byte sourceDensity,
        byte parityBit,
        out int movedX,
        out int movedY)
    {
        movedX = sourceX;
        movedY = sourceY;
        ValidateMoveCap(sourceX, sourceY, targetX, targetY);
        if (!window.TryMoveCell(
            sourceX,
            sourceY,
            targetX,
            targetY,
            materials,
            sourceDensity,
            parityBit,
            rigidDamageSink,
            out int targetSlot))
        {
            return false;
        }

        MarkCenterDirty(centerChunk, sourceX, sourceY);
        // 目标在本 chunk 内写 working dirty；跨界则对邻 chunk 发 KeepAlive 唤醒。
        if (targetSlot == 4)
        {
            MarkCenterDirty(centerChunk, targetX, targetY);
        }
        else
        {
            MarkKeepAliveIfCrossChunk(ref window, targetX, targetY, targetSlot, diagnostics);
        }
        movedX = targetX;
        movedY = targetY;
        return true;
    }

    private static void ProcessLifetime(
        ref NeighborWindow window,
        Chunk centerChunk,
        ILifetimeSink lifetimeSink,
        int wx,
        int wy,
        ushort material,
        byte parityBit,
        ref byte lifetime)
    {
        if (lifetime == 0)
        {
            return;
        }

        lifetime--;
        MarkCenterDirty(centerChunk, wx, wy);
        if (lifetime == 0)
        {
            lifetimeSink.OnExpired(ref window, wx, wy, material, parityBit);
        }
    }

    private static bool TryReactVonNeumann(
        ref NeighborWindow window,
        MaterialPropsTable materials,
        IReactionExecutor reactionExecutor,
        IRigidDamageSink rigidDamageSink,
        SimulationDiagnostics diagnostics,
        int wx,
        int wy,
        ushort material,
        byte parityBit,
        ref Pcg32 rng)
    {
        return material != 0 &&
            materials.ReactionCountOf(material) != 0 &&
            (TryReactNeighbor(ref window, reactionExecutor, rigidDamageSink, diagnostics, wx, wy, material, wx, wy - 1, parityBit, ref rng) ||
            TryReactNeighbor(ref window, reactionExecutor, rigidDamageSink, diagnostics, wx, wy, material, wx - 1, wy, parityBit, ref rng) ||
            TryReactNeighbor(ref window, reactionExecutor, rigidDamageSink, diagnostics, wx, wy, material, wx + 1, wy, parityBit, ref rng) ||
            TryReactNeighbor(ref window, reactionExecutor, rigidDamageSink, diagnostics, wx, wy, material, wx, wy + 1, parityBit, ref rng));
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
            (materials.PropertyFlagsOf(material) & MaterialProperty.HasCustomUpdate) != 0 &&
            customUpdateExecutor.TryUpdate(ref window, chunks, wx, wy, material, parityBit);
    }

    private static bool TryReactNeighbor(
        ref NeighborWindow window,
        IReactionExecutor reactionExecutor,
        IRigidDamageSink rigidDamageSink,
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

        byte sourceFlagsBefore = window.GetFlags(wx, wy);
        byte neighborFlagsBefore = window.GetFlags(neighborX, neighborY);
        diagnostics.RecordReactionAttempt();
        byte randomByte = (byte)(rng.NextUInt() >> 24);
        // 反应概率由 chunk 级确定性 RNG 驱动，保证可重演。
        bool reacted = reactionExecutor.TryReact(ref window, wx, wy, material, neighborX, neighborY, neighborMaterial, parityBit, randomByte);
        if (reacted)
        {
            NotifyRigidReactionDamage(ref window, rigidDamageSink, wx, wy, sourceFlagsBefore, material);
            NotifyRigidReactionDamage(ref window, rigidDamageSink, neighborX, neighborY, neighborFlagsBefore, neighborMaterial);
            MarkReactionTouchedCell(ref window, wx, wy, diagnostics);
            MarkReactionTouchedCell(ref window, neighborX, neighborY, diagnostics);
            diagnostics.RecordReactionSuccess(wx, wy, material, neighborX, neighborY, neighborMaterial);
        }

        return reacted;
    }

    private static void NotifyRigidReactionDamage(
        ref NeighborWindow window,
        IRigidDamageSink rigidDamageSink,
        int wx,
        int wy,
        byte flagsBefore,
        ushort materialBefore)
    {
        if (!CellFlags.Has(flagsBefore, CellFlags.RigidOwned))
        {
            return;
        }

        rigidDamageSink.OnOwnedCellDamaged(wx, wy, materialBefore);
        window.SetFlags(wx, wy, CellFlags.Clear(window.GetFlags(wx, wy), CellFlags.RigidOwned));
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
        int wx,
        int wy,
        SimulationDiagnostics diagnostics)
    {
        int slot = window.SlotOf(wx, wy);
        if (slot == 4)
        {
            MarkCenterDirty(window.GetChunk(slot), wx, wy);
        }
        else
        {
            MarkKeepAliveIfCrossChunk(ref window, wx, wy, slot, diagnostics);
        }
    }

    private static void MarkCenterDirty(Chunk centerChunk, int wx, int wy)
    {
        centerChunk.MarkWorkingDirty(
            CellAddressing.LocalCoord(wx),
            CellAddressing.LocalCoord(wy),
            EngineConstants.DirtyRectPadding);
    }

    private static void MarkKeepAliveIfCrossChunk(
        ref NeighborWindow window,
        int wx,
        int wy,
        int targetSlot,
        SimulationDiagnostics diagnostics)
    {
        if (targetSlot == 4)
        {
            return;
        }

        Chunk targetChunk = window.GetChunk(targetSlot);
        DirtyRect rect = DirtyRect.Empty.Union(
            CellAddressing.LocalCoord(wx),
            CellAddressing.LocalCoord(wy),
            EngineConstants.DirtyRectPadding);
        int incomingSlot = KeepAliveDirections.IncomingSlotForTouchedNeighborSlot(targetSlot);
        targetChunk.MarkIncomingDirty(incomingSlot, rect);
        diagnostics.RecordBoundaryWake(targetChunk.Coord, incomingSlot, rect);
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
