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
        };
        EditorRuntimeDiagnostics runtime = new(0.5, 4, "Sim30Hz", 7);

        PerformanceHudSample sample = PerformanceHudPanel.BuildSample(
            EditorPerformanceSnapshot.Create(counters, profiler, runtime));

        Assert.Equal(1.0, sample.ParticleMs, 3);
        Assert.Equal(1.0, sample.CaMs, 3);
        Assert.Equal(1.5, sample.HeatMs, 3);
        Assert.Equal(0.7, sample.PhysicsMs, 3);
        Assert.Equal(0.8, sample.ShapeRebuildMs, 3);
        Assert.Equal(2.0, sample.RenderMs, 3);
        Assert.Equal(0.9, sample.UploadMs, 3);
        Assert.Equal(0.25, sample.AudioMs, 3);
        Assert.Equal(11, sample.ActiveChunks);
        Assert.Equal(222, sample.ActiveCells);
        Assert.Equal(33, sample.FreeParticles);
        Assert.Equal(4, sample.RigidBodies);
        Assert.Equal(55, sample.ResidentChunks);
        Assert.Equal(6_291_456, sample.ResidentMemoryBytes);
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
}
