using Hexa.NET.ImGui;
using Hexa.NET.ImPlot;
using PixelEngine.Core.Diagnostics;
using System.Numerics;

namespace PixelEngine.Editor;

/// <summary>
/// 架构 §17.1 性能 HUD，只读展示 plan/02 诊断与计时快照。
/// </summary>
public sealed class PerformanceHudPanel : IEditorPanel
{
    private const int HistoryLength = 512;
    private const int WarmupFrames = 60;
    private const int SteadyMinimumFrames = 120;
    private const int PhaseBarCount = 11;
    private static readonly string[] PhaseBarLabels =
    [
        "particle",
        "CA A",
        "CA B",
        "CA C",
        "CA D",
        "heat",
        "physics",
        "shape",
        "render",
        "upload",
        "audio",
    ];

    private readonly float[] _frameHistory = new float[HistoryLength];
    private readonly float[] _caHistory = new float[HistoryLength];
    private readonly float[] _physicsHistory = new float[HistoryLength];
    private readonly float[] _renderHistory = new float[HistoryLength];
    private readonly float[] _audioHistory = new float[HistoryLength];
    private readonly float[] _cpuHistory = new float[HistoryLength];
    private readonly float[] _gpuHistory = new float[HistoryLength];
    private readonly float[] _waitHistory = new float[HistoryLength];
    private readonly float[] _effectiveHistory = new float[HistoryLength];
    private readonly float[] _variableHistory = new float[HistoryLength];
    private readonly float[] _fixedHistory = new float[HistoryLength];
    private readonly float[] _activeChunksHistory = new float[HistoryLength];
    private readonly float[] _activeCellsKHistory = new float[HistoryLength];
    private readonly float[] _freeParticlesHistory = new float[HistoryLength];
    private readonly float[] _rigidBodiesHistory = new float[HistoryLength];
    private readonly float[] _phaseBars = new float[PhaseBarCount];
    private readonly float[] _statsScratch = new float[HistoryLength];
    private PixelEngine.Rendering.IRenderPresentationControl? _presentationControl;
    private long _lastCapturedFrame = -1;
    private int _historyOffset;
    private int _historyCount;
    private int _capturedSampleCount;

    /// <inheritdoc />
    public string Title => EditorDockSpace.PerformanceHudWindowTitle;

    /// <inheritdoc />
    public bool Visible { get; set; } = true;

    /// <summary>
    /// 最近一次采样结果，供测试和诊断读取。
    /// </summary>
    public PerformanceHudSample LastSample { get; private set; }

    /// <summary>
    /// 预热帧之后的滚动整帧墙钟统计。
    /// </summary>
    public PerformanceHudStatistics FrameStatistics { get; private set; }

    /// <summary>
    /// 预热帧之后的滚动 CPU busy 统计。
    /// </summary>
    public PerformanceHudStatistics CpuStatistics { get; private set; }

    /// <summary>
    /// 预热帧之后的滚动 GPU timer query 统计；GPU timer 不可用时样本为 0。
    /// </summary>
    public PerformanceHudStatistics GpuStatistics { get; private set; }

    /// <summary>
    /// 预热帧之后的滚动 present/vsync 等待统计。
    /// </summary>
    public PerformanceHudStatistics WaitStatistics { get; private set; }

    /// <summary>
    /// 预热帧之后的滚动有效帧耗时统计。
    /// </summary>
    public PerformanceHudStatistics EffectiveStatistics { get; private set; }

    /// <summary>
    /// 预热帧之后的滚动随负载变化工作统计。
    /// </summary>
    public PerformanceHudStatistics VariableWorkStatistics { get; private set; }

    /// <summary>
    /// 预热帧之后的滚动每帧固定开销统计。
    /// </summary>
    public PerformanceHudStatistics FixedOverheadStatistics { get; private set; }

    /// <inheritdoc />
    public unsafe void Draw(in EditorContext context)
    {
        PerformanceHudSample sample = CaptureSample(in context);
        bool visible = Visible;
        if (!ImGui.Begin(Title, ref visible))
        {
            Visible = visible;
            ImGui.End();
            return;
        }

        Visible = visible;
        DrawSummary(sample);
        ImGui.Separator();
        DrawCounters(sample);
        ImGui.Separator();
        DrawPhaseTimes(sample);
        ImGui.Separator();
        DrawPlots();
        ImGui.End();
    }

