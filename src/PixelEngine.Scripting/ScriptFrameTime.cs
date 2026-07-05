using PixelEngine.Core.Time;

namespace PixelEngine.Scripting;

/// <summary>
/// 将 Hosting/Core 的固定步长帧时钟暴露为脚本可读时间 facade。
/// </summary>
/// <param name="clock">引擎帧时钟。</param>
public sealed class ScriptFrameTime(FrameClock clock) : IGameTime
{
    private readonly FrameClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <inheritdoc />
    public float DeltaTime => (float)(_clock.Dt * _clock.TimeScale);

    /// <inheritdoc />
    public float RealDeltaTime => PublishedRealDeltaTime > 0f ? PublishedRealDeltaTime : DeltaTime;

    private float PublishedRealDeltaTime { get; set; }

    /// <inheritdoc />
    public float FixedStep => (float)_clock.Dt;

    /// <inheritdoc />
    public long FrameCount => _clock.FrameIndex;

    /// <inheritdoc />
    public float TimeScale => (float)_clock.TimeScale;

    /// <inheritdoc />
    public bool SimSteppedThisFrame => _clock.RunSimThisFrame;

    /// <summary>
    /// 发布 Hosting 采样到的真实渲染帧间隔；用于纯视觉脚本效果按墙钟衰减。
    /// </summary>
    /// <param name="realDeltaSeconds">真实渲染帧间隔，单位秒。</param>
    public void SetRealDeltaTime(double realDeltaSeconds)
    {
        PublishedRealDeltaTime = double.IsFinite(realDeltaSeconds) && realDeltaSeconds > 0
            ? (float)Math.Min(realDeltaSeconds, 1.0)
            : 0f;
    }
}
