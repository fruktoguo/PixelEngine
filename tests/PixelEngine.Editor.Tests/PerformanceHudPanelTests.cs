using PixelEngine.Core.Diagnostics;
using Xunit;

namespace PixelEngine.Editor.Tests;

/// <summary>
/// 性能 HUD 聚合与只读上下文测试。
/// </summary>
public sealed class PerformanceHudPanelTests
{
    /// <summary>
    /// 验证 HUD 从 profiler/counters/runtime 快照聚合 plan/12 §3.7 要求的数据。
    /// </summary>
    [Fact]
    public void BuildSampleAggregatesProfilerCountersAndRuntimeDiagnostics()
    {
        FrameProfiler profiler = new();
        profiler.BeginFrame();
        profiler.Record(FramePhase.ParticleToCell, 0.4);
        profiler.Record(FramePhase.CellToParticle, 0.6);
        profiler.Record(FramePhase.Temperature, 1.5);
        profiler.Record(FramePhase.BuildRenderBuffer, 2.0);
        profiler.RecordSub(FrameSubPhase.CaPassA, 0.1);
        profiler.RecordSub(FrameSubPhase.CaPassB, 0.2);
        profiler.RecordSub(FrameSubPhase.CaPassC, 0.3);
        profiler.RecordSub(FrameSubPhase.CaPassD, 0.4);
        profiler.RecordSub(FrameSubPhase.PhysicsStep, 0.7);
        profiler.RecordSub(FrameSubPhase.ShapeRebuild, 0.8);
        profiler.RecordSub(FrameSubPhase.GpuUpload, 0.9);
        profiler.RecordSub(FrameSubPhase.Lighting, 1.1);
        profiler.RecordSub(FrameSubPhase.PostProcess, 1.2);
        profiler.RecordSub(FrameSubPhase.GpuLightComposite, 8.0);
        profiler.RecordSub(FrameSubPhase.Present, 0.3);
        profiler.RecordSub(FrameSubPhase.PresentWait, 16.0);
        profiler.RecordSub(FrameSubPhase.GpuFrame, 2.5);
        profiler.RecordSub(FrameSubPhase.AudioDispatch, 0.25);
        profiler.EndFrame();
        EngineCounters counters = new()
        {
            ActiveChunks = 11,
            ActiveCells = 222,
            FreeParticles = 33,
            RigidBodies = 4,
            ResidentChunks = 55,
            ResidentMemoryBytes = 6_291_456,
            SimHz = 30,
            FrameCpuWorkMilliseconds = 9.7,
            FrameGpuWorkMilliseconds = 2.5,
            FrameGpuTimerAvailable = true,
            FramePresentSubmitMilliseconds = 0.3,
            FramePresentWaitMilliseconds = 16.0,
            FrameWaitMilliseconds = 16.0,
            EffectiveFrameMilliseconds = 9.7,
            EffectiveFramesPerSecond = 103.09,
            VSyncEnabled = true,
        };
        EditorRuntimeDiagnostics runtime = new(0.5, 4, "Sim30Hz", 7);

        PerformanceHudSample sample = PerformanceHudPanel.BuildSample(
            EditorPerformanceSnapshot.Create(counters, profiler, runtime));

        Assert.Equal(1.0, sample.ParticleMs, 3);
        Assert.Equal(0.1, sample.CaPassAMs, 3);
        Assert.Equal(0.2, sample.CaPassBMs, 3);
        Assert.Equal(0.3, sample.CaPassCMs, 3);
        Assert.Equal(0.4, sample.CaPassDMs, 3);
        Assert.Equal(1.0, sample.CaMs, 3);
        Assert.Equal(1.5, sample.HeatMs, 3);
        Assert.Equal(0.7, sample.PhysicsMs, 3);
        Assert.Equal(0.8, sample.ShapeRebuildMs, 3);
        Assert.Equal(4.6, sample.RenderMs, 3);
        Assert.Equal(0.9, sample.UploadMs, 3);
        Assert.Equal(0.25, sample.AudioMs, 3);
        Assert.Equal(9.7, sample.CpuWorkMs, 3);
        Assert.Equal(2.5, sample.GpuWorkMs, 3);
        Assert.True(sample.GpuTimerAvailable);
        Assert.Equal(0.3, sample.PresentSubmitMs, 3);
        Assert.Equal(16.0, sample.PresentWaitMs, 3);
        Assert.Equal(16.0, sample.WaitMs, 3);
        Assert.Equal(9.7, sample.EffectiveFrameMs, 3);
        Assert.Equal(103.09, sample.EffectiveFps, 2);
        Assert.True(sample.VSyncEnabled);
        Assert.Equal("vsync-bound", sample.BoundType);
        Assert.Equal(11, sample.ActiveChunks);
        Assert.Equal(222, sample.ActiveCells);
        Assert.Equal(33, sample.FreeParticles);
        Assert.Equal(4, sample.RigidBodies);
        Assert.Equal(55, sample.ResidentChunks);
        Assert.Equal(6_291_456, sample.ResidentMemoryBytes);
        Assert.Equal(7.9, sample.VariableWorkMs, 3);
        Assert.Equal(1.5, sample.FixedOverheadMs, 3);
        Assert.Equal(30, sample.SimHz);
        Assert.True(sample.IsTimeDilated);
        Assert.Equal(4, sample.DegradationLevel);
        Assert.Equal("Sim30Hz", sample.DegradationName);
        Assert.Equal(7, sample.ConsecutiveOverBudgetFrames);
    }