    /// <summary>
    /// 从 Editor 上下文采样并更新滚动历史；同一帧重复调用不会重复写历史。
    /// </summary>
    public PerformanceHudSample CaptureSample(in EditorContext context)
    {
        PerformanceHudSample sample = BuildSample(context.Performance);
        _presentationControl = context.Performance.PresentationControl;
        LastSample = sample;
        if (context.FrameIndex == _lastCapturedFrame)
        {
            return sample;
        }

        _lastCapturedFrame = context.FrameIndex;
        WriteHistory(sample);
        return sample;
    }

    /// <summary>
    /// 纯函数聚合诊断快照，便于验证 HUD 与 plan/02 数据一致。
    /// </summary>
    public static PerformanceHudSample BuildSample(EditorPerformanceSnapshot snapshot)
    {
        ReadOnlySpan<double> phases = snapshot.LastFrame;
        ReadOnlySpan<double> subPhases = snapshot.LastSubFrame;
        EngineCounters? counters = snapshot.Counters;
        double particleMs = Get(phases, FramePhase.ParticleToCell) +
            Get(phases, FramePhase.CellToParticle) +
            Get(subPhases, FrameSubPhase.ParticleStamp);
        double caPassA = Get(subPhases, FrameSubPhase.CaPassA);
        double caPassB = Get(subPhases, FrameSubPhase.CaPassB);
        double caPassC = Get(subPhases, FrameSubPhase.CaPassC);
        double caPassD = Get(subPhases, FrameSubPhase.CaPassD);
        if (caPassA + caPassB + caPassC + caPassD <= 0)
        {
            caPassA = Get(phases, FramePhase.CaSimulation);
        }

        double physicsMs = Get(subPhases, FrameSubPhase.PhysicsStep) +
            Get(subPhases, FrameSubPhase.PhysicsCcl) +
            Get(subPhases, FrameSubPhase.PhysicsErase) +
            Get(subPhases, FrameSubPhase.PhysicsInverseSample) +
            Get(subPhases, FrameSubPhase.StaticCollider) +
            Get(subPhases, FrameSubPhase.CharacterController);
        double shapeRebuildMs = Get(subPhases, FrameSubPhase.ShapeRebuild);
        if (physicsMs <= 0 && shapeRebuildMs <= 0)
        {
            physicsMs = Get(phases, FramePhase.PhysicsSync);
        }

        double renderMs = Get(phases, FramePhase.BuildRenderBuffer) +
            Get(subPhases, FrameSubPhase.RenderBufferBuild) +
            Get(subPhases, FrameSubPhase.Lighting) +
            Get(subPhases, FrameSubPhase.Bloom) +
            Get(subPhases, FrameSubPhase.PostProcess) +
            Get(subPhases, FrameSubPhase.Present);
        double uploadMs = Get(subPhases, FrameSubPhase.GpuUpload);
        double presentWaitMs = Get(subPhases, FrameSubPhase.PresentWait);
        double gpuFrameMs = Get(subPhases, FrameSubPhase.GpuFrame);
        if (gpuFrameMs <= 0 && counters is not null)
        {
            gpuFrameMs = counters.FrameGpuWorkMilliseconds;
        }

        double waitMs = counters?.FrameWaitMilliseconds ?? presentWaitMs;
        if (waitMs <= 0)
        {
            waitMs = presentWaitMs;
        }

        double audioMs = Get(subPhases, FrameSubPhase.AudioDispatch);
        if (audioMs <= 0 && counters is not null)
        {
            audioMs = counters.AudioDispatchMilliseconds;
        }

        double totalMs = snapshot.LastWallMilliseconds;
        if (totalMs <= 0 && counters is not null)
        {
            totalMs = counters.RenderFrameLastMilliseconds;
        }

        double phaseTotalMs = Sum(phases);
        double cpuWorkMs = counters?.FrameCpuWorkMilliseconds ?? 0.0;
        if (cpuWorkMs <= 0)
        {
            cpuWorkMs = Math.Max(0.0, phaseTotalMs - waitMs);
        }

        if (totalMs <= 0)
        {
            totalMs = phaseTotalMs > 0
                ? phaseTotalMs
                : particleMs + caPassA + caPassB + caPassC + caPassD + Get(phases, FramePhase.Temperature) + physicsMs + shapeRebuildMs + renderMs + uploadMs + audioMs + waitMs;
        }

        double effectiveMs = counters?.EffectiveFrameMilliseconds ?? 0.0;
        if (effectiveMs <= 0)
        {
            effectiveMs = Math.Max(0.0, totalMs - waitMs);
        }

        bool gpuTimerAvailable = snapshot.PresentationControl?.GpuFrameTimerAvailable ??
            (counters?.FrameGpuTimerAvailable == true);
        bool vSyncEnabled = snapshot.PresentationControl?.VSyncEnabled ??
            (counters?.VSyncEnabled == true);
        string boundType = DetermineBoundType(cpuWorkMs, gpuFrameMs, waitMs, totalMs, vSyncEnabled, gpuTimerAvailable);
        double renderBufferMs = Get(subPhases, FrameSubPhase.RenderBufferBuild);
        if (renderBufferMs <= 0)
        {
            renderBufferMs = Get(phases, FramePhase.BuildRenderBuffer);
        }

        double variableWorkMs = particleMs +
            caPassA +
            caPassB +
            caPassC +
            caPassD +
            Get(phases, FramePhase.Temperature) +
            physicsMs +
            shapeRebuildMs +
            uploadMs +
            renderBufferMs;
        double fixedOverheadMs = Math.Max(0.0, cpuWorkMs - variableWorkMs - Get(subPhases, FrameSubPhase.Present));

        return new PerformanceHudSample(
            totalMs,
            particleMs,
            caPassA,
            caPassB,
            caPassC,
            caPassD,
            Get(phases, FramePhase.Temperature),
            physicsMs,
            shapeRebuildMs,
            renderMs,
            uploadMs,
            audioMs,
            cpuWorkMs,
            gpuFrameMs,
            gpuTimerAvailable,
            Get(subPhases, FrameSubPhase.Present),
            presentWaitMs,
            waitMs,
            effectiveMs,
            effectiveMs > 0.0001 ? 1000.0 / effectiveMs : (counters?.EffectiveFramesPerSecond ?? 0.0),
            vSyncEnabled,
            boundType,
            counters?.ActiveChunks ?? 0,
            counters?.ActiveCells ?? 0,
            counters?.FreeParticles ?? 0,
            counters?.RigidBodies ?? 0,
            counters?.ResidentChunks ?? 0,
            counters?.ResidentMemoryBytes ?? 0,
            variableWorkMs,
            fixedOverheadMs,
            snapshot.SimHz,
            snapshot.Runtime.TimeScale,
            snapshot.Runtime.DegradationLevel,
            snapshot.Runtime.DegradationName,
            snapshot.Runtime.ConsecutiveOverBudgetFrames);
    }

