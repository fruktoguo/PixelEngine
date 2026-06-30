namespace PixelEngine.Core.Time;

/// <summary>
/// 提供固定逻辑步长与时间膨胀帧时钟。
/// </summary>
public sealed class FrameClock
{
    private double _simHz;
    private int _simStride;

    /// <summary>
    /// 创建帧时钟。
    /// </summary>
    /// <param name="simHz">sim 频率，默认 60Hz。</param>
    public FrameClock(double simHz = EngineConstants.DefaultSimHz)
    {
        SetSimHz(simHz);
        TimeScale = 1.0;
    }

    /// <summary>
    /// 获取固定逻辑步长，始终等于 <c>1 / SimHz</c>。
    /// </summary>
    public double Dt => 1.0 / _simHz;

    /// <summary>
    /// 获取或设置 sim 频率。当前支持 60Hz 与 30Hz。
    /// </summary>
    public double SimHz
    {
        get => _simHz;
        set => SetSimHz(value);
    }

    /// <summary>
    /// 获取已开始的渲染帧数量。
    /// </summary>
    public long FrameIndex { get; private set; }

    /// <summary>
    /// 获取已执行的 sim tick 数量。
    /// </summary>
    public long SimTickIndex { get; private set; }

    /// <summary>
    /// 获取当前渲染帧是否执行 sim。
    /// </summary>
    public bool RunSimThisFrame { get; private set; }

    /// <summary>
    /// 获取时间膨胀系数；真实帧超过预算时小于 1。
    /// </summary>
    public double TimeScale { get; private set; }

    /// <summary>
    /// 开始一个渲染帧并返回本帧时序决策。
    /// </summary>
    /// <param name="realDeltaSeconds">上一帧真实墙钟耗时。</param>
    /// <returns>本帧固定步长时序决策。</returns>
    public FrameTiming BeginFrame(double realDeltaSeconds)
    {
        ValidateRealDeltaSeconds(realDeltaSeconds);
        FrameIndex++;
        RunSimThisFrame = ShouldRunSim(FrameIndex);

        if (RunSimThisFrame)
        {
            SimTickIndex++;
        }

        // 架构 §4.1 与不变式 #6：这里绝不维护 fixed-step accumulator，
        // 也绝不在单个渲染帧内 while 追多个 sim/physics step。过载时只降低 TimeScale，
        // CA/physics/particle 在同一个被执行 tick 中共享固定 Dt，避免 death spiral 与耦合错位。
        UpdateTimeScale(realDeltaSeconds);

        return new FrameTiming(
            Dt,
            RunSimThisFrame,
            RunSimThisFrame,
            FrameIndex,
            SimTickIndex);
    }

    /// <summary>
    /// 开始一个只渲染、不执行 sim/physics 的帧，用于 Editor 暂停态。
    /// </summary>
    /// <param name="realDeltaSeconds">上一帧真实墙钟耗时。</param>
    /// <returns>本帧渲染时序决策。</returns>
    public FrameTiming BeginRenderOnlyFrame(double realDeltaSeconds)
    {
        ValidateRealDeltaSeconds(realDeltaSeconds);
        FrameIndex++;
        RunSimThisFrame = false;
        UpdateTimeScale(realDeltaSeconds);
        return new FrameTiming(
            Dt,
            false,
            false,
            FrameIndex,
            SimTickIndex);
    }

    /// <summary>
    /// 开始一个强制执行单次 sim/physics 的帧，用于 Editor 单步调试。
    /// </summary>
    /// <param name="realDeltaSeconds">上一帧真实墙钟耗时。</param>
    /// <returns>本帧固定步长时序决策。</returns>
    public FrameTiming BeginForcedSimFrame(double realDeltaSeconds)
    {
        ValidateRealDeltaSeconds(realDeltaSeconds);
        FrameIndex++;
        RunSimThisFrame = true;
        SimTickIndex++;
        UpdateTimeScale(realDeltaSeconds);
        return new FrameTiming(
            Dt,
            true,
            true,
            FrameIndex,
            SimTickIndex);
    }

    private bool ShouldRunSim(long frameIndex)
    {
        return _simStride <= 1 || (frameIndex & (_simStride - 1)) == 1;
    }

    private static void ValidateRealDeltaSeconds(double realDeltaSeconds)
    {
        if (!double.IsFinite(realDeltaSeconds) || realDeltaSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(realDeltaSeconds), realDeltaSeconds, "真实帧耗时必须是非负有限数。");
        }
    }

    private void UpdateTimeScale(double realDeltaSeconds)
    {
        double frameBudget = 1.0 / EngineConstants.DefaultSimHz;
        TimeScale = realDeltaSeconds <= frameBudget || realDeltaSeconds == 0
            ? 1.0
            : frameBudget / realDeltaSeconds;
    }

    private void SetSimHz(double simHz)
    {
        if (!double.IsFinite(simHz) || simHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(simHz), simHz, "sim 频率必须是正有限数。");
        }

        if (Math.Abs(simHz - EngineConstants.DefaultSimHz) < double.Epsilon)
        {
            _simHz = EngineConstants.DefaultSimHz;
            _simStride = 1;
            return;
        }

        if (Math.Abs(simHz - EngineConstants.SimHzDownscaled) < double.Epsilon)
        {
            _simHz = EngineConstants.SimHzDownscaled;
            _simStride = 2;
            return;
        }

        throw new ArgumentOutOfRangeException(nameof(simHz), simHz, "当前 FrameClock 只支持 60Hz 与 30Hz。");
    }
}
