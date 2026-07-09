using System.Numerics;
using PixelEngine.Core.Diagnostics;
using PixelEngine.Core.Events;
using PixelEngine.Core.Random;
using PixelEngine.Core.Time;
using Xunit;

namespace PixelEngine.Core.Tests;

/// <summary>
/// RNG、事件总线、时钟与诊断测试。
/// </summary>
public sealed class RuntimeInfrastructureTests
{
    /// <summary>
    /// 验证Counter Rng Is Deterministic And Avalanches Bits。
    /// </summary>
    [Fact]
    public void CounterRngIsDeterministicAndAvalanchesBits()
    {
        uint a = CounterRng.Hash(123, -7, 19, 3);
        uint b = CounterRng.Hash(123, -7, 19, 3);
        uint c = CounterRng.Hash(123, -7, 19, 4);

        Assert.Equal(a, b);
        Assert.InRange(BitOperations.PopCount(a ^ c), 8, 24);
    }

    /// <summary>
    /// 验证Rng Factory Creates Different Chunk Streams。
    /// </summary>
    [Fact]
    public void RngFactoryCreatesDifferentChunkStreams()
    {
        Pcg32 left = RngFactory.ForChunk(42, 0, 0, 10);
        Pcg32 right = RngFactory.ForChunk(42, 1, 0, 10);

        Assert.NotEqual(left.NextUInt(), right.NextUInt());
    }

    /// <summary>
    /// 验证Ring Buffer Reports Full And Drains In Order。
    /// </summary>
    [Fact]
    public void RingBufferReportsFullAndDrainsInOrder()
    {
        RingBuffer<int> buffer = new(2);

        Assert.True(buffer.TryEnqueue(1));
        Assert.True(buffer.TryEnqueue(2));
        Assert.False(buffer.TryEnqueue(3));

        Span<int> values = stackalloc int[2];
        Assert.Equal(2, buffer.DrainTo(values));
        Assert.Equal([1, 2], values.ToArray());
    }

    /// <summary>
    /// 验证MPSC 环形缓冲Buffer Accepts Concurrent Producers。
    /// </summary>
    [Fact]
    public async Task MpscRingBufferAcceptsConcurrentProducers()
    {
        MpscRingBuffer<int> buffer = new(1024);
        Task[] producers =
        [
            .. Enumerable.Range(0, 4)
            .Select(worker => Task.Run(() =>
            {
                for (int i = 0; i < 128; i++)
                {
                    int value = (worker * 128) + i;
                    Assert.True(buffer.TryEnqueue(value));
                }
            })),
        ];

        await Task.WhenAll(producers);

        int[] values = new int[512];
        Assert.Equal(512, buffer.DrainTo(values));
        Assert.Equal(512, values.Distinct().Count());
        Assert.All(Enumerable.Range(0, 512), value => Assert.Contains(value, values));
    }

    /// <summary>
    /// 验证Frame Clock Never Runs More Than One Sim Step Per Frame。
    /// </summary>
    [Fact]
    public void FrameClockNeverRunsMoreThanOneSimStepPerFrame()
    {
        FrameClock clock = new();

        FrameTiming slowFrame = clock.BeginFrame(1.0 / 30.0);
        Assert.True(slowFrame.RunSim);
        Assert.True(slowFrame.RunPhysics);
        Assert.Equal(1.0 / EngineConstants.DefaultSimHz, slowFrame.Dt);
        Assert.True(clock.TimeScale < 1.0);

        clock.SimHz = EngineConstants.SimHzDownscaled;
        FrameTiming first30 = clock.BeginFrame(1.0 / 60.0);
        FrameTiming second30 = clock.BeginFrame(1.0 / 60.0);

        Assert.False(first30.RunSim);
        Assert.True(second30.RunSim);
        Assert.Equal(2, clock.SimTickIndex);
    }

    /// <summary>
    /// 验证Frame Clock Render Only Frame不会Advance Sim Tick。
    /// </summary>
    [Fact]
    public void FrameClockRenderOnlyFrameDoesNotAdvanceSimTick()
    {
        FrameClock clock = new();

        FrameTiming renderOnly = clock.BeginRenderOnlyFrame(1.0 / 60.0);

        Assert.Equal(1, renderOnly.FrameIndex);
        Assert.Equal(0, renderOnly.SimTickIndex);
        Assert.False(renderOnly.RunSim);
        Assert.False(renderOnly.RunPhysics);
        Assert.False(clock.RunSimThisFrame);
        Assert.Equal(0, clock.SimTickIndex);
    }

