using BenchmarkDotNet.Attributes;
using PixelEngine.Core.Threading;
using PixelEngine.Simulation;

namespace PixelEngine.Benchmarks;

/// <summary>
/// 以多个独立同初态 kernel 测量单 chunk 16x16 typical dirty cell 的单帧 CA 延迟。
/// </summary>
/// <remarks>
/// 单帧约 0.03ms，直接测量会触发 BenchmarkDotNet MinIterationTime；本基准在一次计时内
/// 依次运行 8192 个独立 kernel，并通过 <see cref="BenchmarkAttribute.OperationsPerInvoke" />
/// 折算单帧。每个 kernel 只推进一帧，后续 dirty 收缩不会污染 measured workload。
/// </remarks>
[MemoryDiagnoser]
public class TypicalDirtyCellThroughputBenchmark : IDisposable
{
    private const int FramesPerInvoke = 8192;
    private const int DirtyMin = 24;
    private const int DirtyMaxExclusive = 40;
    private const int ActiveCellsPerFrame = (DirtyMaxExclusive - DirtyMin) * (DirtyMaxExclusive - DirtyMin);
    private const ushort Water = 1;
    private const ushort Sand = 2;

    private readonly TypicalDirtyFrame[] _frames = new TypicalDirtyFrame[FramesPerInvoke];
    private Chunk[] _sharedGuards = [];
    private JobSystem? _jobs;

    /// <summary>
    /// 创建 8192 份互不共享可变网格的 16x16 dirty 场景与持久 worker。
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        MaterialPropsTable materials = CreateMaterials();
        _sharedGuards = CreateNeighborhood();
        for (int i = 0; i < _frames.Length; i++)
        {
            _frames[i] = new TypicalDirtyFrame(materials, _sharedGuards);
        }

        _jobs = new JobSystem(workerCount: Math.Min(8, Math.Max(1, Environment.ProcessorCount)));
    }

    /// <summary>
    /// 在计时外恢复每个 kernel 的网格、frame/parity 与 16x16 dirty 初态。
    /// </summary>
    [IterationSetup]
    public void SetupIteration()
    {
        ValidateSharedGuards();
        for (int i = 0; i < _frames.Length; i++)
        {
            _frames[i].Reset();
            if (_frames[i].CoveredCells != ActiveCellsPerFrame)
            {
                throw new InvalidOperationException(
                    $"独立 typical dirty frame {i} 仅覆盖 {_frames[i].CoveredCells} / {ActiveCellsPerFrame} cells。");
            }
        }
    }

    /// <summary>
    /// JobSystem 驱动 8192 个独立 16x16 dirty 初态各推进一帧。
    /// </summary>
    [Benchmark(OperationsPerInvoke = FramesPerInvoke)]
    public void StepJobSystemTypicalDirtyIndependentFrames()
    {
        JobSystem jobs = _jobs ?? throw new InvalidOperationException("benchmark jobs 尚未初始化。");
        for (int i = 0; i < _frames.Length; i++)
        {
            SimulationKernel kernel = _frames[i].Kernel;
            kernel.StepCa(jobs);
            kernel.SwapDirtyRects();
        }
    }

    /// <summary>
    /// 每轮结束后确认共享 guard 仍是只读空块，包括最后一次 measured iteration。
    /// </summary>
    [IterationCleanup]
    public void CleanupIteration()
    {
        ValidateSharedGuards();
    }

    /// <summary>
    /// 释放持久 worker。
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _jobs?.Dispose();
        _jobs = null;
    }

    private static MaterialPropsTable CreateMaterials()
    {
        return new MaterialPropsTable(
            [CellType.Empty, CellType.Liquid, CellType.Powder],
            [0, 120, 180],
            [0, 0, 0],
            [0, 0, 0],
            [0, 0, 0],
            [0, 0, 0]);
    }

    private static Chunk[] CreateNeighborhood()
    {
        Chunk[] chunks = new Chunk[9];
        int index = 0;
        for (int cy = -1; cy <= 1; cy++)
        {
            for (int cx = -1; cx <= 1; cx++)
            {
                chunks[index++] = new Chunk(new ChunkCoord(cx, cy));
            }
        }

        return chunks;
    }

    private void ValidateSharedGuards()
    {
        for (int i = 0; i < _sharedGuards.Length; i++)
        {
            if (i is 4 or 7)
            {
                continue;
            }

            Chunk chunk = _sharedGuards[i];
            if (!chunk.CurrentDirty.IsEmpty || !chunk.WorkingDirty.IsEmpty ||
                chunk.State != ChunkState.Sleeping || chunk.Parity != 0)
            {
                throw new InvalidOperationException($"共享 guard chunk {chunk.Coord} 的调度状态被写入。");
            }

            for (int slot = 0; slot < chunk.IncomingDirtySlotCount; slot++)
            {
                if (!chunk.GetIncomingDirty(slot).IsEmpty)
                {
                    throw new InvalidOperationException($"共享 guard chunk {chunk.Coord} 的 incoming dirty 被写入。");
                }
            }

            for (int cell = 0; cell < chunk.Material.Length; cell++)
            {
                if (chunk.Material[cell] != 0 || chunk.Flags[cell] != 0 ||
                    chunk.Lifetime[cell] != 0 || chunk.Damage[cell] != 0)
                {
                    throw new InvalidOperationException($"共享 guard chunk {chunk.Coord} 的 SoA cell {cell} 被写入。");
                }
            }
        }
    }

    private sealed class TypicalDirtyFrame
    {
        private readonly Chunk _center;
        private readonly Chunk _south;

        public TypicalDirtyFrame(MaterialPropsTable materials, Chunk[] sharedGuards)
        {
            _center = new Chunk(new ChunkCoord(0, 0));
            _south = new Chunk(new ChunkCoord(0, 1));
            Chunk[] chunks = [.. sharedGuards];
            chunks[4] = _center;
            chunks[7] = _south;
            BenchmarkChunkSource source = new(chunks);
            Kernel = new SimulationKernel(source, materials, worldSeed: 0xBEEFUL);
        }

        public SimulationKernel Kernel { get; }

        public int CoveredCells { get; private set; }

        public void Reset()
        {
            _center.Reset(_center.Coord);
            _south.Reset(_south.Coord);

            Kernel.RestoreFrameState(frameIndex: 0, currentParity: 0);
            CoveredCells = 0;
            for (int y = DirtyMin; y < DirtyMaxExclusive; y++)
            {
                for (int x = DirtyMin; x < DirtyMaxExclusive; x++)
                {
                    _center.MaterialBuffer[CellAddressing.LocalIndexFromLocal(x, y)] = ((x + y) & 1) == 0 ? Sand : Water;
                    CoveredCells++;
                }
            }

            _center.SetCurrentDirty(new DirtyRect(DirtyMin, DirtyMin, DirtyMaxExclusive - 1, DirtyMaxExclusive - 1));
        }
    }
}