    private static string DetermineBoundType(
        double cpuMs,
        double gpuMs,
        double waitMs,
        double wallMs,
        bool vSyncEnabled,
        bool gpuTimerAvailable)
    {
        return wallMs > 0 && waitMs >= wallMs * 0.35
            ? vSyncEnabled ? "vsync-bound" : "present-bound"
            : gpuTimerAvailable && gpuMs > cpuMs * 1.20 && gpuMs > 0.25
            ? "GPU-bound"
            : cpuMs > Math.Max(gpuMs, 0.0) * 1.20 && cpuMs > 0.25
                ? "CPU-bound"
                : "balanced";
    }

    private static double Get(ReadOnlySpan<double> values, FramePhase phase)
    {
        int index = (int)phase;
        return (uint)index < (uint)values.Length ? values[index] : 0.0;
    }

    private static double Get(ReadOnlySpan<double> values, FrameSubPhase phase)
    {
        int index = (int)phase;
        return (uint)index < (uint)values.Length ? values[index] : 0.0;
    }

    private static double Sum(ReadOnlySpan<double> values)
    {
        double total = 0;
        for (int i = 0; i < values.Length; i++)
        {
            total += values[i];
        }

        return total;
    }

    private void WriteHistory(PerformanceHudSample sample)
    {
        int index = _historyOffset;
        _frameHistory[index] = (float)sample.TotalFrameMs;
        _caHistory[index] = (float)sample.CaMs;
        _physicsHistory[index] = (float)(sample.PhysicsMs + sample.ShapeRebuildMs);
        _renderHistory[index] = (float)(sample.RenderMs + sample.UploadMs);
        _audioHistory[index] = (float)sample.AudioMs;
        _cpuHistory[index] = (float)sample.CpuWorkMs;
        _gpuHistory[index] = (float)sample.GpuWorkMs;
        _waitHistory[index] = (float)sample.WaitMs;
        _effectiveHistory[index] = (float)sample.EffectiveFrameMs;
        _variableHistory[index] = (float)sample.VariableWorkMs;
        _fixedHistory[index] = (float)sample.FixedOverheadMs;
        _activeChunksHistory[index] = sample.ActiveChunks;
        _activeCellsKHistory[index] = sample.ActiveCells / 1000.0f;
        _freeParticlesHistory[index] = sample.FreeParticles;
        _rigidBodiesHistory[index] = sample.RigidBodies;
        _historyOffset = (_historyOffset + 1) % HistoryLength;
        _historyCount = Math.Min(_historyCount + 1, HistoryLength);
        _capturedSampleCount++;
        _phaseBars[0] = (float)sample.ParticleMs;
        _phaseBars[1] = (float)sample.CaPassAMs;
        _phaseBars[2] = (float)sample.CaPassBMs;
        _phaseBars[3] = (float)sample.CaPassCMs;
        _phaseBars[4] = (float)sample.CaPassDMs;
        _phaseBars[5] = (float)sample.HeatMs;
        _phaseBars[6] = (float)sample.PhysicsMs;
        _phaseBars[7] = (float)sample.ShapeRebuildMs;
        _phaseBars[8] = (float)sample.RenderMs;
        _phaseBars[9] = (float)sample.UploadMs;
        _phaseBars[10] = (float)sample.AudioMs;
        UpdateStatistics(sample);
    }

