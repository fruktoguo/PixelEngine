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
    private const int HistoryLength = 240;

    private readonly float[] _frameHistory = new float[HistoryLength];
    private readonly float[] _caHistory = new float[HistoryLength];
    private readonly float[] _physicsHistory = new float[HistoryLength];
    private readonly float[] _renderHistory = new float[HistoryLength];
    private readonly float[] _audioHistory = new float[HistoryLength];
    private readonly float[] _phaseBars = new float[6];
    private long _lastCapturedFrame = -1;
    private int _historyOffset;
    private int _historyCount;

    /// <inheritdoc />
    public string Title => EditorDockSpace.PerformanceHudWindowTitle;

    /// <inheritdoc />
    public bool Visible { get; set; } = true;

    /// <summary>
    /// 最近一次采样结果，供测试和诊断读取。
    /// </summary>
    public PerformanceHudSample LastSample { get; private set; }

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
        double caSubMs = Get(subPhases, FrameSubPhase.CaPassA) +
            Get(subPhases, FrameSubPhase.CaPassB) +
            Get(subPhases, FrameSubPhase.CaPassC) +
            Get(subPhases, FrameSubPhase.CaPassD);
        double caMs = caSubMs > 0 ? caSubMs : Get(phases, FramePhase.CaSimulation);
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
            Get(subPhases, FrameSubPhase.GpuLightComposite) +
            Get(subPhases, FrameSubPhase.Bloom) +
            Get(subPhases, FrameSubPhase.GpuParticleDraw) +
            Get(subPhases, FrameSubPhase.GpuComputeBloom) +
            Get(subPhases, FrameSubPhase.GpuRadianceCascades) +
            Get(subPhases, FrameSubPhase.GpuAirSmoke) +
            Get(subPhases, FrameSubPhase.PostProcess) +
            Get(subPhases, FrameSubPhase.Present);
        double uploadMs = Get(subPhases, FrameSubPhase.GpuUpload);
        double audioMs = Get(subPhases, FrameSubPhase.AudioDispatch);
        if (audioMs <= 0 && counters is not null)
        {
            audioMs = counters.AudioDispatchMilliseconds;
        }

        double totalMs = Sum(phases);
        if (totalMs <= 0)
        {
            totalMs = particleMs + caMs + Get(phases, FramePhase.Temperature) + physicsMs + shapeRebuildMs + renderMs + uploadMs + audioMs;
        }

        return new PerformanceHudSample(
            totalMs,
            particleMs,
            caMs,
            Get(phases, FramePhase.Temperature),
            physicsMs,
            shapeRebuildMs,
            renderMs,
            uploadMs,
            audioMs,
            counters?.ActiveChunks ?? 0,
            counters?.ActiveCells ?? 0,
            counters?.FreeParticles ?? 0,
            counters?.RigidBodies ?? 0,
            counters?.ResidentChunks ?? 0,
            counters?.ResidentMemoryBytes ?? 0,
            snapshot.SimHz,
            snapshot.Runtime.TimeScale,
            snapshot.Runtime.DegradationLevel,
            snapshot.Runtime.DegradationName,
            snapshot.Runtime.ConsecutiveOverBudgetFrames);
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
        _historyOffset = (_historyOffset + 1) % HistoryLength;
        _historyCount = Math.Min(_historyCount + 1, HistoryLength);
        _phaseBars[0] = (float)sample.ParticleMs;
        _phaseBars[1] = (float)sample.CaMs;
        _phaseBars[2] = (float)sample.HeatMs;
        _phaseBars[3] = (float)(sample.PhysicsMs + sample.ShapeRebuildMs);
        _phaseBars[4] = (float)(sample.RenderMs + sample.UploadMs);
        _phaseBars[5] = (float)sample.AudioMs;
    }

    private static void DrawSummary(PerformanceHudSample sample)
    {
        ImGui.TextUnformatted($"Frame {sample.TotalFrameMs:F2} ms   Sim {sample.SimHz:F0} Hz   TimeScale {sample.TimeScale:F2}");
        ImGui.TextUnformatted($"Degrade L{sample.DegradationLevel}: {sample.DegradationName}   OverBudget {sample.ConsecutiveOverBudgetFrames}");
    }

    private static void DrawCounters(PerformanceHudSample sample)
    {
        ImGui.TextUnformatted($"Chunks active/resident: {sample.ActiveChunks} / {sample.ResidentChunks}");
        ImGui.TextUnformatted($"Cells active: {sample.ActiveCells}");
        ImGui.TextUnformatted($"Free particles: {sample.FreeParticles}");
        ImGui.TextUnformatted($"Rigid bodies: {sample.RigidBodies}");
        ImGui.TextUnformatted($"Estimated resident memory: {FormatBytes(sample.ResidentMemoryBytes)}");
    }

    private static void DrawPhaseTimes(PerformanceHudSample sample)
    {
        ImGui.TextUnformatted($"particle: {sample.ParticleMs:F2} ms");
        ImGui.TextUnformatted($"CA A-D: {sample.CaMs:F2} ms");
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
            {
                ImPlot.PlotLine("frame", frame, _historyCount, 1.0, 0.0, ImPlotLineFlags.None, _historyOffset);
                ImPlot.PlotLine("CA", ca, _historyCount, 1.0, 0.0, ImPlotLineFlags.None, _historyOffset);
                ImPlot.PlotLine("physics", physics, _historyCount, 1.0, 0.0, ImPlotLineFlags.None, _historyOffset);
                ImPlot.PlotLine("render", render, _historyCount, 1.0, 0.0, ImPlotLineFlags.None, _historyOffset);
                ImPlot.PlotLine("audio", audio, _historyCount, 1.0, 0.0, ImPlotLineFlags.None, _historyOffset);
            }

            ImPlot.EndPlot();
        }

        if (ImPlot.BeginPlot("相位堆叠条", new Vector2(-1, 120), ImPlotFlags.NoMouseText))
        {
            ImPlot.SetupAxes("phase", "ms", ImPlotAxisFlags.NoTickLabels, ImPlotAxisFlags.AutoFit);
            fixed (float* bars = _phaseBars)
            {
                ImPlot.PlotBars("particle/CA/heat/physics/render/audio", bars, _phaseBars.Length, 0.6);
            }

            ImPlot.EndPlot();
        }
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
