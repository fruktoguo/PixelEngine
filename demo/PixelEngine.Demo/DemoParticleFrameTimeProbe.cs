using PixelEngine.Core.Diagnostics;
using PixelEngine.Hosting;
using PixelEngine.Rendering;

namespace PixelEngine.Demo;

/// <summary>
/// 高密度自由粒子帧时间探针，用于真实窗口 CPU stamp 与 GPU point-sprite 路径对比。
/// </summary>
internal sealed class DemoParticleFrameTimeProbe(
    EngineProbeApi probe,
    int requestedCount,
    int warmupFrames,
    string benchmarkRunId,
    int worldWidth,
    int worldHeight)
{
    private readonly EngineProbeApi _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    private readonly string _benchmarkRunId = string.IsNullOrWhiteSpace(benchmarkRunId)
        ? throw new ArgumentException("Benchmark run id must be non-empty.", nameof(benchmarkRunId))
        : benchmarkRunId;
    private readonly List<double> _wallMs = [];
    private readonly List<double> _stampMs = [];
    private readonly List<double> _gpuDrawMs = [];
    private readonly List<double> _uploadMs = [];
    private readonly List<double> _lightingMs = [];
    private readonly List<double> _bloomMs = [];
    private readonly List<double> _presentMs = [];
    private ushort _material;
    private int _framesSeen;
    private bool _initialized;

    public int LastSpawned { get; private set; }

    public int MeasuredFrames => _wallMs.Count;

    public void RegisterPhases(EnginePhasePipeline phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        phases.Register(EnginePhase.BuildRenderBuffer, FillParticles);
    }

    public void RecordFrame(double wallMilliseconds, ReadOnlySpan<double> subPhases)
    {
        _framesSeen++;
        if (_framesSeen <= warmupFrames)
        {
            return;
        }

        _wallMs.Add(wallMilliseconds);
        _stampMs.Add(Sub(subPhases, FrameSubPhase.ParticleStamp));
        _gpuDrawMs.Add(Sub(subPhases, FrameSubPhase.GpuParticleDraw));
        _uploadMs.Add(Sub(subPhases, FrameSubPhase.GpuUpload));
        _lightingMs.Add(Sub(subPhases, FrameSubPhase.Lighting));
        _bloomMs.Add(Sub(subPhases, FrameSubPhase.Bloom));
        _presentMs.Add(Sub(subPhases, FrameSubPhase.Present));
    }

    public string BuildSummary(ParticleRenderMode mode, bool gpuAvailable)
    {
        return
            $"particle_frame_probe source=PixelEngineParticleFrameProbe, benchmark_run_id={_benchmarkRunId}, " +
            $"mode={ModeName(mode)}, gpu_available={gpuAvailable}, " +
            $"requested_count={requestedCount}, active_count={LastSpawned}, " +
            $"warmup_frames={warmupFrames}, measured_frames={MeasuredFrames}, sample_seconds={SampleSeconds():0.###}, " +
            Stats("wall", _wallMs) + ", " +
            Stats("particle_stamp", _stampMs) + ", " +
            Stats("gpu_particle", _gpuDrawMs) + ", " +
            Stats("gpu_upload", _uploadMs) + ", " +
            Stats("lighting", _lightingMs) + ", " +
            Stats("bloom", _bloomMs) + ", " +
            Stats("present", _presentMs);
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

    private void FillParticles(EngineTickContext context)
    {
        _ = context;
        if (!_initialized)
        {
            _initialized = _probe.TryResolveMaterial("fire", out _material);
            if (!_initialized)
            {
                throw new InvalidOperationException("粒子帧时间探针需要 content/materials.json 中存在 fire 材质。");
            }

            _probe.EnsureParticleCapacity(requestedCount);
        }

        _probe.ClearParticles();
        int columns = Math.Max(1, worldWidth);
        int rows = Math.Max(1, worldHeight);
        int spawned = 0;
        for (int i = 0; i < requestedCount; i++)
        {
            float x = (i % columns) + 0.5f;
            float y = (i / columns % rows) + 0.5f;
            byte variant = (byte)((i * 37) & 0xFF);
            if (_probe.TrySpawnParticle(x, y, 0f, 0f, _material, variant, life: 240))
            {
                spawned++;
            }
        }

        LastSpawned = spawned;
    }

    private static double Sub(ReadOnlySpan<double> values, FrameSubPhase phase)
    {
        int index = (int)phase;
        return (uint)index < (uint)values.Length ? values[index] : 0.0;
    }

    private static string Stats(string name, List<double> samples)
    {
        if (samples.Count == 0)
        {
            return $"{name}_avg_ms=0.000, {name}_p50_ms=0.000, {name}_p95_ms=0.000, {name}_max_ms=0.000";
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
            $"{name}_max_ms={sorted[^1]:0.000}";
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        int index = (int)Math.Ceiling((sorted.Length * percentile) - 1);
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static string ModeName(ParticleRenderMode mode)
    {
        return mode switch
        {
            ParticleRenderMode.CpuStamp => "cpu",
            ParticleRenderMode.GpuPointSprite => "gpu",
            _ => mode.ToString(),
        };
    }
}