    private void DrawSummary(PerformanceHudSample sample)
    {
        ImGui.TextUnformatted($"Frame {sample.TotalFrameMs:F2} ms   Sim {sample.SimHz:F0} Hz   TimeScale {sample.TimeScale:F2}");
        ImGui.TextUnformatted($"Bound {sample.BoundType}: CPU {sample.CpuWorkMs:F2} ms / GPU {(sample.GpuTimerAvailable ? sample.GpuWorkMs.ToString("F2") : "N/A")} ms / wait {sample.WaitMs:F2} ms");
        ImGui.TextUnformatted($"Present submit/wait: {sample.PresentSubmitMs:F2} / {sample.PresentWaitMs:F2} ms   Effective {sample.EffectiveFps:F1} FPS");
        ImGui.TextUnformatted($"Stats {StatisticsStateText()}   samples {FrameStatistics.SampleCount}/{HistoryLength} after warmup {WarmupFrames}   Spike {(FrameStatistics.IsSpike ? "yes" : "no")}");
        ImGui.TextUnformatted($"Frame avg/p50/p95/p99/max: {FrameStatistics.AverageMs:F2} / {FrameStatistics.P50Ms:F2} / {FrameStatistics.P95Ms:F2} / {FrameStatistics.P99Ms:F2} / {FrameStatistics.MaxMs:F2} ms");
        ImGui.TextUnformatted($"CPU avg/p99: {CpuStatistics.AverageMs:F2} / {CpuStatistics.P99Ms:F2} ms   GPU avg/p99: {(sample.GpuTimerAvailable ? GpuStatistics.AverageMs.ToString("F2") : "N/A")} / {(sample.GpuTimerAvailable ? GpuStatistics.P99Ms.ToString("F2") : "N/A")} ms   wait avg/p99: {WaitStatistics.AverageMs:F2} / {WaitStatistics.P99Ms:F2} ms");
        ImGui.TextUnformatted($"Effective avg/p99: {EffectiveStatistics.AverageMs:F2} / {EffectiveStatistics.P99Ms:F2} ms   variable/fixed avg: {VariableWorkStatistics.AverageMs:F2} / {FixedOverheadStatistics.AverageMs:F2} ms");
        bool vSync = sample.VSyncEnabled;
        if (_presentationControl is not null && _presentationControl.CanToggleVSync)
        {
            if (ImGui.Checkbox("VSync", ref vSync))
            {
                _presentationControl.VSyncEnabled = vSync;
            }
        }
        else
        {
            ImGui.TextUnformatted($"VSync: {(vSync ? "on" : "off")}");
        }

        ImGui.TextUnformatted($"Degrade L{sample.DegradationLevel}: {sample.DegradationName}   OverBudget {sample.ConsecutiveOverBudgetFrames}");
    }

    private static void DrawCounters(PerformanceHudSample sample)
    {
        ImGui.TextUnformatted($"Chunks active/resident: {sample.ActiveChunks} / {sample.ResidentChunks}");
        ImGui.TextUnformatted($"Cells active: {sample.ActiveCells}");
        ImGui.TextUnformatted($"Free particles: {sample.FreeParticles}");
        ImGui.TextUnformatted($"Rigid bodies: {sample.RigidBodies}");
        ImGui.TextUnformatted($"Estimated resident memory: {FormatBytes(sample.ResidentMemoryBytes)}");
        ImGui.TextUnformatted($"Workload variable/fixed/wait: {sample.VariableWorkMs:F2} / {sample.FixedOverheadMs:F2} / {sample.WaitMs:F2} ms");
    }