    /// <summary>
    /// 验证 HUD 只读采样不会写入计数器，且同一帧重复采样不会重复推进历史。
    /// </summary>
    [Fact]
    public void CaptureSampleReadsContextWithoutMutatingCounters()
    {
        EngineCounters counters = new()
        {
            ActiveChunks = 3,
            ActiveCells = 9,
            SimHz = 60,
        };
        PerformanceHudPanel panel = new();
        EditorContext context = new(
            counters,
            new EditorSelection(),
            12,
            EditorPerformanceSnapshot.Create(counters, null, EditorRuntimeDiagnostics.FullQuality));

        PerformanceHudSample first = panel.CaptureSample(in context);
        PerformanceHudSample second = panel.CaptureSample(in context);

        Assert.Equal(3, first.ActiveChunks);
        Assert.Equal(9, second.ActiveCells);
        Assert.Equal(60, panel.LastSample.SimHz);
        Assert.Equal(3, counters.ActiveChunks);
        Assert.Equal(9, counters.ActiveCells);
    }

    /// <summary>
    /// 验证 HUD 滚动统计排除启动预热帧，并计算 avg/p50/p95/p99/max。
    /// </summary>
    [Fact]
    public void CaptureSamplePublishesWarmupExcludedRollingPercentiles()
    {
        PerformanceHudPanel panel = new();
        EditorSelection selection = new();

        for (int frame = 1; frame <= 180; frame++)
        {
            EngineCounters counters = new()
            {
                RenderFrameLastMilliseconds = frame,
                FrameCpuWorkMilliseconds = frame * 0.5,
                FrameGpuWorkMilliseconds = frame * 0.25,
                FrameGpuTimerAvailable = true,
                FrameWaitMilliseconds = frame * 0.1,
                EffectiveFrameMilliseconds = frame * 0.9,
            };
            EditorContext context = new(
                counters,
                selection,
                frame,
                EditorPerformanceSnapshot.Create(counters, null, EditorRuntimeDiagnostics.FullQuality));

            _ = panel.CaptureSample(in context);
        }

        Assert.Equal(120, panel.FrameStatistics.SampleCount);
        Assert.True(panel.FrameStatistics.IsSteady);
        Assert.Equal(120.5, panel.FrameStatistics.AverageMs, 3);
        Assert.Equal(120, panel.FrameStatistics.P50Ms, 3);
        Assert.Equal(174, panel.FrameStatistics.P95Ms, 3);
        Assert.Equal(179, panel.FrameStatistics.P99Ms, 3);
        Assert.Equal(180, panel.FrameStatistics.MaxMs, 3);
        Assert.Equal(60.25, panel.CpuStatistics.AverageMs, 3);
        Assert.Equal(30.125, panel.GpuStatistics.AverageMs, 3);
        Assert.Equal(12.05, panel.WaitStatistics.AverageMs, 3);
        Assert.Equal(108.45, panel.EffectiveStatistics.AverageMs, 3);
    }

    /// <summary>
    /// 验证单帧尖刺会被标注，且同一帧重复采样不污染统计窗口。
    /// </summary>
    [Fact]
    public void CaptureSampleMarksSpikeWithoutDuplicatingSameFrame()
    {
        PerformanceHudPanel panel = new();
        EditorSelection selection = new();

        for (int frame = 1; frame <= 100; frame++)
        {
            CaptureFrame(panel, selection, frame, 16.0);
        }

        CaptureFrame(panel, selection, 101, 30.0);
        PerformanceHudStatistics spikeStats = panel.FrameStatistics;
        CaptureFrame(panel, selection, 101, 30.0);

        Assert.True(spikeStats.IsSpike);
        Assert.True(panel.FrameStatistics.IsSpike);
        Assert.Equal(41, panel.FrameStatistics.SampleCount);
        Assert.Equal(16.0, panel.FrameStatistics.P95Ms, 3);
        Assert.Equal(30.0, panel.FrameStatistics.MaxMs, 3);
    }

