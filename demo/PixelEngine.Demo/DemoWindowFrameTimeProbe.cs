using PixelEngine.Core.Diagnostics;

namespace PixelEngine.Demo;

/// <summary>
/// 有限真实窗口短跑的稳态帧时间采样器，用于性能 HUD 静态/动态样本对照。
/// </summary>
internal sealed class DemoWindowFrameTimeProbe(int warmupFrames, string scenario)
{
    private static readonly List<double> NoSamples = [];
    private readonly string _scenario = string.IsNullOrWhiteSpace(scenario)
        ? throw new ArgumentException("Scenario must be non-empty.", nameof(scenario))
        : scenario;
    private readonly List<double> _wallMs = [];
    private readonly List<double> _cpuWorkMs = [];
    private readonly List<double> _gpuFrameMs = [];
    private readonly List<double> _presentSubmitMs = [];
    private readonly List<double> _presentWaitMs = [];
    private readonly List<double> _effectiveFrameMs = [];
    private readonly List<double> _caMs = [];
    private readonly List<double> _physicsMs = [];
    private readonly List<double> _renderBufferMs = [];
    private readonly List<double> _uploadMs = [];
    private readonly List<double> _lightingMs = [];
    private readonly List<double> _bloomMs = [];
    private readonly List<double> _particleStampMs = [];
    private readonly List<double> _activeCells = [];
    private readonly List<double> _activeChunks = [];
    private readonly List<double> _freeParticles = [];
    private readonly List<double> _rigidBodies = [];
    private readonly List<double> _destructionEvents = [];
    private readonly List<double> _customMetric = [];
    private string _customMetricName = string.Empty;
    private readonly List<double> _simHz = [];
    private int _framesSeen;

    public int MeasuredFrames => _wallMs.Count;

    public void RecordFrame(double wallMilliseconds, ReadOnlySpan<double> subPhases, EngineCounters counters)
    {
        ArgumentNullException.ThrowIfNull(counters);
        _framesSeen++;
        if (_framesSeen <= warmupFrames)
        {
            return;
        }

        _wallMs.Add(wallMilliseconds);
        _cpuWorkMs.Add(counters.FrameCpuWorkMilliseconds);
        _gpuFrameMs.Add(counters.FrameGpuTimerAvailable ? counters.FrameGpuWorkMilliseconds : 0.0);
        _presentSubmitMs.Add(Sub(subPhases, FrameSubPhase.Present));
        _presentWaitMs.Add(counters.FramePresentWaitMilliseconds);
        _effectiveFrameMs.Add(counters.EffectiveFrameMilliseconds);
        _caMs.Add(Sub(subPhases, FrameSubPhase.CaPassA) +
            Sub(subPhases, FrameSubPhase.CaPassB) +
            Sub(subPhases, FrameSubPhase.CaPassC) +
            Sub(subPhases, FrameSubPhase.CaPassD));
        _physicsMs.Add(Sub(subPhases, FrameSubPhase.PhysicsStep) +
            Sub(subPhases, FrameSubPhase.PhysicsCcl) +
            Sub(subPhases, FrameSubPhase.PhysicsErase) +
            Sub(subPhases, FrameSubPhase.PhysicsInverseSample) +
            Sub(subPhases, FrameSubPhase.StaticCollider) +
            Sub(subPhases, FrameSubPhase.CharacterController) +
            Sub(subPhases, FrameSubPhase.ShapeRebuild));
        _renderBufferMs.Add(Sub(subPhases, FrameSubPhase.RenderBufferBuild));
        _uploadMs.Add(Sub(subPhases, FrameSubPhase.GpuUpload));
        _lightingMs.Add(Sub(subPhases, FrameSubPhase.Lighting));
        _bloomMs.Add(Sub(subPhases, FrameSubPhase.Bloom));
        _particleStampMs.Add(Sub(subPhases, FrameSubPhase.ParticleStamp));
        _activeCells.Add(counters.ActiveCells);
        _activeChunks.Add(counters.ActiveChunks);
        _freeParticles.Add(counters.FreeParticles);
        _rigidBodies.Add(counters.RigidBodies);
        _destructionEvents.Add(counters.CellDestructionEventsThisTick + counters.RigidBodiesDestroyedThisTick + counters.RigidBodiesCreatedThisTick);
        counters.CustomMetric.Read(out _customMetricName, out long customMetricValue);
        _customMetric.Add(customMetricValue);
        _simHz.Add(counters.SimHz);
    }