    private static void DrawPhaseTimes(PerformanceHudSample sample)
    {
        ImGui.TextUnformatted($"particle: {sample.ParticleMs:F2} ms");
        ImGui.TextUnformatted($"CA A/B/C/D: {sample.CaPassAMs:F2} / {sample.CaPassBMs:F2} / {sample.CaPassCMs:F2} / {sample.CaPassDMs:F2} ms");
        ImGui.TextUnformatted($"heat: {sample.HeatMs:F2} ms");
        ImGui.TextUnformatted($"physics: {sample.PhysicsMs:F2} ms");
        ImGui.TextUnformatted($"shape rebuild: {sample.ShapeRebuildMs:F2} ms");
        ImGui.TextUnformatted($"render/upload: {sample.RenderMs:F2} / {sample.UploadMs:F2} ms");
        ImGui.TextUnformatted($"audio: {sample.AudioMs:F2} ms");
    }

    private unsafe void DrawPlots()
    {
        if (_historyCount == 0)
        {
            return;
        }

        if (ImPlot.BeginPlot("滚动耗时曲线", new Vector2(-1, 150), ImPlotFlags.NoMouseText))
        {
            ImPlot.SetupAxes("frame", "ms", ImPlotAxisFlags.NoTickLabels, ImPlotAxisFlags.AutoFit);
            fixed (float* frame = _frameHistory)
            fixed (float* ca = _caHistory)
            fixed (float* physics = _physicsHistory)
            fixed (float* render = _renderHistory)
            fixed (float* audio = _audioHistory)
            fixed (float* cpu = _cpuHistory)
            fixed (float* wait = _waitHistory)
            fixed (float* effective = _effectiveHistory)
            {
                ImPlot.PlotLine("frame", frame, _historyCount, 1.0, 0.0, ImPlotLineFlags.None, _historyOffset);
                ImPlot.PlotLine("CPU", cpu, _historyCount, 1.0, 0.0, ImPlotLineFlags.None, _historyOffset);
                ImPlot.PlotLine("wait", wait, _historyCount, 1.0, 0.0, ImPlotLineFlags.None, _historyOffset);
                ImPlot.PlotLine("effective", effective, _historyCount, 1.0, 0.0, ImPlotLineFlags.None, _historyOffset);
                ImPlot.PlotLine("CA", ca, _historyCount, 1.0, 0.0, ImPlotLineFlags.None, _historyOffset);
                ImPlot.PlotLine("physics", physics, _historyCount, 1.0, 0.0, ImPlotLineFlags.None, _historyOffset);
                ImPlot.PlotLine("render", render, _historyCount, 1.0, 0.0, ImPlotLineFlags.None, _historyOffset);
                ImPlot.PlotLine("audio", audio, _historyCount, 1.0, 0.0, ImPlotLineFlags.None, _historyOffset);
            }

            ImPlot.EndPlot();
        }

        if (ImPlot.BeginPlot("负载计数趋势", new Vector2(-1, 120), ImPlotFlags.NoMouseText))
        {
            ImPlot.SetupAxes("frame", "count", ImPlotAxisFlags.NoTickLabels, ImPlotAxisFlags.AutoFit);
            fixed (float* chunks = _activeChunksHistory)
            fixed (float* cellsK = _activeCellsKHistory)
            fixed (float* particles = _freeParticlesHistory)
            fixed (float* bodies = _rigidBodiesHistory)
            {
                ImPlot.PlotLine("active chunks", chunks, _historyCount, 1.0, 0.0, ImPlotLineFlags.None, _historyOffset);
                ImPlot.PlotLine("active cells K", cellsK, _historyCount, 1.0, 0.0, ImPlotLineFlags.None, _historyOffset);
                ImPlot.PlotLine("particles", particles, _historyCount, 1.0, 0.0, ImPlotLineFlags.None, _historyOffset);
                ImPlot.PlotLine("rigid bodies", bodies, _historyCount, 1.0, 0.0, ImPlotLineFlags.None, _historyOffset);
            }

            ImPlot.EndPlot();
        }

        if (ImPlot.BeginPlot("固定/负载成本结构", new Vector2(-1, 120), ImPlotFlags.NoMouseText))
        {
            ImPlot.SetupAxes("frame", "ms", ImPlotAxisFlags.NoTickLabels, ImPlotAxisFlags.AutoFit);
            fixed (float* variable = _variableHistory)
            fixed (float* fixedCost = _fixedHistory)
            fixed (float* wait = _waitHistory)
            {
                ImPlot.PlotLine("variable", variable, _historyCount, 1.0, 0.0, ImPlotLineFlags.None, _historyOffset);
                ImPlot.PlotLine("fixed", fixedCost, _historyCount, 1.0, 0.0, ImPlotLineFlags.None, _historyOffset);
                ImPlot.PlotLine("wait", wait, _historyCount, 1.0, 0.0, ImPlotLineFlags.None, _historyOffset);
            }

            ImPlot.EndPlot();
        }

        if (ImPlot.BeginPlot("相位堆叠条", new Vector2(-1, 120), ImPlotFlags.NoMouseText))
        {
            ImPlot.SetupAxes("phase", "ms", ImPlotAxisFlags.NoTickLabels, ImPlotAxisFlags.AutoFit);
            fixed (float* bars = _phaseBars)
            {
                ImPlot.PlotBarGroups(PhaseBarLabels, bars, PhaseBarCount, 1, 0.6, ImPlotBarGroupsFlags.Stacked);
            }

            ImPlot.EndPlot();
        }
    }