    /// <summary>
    /// 验证 HUD 不会在 GPU timer 不可用时发布误导性的 GPU 百分位。
    /// </summary>
    [Fact]
    public void CaptureSampleLeavesGpuStatisticsEmptyWhenTimerUnavailable()
    {
        PerformanceHudPanel panel = new();
        EditorSelection selection = new();

        for (int frame = 1; frame <= 180; frame++)
        {
            EngineCounters counters = new()
            {
                RenderFrameLastMilliseconds = 16,
                FrameCpuWorkMilliseconds = 5,
                FrameGpuWorkMilliseconds = 12,
                FrameGpuTimerAvailable = false,
                FrameWaitMilliseconds = 1,
                EffectiveFrameMilliseconds = 15,
            };
            EditorContext context = new(
                counters,
                selection,
                frame,
                EditorPerformanceSnapshot.Create(counters, null, EditorRuntimeDiagnostics.FullQuality));
            _ = panel.CaptureSample(in context);
        }

        Assert.Equal(120, panel.FrameStatistics.SampleCount);
        Assert.Equal(0, panel.GpuStatistics.SampleCount);
        Assert.Equal(0, panel.GpuStatistics.P99Ms);
    }

    /// <summary>
    /// 验证动态/固定成本统计在预热后跟随负载结构，而不是只看整帧墙钟。
    /// </summary>
    [Fact]
    public void CaptureSampleTracksVariableAndFixedCostStatistics()
    {
        PerformanceHudPanel panel = new();
        EditorSelection selection = new();

        for (int frame = 1; frame <= 180; frame++)
        {
            EngineCounters counters = new()
            {
                RenderFrameLastMilliseconds = 12,
                FrameCpuWorkMilliseconds = 10,
                FrameWaitMilliseconds = 2,
                ActiveChunks = 20,
                ActiveCells = 120_000,
                FreeParticles = 300,
                RigidBodies = 4,
            };
            FrameProfiler profiler = new();
            profiler.BeginFrame();
            profiler.RecordSub(FrameSubPhase.CaPassA, 1);
            profiler.RecordSub(FrameSubPhase.CaPassB, 1);
            profiler.RecordSub(FrameSubPhase.CaPassC, 1);
            profiler.RecordSub(FrameSubPhase.CaPassD, 1);
            profiler.RecordSub(FrameSubPhase.PhysicsStep, 1);
            profiler.RecordSub(FrameSubPhase.RenderBufferBuild, 1);
            profiler.RecordSub(FrameSubPhase.GpuUpload, 1);
            profiler.RecordSub(FrameSubPhase.Present, 1);
            profiler.EndFrame();
            EditorContext context = new(
                counters,
                selection,
                frame,
                EditorPerformanceSnapshot.Create(counters, profiler, EditorRuntimeDiagnostics.FullQuality));
            _ = panel.CaptureSample(in context);
        }

        Assert.Equal(120, panel.VariableWorkStatistics.SampleCount);
        Assert.Equal(7.0, panel.VariableWorkStatistics.AverageMs, 3);
        Assert.Equal(2.0, panel.FixedOverheadStatistics.AverageMs, 3);
        Assert.Equal(2.0, panel.WaitStatistics.AverageMs, 3);
        Assert.Equal(120_000, panel.LastSample.ActiveCells);
    }

    /// <summary>
    /// 验证超过 512 帧后百分位只基于最近窗口，旧样本被淘汰。
    /// </summary>
    [Fact]
    public void CaptureSamplePercentilesUseRecentRingWindowAfterWraparound()
    {
        PerformanceHudPanel panel = new();
        EditorSelection selection = new();

        for (int frame = 1; frame <= 700; frame++)
        {
            double frameMs = frame <= 188 ? 1.0 : 20.0;
            CaptureFrame(panel, selection, frame, frameMs);
        }

        Assert.Equal(512, panel.FrameStatistics.SampleCount);
        Assert.Equal(20.0, panel.FrameStatistics.AverageMs, 3);
        Assert.Equal(20.0, panel.FrameStatistics.P50Ms, 3);
        Assert.Equal(20.0, panel.FrameStatistics.P99Ms, 3);
        Assert.Equal(20.0, panel.FrameStatistics.MaxMs, 3);
    }

    private static void CaptureFrame(PerformanceHudPanel panel, EditorSelection selection, int frame, double frameMs)
    {
        EngineCounters counters = new()
        {
            RenderFrameLastMilliseconds = frameMs,
            FrameCpuWorkMilliseconds = frameMs * 0.5,
            FrameWaitMilliseconds = frameMs * 0.25,
            EffectiveFrameMilliseconds = frameMs * 0.75,
        };
        EditorContext context = new(
            counters,
            selection,
            frame,
            EditorPerformanceSnapshot.Create(counters, null, EditorRuntimeDiagnostics.FullQuality));
        _ = panel.CaptureSample(in context);
    }
}
