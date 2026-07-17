using BenchmarkDotNet.Attributes;
using PixelEngine.Core;
using PixelEngine.Core.Threading;
using PixelEngine.Simulation;

namespace PixelEngine.Benchmarks;

/// <summary>
/// 以多个独立同初态 kernel 测量 2.17M full-active cell 的单帧 CA 吞吐。
/// </summary>
/// <remarks>
/// 单个约 10ms 的 workload 易受 Windows 调度噪声影响；本基准在一次计时内依次运行
/// 16 个独立 full-dirty kernel，并通过 <see cref="BenchmarkAttribute.OperationsPerInvoke" />
/// 折算单帧。每个 kernel 只推进一帧，后续 dirty 收缩不能降低被测工作量。
/// </remarks>
[MemoryDiagnoser]
public class FullActiveCellThroughputBenchmark : IDisposable
{
    private const int FramesPerInvoke = 16;
    private const int ActiveChunksPerAxis = 23;
    private const ushort Water = 1;
    private const ushort Oil = 2;

    private readonly FullActiveFrame[] _frames = new FullActiveFrame[FramesPerInvoke];
    private JobSystem? _jobs;

    /// <summary>
    /// 每个独立 frame 实际扫描的 active cell 数。
    /// </summary>
    public int ActiveCellsPerFrame => ActiveChunksPerAxis * ActiveChunksPerAxis * EngineConstants.ChunkArea;

    /// <summary>
    /// 创建 16 份互不共享可变网格的 2.17M-cell 场景与持久 worker。
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        MaterialPropsTable materials = CreateMaterials();
        for (int i = 0; i < _frames.Length; i++)
        {
            _frames[i] = new FullActiveFrame(materials);
        }

        _jobs = new JobSystem(workerCount: Math.Min(8, Math.Max(1, Environment.ProcessorCount)));
    }

    /// <summary>
    /// 在计时外恢复每个 kernel 的网格、frame/parity 与 full dirty 初态。
    /// </summary>
    [IterationSetup]
    public void SetupIteration()
    {
        for (int i = 0; i < _frames.Length; i++)
        {
            _frames[i].Reset();
            if (_frames[i].CoveredCells != ActiveCellsPerFrame)
            {
                throw new InvalidOperationException(
                    $"独立 full-active frame {i} 仅覆盖 {_frames[i].CoveredCells} / {ActiveCellsPerFrame} cells。");
            }
        }
    }

    /// <summary>
    /// JobSystem 驱动 16 个独立 FullActive2M 初态各推进一帧。
    /// </summary>
    [Benchmark(OperationsPerInvoke = FramesPerInvoke)]
    public void StepJobSystemFullActive2MIndependentFrames()
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
            [CellType.Empty, CellType.Liquid, CellType.Liquid],
            [0, 120, 80],
            [0, 0, 0],
            [0, 0, 0],
            [0, 0, 0],
            [0, 0, 0]);
    }

    private sealed class FullActiveFrame
    {
        private readonly Chunk[] _chunks;
        private readonly BenchmarkChunkSource _source;

        public FullActiveFrame(MaterialPropsTable materials)
        {
            int residentChunksPerAxis = ActiveChunksPerAxis + 2;
            _chunks = new Chunk[residentChunksPerAxis * residentChunksPerAxis];
            int index = 0;
            for (int cy = -1; cy <= ActiveChunksPerAxis; cy++)
            {
                for (int cx = -1; cx <= ActiveChunksPerAxis; cx++)
                {
                    _chunks[index++] = new Chunk(new ChunkCoord(cx, cy));
                }
            }

            _source = new BenchmarkChunkSource(_chunks);
            Kernel = new SimulationKernel(_source, materials, worldSeed: 0xBEEFUL);
        }

        public SimulationKernel Kernel { get; }

        public int CoveredCells { get; private set; }

        public void Reset()
        {
            for (int i = 0; i < _chunks.Length; i++)
            {
                Chunk chunk = _chunks[i];
                chunk.Reset(chunk.Coord);
            }

            Kernel.RestoreFrameState(frameIndex: 0, currentParity: 0);
            CoveredCells = 0;
            for (int cy = 0; cy < ActiveChunksPerAxis; cy++)
            {
                for (int cx = 0; cx < ActiveChunksPerAxis; cx++)
                {
                    Chunk chunk = _source.GetRequired(new ChunkCoord(cx, cy));
                    for (int i = 0; i < chunk.MaterialBuffer.Length; i++)
                    {
                        chunk.MaterialBuffer[i] = (i & 1) == 0 ? Water : Oil;
                    }

                    chunk.SetCurrentDirty(DirtyRect.Full);
                    CoveredCells += EngineConstants.ChunkArea;
                }
            }
        }
    }
}