    /// <summary>
    /// 验证Frame Clock Forced Sim Frame Runs One Sim Tick Even When Downscaled。
    /// </summary>
    [Fact]
    public void FrameClockForcedSimFrameRunsOneSimTickEvenWhenDownscaled()
    {
        FrameClock clock = new(EngineConstants.SimHzDownscaled);
        _ = clock.BeginRenderOnlyFrame(1.0 / 60.0);

        FrameTiming forced = clock.BeginForcedSimFrame(1.0 / 60.0);

        Assert.True(forced.RunSim);
        Assert.True(forced.RunPhysics);
        Assert.Equal(2, forced.FrameIndex);
        Assert.Equal(1, forced.SimTickIndex);
        Assert.Equal(1, clock.SimTickIndex);
    }

    /// <summary>
    /// 验证Frame Clock Restore Counters Sets Saved Frame State。
    /// </summary>
    [Fact]
    public void FrameClockRestoreCountersSetsSavedFrameState()
    {
        FrameClock clock = new();

        clock.RestoreCounters(frameIndex: 10, simTickIndex: 7);

        Assert.Equal(10, clock.FrameIndex);
        Assert.Equal(7, clock.SimTickIndex);
        Assert.False(clock.RunSimThisFrame);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => clock.RestoreCounters(1, 2));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => clock.RestoreCounters(-1, 0));
    }

    /// <summary>
    /// 验证Budget Monitor Tracks Sustained Over Budget Frames。
    /// </summary>
    [Fact]
    public void BudgetMonitorTracksSustainedOverBudgetFrames()
    {
        BudgetMonitor monitor = new(16.6, 3);

        monitor.Submit(20);
        monitor.Submit(20);
        Assert.False(monitor.IsSustainedOverBudget);

        monitor.Submit(20);
        Assert.True(monitor.IsSustainedOverBudget);

        monitor.Submit(10);
        Assert.False(monitor.IsSustainedOverBudget);
        Assert.Equal(0, monitor.ConsecutiveOverBudgetFrames);
    }

    /// <summary>
    /// 验证Frame Profiler Records All Main Phases。
    /// </summary>
    [Fact]
    public void FrameProfilerRecordsAllMainPhases()
    {
        FrameProfiler profiler = new();
        profiler.BeginFrame();

        foreach (FramePhase phase in Enum.GetValues<FramePhase>())
        {
            profiler.Record(phase, 1.0 + (int)phase);
        }

        using (profiler.Measure(FramePhase.InputAndTime))
        {
        }

        profiler.EndFrame();

        Assert.Equal(FrameStats.PhaseCount, profiler.LastFrame.Length);
        Assert.True(profiler.Average(FramePhase.PhysicsSync, 1) > 0);
    }

    /// <summary>
    /// 验证Frame Profiler Records All Sub Phases。
    /// </summary>
    [Fact]
    public void FrameProfilerRecordsAllSubPhases()
    {
        FrameProfiler profiler = new();
        profiler.BeginFrame();

        foreach (FrameSubPhase phase in Enum.GetValues<FrameSubPhase>())
        {
            profiler.RecordSub(phase, 0.1 + (int)phase);
        }

        profiler.EndFrame();

        Assert.Equal(FrameStats.SubPhaseCount, Enum.GetValues<FrameSubPhase>().Length);
        Assert.Equal(0.1 + (int)FrameSubPhase.GpuComputeBloom, profiler.LastSubFrame[(int)FrameSubPhase.GpuComputeBloom]);
        Assert.Equal(0.1 + (int)FrameSubPhase.GpuLightComposite, profiler.LastSubFrame[(int)FrameSubPhase.GpuLightComposite]);
        Assert.Equal(0.1 + (int)FrameSubPhase.PresentWait, profiler.LastSubFrame[(int)FrameSubPhase.PresentWait]);
        Assert.Equal(0.1 + (int)FrameSubPhase.GpuFrame, profiler.LastSubFrame[(int)FrameSubPhase.GpuFrame]);
        Assert.True(profiler.LastWallMilliseconds >= 0);
    }

    /// <summary>
    /// 验证关键编译期常量与架构值一致。
    /// </summary>
    [Fact]
    public void EngineConstantsMatchArchitectureValues()
    {
        Assert.Equal(64, EngineConstants.ChunkSize);
        Assert.Equal(6, EngineConstants.ChunkSizeLog2);
        Assert.Equal(4096, EngineConstants.ChunkArea);
        Assert.Equal(32, EngineConstants.MoveCap);
        Assert.Equal(32, EngineConstants.HaloSize);
        Assert.Equal(16, EngineConstants.PhysicsPixelsPerMeter);
        Assert.Equal(4, EngineConstants.TempFieldDownscale);
    }
}