    public string BuildSummary(bool gpuTimerAvailable, bool vSyncEnabled)
    {
        return
            $"window_frame_probe source=PixelEngineWindowFrameProbe, scenario={_scenario}, " +
            $"gpu_timer_available={gpuTimerAvailable}, vsync={vSyncEnabled}, " +
            $"warmup_frames={warmupFrames}, measured_frames={MeasuredFrames}, sample_seconds={SampleSeconds():0.###}, " +
            LoadStats("active_cells", _activeCells) + ", " +
            LoadStats("active_chunks", _activeChunks) + ", " +
            LoadStats("free_particles", _freeParticles) + ", " +
            LoadStats("rigid_bodies", _rigidBodies) + ", " +
            LoadStats("destruction_events", _destructionEvents) + ", " +
            $"custom_metric_name={_customMetricName}, " +
            LoadStats("custom_metric", _customMetric) + ", " +
            LoadStats("sim_hz", _simHz) + ", " +
            Stats("wall", _wallMs) + ", " +
            Stats("cpu_work", _cpuWorkMs) + ", " +
            Stats("gpu_frame", gpuTimerAvailable ? _gpuFrameMs : NoSamples) + ", " +
            Stats("present_submit", _presentSubmitMs) + ", " +
            Stats("present_wait", _presentWaitMs) + ", " +
            Stats("effective_frame", _effectiveFrameMs) + ", " +
            Stats("ca", _caMs) + ", " +
            Stats("physics", _physicsMs) + ", " +
            Stats("render_buffer", _renderBufferMs) + ", " +
            Stats("gpu_upload", _uploadMs) + ", " +
            Stats("lighting", _lightingMs) + ", " +
            Stats("bloom", _bloomMs) + ", " +
            Stats("particle_stamp", _particleStampMs);
    }

    private double SampleSeconds()
    {
        double sum = 0;
        for (int i = 0; i < _wallMs.Count; i++)
        {
            sum += _wallMs[i];
        }

        return sum / 1000.0;
    }

    private static double Sub(ReadOnlySpan<double> values, FrameSubPhase phase)
    {
        int index = (int)phase;
        return (uint)index < (uint)values.Length ? values[index] : 0.0;
    }

    private static string LoadStats(string name, List<double> samples)
    {
        if (samples.Count == 0)
        {
            return $"{name}_avg=0.000, {name}_p50=0.000, {name}_p95=0.000, {name}_p99=0.000, {name}_max=0.000";
        }

        double[] sorted = [.. samples];
        Array.Sort(sorted);
        double sum = 0;
        for (int i = 0; i < sorted.Length; i++)
        {
            sum += sorted[i];
        }

        return
            $"{name}_avg={sum / sorted.Length:0.000}, " +
            $"{name}_p50={Percentile(sorted, 0.50):0.000}, " +
            $"{name}_p95={Percentile(sorted, 0.95):0.000}, " +
            $"{name}_p99={Percentile(sorted, 0.99):0.000}, " +
            $"{name}_max={sorted[^1]:0.000}";
    }

    private static string Stats(string name, List<double> samples)
    {
        if (samples.Count == 0)
        {
            return $"{name}_avg_ms=0.000, {name}_p50_ms=0.000, {name}_p95_ms=0.000, {name}_p99_ms=0.000, {name}_max_ms=0.000";
        }

        double[] sorted = [.. samples];
        Array.Sort(sorted);
        double sum = 0;
        for (int i = 0; i < sorted.Length; i++)
        {
            sum += sorted[i];
        }

        return
            $"{name}_avg_ms={sum / sorted.Length:0.000}, " +
            $"{name}_p50_ms={Percentile(sorted, 0.50):0.000}, " +
            $"{name}_p95_ms={Percentile(sorted, 0.95):0.000}, " +
            $"{name}_p99_ms={Percentile(sorted, 0.99):0.000}, " +
            $"{name}_max_ms={sorted[^1]:0.000}";
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        int index = (int)Math.Ceiling((sorted.Length * percentile) - 1);
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
