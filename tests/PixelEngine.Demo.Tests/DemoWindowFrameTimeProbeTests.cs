using PixelEngine.Core.Diagnostics;
using Xunit;

namespace PixelEngine.Demo.Tests;

/// <summary>
/// 真实窗口帧时间探针输出格式测试。
/// 不变式：帧时间探针输出格式稳定、可被自动化解析。
/// </summary>
public sealed class DemoWindowFrameTimeProbeTests
{
    /// <summary>
    /// 验证窗口探针排除预热帧并输出性能 HUD 验收需要的分位数与负载字段。
    /// </summary>
    [Fact]
    public void BuildSummaryReportsWarmupExcludedPercentilesAndLoadCounters()
    {
        DemoWindowFrameTimeProbe probe = new(2, "static");
        double[] main = new double[FrameStats.PhaseCount];
        main[(int)FramePhase.GameLogic] = 0.7;
        main[(int)FramePhase.Temperature] = 0.8;
        main[(int)FramePhase.WorldStreaming] = 0.9;
        double[] sub = new double[FrameStats.SubPhaseCount];
        sub[(int)FrameSubPhase.CaPassA] = 0.1;
        sub[(int)FrameSubPhase.CaPassB] = 0.2;
        sub[(int)FrameSubPhase.PhysicsStep] = 0.3;
        sub[(int)FrameSubPhase.RenderBufferBuild] = 0.4;
        sub[(int)FrameSubPhase.GpuUpload] = 0.5;
        sub[(int)FrameSubPhase.Present] = 0.6;
        sub[(int)FrameSubPhase.AudioDispatch] = 1.1;

        for (int i = 1; i <= 6; i++)
        {
            EngineCounters counters = new()
            {
                FrameCpuWorkMilliseconds = i,
                FrameGpuTimerAvailable = true,
                FrameGpuWorkMilliseconds = i * 0.5,
                FramePresentWaitMilliseconds = i * 0.25,
                EffectiveFrameMilliseconds = i * 0.75,
                ActiveCells = i * 1000,
                ActiveChunks = i,
                FreeParticles = i * 2,
                RigidBodies = i % 2,
                CellDestructionEventsThisTick = i,
                RigidBodiesDestroyedThisTick = i % 3,
                RigidBodiesCreatedThisTick = i % 4,
                SimHz = 60 - i,
            };
            counters.SetCustomMetric("test_metric", i * 10000);
            probe.RecordFrame(i, main, sub, counters, threadAllocatedBytes: i * 128L);
        }

        string summary = probe.BuildSummary(gpuTimerAvailable: true, vSyncEnabled: false);

        Assert.Contains("window_frame_probe source=PixelEngineWindowFrameProbe", summary);
        Assert.Contains("scenario=static", summary);
        Assert.Contains("warmup_frames=2", summary);
        Assert.Contains("measured_frames=4", summary);
        Assert.Contains("wall_avg_ms=4.500", summary);
        Assert.Contains("wall_p50_ms=4.000", summary);
        Assert.Contains("wall_p95_ms=6.000", summary);
        Assert.Contains("wall_p99_ms=6.000", summary);
        Assert.Contains("cpu_work_avg_ms=4.500", summary);
        Assert.Contains("gpu_frame_avg_ms=2.250", summary);
        Assert.Contains("present_wait_avg_ms=1.125", summary);
        Assert.Contains("game_logic_avg_ms=0.700", summary);
        Assert.Contains("temperature_avg_ms=0.800", summary);
        Assert.Contains("world_streaming_avg_ms=0.900", summary);
        Assert.Contains("audio_dispatch_avg_ms=1.100", summary);
        Assert.Contains("logic_audio_avg_ms=1.800", summary);
        Assert.Contains("thread_allocated_bytes_avg=576.000", summary);
        Assert.Contains("runtime_gc_pause_observed=false", summary);
        Assert.Contains("active_cells_avg=4500.000", summary);
        Assert.Contains("active_cells_p50=4000.000", summary);
        Assert.Contains("active_cells_p95=6000.000", summary);
        Assert.Contains("active_cells_p99=6000.000", summary);
        Assert.Contains("active_cells_max=6000.000", summary);
        Assert.Contains("active_chunks_avg=4.500", summary);
        Assert.Contains("free_particles_avg=9.000", summary);
        Assert.Contains("destruction_events_avg=6.750", summary);
        Assert.Contains("destruction_events_p50=6.000", summary);
        Assert.Contains("destruction_events_p95=8.000", summary);
        Assert.Contains("destruction_events_p99=8.000", summary);
        Assert.Contains("destruction_events_max=8.000", summary);
        Assert.Contains("custom_metric_name=test_metric", summary);
        Assert.Contains("custom_metric_avg=45000.000", summary);
        Assert.Contains("custom_metric_p50=40000.000", summary);
        Assert.Contains("custom_metric_p95=60000.000", summary);
        Assert.Contains("custom_metric_p99=60000.000", summary);
        Assert.Contains("custom_metric_max=60000.000", summary);
        Assert.Contains("sim_hz_avg=55.500", summary);
        Assert.Contains("sim_hz_p50=55.000", summary);
        Assert.Contains("sim_hz_p95=57.000", summary);
        Assert.Contains("sim_hz_p99=57.000", summary);
        Assert.Contains("sim_hz_max=57.000", summary);
    }

    /// <summary>
    /// 验证 GPU timer 不可用时 GPU 分位数字段显式为 0，而不是伪造 GPU 执行时间。
    /// </summary>
    [Fact]
    public void BuildSummaryMarksGpuFrameStatsEmptyWhenTimerUnavailable()
    {
        DemoWindowFrameTimeProbe probe = new(0, "scripted_demo");
        double[] sub = new double[FrameStats.SubPhaseCount];
        EngineCounters counters = new()
        {
            FrameCpuWorkMilliseconds = 3,
            FrameGpuTimerAvailable = false,
            FrameGpuWorkMilliseconds = 10,
            FramePresentWaitMilliseconds = 1,
            EffectiveFrameMilliseconds = 4,
        };

        probe.RecordFrame(5, sub, counters);

        string summary = probe.BuildSummary(gpuTimerAvailable: false, vSyncEnabled: true);

        Assert.Contains("gpu_timer_available=False", summary);
        Assert.Contains("vsync=True", summary);
        Assert.Contains("gpu_frame_avg_ms=0.000", summary);
        Assert.Contains("gpu_frame_p99_ms=0.000", summary);
    }
}
