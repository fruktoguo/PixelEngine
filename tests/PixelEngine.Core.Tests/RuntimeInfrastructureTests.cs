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
    /// 验证 CounterRng 纯函数式输出与基础雪崩性。
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
    /// 验证 chunk RNG 工厂为不同 chunk 产生不同流。
    /// </summary>
    [Fact]
    public void RngFactoryCreatesDifferentChunkStreams()
    {
        Pcg32 left = RngFactory.ForChunk(42, 0, 0, 10);
        Pcg32 right = RngFactory.ForChunk(42, 1, 0, 10);

        Assert.NotEqual(left.NextUInt(), right.NextUInt());
    }

    /// <summary>
    /// 验证 SPSC ring buffer 满队列与 drain 语义。
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
    /// 验证 MPSC ring buffer 多生产者不丢失事件。
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
    /// 验证 FrameClock 不追帧且 30Hz 模式隔帧执行。
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
    /// 验证 BudgetMonitor 连续超预算后置位，回落后复位。
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
    /// 验证 FrameProfiler 覆盖全部主相位并能平均历史。
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

        using (profiler.Measure(FramePhase.FrameClock))
        {
        }

        profiler.EndFrame();

        Assert.Equal(FrameStats.PhaseCount, profiler.LastFrame.Length);
        Assert.True(profiler.Average(FramePhase.Physics, 1) > 0);
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
