using System.Diagnostics;

namespace PixelEngine.Core.Diagnostics;

/// <summary>
/// 提供按帧相位记录的零分配计时器。
/// </summary>
public sealed class FrameProfiler
{
    private const int HistoryLength = 256;

    private readonly double[] _current = new double[FrameStats.PhaseCount];
    private readonly double[] _last = new double[FrameStats.PhaseCount];
    private readonly double[] _subCurrent = new double[FrameStats.SubPhaseCount];
    private readonly double[] _history = new double[FrameStats.PhaseCount * HistoryLength];
    private int _historyIndex;
    private int _historyCount;

    /// <summary>
    /// 获取上一帧各主相位耗时，单位毫秒。
    /// </summary>
    public ReadOnlySpan<double> LastFrame => _last;

    /// <summary>
    /// 开始记录新一帧。
    /// </summary>
    public void BeginFrame()
    {
        Array.Clear(_current);
        Array.Clear(_subCurrent);
    }

    /// <summary>
    /// 结束当前帧并写入历史环。
    /// </summary>
    public void EndFrame()
    {
        _current.CopyTo(_last, 0);
        int offset = _historyIndex * FrameStats.PhaseCount;
        _current.CopyTo(_history, offset);
        _historyIndex = (_historyIndex + 1) & (HistoryLength - 1);
        _historyCount = Math.Min(_historyCount + 1, HistoryLength);
    }

    /// <summary>
    /// 开始测量指定主相位。
    /// </summary>
    /// <param name="phase">主相位。</param>
    /// <returns>using-scope 计时器。</returns>
    public ProfilerScope Measure(FramePhase phase)
    {
        return new ProfilerScope(this, phase, Stopwatch.GetTimestamp());
    }

    /// <summary>
    /// 记录主相位耗时。
    /// </summary>
    /// <param name="phase">主相位。</param>
    /// <param name="ms">耗时毫秒。</param>
    public void Record(FramePhase phase, double ms)
    {
        ValidateMilliseconds(ms);
        _current[PhaseIndex(phase)] += ms;
    }

    /// <summary>
    /// 记录细分相位耗时。
    /// </summary>
    /// <param name="phase">细分相位。</param>
    /// <param name="ms">耗时毫秒。</param>
    public void RecordSub(FrameSubPhase phase, double ms)
    {
        ValidateMilliseconds(ms);
        _subCurrent[SubPhaseIndex(phase)] += ms;
    }

    /// <summary>
    /// 计算指定主相位近 N 帧平均耗时。
    /// </summary>
    /// <param name="phase">主相位。</param>
    /// <param name="window">窗口帧数。</param>
    /// <returns>平均毫秒。</returns>
    public double Average(FramePhase phase, int window)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(window);
        int count = Math.Min(window, _historyCount);
        if (count == 0)
        {
            return 0;
        }

        int phaseIndex = PhaseIndex(phase);
        double total = 0;
        for (int i = 0; i < count; i++)
        {
            int historySlot = (_historyIndex - 1 - i) & (HistoryLength - 1);
            total += _history[(historySlot * FrameStats.PhaseCount) + phaseIndex];
        }

        return total / count;
    }

    private static int PhaseIndex(FramePhase phase)
    {
        int index = (int)phase;
        return (uint)index >= FrameStats.PhaseCount ? throw new ArgumentOutOfRangeException(nameof(phase), phase, "未知帧主相位。") : index;
    }

    private static int SubPhaseIndex(FrameSubPhase phase)
    {
        int index = (int)phase;
        return (uint)index >= FrameStats.SubPhaseCount ? throw new ArgumentOutOfRangeException(nameof(phase), phase, "未知帧细分相位。") : index;
    }

    private static void ValidateMilliseconds(double ms)
    {
        _ = double.IsFinite(ms) && ms >= 0
            ? true
            : throw new ArgumentOutOfRangeException(nameof(ms), ms, "耗时必须是非负有限毫秒数。");
    }

    /// <summary>
    /// 表示一个零分配 using-scope 计时器。
    /// </summary>
    public readonly struct ProfilerScope : IDisposable
    {
        private readonly FrameProfiler? _owner;
        private readonly FramePhase _phase;
        private readonly long _startTimestamp;

        internal ProfilerScope(FrameProfiler owner, FramePhase phase, long startTimestamp)
        {
            _owner = owner;
            _phase = phase;
            _startTimestamp = startTimestamp;
        }

        /// <summary>
        /// 结束计时并记录到所属 profiler。
        /// </summary>
        public void Dispose()
        {
            if (_owner is null)
            {
                return;
            }

            long elapsed = Stopwatch.GetTimestamp() - _startTimestamp;
            double ms = elapsed * 1000.0 / Stopwatch.Frequency;
            _owner.Record(_phase, ms);
        }
    }
}