    private void UpdateStatistics(PerformanceHudSample sample)
    {
        FrameStatistics = CalculateStatistics(_frameHistory, sample.TotalFrameMs);
        CpuStatistics = CalculateStatistics(_cpuHistory, sample.CpuWorkMs);
        GpuStatistics = sample.GpuTimerAvailable ? CalculateStatistics(_gpuHistory, sample.GpuWorkMs) : PerformanceHudStatistics.Empty;
        WaitStatistics = CalculateStatistics(_waitHistory, sample.WaitMs);
        EffectiveStatistics = CalculateStatistics(_effectiveHistory, sample.EffectiveFrameMs);
        VariableWorkStatistics = CalculateStatistics(_variableHistory, sample.VariableWorkMs);
        FixedOverheadStatistics = CalculateStatistics(_fixedHistory, sample.FixedOverheadMs);
    }

    private string StatisticsStateText()
    {
        return _capturedSampleCount <= WarmupFrames
            ? "warmup"
            : FrameStatistics.IsSteady ? "steady" : "collecting";
    }

    private PerformanceHudStatistics CalculateStatistics(float[] history, double latestMs)
    {
        int warmedSamples = _capturedSampleCount - WarmupFrames;
        if (warmedSamples <= 0)
        {
            return PerformanceHudStatistics.Empty;
        }

        int sampleCount = Math.Min(_historyCount, warmedSamples);
        double total = 0;
        double max = 0;
        int start = (_historyOffset - sampleCount + HistoryLength) % HistoryLength;
        for (int i = 0; i < sampleCount; i++)
        {
            int sourceIndex = (start + i) % HistoryLength;
            float value = history[sourceIndex];
            _statsScratch[i] = value;
            total += value;
            if (value > max)
            {
                max = value;
            }
        }

        Array.Sort(_statsScratch, 0, sampleCount);
        double average = total / sampleCount;
        double p50 = Percentile(_statsScratch, sampleCount, 0.50);
        double p95 = Percentile(_statsScratch, sampleCount, 0.95);
        double p99 = Percentile(_statsScratch, sampleCount, 0.99);
        bool isSteady = sampleCount >= SteadyMinimumFrames;
        bool isSpike = sampleCount >= 32 && latestMs >= p95 * 1.25 && latestMs >= p50 + 2.0;
        return new PerformanceHudStatistics(sampleCount, average, p50, p95, p99, max, isSteady, isSpike);
    }

    private static double Percentile(float[] sorted, int count, double percentile)
    {
        int index = Math.Clamp((int)Math.Ceiling(count * percentile) - 1, 0, count - 1);
        return sorted[index];
    }

    private static string FormatBytes(long bytes)
    {
        const double KiB = 1024.0;
        const double MiB = KiB * 1024.0;
        const double GiB = MiB * 1024.0;
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / KiB:F1} KiB",
            < 1024 * 1024 * 1024 => $"{bytes / MiB:F1} MiB",
            _ => $"{bytes / GiB:F1} GiB",
        };
    }
}
